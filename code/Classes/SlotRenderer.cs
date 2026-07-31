#nullable enable

using Sandbox.UI;
using System;
namespace Sandbox.Classes;

/// <summary>
///     Cached render resources for one MTG zone marker.
/// </summary>
public readonly record struct ZoneSlotTextures
{
	public required Texture  Atlas    { get; init; }
	public required Material Material { get; init; }


	public void ApplyTo( ModelRenderer renderer )
	{
		ArgumentNullException.ThrowIfNull( renderer );
		renderer.Model            = CardMesh.Shared;
		renderer.MaterialOverride = Material;
	}
}

/// <summary>
///     Builds card-sized markers for MTG zones. The marker is duplicated into
///     both halves of the atlas so it works with the shared two-sided card mesh.
/// </summary>
public static class SlotRenderer
{
	private const                         TextFlag                             Centered = TextFlag.Center | TextFlag.DontClip;
	[SkipHotload] private static readonly Dictionary<string, ZoneSlotTextures> Cache    = new Dictionary<string, ZoneSlotTextures>( StringComparer.Ordinal );


	public static ZoneSlotTextures BuildSlot( ZoneType zone, int resolution = 512 )
	{
		resolution = Math.Max( resolution, 128 );
		string key = $"{zone}|{resolution}";

		if ( Cache.TryGetValue( key, out ZoneSlotTextures cached ) && cached.Atlas.IsLoaded )
			return cached;

		int    height = resolution;
		int    width  = Math.Max( (int)MathF.Round( height * CardFaceRenderer.Aspect ), 1 );
		Bitmap face   = new Bitmap( width, height );
		DrawZoneFace( face, zone, width, height );

		Bitmap atlas = new Bitmap( width * 2, height );
		atlas.Clear( Color.Black );
		atlas.DrawBitmap( face, new Rect( 0, 0, width, height ) );
		atlas.DrawBitmap( face, new Rect( width, 0, width, height ) );

		Texture  texture  = atlas.ToTexture();
		Material material = CardMaterialFactory.Create( $"mtgsbox_zone_{zone}_{resolution}", texture );

		ZoneSlotTextures result = new ZoneSlotTextures { Atlas = texture, Material = material };
		Cache[key] = result;

		return result;
	}


	private static void DrawZoneFace( Bitmap bitmap, ZoneType zone, int width, int height )
	{
		Color accent = ZoneColor( zone );
		Rect  area   = new Rect( 0, 0, width, height );
		bitmap.Clear( Color.Lerp( new Color( 0.035f, 0.04f, 0.045f ), accent, 0.16f ) );

		float inset = width * 0.065f;
		bitmap.SetFill( Color.Transparent );
		bitmap.SetPen( accent, width * 0.035f );
		bitmap.DrawRoundRect( Inset( area, inset ), new Margin( width * 0.075f ) );

		string label     = ZoneLabel( zone );
		Rect   labelArea = new Rect( width * 0.08f, height * 0.34f, width * 0.84f, height * 0.32f );
		bitmap.DrawText( new TextRendering.Scope( label, accent, MathF.Min( height * 0.10f, width * 0.15f ), "Roboto", 600 ), labelArea, Centered );
	}


	private static string ZoneLabel( ZoneType zone )
	{
		return zone switch
			   {
				   ZoneType.Library     => "LIBRARY",
				   ZoneType.Hand        => "HAND",
				   ZoneType.Battlefield => "BATTLEFIELD",
				   ZoneType.Graveyard   => "GRAVEYARD",
				   ZoneType.Exile       => "EXILE",
				   ZoneType.Command     => "COMMAND",
				   ZoneType.Stack       => "STACK",
				   ZoneType.Sideboard   => "SIDEBOARD",
				   _                    => "ZONE"
			   };
	}


	private static Color ZoneColor( ZoneType zone )
	{
		return zone switch
			   {
				   ZoneType.Library     => new Color( 0.28f, 0.52f, 0.82f ),
				   ZoneType.Hand        => new Color( 0.36f, 0.68f, 0.92f ),
				   ZoneType.Battlefield => new Color( 0.35f, 0.72f, 0.42f ),
				   ZoneType.Graveyard   => new Color( 0.58f, 0.48f, 0.68f ),
				   ZoneType.Exile       => new Color( 0.88f, 0.78f, 0.38f ),
				   ZoneType.Command     => new Color( 0.9f, 0.5f, 0.22f ),
				   ZoneType.Stack       => new Color( 0.7f, 0.42f, 0.88f ),
				   ZoneType.Sideboard   => new Color( 0.55f, 0.6f, 0.65f ),
				   _                    => Color.White
			   };
	}


	private static Rect Inset( Rect rect, float amount ) { return new Rect( rect.Left + amount, rect.Top + amount, rect.Width - amount * 2f, rect.Height - amount * 2f ); }


	public static void ClearCache() { Cache.Clear(); }
}
