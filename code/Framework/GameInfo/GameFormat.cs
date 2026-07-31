#nullable enable

namespace Sandbox.Classes;

/// <summary>
///     Asset metadata shared by an MTG rules prefab and its lobby.
/// </summary>
[AssetType( Name = "MTG Game Format", Extension = "format", Category = "MTG" )]
public sealed class GameFormat : GameResource
{
	[Property] public string DisplayName { get; set; } = "Magic";

	[Property][TextArea] public string Tutorial { get; set; } = string.Empty;

	[Property] public string FormatCode { get; set; } = "standard";

	[Property] public PrefabFile? RulesPrefab { get; set; }

	[Property] public int MinimumPlayers { get; set; } = 2;

	[Property] public int MaximumPlayers { get; set; } = 2;

	[Property] public int StartingLife { get; set; } = 20;

	[Property] public float TurnTimeLimit { get; set; }

	[Property] public float CardWidth { get; set; } = CardMesh.DefaultWidth;

	[Property] public float CardThicknessRatio { get; set; } = CardMesh.DefaultThicknessRatio;


	public override void ConfigurePublishing( ResourcePublishContext context )
	{
		if ( RulesPrefab is null )
		{
			context.SetPublishingDisabled( "Invalid: missing an MTG rules prefab." );
		}

		context.IncludeCode = true;
	}
}
