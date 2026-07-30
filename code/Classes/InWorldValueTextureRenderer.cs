#nullable enable

using Sandbox.UI;
using System.Collections.Generic;

namespace Sandbox.Classes;

public readonly record struct InWorldValueStyle
{
	public Color Background { get; init; }
	public Color Foreground { get; init; }
	public Color Border { get; init; }
	public string Font { get; init; }
	public int FontWeight { get; init; }

	public static InWorldValueStyle Default => new()
	{
		Background = new Color( 0.035f, 0.04f, 0.05f, 0.96f ),
		Foreground = Color.White,
		Border = new Color( 0.3f, 0.62f, 0.92f ),
		Font = "Roboto",
		FontWeight = 700
	};

	public InWorldValueStyle WithAccent( Color accent ) => this with
	{
		Border = accent
	};
}

/// <summary>
/// Builds cached label textures for zone values, mana, turn markers, card
/// statuses, flip hints, and action buttons.
/// </summary>
public static class InWorldValueTextureRenderer
{
	public const int Width = 512;
	public const int Height = 160;
	public const float Aspect = (float)Width / Height;

	[SkipHotload]
	private static readonly Dictionary<
		(string Text, InWorldValueStyle Style),
		Texture> Cache = [];

	private const TextFlag Centered =
		TextFlag.Center | TextFlag.DontClip;

	public static Texture BuildLabel(
		string text,
		InWorldValueStyle? requestedStyle = null )
	{
		text ??= string.Empty;
		InWorldValueStyle style =
			requestedStyle ?? InWorldValueStyle.Default;
		var key = (text, style);

		if ( Cache.TryGetValue( key, out Texture? cached ) &&
			cached.IsLoaded )
		{
			return cached;
		}

		var area = new Rect( 0, 0, Width, Height );
		var bitmap = new Bitmap( Width, Height );
		bitmap.Clear( Color.Transparent );
		bitmap.SetFill( style.Background );
		bitmap.SetPen( style.Border, Height * 0.055f );
		bitmap.DrawRoundRect(
			Inset( area, Height * 0.1f ),
			new Margin( Height * 0.48f ) );

		float fontSize = Height * 0.46f;

		if ( text.Length > 9 )
			fontSize *= 9f / text.Length;

		var textArea = new Rect(
			area.Left,
			area.Top - Height * 0.04f,
			area.Width,
			area.Height );
		bitmap.DrawText(
			new TextRendering.Scope(
				text,
				style.Foreground,
				fontSize,
				string.IsNullOrWhiteSpace( style.Font )
					? "Roboto"
					: style.Font,
				style.FontWeight <= 0
					? 700
					: style.FontWeight ),
			textArea,
			Centered );

		Texture texture = bitmap.ToTexture();
		Cache[key] = texture;
		return texture;
	}

	public static void ClearCache() => Cache.Clear();

	private static Rect Inset( Rect rect, float amount ) => new(
		rect.Left + amount,
		rect.Top + amount,
		rect.Width - amount * 2f,
		rect.Height - amount * 2f );
}
