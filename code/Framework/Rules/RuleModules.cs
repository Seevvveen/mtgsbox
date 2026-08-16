#nullable enable

using Sandbox.Classes.Deck.Validation;
using Sandbox.Classes.Deck;
using Sandbox.Framework.GameInfo;
using System;

namespace Sandbox.Framework.Rules;

public interface IGameRuleModule
{
	RuleEvaluation Evaluate( GameIntent intent, RulesContext context );
}

/// <summary>
///     Add a derived component to a match prefab to contribute format-specific
///     restrictions without subclassing the complete rules engine.
/// </summary>
public abstract class RulesModule : Component, IGameRuleModule
{
	[Property] public int Order { get; set; }
	[Property] public string ModuleId { get; set; } = string.Empty;
	[Property] public string ModuleVersion { get; set; } = "1";
	[Property] public List<string> Dependencies { get; set; } = [ ];
	[Property] public List<string> IncompatibleWith { get; set; } = [ ];

	public string EffectiveModuleId => string.IsNullOrWhiteSpace( ModuleId )
		? GetType().FullName ?? GetType().Name
		: ModuleId.Trim();

	public virtual RuleEvaluation Evaluate( GameIntent intent, RulesContext context )
	{
		return RuleEvaluation.Abstain();
	}
}

public interface IGameCommandProvider
{
	bool TryCreateCommand( GameIntent intent, RulesContext context, out GameCommand? command );
}

public interface IGameCommandHandler
{
	bool CanExecute( GameCommand command );
	void Execute( GameCommand command, GameExecutionContext context );
}

public sealed record GameExecutionContext
{
	public required Match Match { get; init; }
	public required GameFlowCoordinator Flow { get; init; }
}

public interface IDeckRuleProvider
{
	DeckFormatDefinition CreateDeckFormat( MTGFormat format );
	IEnumerable<IDeckFormatRule> AdditionalDeckRules { get; }
}

public interface ITurnStructurePolicy
{
	IReadOnlyList<TurnStep> Steps { get; }
	bool GrantsPriority( TurnStep step, RulesContext context );
	bool CanTakeTurn( Seat seat, RulesContext context );
	Seat? NextActivePlayer( Seat current, RulesContext context );
}

public interface IPriorityPolicy
{
	IReadOnlyList<Seat> EligiblePlayers( RulesContext context );
	Seat? FirstPlayer( RulesContext context );
}

public interface IOutcomeRule
{
	MatchOutcomeDecision EvaluateOutcome( RulesContext context );
}

public interface IMatchLifecycleRule
{
	RuleEvaluation CanJoin( Match match, Connection connection, IReadOnlyList<Seat> seats ) => RuleEvaluation.Abstain();
	RuleEvaluation CanSubmitDeck( Match match, Seat seat, Deck deck ) => RuleEvaluation.Abstain();
	RuleEvaluation CanSetReady( Match match, Seat seat, bool ready ) => RuleEvaluation.Abstain();
	RuleEvaluation CanBegin( Match match, IReadOnlyList<Seat> seats ) => RuleEvaluation.Abstain();
}

public readonly record struct MatchOutcomeDecision( bool Finished, Guid WinnerPlayerId = default, string Detail = "" );
