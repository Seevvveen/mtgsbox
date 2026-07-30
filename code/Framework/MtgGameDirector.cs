#nullable enable

using Sandbox.Classes.DeckValidation;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Classes;

/// <summary>
/// Host-authoritative MTG match coordinator: seats, deck validation, match
/// lifecycle, phases, priority, and validated world-card commands.
/// </summary>
public sealed class MtgGameDirector :
	Component,
	Component.INetworkListener
{
	[Property]
	public MtgGameDefinition? Definition { get; set; }

	[Sync]
	public Guid MatchId { get; set; }

	[Sync]
	public MtgMatchState State { get; set; } =
		MtgMatchState.Lobby;

	[Sync]
	public int TurnNumber { get; set; }

	[Sync]
	public MtgTurnPhase Phase { get; set; } =
		MtgTurnPhase.Beginning;

	[Sync]
	public Guid ActivePlayerId { get; set; }

	[Sync]
	public Guid PriorityPlayerId { get; set; }

	[Sync]
	public int PriorityPassCount { get; set; }

	[Sync]
	public string StatusText { get; set; } =
		"Waiting for players";

	[Sync]
	public TimeUntil TurnExpires { get; set; }

	[Sync]
	public MtgMatchResult Result { get; set; }

	public IReadOnlyList<MtgPlayerSeat> Seats =>
		Scene.GetAllComponents<MtgPlayerSeat>()
			.OrderBy( seat => seat.Index )
			.ToArray();

	public MtgPlayerSeat? LocalSeat =>
		Seats.FirstOrDefault( seat => seat.IsLocal );

	public MtgGameRules Rules =>
		Scene.Get<MtgGameRules>() ??
		throw new InvalidOperationException(
			"The scene has no MtgGameRules component." );

	private readonly Dictionary<Guid, Deck> _submittedDecks = [];
	private bool _handlingTimeout;

	protected override void OnStart()
	{
		Mouse.Visibility = MouseVisibility.Visible;

		if ( Definition is not null )
		{
			CardMesh.SetSize( Definition.CardWidth );
			CardMesh.SetThicknessRatio(
				Definition.CardThicknessRatio );
		}

		if ( !Networking.IsHost )
			return;

		if ( MatchId == Guid.Empty )
			MatchId = Guid.NewGuid();

		foreach ( Connection connection in Connection.All )
			SeatPlayer( connection );
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost ||
			State != MtgMatchState.Playing ||
			Definition is null ||
			Definition.TurnTimeLimit <= 0f ||
			_handlingTimeout ||
			!TurnExpires )
		{
			return;
		}

		_handlingTimeout = true;
		AdvanceTurnAuthority();
		_handlingTimeout = false;
	}

	public Deck? GetSubmittedDeck( Guid playerId ) =>
		_submittedDecks.TryGetValue(
			playerId,
			out Deck? deck )
				? deck
				: null;

	public MtgPlayerSeat? SeatOf( Guid playerId ) =>
		Seats.FirstOrDefault(
			seat => seat.PlayerId == playerId );

	[Rpc.Host]
	public void SubmitDeck( string deckJson )
	{
		if ( !Networking.IsHost ||
			State != MtgMatchState.Lobby ||
			SeatOf( Rpc.CallerId ) is not MtgPlayerSeat seat )
		{
			return;
		}

		try
		{
			Deck deck = DeckJson.Deserialize( deckJson );
			AcceptDeckAuthority( seat, deck );
		}
		catch ( Exception exception )
		{
			seat.DeckAccepted = false;
			seat.Ready = false;
			seat.DeckStatus = exception.Message;
		}
	}

	[Rpc.Host]
	public void SetReady( bool ready )
	{
		if ( !Networking.IsHost ||
			State != MtgMatchState.Lobby ||
			SeatOf( Rpc.CallerId ) is not MtgPlayerSeat seat )
		{
			return;
		}

		seat.Ready = ready && seat.DeckAccepted;
	}

	public bool AcceptDeckAuthority(
		MtgPlayerSeat seat,
		Deck deck )
	{
		if ( !Networking.IsHost ||
			State != MtgMatchState.Lobby )
		{
			return false;
		}

		string formatCode =
			Definition?.FormatCode ??
			deck.FormatCode ??
			"standard";
		DeckValidationReport report =
			DeckValidator.Validate(
				deck,
				Rules.CreateFormat( formatCode ) );

		if ( !report.IsLegal )
		{
			seat.DeckAccepted = false;
			seat.Ready = false;
			seat.DeckStatus =
				report.Issues[0].Message;
			return false;
		}

		_submittedDecks[seat.PlayerId] = deck;
		seat.DeckAccepted = true;
		seat.DeckStatus = $"{deck.Name} accepted";
		return true;
	}

	public void SetReadyAuthority(
		MtgPlayerSeat seat,
		bool ready )
	{
		if ( !Networking.IsHost ||
			State != MtgMatchState.Lobby )
		{
			return;
		}

		seat.Ready = ready && seat.DeckAccepted;
	}

	[Rpc.Host]
	public void RequestStartMatch()
	{
		if ( !Networking.IsHost ||
			Connection.Host is not { } host ||
			Rpc.CallerId != host.Id )
		{
			return;
		}

		StartMatchAuthority();
	}

	public void StartMatchAuthority()
	{
		if ( !Networking.IsHost ||
			State != MtgMatchState.Lobby )
		{
			return;
		}

		MtgPlayerSeat[] players = Seats
			.Where( seat => seat.Occupied )
			.ToArray();
		int minimum = Definition?.MinimumPlayers ?? 2;
		int maximum = Definition?.MaximumPlayers ?? 2;

		if ( players.Length < minimum ||
			players.Length > maximum ||
			players.Any( seat =>
				!seat.Ready || !seat.DeckAccepted ) )
		{
			StatusText =
				"All players need a valid deck and must be ready";
			return;
		}

		State = MtgMatchState.Loading;

		foreach ( MtgPlayerSeat seat in players )
		{
			seat.Life = Definition?.StartingLife ?? 20;
			seat.Poison = 0;
			seat.Outcome = MtgPlayerOutcome.None;
			seat.MulliganCount = 0;
			seat.MulliganComplete = false;
		}

		Rules.SetupMatch( players );
		State = MtgMatchState.Mulligan;
		StatusText = "Choose your opening hand";
	}

	[Rpc.Host]
	public void KeepOpeningHand()
	{
		if ( !Networking.IsHost ||
			State != MtgMatchState.Mulligan ||
			SeatOf( Rpc.CallerId ) is not MtgPlayerSeat seat ||
			seat.MulliganComplete )
		{
			return;
		}

		seat.MulliganComplete = true;
		Rules.OnOpeningHandKept( seat );
		TryFinishMulligans();
	}

	public void KeepOpeningHandAuthority(
		MtgPlayerSeat seat )
	{
		if ( !Networking.IsHost ||
			State != MtgMatchState.Mulligan ||
			seat.MulliganComplete )
		{
			return;
		}

		seat.MulliganComplete = true;
		Rules.OnOpeningHandKept( seat );
		TryFinishMulligans();
	}

	[Rpc.Host]
	public void TakeMulligan()
	{
		if ( !Networking.IsHost ||
			State != MtgMatchState.Mulligan ||
			SeatOf( Rpc.CallerId ) is not MtgPlayerSeat seat ||
			seat.MulliganComplete ||
			!Rules.TryMulligan( seat ) )
		{
			return;
		}

		seat.MulliganCount++;
	}

	public void BeginPlaying()
	{
		if ( !Networking.IsHost ||
			State is not (MtgMatchState.Mulligan or
				MtgMatchState.Loading) )
		{
			return;
		}

		MtgPlayerSeat? first = Seats.FirstOrDefault(
			seat => seat.Occupied && !seat.IsEliminated );

		if ( first is null )
			return;

		State = MtgMatchState.Playing;
		TurnNumber = 0;
		BeginTurnAuthority( first );
	}

	[Rpc.Host]
	public void PassPriority()
	{
		if ( !Networking.IsHost ||
			State != MtgMatchState.Playing ||
			PriorityPlayerId != Rpc.CallerId )
		{
			return;
		}

		MtgPlayerSeat[] players = ActiveSeats();
		PriorityPassCount++;

		if ( PriorityPassCount >= players.Length )
		{
			PriorityPassCount = 0;
			Rules.OnAllPlayersPassedPriority();
			return;
		}

		PriorityPlayerId =
			NextSeat( Rpc.CallerId, players )?.PlayerId ??
			ActivePlayerId;
	}

	[Rpc.Host]
	public void EndTurn()
	{
		if ( !Networking.IsHost ||
			State != MtgMatchState.Playing ||
			SeatOf( Rpc.CallerId ) is not MtgPlayerSeat seat ||
			!Rules.CanEndTurn( seat ) )
		{
			return;
		}

		AdvanceTurnAuthority();
	}

	[Rpc.Host]
	public void Concede()
	{
		if ( !Networking.IsHost ||
			State is not (MtgMatchState.Mulligan or
				MtgMatchState.Playing) ||
			SeatOf( Rpc.CallerId ) is not MtgPlayerSeat seat )
		{
			return;
		}

		seat.Outcome = MtgPlayerOutcome.Conceded;
		seat.MulliganComplete = true;
		ReleasePlayerGrabs( seat.PlayerId );
		MtgPlayerSeat[] remaining = ActiveSeats();

		if ( remaining.Length <= 1 )
			FinishMatch(
				remaining.FirstOrDefault(),
				$"{seat.DisplayName} conceded" );
	}

	[Rpc.Host]
	public void PerformAction( string actionId, long amount = 0 )
	{
		if ( !Networking.IsHost ||
			State != MtgMatchState.Playing ||
			string.IsNullOrWhiteSpace( actionId ) ||
			SeatOf( Rpc.CallerId ) is not MtgPlayerSeat seat )
		{
			return;
		}

		MtgGameAction? offered = Rules.ActionsFor( seat )
			.FirstOrDefault( action =>
				string.Equals(
					action.Id,
					actionId,
					StringComparison.Ordinal ) );

		if ( offered is not MtgGameAction action ||
			action.Disabled )
		{
			return;
		}

		Rules.TryPerformAction( seat, actionId, amount );
	}

	[Rpc.Host]
	public void RequestMoveCard(
		CardObject card,
		Guid destinationZoneId,
		Transform freeformPose )
	{
		if ( !Networking.IsHost ||
			State != MtgMatchState.Playing ||
			card is null ||
			SeatOf( Rpc.CallerId ) is not MtgPlayerSeat seat ||
			ZoneObject.Find( destinationZoneId ) is not
				ZoneObject destination )
		{
			return;
		}

		ZoneObject? source = ZoneObject.Find( card.ZoneId );

		if ( card.GrabbedByPlayerId != Guid.Empty &&
			card.GrabbedByPlayerId != seat.PlayerId )
		{
			return;
		}

		if ( !Rules.CanMoveCard(
			seat,
			card,
			source,
			destination ) )
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
		if ( !Networking.IsHost ||
			State != MtgMatchState.Playing ||
			card is null ||
			card.GrabbedByPlayerId != Guid.Empty ||
			SeatOf( Rpc.CallerId ) is not MtgPlayerSeat seat )
		{
			return;
		}

		ZoneObject? source = ZoneObject.Find( card.ZoneId );

		if ( source is null ||
			!Rules.CanMoveCard( seat, card, source, source ) )
		{
			return;
		}

		card.GrabbedByPlayerId = seat.PlayerId;
	}

	[Rpc.Host]
	public void ReleaseGrab( CardObject card )
	{
		if ( !Networking.IsHost ||
			card is null ||
			card.GrabbedByPlayerId != Rpc.CallerId )
		{
			return;
		}

		card.GrabbedByPlayerId = Guid.Empty;
	}

	[Rpc.Host]
	public void RequestFlipCard( CardObject card )
	{
		if ( !Networking.IsHost ||
			card is null ||
			SeatOf( Rpc.CallerId ) is not MtgPlayerSeat seat ||
			!Rules.CanFlipCard( seat, card ) )
		{
			return;
		}

		card.FlipPrintedFace();
	}

	[Rpc.Host]
	public void RequestThrowCard(
		CardObject card,
		Vector3 velocity,
		Vector3 angularVelocity )
	{
		if ( !Networking.IsHost ||
			State != MtgMatchState.Playing ||
			card is null ||
			SeatOf( Rpc.CallerId ) is not MtgPlayerSeat seat )
		{
			return;
		}

		ZoneObject? source = ZoneObject.Find( card.ZoneId );

		if ( source is null ||
			!Rules.CanMoveCard( seat, card, source, source ) )
		{
			return;
		}

		card.GrabbedByPlayerId = Guid.Empty;
		card.Throw( velocity, angularVelocity );
	}

	[Rpc.Host]
	public void RequestTapCard( CardObject card )
	{
		if ( !Networking.IsHost ||
			card is null ||
			SeatOf( Rpc.CallerId ) is not MtgPlayerSeat seat ||
			!Rules.CanTapCard( seat, card ) )
		{
			return;
		}

		card.SetTapped( !card.Tapped );
	}

	public void SetPhase( MtgTurnPhase phase )
	{
		if ( !Networking.IsHost ||
			State != MtgMatchState.Playing )
		{
			return;
		}

		MtgTurnPhase previous = Phase;
		Phase = phase;
		PriorityPassCount = 0;
		PriorityPlayerId = ActivePlayerId;
		StatusText = phase.ToString();
		Rules.OnPhaseChanged( previous, phase );
	}

	public void FinishMatch(
		MtgPlayerSeat? winner,
		string detail = "" )
	{
		if ( !Networking.IsHost )
			return;

		State = MtgMatchState.Finished;

		foreach ( MtgPlayerSeat seat in Seats )
		{
			if ( !seat.Occupied )
				continue;

			seat.Outcome = winner is null
				? MtgPlayerOutcome.Draw
				: ReferenceEquals( seat, winner )
					? MtgPlayerOutcome.Won
					: MtgPlayerOutcome.Lost;
		}

		Result = new MtgMatchResult(
			winner is null
				? "Draw"
				: $"{winner.DisplayName} wins",
			detail,
			winner is null
				? MtgMatchResultTone.Draw
				: MtgMatchResultTone.Neutral );
		StatusText = Result.Title;
	}

	public bool SeatPlayer( Connection connection )
	{
		if ( !Networking.IsHost ||
			connection is null )
		{
			return false;
		}

		if ( SeatOf( connection.Id ) is MtgPlayerSeat existing )
		{
			existing.Connected = true;
			return true;
		}

		int maximum = Definition?.MaximumPlayers ?? 2;
		int index = Enumerable.Range( 0, maximum )
			.FirstOrDefault( candidate =>
				Seats.All( seat =>
					seat.Index != candidate ) );

		if ( Seats.Count >= maximum )
			return false;

		var seatObject = new GameObject(
			GameObject,
			true,
			$"MTG Seat {index + 1}" );
		var seat = seatObject.Components
			.Create<MtgPlayerSeat>();
		seat.Index = index;
		seat.PlayerId = connection.Id;
		seat.Connected = true;
		seat.Life = Definition?.StartingLife ?? 20;

		if ( Networking.IsActive )
			seatObject.NetworkSpawn();

		return true;
	}

	public MtgPlayerSeat? AddBotPlayer(
		string displayName = "Sparky" )
	{
		if ( !Networking.IsHost ||
			State != MtgMatchState.Lobby )
		{
			return null;
		}

		int maximum = Definition?.MaximumPlayers ?? 2;

		if ( Seats.Count >= maximum )
			return null;

		int index = Enumerable.Range( 0, maximum )
			.First( candidate =>
				Seats.All( seat =>
					seat.Index != candidate ) );
		var seatObject = new GameObject(
			GameObject,
			true,
			$"MTG Bot Seat {index + 1}" );
		var seat = seatObject.Components
			.Create<MtgPlayerSeat>();
		seat.Index = index;
		seat.PlayerId = Guid.NewGuid();
		seat.Connected = true;
		seat.IsBot = true;
		seat.BotName = displayName;
		seat.Life = Definition?.StartingLife ?? 20;

		if ( Networking.IsActive )
			seatObject.NetworkSpawn();

		return seat;
	}

	public void RemovePlayer( Connection connection )
	{
		if ( !Networking.IsHost ||
			SeatOf( connection.Id ) is not MtgPlayerSeat seat )
		{
			return;
		}

		seat.Connected = false;
		seat.Ready = false;
		ReleasePlayerGrabs( seat.PlayerId );
		Rules.OnPlayerDisconnected( seat );
	}

	void Component.INetworkListener.OnActive(
		Connection channel ) =>
		SeatPlayer( channel );

	void Component.INetworkListener.OnDisconnected(
		Connection channel ) =>
		RemovePlayer( channel );

	private void BeginTurnAuthority( MtgPlayerSeat player )
	{
		TurnNumber++;
		ActivePlayerId = player.PlayerId;
		PriorityPlayerId = player.PlayerId;
		PriorityPassCount = 0;
		Phase = MtgTurnPhase.Beginning;
		StatusText =
			$"{player.DisplayName}'s turn";

		if ( Definition?.TurnTimeLimit > 0f )
			TurnExpires = Definition.TurnTimeLimit;

		Rules.OnTurnStarted( player );
	}

	private void AdvanceTurnAuthority()
	{
		MtgPlayerSeat[] players = ActiveSeats();
		MtgPlayerSeat? next =
			NextSeat( ActivePlayerId, players );

		if ( next is not null )
			BeginTurnAuthority( next );
	}

	private void TryFinishMulligans()
	{
		MtgPlayerSeat[] players = ActiveSeats();

		if ( players.Length > 0 &&
			players.All( seat => seat.MulliganComplete ) )
		{
			BeginPlaying();
		}
	}

	private MtgPlayerSeat[] ActiveSeats() => Seats
		.Where( seat =>
			seat.Occupied && !seat.IsEliminated )
		.ToArray();

	private static MtgPlayerSeat? NextSeat(
		Guid current,
		IReadOnlyList<MtgPlayerSeat> players )
	{
		if ( players.Count == 0 )
			return null;

		int index = -1;

		for ( int candidate = 0;
			candidate < players.Count;
			candidate++ )
		{
			if ( players[candidate].PlayerId == current )
			{
				index = candidate;
				break;
			}
		}

		return players[(index + 1) % players.Count];
	}

	private void ReleasePlayerGrabs( Guid playerId )
	{
		foreach ( CardObject card
			in Scene.GetAllComponents<CardObject>() )
		{
			if ( card.GrabbedByPlayerId == playerId )
				card.GrabbedByPlayerId = Guid.Empty;
		}
	}
}
