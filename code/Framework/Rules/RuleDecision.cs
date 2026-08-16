#nullable enable

namespace Sandbox.Framework.Rules;

public enum RuleVerdict
{
	Abstain,
	Allow,
	OverrideAllow,
	Deny
}

/// <summary>
///     One module's answer. Abstain means the module does not govern this
///     intent; Allow means its requirements passed; Deny stops the pipeline.
/// </summary>
public readonly record struct RuleEvaluation( RuleVerdict Verdict, string Code = "", string Message = "" )
{
	public static RuleEvaluation Abstain() => new( RuleVerdict.Abstain );
	public static RuleEvaluation Allow() => new( RuleVerdict.Allow );
	/// <summary>Clears an earlier policy denial. Match/actor invariants cannot be overridden.</summary>
	public static RuleEvaluation OverrideAllow() => new( RuleVerdict.OverrideAllow );
	public static RuleEvaluation Deny( string code, string message ) => new( RuleVerdict.Deny, code, message );
}

/// <summary>
///     Final answer from the rules session. Rejections are structured so UI and
///     logs do not have to infer why an action failed.
/// </summary>
public sealed record RuleDecision
{
	public required bool Allowed { get; init; }
	public string Code { get; init; } = string.Empty;
	public string Message { get; init; } = string.Empty;
	public IReadOnlyList<GameCommand> Commands { get; init; } = [ ];

	public static RuleDecision Permit( params GameCommand[] commands )
	{
		return new RuleDecision { Allowed = true, Commands = commands };
	}

	public static RuleDecision Reject( string code, string message )
	{
		return new RuleDecision { Allowed = false, Code = code, Message = message };
	}
}
