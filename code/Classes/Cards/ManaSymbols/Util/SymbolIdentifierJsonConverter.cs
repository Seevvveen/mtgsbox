using System;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Sandbox.Classes.Cards.ManaSymbols.Util;

#nullable enable

/// <summary>
///     Stores a SymbolIdentifier as its canonical, braced Scryfall string.
/// </summary>
public sealed class SymbolIdentifierJsonConverter : JsonConverter<SymbolIdentifier>
{
	public override SymbolIdentifier Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		if ( reader.TokenType != JsonTokenType.String )
		{
			throw new JsonException( "Expected a symbol-identifier string." );
		}

		return Parse( reader.GetString() );
	}


	public override void Write( Utf8JsonWriter writer, SymbolIdentifier value, JsonSerializerOptions options )
	{
		if ( !value.IsValid )
		{
			throw new JsonException( "An uninitialized symbol identifier cannot be serialized." );
		}

		writer.WriteStringValue( value.Value );
	}


	public override SymbolIdentifier ReadAsPropertyName( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options ) { return Parse( reader.GetString() ); }


	public override void WriteAsPropertyName( Utf8JsonWriter writer, SymbolIdentifier value, JsonSerializerOptions options )
	{
		if ( !value.IsValid )
		{
			throw new JsonException( "An uninitialized symbol identifier cannot be serialized." );
		}

		writer.WritePropertyName( value.Value );
	}


	private static SymbolIdentifier Parse( string? value )
	{
		if ( value is null )
		{
			throw new JsonException( "Symbol-identifier string cannot be null." );
		}

		try
		{
			return SymbolIdentifier.Parse( value );
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
}
