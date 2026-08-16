#nullable enable

using Sandbox.Framework.Rules;
using System;

namespace Sandbox.Framework;

/// <summary>
///     Match-owned gameplay mechanisms. Policies come from RulesEngine; these
///     coordinators only maintain deterministic flow state.
/// </summary>
public sealed class GameFlowCoordinator
{
	private readonly Match _match;
	private readonly RulesEngine _rules;

	public GameFlowCoordinator( Match match, RulesEngine rules )
	{
		_match = match;
		_rules = rules;
		Stack = new StackCoordinator( match );
		Choices = new ChoiceCoordinator( match );
		Priority = new PriorityCoordinator( match, rules, OnPriorityCycleCompleted );
		Turns = new TurnCoordinator( match, rules, Priority );
	}

	public TurnCoordinator Turns { get; }
	public PriorityCoordinator Priority { get; }
	public StackCoordinator Stack { get; }
	public ChoiceCoordinator Choices { get; }

	public void Start()
	{
		Seat? first = _match.Seats.FirstOrDefault( seat => seat.Occupied && seat.IsConnected && !seat.Eliminated );

		if ( first is not null )
			Turns.BeginTurn( first );
	}

	public void Stop()
	{
		Priority.End();
		Stack.Clear();
		Choices.Clear();
	}

	private void OnPriorityCycleCompleted()
	{
		if ( Stack.Count > 0 )
		{
			Stack.ResolveTop();
			Priority.Begin();
			return;
		}

		Turns.AdvanceStep();
	}
}


public sealed class TurnCoordinator
{
	private readonly Match _match;
	private readonly RulesEngine _rules;
	private readonly PriorityCoordinator _priority;

	internal TurnCoordinator( Match match, RulesEngine rules, PriorityCoordinator priority )
	{
		_match = match;
		_rules = rules;
		_priority = priority;
	}

	public void BeginTurn( Seat player )
	{
		RulesContext context = _match.CreateRulesContext( player );

		if ( !_rules.TurnPolicy.CanTakeTurn( player, context ) )
			return;

		_match.TurnNumber++;
		_match.ActivePlayerId = player.Player;
		BeginStepAt( 0 );
	}

	public void AdvanceStep()
	{
		IReadOnlyList<TurnStep> steps = _rules.TurnPolicy.Steps;
		int index = IndexOf( steps, _match.Step );

		if ( index < 0 || index + 1 >= steps.Count )
		{
			EndTurn();
			return;
		}

		BeginStepAt( index + 1 );
	}

	public void EndTurn()
	{
		_priority.End();
		Seat? current = _match.Seats.FirstOrDefault( seat => seat.Player == _match.ActivePlayerId );

		if ( current is null )
			return;

		Seat? next = _rules.TurnPolicy.NextActivePlayer( current, _match.CreateRulesContext( current ) );

		if ( next is null )
		{
			_match.ActivePlayerId = Guid.Empty;
			return;
		}

		BeginTurn( next );
	}

	private void BeginStepAt( int index )
	{
		IReadOnlyList<TurnStep> steps = _rules.TurnPolicy.Steps;

		if ( index < 0 || index >= steps.Count )
		{
			EndTurn();
			return;
		}

		_match.Step = steps[index];
		_match.Phase = PhaseOf( _match.Step );
		Seat? active = _match.Seats.FirstOrDefault( seat => seat.Player == _match.ActivePlayerId );

		if ( active is null )
			return;

		RulesContext context = _match.CreateRulesContext( active );

		if ( _rules.TurnPolicy.GrantsPriority( _match.Step, context ) )
			_priority.Begin();
		else
			AdvanceStep();
	}

	private static int IndexOf( IReadOnlyList<TurnStep> steps, TurnStep step )
	{
		for ( int index = 0; index < steps.Count; index++ )
		{
			if ( steps[index] == step )
				return index;
		}

		return -1;
	}

	private static TurnPhase PhaseOf( TurnStep step )
	{
		return step switch
		{
			TurnStep.Untap or TurnStep.Upkeep or TurnStep.Draw => TurnPhase.Beginning,
			TurnStep.PrecombatMain => TurnPhase.PrecombatMain,
			TurnStep.BeginningOfCombat or TurnStep.DeclareAttackers or TurnStep.DeclareBlockers or
				TurnStep.FirstStrikeCombatDamage or TurnStep.CombatDamage or TurnStep.EndOfCombat => TurnPhase.Combat,
			TurnStep.PostcombatMain => TurnPhase.PostcombatMain,
			_ => TurnPhase.Ending
		};
	}
}


public sealed class PriorityCoordinator
{
	private readonly Match _match;
	private readonly RulesEngine _rules;
	private readonly Action _completed;

	internal PriorityCoordinator( Match match, RulesEngine rules, Action completed )
	{
		_match = match;
		_rules = rules;
		_completed = completed;
	}

	public void Begin()
	{
		Seat? contextSeat = _match.Seats.FirstOrDefault( seat => seat.Player == _match.ActivePlayerId )
		                    ?? _match.Seats.FirstOrDefault( seat => seat.Occupied && !seat.Eliminated );

		if ( contextSeat is null )
		{
			End();
			return;
		}

		RulesContext context = _match.CreateRulesContext( contextSeat );
		Seat? first = _rules.PriorityPolicy.FirstPlayer( context );
		_match.PriorityPlayerId = first?.Player ?? Guid.Empty;
		_match.ConsecutivePasses = 0;
	}

	public void Pass( Seat player )
	{
		if ( player.Player != _match.PriorityPlayerId )
			return;

		RulesContext context = _match.CreateRulesContext( player );
		IReadOnlyList<Seat> eligible = _rules.PriorityPolicy.EligiblePlayers( context );

		if ( eligible.Count == 0 )
		{
			End();
			_completed();
			return;
		}

		_match.ConsecutivePasses++;

		if ( _match.ConsecutivePasses >= eligible.Count )
		{
			End();
			_completed();
			return;
		}

		int current = IndexOf( eligible, player );
		_match.PriorityPlayerId = eligible[(current + 1 + eligible.Count) % eligible.Count].Player;
	}

	public void ActionTaken( Seat player )
	{
		_match.PriorityPlayerId = player.Player;
		_match.ConsecutivePasses = 0;
	}

	public void End()
	{
		_match.PriorityPlayerId = Guid.Empty;
		_match.ConsecutivePasses = 0;
	}

	private static int IndexOf( IReadOnlyList<Seat> seats, Seat player )
	{
		for ( int index = 0; index < seats.Count; index++ )
		{
			if ( ReferenceEquals( seats[index], player ) || seats[index].Player == player.Player )
				return index;
		}

		return 0;
	}
}


public sealed class StackCoordinator
{
	private readonly Match _match;
	private readonly List<StackEntry> _entries = [ ];

	internal StackCoordinator( Match match )
	{
		_match = match;
	}

	public int Count => _entries.Count;
	public IReadOnlyList<StackEntry> Entries => _entries;

	public void Push( StackEntry entry )
	{
		_entries.Add( entry );
		_match.StackCount = _entries.Count;
	}

	public StackEntry? ResolveTop()
	{
		if ( _entries.Count == 0 )
			return null;

		StackEntry top = _entries[^1];
		_entries.RemoveAt( _entries.Count - 1 );
		_match.StackCount = _entries.Count;
		return top;
	}

	public void Clear()
	{
		_entries.Clear();
		_match.StackCount = 0;
	}
}

public readonly record struct StackEntry( Guid Id, Guid ControllerPlayerId, Guid SourceCardId, string DisplayName );


public sealed class ChoiceCoordinator
{
	private readonly Match _match;
	private PendingChoice? _pending;

	internal ChoiceCoordinator( Match match )
	{
		_match = match;
	}

	public PendingChoice? Pending => _pending;

	public bool Begin( PendingChoice choice )
	{
		if ( _pending is not null )
			return false;

		_pending = choice;
		_match.HasPendingChoice = true;
		return true;
	}

	public bool Complete( Guid playerId )
	{
		if ( _pending is not { } choice || choice.PlayerId != playerId )
			return false;

		Clear();
		return true;
	}

	public void Clear()
	{
		_pending = null;
		_match.HasPendingChoice = false;
	}
}

public sealed record PendingChoice( Guid Id, Guid PlayerId, string Kind, string Prompt );
