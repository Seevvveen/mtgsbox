using Sandbox.Classes.Cards.Colors;
using System;
using System.Globalization;
namespace Sandbox.Classes.Cards.ManaSymbols;

#nullable enable

/// <summary>
///     One symbol appearing inside a mana cost.
///     Examples:
///     {2}, {W}, {C}, {X}, {W/U}, {2/W}, {W/P}, {W/U/P}, {S}.
/// </summary>
public readonly record struct ManaSymbol
{
	private readonly string? _code;


	private ManaSymbol( string code, ManaSymbolKind kind, ColorSet colors, int genericAmount = 0 )
	{
		_code         = code;
		Kind          = kind;
		Colors        = colors;
		GenericAmount = genericAmount;
	}


	public string Code
	{
		get { return _code ?? throw new InvalidOperationException( "An uninitialized ManaSymbol has no code." ); }
	}

	public ManaSymbolKind Kind { get; }

	public bool IsValid
	{
		get { return _code is not null; }
	}

	/// <summary>
	///     Colors represented by this symbol.
	///     Examples:
	///     {W}     = W
	///     {W/U}   = WU
	///     {2/W}   = W
	///     {W/U/P} = WU
	///     {C}     = colorless/empty
	/// </summary>
	public ColorSet Colors { get; }

	/// <summary>
	///     The value of a normal numeric generic symbol.
	///     This is only meaningful when Kind is Generic.
	/// </summary>
	public int GenericAmount { get; }


	public bool ContainsColor( MagicColor color )
	{
		return Colors.Contains( color );
	}


	public override string ToString()
	{
		return _code is null? "" : $"{{{_code}}}";
	}


	/// <summary>
	///     Resolves a syntactically valid identifier into one of the mana-symbol
	///     forms understood by the rules implementation.
	///     A SymbolIdentifier may be valid without being a mana symbol. For
	///     example, {T} and {D} should remain valid identifiers but this method
	///     rejects them as mana symbols.
	/// </summary>
	public static ManaSymbol Parse( SymbolIdentifier identifier )
	{
		if ( !identifier.IsValid )
			throw new ArgumentException( "Cannot parse an uninitialized symbol identifier.", nameof(identifier) );

		return ParseCode( identifier.Code );
	}


	/// <summary>
	///     Resolves either a loose code such as W/U or a canonical identifier
	///     such as {W/U}.
	/// </summary>
	public static ManaSymbol Parse( string value )
	{
		return Parse( SymbolIdentifier.Parse( value ) );
	}


	public static bool TryParse( string? value, out ManaSymbol symbol )
	{
		if ( string.IsNullOrWhiteSpace( value ) )
		{
			symbol = default(ManaSymbol);

			return false;
		}

		try
		{
			symbol = Parse( value );

			return true;
		}
		catch ( ArgumentException )
		{
			symbol = default(ManaSymbol);

			return false;
		}
		catch ( FormatException )
		{
			symbol = default(ManaSymbol);

			return false;
		}
	}


	private static ManaSymbol ParseCode( string value )
	{
		ArgumentNullException.ThrowIfNull( value );

		string code = value.Trim().ToUpperInvariant();

		if ( code.Length == 0 )
			throw new FormatException( "Mana symbols cannot be empty." );

		if ( IsAsciiNumber( code ) )
		{
			if ( !int.TryParse( code, NumberStyles.None, CultureInfo.InvariantCulture, out int amount ) )
				throw new FormatException( $"Generic mana value '{code}' is too large." );

			return new ManaSymbol( amount.ToString( CultureInfo.InvariantCulture ), ManaSymbolKind.Generic, ColorSet.Colorless, amount );
		}

		switch ( code )
		{
			case "W": return Colored( code, MagicColor.White );

			case "U": return Colored( code, MagicColor.Blue );

			case "B": return Colored( code, MagicColor.Black );

			case "R": return Colored( code, MagicColor.Red );

			case "G": return Colored( code, MagicColor.Green );

			case "C": return new ManaSymbol( code, ManaSymbolKind.Colorless, ColorSet.Colorless );

			case "X":
			case "Y":
			case "Z":
				return new ManaSymbol( code, ManaSymbolKind.Variable, ColorSet.Colorless );

			case "S": return new ManaSymbol( code, ManaSymbolKind.Snow, ColorSet.Colorless );

			case "L": return new ManaSymbol( code, ManaSymbolKind.Legendary, ColorSet.Colorless );

			case "½": return new ManaSymbol( code, ManaSymbolKind.Half, ColorSet.Colorless );

			case "HW": return new ManaSymbol( code, ManaSymbolKind.Half, ColorSet.White );

			case "HR": return new ManaSymbol( code, ManaSymbolKind.Half, ColorSet.Red );

			case "∞": return new ManaSymbol( code, ManaSymbolKind.Infinity, ColorSet.Colorless );
		}

		string[] parts = code.Split( '/' );

		if ( parts.Length == 2 )
			return ParseTwoPartSymbol( code, parts[0], parts[1] );

		if ( parts.Length == 3 )
			return ParseThreePartSymbol( code, parts[0], parts[1], parts[2] );

		throw new FormatException( $"Unsupported mana symbol '{{{code}}}'." );
	}


	private static ManaSymbol ParseTwoPartSymbol( string code, string first, string second )
	{
		// {W/P}, {U/P}, {C/P}, etc.
		if ( second == "P" && TryGetManaComponentColors( first, out ColorSet phyrexianColors ) )
			return new ManaSymbol( code, ManaSymbolKind.Phyrexian, phyrexianColors );

		// {2/W}, {2/U}, etc.
		if ( first == "2" && TryGetColor( second, out MagicColor genericHybridColor ) )
			return new ManaSymbol( code, ManaSymbolKind.GenericHybrid, ColorSet.FromColor( genericHybridColor ) );

		// {C/W}, {C/U}, etc.
		if ( first == "C" && TryGetColor( second, out MagicColor colorlessHybridColor ) )
			return new ManaSymbol( code, ManaSymbolKind.ColorlessHybrid, ColorSet.FromColor( colorlessHybridColor ) );

		// {W/U}, {B/R}, etc.
		if ( TryGetColor( first, out MagicColor firstColor ) && TryGetColor( second, out MagicColor secondColor ) && firstColor != secondColor )
			return new ManaSymbol( code, ManaSymbolKind.Hybrid, ColorSet.FromColors( firstColor, secondColor ) );

		throw new FormatException( $"Unsupported mana symbol '{{{code}}}'." );
	}


	private static ManaSymbol ParseThreePartSymbol( string code, string first, string second, string third )
	{
		// {W/U/P}, {B/G/P}, etc.
		if ( third == "P" && TryGetColor( first, out MagicColor firstColor ) && TryGetColor( second, out MagicColor secondColor ) && firstColor != secondColor )
			return new ManaSymbol( code, ManaSymbolKind.HybridPhyrexian, ColorSet.FromColors( firstColor, secondColor ) );

		throw new FormatException( $"Unsupported mana symbol '{{{code}}}'." );
	}


	private static ManaSymbol Colored( string code, MagicColor color )
	{
		return new ManaSymbol( code, ManaSymbolKind.Colored, ColorSet.FromColor( color ) );
	}


	private static bool TryGetManaComponentColors( string value, out ColorSet colors )
	{
		if ( value == "C" )
		{
			colors = ColorSet.Colorless;

			return true;
		}

		if ( TryGetColor( value, out MagicColor color ) )
		{
			colors = ColorSet.FromColor( color );

			return true;
		}

		colors = ColorSet.Colorless;

		return false;
	}


	private static bool TryGetColor( string value, out MagicColor color )
	{
		switch ( value )
		{
			case "W":
				color = MagicColor.White;

				return true;

			case "U":
				color = MagicColor.Blue;

				return true;

			case "B":
				color = MagicColor.Black;

				return true;

			case "R":
				color = MagicColor.Red;

				return true;

			case "G":
				color = MagicColor.Green;

				return true;

			default:
				color = default(MagicColor);

				return false;
		}
	}


	private static bool IsAsciiNumber( string value )
	{
		foreach ( char character in value )
		{
			if ( character < '0' || character > '9' )
				return false;
		}

		return value.Length > 0;
	}
}
