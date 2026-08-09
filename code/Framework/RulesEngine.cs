#nullable enable

using Sandbox.Classes.Cards;
using Sandbox.Classes.Deck;
using Sandbox.Classes.Deck.Validation;
using Sandbox.Classes.Zones;
using Sandbox.Framework.GameInfo;
using System;
namespace Sandbox.Framework;

/// <summary>
///     Provides default interaction methods. This is contains standard behavior interactions.
///     different formats will create derivatives of this class to establish custom rules
/// </summary>
public class RulesEngine : Component
{
	public GameDirector Director
	{
		get { return Scene.Get<GameDirector>() ?? throw new InvalidOperationException( "The scene has no MTG game director." ); }
	}


	//Returns Deck Building Rules
	public virtual DeckFormatDefinition CreateFormat( string formatCode )
	{
		return DeckFormatDefinition.Constructed( formatCode, formatCode );
	}


	//
	// Validation
	//
	public virtual bool CanMoveCard( PlayerSeat player, CardObject card, ZoneObject? source, ZoneObject destination )
	{
		if ( player.IsEliminated || Director.State != GameState.Playing || !Director.Priority.HasPriority( player ) || !destination.CanAccept( card ) )
			return false;

		if ( destination.OwnerPlayerId != Guid.Empty && destination.OwnerPlayerId != player.ParticipantId )
			return false;

		Guid controller = card.ControllerPlayerId != Guid.Empty? card.ControllerPlayerId : card.OwnerPlayerId;

		return controller == Guid.Empty || controller == player.ParticipantId;
	}


	public virtual bool CanFlipCard( PlayerSeat player, CardObject card )
	{
		return !player.IsEliminated && ( card.ControllerPlayerId == player.ParticipantId || card.OwnerPlayerId == player.ParticipantId );
	}


	public virtual bool CanTapCard( PlayerSeat player, CardObject card )
	{
		return CanFlipCard( player, card ) && ZoneObject.Find( card.ZoneId )?.ZoneType == ZoneType.Battlefield;
	}


	public virtual bool CanEndTurn( PlayerSeat player )
	{
		return Director.TurnManager.ActivePlayerId == player.ParticipantId;
	}


	//
	// Custom Actions
	//
	public virtual IReadOnlyList<GameAction> ActionsFor( PlayerSeat player )
	{
		return Array.Empty<GameAction>();
	}


	public virtual bool TryPerformAction( PlayerSeat player, string actionId, long amount )
	{
		return false;
	}



	//
	// Match Setup and turn flow
	//
	public virtual void SetupMatch( IReadOnlyList<PlayerSeat> players ) { }


	public virtual void OnTurnStarted( PlayerSeat player ) { }


	public virtual bool TryMulligan( PlayerSeat player )
	{
		return false;
	}


	public virtual void OnOpeningHandKept( PlayerSeat player ) { }


	public virtual void OnPhaseChanged( TurnPhase previous, TurnPhase current ) { }


	public virtual void OnStepChanged( TurnStep previous, TurnStep current ) { }


	/// <summary>
	///     Returns whether a conditional turn step should occur.
	///     First-strike combat damage is skipped unless a format-specific
	///     rules engine determines that the step is required.
	/// </summary>
	public virtual bool ShouldEnterStep( TurnStep step )
	{
		return step != TurnStep.FirstStrikeCombatDamage;
	}


	/// <summary>
	///     Cleanup normally does not grant priority. Override this when
	///     state-based actions or triggered abilities require a priority window.
	/// </summary>
	public virtual bool ShouldGrantCleanupPriority()
	{
		return false;
	}


	public virtual void OnAllPlayersPassedPriority() { }


	public virtual void OnPlayerDisconnected( PlayerSeat player ) { }


	//
	// Helpers
	//
	protected ZoneObject CreatePlayerZone( PlayerSeat player, ZoneType type, Transform pose, ZoneLayout? layout = null, int capacity = 0 )
	{
		if ( !Networking.IsHost )
			throw new InvalidOperationException( "Only the host can create match zones." );

		GameObject  zoneObject = new GameObject( Director.GameObject, true, $"{player.DisplayName} {type}" ) { WorldPosition = pose.Position, WorldRotation = pose.Rotation };
		ZoneObject? zone       = zoneObject.Components.Create<ZoneObject>();
		zone.OwnerPlayerId = player.ParticipantId;
		zone.ZoneType      = type;
		zone.Role          = type.ToString();
		zone.Capacity      = capacity;

		if ( layout is ZoneLayout selectedLayout )
		{
			zone.UseRecommendedLayout = false;
			zone.Layout               = selectedLayout;
		}

		zone.RefreshConfiguration();
		AssignZoneToSeat( player, type, zone.ZoneId );

		if ( Networking.IsActive )
			zoneObject.NetworkSpawn();

		return zone;
	}


	protected IReadOnlyList<CardObject> PopulateZoneFromDeck( PlayerSeat player, Deck deck, ZoneObject zone, string section = DeckSections.Main, MtgZoneCardState state = MtgZoneCardState.ZoneDefault )
	{
		ArgumentNullException.ThrowIfNull( deck );
		List<CardObject> cards = new List<CardObject>();

		foreach ( DeckEntry entry in deck.Entries.Where( entry => string.Equals( entry.Section, section, StringComparison.OrdinalIgnoreCase ) ) )
		{
			for ( int copy = 0; copy < entry.Quantity; copy++ )
			{
				CardObject card = CreateCard( player, entry.Card.ScryfallId );
				zone.AddCard( card, state, animate: false );
				cards.Add( card );
			}
		}

		return cards;
	}


	public CardObject CreateCard( PlayerSeat owner, Guid printingId )
	{
		if ( !Networking.IsHost )
			throw new InvalidOperationException( "Only the host can create match cards." );

		GameObject  cardObject = new GameObject( Director.GameObject, true, "MTG Card" );
		CardObject? card       = cardObject.Components.Create<CardObject>();
		card.OwnerPlayerId      = owner.ParticipantId;
		card.ControllerPlayerId = owner.ParticipantId;
		card.SetCard( printingId );

		if ( Networking.IsActive )
			cardObject.NetworkSpawn();

		return card;
	}


	private static void AssignZoneToSeat( PlayerSeat player, ZoneType type, Guid zoneId )
	{
		switch ( type )
		{
			case ZoneType.Library:     player.LibraryZoneId     = zoneId; break;
			case ZoneType.Hand:        player.HandZoneId        = zoneId; break;
			case ZoneType.Battlefield: player.BattlefieldZoneId = zoneId; break;
			case ZoneType.Graveyard:   player.GraveyardZoneId   = zoneId; break;
			case ZoneType.Exile:       player.ExileZoneId       = zoneId; break;
			case ZoneType.Command:     player.CommandZoneId     = zoneId; break;
		}
	}
}
