#nullable enable

using System;
using System.Collections.Generic;

namespace Sandbox.Classes.Cards.Legality;

/// <summary>
/// Legality indexed by Scryfall format code. A dictionary is intentional:
/// Scryfall can introduce formats without requiring a database schema change.
/// </summary>
public sealed record FormatLegalities
{
	private Dictionary<string, CardLegality> _byFormat =
		new( StringComparer.OrdinalIgnoreCase );

	public Dictionary<string, CardLegality> ByFormat
	{
		get => _byFormat;
		init => _byFormat = value is null
			? new Dictionary<string, CardLegality>(
				StringComparer.OrdinalIgnoreCase )
			: new Dictionary<string, CardLegality>(
				value,
				StringComparer.OrdinalIgnoreCase );
	}

	public CardLegality? Get( string format )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( format );

		return ByFormat.TryGetValue( format, out CardLegality legality )
			? legality
			: null;
	}
}
