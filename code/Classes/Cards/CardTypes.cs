#nullable enable

using Sandbox.Classes.Cards.Colors;
using Sandbox.Classes.Cards.ManaSymbols;
using System;
using System.Text.Json;
namespace Sandbox.Classes.Cards;

/// <summary>
///     Gameplay and presentation data belonging to one physical card face.
///     Ordinary cards are represented by one synthesized face.
/// </summary>
public sealed record CardFace
{
	public required string                          Object           { get; init; }
	public required string                          Name             { get; init; }
	public          string?                         SourceManaCost   { get; init; }
	public required ManaCost                        ManaCost         { get; init; }
	public          string?                         TypeLine         { get; init; }
	public          string?                         OracleText       { get; init; }
	public          ColorSet?                       Colors           { get; init; }
	public          ColorSet?                       ColorIndicator   { get; init; }
	public          CardPower?                      Power            { get; init; }
	public          CardToughness?                  Toughness        { get; init; }
	public          CardLoyalty?                    Loyalty          { get; init; }
	public          CardDefense?                    Defense          { get; init; }
	public          string?                         Artist           { get; init; }
	public          Guid?                           ArtistId         { get; init; }
	public          Guid?                           IllustrationId   { get; init; }
	public          CardImages?                     Images           { get; init; }
	public          string?                         FlavorName       { get; init; }
	public          string?                         FlavorText       { get; init; }
	public          string?                         PrintedName      { get; init; }
	public          string?                         PrintedText      { get; init; }
	public          string?                         PrintedTypeLine  { get; init; }
	public          Guid?                           OracleId         { get; init; }
	public          string?                         Layout           { get; init; }
	public          decimal?                        ManaValue        { get; init; }
	public          string?                         Watermark        { get; init; }
	public          Dictionary<string, JsonElement> SourceExtensions { get; init; } = [ ];
}

/// <summary>
///     Scryfall power values are symbolic strings and may contain values such as
///     "*", "1+*", and "∞".
/// </summary>
public readonly record struct CardPower( string Value );

/// <summary>
///     Scryfall toughness values are symbolic strings rather than integers.
/// </summary>
public readonly record struct CardToughness( string Value );

/// <summary>
///     Scryfall loyalty values may be numeric, symbolic, or "X".
/// </summary>
public readonly record struct CardLoyalty( string Value );

/// <summary>
///     Scryfall battle defense values are retained exactly as printed.
/// </summary>
public readonly record struct CardDefense( string Value );

/// <summary>
///     Vanguard/Archenemy hand modifiers are signed source strings.
/// </summary>
public readonly record struct HandModifier( string Value );

/// <summary>
///     Vanguard/Archenemy life modifiers are signed source strings.
/// </summary>
public readonly record struct LifeModifier( string Value );

/// <summary>
///     Keyword names are open-ended Scryfall catalog values.
/// </summary>
public sealed record CardKeywords
{
	public string[] Values { get; init; } = [ ];
}

public enum CardFinish
{
	Nonfoil = 0,
	Foil    = 1,
	Etched  = 2
}

/// <summary>
///     Every image rendition currently supplied by Scryfall. Nullable fields
///     preserve cards or faces whose imagery is missing or incomplete.
/// </summary>
public sealed record CardImages
{
	public string?                         Small            { get; init; }
	public string?                         Normal           { get; init; }
	public string?                         Large            { get; init; }
	public string?                         Png              { get; init; }
	public string?                         ArtCrop          { get; init; }
	public string?                         BorderCrop       { get; init; }
	public string?                         Thumb            { get; init; }
	public string?                         Grid             { get; init; }
	public string?                         Display          { get; init; }
	public string?                         Art              { get; init; }
	public string?                         Crop             { get; init; }
	public Dictionary<string, JsonElement> SourceExtensions { get; init; } = [ ];
}
