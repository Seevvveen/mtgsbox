#nullable enable

using Sandbox.Classes.Database.Types;
using System;
using System.Text.RegularExpressions;
using RuntimeCardDatabase = Sandbox.Classes.Database.CardDatabase;
namespace Sandbox.Classes.Deck.Import;

/// <summary>
///     Imports common plain-text deck lists. Supported identities include UUID,
///     (SET) collector, [SET:collector], name plus set, and exact card name.
/// </summary>
public sealed class PlainTextDeckParser
{
	private static readonly Regex QuantityPattern     = new Regex( @"^(?<quantity>\d+)[xX]?\s+(?<card>.+)$" );
	private static readonly Regex UuidPattern         = new Regex( @"(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-"     + @"[0-9a-fA-F]{4}-[0-9a-fA-F]{12})" );
	private static readonly Regex SetCollectorPattern = new Regex( @"^(?<name>.+?)\s+\((?<set>[\w_]+)\)\s+"                   + @"(?<collector>\S+)(?:\s+.*)?$" );
	private static readonly Regex BracketPattern      = new Regex( @"^(?<name>.+?)\s+\[(?<set>[\w_]+):(?<collector>[^\]]+)\]" + @"(?:\s+.*)?$" );
	private static readonly Regex NameSetPattern      = new Regex( @"^(?<name>.+?)&set=(?<set>[\w_]+)$", RegexOptions.IgnoreCase );
	private static readonly Regex SetOnlyPattern      = new Regex( @"^(?<name>.+?)\s+\((?<set>[\w_]+)\)(?:\s+\*[^*]+\*)?$" );


	public DeckImportResult Import( string text, DeckImportOptions? options = null )
	{
		ArgumentNullException.ThrowIfNull( text );
		options ??= new DeckImportOptions();

		Deck deck = new Deck { Name = options.DeckName, FormatCode = options.FormatCode, Source = options.Source ?? new DeckSource { Kind = "text" } };

		List<DeckImportIssue> issues  = new List<DeckImportIssue>();
		string                section = DeckSections.Main;
		string[]              lines   = text.Replace( "\r\n", "\n" ).Split( '\n' );

		for ( int index = 0; index < lines.Length; index++ )
		{
			string raw  = lines[index];
			string line = raw.Trim().TrimStart( '\uFEFF' );

			if ( line.Length == 0 || line.StartsWith( "//", StringComparison.Ordinal ) || line.StartsWith( "#", StringComparison.Ordinal ) )
				continue;

			if ( TryReadSection( line, out string? nextSection ) )
			{
				section = nextSection;
				continue;
			}

			if ( line.StartsWith( "SB:", StringComparison.OrdinalIgnoreCase ) )
			{
				section = DeckSections.Sideboard;
				line    = line[3..].Trim();
			}

			ParseLine( line, raw, index + 1, section, deck, issues );
		}

		return new DeckImportResult { Deck = deck, Issues = issues };
	}


	private void ParseLine( string line, string raw, int lineNumber, string section, Deck deck, List<DeckImportIssue> issues )
	{
		int    quantity      = 1;
		string cardText      = line;
		Match  quantityMatch = QuantityPattern.Match( line );

		if ( quantityMatch.Success )
		{
			if ( !int.TryParse( quantityMatch.Groups["quantity"].Value, out quantity ) || quantity <= 0 )
			{
				issues.Add( CreateLineIssue( DeckImportIssueCode.InvalidQuantity, lineNumber, raw, $"Invalid card quantity on line {lineNumber}." ) );
				return;
			}

			cardText = quantityMatch.Groups["card"].Value.Trim();
		}

		DeckCardQuery      query      = ParseCardQuery( cardText );
		DeckCardResolution resolution = Resolve( query );

		if ( !resolution.IsResolved )
		{
			issues.Add( CreateLineIssue( DeckImportIssueCode.CardNotFound, lineNumber, raw, $"Card not found: {cardText}" ) );
			return;
		}

		AddOrMerge( deck, section, quantity, resolution.Card! );
	}


	private static DeckCardQuery ParseCardQuery( string cardText )
	{
		string cleaned = cardText.Trim();
		Match  match   = UuidPattern.Match( cleaned );

		if ( match.Success && Guid.TryParse( match.Groups["id"].Value, out Guid id ) )
		{
			string name = cleaned.Replace( match.Groups["id"].Value, "", StringComparison.OrdinalIgnoreCase ).Trim();

			return new DeckCardQuery { ScryfallId = id, Name = name.Length == 0? id.ToString() : name };
		}

		match = SetCollectorPattern.Match( cleaned );

		if ( match.Success )
			return FromMatch( match, true );

		match = BracketPattern.Match( cleaned );

		if ( match.Success )
			return FromMatch( match, true );

		match = NameSetPattern.Match( cleaned );

		if ( match.Success )
			return FromMatch( match, false );

		match = SetOnlyPattern.Match( cleaned );

		if ( match.Success )
			return FromMatch( match, false );

		return new DeckCardQuery { Name = RemoveFinishMarker( cleaned ) };
	}


	private static DeckCardQuery FromMatch( Match match, bool includeCollector )
	{
		return new DeckCardQuery { Name = match.Groups["name"].Value.Trim(), SetCode = match.Groups["set"].Value, CollectorNumber = includeCollector? match.Groups["collector"].Value : null };
	}


	private static DeckCardResolution Resolve( DeckCardQuery query )
	{
		if ( query.ScryfallId is Guid scryfallId && scryfallId != Guid.Empty )
			return FromSingle( RuntimeCardDatabase.GetCard( scryfallId ) );

		if ( !string.IsNullOrWhiteSpace( query.SetCode ) && !string.IsNullOrWhiteSpace( query.CollectorNumber ) )
		{
			NormalizedCard? byPrinting = RuntimeCardDatabase.FindPrinting( NormalizeSetCode( query.SetCode ), query.CollectorNumber );

			if ( byPrinting is not null )
				return FromSingle( byPrinting );
		}

		NormalizedCard[] matches = RuntimeCardDatabase.FindByName( query.Name );

		if ( !string.IsNullOrWhiteSpace( query.SetCode ) )
		{
			string setCode = NormalizeSetCode( query.SetCode );
			matches = matches.Where( card => string.Equals( card.Set.Code, setCode, StringComparison.OrdinalIgnoreCase ) ).ToArray();
		}

		NormalizedCard[] deckConstructibleMatches = matches.Where( card => card.Gameplay.Capabilities.SupportsDeckConstruction ).ToArray();

		if ( deckConstructibleMatches.Length > 0 )
			matches = deckConstructibleMatches;

		return matches.Length == 0? new DeckCardResolution() : new DeckCardResolution { Card = CreateReference( matches[0] ), MatchCount = matches.Length };
	}


	private static DeckCardResolution FromSingle( NormalizedCard? card )
	{
		return new DeckCardResolution { Card = card is null? null : CreateReference( card ), MatchCount = card is null? 0 : 1 };
	}


	private static DeckCardReference CreateReference( NormalizedCard card )
	{
		return new DeckCardReference
			   {
				   ScryfallId      = card.Gameplay.ScryfallId,
				   OracleId        = card.Gameplay.OracleId,
				   Name            = card.Gameplay.Name,
				   SetCode         = card.Set.Code,
				   CollectorNumber = card.Presentation.CollectorNumber
			   };
	}


	private static string NormalizeSetCode( string setCode )
	{
		string trimmed = setCode.Trim();
		int    suffix  = trimmed.IndexOf( '_' );

		return suffix < 0? trimmed : trimmed[..suffix];
	}


	private static string RemoveFinishMarker( string text )
	{
		return Regex.Replace( text, @"\s+\*(?:F|E)\*\s*$", "" );
	}


	private static bool TryReadSection( string line, out string section )
	{
		string header = line.TrimEnd( ':' ).Trim().ToLowerInvariant();

		section = header switch
				  {
					  "deck" or "main" or "mainboard" => DeckSections.Main,
					  "sideboard" or "side board"     => DeckSections.Sideboard,
					  "commander" or "commanders"     => DeckSections.Commander,
					  "companion" or "companions"     => DeckSections.Companion,
					  "maybeboard" or "maybe board"   => DeckSections.Maybeboard,
					  _                               => ""
				  };

		return section.Length != 0;
	}


	private static void AddOrMerge( Deck deck, string section, int quantity, DeckCardReference card )
	{
		int existingIndex = deck.Entries.FindIndex( entry => string.Equals( entry.Section, section, StringComparison.OrdinalIgnoreCase ) && entry.Card.ScryfallId == card.ScryfallId );

		if ( existingIndex < 0 )
		{
			deck.Entries.Add( new DeckEntry { Section = section, Quantity = quantity, Card = card } );

			return;
		}

		DeckEntry existing = deck.Entries[existingIndex];
		deck.Entries[existingIndex] = existing with { Quantity = checked(existing.Quantity + quantity) };
	}


	private static DeckImportIssue CreateLineIssue( DeckImportIssueCode code, int lineNumber, string raw, string message )
	{
		return new DeckImportIssue
			   {
				   Severity   = DeckImportIssueSeverity.Error,
				   Code       = code,
				   LineNumber = lineNumber,
				   RawText    = raw,
				   Message    = message
			   };
	}


	private readonly record struct DeckCardQuery
	{
		public          Guid?   ScryfallId      { get; init; }
		public required string  Name            { get; init; }
		public          string? SetCode         { get; init; }
		public          string? CollectorNumber { get; init; }
	}

	private sealed record DeckCardResolution
	{
		public DeckCardReference? Card       { get; init; }
		public int                MatchCount { get; init; }

		public bool IsResolved
		{
			get { return Card is not null; }
		}

		public bool IsAmbiguous
		{
			get { return MatchCount > 1; }
		}
	}
}
