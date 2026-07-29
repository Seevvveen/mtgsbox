using System.Text.Json;
namespace Sandbox.Classes.CardDatabase;


/// <summary>
/// Source of truth for names and options
/// </summary>
internal static class DatabaseFileInfo
{
	public const string SourceFile = "oracle-cards.json";
	public const string RulingsSourceFile = "rulings.json";
	public const string SetSourceFile = "sets.json";
	public const string SymbolSourceFile = "symbology.json";
	public const string CardDataFile = "CardDefinitions.dat";
	public const string CardIndexFile = "CardDefinitionsIndex.json";
	public const string SymbolDefinitionsFile = "CardSymbolDefinitions.json";
	public const string SetDefinitionsFile = "CardSetDefinitions.json";
	public const string RulingsFile = "CardRulings.json";
	
	public const string CardGameplayDataFile  = "CardGameplay.dat";
	public const string CardPresentationFile = "CardPresentations.dat";

	public const int CurrentFormatVersion = 3;

	public static readonly JsonSerializerOptions ImportJsonOptions = new() { PropertyNameCaseInsensitive = true };
	public static readonly JsonSerializerOptions DatabaseJsonOptions = new() { WriteIndented = false };
}
