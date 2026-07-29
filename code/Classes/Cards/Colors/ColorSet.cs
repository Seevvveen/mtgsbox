using Sandbox.Classes.Cards.Colors.Util;
using System;
using System.Numerics;
using System.Text.Json.Serialization;
namespace Sandbox.Classes.Cards.Colors;

/// <summary>
/// An immutable set containing zero or more of Magic's five colors.
///
/// Colorless is represented by an empty set because colorless is not a color.
/// null and colorless intentionally mean different things.
///
/// This type represents color membership only. It does not represent mana
/// symbols, mana costs, color identity as a separate game concept, or mana.
/// </summary>
[JsonConverter( typeof(ColorSetJsonConverter) )]
public readonly record struct ColorSet
{
	// Each entry pairs a color with its bit and its WUBRG letter, in
	// canonical order. Everything that needs to "loop over the colors"
	// (Count, ToArray, ToAbbreviationString, ToScryfallArray) reads from
	// this single table instead of repeating five near-identical if blocks.
	// Add a color here once and every method below picks it up automatically.
	private static readonly (MagicColor Color, byte Mask, char Symbol)[] Entries =
	{
		( MagicColor.White, 1 << 0, 'W' ),
		( MagicColor.Blue,  1 << 1, 'U' ),
		( MagicColor.Black, 1 << 2, 'B' ),
		( MagicColor.Red,   1 << 3, 'R' ),
		( MagicColor.Green, 1 << 4, 'G' ),
	};

	private const byte ValidMask =
		(1 << 0) | (1 << 1) | (1 << 2) | (1 << 3) | (1 << 4);

	private readonly byte _mask;

	public static ColorSet Colorless { get; } = new( 0 );
	public static ColorSet White { get; } = new( GetMask( MagicColor.White ) );
	public static ColorSet Blue { get; } = new( GetMask( MagicColor.Blue ) );
	public static ColorSet Black { get; } = new( GetMask( MagicColor.Black ) );
	public static ColorSet Red { get; } = new( GetMask( MagicColor.Red ) );
	public static ColorSet Green { get; } = new( GetMask( MagicColor.Green ) );

	public bool IsColorless => _mask == 0;
	public bool IsMonocolored => Count == 1;
	public bool IsMulticolored => Count > 1;
	
	public int Count => BitOperations.PopCount( _mask );
	
	private ColorSet( byte mask )
	{
		if ( (mask & ~ValidMask) != 0 )
		{
			throw new ArgumentOutOfRangeException(
				nameof( mask ),
				$"Color mask '{mask}' contains undefined bits." );
		}

		_mask = mask;
	}

	public static ColorSet FromColor( MagicColor color )
	{
		return new ColorSet( GetMask( color ) );
	}

	public static ColorSet FromColors( params MagicColor[] colors )
	{
		ArgumentNullException.ThrowIfNull( colors );

		return FromColors( (IEnumerable<MagicColor>)colors );
	}
	
	public static ColorSet FromColors( IEnumerable<MagicColor> colors )
	{
		ArgumentNullException.ThrowIfNull( colors );

		byte mask = 0;

		foreach ( var color in colors )
			mask |= GetMask( color );

		return new ColorSet( mask );
	}

	public static ColorSet FromScryfall( string[] values )
	{
		ArgumentNullException.ThrowIfNull( values );

		var result = Colorless;

		foreach ( var value in values )
			result = result.Union( FromScryfallSymbol( value ) );

		return result;
	}

	public static ColorSet? FromNullableScryfall( string[]? values )
	{
		return values is null
			? null
			: FromScryfall( values );
	}

	public bool Contains( MagicColor color )
	{
		return (_mask & GetMask( color )) != 0;
	}

	public bool ContainsAll( ColorSet other )
	{
		return (_mask & other._mask) == other._mask;
	}

	public bool Overlaps( ColorSet other )
	{
		return (_mask & other._mask) != 0;
	}

	public ColorSet Add( MagicColor color )
	{
		return new ColorSet( (byte)(_mask | GetMask( color )) );
	}

	public ColorSet Remove( MagicColor color )
	{
		return new ColorSet( (byte)(_mask & ~GetMask( color )) );
	}

	public ColorSet Union( ColorSet other )
	{
		return new ColorSet( (byte)(_mask | other._mask) );
	}

	public ColorSet Intersect( ColorSet other )
	{
		return new ColorSet( (byte)(_mask & other._mask) );
	}

	public ColorSet Except( ColorSet other )
	{
		return new ColorSet( (byte)(_mask & ~other._mask) );
	}

	public MagicColor[] ToArray()
	{
		var result = new List<MagicColor>( Count );

		foreach ( var entry in Entries )
		{
			if ( (_mask & entry.Mask) != 0 )
				result.Add( entry.Color );
		}

		return result.ToArray();
	}

	public string[] ToScryfallArray()
	{
		var result = new List<string>( Count );

		foreach ( var entry in Entries )
		{
			if ( (_mask & entry.Mask) != 0 )
				result.Add( entry.Symbol.ToString() );
		}

		return result.ToArray();
	}

	public string ToAbbreviationString()
	{
		var chars = new List<char>( Count );

		foreach ( var entry in Entries )
		{
			if ( (_mask & entry.Mask) != 0 )
				chars.Add( entry.Symbol );
		}

		return new string( chars.ToArray() );
	}

	public override string ToString()
	{
		return IsColorless
			? "Colorless"
			: ToAbbreviationString();
	}

	internal static ColorSet FromScryfallSymbol( string? value )
	{
		return value switch
		{
			"W" => White,
			"U" => Blue,
			"B" => Black,
			"R" => Red,
			"G" => Green,

			"C" => throw new ArgumentException(
				"'C' means colorless mana, not a Magic color. " +
				"Use ProducedManaSet for produced_mana.",
				nameof( value ) ),

			_ => throw new ArgumentException(
				$"Unknown Scryfall color value '{value ?? "<null>"}'.",
				nameof( value ) )
		};
	}

	private static byte GetMask( MagicColor color )
	{
		foreach ( var entry in Entries )
		{
			if ( entry.Color == color )
				return entry.Mask;
		}

		throw new ArgumentOutOfRangeException(
			nameof( color ), color, "Unknown Magic color." );
	}
}
