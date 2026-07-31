#nullable enable

using Sandbox.Classes.CardDatabase;
using Sandbox.Classes.Cards.Colors;
using Sandbox.Classes.Cards.ManaSymbols;
using Sandbox.Classes.Database.Types;
using System;
using System.IO;
using System.Text.Json;
namespace Sandbox.Classes.Database;

public static class ScryfallSymbolNormalizer
{
	public static CardSymbolDefinition Normalize( ScryfallCardSymbolDto dto )
	{
		ArgumentNullException.ThrowIfNull( dto );

		if ( !string.Equals( dto.Object, "card_symbol", StringComparison.Ordinal ) )
		{
			throw new InvalidDataException( $"Expected a Scryfall card_symbol object, but received " + $"'{dto.Object}'." );
		}

		return new CardSymbolDefinition
			   {
				   Object             = dto.Object,
				   Id                 = ParseIdentifier( dto.Symbol ),
				   LooseVariant       = dto.LooseVariant,
				   SvgUri             = dto.SvgUri,
				   English            = RequireString( dto.English, "english" ),
				   Transposable       = dto.Transposable,
				   RepresentsMana     = dto.RepresentsMana,
				   AppearsInManaCosts = dto.AppearsInManaCosts,
				   Hybrid             = dto.Hybrid,
				   Phyrexian          = dto.Phyrexian,
				   Funny              = dto.Funny,
				   ManaValue          = dto.ManaValue,
				   ConvertedManaCost  = dto.Cmc,
				   Colors             = ColorSet.FromScryfall( dto.Colors ?? throw MissingField( "colors" ) ),
				   GathererAlternates = dto.GathererAlternates is null? null : [ .. dto.GathererAlternates ],
				   SourceExtensions   = CopyExtensions( dto.AdditionalFields )
			   };
	}


	private static SymbolIdentifier ParseIdentifier( string value )
	{
		try
		{
			return SymbolIdentifier.Parse( RequireString( value, "symbol" ) );
		}
		catch ( ArgumentException exception )
		{
			throw new InvalidDataException( $"Scryfall field 'symbol' contains invalid identifier " + $"'{value}'.", exception );
		}
		catch ( FormatException exception )
		{
			throw new InvalidDataException( $"Scryfall field 'symbol' contains invalid identifier " + $"'{value}'.", exception );
		}
	}


	private static string RequireString( string? value, string field )
	{
		if ( string.IsNullOrWhiteSpace( value ) )
			throw MissingField( field );

		return value;
	}


	private static InvalidDataException MissingField( string field ) { return new InvalidDataException( $"Required Scryfall symbol field '{field}' is missing." ); }


	private static Dictionary<string, JsonElement> CopyExtensions( Dictionary<string, JsonElement>? source )
	{
		if ( source is not { Count: > 0 } )
			return [ ];

		Dictionary<string, JsonElement> result = new Dictionary<string, JsonElement>( source.Count, StringComparer.Ordinal );

		foreach ( KeyValuePair<string, JsonElement> pair in source )
			result.Add( pair.Key, pair.Value.Clone() );

		return result;
	}
}
