#nullable enable

using Sandbox.Classes.Cards;
using Sandbox.Classes.Cards.CardFrames;
using Sandbox.Classes.Cards.Colors;
using Sandbox.Classes.Cards.Legality;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Sandbox.Classes.Database.Types;

/// <summary>
/// Lossless normalized representation of one Scryfall Card object.
/// </summary>
public sealed record NormalizedCard
{
	public required CardGameplayData Gameplay { get; init; }
	public required CardPresentationData Presentation { get; init; }
	public required CardIdentifierData Identifiers { get; init; }
	public required CardSetData Set { get; init; }
	public required CardResourceLinks Links { get; init; }
	public required CardSourceMetadata Source { get; init; }
}

public sealed record CardGameplayData
{
	public Guid ScryfallId { get; init; }
	public Guid? OracleId { get; init; }
	public CardLayout Layout { get; init; }
	public string? SourceManaCost { get; init; }
	public required CardFace[] Faces { get; init; }
	public decimal ManaValue { get; init; }
	public ColorSet ColorIdentity { get; init; }
	public ColorSet? Colors { get; init; }
	public ColorSet? ColorIndicator { get; init; }
	public CardDefense? Defense { get; init; }
	public HandModifier? HandModifier { get; init; }
	public required CardKeywords Keywords { get; init; }
	public required FormatLegalities Legalities { get; init; }
	public LifeModifier? LifeModifier { get; init; }
	public CardLoyalty? Loyalty { get; init; }
	public required string Name { get; init; }
	public string? OracleText { get; init; }
	public CardPower? Power { get; init; }
	public ProducedManaSet? ProducedMana { get; init; }
	public bool Reserved { get; init; }
	public CardToughness? Toughness { get; init; }
	public string? TypeLine { get; init; }
	public RelatedCard[]? AllParts { get; init; }
	public int? EdhrecRank { get; init; }
	public int? PennyRank { get; init; }
	public bool? GameChanger { get; init; }
}

public sealed record CardPresentationData
{
	public string? Artist { get; init; }
	public Guid[]? ArtistIds { get; init; }
	public int[]? AttractionLights { get; init; }
	public bool Booster { get; init; }
	public BorderColor BorderColor { get; init; }
	public Guid? CardBack { get; init; }
	public required string CollectorNumber { get; init; }
	public bool? ContentWarning { get; init; }
	public bool Digital { get; init; }
	public CardFinish[] Finishes { get; init; } = [];
	public bool Foil { get; init; }
	public bool Nonfoil { get; init; }
	public string? FlavorName { get; init; }
	public string? FlavorText { get; init; }
	public CardFrame Frame { get; init; }
	public FrameEffect[]? FrameEffects { get; init; }
	public bool FullArt { get; init; }
	public string[] Games { get; init; } = [];
	public bool HighResolutionImage { get; init; }
	public Guid? IllustrationId { get; init; }
	public CardImages? Images { get; init; }
	public required string ImageStatus { get; init; }
	public DateTimeOffset? ImageUpdatedAt { get; init; }
	public bool Oversized { get; init; }
	public required CardPrices Prices { get; init; }
	public string? PrintedName { get; init; }
	public string? PrintedText { get; init; }
	public string? PrintedTypeLine { get; init; }
	public bool Promo { get; init; }
	public string[]? PromoTypes { get; init; }
	public CardRarity Rarity { get; init; }
	public DateTime? ReleasedAt { get; init; }
	public bool Reprint { get; init; }
	public string? SecurityStamp { get; init; }
	public bool StorySpotlight { get; init; }
	public bool Textless { get; init; }
	public bool Variation { get; init; }
	public Guid? VariationOf { get; init; }
	public string? Watermark { get; init; }
	public CardPreview? Preview { get; init; }
}

public sealed record CardIdentifierData
{
	public int? ArenaId { get; init; }
	public int? MtgoId { get; init; }
	public int? MtgoFoilId { get; init; }
	public int[]? MultiverseIds { get; init; }
	public int? TcgplayerId { get; init; }
	public int? TcgplayerEtchedId { get; init; }
	public int? CardmarketId { get; init; }
	public string? ResourceId { get; init; }
}

public sealed record CardSetData
{
	public Guid Id { get; init; }
	public required string Code { get; init; }
	public required string Name { get; init; }
	public required string Type { get; init; }
	public required string ApiUri { get; init; }
	public required string SearchUri { get; init; }
	public required string ScryfallUri { get; init; }
}

public sealed record CardResourceLinks
{
	public required string ApiUri { get; init; }
	public required string ScryfallUri { get; init; }
	public required string PrintsSearchUri { get; init; }
	public required string RulingsUri { get; init; }
	public Dictionary<string, string>? PurchaseUris { get; init; }
	public Dictionary<string, string> RelatedUris { get; init; } =
		new( StringComparer.OrdinalIgnoreCase );
}

public sealed record CardSourceMetadata
{
	public required string Object { get; init; }
	public required string Language { get; init; }
	public Dictionary<string, JsonElement> Extensions { get; init; } = [];
}

public sealed record RelatedCard
{
	public Guid Id { get; init; }
	public required string Object { get; init; }
	public required string Component { get; init; }
	public required string Name { get; init; }
	public required string TypeLine { get; init; }
	public required string ApiUri { get; init; }
	public Dictionary<string, JsonElement> SourceExtensions { get; init; } = [];
}

public sealed record CardPrices
{
	public string? Usd { get; init; }
	public string? UsdFoil { get; init; }
	public string? UsdEtched { get; init; }
	public string? Eur { get; init; }
	public string? EurFoil { get; init; }
	public string? EurEtched { get; init; }
	public string? Tix { get; init; }
	public Dictionary<string, JsonElement> SourceExtensions { get; init; } = [];
}

public sealed record CardPreview
{
	public DateTime? PreviewedAt { get; init; }
	public string? SourceUri { get; init; }
	public string? Source { get; init; }
	public Dictionary<string, JsonElement> SourceExtensions { get; init; } = [];
}
