#nullable enable

using Sandbox.Classes.Database.Types;
using System;
using RuntimeCardDatabase = Sandbox.Classes.Database.CardDatabase;

namespace Sandbox.Classes.DeckImport;

/// <summary>
///     Resolves external deck-list identities against the open local card database.
/// </summary>
public sealed class DatabaseDeckCardResolver : IDeckCardResolver
{
	public DeckCardResolution Resolve( DeckCardQuery query )
	{
		if ( query.ScryfallId is Guid scryfallId && scryfallId != Guid.Empty )
		{
			NormalizedCard? byId = RuntimeCardDatabase.GetCard( scryfallId );

			return FromSingle( byId );
		}

		if ( !string.IsNullOrWhiteSpace( query.SetCode ) && !string.IsNullOrWhiteSpace( query.CollectorNumber ) )
		{
			NormalizedCard? byPrinting = RuntimeCardDatabase.FindPrinting( NormalizeSetCode( query.SetCode ), query.CollectorNumber );

			if ( byPrinting is not null )
				return FromSingle( byPrinting );
		}

		NormalizedCard[] matches = RuntimeCardDatabase.FindByName( query.Name );

		if ( !string.IsNullOrWhiteSpace( query.SetCode ) )
		{
			string setCode = NormalizeSetCode( query.SetCode );
			matches = matches.Where( card => string.Equals( card.Set.Code, setCode, StringComparison.OrdinalIgnoreCase ) ).ToArray();
		}

		if ( matches.Length == 0 )
			return new DeckCardResolution();

		// The Lua importer chooses the first exact-name result. Preserve that
		// behavior but report ambiguity so a deck-building UI can offer art.
		return new DeckCardResolution { Card = CreateReference( matches[0] ), MatchCount = matches.Length };
	}


	private static DeckCardResolution FromSingle( NormalizedCard? card )
	{
		return new DeckCardResolution { Card = card is null? null : CreateReference( card ), MatchCount = card is null? 0 : 1 };
	}


	private static DeckCardReference CreateReference( NormalizedCard card )
	{
		return new DeckCardReference
			   {
				   ScryfallId      = card.Gameplay.ScryfallId,
				   OracleId        = card.Gameplay.OracleId,
				   Name            = card.Gameplay.Name,
				   SetCode         = card.Set.Code,
				   CollectorNumber = card.Presentation.CollectorNumber
			   };
	}


	private static string NormalizeSetCode( string setCode )
	{
		string trimmed = setCode.Trim();
		int    suffix  = trimmed.IndexOf( '_' );

		return suffix < 0? trimmed : trimmed[..suffix];
	}
}
