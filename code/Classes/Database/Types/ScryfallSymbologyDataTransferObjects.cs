#nullable enable

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox.Classes.CardDatabase;

/// <summary>
/// Direct mirror of Scryfall's /symbology list response.
/// </summary>
public sealed class ScryfallSymbologyDto
{
	[JsonPropertyName( "object" )]
	public string Object { get; set; } = "";

	[JsonPropertyName( "has_more" )]
	public bool HasMore { get; set; }

	[JsonPropertyName( "data" )]
	public ScryfallCardSymbolDto[] Data { get; set; } = [];

	[JsonExtensionData]
	public Dictionary<string, JsonElement> AdditionalFields { get; set; } = [];
}

/// <summary>
/// Direct mirror of one Scryfall card-symbol object.
/// </summary>
public sealed class ScryfallCardSymbolDto
{
	[JsonPropertyName( "object" )]
	public string Object { get; set; } = "";

	[JsonPropertyName( "symbol" )]
	public string Symbol { get; set; } = "";

	[JsonPropertyName( "loose_variant" )]
	public string? LooseVariant { get; set; }

	[JsonPropertyName( "svg_uri" )]
	public string? SvgUri { get; set; }

	[JsonPropertyName( "english" )]
	public string English { get; set; } = "";

	[JsonPropertyName( "transposable" )]
	public bool Transposable { get; set; }

	[JsonPropertyName( "represents_mana" )]
	public bool RepresentsMana { get; set; }

	[JsonPropertyName( "appears_in_mana_costs" )]
	public bool AppearsInManaCosts { get; set; }

	[JsonPropertyName( "hybrid" )]
	public bool Hybrid { get; set; }

	[JsonPropertyName( "phyrexian" )]
	public bool Phyrexian { get; set; }

	[JsonPropertyName( "funny" )]
	public bool Funny { get; set; }

	[JsonPropertyName( "mana_value" )]
	public decimal? ManaValue { get; set; }

	/// <summary>
	/// Legacy alias still emitted in the live response.
	/// </summary>
	[JsonPropertyName( "cmc" )]
	public decimal? Cmc { get; set; }

	[JsonPropertyName( "colors" )]
	public string[] Colors { get; set; } = [];

	[JsonPropertyName( "gatherer_alternates" )]
	public string[]? GathererAlternates { get; set; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement> AdditionalFields { get; set; } = [];
}
