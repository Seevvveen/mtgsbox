#nullable enable

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Sandbox.Classes.CardDatabase;

public sealed class ScryfallCardDto
{
	[JsonPropertyName( "object" )] public string Object { get; set; } = "";

	[JsonPropertyName( "id" )] public string Id { get; set; } = "";

	[JsonPropertyName( "oracle_id" )] public string? OracleId { get; set; }

	[JsonPropertyName( "name" )] public string Name { get; set; } = "";

	[JsonPropertyName( "layout" )] public string Layout { get; set; } = "";

	/*
	 * Present for multifaced cards.
	 * Null for ordinary single-faced cards.
	 */
	[JsonPropertyName( "card_faces" )] public ScryfallCardFaceDto[]? CardFaces { get; set; }

	/*
	 * These may be absent at the card level when their values are instead
	 * provided independently by card_faces.
	 */
	[JsonPropertyName( "mana_cost" )] public string? ManaCost { get; set; }

	[JsonPropertyName( "colors" )] public string[]? Colors { get; set; }

	[JsonPropertyName( "color_indicator" )] public string[]? ColorIndicator { get; set; }

	[JsonPropertyName( "type_line" )] public string? TypeLine { get; set; }

	[JsonPropertyName( "oracle_text" )] public string? OracleText { get; set; }

	[JsonPropertyName( "power" )] public string? Power { get; set; }

	[JsonPropertyName( "toughness" )] public string? Toughness { get; set; }

	[JsonPropertyName( "loyalty" )] public string? Loyalty { get; set; }

	[JsonPropertyName( "defense" )] public string? Defense { get; set; }

	[JsonPropertyName( "image_uris" )] public ScryfallImageUrisDto? ImageUris { get; set; }

	[JsonPropertyName( "artist" )] public string? Artist { get; set; }

	[JsonPropertyName( "illustration_id" )] public string? IllustrationId { get; set; }

	[JsonPropertyName( "flavor_text" )] public string? FlavorText { get; set; }

	[JsonPropertyName( "flavor_name" )] public string? FlavorName { get; set; }

	[JsonPropertyName( "printed_name" )] public string? PrintedName { get; set; }

	[JsonPropertyName( "printed_text" )] public string? PrintedText { get; set; }

	[JsonPropertyName( "printed_type_line" )] public string? PrintedTypeLine { get; set; }

	[JsonPropertyName( "watermark" )] public string? Watermark { get; set; }

	// These are card-wide properties, not individual-face properties.

	[JsonPropertyName( "cmc" )] public decimal Cmc { get; set; }

	[JsonPropertyName( "color_identity" )] public string[] ColorIdentity { get; set; } = [ ];

	[JsonPropertyName( "keywords" )] public string[] Keywords { get; set; } = [ ];

	[JsonPropertyName( "legalities" )] public Dictionary<string, string> Legalities { get; set; } = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

	[JsonPropertyName( "reserved" )] public bool Reserved { get; set; }

	[JsonPropertyName( "card_back_id" )] public string? CardBackId { get; set; }

	[JsonPropertyName( "set_id" )] public string SetId { get; set; } = "";

	[JsonPropertyName( "released_at" )] public DateTime? ReleasedAt { get; set; }

	// ---------------------------------------------------------------------
	// Additional identifiers
	// ---------------------------------------------------------------------

	[JsonPropertyName( "arena_id" )] public int? ArenaId { get; set; }

	[JsonPropertyName( "mtgo_id" )] public int? MtgoId { get; set; }

	[JsonPropertyName( "mtgo_foil_id" )] public int? MtgoFoilId { get; set; }

	[JsonPropertyName( "multiverse_ids" )] public int[]? MultiverseIds { get; set; }

	[JsonPropertyName( "tcgplayer_id" )] public int? TcgplayerId { get; set; }

	[JsonPropertyName( "tcgplayer_etched_id" )] public int? TcgplayerEtchedId { get; set; }

	[JsonPropertyName( "cardmarket_id" )] public int? CardmarketId { get; set; }

	[JsonPropertyName( "resource_id" )] public string? ResourceId { get; set; }

	// ---------------------------------------------------------------------
	// Scryfall metadata and links
	// ---------------------------------------------------------------------

	[JsonPropertyName( "lang" )] public string Lang { get; set; } = "";

	[JsonPropertyName( "prints_search_uri" )] public string PrintsSearchUri { get; set; } = "";

	[JsonPropertyName( "rulings_uri" )] public string RulingsUri { get; set; } = "";

	[JsonPropertyName( "scryfall_uri" )] public string ScryfallUri { get; set; } = "";

	[JsonPropertyName( "uri" )] public string Uri { get; set; } = "";

	[JsonPropertyName( "all_parts" )] public ScryfallRelatedCardDto[]? AllParts { get; set; }

	// ---------------------------------------------------------------------
	// Additional gameplay data
	// ---------------------------------------------------------------------

	[JsonPropertyName( "edhrec_rank" )] public int? EdhrecRank { get; set; }

	[JsonPropertyName( "penny_rank" )] public int? PennyRank { get; set; }

	[JsonPropertyName( "game_changer" )] public bool? GameChanger { get; set; }

	[JsonPropertyName( "hand_modifier" )] public string? HandModifier { get; set; }

	[JsonPropertyName( "life_modifier" )] public string? LifeModifier { get; set; }

	[JsonPropertyName( "produced_mana" )] public string[]? ProducedMana { get; set; }

	// ---------------------------------------------------------------------
	// Print and presentation data
	// ---------------------------------------------------------------------

	[JsonPropertyName( "artist_ids" )] public string[]? ArtistIds { get; set; }

	[JsonPropertyName( "attraction_lights" )] public int[]? AttractionLights { get; set; }

	[JsonPropertyName( "booster" )] public bool Booster { get; set; }

	[JsonPropertyName( "border_color" )] public string BorderColor { get; set; } = "";

	[JsonPropertyName( "collector_number" )] public string CollectorNumber { get; set; } = "";

	[JsonPropertyName( "content_warning" )] public bool? ContentWarning { get; set; }

	[JsonPropertyName( "digital" )] public bool Digital { get; set; }

	[JsonPropertyName( "finishes" )] public string[] Finishes { get; set; } = [ ];

	[JsonPropertyName( "foil" )] public bool Foil { get; set; }

	[JsonPropertyName( "nonfoil" )] public bool Nonfoil { get; set; }

	[JsonPropertyName( "frame" )] public string Frame { get; set; } = "";

	[JsonPropertyName( "frame_effects" )] public string[]? FrameEffects { get; set; }

	[JsonPropertyName( "full_art" )] public bool FullArt { get; set; }

	[JsonPropertyName( "games" )] public string[] Games { get; set; } = [ ];

	[JsonPropertyName( "highres_image" )] public bool HighresImage { get; set; }

	[JsonPropertyName( "image_status" )] public string ImageStatus { get; set; } = "";

	[JsonPropertyName( "image_updated_at" )] public DateTimeOffset? ImageUpdatedAt { get; set; }

	[JsonPropertyName( "oversized" )] public bool Oversized { get; set; }

	[JsonPropertyName( "prices" )] public ScryfallPricesDto Prices { get; set; } = new ScryfallPricesDto();

	[JsonPropertyName( "promo" )] public bool Promo { get; set; }

	[JsonPropertyName( "promo_types" )] public string[]? PromoTypes { get; set; }

	[JsonPropertyName( "rarity" )] public string Rarity { get; set; } = "";

	[JsonPropertyName( "reprint" )] public bool Reprint { get; set; }

	[JsonPropertyName( "security_stamp" )] public string? SecurityStamp { get; set; }

	[JsonPropertyName( "story_spotlight" )] public bool StorySpotlight { get; set; }

	[JsonPropertyName( "textless" )] public bool Textless { get; set; }

	[JsonPropertyName( "variation" )] public bool Variation { get; set; }

	[JsonPropertyName( "variation_of" )] public string? VariationOf { get; set; }

	[JsonPropertyName( "purchase_uris" )] public Dictionary<string, string>? PurchaseUris { get; set; }

	[JsonPropertyName( "related_uris" )] public Dictionary<string, string> RelatedUris { get; set; } = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

	// ---------------------------------------------------------------------
	// Set data
	// ---------------------------------------------------------------------

	[JsonPropertyName( "set" )] public string SetCode { get; set; } = "";

	[JsonPropertyName( "set_name" )] public string SetName { get; set; } = "";

	[JsonPropertyName( "set_type" )] public string SetType { get; set; } = "";

	[JsonPropertyName( "set_uri" )] public string SetUri { get; set; } = "";

	[JsonPropertyName( "set_search_uri" )] public string SetSearchUri { get; set; } = "";

	[JsonPropertyName( "scryfall_set_uri" )] public string ScryfallSetUri { get; set; } = "";

	[JsonPropertyName( "preview" )] public ScryfallPreviewDto? Preview { get; set; }

	/// <summary>
	///     Retains fields introduced by Scryfall before this DTO is updated.
	/// </summary>
	[JsonExtensionData] public Dictionary<string, JsonElement> AdditionalFields { get; set; } = [ ];
}

/// <summary>
///     Direct mirror of an entry inside a card's card_faces array.
/// </summary>
public sealed class ScryfallCardFaceDto
{
	[JsonPropertyName( "object" )] public string Object { get; set; } = "";

	[JsonPropertyName( "name" )] public string Name { get; set; } = "";

	[JsonPropertyName( "mana_cost" )] public string? ManaCost { get; set; }

	[JsonPropertyName( "type_line" )] public string? TypeLine { get; set; }

	[JsonPropertyName( "oracle_text" )] public string? OracleText { get; set; }

	[JsonPropertyName( "colors" )] public string[]? Colors { get; set; }

	[JsonPropertyName( "color_indicator" )] public string[]? ColorIndicator { get; set; }

	[JsonPropertyName( "power" )] public string? Power { get; set; }

	[JsonPropertyName( "toughness" )] public string? Toughness { get; set; }

	[JsonPropertyName( "loyalty" )] public string? Loyalty { get; set; }

	[JsonPropertyName( "defense" )] public string? Defense { get; set; }

	[JsonPropertyName( "artist" )] public string? Artist { get; set; }

	[JsonPropertyName( "artist_id" )] public string? ArtistId { get; set; }

	[JsonPropertyName( "illustration_id" )] public string? IllustrationId { get; set; }

	[JsonPropertyName( "image_uris" )] public ScryfallImageUrisDto? ImageUris { get; set; }

	[JsonPropertyName( "flavor_text" )] public string? FlavorText { get; set; }

	[JsonPropertyName( "flavor_name" )] public string? FlavorName { get; set; }

	[JsonPropertyName( "printed_name" )] public string? PrintedName { get; set; }

	[JsonPropertyName( "printed_text" )] public string? PrintedText { get; set; }

	[JsonPropertyName( "printed_type_line" )] public string? PrintedTypeLine { get; set; }

	[JsonPropertyName( "oracle_id" )] public string? OracleId { get; set; }

	[JsonPropertyName( "layout" )] public string? Layout { get; set; }

	[JsonPropertyName( "cmc" )] public decimal? Cmc { get; set; }

	[JsonPropertyName( "watermark" )] public string? Watermark { get; set; }

	[JsonExtensionData] public Dictionary<string, JsonElement> AdditionalFields { get; set; } = [ ];
}

/// <summary>
///     Direct mirror of an entry inside a card's all_parts array.
/// </summary>
public sealed class ScryfallRelatedCardDto
{
	[JsonPropertyName( "id" )] public string Id { get; set; } = "";

	[JsonPropertyName( "object" )] public string Object { get; set; } = "";

	[JsonPropertyName( "component" )] public string Component { get; set; } = "";

	[JsonPropertyName( "name" )] public string Name { get; set; } = "";

	[JsonPropertyName( "type_line" )] public string TypeLine { get; set; } = "";

	[JsonPropertyName( "uri" )] public string Uri { get; set; } = "";

	[JsonExtensionData] public Dictionary<string, JsonElement> AdditionalFields { get; set; } = [ ];
}

/// <summary>
///     Direct mirror of image_uris on either a card or card face.
/// </summary>
public sealed class ScryfallImageUrisDto
{
	[JsonPropertyName( "small" )] public string? Small { get; set; }

	[JsonPropertyName( "normal" )] public string? Normal { get; set; }

	[JsonPropertyName( "large" )] public string? Large { get; set; }

	[JsonPropertyName( "png" )] public string? Png { get; set; }

	[JsonPropertyName( "art_crop" )] public string? ArtCrop { get; set; }

	[JsonPropertyName( "border_crop" )] public string? BorderCrop { get; set; }

	[JsonPropertyName( "thumb" )] public string? Thumb { get; set; }

	[JsonPropertyName( "grid" )] public string? Grid { get; set; }

	[JsonPropertyName( "display" )] public string? Display { get; set; }

	[JsonPropertyName( "art" )] public string? Art { get; set; }

	[JsonPropertyName( "crop" )] public string? Crop { get; set; }

	[JsonExtensionData] public Dictionary<string, JsonElement> AdditionalFields { get; set; } = [ ];
}

/// <summary>
///     Scryfall represents prices as nullable JSON strings, not numbers.
/// </summary>
public sealed class ScryfallPricesDto
{
	[JsonPropertyName( "usd" )] public string? Usd { get; set; }

	[JsonPropertyName( "usd_foil" )] public string? UsdFoil { get; set; }

	[JsonPropertyName( "usd_etched" )] public string? UsdEtched { get; set; }

	[JsonPropertyName( "eur" )] public string? Eur { get; set; }

	[JsonPropertyName( "eur_foil" )] public string? EurFoil { get; set; }

	[JsonPropertyName( "eur_etched" )] public string? EurEtched { get; set; }

	[JsonPropertyName( "tix" )] public string? Tix { get; set; }

	[JsonExtensionData] public Dictionary<string, JsonElement> AdditionalFields { get; set; } = [ ];
}

/// <summary>
///     Direct mirror of the optional preview object.
/// </summary>
public sealed class ScryfallPreviewDto
{
	[JsonPropertyName( "previewed_at" )] public DateTime? PreviewedAt { get; set; }

	[JsonPropertyName( "source_uri" )] public string? SourceUri { get; set; }

	[JsonPropertyName( "source" )] public string? Source { get; set; }

	[JsonExtensionData] public Dictionary<string, JsonElement> AdditionalFields { get; set; } = [ ];
}
