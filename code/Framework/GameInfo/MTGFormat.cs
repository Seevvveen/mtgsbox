#nullable enable

using Sandbox.Classes.Cards;
namespace Sandbox.Framework.GameInfo;

/// <summary>
///     Represents format specifications and locations of prefabs
/// </summary>
[AssetType( Name = "MTG Format", Extension = "format", Category = "MTG" )]
public sealed class MTGFormat : GameResource
{
	[Property] public string DisplayName    { get; set; } = "Magic";
	[Property] public string FormatCode     { get; set; } = "standard";
	[Property] public byte   DeckSize       { get; set; } = 20;
	[Property] public byte   MinimumPlayers { get; set; } = 2;
	[Property] public byte   MaximumPlayers { get; set; } = 8;
	[Property] public int    StartingLife   { get; set; } = 40;
	[Property] public float  TurnTimeLimit  { get; set; }
	[Property] public List<string> RequiredRuleModules { get; set; } = [ ];

	[Property] public PrefabFile? Prefab { get; set; }
}
