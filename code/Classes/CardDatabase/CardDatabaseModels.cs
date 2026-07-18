using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox.Classes.CardDatabase.Models;

//What I choose to capture from scryfall
public sealed record ScryfallCardDto
{
	[JsonPropertyName( "id" )]
	public string ScryfallId { get; init; } = "";

	[JsonPropertyName( "oracle_id" )]
	public string? OracleId { get; init; }

	[JsonPropertyName( "name" )]
	public string Name { get; init; } = "";
}

//A Normalized Card
public sealed record CardDefinition
{
	public Guid ScryfallId { get; init; }
	public Guid? OracleId { get; init; }
	public string Name { get; init; } = "";
}





/// <summary>
/// Describes where one card is stored inside cards.dat.
/// </summary>
public readonly record struct CardIndexEntry
{
	public Guid ScryfallId { get; init; }
	public long Offset { get; init; }
	public int Length { get; init; }
}

/// <summary>
/// The root object stored in card-index.json.
/// </summary>
public sealed record CardIndexFile
{
	public int FormatVersion { get; init; }
	public int CardCount { get; init; }

	public List<CardIndexEntry> Cards { get; init; } = [];
}