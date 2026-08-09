#nullable enable

using Sandbox.Classes.Cards;
using Sandbox.Classes.Deck;
using Sandbox.Framework.GameInfo;
using System;
namespace Sandbox.Framework;

/// <summary>
///     Host Authority Coordinator
///     Dictates the source of truth between host and clients
/// </summary>
public sealed partial class GameDirector : Component
{
	private readonly  Dictionary<Guid, Deck> _submittedDecks = [ ];
	[Property] public GameFormat?            Format { get; set; }

	[Sync] public Guid        MatchId    { get; set; }
	[Sync] public GameState   State      { get; set; } = GameState.Lobby;
	[Sync] public string      StatusText { get; set; } = "Waiting for players";
	[Sync] public MatchResult Result     { get; set; }


	//
	// Helpers
	//
	public RulesEngine RulesEngine
	{
		get { return Scene.Get<RulesEngine>() ?? throw new InvalidOperationException( "The scene has no MtgGameRules component." ); }
	}

	public PlayerRoster Roster
	{
		get { return Scene.Get<PlayerRoster>() ?? throw new InvalidOperationException( "No Player Roster" ); }
	}

	public TurnManager TurnManager
	{
		get { return Scene.Get<TurnManager>() ?? throw new InvalidOperationException( "No Turn Manager" ); }
	}

	public PriorityManager Priority
	{
		get { return Scene.Get<PriorityManager>() ?? throw new InvalidOperationException( "No Priority Manager" ); }
	}

	public StackManager Stack
	{
		get { return Scene.Get<StackManager>() ?? throw new InvalidOperationException( "No Stack Manager" ); }
	}


	protected override void OnStart()
	{
		Mouse.Visibility = MouseVisibility.Visible;

		if ( !Networking.IsHost )
			return;

		if ( MatchId == Guid.Empty )
			MatchId = Guid.NewGuid();
	}


	public bool StartGame()
	{
		return false;
	}


	public bool EndGame()
	{
		return false;
	}


	public void OnPlayerJoined( Connection Channel ) { }


	public void OnPlayerDisconnected( Connection Channel ) { }


	public void OnTurnCompleted() { }
}
