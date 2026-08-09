using Sandbox.Classes;
using Sandbox.Classes.Cards;
using Sandbox.Classes.Deck;
using Sandbox.Classes.Zones;
using Sandbox.Framework.GameInfo;
using System;
namespace Sandbox.Framework;

/// <summary>
///     Untrusted Requests Made by Client
/// </summary>
public sealed partial class GameDirector
{
	[Rpc.Host]
	public void SetReady( bool ready )
	{
		if ( State != GameState.Lobby || Roster.SeatOf( Rpc.CallerId ) is not PlayerSeat seat )
			return;

		SetReadyAuthority( seat, ready );
	}


	[Rpc.Host]
	public void RequestStartMatch()
	{
		if ( Connection.Host is not { } host || Rpc.CallerId != host.Id )
			return;

		StartMatchAuthority();
	}


	[Rpc.Host]
	public void KeepOpeningHand()
	{
		if ( Roster.SeatOf( Rpc.CallerId ) is not PlayerSeat seat )
			return;

		KeepOpeningHandAuthority( seat );
	}


	[Rpc.Host]
	public void TakeMulligan()
	{
		if ( State != GameState.Mulligan || Roster.SeatOf( Rpc.CallerId ) is not PlayerSeat seat || seat.MulliganComplete || !RulesEngine.TryMulligan( seat ) )
			return;

		seat.MulliganCount++;
	}


	[Rpc.Host]
	public void PassPriority()
	{
		if ( State != GameState.Playing || Roster.SeatOf( Rpc.CallerId ) is not PlayerSeat seat )
			return;

		Priority.Pass( seat );
	}


	[Rpc.Host]
	public void EndTurn()
	{
		if ( State != GameState.Playing || Roster.SeatOf( Rpc.CallerId ) is not PlayerSeat seat || !RulesEngine.CanEndTurn( seat ) )
			return;

		TurnManager.EndTurn();
	}


	[Rpc.Host]
	public void Concede()
	{
		if ( State is not (GameState.Mulligan or GameState.Playing) || Roster.SeatOf( Rpc.CallerId ) is not PlayerSeat seat )
			return;

		seat.Outcome          = PlayerOutcome.Conceded;
		seat.MulliganComplete = true;
		ReleasePlayerGrabs( seat.ParticipantId );
		PlayerSeat[] remaining = Roster.Seats.Where( s => s.IsOccupied && !s.IsEliminated ).ToArray();

		if ( remaining.Length <= 1 )
			FinishMatch( remaining.FirstOrDefault(), $"{seat.DisplayName} conceded" );
	}


	[Rpc.Host]
	public void RequestMoveCard( CardObject card, Guid destinationZoneId, Transform freeformPose )
	{
		if ( State != GameState.Playing || card is null || Roster.SeatOf( Rpc.CallerId ) is not PlayerSeat seat || ZoneObject.Find( destinationZoneId ) is not ZoneObject destination )
			return;

		ZoneObject? source = ZoneObject.Find( card.ZoneId );

		if ( card.GrabbedByPlayerId != Guid.Empty && card.GrabbedByPlayerId != seat.ParticipantId )
			return;

		if ( !RulesEngine.CanMoveCard( seat, card, source, destination ) )
		{
			card.MoveTo( card.RestPose );
			card.Pulse();

			return;
		}

		card.GrabbedByPlayerId = Guid.Empty;
		card.PlaceInZone( destinationZoneId, freeformPose );
	}


	[Rpc.Host]
	public void RequestGrabCard( CardObject card )
	{
		if ( State != GameState.Playing || card is null || card.GrabbedByPlayerId != Guid.Empty || Roster.SeatOf( Rpc.CallerId ) is not PlayerSeat seat )
			return;

		ZoneObject? source = ZoneObject.Find( card.ZoneId );

		if ( source is null || !RulesEngine.CanMoveCard( seat, card, source, source ) )
			return;

		card.GrabbedByPlayerId = seat.ParticipantId;
	}


	[Rpc.Host]
	public void RequestFlipCard( CardObject card )
	{
		if ( card is null || Roster.SeatOf( Rpc.CallerId ) is not PlayerSeat seat || !RulesEngine.CanFlipCard( seat, card ) )
			return;

		card.FlipPrintedFace();
	}


	[Rpc.Host]
	public void RequestThrowCard( CardObject card, Vector3 velocity, Vector3 angularVelocity )
	{
		if ( State != GameState.Playing || card is null || Roster.SeatOf( Rpc.CallerId ) is not PlayerSeat seat )
			return;

		ZoneObject? source = ZoneObject.Find( card.ZoneId );

		if ( source is null || !RulesEngine.CanMoveCard( seat, card, source, source ) )
			return;

		card.GrabbedByPlayerId = Guid.Empty;
		card.Throw( velocity, angularVelocity );
	}


	[Rpc.Host]
	public void RequestTapCard( CardObject card )
	{
		if ( card is null || Roster.SeatOf( Rpc.CallerId ) is not PlayerSeat seat || !RulesEngine.CanTapCard( seat, card ) )
			return;

		card.SetTapped( !card.Tapped );
	}


	[Rpc.Host]
	public void PerformAction( string actionId, long amount = 0 )
	{
		if ( State != GameState.Playing || string.IsNullOrWhiteSpace( actionId ) || Roster.SeatOf( Rpc.CallerId ) is not PlayerSeat seat )
			return;

		GameAction? offered = RulesEngine.ActionsFor( seat ).FirstOrDefault( action => string.Equals( action.Id, actionId, StringComparison.Ordinal ) );

		if ( offered is not GameAction action || action.Disabled )
			return;

		RulesEngine.TryPerformAction( seat, actionId, amount );
	}


	[Rpc.Host]
	public void ReleaseGrab( CardObject card )
	{
		if ( card is null || card.GrabbedByPlayerId != Rpc.CallerId )
			return;

		card.GrabbedByPlayerId = Guid.Empty;
	}


	[Rpc.Host]
	public void SubmitDeck( string deckJson )
	{
		if ( State != GameState.Lobby || Roster.SeatOf( Rpc.CallerId ) is not PlayerSeat seat )
			return;

		try
		{
			Deck deck = DeckJson.Deserialize( deckJson );
			AcceptDeckAuthority( seat, deck );
		}
		catch ( Exception exception )
		{
			seat.DeckAccepted = false;
			seat.Ready        = false;
			seat.DeckStatus   = exception.Message;
		}
	}
}
