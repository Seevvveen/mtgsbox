#nullable enable

namespace Sandbox.Classes;

public enum GameState
{
	Lobby,
	Loading,
	Mulligan,
	Playing,
	Paused,
	Finished
}

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

public enum PlayerOutcome
{
	None,
	Won,
	Lost,
	Draw,
	Conceded
}

public enum MatchResultTone
{
	Neutral,
	Win,
	Loss,
	Draw
}

public readonly record struct MatchResult( string Title, string       Detail, MatchResultTone Tone    = MatchResultTone.Neutral );
public readonly record struct GameAction( string  Id,    string       Label,  bool            Primary = false, bool Disabled = false, long Amount = 0 );
public readonly record struct GameHint( string    Text,  CardObject[] Cards );
