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
	public Dictionary<string, CardLegality> ByFormat { get; init; } =
		new( StringComparer.OrdinalIgnoreCase );

	public CardLegality? Get( string format )
	{
		return ByFormat.TryGetValue( format, out CardLegality legality )
			? legality
			: null;
	}
}
