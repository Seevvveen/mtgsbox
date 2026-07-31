#nullable enable

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Sandbox.Classes.Database.Types;

public sealed class ScryfallListDto<T>
{
	[JsonPropertyName( "object" )] public string Object { get; set; } = "";

	[JsonPropertyName( "has_more" )] public bool HasMore { get; set; }

	[JsonPropertyName( "next_page" )] public string? NextPage { get; set; }

	[JsonPropertyName( "data" )] public T[] Data { get; set; } = [ ];

	[JsonExtensionData] public Dictionary<string, JsonElement> AdditionalFields { get; set; } = [ ];
}

public sealed class ScryfallSetDto
{
	[JsonPropertyName( "object" )] public string Object { get; set; } = "";

	[JsonPropertyName( "id" )] public string Id { get; set; } = "";

	[JsonPropertyName( "code" )] public string Code { get; set; } = "";

	[JsonPropertyName( "mtgo_code" )] public string? MtgoCode { get; set; }

	[JsonPropertyName( "arena_code" )] public string? ArenaCode { get; set; }

	[JsonPropertyName( "tcgplayer_id" )] public int? TcgplayerId { get; set; }

	[JsonPropertyName( "name" )] public string Name { get; set; } = "";

	[JsonPropertyName( "set_type" )] public string SetType { get; set; } = "";

	[JsonPropertyName( "released_at" )] public DateTime? ReleasedAt { get; set; }

	[JsonPropertyName( "block_code" )] public string? BlockCode { get; set; }

	[JsonPropertyName( "block" )] public string? Block { get; set; }

	[JsonPropertyName( "parent_set_code" )] public string? ParentSetCode { get; set; }

	[JsonPropertyName( "card_count" )] public int CardCount { get; set; }

	[JsonPropertyName( "printed_size" )] public int? PrintedSize { get; set; }

	[JsonPropertyName( "digital" )] public bool Digital { get; set; }

	[JsonPropertyName( "foil_only" )] public bool FoilOnly { get; set; }

	[JsonPropertyName( "nonfoil_only" )] public bool NonfoilOnly { get; set; }

	[JsonPropertyName( "scryfall_uri" )] public string ScryfallUri { get; set; } = "";

	[JsonPropertyName( "uri" )] public string Uri { get; set; } = "";

	[JsonPropertyName( "icon_svg_uri" )] public string IconSvgUri { get; set; } = "";

	[JsonPropertyName( "search_uri" )] public string SearchUri { get; set; } = "";

	[JsonExtensionData] public Dictionary<string, JsonElement> AdditionalFields { get; set; } = [ ];
}

public sealed class ScryfallRulingDto
{
	[JsonPropertyName( "object" )] public string Object { get; set; } = "";

	[JsonPropertyName( "oracle_id" )] public string OracleId { get; set; } = "";

	[JsonPropertyName( "source" )] public string Source { get; set; } = "";

	[JsonPropertyName( "published_at" )] public DateTime PublishedAt { get; set; }

	[JsonPropertyName( "comment" )] public string Comment { get; set; } = "";

	[JsonExtensionData] public Dictionary<string, JsonElement> AdditionalFields { get; set; } = [ ];
}
