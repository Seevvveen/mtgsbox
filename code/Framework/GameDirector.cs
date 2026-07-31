#nullable enable

using Sandbox.Classes.DeckValidation;
using System;
namespace Sandbox.Classes;

/// <summary>
///     Host Authority Coordinator
///     Dictates the source of truth between host and clients
/// </summary>
public sealed class GameDirector : Component, Component.INetworkListener
{
	private readonly  Dictionary<Guid, Deck> _submittedDecks = [ ];
	private           bool                   _handlingTimeout;
	[Property] public GameFormat?            Definition { get; set; }
	[Sync]     public Guid                   MatchId    { get; set; }

	[Sync] public GameState State { get; set; } = GameState.Lobby;

	[Sync] public int TurnNumber { get; set; }

	[Sync] public TurnPhase Phase { get; set; } = TurnPhase.Beginning;

	[Sync] public Guid ActivePlayerId { get; set; }

	[Sync] public Guid PriorityPlayerId { get; set; }

	[Sync] public int PriorityPassCount { get; set; }

	[Sync] public string StatusText { get; set; } = "Waiting for players";

	[Sync] public TimeUntil TurnExpires { get; set; }

	[Sync] public MatchResult Result { get; set; }


	//
	// Helpers
	//

	public IReadOnlyList<PlayerSeat> Seats
	{
		get { return Scene.GetAllComponents<PlayerSeat>().OrderBy( seat => seat.Index ).ToArray(); }
	}

	public PlayerSeat? LocalSeat
	{
		get { return Seats.FirstOrDefault( seat => seat.IsLocal ); }
	}

	public RulesEngine RulesEngine
	{
		get { return Scene.Get<RulesEngine>() ?? throw new InvalidOperationException( "The scene has no MtgGameRules component." ); }
	}


	void INetworkListener.OnActive( Connection channel ) { SeatPlayer( channel ); }


	void INetworkListener.OnDisconnected( Connection channel ) { RemovePlayer( channel ); }





	protected override void OnStart()
	{
		Mouse.Visibility = MouseVisibility.Visible;

		if ( Definition is not null )
		{
			CardMesh.SetSize( Definition.CardWidth );
			CardMesh.SetThicknessRatio( Definition.CardThicknessRatio );
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
		if ( !Networking.IsHost || State != GameState.Playing || Definition is null || Definition.TurnTimeLimit <= 0f || _handlingTimeout || !TurnExpires )
			return;

		_handlingTimeout = true;
		AdvanceTurnAuthority();
		_handlingTimeout = false;
	}


	public Deck? GetSubmittedDeck( Guid playerId ) { return _submittedDecks.TryGetValue( playerId, out Deck? deck )? deck : null; }


	public PlayerSeat? SeatOf( Guid playerId ) { return Seats.FirstOrDefault( seat => seat.PlayerId == playerId ); }


	[Rpc.Host]
	public void SubmitDeck( string deckJson )
	{
		if ( !Networking.IsHost || State != GameState.Lobby || SeatOf( Rpc.CallerId ) is not PlayerSeat seat )
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


	[Rpc.Host]
	public void SetReady( bool ready )
	{
		if ( !Networking.IsHost || State != GameState.Lobby || SeatOf( Rpc.CallerId ) is not PlayerSeat seat )
			return;

		seat.Ready = ready && seat.DeckAccepted;
	}


	public bool AcceptDeckAuthority( PlayerSeat seat, Deck deck )
	{
		if ( !Networking.IsHost || State != GameState.Lobby )
			return false;

		string formatCode = Definition?.FormatCode ?? deck.FormatCode ?? "standard";

		DeckValidationReport report = DeckValidator.Validate( deck, RulesEngine.CreateFormat( formatCode ) );

		if ( !report.IsLegal )
		{
			seat.DeckAccepted = false;
			seat.Ready        = false;

			seat.DeckStatus = report.Issues[0].Message;

			return false;
		}

		_submittedDecks[seat.PlayerId] = deck;
		seat.DeckAccepted              = true;
		seat.DeckStatus                = $"{deck.Name} accepted";

		return true;
	}


	public void SetReadyAuthority( PlayerSeat seat, bool ready )
	{
		if ( !Networking.IsHost || State != GameState.Lobby )
			return;

		seat.Ready = ready && seat.DeckAccepted;
	}


	[Rpc.Host]
	public void RequestStartMatch()
	{
		if ( !Networking.IsHost || Connection.Host is not { } host || Rpc.CallerId != host.Id )
			return;

		StartMatchAuthority();
	}


	public void StartMatchAuthority()
	{
		if ( !Networking.IsHost || State != GameState.Lobby )
			return;

		PlayerSeat[] players = Seats.Where( seat => seat.Occupied ).ToArray();

		int minimum = Definition?.MinimumPlayers ?? 2;
		int maximum = Definition?.MaximumPlayers ?? 2;

		if ( players.Length < minimum || players.Length > maximum || players.Any( seat => !seat.Ready || !seat.DeckAccepted ) )
		{
			StatusText = "All players need a valid deck and must be ready";

			return;
		}

		State = GameState.Loading;

		foreach ( PlayerSeat seat in players )
		{
			seat.Life             = Definition?.StartingLife ?? 20;
			seat.Poison           = 0;
			seat.Outcome          = PlayerOutcome.None;
			seat.MulliganCount    = 0;
			seat.MulliganComplete = false;
		}

		RulesEngine.SetupMatch( players );
		State      = GameState.Mulligan;
		StatusText = "Choose your opening hand";
	}


	[Rpc.Host]
	public void KeepOpeningHand()
	{
		if ( !Networking.IsHost || State != GameState.Mulligan || SeatOf( Rpc.CallerId ) is not PlayerSeat seat || seat.MulliganComplete )
			return;

		seat.MulliganComplete = true;
		RulesEngine.OnOpeningHandKept( seat );
		TryFinishMulligans();
	}


	public void KeepOpeningHandAuthority( PlayerSeat seat )
	{
		if ( !Networking.IsHost || State != GameState.Mulligan || seat.MulliganComplete )
			return;

		seat.MulliganComplete = true;
		RulesEngine.OnOpeningHandKept( seat );
		TryFinishMulligans();
	}


	[Rpc.Host]
	public void TakeMulligan()
	{
		if ( !Networking.IsHost || State != GameState.Mulligan || SeatOf( Rpc.CallerId ) is not PlayerSeat seat || seat.MulliganComplete || !RulesEngine.TryMulligan( seat ) )
			return;

		seat.MulliganCount++;
	}


	public void BeginPlaying()
	{
		if ( !Networking.IsHost || State is not (GameState.Mulligan or GameState.Loading) )
			return;

		PlayerSeat? first = Seats.FirstOrDefault( seat => seat.Occupied && !seat.IsEliminated );

		if ( first is null )
			return;

		State      = GameState.Playing;
		TurnNumber = 0;
		BeginTurnAuthority( first );
	}


	[Rpc.Host]
	public void PassPriority()
	{
		if ( !Networking.IsHost || State != GameState.Playing || PriorityPlayerId != Rpc.CallerId )
			return;

		PlayerSeat[] players = ActiveSeats();
		PriorityPassCount++;

		if ( PriorityPassCount >= players.Length )
		{
			PriorityPassCount = 0;
			RulesEngine.OnAllPlayersPassedPriority();

			return;
		}

		PriorityPlayerId = NextSeat( Rpc.CallerId, players )?.PlayerId ?? ActivePlayerId;
	}


	[Rpc.Host]
	public void EndTurn()
	{
		if ( !Networking.IsHost || State != GameState.Playing || SeatOf( Rpc.CallerId ) is not PlayerSeat seat || !RulesEngine.CanEndTurn( seat ) )
			return;

		AdvanceTurnAuthority();
	}


	[Rpc.Host]
	public void Concede()
	{
		if ( !Networking.IsHost || State is not (GameState.Mulligan or GameState.Playing) || SeatOf( Rpc.CallerId ) is not PlayerSeat seat )
			return;

		seat.Outcome          = PlayerOutcome.Conceded;
		seat.MulliganComplete = true;
		ReleasePlayerGrabs( seat.PlayerId );
		PlayerSeat[] remaining = ActiveSeats();

		if ( remaining.Length <= 1 )
			FinishMatch( remaining.FirstOrDefault(), $"{seat.DisplayName} conceded" );
	}


	[Rpc.Host]
	public void PerformAction( string actionId, long amount = 0 )
	{
		if ( !Networking.IsHost || State != GameState.Playing || string.IsNullOrWhiteSpace( actionId ) || SeatOf( Rpc.CallerId ) is not PlayerSeat seat )
			return;

		GameAction? offered = RulesEngine.ActionsFor( seat ).FirstOrDefault( action => string.Equals( action.Id, actionId, StringComparison.Ordinal ) );

		if ( offered is not GameAction action || action.Disabled )
			return;

		RulesEngine.TryPerformAction( seat, actionId, amount );
	}


	[Rpc.Host]
	public void RequestMoveCard( CardObject card, Guid destinationZoneId, Transform freeformPose )
	{
		if ( !Networking.IsHost || State != GameState.Playing || card is null || SeatOf( Rpc.CallerId ) is not PlayerSeat seat || ZoneObject.Find( destinationZoneId ) is not ZoneObject destination )
			return;

		ZoneObject? source = ZoneObject.Find( card.ZoneId );

		if ( card.GrabbedByPlayerId != Guid.Empty && card.GrabbedByPlayerId != seat.PlayerId )
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
		if ( !Networking.IsHost || State != GameState.Playing || card is null || card.GrabbedByPlayerId != Guid.Empty || SeatOf( Rpc.CallerId ) is not PlayerSeat seat )
			return;

		ZoneObject? source = ZoneObject.Find( card.ZoneId );

		if ( source is null || !RulesEngine.CanMoveCard( seat, card, source, source ) )
			return;

		card.GrabbedByPlayerId = seat.PlayerId;
	}


	[Rpc.Host]
	public void ReleaseGrab( CardObject card )
	{
		if ( !Networking.IsHost || card is null || card.GrabbedByPlayerId != Rpc.CallerId )
			return;

		card.GrabbedByPlayerId = Guid.Empty;
	}


	[Rpc.Host]
	public void RequestFlipCard( CardObject card )
	{
		if ( !Networking.IsHost || card is null || SeatOf( Rpc.CallerId ) is not PlayerSeat seat || !RulesEngine.CanFlipCard( seat, card ) )
			return;

		card.FlipPrintedFace();
	}


	[Rpc.Host]
	public void RequestThrowCard( CardObject card, Vector3 velocity, Vector3 angularVelocity )
	{
		if ( !Networking.IsHost || State != GameState.Playing || card is null || SeatOf( Rpc.CallerId ) is not PlayerSeat seat )
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
		if ( !Networking.IsHost || card is null || SeatOf( Rpc.CallerId ) is not PlayerSeat seat || !RulesEngine.CanTapCard( seat, card ) )
			return;

		card.SetTapped( !card.Tapped );
	}


	public void SetPhase( TurnPhase phase )
	{
		if ( !Networking.IsHost || State != GameState.Playing )
			return;

		TurnPhase previous = Phase;
		Phase             = phase;
		PriorityPassCount = 0;
		PriorityPlayerId  = ActivePlayerId;
		StatusText        = phase.ToString();
		RulesEngine.OnPhaseChanged( previous, phase );
	}


	public void FinishMatch( PlayerSeat? winner, string detail = "" )
	{
		if ( !Networking.IsHost )
			return;

		State = GameState.Finished;

		foreach ( PlayerSeat seat in Seats )
		{
			if ( !seat.Occupied )
				continue;

			seat.Outcome = winner is null? PlayerOutcome.Draw : ReferenceEquals( seat, winner )? PlayerOutcome.Won : PlayerOutcome.Lost;
		}

		Result = new MatchResult( winner is null? "Draw" : $"{winner.DisplayName} wins", detail, winner is null? MatchResultTone.Draw : MatchResultTone.Neutral );

		StatusText = Result.Title;
	}


	public bool SeatPlayer( Connection connection )
	{
		if ( !Networking.IsHost || connection is null )
			return false;

		if ( SeatOf( connection.Id ) is PlayerSeat existing )
		{
			existing.Connected = true;

			return true;
		}

		int maximum = Definition?.MaximumPlayers ?? 2;

		int index = Enumerable.Range( 0, maximum ).FirstOrDefault( candidate => Seats.All( seat => seat.Index != candidate ) );

		if ( Seats.Count >= maximum )
			return false;

		GameObject seatObject = new GameObject( GameObject, true, $"MTG Seat {index + 1}" );

		PlayerSeat? seat = seatObject.Components.Create<PlayerSeat>();

		seat.Index     = index;
		seat.PlayerId  = connection.Id;
		seat.Connected = true;
		seat.Life      = Definition?.StartingLife ?? 20;

		if ( Networking.IsActive )
			seatObject.NetworkSpawn();

		return true;
	}


	public PlayerSeat? AddBotPlayer( string displayName = "Sparky" )
	{
		if ( !Networking.IsHost || State != GameState.Lobby )
			return null;

		int maximum = Definition?.MaximumPlayers ?? 2;

		if ( Seats.Count >= maximum )
			return null;

		int index = Enumerable.Range( 0, maximum ).First( candidate => Seats.All( seat => seat.Index != candidate ) );

		GameObject seatObject = new GameObject( GameObject, true, $"MTG Bot Seat {index + 1}" );

		PlayerSeat? seat = seatObject.Components.Create<PlayerSeat>();

		seat.Index     = index;
		seat.PlayerId  = Guid.NewGuid();
		seat.Connected = true;
		seat.IsBot     = true;
		seat.BotName   = displayName;
		seat.Life      = Definition?.StartingLife ?? 20;

		if ( Networking.IsActive )
			seatObject.NetworkSpawn();

		return seat;
	}


	public void RemovePlayer( Connection connection )
	{
		if ( !Networking.IsHost || SeatOf( connection.Id ) is not PlayerSeat seat )
			return;

		seat.Connected = false;
		seat.Ready     = false;
		ReleasePlayerGrabs( seat.PlayerId );
		RulesEngine.OnPlayerDisconnected( seat );
	}


	private void BeginTurnAuthority( PlayerSeat player )
	{
		TurnNumber++;
		ActivePlayerId    = player.PlayerId;
		PriorityPlayerId  = player.PlayerId;
		PriorityPassCount = 0;
		Phase             = TurnPhase.Beginning;

		StatusText = $"{player.DisplayName}'s turn";

		if ( Definition?.TurnTimeLimit > 0f )
			TurnExpires = Definition.TurnTimeLimit;

		RulesEngine.OnTurnStarted( player );
	}


	private void AdvanceTurnAuthority()
	{
		PlayerSeat[] players = ActiveSeats();

		PlayerSeat? next = NextSeat( ActivePlayerId, players );

		if ( next is not null )
			BeginTurnAuthority( next );
	}


	private void TryFinishMulligans()
	{
		PlayerSeat[] players = ActiveSeats();

		if ( players.Length > 0 && players.All( seat => seat.MulliganComplete ) )
			BeginPlaying();
	}


	private PlayerSeat[] ActiveSeats() { return Seats.Where( seat => seat.Occupied && !seat.IsEliminated ).ToArray(); }


	private static PlayerSeat? NextSeat( Guid current, IReadOnlyList<PlayerSeat> players )
	{
		if ( players.Count == 0 )
			return null;

		int index = -1;

		for ( int candidate = 0; candidate < players.Count; candidate++ )
		{
			if ( players[candidate].PlayerId == current )
			{
				index = candidate;

				break;
			}
		}

		return players[( index + 1 ) % players.Count];
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
