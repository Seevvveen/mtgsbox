#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Sandbox.Classes.DeckImport;

/// <summary>
/// Imports common MTGO/Moxfield-style text lists. Supported identities match
/// the TTS importer: UUID, (SET) collector, [SET:collector], name plus set,
/// and exact card name.
/// </summary>
public sealed class DeckTextImporter
{
	private static readonly Regex QuantityPattern = new(
		@"^(?<quantity>\d+)[xX]?\s+(?<card>.+)$" );

	private static readonly Regex UuidPattern = new(
		@"(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-" +
		@"[0-9a-fA-F]{4}-[0-9a-fA-F]{12})" );

	private static readonly Regex SetCollectorPattern = new(
		@"^(?<name>.+?)\s+\((?<set>[\w_]+)\)\s+" +
		@"(?<collector>\S+)(?:\s+.*)?$" );

	private static readonly Regex BracketPattern = new(
		@"^(?<name>.+?)\s+\[(?<set>[\w_]+):(?<collector>[^\]]+)\]" +
		@"(?:\s+.*)?$" );

	private static readonly Regex NameSetPattern = new(
		@"^(?<name>.+?)&set=(?<set>[\w_]+)$",
		RegexOptions.IgnoreCase );

	private static readonly Regex SetOnlyPattern = new(
		@"^(?<name>.+?)\s+\((?<set>[\w_]+)\)(?:\s+\*[^*]+\*)?$" );

	private readonly IDeckCardResolver _resolver;

	public DeckTextImporter( IDeckCardResolver resolver )
	{
		_resolver = resolver
			?? throw new ArgumentNullException( nameof(resolver) );
	}

	public DeckImportResult Import(
		string text,
		DeckImportOptions? options = null )
	{
		ArgumentNullException.ThrowIfNull( text );
		options ??= new DeckImportOptions();

		var deck = new Deck
		{
			Name = options.DeckName,
			FormatCode = options.FormatCode,
			Source = options.Source
				?? new DeckSource { Kind = "text" }
		};

		var issues = new List<DeckImportIssue>();
		string section = DeckSections.Main;
		string[] lines = text.Replace( "\r\n", "\n" ).Split( '\n' );

		for ( int index = 0; index < lines.Length; index++ )
		{
			string raw = lines[index];
			string line = raw.Trim().TrimStart( '\uFEFF' );

				if ( line.Length == 0 ||
					line.StartsWith( "//", StringComparison.Ordinal ) ||
					line.StartsWith( "#", StringComparison.Ordinal ) )
			{
				continue;
			}

			if ( TryReadSection( line, out string? nextSection ) )
			{
				section = nextSection;
				continue;
			}

			if ( line.StartsWith( "SB:", StringComparison.OrdinalIgnoreCase ) )
			{
				section = DeckSections.Sideboard;
				line = line[3..].Trim();
			}

			ParseLine(
				line,
				raw,
				index + 1,
				section,
				deck,
				issues );
		}

		return new DeckImportResult
		{
			Deck = deck,
			Issues = issues
		};
	}

	private void ParseLine(
		string line,
		string raw,
		int lineNumber,
		string section,
		Deck deck,
		List<DeckImportIssue> issues )
	{
		int quantity = 1;
		string cardText = line;
		Match quantityMatch = QuantityPattern.Match( line );

		if ( quantityMatch.Success )
		{
			if ( !int.TryParse(
				quantityMatch.Groups["quantity"].Value,
				out quantity ) ||
				quantity <= 0 )
			{
				issues.Add( CreateLineIssue(
					DeckImportIssueCode.InvalidQuantity,
					lineNumber,
					raw,
					$"Invalid card quantity on line {lineNumber}." ) );
				return;
			}

			cardText = quantityMatch.Groups["card"].Value.Trim();
		}

		DeckCardQuery query = ParseCardQuery( cardText );
		DeckCardResolution resolution = _resolver.Resolve( query );

		if ( !resolution.IsResolved )
		{
			issues.Add( CreateLineIssue(
				DeckImportIssueCode.CardNotFound,
				lineNumber,
				raw,
				$"Card not found: {cardText}" ) );
			return;
		}

		if ( resolution.IsAmbiguous )
		{
			issues.Add( new DeckImportIssue
			{
				Severity = DeckImportIssueSeverity.Warning,
				Code = DeckImportIssueCode.AmbiguousCard,
				LineNumber = lineNumber,
				RawText = raw,
				Message =
					$"'{query.Name}' matched {resolution.MatchCount} " +
					"printings; the default printing was selected."
			});
		}

		AddOrMerge(
			deck,
			section,
			quantity,
			resolution.Card! );
	}

	private static DeckCardQuery ParseCardQuery( string cardText )
	{
		string cleaned = cardText.Trim();
		Match match = UuidPattern.Match( cleaned );

		if ( match.Success &&
			Guid.TryParse( match.Groups["id"].Value, out Guid id ) )
		{
			string name = cleaned.Replace(
				match.Groups["id"].Value,
				"",
				StringComparison.OrdinalIgnoreCase ).Trim();

			return new DeckCardQuery
			{
				ScryfallId = id,
				Name = name.Length == 0 ? id.ToString() : name
			};
		}

		match = SetCollectorPattern.Match( cleaned );
		if ( match.Success )
			return FromMatch( match, includeCollector: true );

		match = BracketPattern.Match( cleaned );
		if ( match.Success )
			return FromMatch( match, includeCollector: true );

		match = NameSetPattern.Match( cleaned );
		if ( match.Success )
			return FromMatch( match, includeCollector: false );

		match = SetOnlyPattern.Match( cleaned );
		if ( match.Success )
			return FromMatch( match, includeCollector: false );

		return new DeckCardQuery
		{
			Name = RemoveFinishMarker( cleaned )
		};
	}

	private static DeckCardQuery FromMatch(
		Match match,
		bool includeCollector )
	{
		return new DeckCardQuery
		{
			Name = match.Groups["name"].Value.Trim(),
			SetCode = match.Groups["set"].Value,
			CollectorNumber = includeCollector
				? match.Groups["collector"].Value
				: null
		};
	}

	private static string RemoveFinishMarker( string text )
	{
		return Regex.Replace( text, @"\s+\*(?:F|E)\*\s*$", "" );
	}

	private static bool TryReadSection(
		string line,
		out string section )
	{
		string header = line.TrimEnd( ':' ).Trim().ToLowerInvariant();

		section = header switch
		{
			"deck" or "main" or "mainboard" => DeckSections.Main,
			"sideboard" or "side board" => DeckSections.Sideboard,
			"commander" or "commanders" => DeckSections.Commander,
			"companion" or "companions" => DeckSections.Companion,
			"maybeboard" or "maybe board" => DeckSections.Maybeboard,
			_ => ""
		};

		return section.Length != 0;
	}

	private static void AddOrMerge(
		Deck deck,
		string section,
		int quantity,
		DeckCardReference card )
	{
		int existingIndex = deck.Entries.FindIndex( entry =>
			string.Equals(
				entry.Section,
				section,
				StringComparison.OrdinalIgnoreCase ) &&
			entry.Card.ScryfallId == card.ScryfallId );

		if ( existingIndex < 0 )
		{
			deck.Entries.Add( new DeckEntry
			{
				Section = section,
				Quantity = quantity,
				Card = card
			});
			return;
		}

		DeckEntry existing = deck.Entries[existingIndex];
		deck.Entries[existingIndex] = existing with
		{
			Quantity = checked(existing.Quantity + quantity)
		};
	}

	private static DeckImportIssue CreateLineIssue(
		DeckImportIssueCode code,
		int lineNumber,
		string raw,
		string message )
	{
		return new DeckImportIssue
		{
			Severity = DeckImportIssueSeverity.Error,
			Code = code,
			LineNumber = lineNumber,
			RawText = raw,
			Message = message
		};
	}
}
