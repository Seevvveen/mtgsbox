#nullable enable

using Sandbox.Classes.DeckValidation;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Classes;

/// <summary>
/// Base rules surface for an actual MTG game variant. The director owns
/// networking and lifecycle; subclasses own format and gameplay decisions.
/// </summary>
public class MtgGameRules : Component
{
	public MtgGameDirector Director =>
		Scene.Get<MtgGameDirector>() ??
		throw new InvalidOperationException(
			"The scene has no MTG game director." );

	public virtual DeckFormatDefinition CreateFormat(
		string formatCode ) =>
		DeckFormatDefinition.Constructed(
			formatCode,
			formatCode,
			60,
			15 );

	public virtual bool CanMoveCard(
		MtgPlayerSeat player,
		CardObject card,
		ZoneObject? source,
		ZoneObject destination )
	{
		if ( player.IsEliminated ||
			Director.State != MtgMatchState.Playing ||
			Director.PriorityPlayerId != player.PlayerId ||
			!destination.CanAccept( card ) )
		{
			return false;
		}

		if ( destination.OwnerPlayerId != Guid.Empty &&
			destination.OwnerPlayerId != player.PlayerId )
		{
			return false;
		}

		Guid controller = card.ControllerPlayerId != Guid.Empty
			? card.ControllerPlayerId
			: card.OwnerPlayerId;
		return controller == Guid.Empty ||
			controller == player.PlayerId;
	}

	public virtual bool CanFlipCard(
		MtgPlayerSeat player,
		CardObject card ) =>
		!player.IsEliminated &&
		(card.ControllerPlayerId == player.PlayerId ||
			card.OwnerPlayerId == player.PlayerId);

	public virtual bool CanTapCard(
		MtgPlayerSeat player,
		CardObject card ) =>
		CanFlipCard( player, card ) &&
		ZoneObject.Find( card.ZoneId )?.ZoneKind ==
			MtgZoneKind.Battlefield;

	public virtual bool CanEndTurn( MtgPlayerSeat player ) =>
		Director.ActivePlayerId == player.PlayerId;

	public virtual IReadOnlyList<MtgGameAction> ActionsFor(
		MtgPlayerSeat player ) =>
		Array.Empty<MtgGameAction>();

	public virtual bool TryPerformAction(
		MtgPlayerSeat player,
		string actionId,
		long amount ) => false;

	public virtual void SetupMatch(
		IReadOnlyList<MtgPlayerSeat> players )
	{
	}

	public virtual void OnTurnStarted( MtgPlayerSeat player )
	{
	}

	public virtual bool TryMulligan( MtgPlayerSeat player )
	{
		return false;
	}

	public virtual void OnOpeningHandKept(
		MtgPlayerSeat player )
	{
	}

	public virtual void OnPhaseChanged(
		MtgTurnPhase previous,
		MtgTurnPhase current )
	{
	}

	public virtual void OnAllPlayersPassedPriority()
	{
	}

	public virtual void OnPlayerDisconnected(
		MtgPlayerSeat player )
	{
	}

	protected ZoneObject CreatePlayerZone(
		MtgPlayerSeat player,
		MtgZoneKind kind,
		Transform pose,
		MtgZoneLayout? layout = null,
		int capacity = 0 )
	{
		if ( !Networking.IsHost )
			throw new InvalidOperationException(
				"Only the host can create match zones." );

		var zoneObject = new GameObject(
			Director.GameObject,
			true,
			$"{player.DisplayName} {kind}" )
		{
			WorldPosition = pose.Position,
			WorldRotation = pose.Rotation
		};
		var zone =
			zoneObject.Components.Create<ZoneObject>();
		zone.OwnerPlayerId = player.PlayerId;
		zone.ZoneKind = kind;
		zone.Role = kind.ToString();
		zone.Capacity = capacity;

		if ( layout is MtgZoneLayout selectedLayout )
		{
			zone.UseRecommendedLayout = false;
			zone.Layout = selectedLayout;
		}

		zone.RefreshConfiguration();
		AssignZoneToSeat( player, kind, zone.ZoneId );

		if ( Networking.IsActive )
			zoneObject.NetworkSpawn();

		return zone;
	}

	protected IReadOnlyList<CardObject> PopulateZoneFromDeck(
		MtgPlayerSeat player,
		Deck deck,
		ZoneObject zone,
		string section = DeckSections.Main,
		MtgZoneCardState state =
			MtgZoneCardState.ZoneDefault )
	{
		ArgumentNullException.ThrowIfNull( deck );
		var cards = new List<CardObject>();

		foreach ( DeckEntry entry in deck.Entries.Where(
			entry => string.Equals(
				entry.Section,
				section,
				StringComparison.OrdinalIgnoreCase ) ) )
		{
			for ( int copy = 0;
				copy < entry.Quantity;
				copy++ )
			{
				CardObject card = CreateCard(
					player,
					entry.Card.ScryfallId );
				zone.AddCard(
					card,
					state,
					animate: false );
				cards.Add( card );
			}
		}

		return cards;
	}

	protected CardObject CreateCard(
		MtgPlayerSeat owner,
		Guid printingId )
	{
		if ( !Networking.IsHost )
			throw new InvalidOperationException(
				"Only the host can create match cards." );

		var cardObject = new GameObject(
			Director.GameObject,
			true,
			"MTG Card" );
		var card =
			cardObject.Components.Create<CardObject>();
		card.OwnerPlayerId = owner.PlayerId;
		card.ControllerPlayerId = owner.PlayerId;
		card.SetCard( printingId );

		if ( Networking.IsActive )
			cardObject.NetworkSpawn();

		return card;
	}

	private static void AssignZoneToSeat(
		MtgPlayerSeat player,
		MtgZoneKind kind,
		Guid zoneId )
	{
		switch ( kind )
		{
			case MtgZoneKind.Library:
				player.LibraryZoneId = zoneId;
				break;
			case MtgZoneKind.Hand:
				player.HandZoneId = zoneId;
				break;
			case MtgZoneKind.Battlefield:
				player.BattlefieldZoneId = zoneId;
				break;
			case MtgZoneKind.Graveyard:
				player.GraveyardZoneId = zoneId;
				break;
			case MtgZoneKind.Exile:
				player.ExileZoneId = zoneId;
				break;
			case MtgZoneKind.Command:
				player.CommandZoneId = zoneId;
				break;
		}
	}
}
