#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Sandbox.Classes.Database.Types;

public sealed record CardSetDefinition
{
	public required string Object { get; init; }
	public Guid Id { get; init; }
	public required string Code { get; init; }
	public string? MtgoCode { get; init; }
	public string? ArenaCode { get; init; }
	public int? TcgplayerId { get; init; }
	public required string Name { get; init; }
	public required string Type { get; init; }
	public DateTime? ReleasedAt { get; init; }
	public string? BlockCode { get; init; }
	public string? Block { get; init; }
	public string? ParentSetCode { get; init; }
	public int CardCount { get; init; }
	public int? PrintedSize { get; init; }
	public bool Digital { get; init; }
	public bool FoilOnly { get; init; }
	public bool NonfoilOnly { get; init; }
	public required string ScryfallUri { get; init; }
	public required string ApiUri { get; init; }
	public required string IconSvgUri { get; init; }
	public required string SearchUri { get; init; }
	public Dictionary<string, JsonElement> SourceExtensions { get; init; } = [];
}

public sealed record CardRuling
{
	public required string Object { get; init; }
	public Guid OracleId { get; init; }
	public required string Source { get; init; }
	public DateTime PublishedAt { get; init; }
	public required string Comment { get; init; }
	public Dictionary<string, JsonElement> SourceExtensions { get; init; } = [];
}
