#nullable enable

using Sandbox.Classes;
using Sandbox.Classes.Cards;
using Sandbox.Classes.DeckValidation;
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
		if ( player.IsEliminated || Director.State != GameState.Playing || Director.PriorityPlayerId != player.PlayerId || !destination.CanAccept( card ) )
			return false;

		if ( destination.OwnerPlayerId != Guid.Empty && destination.OwnerPlayerId != player.PlayerId )
			return false;

		Guid controller = card.ControllerPlayerId != Guid.Empty? card.ControllerPlayerId : card.OwnerPlayerId;

		return controller == Guid.Empty || controller == player.PlayerId;
	}


	public virtual bool CanFlipCard( PlayerSeat player, CardObject card )
	{
		return !player.IsEliminated && ( card.ControllerPlayerId == player.PlayerId || card.OwnerPlayerId == player.PlayerId );
	}


	public virtual bool CanTapCard( PlayerSeat player, CardObject card )
	{
		return CanFlipCard( player, card ) && ZoneObject.Find( card.ZoneId )?.ZoneType == ZoneType.Battlefield;
	}


	public virtual bool CanEndTurn( PlayerSeat player )
	{
		return Director.ActivePlayerId == player.PlayerId;
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
		zone.OwnerPlayerId = player.PlayerId;
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


	protected CardObject CreateCard( PlayerSeat owner, Guid printingId )
	{
		if ( !Networking.IsHost )
			throw new InvalidOperationException( "Only the host can create match cards." );

		GameObject  cardObject = new GameObject( Director.GameObject, true, "MTG Card" );
		CardObject? card       = cardObject.Components.Create<CardObject>();
		card.OwnerPlayerId      = owner.PlayerId;
		card.ControllerPlayerId = owner.PlayerId;
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
