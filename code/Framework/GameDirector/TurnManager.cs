#nullable enable
using System;
namespace Sandbox.Framework;

public enum TurnPhase
{
	Beginning,
	PrecombatMain,
	BeginningOfCombat,
	DeclareAttackers,
	DeclareBlockers,
	CombatDamage,
	EndOfCombat,
	PostcombatMain,
	Ending
}

public enum TurnStep
{
	Untap,
	Upkeep,
	Draw,
	PrecombatMain,
	BeginningOfCombat,
	DeclareAttackers,
	DeclareBlockers,
	FirstStrikeCombatDamage,
	CombatDamage,
	EndOfCombat,
	PostcombatMain,
	End,
	Cleanup
}

/// <summary>
///     Host-authoritative manager for turn order and turn-step progression.
///     Priority is managed separately by <see cref = "PriorityManager"/>.
/// </summary>
public sealed class TurnManager : Component
{
	public GameDirector Director
	{
		get { return Scene.Get<GameDirector>() ?? throw new InvalidOperationException( "The scene has no MTG game director." ); }
	}

	public PlayerRoster Roster
	{
		get { return Scene.Get<PlayerRoster>() ?? throw new InvalidOperationException( "The scene has no player roster." ); }
	}

	public PriorityManager Priority
	{
		get { return Scene.Get<PriorityManager>() ?? throw new InvalidOperationException( "The scene has no priority manager." ); }
	}

	public StackManager Stack
	{
		get { return Scene.Get<StackManager>() ?? throw new InvalidOperationException( "The scene has no stack manager." ); }
	}

	[Sync] public int TurnNumber { get; set; }

	[Sync] public TurnPhase Phase { get; set; } = TurnPhase.Beginning;

	[Sync] public TurnStep Step { get; set; } = TurnStep.Untap;

	[Sync] public Guid ActivePlayerId { get; set; }

	public PlayerSeat? ActivePlayer
	{
		get { return Roster.SeatOf( ActivePlayerId ); }
	}


	protected override void OnStart()
	{
		if ( !Networking.IsHost )
			return;

		Reset();
	}


	public void Reset()
	{
		if ( !Networking.IsHost )
			return;

		TurnNumber     = 0;
		Phase          = TurnPhase.Beginning;
		Step           = TurnStep.Untap;
		ActivePlayerId = Guid.Empty;

		Priority.End();
	}


	public void BeginTurn( PlayerSeat player )
	{
		if ( !Networking.IsHost )
			return;

		if ( !CanTakeTurn( player ) )
			return;

		TurnNumber++;
		ActivePlayerId = player.ParticipantId;

		Director.StatusText = $"{player.DisplayName}'s turn";
		Director.RulesEngine.OnTurnStarted( player );

		BeginStep( TurnStep.Untap );
	}


	public void BeginStep( TurnStep step )
	{
		if ( !Networking.IsHost )
			return;

		if ( ActivePlayer is not { } activePlayer )
			return;

		TurnPhase previousPhase = Phase;
		TurnStep  previousStep  = Step;
		TurnPhase nextPhase     = PhaseOf( step );

		Phase = nextPhase;
		Step  = step;

		Director.StatusText = FormatStepName( step );

		if ( previousPhase != nextPhase || step == TurnStep.Untap )
			Director.RulesEngine.OnPhaseChanged( previousPhase, nextPhase );

		Director.RulesEngine.OnStepChanged( previousStep, step );

		if ( StepGrantsPriority( step ) )
			Priority.Begin( activePlayer );
		else
			AdvanceStep();
	}


	/// <summary>
	///     Called by PriorityManager after every eligible player passes
	///     consecutively.
	/// </summary>
	public void OnPriorityCycleCompleted()
	{
		if ( !Networking.IsHost )
			return;

		Director.RulesEngine.OnAllPlayersPassedPriority();

		if ( Stack.HasObjects )
		{
			Stack.ResolveTop();

			if ( ActivePlayer is { } activePlayer )
				Priority.Begin( activePlayer );

			return;
		}

		AdvanceStep();
	}


	public void AdvanceStep()
	{
		if ( !Networking.IsHost )
			return;

		Priority.End();

		if ( Step == TurnStep.Cleanup )
		{
			EndTurn();

			return;
		}

		TurnStep next = GetNextStep( Step );

		while ( !Director.RulesEngine.ShouldEnterStep( next ) )
		{
			if ( next == TurnStep.Cleanup )
			{
				EndTurn();

				return;
			}

			next = GetNextStep( next );
		}

		BeginStep( next );
	}


	public void EndTurn()
	{
		if ( !Networking.IsHost )
			return;

		if ( ActivePlayer is not { } currentPlayer )
			return;

		Priority.End();

		PlayerSeat? nextPlayer = Roster.GetNextPlayer( currentPlayer.ParticipantId );

		Director.OnTurnCompleted();

		if ( nextPlayer is null )
		{
			ActivePlayerId = Guid.Empty;

			return;
		}

		BeginTurn( nextPlayer );
	}


	private bool StepGrantsPriority( TurnStep step )
	{
		return step switch
		{
			TurnStep.Untap   => false,
			TurnStep.Cleanup => Director.RulesEngine.ShouldGrantCleanupPriority(),
			_                => true
		};
	}


	private static bool CanTakeTurn( PlayerSeat player )
	{
		return player.IsOccupied && !player.IsEliminated;
	}


	private static TurnPhase PhaseOf( TurnStep step )
	{
		return step switch
		{
			TurnStep.Untap                   => TurnPhase.Beginning,
			TurnStep.Upkeep                  => TurnPhase.Beginning,
			TurnStep.Draw                    => TurnPhase.Beginning,
			TurnStep.PrecombatMain           => TurnPhase.PrecombatMain,
			TurnStep.BeginningOfCombat       => TurnPhase.BeginningOfCombat,
			TurnStep.DeclareAttackers        => TurnPhase.DeclareAttackers,
			TurnStep.DeclareBlockers         => TurnPhase.DeclareBlockers,
			TurnStep.FirstStrikeCombatDamage => TurnPhase.CombatDamage,
			TurnStep.CombatDamage            => TurnPhase.CombatDamage,
			TurnStep.EndOfCombat             => TurnPhase.EndOfCombat,
			TurnStep.PostcombatMain          => TurnPhase.PostcombatMain,
			TurnStep.End                     => TurnPhase.Ending,
			TurnStep.Cleanup                 => TurnPhase.Ending,
			_                                => throw new ArgumentOutOfRangeException( nameof(step), step, null )
		};
	}


	private static TurnStep GetNextStep( TurnStep step )
	{
		return step switch
		{
			TurnStep.Untap                   => TurnStep.Upkeep,
			TurnStep.Upkeep                  => TurnStep.Draw,
			TurnStep.Draw                    => TurnStep.PrecombatMain,
			TurnStep.PrecombatMain           => TurnStep.BeginningOfCombat,
			TurnStep.BeginningOfCombat       => TurnStep.DeclareAttackers,
			TurnStep.DeclareAttackers        => TurnStep.DeclareBlockers,
			TurnStep.DeclareBlockers         => TurnStep.FirstStrikeCombatDamage,
			TurnStep.FirstStrikeCombatDamage => TurnStep.CombatDamage,
			TurnStep.CombatDamage            => TurnStep.EndOfCombat,
			TurnStep.EndOfCombat             => TurnStep.PostcombatMain,
			TurnStep.PostcombatMain          => TurnStep.End,
			TurnStep.End                     => TurnStep.Cleanup,
			TurnStep.Cleanup                 => TurnStep.Cleanup,
			_                                => throw new ArgumentOutOfRangeException( nameof(step), step, null )
		};
	}


	private static string FormatStepName( TurnStep step )
	{
		return step switch
		{
			TurnStep.PrecombatMain           => "Precombat Main",
			TurnStep.BeginningOfCombat       => "Beginning of Combat",
			TurnStep.DeclareAttackers        => "Declare Attackers",
			TurnStep.DeclareBlockers         => "Declare Blockers",
			TurnStep.FirstStrikeCombatDamage => "First-Strike Combat Damage",
			TurnStep.CombatDamage            => "Combat Damage",
			TurnStep.EndOfCombat             => "End of Combat",
			TurnStep.PostcombatMain          => "Postcombat Main",
			_                                => step.ToString()
		};
	}
}