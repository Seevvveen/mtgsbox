#nullable enable

using Sandbox.Classes.Cards;
namespace Sandbox.Framework.GameInfo;

/// <summary>
///     Asset metadata shared by an MTG rules prefab and its lobby.
/// </summary>
[AssetType( Name = "MTG Game Format", Extension = "format", Category = "MTG" )]
public sealed class GameFormat : GameResource
{
	[Property] public string DisplayName    { get; set; } = "Magic";
	[Property] public string FormatCode     { get; set; } = "standard";
	[Property] public byte   DeckSize       { get; set; } = 20;
	[Property] public byte   MinimumPlayers { get; set; } = 2;
	[Property] public byte   MaximumPlayers { get; set; } = 8;
	[Property] public int    StartingLife   { get; set; } = 40;
	[Property] public float  TurnTimeLimit  { get; set; }
	public override void ConfigurePublishing( ResourcePublishContext context )
	{
	}
}
