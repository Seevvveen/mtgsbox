using Sandbox.Classes.CardDatabase;
using System;
namespace Sandbox.Classes.Cards.Colors.Util;

#nullable enable


/// <summary>
/// Maps Scryfall color-related DTO fields without merging distinct card faces.
/// Each face's Colors field must be normalized separately when card-level
/// Colors is null.
/// </summary>
public static class ScryfallColorManaNormalizer
{
	public static ColorSet? NormalizeCardColors( ScryfallCardDto dto )
	{
		ArgumentNullException.ThrowIfNull( dto );

		return ColorSet.FromNullableScryfall( dto.Colors );
	}

	public static ProducedManaSet? NormalizeProducedMana( string[]? producedMana )
	{
		return ProducedManaSet.FromNullableScryfall( producedMana );
	}

	public static ColorSet? NormalizeFaceColors( string[]? faceColors )
	{
		return ColorSet.FromNullableScryfall( faceColors );
	}
}
