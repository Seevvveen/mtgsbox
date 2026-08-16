#nullable enable

using Sandbox.Classes.Cards;
using Sandbox.Framework.GameInfo;
using Sandbox.Framework.Rules;
using System;

namespace Sandbox.Framework;

/// <summary>
///     The only normal gameplay path that applies commands approved by the
///     rules session. Low-level card and zone APIs remain mechanics, not policy.
/// </summary>
public sealed class GameActionExecutor
{
	private const int MaximumStabilizationPasses = 64;
	private readonly Match _match;
	private readonly GameFlowCoordinator _flow;

	public GameActionExecutor( Match match, GameFlowCoordinator flow )
	{
		_match = match;
		_flow = flow;
	}

	public ActionExecutionResult Execute( RuleDecision decision, Seat actor )
	{
		if ( !decision.Allowed )
			return ActionExecutionResult.Failed( decision.Code, decision.Message );

		try
		{
			RulesContext context = _match.CreateRulesContext( actor );
			GameTransaction transaction = new GameTransaction
			{
				Id = Guid.NewGuid(),
				ActorPlayerId = actor.Player,
				Commands = decision.Commands
					.Select( command => _match.Rules.ApplyReplacements( command, context ) )
					.ToList()
			};

			foreach ( GameCommand command in transaction.Commands )
				Commit( command, transaction );

			if ( transaction.Commands.Any( UsesPriority ) )
				_flow.Priority.ActionTaken( actor );

			Stabilize( actor, transaction );

			foreach ( StackEntry trigger in _match.Rules.CollectTriggers( transaction.Events, _match.CreateRulesContext( actor ) ) )
				_flow.Stack.Push( trigger );

			EvaluateOutcomes();
			return ActionExecutionResult.Succeeded( transaction );
		}
		catch ( Exception exception )
		{
			return ActionExecutionResult.Failed( "action.execution_failed", exception.Message );
		}
	}


	private static bool UsesPriority( GameCommand command )
	{
		return command is MoveCardCommand or ThrowCardCommand or FlipCardCommand or TapCardCommand;
	}

	private void Commit( GameCommand command, GameTransaction transaction )
	{
		switch ( command )
		{
			case NoOpCommand:
				break;

			case SelectCardCommand select:
				select.Actor.SelectedCard = select.Card;
				break;

			case GrabCardCommand grab:
				grab.Card.GrabbedByPlayerId = grab.ActorPlayerId;
				grab.Card.CancelThrow();
				break;

			case ReleaseCardCommand release:
				if ( release.Card.GrabbedByPlayerId == release.ActorPlayerId )
				{
					release.Card.GrabbedByPlayerId = Guid.Empty;
					release.Card.MoveTo( release.Card.RestPose );
				}
				break;

			case MoveCardCommand move:
				move.Card.GrabbedByPlayerId = Guid.Empty;
				move.Card.PlaceInZone( move.Destination.ZoneId, move.FreeformPose );
				break;

			case ThrowCardCommand thrown:
				thrown.Card.GrabbedByPlayerId = Guid.Empty;
				thrown.Card.Throw( thrown.Velocity, thrown.AngularVelocity );
				break;

			case FlipCardCommand flip:
				flip.Card.FlipPrintedFace();
				break;

			case TapCardCommand tap:
				tap.Card.SetTapped( tap.Tapped );
				break;

			case PassPriorityCommand pass:
				_flow.Priority.Pass( pass.Actor );
				break;

			case EndTurnCommand:
				_flow.Turns.EndTurn();
				break;

			case ConcedeCommand concede:
				Concede( concede.Actor );
				break;

			default:
				if ( !_match.Rules.TryExecuteCustomCommand( command, _flow ) )
					throw new InvalidOperationException( $"Unsupported game command '{command.GetType().Name}'." );
				break;
		}

		transaction.Events.Add( new CommandCommittedEvent( transaction.Id, command.GetType().Name, transaction.ActorPlayerId ) );
	}

	private void Stabilize( Seat actor, GameTransaction transaction )
	{
		for ( int pass = 0; pass < MaximumStabilizationPasses; pass++ )
		{
			RulesContext context = _match.CreateRulesContext( actor );
			IReadOnlyList<GameCommand> stateActions = _match.Rules.EvaluateStateBasedActions( context );

			if ( stateActions.Count == 0 )
				return;

			foreach ( GameCommand proposed in stateActions )
			{
				GameCommand command = _match.Rules.ApplyReplacements( proposed, context );
				transaction.Commands.Add( command );
				Commit( command, transaction );
			}
		}

		throw new InvalidOperationException( "State-based actions did not stabilize after 64 passes." );
	}

	private void Concede( Seat seat )
	{
		seat.Eliminated = true;
		seat.Outcome = SeatOutcome.Loss;
		seat.SelectedCard = null;

		foreach ( CardObject card in _match.GameObject.GetComponentsInChildren<CardObject>() )
		{
			if ( card.GrabbedByPlayerId == seat.Player )
				card.GrabbedByPlayerId = Guid.Empty;
		}
	}

	private void EvaluateOutcomes()
	{
		Seat? contextSeat = _match.Seats.FirstOrDefault( seat => seat.Occupied && !seat.Eliminated );

		if ( contextSeat is null )
		{
			_match.Conclude();
			return;
		}

		RulesContext context = _match.CreateRulesContext( contextSeat );

		foreach ( IOutcomeRule rule in _match.Rules.OutcomeRules )
		{
			MatchOutcomeDecision result = rule.EvaluateOutcome( context );

			if ( !result.Finished )
				continue;

			Seat? winner = _match.Seats.FirstOrDefault( seat => seat.Player == result.WinnerPlayerId );
			_match.Conclude( winner, result.Detail );
			return;
		}

		Seat[] remaining = _match.Seats.Where( seat => seat.Occupied && !seat.Eliminated ).ToArray();

		if ( _match.Seats.Count > 1 && remaining.Length <= 1 )
			_match.Conclude( remaining.FirstOrDefault(), "All other participants have left the game." );
	}
}

public readonly record struct ActionExecutionResult( bool Success, string Code = "", string Message = "", GameTransaction? Transaction = null )
{
	public static ActionExecutionResult Succeeded( GameTransaction transaction ) => new( true, Transaction: transaction );
	public static ActionExecutionResult Failed( string code, string message ) => new( false, code, message );
}
