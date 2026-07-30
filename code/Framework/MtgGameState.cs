#nullable enable

namespace Sandbox.Classes;

public enum MtgMatchState
{
	Lobby,
	Loading,
	Mulligan,
	Playing,
	Paused,
	Finished
}

public enum MtgTurnPhase
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

public enum MtgPlayerOutcome
{
	None,
	Won,
	Lost,
	Draw,
	Conceded
}

public enum MtgMatchResultTone
{
	Neutral,
	Win,
	Loss,
	Draw
}

public readonly record struct MtgMatchResult(
	string Title,
	string Detail,
	MtgMatchResultTone Tone =
		MtgMatchResultTone.Neutral );

public readonly record struct MtgGameAction(
	string Id,
	string Label,
	bool Primary = false,
	bool Disabled = false,
	long Amount = 0 );

public readonly record struct MtgGameHint(
	string Text,
	CardObject[] Cards );
