#nullable enable

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Sandbox.Classes;

/// <summary>
///     A portable, editable deck definition. It stores compact card references
///     rather than embedding the complete card database records.
/// </summary>
public sealed record Deck
{
	public const int CurrentSchemaVersion = 1;

	public          int             SchemaVersion { get; init; } = CurrentSchemaVersion;
	public          Guid            Id            { get; init; } = Guid.NewGuid();
	public required string          Name          { get; init; }
	public          string?         FormatCode    { get; init; }
	public          DeckSource?     Source        { get; init; }
	public          List<DeckEntry> Entries       { get; init; } = [ ];


	public int Count( string section )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( section );

		return Entries.Where( entry => string.Equals( entry.Section, section, StringComparison.OrdinalIgnoreCase ) ).Sum( entry => entry.Quantity );
	}
}

public static class DeckSections
{
	public const string Main       = "main";
	public const string Sideboard  = "sideboard";
	public const string Commander  = "commander";
	public const string Companion  = "companion";
	public const string Maybeboard = "maybeboard";
}

public sealed record DeckEntry
{
	public required string            Section  { get; init; }
	public          int               Quantity { get; init; } = 1;
	public required DeckCardReference Card     { get; init; }
}

/// <summary>
///     Identifies the exact printing while retaining portable recovery hints.
///     OracleId is used for gameplay identity and copy-limit validation.
/// </summary>
public sealed record DeckCardReference
{
	public          Guid    ScryfallId      { get; init; }
	public          Guid?   OracleId        { get; init; }
	public required string  Name            { get; init; }
	public          string? SetCode         { get; init; }
	public          string? CollectorNumber { get; init; }
}

public sealed record DeckSource
{
	public required string  Kind       { get; init; }
	public          string? Site       { get; init; }
	public          string? ExternalId { get; init; }
	public          string? Url        { get; init; }
}

/// <summary>
///     Canonical JSON used by local saves, clipboard export, and future sharing.
/// </summary>
public static class DeckJson
{
	private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true, Converters = { new JsonStringEnumConverter() } };


	public static string Serialize( Deck deck )
	{
		ArgumentNullException.ThrowIfNull( deck );

		return JsonSerializer.Serialize( deck, Options );
	}


	public static Deck Deserialize( string json )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( json );

		Deck deck = JsonSerializer.Deserialize<Deck>( json, Options ) ?? throw new JsonException( "Deck JSON deserialized to null." );

		if ( deck.SchemaVersion != Deck.CurrentSchemaVersion )
		{
			throw new NotSupportedException( $"Deck schema version {deck.SchemaVersion} is not supported." );
		}

		return deck;
	}
}
