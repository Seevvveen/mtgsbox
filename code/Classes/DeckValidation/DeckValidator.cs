#nullable enable

using Sandbox.Classes.Cards.Legality;
using Sandbox.Classes.Database.Types;
using System;
using System.Collections.Generic;
using RuntimeCardDatabase = Sandbox.Classes.Database.CardDatabase;

namespace Sandbox.Classes.DeckValidation;

/// <summary>
/// Server-safe deck validation. Every card is resolved from the authoritative
/// local database; legality asserted by an imported or shared file is ignored.
/// </summary>
public static class DeckValidator
{
	public static DeckValidationReport Validate(
		Deck deck,
		DeckFormatDefinition format,
		IEnumerable<IDeckFormatRule>? additionalRules = null )
	{
		ArgumentNullException.ThrowIfNull( deck );
		ArgumentNullException.ThrowIfNull( format );

		var report = new DeckValidationReport
		{
			FormatCode = format.Code
		};
		var sectionCounts = new Dictionary<string, int>(
			StringComparer.OrdinalIgnoreCase );
		var copiesByOracleId = new Dictionary<Guid, int>();

		foreach ( DeckEntry entry in deck.Entries )
		{
			if ( entry.Quantity <= 0 )
			{
				Add(
					report,
					DeckValidationIssueCode.InvalidQuantity,
					$"'{entry.Card.Name}' has invalid quantity " +
					$"{entry.Quantity}.",
					entry );
				continue;
			}

			if ( !format.Sections.TryGetValue(
				entry.Section,
				out DeckSectionRule? sectionRule ) )
			{
				Add(
					report,
					DeckValidationIssueCode.UnknownSection,
					$"Section '{entry.Section}' is not allowed in " +
					$"{format.DisplayName}.",
					entry );
				continue;
			}

			sectionCounts.TryGetValue(
				entry.Section,
				out int currentSectionCount );
			sectionCounts[entry.Section] =
				checked(currentSectionCount + entry.Quantity);

			NormalizedCard? card =
				RuntimeCardDatabase.GetCard( entry.Card.ScryfallId );

			if ( card is null )
			{
				Add(
					report,
					DeckValidationIssueCode.CardNotFound,
					$"Printing for '{entry.Card.Name}' is not in the " +
					"server card database.",
					entry );
				continue;
			}

			if ( entry.Card.OracleId is Guid storedOracleId &&
				card.Gameplay.OracleId != storedOracleId )
			{
				Add(
					report,
					DeckValidationIssueCode.PrintingIdentityMismatch,
					$"Stored Oracle identity for '{entry.Card.Name}' " +
					"does not match its printing.",
					entry );
				continue;
			}

			if ( card.Gameplay.OracleId is not Guid oracleId )
			{
				Add(
					report,
					DeckValidationIssueCode.MissingOracleIdentity,
					$"'{entry.Card.Name}' has no Oracle identity.",
					entry );
				continue;
			}

			ValidateCardLegality(
				card,
				oracleId,
				entry,
				format,
				report );

			if ( sectionRule.CountsTowardCopyLimit )
			{
				copiesByOracleId.TryGetValue(
					oracleId,
					out int currentCopies );
				copiesByOracleId[oracleId] =
					checked(currentCopies + entry.Quantity);
			}
		}

		ValidateSectionCounts( format, sectionCounts, report );
		ValidateCopyCounts( deck, format, copiesByOracleId, report );

		if ( additionalRules is not null )
		{
			foreach ( IDeckFormatRule rule in additionalRules )
				rule.Validate( deck, format, report );
		}

		return report;
	}

	private static void ValidateCardLegality(
		NormalizedCard card,
		Guid oracleId,
		DeckEntry entry,
		DeckFormatDefinition format,
		DeckValidationReport report )
	{
		if ( format.BannedOracleIds.Contains( oracleId ) )
		{
			Add(
				report,
				DeckValidationIssueCode.CardBanned,
				$"'{card.Gameplay.Name}' is banned in " +
				$"{format.DisplayName}.",
				entry );
			return;
		}

		string legalityCode = format.CardLegalityCode ?? format.Code;
		CardLegality? legality =
			card.Gameplay.Legalities.Get( legalityCode );

		switch ( legality )
		{
			case CardLegality.Banned:
				Add(
					report,
					DeckValidationIssueCode.CardBanned,
					$"'{card.Gameplay.Name}' is banned in " +
					$"{format.DisplayName}.",
					entry );
				break;

			case null:
			case CardLegality.NotLegal:
				Add(
					report,
					DeckValidationIssueCode.CardNotLegal,
					$"'{card.Gameplay.Name}' is not legal in " +
					$"{format.DisplayName}.",
					entry );
				break;
		}
	}

	private static void ValidateSectionCounts(
		DeckFormatDefinition format,
		Dictionary<string, int> counts,
		DeckValidationReport report )
	{
		foreach ( KeyValuePair<string, DeckSectionRule> pair
			in format.Sections )
		{
			counts.TryGetValue( pair.Key, out int count );

			if ( count < pair.Value.MinimumCards )
			{
				report.Issues.Add( new DeckValidationIssue
				{
					Code = DeckValidationIssueCode.SectionTooSmall,
					Section = pair.Key,
					Message =
						$"Section '{pair.Key}' contains {count} cards; " +
						$"{pair.Value.MinimumCards} are required."
				});
			}

			if ( pair.Value.MaximumCards is int maximum &&
				count > maximum )
			{
				report.Issues.Add( new DeckValidationIssue
				{
					Code = DeckValidationIssueCode.SectionTooLarge,
					Section = pair.Key,
					Message =
						$"Section '{pair.Key}' contains {count} cards; " +
						$"the maximum is {maximum}."
				});
			}
		}
	}

	private static void ValidateCopyCounts(
		Deck deck,
		DeckFormatDefinition format,
		Dictionary<Guid, int> copies,
		DeckValidationReport report )
	{
		foreach ( KeyValuePair<Guid, int> pair in copies )
		{
			int limit = format.DefaultCopyLimit;

			if ( format.CardCopyLimits.TryGetValue(
				pair.Key,
				out int cardLimit ) )
			{
				limit = cardLimit;
			}
			else if ( format.RestrictedOracleIds.Contains( pair.Key ) )
			{
				limit = 1;
			}

			if ( pair.Value <= limit )
				continue;

			DeckEntry? entry = deck.Entries.Find(
				candidate => candidate.Card.OracleId == pair.Key );
			string cardName = entry?.Card.Name ?? pair.Key.ToString();

			report.Issues.Add( new DeckValidationIssue
			{
				Code = format.RestrictedOracleIds.Contains( pair.Key )
					? DeckValidationIssueCode.CardRestricted
					: DeckValidationIssueCode.TooManyCopies,
				OracleId = pair.Key,
				ScryfallId = entry?.Card.ScryfallId,
				Message =
					$"'{cardName}' has {pair.Value} copies; " +
					$"the maximum is {limit}."
			});
		}
	}

	private static void Add(
		DeckValidationReport report,
		DeckValidationIssueCode code,
		string message,
		DeckEntry entry )
	{
		report.Issues.Add( new DeckValidationIssue
		{
			Code = code,
			Message = message,
			Section = entry.Section,
			ScryfallId = entry.Card.ScryfallId == Guid.Empty
				? null
				: entry.Card.ScryfallId,
			OracleId = entry.Card.OracleId
		});
	}
}
