using Sandbox.Classes.Cards.ManaSymbols.Util;
using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json.Serialization;

namespace Sandbox.Classes.Cards.ManaSymbols;

#nullable enable

/// <summary>
/// An ordered mana cost for one card face.
///
/// Preserves repeated symbols and printed order. This describes the cost,
/// but does not determine how that cost can be paid.
/// </summary>
[JsonConverter( typeof(ManaCostJsonConverter) )]
public sealed class ManaCost : IEquatable<ManaCost>
{
	private readonly SymbolIdentifier[] _symbols;
	private readonly ReadOnlyCollection<SymbolIdentifier> _symbolsView;
	private readonly string _canonicalText;

	/// <summary>
	/// Represents the absence of a mana cost: "".
	///
	/// This differs from {0}, which is an existing mana cost with a value
	/// of zero.
	/// </summary>
	public static ManaCost None { get; } =
		new( Array.Empty<SymbolIdentifier>() );

	/// <summary>
	/// A read-only view of the symbols in printed order.
	///
	/// The backing array is not exposed, so callers cannot mutate this
	/// ManaCost after its canonical text and hash identity are established.
	/// </summary>
	public IReadOnlyList<SymbolIdentifier> Symbols => _symbolsView;

	public bool HasManaCost => _symbols.Length > 0;
	public int SymbolCount => _symbols.Length;
	public SymbolIdentifier this[int index] => _symbols[index];

	private ManaCost( SymbolIdentifier[] symbols )
	{
		ArgumentNullException.ThrowIfNull( symbols );

		if ( symbols.Length == 0 )
		{
			_symbols = Array.Empty<SymbolIdentifier>();
		}
		else
		{
			_symbols = new SymbolIdentifier[symbols.Length];
			Array.Copy( symbols, _symbols, symbols.Length );
		}

		_symbolsView = Array.AsReadOnly( _symbols );

		var text = new StringBuilder();

		foreach ( var symbol in _symbols )
		{
			if ( !symbol.IsValid )
			{
				throw new ArgumentException(
					"A mana cost cannot contain an uninitialized symbol.",
					nameof( symbols ) );
			}

			text.Append( symbol );
		}

		_canonicalText = text.ToString();
	}

	/// <summary>
	/// Parses one face's Scryfall mana_cost value.
	/// </summary>
	public static ManaCost Parse( string value )
	{
		ArgumentNullException.ThrowIfNull( value );

		if ( value.Length == 0 )
			return None;

		if ( value.Contains( "//", StringComparison.Ordinal ) )
		{
			throw new FormatException(
				"The value contains multiple face costs. " +
				"Parse each card face's mana_cost separately." );
		}

		var symbols = new List<SymbolIdentifier>();
		var index = 0;

		while ( index < value.Length )
		{
			if ( char.IsWhiteSpace( value[index] ) )
			{
				index++;
				continue;
			}

			if ( value[index] != '{' )
			{
				throw new FormatException(
					$"Expected '{{' at character index {index}." );
			}

			var closingBrace = value.IndexOf( '}', index + 1 );

			if ( closingBrace < 0 )
			{
				throw new FormatException(
					$"Mana symbol beginning at index {index} is not closed." );
			}

			var tokenLength = closingBrace - index + 1;
			var token = value.Substring( index, tokenLength );

			symbols.Add( SymbolIdentifier.Parse( token ) );

			index = closingBrace + 1;
		}

		if ( symbols.Count == 0 )
			throw new FormatException( "A mana cost cannot contain only whitespace." );

		return new ManaCost( symbols.ToArray() );
	}

	public static ManaCost? ParseNullable( string? value )
	{
		return value is null ? null : Parse( value );
	}

	public static bool TryParse( string? value, out ManaCost? manaCost )
	{
		if ( value is null )
		{
			manaCost = null;
			return false;
		}

		try
		{
			manaCost = Parse( value );
			return true;
		}
		catch ( FormatException )
		{
			manaCost = null;
			return false;
		}
		catch ( ArgumentException )
		{
			manaCost = null;
			return false;
		}
	}

	public override string ToString()
	{
		return _canonicalText;
	}

	public bool Equals( ManaCost? other )
	{
		return other is not null &&
			string.Equals(
				_canonicalText,
				other._canonicalText,
				StringComparison.Ordinal );
	}

	public override bool Equals( object? obj )
	{
		return obj is ManaCost other && Equals( other );
	}

	public override int GetHashCode()
	{
		return StringComparer.Ordinal.GetHashCode( _canonicalText );
	}

	public static bool operator ==(
		ManaCost? left,
		ManaCost? right )
	{
		return Equals( left, right );
	}

	public static bool operator !=(
		ManaCost? left,
		ManaCost? right )
	{
		return !Equals( left, right );
	}
}
