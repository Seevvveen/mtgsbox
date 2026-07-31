#nullable enable

using System;
namespace Sandbox.Classes.Cards;

/// <summary>
///     Creates the simple, non-metallic complex materials used by cards and zone
///     markers. Runtime-created complex materials do not inherit the neutral
///     texture inputs that an authored .vmat normally supplies.
/// </summary>
static class CardMaterialFactory
{
	public static Material Create( string name, Texture color, bool anonymous = true )
	{
		ArgumentNullException.ThrowIfNull( color );

		Material material = Material.Create( name, "complex.shader", anonymous );

		material.Set( "g_tColor", color );

		// A complex material expects an AO texture even when the surface has no
		// baked occlusion. Leaving this unbound displays the engine's missing
		// texture checkerboard over the card.
		material.Set( "g_tAmbientOcclusion", Texture.White );
		material.Set( "g_flAmbientOcclusionDirectDiffuse", 0f );
		material.Set( "g_flAmbientOcclusionDirectSpecular", 0f );

		return material;
	}
}
