#nullable enable

namespace Sandbox.Framework.Rules;

public enum TurnPhase
{
	Beginning,
	PrecombatMain,
	Combat,
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
