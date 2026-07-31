#nullable enable

using Sandbox.Classes.Database.Types;
using System;
using System.IO;
using System.Text.Json;
namespace Sandbox.Classes.Database;

public static class ScryfallSupplementalNormalizer
{
	public static CardSetDefinition NormalizeSet( ScryfallSetDto source )
	{
		ArgumentNullException.ThrowIfNull( source );

		return new CardSetDefinition
			   {
				   Object           = RequireString( source.Object, "object" ),
				   Id               = ParseGuid( source.Id, "id" ),
				   Code             = RequireString( source.Code, "code" ),
				   MtgoCode         = source.MtgoCode,
				   ArenaCode        = source.ArenaCode,
				   TcgplayerId      = source.TcgplayerId,
				   Name             = RequireString( source.Name, "name" ),
				   Type             = RequireString( source.SetType, "set_type" ),
				   ReleasedAt       = source.ReleasedAt,
				   BlockCode        = source.BlockCode,
				   Block            = source.Block,
				   ParentSetCode    = source.ParentSetCode,
				   CardCount        = source.CardCount,
				   PrintedSize      = source.PrintedSize,
				   Digital          = source.Digital,
				   FoilOnly         = source.FoilOnly,
				   NonfoilOnly      = source.NonfoilOnly,
				   ScryfallUri      = RequireString( source.ScryfallUri, "scryfall_uri" ),
				   ApiUri           = RequireString( source.Uri, "uri" ),
				   IconSvgUri       = RequireString( source.IconSvgUri, "icon_svg_uri" ),
				   SearchUri        = RequireString( source.SearchUri, "search_uri" ),
				   SourceExtensions = CopyExtensions( source.AdditionalFields )
			   };
	}


	public static CardRuling NormalizeRuling( ScryfallRulingDto source )
	{
		ArgumentNullException.ThrowIfNull( source );

		return new CardRuling
			   {
				   Object      = RequireString( source.Object, "object" ),
				   OracleId    = ParseGuid( source.OracleId, "oracle_id" ),
				   Source      = RequireString( source.Source, "source" ),
				   PublishedAt = source.PublishedAt,

				   // Empty comments occur in Scryfall's real rulings bulk export.
				   Comment          = source.Comment ?? "",
				   SourceExtensions = CopyExtensions( source.AdditionalFields )
			   };
	}


	private static Guid ParseGuid( string? value, string field )
	{
		if ( Guid.TryParse( value, out Guid result ) )
			return result;

		throw new InvalidDataException( $"Scryfall field '{field}' contains invalid GUID " + $"'{value ?? "<null>"}'." );
	}


	private static string RequireString( string? value, string field )
	{
		if ( string.IsNullOrWhiteSpace( value ) )
			throw new InvalidDataException( $"Required Scryfall field '{field}' is missing." );

		return value;
	}


	private static Dictionary<string, JsonElement> CopyExtensions( Dictionary<string, JsonElement>? source )
	{
		if ( source is not { Count: > 0 } )
			return [ ];

		Dictionary<string, JsonElement> result = new Dictionary<string, JsonElement>( source.Count, StringComparer.Ordinal );

		foreach ( KeyValuePair<string, JsonElement> pair in source )
			result.Add( pair.Key, pair.Value.Clone() );

		return result;
	}
}
