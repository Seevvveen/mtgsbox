#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox.Classes.CardDatabase;

/// <summary>
/// Source of truth for database file names, format identity, and JSON options.
/// </summary>
internal static class DatabaseFileInfo
{
	public const string SourceFile = "default-cards.json";
	public const string RulingsSourceFile = "rulings.json";
	public const string SetSourceFile = "sets.json";
	public const string SymbolSourceFile = "symbology.json";
	public const string CardDataFile = "CardDefinitions.dat";
	public const string CardIndexFile = "CardDefinitionsIndex.json";
	public const string SymbolDefinitionsFile = "CardSymbolDefinitions.json";
	public const string SetDefinitionsFile = "CardSetDefinitions.json";
	public const string RulingsFile = "CardRulings.json";

	public const string CardGameplayDataFile = "CardGameplay.dat";
	public const string CardPresentationFile = "CardPresentations.dat";

	public const int MaxCardRecordBytes = 4 * 1024 * 1024;

	// Version 4 stores domain enums by stable names rather than declaration
	// positions. Version 5 adds deck-import lookup indexes.
	public const int CurrentFormatVersion = 5;
	public const int OldestReadableFormatVersion = 3;

	public static readonly JsonSerializerOptions ImportJsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	public static readonly JsonSerializerOptions DatabaseJsonOptions = new()
	{
		WriteIndented = false,
		Converters =
		{
			new JsonStringEnumConverter(
				namingPolicy: null,
				allowIntegerValues: false )
		}
	};

	// Version 3 used numeric enum values. Persisted enum declarations retain
	// explicit v3 wire numbers, and the default converter deliberately rejects
	// string enum names so mixed v3/v4 records do not load silently.
	public static readonly JsonSerializerOptions LegacyDatabaseJsonOptions =
		new()
		{
			WriteIndented = false
		};
}
