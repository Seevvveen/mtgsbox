#nullable enable

using Sandbox.Classes.Database.Types;
using Sandbox.Classes.Deck;
using Sandbox.Classes.Deck.Validation;
using Sandbox.Framework.GameInfo;
using System;
using RuntimeCardDatabase = Sandbox.Classes.Database.CardDatabase;

namespace Sandbox.Framework.Rules.Formats;

/// <summary>
///     Commander deck-construction profile. Gameplay-specific Commander rules
///     (command-zone replacement, tax, color identity, and commander damage)
///     can be added as separate modules without changing this validator.
/// </summary>
public sealed class CommanderRulesModule : RulesModule, IDeckRuleProvider
{
	private readonly CommanderDeckRule _deckRule = new CommanderDeckRule();

	public CommanderRulesModule()
	{
		ModuleId = "mtg.commander";
		ModuleVersion = "1";
	}

	public IEnumerable<IDeckFormatRule> AdditionalDeckRules => [ _deckRule ];

	public DeckFormatDefinition CreateDeckFormat( MTGFormat format )
	{
		return new DeckFormatDefinition
		{
			Code = format.FormatCode,
			DisplayName = format.DisplayName,
			CardLegalityCode = "commander",
			// Commander copy limits require a Basic-land exception, which is
			// evaluated by CommanderDeckRule after card identities are resolved.
			DefaultCopyLimit = int.MaxValue,
			Sections = new Dictionary<string, DeckSectionRule>( StringComparer.OrdinalIgnoreCase )
			{
				[DeckSections.Main] = new DeckSectionRule { MaximumCards = format.DeckSize - 1, CountsTowardCopyLimit = true },
				[DeckSections.Commander] = new DeckSectionRule { MinimumCards = 1, MaximumCards = 2, CountsTowardCopyLimit = true },
				[DeckSections.Companion] = new DeckSectionRule { MaximumCards = 1, CountsTowardCopyLimit = true },
				[DeckSections.Sideboard] = new DeckSectionRule { MaximumCards = 0, CountsTowardCopyLimit = false }
			}
		};
	}


	private sealed class CommanderDeckRule : IDeckFormatRule
	{
		public void Validate( Deck deck, DeckFormatDefinition format, DeckValidationReport report )
		{
			int deckCards = deck.Count( DeckSections.Main ) + deck.Count( DeckSections.Commander );

			if ( deckCards != 100 )
			{
				report.Issues.Add(
					new DeckValidationIssue
					{
						Code = DeckValidationIssueCode.FormatRule,
						Message = $"A Commander deck must contain exactly 100 cards including its commander; this deck contains {deckCards}."
					}
				);
			}

			Dictionary<Guid, (int Quantity, DeckEntry Entry)> copies = new Dictionary<Guid, (int, DeckEntry)>();

			foreach ( DeckEntry entry in deck.Entries.Where( entry =>
				string.Equals( entry.Section, DeckSections.Main, StringComparison.OrdinalIgnoreCase ) ||
				string.Equals( entry.Section, DeckSections.Commander, StringComparison.OrdinalIgnoreCase ) ||
				string.Equals( entry.Section, DeckSections.Companion, StringComparison.OrdinalIgnoreCase ) ) )
			{
				if ( entry.Card.OracleId is not Guid oracleId || entry.Quantity <= 0 )
					continue;

				if ( copies.TryGetValue( oracleId, out (int Quantity, DeckEntry Entry) current ) )
					copies[oracleId] = (checked(current.Quantity + entry.Quantity), current.Entry);
				else
					copies.Add( oracleId, (entry.Quantity, entry) );
			}

			foreach ( (Guid _, (int quantity, DeckEntry entry)) in copies )
			{
				if ( quantity <= 1 || IsBasicLand( entry.Card.ScryfallId ) )
					continue;

				report.Issues.Add(
					new DeckValidationIssue
					{
						Code = DeckValidationIssueCode.TooManyCopies,
						Section = entry.Section,
						ScryfallId = entry.Card.ScryfallId,
						OracleId = entry.Card.OracleId,
						Message = $"'{entry.Card.Name}' has {quantity} copies; Commander allows one copy except for basic lands."
					}
				);
			}
		}


		private static bool IsBasicLand( Guid printingId )
		{
			NormalizedCard? card = RuntimeCardDatabase.GetCard( printingId );
			string typeLine = card?.Gameplay.TypeLine ?? string.Empty;

			return typeLine.StartsWith( "Basic ", StringComparison.OrdinalIgnoreCase ) &&
			       typeLine.Contains( "Land", StringComparison.OrdinalIgnoreCase );
		}
	}
}
