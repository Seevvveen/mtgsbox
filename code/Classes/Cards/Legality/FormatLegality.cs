#nullable enable

using System;
namespace Sandbox.Classes.Cards.Legality;

/// <summary>
///     Legality indexed by Scryfall format code. A dictionary is intentional:
///     Scryfall can introduce formats without requiring a database schema change.
/// </summary>
public sealed record FormatLegalities
{
	private readonly Dictionary<string, CardLegality> _byFormat = new Dictionary<string, CardLegality>( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, string> _sourceValues = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

	public Dictionary<string, CardLegality> ByFormat
	{
		get { return _byFormat; }
		init { _byFormat = value is null? new Dictionary<string, CardLegality>( StringComparer.OrdinalIgnoreCase ) : new Dictionary<string, CardLegality>( value, StringComparer.OrdinalIgnoreCase ); }
	}

	/// <summary>Original provider values, including values this build does not recognize.</summary>
	public Dictionary<string, string> SourceValues
	{
		get { return _sourceValues; }
		init { _sourceValues = value is null? new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase ) : new Dictionary<string, string>( value, StringComparer.OrdinalIgnoreCase ); }
	}


	public CardLegality? Get( string format )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( format );

		return ByFormat.TryGetValue( format, out CardLegality legality )? legality : null;
	}
}
