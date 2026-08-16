#nullable enable

using Sandbox.Classes.Cards;
using Sandbox.Classes.Database;
using Sandbox.Classes.Deck;
using Sandbox.Classes.Zones;
using Sandbox.Framework.GameInfo;
using Sandbox.Framework.Rules;
using Sandbox.Framework.Table;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sandbox.Framework;

/// <summary>
///     Holds the active game and its player seats.
/// </summary>
public class Match : Component
{
	private const int MaximumDeckPayloadLength = 512_000;
	private readonly HashSet<Guid> _databaseVerifiedPlayers = [ ];
	private RulesEngine? _rules;
	private GameFlowCoordinator? _flow;
	private GameActionExecutor? _executor;
	private Task? _databaseHandshakeTask;
	private string _databaseHandshakeTarget = string.Empty;

	[Sync( SyncFlags.FromHost )] public GameState State { get; private set; } = GameState.Lobby;
	[Sync( SyncFlags.FromHost )] public string StatusText { get; private set; } = "Waiting for players";
	[Sync( SyncFlags.FromHost )] public int LobbyRevision { get; private set; }
	[Sync( SyncFlags.FromHost )] public string RulesProfile { get; private set; } = string.Empty;
	[Sync( SyncFlags.FromHost )] public string RequiredDatabaseChecksum { get; private set; } = string.Empty;
	[Sync( SyncFlags.FromHost )] public string RequiredDatabaseSource { get; private set; } = string.Empty;

	[Sync( SyncFlags.FromHost )] public int TurnNumber { get; internal set; }
	[Sync( SyncFlags.FromHost )] public TurnPhase Phase { get; internal set; } = TurnPhase.Beginning;
	[Sync( SyncFlags.FromHost )] public TurnStep Step { get; internal set; } = TurnStep.Untap;
	[Sync( SyncFlags.FromHost )] public Guid ActivePlayerId { get; internal set; }
	[Sync( SyncFlags.FromHost )] public Guid PriorityPlayerId { get; internal set; }
	[Sync( SyncFlags.FromHost )] public int ConsecutivePasses { get; internal set; }
	[Sync( SyncFlags.FromHost )] public int StackCount { get; internal set; }
	[Sync( SyncFlags.FromHost )] public bool HasPendingChoice { get; internal set; }

	public IReadOnlyList<Seat> Seats => Scene.GetAllComponents<Seat>()
		.Where( seat => seat.GameObject.Parent == GameObject )
		.OrderBy( seat => seat.Index )
		.ToArray();

	public RulesEngine Rules
	{
		get
		{
			EnsureRuntime();
			return _rules!;
		}
	}

	public GameFlowCoordinator Flow
	{
		get
		{
			EnsureRuntime();
			return _flow!;
		}
	}


	protected override void OnStart()
	{
		base.OnStart();
		GetOrAddComponent<TableAnchor>();

		if ( Networking.IsHost )
		{
			EnsureRuntime();
			_databaseHandshakeTask = PublishRequiredDatabaseChecksumAsync();
		}
	}


	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( Networking.IsHost || string.IsNullOrWhiteSpace( RequiredDatabaseChecksum ) )
			return;

		if ( string.Equals( _databaseHandshakeTarget, RequiredDatabaseChecksum, StringComparison.Ordinal ) )
			return;

		_databaseHandshakeTarget = RequiredDatabaseChecksum;
		_databaseHandshakeTask = VerifyLocalDatabaseAsync( RequiredDatabaseChecksum );
	}


	public void AddPlayer( Connection channel )
	{
		if ( !Networking.IsHost )
			return;

		if ( Seats.FirstOrDefault( seat => seat.Player == channel.Id ) is { } existing )
		{
			existing.IsConnected = true;
			return;
		}

		EnsureRuntime();
		RuleEvaluation join = Rules.CanJoin( this, channel, Seats );

		if ( join.Verdict == RuleVerdict.Deny )
		{
			Log.Warning( $"Could not seat {channel.DisplayName}: {join.Message}" );
			return;
		}

		int maximumPlayers = Scene.Get<GameDirector>()?.FormatFile?.MaximumPlayers ?? byte.MaxValue;

		int index = Enumerable.Range( 0, maximumPlayers )
			.First( candidate => Seats.All( seat => seat.Index != candidate ) );

		GameObject seatObject = new( GameObject, true, $"{channel.DisplayName} Seat" );
		Seat seat = seatObject.Components.Create<Seat>();
		seat.Index  = index;
		seat.Player = channel.Id;
		seat.PlayerName = channel.DisplayName;
		seat.IsConnected = true;
		seat.CreateZones();

		LayoutSeats();

		if ( Networking.IsActive )
			seatObject.NetworkSpawn();

		RefreshLobbyStatus();
	}


	private void LayoutSeats( Seat? excluded = null )
	{
		TableAnchor table = Scene.Get<TableAnchor>() ?? GetOrAddComponent<TableAnchor>();
		IReadOnlyList<Seat> seats = Seats
			.Where( seat => !ReferenceEquals( seat, excluded ) )
			.ToArray();

		for ( int position = 0; position < seats.Count; position++ )
		{
			Transform spot = table.PlayerSpot( position, seats.Count );
			seats[position].WorldPosition = spot.Position;
			seats[position].WorldRotation = spot.Rotation;
		}
	}


	public void RemovePlayer( Connection channel )
	{
		if ( !Networking.IsHost )
			return;

		Seat? seat = Seats.FirstOrDefault( candidate => candidate.Player == channel.Id );

		if ( seat is null )
			return;

		seat.IsConnected = false;
		seat.Ready = false;
		seat.SelectedCard = null;
		_databaseVerifiedPlayers.Remove( channel.Id );

		if ( State is GameState.Mulligan or GameState.Playing )
		{
			EnsureRuntime();
			_executor!.Execute( RuleDecision.Permit( new ConcedeCommand( seat ) ), seat );

			if ( State == GameState.Playing && ActivePlayerId == seat.Player )
				Flow.Turns.EndTurn();
			else if ( State == GameState.Playing && PriorityPlayerId == seat.Player )
				Flow.Priority.Begin();

			return;
		}

		seat.GameObject.Destroy();
		LayoutSeats( seat );

		if ( State == GameState.Lobby )
			RefreshLobbyStatus( seat );
	}


	/// <summary>
	///     Sends a deck to the host. Only lobby metadata is replicated; the full
	///     deck remains authoritative on the host-side seat.
	/// </summary>
	[Rpc.Host]
	public void SubmitDeck( string deckJson )
	{
		if ( State != GameState.Lobby )
			return;

		Seat? seat = SeatFor( Rpc.Caller );

		if ( seat is null )
			return;

		seat.Ready = false;
		seat.HasSubmittedDeck = false;
		seat.DeckName = string.Empty;
		seat.DeckCardCount = 0;
		seat.DeckError = string.Empty;
		seat.SubmittedDeck = null;

		try
		{
			if ( !IsDatabaseVerified( Rpc.Caller ) )
				throw new InvalidOperationException( "Your card database has not been verified against the host yet." );

			if ( string.IsNullOrWhiteSpace( deckJson ) || deckJson.Length > MaximumDeckPayloadLength )
				throw new InvalidOperationException( "The submitted deck is empty or too large." );

			Deck deck = DeckJson.Deserialize( deckJson );
			EnsureRuntime();
			RuleEvaluation submission = Rules.CanSubmitDeck( this, seat, deck );

			if ( submission.Verdict == RuleVerdict.Deny )
				throw new InvalidOperationException( submission.Message );

			var validation = Rules.ValidateDeck( deck );

			if ( !validation.IsLegal )
				throw new InvalidOperationException( validation.Issues[0].Message );

			seat.SubmittedDeck = deck;
			seat.HasSubmittedDeck = true;
			seat.DeckName = deck.Name;
			seat.DeckCardCount = deck.Entries.Sum( entry => entry.Quantity );
		}
		catch ( Exception exception )
		{
			seat.DeckError = exception.Message;
		}

		RefreshLobbyStatus();
	}


	/// <summary>
	///     Updates the calling player's ready state. A player cannot ready up
	///     until the host has accepted their deck.
	/// </summary>
	[Rpc.Host]
	public void SetReady( bool ready )
	{
		Seat? seat = SeatFor( Rpc.Caller );

		if ( seat is null )
			return;

		if ( ready && !IsDatabaseVerified( Rpc.Caller ) )
		{
			seat.DeckError = "Your card database has not been verified against the host yet.";
			RefreshLobbyStatus();
			return;
		}

		EnsureRuntime();
		RuleEvaluation readiness = Rules.CanSetReady( this, seat, ready );

		if ( readiness.Verdict == RuleVerdict.Deny )
		{
			seat.DeckError = readiness.Message;

			if ( State == GameState.Lobby )
				RefreshLobbyStatus();

			return;
		}

		seat.DeckError = string.Empty;
		seat.Ready = ready;
		RefreshLobbyStatus();
		TryBeginWhenReady();
	}


	public void Begin()
	{
		if ( !Networking.IsHost )
			return;

		EnsureRuntime();

		if ( Seats.Any( seat => seat.Occupied && !IsPlayerDatabaseVerified( seat.Player ) ) )
			return;

		if ( Scene.Get<GameDirector>()?.IsMultiplayer == true && Rules.CanBegin( this, Seats ).Verdict == RuleVerdict.Deny )
			return;

		State = GameState.Loading;
		StatusText = "Preparing the table";
		LobbyRevision++;

		Rules.SetupMatch( Seats );

		State = GameState.Playing;
		StatusText = "Match in progress";
		LobbyRevision++;
		Flow.Start();

		if ( Networking.IsActive )
			Networking.SetData( "state", "in_progress" );
	}


	private async Task PublishRequiredDatabaseChecksumAsync()
	{
		DatabaseManager? database = Scene.Get<DatabaseManager>();

		if ( database is null )
		{
			Log.Error( "The match cannot publish a database checksum because no DatabaseManager exists in the scene." );
			return;
		}

		await database.Completion;

		if ( !database.IsReady || string.IsNullOrWhiteSpace( database.DatabaseChecksum ) )
		{
			Log.Error( $"The host card database is unavailable: {database.FailureReason ?? database.StatusMessage}" );
			return;
		}

		RequiredDatabaseChecksum = database.DatabaseChecksum;
		RequiredDatabaseSource = database.SourceSnapshot is null
			? string.Empty
			: JsonSerializer.Serialize( database.SourceSnapshot, DatabaseFileInfo.DatabaseJsonOptions );
		_databaseVerifiedPlayers.Add( Connection.Local.Id );

		if ( Networking.IsActive )
			Networking.SetData( "card_database_checksum", RequiredDatabaseChecksum );

		Log.Info( $"Host card database generation is {RequiredDatabaseChecksum}." );
	}


	private async Task VerifyLocalDatabaseAsync( string expectedChecksum )
	{
		DatabaseManager? database = Scene.Get<DatabaseManager>();

		if ( database is null )
		{
			Log.Error( "Cannot verify the local card database because no DatabaseManager exists in the scene." );
			return;
		}

		await database.Completion;

		// A startup/schema failure is not a generation mismatch. Do not launch a
		// second identical build after provisioning has already failed.
		if ( !database.IsReady )
		{
			Log.Error( $"Local card database startup failed: {database.FailureReason ?? database.StatusMessage}" );
			return;
		}

		DatabaseSourceSnapshot? source = null;

		if ( !string.IsNullOrWhiteSpace( RequiredDatabaseSource ) )
		{
			try
			{
				source = JsonSerializer.Deserialize<DatabaseSourceSnapshot>( RequiredDatabaseSource, DatabaseFileInfo.DatabaseJsonOptions );
			}
			catch ( JsonException exception )
			{
				Log.Warning( $"The host supplied an invalid card source descriptor: {exception.Message}" );
			}
		}

		await database.EnsureHostGenerationAsync( expectedChecksum, source );

		if ( database.IsReady && string.Equals( database.DatabaseChecksum, expectedChecksum, StringComparison.Ordinal ) )
			ReportDatabaseChecksum( expectedChecksum );
	}


	[Rpc.Host]
	private void ReportDatabaseChecksum( string checksum )
	{
		Seat? seat = SeatFor( Rpc.Caller );

		if ( seat is null )
			return;

		if ( string.Equals( checksum, RequiredDatabaseChecksum, StringComparison.Ordinal ) )
		{
			_databaseVerifiedPlayers.Add( Rpc.Caller.Id );
			seat.DeckError = string.Empty;
			Log.Info( $"Verified card database generation for {seat.PlayerName}." );
		}
		else
		{
			_databaseVerifiedPlayers.Remove( Rpc.Caller.Id );
			seat.Ready = false;
			seat.DeckError = "Your card database differs from the host.";
		}

		RefreshLobbyStatus();
	}


	private bool IsDatabaseVerified( Connection connection )
	{
		return IsPlayerDatabaseVerified( connection.Id );
	}


	private bool IsPlayerDatabaseVerified( Guid playerId )
	{
		if ( Networking.IsHost && playerId == Connection.Local.Id )
		{
			DatabaseManager? database = Scene.Get<DatabaseManager>();

			return database?.IsReady == true &&
			       (string.IsNullOrWhiteSpace( RequiredDatabaseChecksum ) ||
			        string.Equals( database.DatabaseChecksum, RequiredDatabaseChecksum, StringComparison.Ordinal ));
		}

		return _databaseVerifiedPlayers.Contains( playerId );
	}


	public void Conclude( Seat? winner = null, string detail = "" )
	{
		if ( !Networking.IsHost )
			return;

		_flow?.Stop();

		foreach ( CardObject card in GameObject.GetComponentsInChildren<CardObject>() )
			card.GrabbedByPlayerId = Guid.Empty;

		foreach ( Seat seat in Seats.Where( seat => seat.Occupied ) )
		{
			seat.SelectedCard = null;

			if ( winner is null )
				seat.Outcome = seat.Eliminated? SeatOutcome.Loss : SeatOutcome.None;
			else
				seat.Outcome = ReferenceEquals( seat, winner )? SeatOutcome.Win : SeatOutcome.Loss;
		}

		State = GameState.Finished;
		StatusText = winner is null? "Match finished" : $"{winner.PlayerName} wins";

		if ( detail.Length > 0 )
			Log.Info( detail );

		LobbyRevision++;
	}


	[Rpc.Host]
	public void RequestSelectCard( CardObject? card )
	{
		ProcessIntent( caller => new SelectCardIntent( caller.Id, card ) );
	}


	[Rpc.Host]
	public void RequestGrabCard( CardObject card )
	{
		ProcessIntent( caller => new GrabCardIntent( caller.Id, card ) );
	}


	[Rpc.Host]
	public void ReleaseGrab( CardObject card )
	{
		ProcessIntent( caller => new ReleaseCardIntent( caller.Id, card ) );
	}


	[Rpc.Host]
	public void RequestMoveCard( CardObject card, Guid destinationZoneId, Transform freeformPose )
	{
		ZoneObject? destination = ZoneObject.Find( destinationZoneId );

		if ( destination is null )
		{
			RejectCaller( "The destination zone no longer exists." );
			return;
		}

		ProcessIntent( caller => new MoveCardIntent( caller.Id, card, destination, freeformPose ) );
	}


	[Rpc.Host]
	public void RequestThrowCard( CardObject card, Vector3 velocity, Vector3 angularVelocity )
	{
		ProcessIntent( caller => new ThrowCardIntent( caller.Id, card, velocity, angularVelocity ) );
	}


	[Rpc.Host]
	public void RequestFlipCard( CardObject card )
	{
		ProcessIntent( caller => new FlipCardIntent( caller.Id, card ) );
	}


	[Rpc.Host]
	public void RequestTapCard( CardObject card )
	{
		ProcessIntent( caller => new TapCardIntent( caller.Id, card, !card.Tapped ) );
	}


	[Rpc.Host]
	public void PassPriority()
	{
		ProcessIntent( caller => new PassPriorityIntent( caller.Id ) );
	}


	[Rpc.Host]
	public void EndTurn()
	{
		ProcessIntent( caller => new EndTurnIntent( caller.Id ) );
	}


	[Rpc.Host]
	public void Concede()
	{
		ProcessIntent( caller => new ConcedeIntent( caller.Id ) );
	}


	internal RulesContext CreateRulesContext( Seat actor )
	{
		return new RulesContext
		{
			Match = this,
			Actor = actor,
			Seats = Seats,
			MatchState = State,
			Flow = new MatchFlowSnapshot(
				TurnNumber,
				Phase,
				Step,
				ActivePlayerId,
				PriorityPlayerId,
				ConsecutivePasses,
				StackCount,
				HasPendingChoice
			)
		};
	}


	private void ProcessIntent( Func<Connection, GameIntent> createIntent )
	{
		if ( !Networking.IsHost )
			return;

		Connection caller = Rpc.Caller;
		Seat? actor = SeatFor( caller );

		if ( actor is null )
			return;

		EnsureRuntime();
		GameIntent intent = createIntent( caller );
		RuleDecision decision = Rules.Evaluate( intent, CreateRulesContext( actor ) );

		if ( !decision.Allowed )
		{
			actor.ActionError = decision.Message;
			Log.Info( $"Rejected {intent.GetType().Name} from {actor.PlayerName}: {decision.Code} - {decision.Message}" );
			RejectPhysicalAction( intent );
			return;
		}

		ActionExecutionResult result = _executor!.Execute( decision, actor );
		actor.ActionError = result.Success? string.Empty : result.Message;

		if ( !result.Success )
			RejectPhysicalAction( intent );
	}


	private void RejectCaller( string message )
	{
		Seat? actor = SeatFor( Rpc.Caller );

		if ( actor is not null )
			actor.ActionError = message;
	}


	private static void RejectPhysicalAction( GameIntent intent )
	{
		CardObject? card = intent switch
		{
			SelectCardIntent value  => value.Card,
			GrabCardIntent value    => value.Card,
			ReleaseCardIntent value => value.Card,
			MoveCardIntent value    => value.Card,
			ThrowCardIntent value   => value.Card,
			FlipCardIntent value    => value.Card,
			TapCardIntent value     => value.Card,
			_                       => null
		};

		if ( card.IsValid() )
		{
			if ( intent is MoveCardIntent or ThrowCardIntent && card.GrabbedByPlayerId == intent.ActorPlayerId )
				card.GrabbedByPlayerId = Guid.Empty;

			card.MoveTo( card.RestPose );
			card.Pulse();
		}
	}


	private void EnsureRuntime()
	{
		MTGFormat format = Scene.Get<GameDirector>()?.FormatFile
			?? throw new InvalidOperationException( "The match has no configured format." );

		_rules ??= GameObject.GetComponentsInChildren<RulesEngine>().FirstOrDefault()
		           ?? Scene.Get<RulesEngine>()
		           ?? throw new InvalidOperationException( "The match has no rules engine." );

		_rules.Initialize( this, format );

		if ( Networking.IsHost && !string.Equals( RulesProfile, _rules.CompositionSignature, StringComparison.Ordinal ) )
		{
			RulesProfile = _rules.CompositionSignature;

			if ( Networking.IsActive )
				Networking.SetData( "rules_profile", RulesProfile );
		}

		_flow ??= new GameFlowCoordinator( this, _rules );
		_executor ??= new GameActionExecutor( this, _flow );
	}


	private Seat? SeatFor( Connection connection )
	{
		return Seats.FirstOrDefault( seat => seat.Player == connection.Id );
	}


	private bool CanBegin( Seat? excluded = null )
	{
		IReadOnlyList<Seat> players = Seats
			.Where( seat => !ReferenceEquals( seat, excluded ) )
			.ToArray();

		return Rules.CanBegin( this, players ).Verdict != RuleVerdict.Deny;
	}


	private void TryBeginWhenReady()
	{
		if ( CanBegin() )
			Begin();
	}


	private void RefreshLobbyStatus( Seat? excluded = null )
	{
		IReadOnlyList<Seat> players = Seats
			.Where( seat => !ReferenceEquals( seat, excluded ) )
			.ToArray();
		int minimumPlayers = Scene.Get<GameDirector>()?.FormatFile?.MinimumPlayers ?? 2;

		if ( players.Count < minimumPlayers )
		{
			int needed = minimumPlayers - players.Count;
			StatusText = $"Waiting for {needed} more player{(needed == 1 ? string.Empty : "s")}";
		}
		else if ( players.Count( seat => !seat.HasSubmittedDeck ) is int missingDecks && missingDecks > 0 )
		{
			StatusText = $"Waiting for {missingDecks} deck{(missingDecks == 1 ? string.Empty : "s")}";
		}
		else if ( players.Count( seat => !seat.Ready ) is int notReady && notReady > 0 )
		{
			StatusText = $"Waiting for {notReady} player{(notReady == 1 ? string.Empty : "s")} to ready up";
		}
		else
		{
			StatusText = "Starting match";
		}

		LobbyRevision++;
	}
}
