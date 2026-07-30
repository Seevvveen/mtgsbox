using Sandbox.Classes.Cards.Colors.Util;
using Sandbox.Classes.Cards.ManaSymbols;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Sandbox.Classes.Cards.Colors;

#nullable enable

/// <summary>
/// An immutable set of symbols reported by Scryfall's produced_mana field.
///
/// Usually contains the standard mana symbols W, U, B, R, G, and C, but may
/// contain unusual symbols such as T.
///
/// This does not represent quantities, spending restrictions, source
/// properties such as snow, or what the card can produce in the current
/// game state.
/// </summary>
[JsonConverter( typeof(ProducedManaSetJsonConverter) )]
public readonly struct ProducedManaSet : IEquatable<ProducedManaSet>
{
	private static readonly ReadOnlyCollection<SymbolIdentifier>
		EmptySymbolsView = Array.AsReadOnly(
			Array.Empty<SymbolIdentifier>() );

	private readonly SymbolIdentifier[]? _symbols;
	private readonly ReadOnlyCollection<SymbolIdentifier>? _symbolsView;

	private SymbolIdentifier[] Values =>
		_symbols ?? Array.Empty<SymbolIdentifier>();

	/// <summary>
	/// A read-only view of the normalized Scryfall symbols.
	/// </summary>
	public IReadOnlyList<SymbolIdentifier> Symbols =>
		_symbolsView ?? EmptySymbolsView;

	public static ProducedManaSet Empty { get; } =
		new( Array.Empty<SymbolIdentifier>() );

	// Standard conveniences. These do not restrict the set to these symbols.
	public static ProducedManaSet White { get; } =
		FromScryfallSymbol( "W" );

	public static ProducedManaSet Blue { get; } =
		FromScryfallSymbol( "U" );

	public static ProducedManaSet Black { get; } =
		FromScryfallSymbol( "B" );

	public static ProducedManaSet Red { get; } =
		FromScryfallSymbol( "R" );

	public static ProducedManaSet Green { get; } =
		FromScryfallSymbol( "G" );

	public static ProducedManaSet Colorless { get; } =
		FromScryfallSymbol( "C" );

	public bool IsEmpty => Values.Length == 0;
	public int Count => Values.Length;

	private ProducedManaSet( SymbolIdentifier[] symbols )
	{
		ArgumentNullException.ThrowIfNull( symbols );

		_symbols = Normalize( symbols );
		_symbolsView = Array.AsReadOnly( _symbols );
	}

	public static ProducedManaSet FromSymbol(
		SymbolIdentifier symbol )
	{
		return new ProducedManaSet( [symbol] );
	}

	public static ProducedManaSet FromSymbols(
		params SymbolIdentifier[] symbols )
	{
		ArgumentNullException.ThrowIfNull( symbols );

		return new ProducedManaSet( symbols );
	}

	public static ProducedManaSet FromSymbols(
		IEnumerable<SymbolIdentifier> symbols )
	{
		ArgumentNullException.ThrowIfNull( symbols );

		var collected = new List<SymbolIdentifier>();

		foreach ( var symbol in symbols )
			collected.Add( symbol );

		return new ProducedManaSet( collected.ToArray() );
	}

	/// <summary>
	/// Parses a present Scryfall produced_mana array.
	///
	/// Values normally look like W, U, B, R, G, or C, but any valid
	/// SymbolIdentifier is preserved. An empty array returns Empty.
	/// </summary>
	public static ProducedManaSet FromScryfall(
		string[] values )
	{
		ArgumentNullException.ThrowIfNull( values );

		var symbols = new List<SymbolIdentifier>( values.Length );

		foreach ( var value in values )
			symbols.Add( ParseScryfallSymbol( value ) );

		return new ProducedManaSet( symbols.ToArray() );
	}

	public static ProducedManaSet? FromNullableScryfall(
		string[]? values )
	{
		return values is null
			? null
			: FromScryfall( values );
	}

	public bool Contains( SymbolIdentifier symbol )
	{
		foreach ( var existing in Values )
		{
			if ( existing.Equals( symbol ) )
				return true;
		}

		return false;
	}

	/// <summary>
	/// Checks for a loose Scryfall value such as W, C, or T.
	/// </summary>
	public bool ContainsScryfallSymbol( string value )
	{
		return Contains( ParseScryfallSymbol( value ) );
	}

	public bool ContainsAll( ProducedManaSet other )
	{
		foreach ( var symbol in other.Values )
		{
			if ( !Contains( symbol ) )
				return false;
		}

		return true;
	}

	public bool Overlaps( ProducedManaSet other )
	{
		foreach ( var symbol in other.Values )
		{
			if ( Contains( symbol ) )
				return true;
		}

		return false;
	}

	public ProducedManaSet Add( SymbolIdentifier symbol )
	{
		if ( Contains( symbol ) )
			return this;

		var result = new SymbolIdentifier[Count + 1];

		Array.Copy( Values, result, Count );
		result[Count] = symbol;

		return new ProducedManaSet( result );
	}

	public ProducedManaSet Remove( SymbolIdentifier symbol )
	{
		if ( !Contains( symbol ) )
			return this;

		var result = new List<SymbolIdentifier>( Count - 1 );

		foreach ( var existing in Values )
		{
			if ( !existing.Equals( symbol ) )
				result.Add( existing );
		}

		return new ProducedManaSet( result.ToArray() );
	}

	public ProducedManaSet Union( ProducedManaSet other )
	{
		var result = new List<SymbolIdentifier>(
			Count + other.Count );

		result.AddRange( Values );
		result.AddRange( other.Values );

		return new ProducedManaSet( result.ToArray() );
	}

	public ProducedManaSet Intersect( ProducedManaSet other )
	{
		var result = new List<SymbolIdentifier>();

		foreach ( var symbol in Values )
		{
			if ( other.Contains( symbol ) )
				result.Add( symbol );
		}

		return new ProducedManaSet( result.ToArray() );
	}

	public ProducedManaSet Except( ProducedManaSet other )
	{
		var result = new List<SymbolIdentifier>();

		foreach ( var symbol in Values )
		{
			if ( !other.Contains( symbol ) )
				result.Add( symbol );
		}

		return new ProducedManaSet( result.ToArray() );
	}

	public SymbolIdentifier[] ToArray()
	{
		var result = new SymbolIdentifier[Count];

		Array.Copy( Values, result, Count );

		return result;
	}

	/// <summary>
	/// Returns loose Scryfall produced_mana values without braces.
	///
	/// For example: ["W", "U"] rather than ["{W}", "{U}"].
	/// </summary>
	public string[] ToScryfallArray()
	{
		var result = new string[Count];

		for ( var index = 0; index < Values.Length; index++ )
			result[index] = GetSymbolCode( Values[index] );

		return result;
	}

	public override string ToString()
	{
		return IsEmpty
			? "None"
			: string.Join( "", ToScryfallArray() );
	}

	public bool Equals( ProducedManaSet other )
	{
		if ( Count != other.Count )
			return false;

		for ( var index = 0; index < Count; index++ )
		{
			if ( !Values[index].Equals( other.Values[index] ) )
				return false;
		}

		return true;
	}

	public override bool Equals( object? obj )
	{
		return obj is ProducedManaSet other && Equals( other );
	}

	public override int GetHashCode()
	{
		unchecked
		{
			var hash = 17;

			foreach ( var symbol in Values )
				hash = (hash * 31) + symbol.GetHashCode();

			return hash;
		}
	}

	public static bool operator ==(
		ProducedManaSet left,
		ProducedManaSet right )
	{
		return left.Equals( right );
	}

	public static bool operator !=(
		ProducedManaSet left,
		ProducedManaSet right )
	{
		return !left.Equals( right );
	}

	/// <summary>
	/// Retained for compatibility with the existing JSON converter.
	/// </summary>
	internal static ProducedManaSet FromScryfallSymbol(
		string? value )
	{
		if ( value is null )
		{
			throw new ArgumentException(
				"Scryfall produced-mana symbols cannot be null.",
				nameof( value ) );
		}

		return FromSymbol( ParseScryfallSymbol( value ) );
	}

	private static SymbolIdentifier ParseScryfallSymbol(
		string value )
	{
		return SymbolIdentifier.Parse( value );
	}

	private static SymbolIdentifier[] Normalize(
		IEnumerable<SymbolIdentifier> symbols )
	{
		var result = new List<SymbolIdentifier>();

		foreach ( var symbol in symbols )
		{
			if ( !symbol.IsValid )
			{
				throw new ArgumentException(
					"A produced-mana set cannot contain an " +
					"uninitialized symbol.",
					nameof( symbols ) );
			}

			if ( !Contains( result, symbol ) )
				result.Add( symbol );
		}

		result.Sort( CompareSymbols );

		return result.ToArray();
	}

	private static bool Contains(
		List<SymbolIdentifier> symbols,
		SymbolIdentifier candidate )
	{
		foreach ( var symbol in symbols )
		{
			if ( symbol.Equals( candidate ) )
				return true;
		}

		return false;
	}

	private static int CompareSymbols(
		SymbolIdentifier left,
		SymbolIdentifier right )
	{
		var leftText = left.ToString();
		var rightText = right.ToString();

		var leftOrder = GetStandardOrder( leftText );
		var rightOrder = GetStandardOrder( rightText );

		if ( leftOrder != rightOrder )
			return leftOrder.CompareTo( rightOrder );

		return string.CompareOrdinal( leftText, rightText );
	}

	private static int GetStandardOrder( string symbol )
	{
		return symbol switch
		{
			"{W}" => 0,
			"{U}" => 1,
			"{B}" => 2,
			"{R}" => 3,
			"{G}" => 4,
			"{C}" => 5,
			_ => 100
		};
	}

	private static string GetSymbolCode(
		SymbolIdentifier symbol )
	{
		return symbol.Code;
	}
}
