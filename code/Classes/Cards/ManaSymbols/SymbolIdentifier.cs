using System;

namespace Sandbox.Classes.Cards.ManaSymbols;

public readonly record struct SymbolIdentifier
{
	public string Value { get; }

	public string Code =>
		Value.Substring( 1, Value.Length - 2 );

	private SymbolIdentifier( string value )
	{
		Value = value;
	}

	public static SymbolIdentifier Parse( string value )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( value );

		var normalized = value.Trim().ToUpperInvariant();

		if ( !normalized.StartsWith( '{' ) )
			normalized = $"{{{normalized}}}";

		if ( !normalized.EndsWith( '}' ) )
		{
			throw new FormatException(
				$"Invalid symbol identifier '{value}'." );
		}

		return new SymbolIdentifier( normalized );
	}

	public override string ToString() => Value;
}