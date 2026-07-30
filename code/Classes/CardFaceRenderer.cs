#nullable enable

using Sandbox.Classes.Cards;
using Sandbox.Classes.Database.Types;
using Sandbox.UI;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sandbox.Classes;

/// <summary>
/// Client-side resources for one physical card printing. Atlas contains the
/// front image in its left half and the physical back in its right half,
/// matching <see cref="CardMesh"/>'s UV layout.
/// </summary>
public readonly record struct CardTextures
{
	public required Texture Front { get; init; }
	public required Texture Back { get; init; }
	public required Texture Atlas { get; init; }
	public required Material Material { get; init; }
	public bool HasPrintedBack { get; init; }

	/// <summary>
	/// Applies the shared procedural mesh and this printing's atlas material.
	/// </summary>
	public void ApplyTo( ModelRenderer renderer )
	{
		ArgumentNullException.ThrowIfNull( renderer );
		renderer.Model = CardMesh.Shared;
		renderer.MaterialOverride = Material;
	}
}

/// <summary>
/// Loads the images supplied by normalized card records and creates the
/// two-sided texture atlas used by the procedural card mesh.
/// </summary>
public static class CardFaceRenderer
{
	public const float Aspect = 63f / 88f;

	/// <summary>
	/// Normalized card records omit the shared back of ordinary Magic cards.
	/// </summary>
	public static string StandardBackImageUrl { get; set; } =
		"https://cards.scryfall.io/back.png";

	private static readonly object CacheLock = new();
	private static readonly Dictionary<string, Task<CardTextures>> Cache =
		new( StringComparer.Ordinal );

	/// <summary>
	/// Builds a card-back-only material without requiring a card identity.
	/// This is safe for clients that are not allowed to know a hidden card.
	/// </summary>
	public static Task<CardTextures> BuildConcealedAsync()
	{
		if ( Application.IsHeadless )
		{
			throw new InvalidOperationException(
				"Card textures cannot be created on a headless host." );
		}

		string key = $"concealed|{StandardBackImageUrl}";

		lock ( CacheLock )
		{
			if ( Cache.TryGetValue( key, out Task<CardTextures>? cached ) )
				return cached;

			Task<CardTextures> created = BuildConcealedCoreAsync();
			Cache.Add( key, created );
			return created;
		}
	}

	/// <summary>
	/// Builds or retrieves render resources for one exact printing.
	/// Double-faced cards use their supplied second face as the physical back.
	/// </summary>
	public static Task<CardTextures> BuildCardAsync(
		NormalizedCard card,
		bool concealed = false )
	{
		ArgumentNullException.ThrowIfNull( card );

		if ( Application.IsHeadless )
		{
			throw new InvalidOperationException(
				"Card textures cannot be created on a headless host." );
		}

		string? frontUrl = GetImageUrl(
			card.Gameplay.Faces[0].Images )
			?? GetImageUrl( card.Presentation.Images );
		string? printedBackUrl = GetPrintedBackUrl( card );
		string backUrl = printedBackUrl ?? StandardBackImageUrl;
		string key =
			$"{card.Gameplay.ScryfallId:N}|{frontUrl}|{backUrl}|" +
			$"{concealed}";

		lock ( CacheLock )
		{
			if ( Cache.TryGetValue( key, out Task<CardTextures>? cached ) )
				return cached;

			Task<CardTextures> created = BuildCardCoreAsync(
				card,
				frontUrl,
				printedBackUrl,
				concealed );
			Cache.Add( key, created );
			return created;
		}
	}

	public static bool HasPrintedBack( NormalizedCard card )
	{
		ArgumentNullException.ThrowIfNull( card );
		return GetPrintedBackUrl( card ) is not null;
	}

	private static async Task<CardTextures> BuildCardCoreAsync(
		NormalizedCard card,
		string? frontUrl,
		string? printedBackUrl,
		bool concealed )
	{
		Texture front = await LoadFrontAsync(
			frontUrl,
			card.Gameplay.Name );
		(Texture Back, bool HasPrintedBack) backResult =
			await LoadBackAsync(
				printedBackUrl,
				card.Gameplay.Name );
		Texture back = backResult.Back;

		Texture atlas = await CreateAtlasAsync(
			concealed ? back : front,
			back );

		Material material = Material.Create(
			$"mtgsbox_card_{card.Gameplay.ScryfallId:N}_" +
			$"{(concealed ? "concealed" : "visible")}",
			"complex.shader",
			anonymous: true );
		material.Set( "g_tColor", atlas );

		return new CardTextures
		{
			Front = concealed ? back : front,
			Back = back,
			Atlas = atlas,
			Material = material,
			HasPrintedBack = backResult.HasPrintedBack
		};
	}

	private static async Task<CardTextures> BuildConcealedCoreAsync()
	{
		Texture back;

		try
		{
			back = await LoadTextureAsync( StandardBackImageUrl );
		}
		catch ( Exception exception )
		{
			Log.Warning(
				$"Unable to load the standard Magic card back from " +
				$"'{StandardBackImageUrl}': {exception.Message}" );
			back = RenderFallbackBack();
		}

		Texture atlas = await CreateAtlasAsync( back, back );
		Material material = Material.Create(
			"mtgsbox_card_concealed",
			"complex.shader",
			anonymous: true );
		material.Set( "g_tColor", atlas );

		return new CardTextures
		{
			Front = back,
			Back = back,
			Atlas = atlas,
			Material = material,
			HasPrintedBack = false
		};
	}

	private static async Task<Texture> LoadFrontAsync(
		string? url,
		string cardName )
	{
		if ( !string.IsNullOrWhiteSpace( url ) )
		{
			try
			{
				return await LoadTextureAsync( url );
			}
			catch ( Exception exception )
			{
				Log.Warning(
					$"Unable to load the front image for '{cardName}' " +
					$"from '{url}': {exception.Message}" );
			}
		}
		else
		{
			Log.Warning(
				$"Card printing '{cardName}' has no front image URL." );
		}

		return RenderMissingFront();
	}

	private static async Task<(Texture Back, bool HasPrintedBack)>
		LoadBackAsync(
			string? printedBackUrl,
			string cardName )
	{
		if ( !string.IsNullOrWhiteSpace( printedBackUrl ) )
		{
			try
			{
				return (
					await LoadTextureAsync( printedBackUrl ),
					true );
			}
			catch ( Exception exception )
			{
				Log.Warning(
					$"Unable to load the printed back face for " +
					$"'{cardName}' from '{printedBackUrl}': " +
					$"{exception.Message}" );
			}
		}

		try
		{
			return (
				await LoadTextureAsync( StandardBackImageUrl ),
				false );
		}
		catch ( Exception exception )
		{
			Log.Warning(
				$"Unable to load the standard Magic card back from " +
				$"'{StandardBackImageUrl}': {exception.Message}" );
			return (RenderFallbackBack(), false);
		}
	}

	private static async Task<Texture> LoadTextureAsync( string url )
	{
		Texture texture = await Texture.LoadAsync(
			url,
			warnOnMissing: false );

		if ( !texture.IsLoaded || texture.IsError )
		{
			throw new InvalidOperationException(
				"The downloaded texture was invalid." );
		}

		return texture;
	}

	private static string? GetPrintedBackUrl( NormalizedCard card )
	{
		if ( card.Gameplay.Faces.Length < 2 )
			return null;

		// Split/adventure-style multi-face records have no per-face images.
		// A supplied second face image indicates a physical printed back.
		return GetImageUrl( card.Gameplay.Faces[1].Images );
	}

	private static string? GetImageUrl( CardImages? images )
	{
		if ( images is null )
			return null;

		return FirstNonEmpty(
			images.Large,
			images.Png,
			images.Normal,
			images.Display,
			images.Grid,
			images.Small,
			images.BorderCrop,
			images.Crop,
			images.Thumb );
	}

	private static string? FirstNonEmpty( params string?[] values )
	{
		foreach ( string? value in values )
		{
			if ( !string.IsNullOrWhiteSpace( value ) )
				return value;
		}

		return null;
	}

	private static async Task<Texture> CreateAtlasAsync(
		Texture front,
		Texture back )
	{
		Task<Bitmap> frontBitmapTask = GetBitmapAsync( front );
		Task<Bitmap> backBitmapTask = GetBitmapAsync( back );

		Bitmap frontBitmap = await frontBitmapTask;
		Bitmap backBitmap = await backBitmapTask;
		int height = Math.Max(
			Math.Max( front.Height, back.Height ),
			256 );
		int width = Math.Max(
			(int)MathF.Round( height * Aspect ),
			1 );

		var atlas = new Bitmap( width * 2, height );
		atlas.Clear( Color.Black );
		atlas.DrawBitmap(
			frontBitmap,
			new Rect( 0, 0, width, height ) );
		atlas.DrawBitmap(
			backBitmap,
			new Rect( width, 0, width, height ) );
		return atlas.ToTexture();
	}

	private static Task<Bitmap> GetBitmapAsync( Texture texture )
	{
		var completion = new TaskCompletionSource<Bitmap>(
			TaskCreationOptions.RunContinuationsAsynchronously );

		texture.GetBitmapAsync(
			bitmap => completion.TrySetResult( bitmap ),
			mip: 0 );

		return completion.Task;
	}

	private static Texture RenderMissingFront()
	{
		const int width = 672;
		const int height = 936;
		var bitmap = new Bitmap( width, height );
		bitmap.Clear( new Color( 0.12f, 0.12f, 0.12f ) );
		bitmap.SetPen( new Color( 0.8f, 0.15f, 0.2f ), 24f );
		bitmap.DrawRoundRect(
			new Rect( 24, 24, width - 48, height - 48 ),
			new Margin( 36 ) );
		return bitmap.ToTexture();
	}

	private static Texture RenderFallbackBack()
	{
		const int width = 672;
		const int height = 936;
		var bitmap = new Bitmap( width, height );
		bitmap.Clear( new Color( 0.08f, 0.06f, 0.04f ) );
		bitmap.SetFill( new Color( 0.16f, 0.24f, 0.36f ) );
		bitmap.DrawRoundRect(
			new Rect( 42, 42, width - 84, height - 84 ),
			new Margin( 48 ) );
		bitmap.SetFill( new Color( 0.5f, 0.22f, 0.08f ) );
		bitmap.DrawCircle(
			new Vector2( width / 2f, height / 2f ),
			width * 0.27f );
		return bitmap.ToTexture();
	}

	public static void ClearCache()
	{
		lock ( CacheLock )
			Cache.Clear();
	}
}
