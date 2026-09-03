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
	private static Texture? _neutralNormalRoughness;

	/// <summary>
	///     Increment when the runtime material contract changes so render-resource
	///     caches do not retain materials built with incomplete shader inputs.
	/// </summary>
	public const int CacheVersion = 2;

	private static Texture NeutralNormalRoughness
	{
		get { return _neutralNormalRoughness ??= BuildNeutralNormalRoughness(); }
	}


	public static Material Create( string name, Texture color, bool anonymous = true )
	{
		ArgumentNullException.ThrowIfNull( color );

		// Material.Create still validates runtime/anonymous material names as
		// resource paths. Supplying the normal material extension prevents
		// FixupResourceName warnings for every generated card and zone marker.
		if ( !name.EndsWith( ".vmat", StringComparison.OrdinalIgnoreCase ) )
			name += ".vmat";

		Material material = Material.Create( name, "complex.shader", anonymous );

		material.Set( "g_tColor", color );

		// A complex material expects an AO texture even when the surface has no
		// baked occlusion. Leaving this unbound displays the engine's missing
		// texture checkerboard over the card.
		material.Set( "g_tAmbientOcclusion", Texture.White );
		material.Set( "g_flAmbientOcclusionDirectDiffuse", 0f );
		material.Set( "g_flAmbientOcclusionDirectSpecular", 0f );

		// complex.shader packs its tangent-space normal and isotropic roughness
		// into the normal surface input. An empty runtime material has no authored
		// default here, so direct/sky lighting samples the missing-texture grid.
		// (0.5, 0.5, 1.0) is a flat normal and blue=1 gives a matte surface.
		material.Set( "g_tNormal", NeutralNormalRoughness );
		material.Set( "g_tRoughness", Texture.White );
		material.Set( "g_tMetalness", Texture.Black );
		material.Set( "g_flRoughness", 1f );
		material.Set( "g_flMetalness", 0f );

		return material;
	}


	private static Texture BuildNeutralNormalRoughness()
	{
		Bitmap bitmap = new Bitmap( 1, 1 );
		bitmap.Clear( new Color( 0.5f, 0.5f, 1f, 1f ) );

		return bitmap.ToTexture();
	}
}
