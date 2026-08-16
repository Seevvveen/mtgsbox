#nullable enable

using System;

namespace Sandbox.Framework.Rules;

/// <summary>
///     Host-side record of one accepted action. Replacement rules transform
///     proposed commands before commit; committed events then feed state-based
///     actions and trigger collection.
/// </summary>
public sealed record GameTransaction
{
	public required Guid Id { get; init; }
	public required Guid ActorPlayerId { get; init; }
	public List<GameCommand> Commands { get; init; } = [ ];
	public List<GameEvent> Events { get; init; } = [ ];
}

public abstract record GameEvent( Guid TransactionId );

public sealed record CommandCommittedEvent(
	Guid TransactionId,
	string CommandType,
	Guid ActorPlayerId
) : GameEvent( TransactionId );

public interface IReplacementRule
{
	GameCommand Replace( GameCommand proposed, RulesContext context );
}

public interface IStateBasedActionRule
{
	IEnumerable<GameCommand> EvaluateState( RulesContext context );
}

public interface ITriggerRule
{
	IEnumerable<StackEntry> CollectTriggers( IReadOnlyList<GameEvent> events, RulesContext context );
}
