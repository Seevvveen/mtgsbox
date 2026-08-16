#nullable enable

using Sandbox.Framework.GameInfo;
using System;

namespace Sandbox.Framework.Rules;

/// <summary>
///     Immutable view supplied to rule modules for one decision. Modules receive
///     dependencies here instead of discovering mutable scene state themselves.
/// </summary>
public sealed record RulesContext
{
	public required Match Match { get; init; }
	public required Seat Actor { get; init; }
	public required IReadOnlyList<Seat> Seats { get; init; }
	public required GameState MatchState { get; init; }
	public required MatchFlowSnapshot Flow { get; init; }
}

public readonly record struct MatchFlowSnapshot(
	int TurnNumber,
	TurnPhase Phase,
	TurnStep Step,
	Guid ActivePlayerId,
	Guid PriorityPlayerId,
	int ConsecutivePasses,
	int StackCount,
	bool HasPendingChoice
);
