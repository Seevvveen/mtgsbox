#nullable enable

using Sandbox.Classes.Cards.Colors;
using Sandbox.Classes.Cards.ManaSymbols;
using System.Collections.Generic;
using System.Text.Json;

namespace Sandbox.Classes.Database.Types;

public sealed record CardSymbolDefinition
{
	public required string Object { get; init; }
	public required SymbolIdentifier Id { get; init; }
	public string? LooseVariant { get; init; }
	public string? SvgUri { get; init; }
	public required string English { get; init; }
	public bool Transposable { get; init; }
	public bool RepresentsMana { get; init; }
	public bool AppearsInManaCosts { get; init; }
	public bool Hybrid { get; init; }
	public bool Phyrexian { get; init; }
	public bool Funny { get; init; }
	public decimal? ManaValue { get; init; }
	public decimal? ConvertedManaCost { get; init; }
	public ColorSet Colors { get; init; }
	public string[]? GathererAlternates { get; init; }
	public Dictionary<string, JsonElement> SourceExtensions { get; init; } = [];
}
