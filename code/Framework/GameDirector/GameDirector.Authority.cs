using Sandbox.Classes;
using Sandbox.Classes.Cards;
using Sandbox.Classes.Deck;
using Sandbox.Classes.Deck.Validation;
using Sandbox.Framework.GameInfo;
using System;

namespace Sandbox.Framework;

/// <summary>
///     Mutations to the game State made from validated requests
/// </summary>
public sealed partial class GameDirector
{
	public bool AcceptDeckAuthority( PlayerSeat seat, Deck deck )
	{
		if ( !Networking.IsHost || State != GameState.Lobby )
			return false;

		string formatCode = Format?.FormatCode ?? deck.FormatCode ?? "standard";

		DeckValidationReport report = DeckValidator.Validate( deck, RulesEngine.CreateFormat( formatCode ) );

		if ( !report.IsLegal )
		{
			seat.DeckAccepted = false;
			seat.Ready        = false;

			seat.DeckStatus = report.Issues[0].Message;

			return false;
		}

		_submittedDecks[seat.ParticipantId] = deck;
		seat.DeckAccepted                   = true;
		seat.DeckStatus                     = $"{deck.Name} accepted";

		return true;
	}


	public void SetReadyAuthority( PlayerSeat seat, bool ready )
	{
		if ( !Networking.IsHost || State != GameState.Lobby )
			return;

		seat.Ready = ready && seat.DeckAccepted;
	}


	public void StartMatchAuthority()
	{
		if ( !Networking.IsHost || State != GameState.Lobby )
			return;

		PlayerSeat[] players = Roster.Seats.Where( seat => seat.IsOccupied ).ToArray();

		int minimum = Format?.MinimumPlayers ?? 2;
		int maximum = Format?.MaximumPlayers ?? 2;

		if ( players.Length < minimum || players.Length > maximum || players.Any( seat => !seat.Ready || !seat.DeckAccepted ) )
		{
			StatusText = "All players need a valid deck and must be ready";

			return;
		}

		State = GameState.Loading;

		foreach ( PlayerSeat seat in players )
		{
			seat.Life             = Format?.StartingLife ?? 20;
			seat.Poison           = 0;
			seat.Outcome          = PlayerOutcome.None;
			seat.MulliganCount    = 0;
			seat.MulliganComplete = false;
		}

		RulesEngine.SetupMatch( players );
		State      = GameState.Mulligan;
		StatusText = "Choose your opening hand";
	}


	public void KeepOpeningHandAuthority( PlayerSeat seat )
	{
		if ( !Networking.IsHost || State != GameState.Mulligan || seat.MulliganComplete )
			return;

		seat.MulliganComplete = true;
		RulesEngine.OnOpeningHandKept( seat );
		TryFinishMulligans();
	}


	public void BeginPlaying()
	{
		if ( !Networking.IsHost || State is not (GameState.Mulligan or GameState.Loading) )
			return;

		PlayerSeat? first = Roster.Seats.FirstOrDefault( seat => seat.IsOccupied && !seat.IsEliminated );

		if ( first is null )
			return;

		State = GameState.Playing;

		// TurnManager owns turn/phase progression and PriorityManager owns priority now -
		// GameDirector's job is just to kick the first turn off.
		TurnManager.Reset();
		TurnManager.BeginTurn( first );
	}


	public void FinishMatch( PlayerSeat? winner, string detail = "" )
	{
		if ( !Networking.IsHost )
			return;

		State = GameState.Finished;

		foreach ( PlayerSeat seat in Roster.Seats )
		{
			if ( !seat.IsOccupied )
				continue;

			seat.Outcome = winner is null? PlayerOutcome.Draw : ReferenceEquals( seat, winner )? PlayerOutcome.Won : PlayerOutcome.Lost;
		}

		Result = new MatchResult( winner is null? "Draw" : $"{winner.DisplayName} wins", detail, winner is null? MatchResultTone.Draw : MatchResultTone.Neutral );

		StatusText = Result.Title;

		// Stop the clock and clear anything left pending so a finished match
		// doesn't leave a dangling priority window or stack objects behind.
		Priority.End();
		Stack.Clear();
	}


	private void TryFinishMulligans()
	{
		PlayerSeat[] players = Roster.Seats.Where( seat => seat.IsOccupied && !seat.IsEliminated ).ToArray();

		if ( players.Length > 0 && players.All( seat => seat.MulliganComplete ) )
			BeginPlaying();
	}


	public Deck? GetSubmittedDeck( Guid playerId )
	{
		return _submittedDecks.TryGetValue( playerId, out Deck? deck )? deck : null;
	}


	private void ReleasePlayerGrabs( Guid playerId )
	{
		foreach ( CardObject card in Scene.GetAllComponents<CardObject>() )
		{
			if ( card.GrabbedByPlayerId == playerId )
				card.GrabbedByPlayerId = Guid.Empty;
		}
	}
}
