using System;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Sandbox.Classes.Cards.ManaSymbols.Util;

/// <summary>
///     Stores ManaCost as its canonical Scryfall string.
///     Example: "{2}{W}{W}".
/// </summary>
public sealed class ManaCostJsonConverter : JsonConverter<ManaCost>
{
	public override ManaCost Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		if ( reader.TokenType != JsonTokenType.String )
			throw new JsonException( "Expected a mana-cost string." );

		string value = reader.GetString();

		if ( value is null )
			throw new JsonException( "Mana-cost string cannot be null." );

		try
		{
			return ManaCost.Parse( value );
		}
		catch ( FormatException exception )
		{
			throw new JsonException( exception.Message, exception );
		}
	}


	public override void Write( Utf8JsonWriter writer, ManaCost value, JsonSerializerOptions options ) { writer.WriteStringValue( value.ToString() ); }
}
