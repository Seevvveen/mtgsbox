using Sandbox.Classes.Cards.ManaSymbols;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Sandbox.Classes.Cards.Colors.Util;

#nullable enable

static class ScryfallSetJsonReader
{
	public static T ReadArray<T>( ref Utf8JsonReader reader, T empty, Func<string, T> parseSymbol, Func<T, T, T> union, string typeName )
	{
		if ( reader.TokenType != JsonTokenType.StartArray )
			throw new JsonException( $"Expected a JSON array for {typeName}." );

		T result = empty;

		while ( reader.Read() )
		{
			if ( reader.TokenType == JsonTokenType.EndArray )
				return result;

			if ( reader.TokenType != JsonTokenType.String )
				throw new JsonException( $"{typeName} entries must be strings." );

			try
			{
				string symbol = reader.GetString() ?? throw new JsonException( $"{typeName} entries cannot be null." );

				result = union( result, parseSymbol( symbol ) );
			}
			catch ( ArgumentException exception )
			{
				throw new JsonException( exception.Message, exception );
			}
		}

		throw new JsonException( $"Unexpected end of JSON while reading {typeName}." );
	}


	public static void WriteArray( Utf8JsonWriter writer, string[] symbols )
	{
		writer.WriteStartArray();

		foreach ( string symbol in symbols )
			writer.WriteStringValue( symbol );

		writer.WriteEndArray();
	}
}

/// <summary>
///     Serializes ColorSet as a Scryfall-compatible JSON color array.
/// </summary>
public sealed class ColorSetJsonConverter : JsonConverter<ColorSet>
{
	public override ColorSet Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		return ScryfallSetJsonReader.ReadArray( ref reader, ColorSet.Colorless, ColorSet.FromScryfallSymbol, ( current, next ) => current.Union( next ), nameof(ColorSet) );
	}


	public override void Write( Utf8JsonWriter writer, ColorSet value, JsonSerializerOptions options )
	{
		ScryfallSetJsonReader.WriteArray( writer, value.ToScryfallArray() );
	}
}

/// <summary>
///     Serializes ProducedManaSet as a Scryfall-compatible JSON produced_mana
///     array.
/// </summary>
public sealed class ProducedManaSetJsonConverter : JsonConverter<ProducedManaSet>
{
	public override ProducedManaSet Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		if ( reader.TokenType != JsonTokenType.StartArray )
			throw new JsonException( $"Expected a JSON array for {nameof(ProducedManaSet)}." );

		List<string> values = new List<string>();

		while ( reader.Read() )
		{
			if ( reader.TokenType == JsonTokenType.EndArray )
			{
				try
				{
					return ProducedManaSet.FromScryfall( values.ToArray() );
				}
				catch ( ArgumentException exception )
				{
					throw new JsonException( exception.Message, exception );
				}
				catch ( FormatException exception )
				{
					throw new JsonException( exception.Message, exception );
				}
			}

			if ( reader.TokenType != JsonTokenType.String )
				throw new JsonException( $"{nameof(ProducedManaSet)} entries must be strings." );

			values.Add( reader.GetString() ?? throw new JsonException( $"{nameof(ProducedManaSet)} entries cannot be null." ) );
		}

		throw new JsonException( "Unexpected end of JSON while reading " + $"{nameof(ProducedManaSet)}." );
	}


	public override void Write( Utf8JsonWriter writer, ProducedManaSet value, JsonSerializerOptions options )
	{
		ScryfallSetJsonReader.WriteArray( writer, value.ToScryfallArray() );
	}
}
