#nullable enable

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Classes.Cards;
using Sandbox.Classes.Cards.Colors;
using Sandbox.Classes.Cards.Legality;
using Sandbox.Classes.Cards.ManaSymbols;
using Sandbox.Classes.Deck;
using Sandbox.Classes.Deck.Validation;
using Sandbox.Classes.Zones;
using Sandbox.Framework.Rules;
using System;
using System.Linq;

namespace Sandbox.UnitTests;

[TestClass]
public sealed class ZoneLayoutMathTests
{
	[TestMethod]
	public void Fan_OddCardCountCentersMiddleCard()
	{
		FanLayout middle = ZoneLayoutMath.Fan( 3, 7, 63f, 88f, 0.35f );

		Assert.AreEqual( 0f, middle.Across, 0.001f );
		Assert.AreEqual( 0f, middle.Depth, 0.001f );
		Assert.AreEqual( 0f, middle.Angle, 0.001f );
	}

	[TestMethod]
	public void Fan_OuterCardsAreSymmetricAndAngleOutward()
	{
		FanLayout left  = ZoneLayoutMath.Fan( 0, 7, 63f, 88f, 0.35f );
		FanLayout right = ZoneLayoutMath.Fan( 6, 7, 63f, 88f, 0.35f );

		Assert.AreEqual( -left.Across, right.Across, 0.001f );
		Assert.AreEqual( left.Depth, right.Depth, 0.001f );
		Assert.AreEqual( -left.Angle, right.Angle, 0.001f );
		Assert.IsTrue( left.Across < 0f );
		Assert.IsTrue( left.Angle > 0f );
		Assert.IsTrue( right.Across > 0f );
		Assert.IsTrue( right.Angle < 0f );
	}

	[TestMethod]
	public void Fan_EvenCardCountKeepsCenterGapSymmetric()
	{
		FanLayout centerLeft  = ZoneLayoutMath.Fan( 1, 4, 63f, 88f, 0.35f );
		FanLayout centerRight = ZoneLayoutMath.Fan( 2, 4, 63f, 88f, 0.35f );

		Assert.AreEqual( -centerLeft.Across, centerRight.Across, 0.001f );
		Assert.AreEqual( centerLeft.Depth, centerRight.Depth, 0.001f );
		Assert.AreEqual( -centerLeft.Angle, centerRight.Angle, 0.001f );
	}

	[TestMethod]
	public void Fan_DepthCurveMovesOuterCardsFartherThanInnerCards()
	{
		FanLayout outer = ZoneLayoutMath.Fan( 0, 7, 63f, 88f, 0.35f );
		FanLayout inner = ZoneLayoutMath.Fan( 2, 7, 63f, 88f, 0.35f );

		Assert.IsTrue( outer.Depth > inner.Depth );
		Assert.IsTrue( inner.Depth > 0f );
	}
}

[TestClass]
public sealed class DeckModelTests
{
	private static readonly Guid FirstPrinting = Guid.Parse( "10000000-0000-4000-8000-000000000001" );
	private static readonly Guid FirstOracle   = Guid.Parse( "20000000-0000-4000-8000-000000000001" );

	[TestMethod]
	public void Count_IsCaseInsensitiveAndSumsMatchingEntries()
	{
		Deck deck = CreateDeck(
			new DeckEntry { Section = "MAIN", Quantity = 2, Card = Card( FirstPrinting, FirstOracle, "Alpha" ) },
			new DeckEntry { Section = "main", Quantity = 3, Card = Card( Guid.NewGuid(), Guid.NewGuid(), "Beta" ) },
			new DeckEntry { Section = DeckSections.Sideboard, Quantity = 4, Card = Card( Guid.NewGuid(), Guid.NewGuid(), "Gamma" ) }
		);

		Assert.AreEqual( 5, deck.Count( DeckSections.Main ) );
		Assert.AreEqual( 4, deck.Count( "SIDEBOARD" ) );
	}

	[TestMethod]
	public void JsonRoundTrip_PreservesPortableDeckIdentity()
	{
		Deck source = CreateDeck(
			new DeckEntry { Section = DeckSections.Commander, Quantity = 1, Card = Card( FirstPrinting, FirstOracle, "Commander" ) }
		) with
		{
			FormatCode = "commander",
			Source = new DeckSource { Kind = "text", Site = "fixture", ExternalId = "deck-1" }
		};

		Deck restored = DeckJson.Deserialize( DeckJson.Serialize( source ) );

		Assert.AreEqual( source.Id, restored.Id );
		Assert.AreEqual( source.Name, restored.Name );
		Assert.AreEqual( source.FormatCode, restored.FormatCode );
		Assert.AreEqual( source.Source, restored.Source );
		Assert.AreEqual( source.Entries.Single(), restored.Entries.Single() );
	}

	[TestMethod]
	public void Deserialize_RejectsUnsupportedSchemaVersion()
	{
		const string json = """{"schemaVersion":999,"name":"Old deck","entries":[]}""";

		NotSupportedException exception = Assert.ThrowsException<NotSupportedException>( () => DeckJson.Deserialize( json ) );

		StringAssert.Contains( exception.Message, "999" );
	}

	[TestMethod]
	public void ConstructedFormat_CreatesExpectedMainAndSideboardRules()
	{
		DeckFormatDefinition format = DeckFormatDefinition.Constructed( "standard", "Standard", 60, 15 );

		Assert.AreEqual( 4, format.DefaultCopyLimit );
		Assert.AreEqual( 60, format.Sections[DeckSections.Main].MinimumCards );
		Assert.IsTrue( format.Sections[DeckSections.Main].CountsTowardCopyLimit );
		Assert.AreEqual( 15, format.Sections[DeckSections.Sideboard].MaximumCards );
		Assert.IsTrue( format.Sections[DeckSections.Sideboard].CountsTowardCopyLimit );
	}

	private static Deck CreateDeck( params DeckEntry[] entries )
	{
		return new Deck { Name = "Test deck", Entries = entries.ToList() };
	}

	private static DeckCardReference Card( Guid printingId, Guid oracleId, string name )
	{
		return new DeckCardReference { ScryfallId = printingId, OracleId = oracleId, Name = name, SetCode = "tst", CollectorNumber = "1" };
	}
}

[TestClass]
public sealed class ManaValueTests
{
	[TestMethod]
	public void ManaCost_PreservesPrintedOrderAndRepeatedSymbols()
	{
		ManaCost cost = ManaCost.Parse( " {2} {W} {W/U} {W} " );

		Assert.AreEqual( "{2}{W}{W/U}{W}", cost.ToString() );
		Assert.AreEqual( 4, cost.SymbolCount );
		Assert.AreEqual( SymbolIdentifier.Parse( "W/U" ), cost[2] );
	}

	[TestMethod]
	public void ManaCost_DistinguishesNoCostFromZeroCost()
	{
		ManaCost none = ManaCost.Parse( "" );
		ManaCost zero = ManaCost.Parse( "{0}" );

		Assert.IsFalse( none.HasManaCost );
		Assert.IsTrue( zero.HasManaCost );
		Assert.AreNotEqual( none, zero );
	}

	[TestMethod]
	public void ManaCost_RejectsCombinedFaceCosts()
	{
		Assert.ThrowsException<FormatException>( () => ManaCost.Parse( "{1}{W} // {2}{U}" ) );
	}

	[TestMethod]
	public void ManaSymbol_RecognizesHybridAndRejectsNonManaSymbols()
	{
		ManaSymbol hybrid = ManaSymbol.Parse( "{W/U/P}" );

		Assert.AreEqual( ManaSymbolKind.HybridPhyrexian, hybrid.Kind );
		Assert.IsTrue( hybrid.ContainsColor( MagicColor.White ) );
		Assert.IsTrue( hybrid.ContainsColor( MagicColor.Blue ) );
		Assert.ThrowsException<FormatException>( () => ManaSymbol.Parse( "{T}" ) );
	}

	[TestMethod]
	public void ProducedMana_NormalizesOrderAndRemovesDuplicates()
	{
		ProducedManaSet mana = ProducedManaSet.FromScryfall( [ "T", "U", "W", "U", "C" ] );

		CollectionAssert.AreEqual( new[] { "W", "U", "C", "T" }, mana.ToScryfallArray() );
	}

	[TestMethod]
	public void ColorSet_UsesCanonicalWubrgOrderAndSetOperations()
	{
		ColorSet colors = ColorSet.FromColors( MagicColor.Green, MagicColor.White, MagicColor.Black );

		Assert.AreEqual( "WBG", colors.ToAbbreviationString() );
		Assert.AreEqual( ColorSet.White, colors.Intersect( ColorSet.White.Union( ColorSet.Blue ) ) );
		Assert.AreEqual( ColorSet.FromColors( MagicColor.Black, MagicColor.Green ), colors.Except( ColorSet.White ) );
	}

	[TestMethod]
	public void FormatLegalities_LookupIsCaseInsensitiveAndUnknownIsNull()
	{
		FormatLegalities legalities = new FormatLegalities
		{
			ByFormat = new() { ["commander"] = CardLegality.Legal }
		};

		Assert.AreEqual( CardLegality.Legal, legalities.Get( "COMMANDER" ) );
		Assert.IsNull( legalities.Get( "future-format" ) );
	}
}

[TestClass]
public sealed class RuleDecisionTests
{
	[TestMethod]
	public void Permit_CarriesCommandsInOrder()
	{
		NoOpCommand first  = new NoOpCommand( "first" );
		NoOpCommand second = new NoOpCommand( "second" );

		RuleDecision decision = RuleDecision.Permit( first, second );

		Assert.IsTrue( decision.Allowed );
		CollectionAssert.AreEqual( new GameCommand[] { first, second }, decision.Commands.ToArray() );
	}

	[TestMethod]
	public void Reject_PreservesStructuredFailure()
	{
		RuleDecision decision = RuleDecision.Reject( "priority.not_holder", "Another player has priority." );

		Assert.IsFalse( decision.Allowed );
		Assert.AreEqual( "priority.not_holder", decision.Code );
		Assert.AreEqual( "Another player has priority.", decision.Message );
		Assert.AreEqual( 0, decision.Commands.Count );
	}
}

[TestClass]
public sealed class MultiplayerPolicyTests
{
	private static readonly Guid Actor = Guid.Parse( "30000000-0000-4000-8000-000000000001" );
	private static readonly Guid Other = Guid.Parse( "30000000-0000-4000-8000-000000000002" );

	[TestMethod]
	public void InteractiveCardAction_IsDeniedWithoutPriority()
	{
		RuleEvaluation result = FlowPermissionPolicy.Evaluate(
			new GrabCardIntent( Actor, null! ),
			Actor,
			Flow( priorityPlayerId: Other )
		);

		Assert.AreEqual( RuleVerdict.Deny, result.Verdict );
		Assert.AreEqual( "priority.not_holder", result.Code );
	}

	[TestMethod]
	public void InteractiveCardAction_IsNotDeniedForPriorityHolder()
	{
		RuleEvaluation result = FlowPermissionPolicy.Evaluate(
			new FlipCardIntent( Actor, null! ),
			Actor,
			Flow( priorityPlayerId: Actor )
		);

		Assert.AreEqual( RuleVerdict.Abstain, result.Verdict );
	}

	[TestMethod]
	public void PassPriority_RequiresCurrentPriorityHolder()
	{
		Assert.AreEqual(
			RuleVerdict.Allow,
			FlowPermissionPolicy.Evaluate( new PassPriorityIntent( Actor ), Actor, Flow( priorityPlayerId: Actor ) ).Verdict
		);
		Assert.AreEqual(
			RuleVerdict.Deny,
			FlowPermissionPolicy.Evaluate( new PassPriorityIntent( Actor ), Actor, Flow( priorityPlayerId: Other ) ).Verdict
		);
	}

	[TestMethod]
	public void EndTurn_RequiresActivePlayer()
	{
		RuleEvaluation denied = FlowPermissionPolicy.Evaluate(
			new EndTurnIntent( Actor ),
			Actor,
			Flow( activePlayerId: Other, priorityPlayerId: Actor )
		);

		Assert.AreEqual( RuleVerdict.Deny, denied.Verdict );
		Assert.AreEqual( "turn.not_active_player", denied.Code );
		Assert.AreEqual(
			RuleVerdict.Allow,
			FlowPermissionPolicy.Evaluate( new EndTurnIntent( Actor ), Actor, Flow( activePlayerId: Actor ) ).Verdict
		);
	}

	[TestMethod]
	public void CommandZoneEntry_RequiresDeclaredCommander()
	{
		RuleEvaluation denied = CardMovePolicy.EvaluateCommandZoneEntry( commandZone: true, declaredCommander: false );

		Assert.AreEqual( RuleVerdict.Deny, denied.Verdict );
		Assert.AreEqual( "zone.commander_only", denied.Code );
		Assert.AreEqual( RuleVerdict.Abstain, CardMovePolicy.EvaluateCommandZoneEntry( true, true ).Verdict );
		Assert.AreEqual( RuleVerdict.Abstain, CardMovePolicy.EvaluateCommandZoneEntry( false, false ).Verdict );
	}

	[TestMethod]
	public void CardVisibility_PrefersPublicIdentityButRetainsOwnerPrivateIdentity()
	{
		Guid publicIdentity  = Guid.Parse( "40000000-0000-4000-8000-000000000001" );
		Guid privateIdentity = Guid.Parse( "40000000-0000-4000-8000-000000000002" );

		Assert.AreEqual( publicIdentity, CardVisibilityPolicy.KnownPrintingId( publicIdentity, privateIdentity ) );
		Assert.AreEqual( privateIdentity, CardVisibilityPolicy.KnownPrintingId( Guid.Empty, privateIdentity ) );
		Assert.AreEqual( Guid.Empty, CardVisibilityPolicy.KnownPrintingId( Guid.Empty, Guid.Empty ) );
		Assert.IsTrue( CardVisibilityPolicy.IsPrivateView( privateIdentity, Guid.Empty ) );
		Assert.IsFalse( CardVisibilityPolicy.IsPrivateView( publicIdentity, publicIdentity ) );
	}

	private static MatchFlowSnapshot Flow( Guid activePlayerId = default, Guid priorityPlayerId = default )
	{
		return new MatchFlowSnapshot( 1, TurnPhase.PrecombatMain, TurnStep.PrecombatMain, activePlayerId, priorityPlayerId, 0, 0, false );
	}
}
