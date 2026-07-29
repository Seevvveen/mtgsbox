using System;
namespace Sandbox.Classes.Database.Types;

/// <summary>
/// Describes where one card is stored inside cards.dat.
/// </summary>
public readonly record struct CardIndexEntry
{
	public long Offset { get; init; }
	public int Length { get; init; }
}

public readonly record struct CardIdMapping
{
	public Guid ScryfallId { get; init; }
	public int RecordId { get; init; }
}

/// <summary>
/// The root object stored in card-index.json.
/// </summary>
public sealed record CardIndexFile
{
	public int FormatVersion { get; init; }
	public int CardCount { get; init; }

	public List<CardIndexEntry> Cards { get; init; } = [];
	public List<CardIdMapping> IdMappings { get; init; } = [];
}

public sealed record CardSymbolDefinitionFile
{
	public int FormatVersion { get; init; }
	public int SymbolCount { get; init; }
	public List<CardSymbolDefinition> Symbols { get; init; } = [];
}

public sealed record CardSetDefinitionFile
{
	public int FormatVersion { get; init; }
	public int SetCount { get; init; }
	public List<CardSetDefinition> Sets { get; init; } = [];
}

public sealed record CardRulingFile
{
	public int FormatVersion { get; init; }
	public int RulingCount { get; init; }
	public List<CardRuling> Rulings { get; init; } = [];
}
