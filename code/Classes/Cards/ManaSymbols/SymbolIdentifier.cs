using System;

using System.Text.Json.Serialization;
using Sandbox.Classes.Cards.ManaSymbols.Util;

namespace Sandbox.Classes.Cards.ManaSymbols;

/// <summary>
/// The canonical identifier for exactly one Scryfall symbol.
///
/// Examples: {W}, {2/W}, {W/U/P}, {T}, and {CHAOS}.
///
/// This type identifies a symbol. It does not claim that the symbol
/// represents mana or that it is legal inside a mana cost.
/// </summary>
[JsonConverter( typeof(SymbolIdentifierJsonConverter) )]
public readonly record struct SymbolIdentifier
{
	private readonly string? _value;

	/// <summary>
	/// The canonical, braced representation of this identifier.
	/// </summary>
	public string Value =>
		_value ?? throw new InvalidOperationException(
			"An uninitialized SymbolIdentifier has no value." );

	/// <summary>
	/// The identifier without its outer braces.
	/// </summary>
	public string Code =>
		_value is null
			? throw new InvalidOperationException(
				"An uninitialized SymbolIdentifier has no code." )
			: _value.Substring( 1, _value.Length - 2 );

	/// <summary>
	/// False only for default(SymbolIdentifier).
	/// </summary>
	public bool IsValid => _value is not null;

	private SymbolIdentifier( string value )
	{
		_value = value;
	}

	/// <summary>
	/// Parses either a loose code such as W/U or a canonical identifier
	/// such as {W/U}.
	///
	/// The result is guaranteed to contain exactly one nonempty symbol.
	/// Unknown but syntactically valid symbols are preserved so that a new
	/// Scryfall symbol does not break database ingestion.
	/// </summary>
	public static SymbolIdentifier Parse( string value )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( value );

		var normalized = value.Trim().ToUpperInvariant();

		var beginsWithBrace = normalized[0] == '{';
		var endsWithBrace = normalized[^1] == '}';

		if ( !beginsWithBrace && !endsWithBrace )
		{
			if ( normalized.IndexOf( '{' ) >= 0 ||
				 normalized.IndexOf( '}' ) >= 0 )
			{
				throw InvalidFormat( value );
			}

			normalized = $"{{{normalized}}}";
		}

		if ( normalized.Length < 3 ||
			 normalized[0] != '{' ||
			 normalized[^1] != '}' )
		{
			throw InvalidFormat( value );
		}

		for ( var index = 1; index < normalized.Length - 1; index++ )
		{
			var character = normalized[index];

			if ( character == '{' ||
				 character == '}' ||
				 char.IsWhiteSpace( character ) )
			{
				throw InvalidFormat( value );
			}
		}

		return new SymbolIdentifier( normalized );
	}

	public static bool TryParse(
		string? value,
		out SymbolIdentifier identifier )
	{
		if ( string.IsNullOrWhiteSpace( value ) )
		{
			identifier = default;
			return false;
		}

		try
		{
			identifier = Parse( value );
			return true;
		}
		catch ( ArgumentException )
		{
			identifier = default;
			return false;
		}
		catch ( FormatException )
		{
			identifier = default;
			return false;
		}
	}

	public override string ToString()
	{
		return _value ?? "";
	}

	private static FormatException InvalidFormat( string value )
	{
		return new FormatException(
			$"'{value}' is not exactly one valid symbol identifier." );
	}
}
