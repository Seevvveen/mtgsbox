#nullable enable

using Sandbox.Classes.CardDatabase;
using Sandbox.Classes.Cards;
using Sandbox.Classes.Cards.CardFrames;
using Sandbox.Classes.Cards.Colors;
using Sandbox.Classes.Cards.Legality;
using Sandbox.Classes.Cards.ManaSymbols;
using Sandbox.Classes.Database.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Sandbox.Classes.Database;

public static class ScryfallCardNormalizer
{
	public static NormalizedCard Normalize( ScryfallCardDto dto )
	{
		ArgumentNullException.ThrowIfNull( dto );
		string objectKind =
			RequireObjectKind( dto.Object, "object", "card" );

		return new NormalizedCard
		{
			Gameplay = new CardGameplayData
			{
				ScryfallId = ParseRequiredGuid( dto.Id, "id" ),
				OracleId = ParseOptionalGuid( dto.OracleId, "oracle_id" ),
				Layout = ParseLayout( RequireString( dto.Layout, "layout" ) ),
				SourceManaCost = dto.ManaCost,
				Faces = NormalizeFaces( dto ),
				ManaValue = dto.Cmc,
				ColorIdentity = ParseRequiredColors(
					dto.ColorIdentity,
					"color_identity" ),
				Colors = ParseOptionalColors( dto.Colors, "colors" ),
				ColorIndicator = ParseOptionalColors(
					dto.ColorIndicator,
					"color_indicator" ),
				Defense = ToDefense( dto.Defense ),
				HandModifier = ToHandModifier( dto.HandModifier ),
				Keywords = new CardKeywords
				{
					Values = CopyArray( dto.Keywords )
				},
				Legalities = ParseLegalities( dto.Legalities ),
				LifeModifier = ToLifeModifier( dto.LifeModifier ),
				Loyalty = ToLoyalty( dto.Loyalty ),
				Name = RequireString( dto.Name, "name" ),
				OracleText = dto.OracleText,
				Power = ToPower( dto.Power ),
				ProducedMana = ParseProducedMana( dto.ProducedMana ),
				Reserved = dto.Reserved,
				Toughness = ToToughness( dto.Toughness ),
				TypeLine = dto.TypeLine,
				AllParts = NormalizeRelatedCards( dto.AllParts ),
				EdhrecRank = dto.EdhrecRank,
				PennyRank = dto.PennyRank,
				GameChanger = dto.GameChanger
			},
			Presentation = new CardPresentationData
			{
				Artist = dto.Artist,
				ArtistIds = ParseOptionalGuidArray(
					dto.ArtistIds,
					"artist_ids" ),
				AttractionLights = CopyNullableArray(
					dto.AttractionLights ),
				Booster = dto.Booster,
				BorderColor = ParseBorderColor(
					RequireString( dto.BorderColor, "border_color" ) ),
				CardBack = ParseOptionalGuid(
					dto.CardBackId,
					"card_back_id" ),
				CollectorNumber = RequireString(
					dto.CollectorNumber,
					"collector_number" ),
				ContentWarning = dto.ContentWarning,
				Digital = dto.Digital,
				Finishes = ParseFinishes( dto.Finishes ),
				Foil = dto.Foil,
				Nonfoil = dto.Nonfoil,
				FlavorName = dto.FlavorName,
				FlavorText = dto.FlavorText,
				Frame = ParseFrame( RequireString( dto.Frame, "frame" ) ),
				FrameEffects = ParseFrameEffects( dto.FrameEffects ),
				FullArt = dto.FullArt,
				Games = CopyArray( dto.Games ),
				HighResolutionImage = dto.HighresImage,
				IllustrationId = ParseOptionalGuid(
					dto.IllustrationId,
					"illustration_id" ),
				Images = NormalizeImages( dto.ImageUris ),
				ImageStatus = RequireString(
					dto.ImageStatus,
					"image_status" ),
				ImageUpdatedAt = dto.ImageUpdatedAt,
				Oversized = dto.Oversized,
				Prices = NormalizePrices(
					dto.Prices
						?? throw MissingField( "prices" ) ),
				PrintedName = dto.PrintedName,
				PrintedText = dto.PrintedText,
				PrintedTypeLine = dto.PrintedTypeLine,
				Promo = dto.Promo,
				PromoTypes = CopyNullableArray( dto.PromoTypes ),
				Rarity = ParseRarity(
					RequireString( dto.Rarity, "rarity" ) ),
				ReleasedAt = dto.ReleasedAt,
				Reprint = dto.Reprint,
				SecurityStamp = dto.SecurityStamp,
				StorySpotlight = dto.StorySpotlight,
				Textless = dto.Textless,
				Variation = dto.Variation,
				VariationOf = ParseOptionalGuid(
					dto.VariationOf,
					"variation_of" ),
				Watermark = dto.Watermark,
				Preview = NormalizePreview( dto.Preview )
			},
			Identifiers = new CardIdentifierData
			{
				ArenaId = dto.ArenaId,
				MtgoId = dto.MtgoId,
				MtgoFoilId = dto.MtgoFoilId,
				MultiverseIds = CopyNullableArray( dto.MultiverseIds ),
				TcgplayerId = dto.TcgplayerId,
				TcgplayerEtchedId = dto.TcgplayerEtchedId,
				CardmarketId = dto.CardmarketId,
				ResourceId = dto.ResourceId
			},
			Set = new CardSetData
			{
				Id = ParseRequiredGuid( dto.SetId, "set_id" ),
				Code = RequireString( dto.SetCode, "set" ),
				Name = RequireString( dto.SetName, "set_name" ),
				Type = RequireString( dto.SetType, "set_type" ),
				ApiUri = RequireString( dto.SetUri, "set_uri" ),
				SearchUri = RequireString(
					dto.SetSearchUri,
					"set_search_uri" ),
				ScryfallUri = RequireString(
					dto.ScryfallSetUri,
					"scryfall_set_uri" )
			},
			Links = new CardResourceLinks
			{
				ApiUri = RequireString( dto.Uri, "uri" ),
				ScryfallUri = RequireString(
					dto.ScryfallUri,
					"scryfall_uri" ),
				PrintsSearchUri = RequireString(
					dto.PrintsSearchUri,
					"prints_search_uri" ),
				RulingsUri = RequireString(
					dto.RulingsUri,
					"rulings_uri" ),
				PurchaseUris = CopyNullableDictionary(
					dto.PurchaseUris ),
				RelatedUris = CopyDictionary( dto.RelatedUris )
			},
			Source = new CardSourceMetadata
			{
				Object = objectKind,
				Language = RequireString( dto.Lang, "lang" ),
				Extensions = CopyExtensions( dto.AdditionalFields )
			}
		};
	}

	private static CardFace[] NormalizeFaces( ScryfallCardDto dto )
	{
		if ( dto.CardFaces is null )
			return [NormalizeSingleFace( dto )];

		if ( dto.CardFaces.Length == 0 )
		{
			throw new InvalidDataException(
				"Scryfall field 'card_faces' cannot be an empty array." );
		}

		var faces = new CardFace[dto.CardFaces.Length];

		for ( var index = 0; index < dto.CardFaces.Length; index++ )
		{
			ScryfallCardFaceDto source =
				dto.CardFaces[index]
				?? throw new InvalidDataException(
					$"Scryfall field 'card_faces[{index}]' cannot be null." );

			string prefix = $"card_faces[{index}]";

			faces[index] = new CardFace
			{
				Object = RequireObjectKind(
					source.Object,
					$"{prefix}.object",
					"card_face" ),
				Name = RequireString(
					source.Name,
					$"{prefix}.name" ),
				SourceManaCost = source.ManaCost,
				ManaCost = ParseFaceManaCost(
					source.ManaCost,
					$"{prefix}.mana_cost" ),
				TypeLine = source.TypeLine,
				OracleText = source.OracleText,
				Colors = ParseOptionalColors(
					source.Colors,
					$"{prefix}.colors" ),
				ColorIndicator = ParseOptionalColors(
					source.ColorIndicator,
					$"{prefix}.color_indicator" ),
				Power = ToPower( source.Power ),
				Toughness = ToToughness( source.Toughness ),
				Loyalty = ToLoyalty( source.Loyalty ),
				Defense = ToDefense( source.Defense ),
				Artist = source.Artist,
				ArtistId = ParseOptionalGuid(
					source.ArtistId,
					$"{prefix}.artist_id" ),
				IllustrationId = ParseOptionalGuid(
					source.IllustrationId,
					$"{prefix}.illustration_id" ),
				Images = NormalizeImages( source.ImageUris ),
				FlavorName = source.FlavorName,
				FlavorText = source.FlavorText,
				PrintedName = source.PrintedName,
				PrintedText = source.PrintedText,
				PrintedTypeLine = source.PrintedTypeLine,
				OracleId = ParseOptionalGuid(
					source.OracleId,
					$"{prefix}.oracle_id" ),
				Layout = source.Layout,
				ManaValue = source.Cmc,
				Watermark = source.Watermark,
				SourceExtensions = CopyExtensions(
					source.AdditionalFields )
			};
		}

		return faces;
	}

	private static CardFace NormalizeSingleFace( ScryfallCardDto dto )
	{
		Guid[]? artistIds = ParseOptionalGuidArray(
			dto.ArtistIds,
			"artist_ids" );

		return new CardFace
		{
			Object = "card_face",
			Name = RequireString( dto.Name, "name" ),
			SourceManaCost = dto.ManaCost,
			ManaCost = ParseFaceManaCost( dto.ManaCost, "mana_cost" ),
			TypeLine = dto.TypeLine,
			OracleText = dto.OracleText,
			Colors = ParseOptionalColors( dto.Colors, "colors" ),
			ColorIndicator = ParseOptionalColors(
				dto.ColorIndicator,
				"color_indicator" ),
			Power = ToPower( dto.Power ),
			Toughness = ToToughness( dto.Toughness ),
			Loyalty = ToLoyalty( dto.Loyalty ),
			Defense = ToDefense( dto.Defense ),
			Artist = dto.Artist,
			ArtistId = artistIds is { Length: 1 }
				? artistIds[0]
				: null,
			IllustrationId = ParseOptionalGuid(
				dto.IllustrationId,
				"illustration_id" ),
			Images = NormalizeImages( dto.ImageUris ),
			FlavorName = dto.FlavorName,
			FlavorText = dto.FlavorText,
			PrintedName = dto.PrintedName,
			PrintedText = dto.PrintedText,
			PrintedTypeLine = dto.PrintedTypeLine,
			OracleId = ParseOptionalGuid( dto.OracleId, "oracle_id" ),
			Watermark = dto.Watermark
		};
	}

	private static RelatedCard[]? NormalizeRelatedCards(
		ScryfallRelatedCardDto[]? source )
	{
		if ( source is null )
			return null;

		if ( source.Length == 0 )
			return [];

		var result = new RelatedCard[source.Length];

		for ( var index = 0; index < source.Length; index++ )
		{
			ScryfallRelatedCardDto item =
				source[index]
				?? throw new InvalidDataException(
					$"Scryfall field 'all_parts[{index}]' cannot be null." );

			string prefix = $"all_parts[{index}]";

			result[index] = new RelatedCard
			{
				Id = ParseRequiredGuid( item.Id, $"{prefix}.id" ),
				Object = RequireString(
					item.Object,
					$"{prefix}.object" ),
				Component = RequireString(
					item.Component,
					$"{prefix}.component" ),
				Name = RequireString( item.Name, $"{prefix}.name" ),
				TypeLine = RequireString(
					item.TypeLine,
					$"{prefix}.type_line" ),
				ApiUri = RequireString( item.Uri, $"{prefix}.uri" ),
				SourceExtensions = CopyExtensions(
					item.AdditionalFields )
			};
		}

		return result;
	}

	private static CardImages? NormalizeImages(
		ScryfallImageUrisDto? source )
	{
		if ( source is null )
			return null;

		return new CardImages
		{
			Small = source.Small,
			Normal = source.Normal,
			Large = source.Large,
			Png = source.Png,
			ArtCrop = source.ArtCrop,
			BorderCrop = source.BorderCrop,
			Thumb = source.Thumb,
			Grid = source.Grid,
			Display = source.Display,
			Art = source.Art,
			Crop = source.Crop,
			SourceExtensions = CopyExtensions(
				source.AdditionalFields )
		};
	}

	private static CardPrices NormalizePrices(
		ScryfallPricesDto source )
	{
		return new CardPrices
		{
			Usd = source.Usd,
			UsdFoil = source.UsdFoil,
			UsdEtched = source.UsdEtched,
			Eur = source.Eur,
			EurFoil = source.EurFoil,
			EurEtched = source.EurEtched,
			Tix = source.Tix,
			SourceExtensions = CopyExtensions(
				source.AdditionalFields )
		};
	}

	private static CardPreview? NormalizePreview(
		ScryfallPreviewDto? source )
	{
		if ( source is null )
			return null;

		return new CardPreview
		{
			PreviewedAt = source.PreviewedAt,
			SourceUri = source.SourceUri,
			Source = source.Source,
			SourceExtensions = CopyExtensions(
				source.AdditionalFields )
		};
	}

	private static FormatLegalities ParseLegalities(
		Dictionary<string, string>? values )
	{
		if ( values is null )
			throw MissingField( "legalities" );

		var result = new Dictionary<string, CardLegality>(
			values.Count,
			StringComparer.OrdinalIgnoreCase );

		foreach ( KeyValuePair<string, string> pair in values )
		{
			result[pair.Key] = pair.Value switch
			{
				"not_legal" => CardLegality.NotLegal,
				"legal" => CardLegality.Legal,
				"restricted" => CardLegality.Restricted,
				"banned" => CardLegality.Banned,
				_ => throw UnknownValue(
					$"legalities.{pair.Key}",
					pair.Value )
			};
		}

		return new FormatLegalities { ByFormat = result };
	}

	private static CardFinish[] ParseFinishes( string[]? values )
	{
		if ( values is not { Length: > 0 } )
			return [];

		var results = new CardFinish[values.Length];

		for ( var index = 0; index < values.Length; index++ )
		{
			results[index] = values[index] switch
			{
				"nonfoil" => CardFinish.Nonfoil,
				"foil" => CardFinish.Foil,
				"etched" => CardFinish.Etched,
				_ => throw UnknownValue( "finishes", values[index] )
			};
		}

		return results;
	}

	private static ManaCost ParseFaceManaCost(
		string? value,
		string field )
	{
		if ( value is null )
			return ManaCost.None;

		try
		{
			return ManaCost.Parse( value );
		}
		catch ( FormatException exception )
		{
			throw InvalidValue( field, value, "mana cost", exception );
		}
		catch ( ArgumentException exception )
		{
			throw InvalidValue( field, value, "mana cost", exception );
		}
	}

	private static ColorSet ParseRequiredColors(
		string[]? values,
		string field )
	{
		if ( values is null )
			throw MissingField( field );

		return ParseColors( values, field );
	}

	private static ColorSet? ParseOptionalColors(
		string[]? values,
		string field )
	{
		return values is null ? null : ParseColors( values, field );
	}

	private static ColorSet ParseColors(
		string[] values,
		string field )
	{
		try
		{
			return ColorSet.FromScryfall( values );
		}
		catch ( ArgumentException exception )
		{
			throw new InvalidDataException(
				$"Scryfall field '{field}' contains an invalid color.",
				exception );
		}
	}

	private static ProducedManaSet? ParseProducedMana(
		string[]? values )
	{
		try
		{
			return ProducedManaSet.FromNullableScryfall( values );
		}
		catch ( ArgumentException exception )
		{
			throw new InvalidDataException(
				"Scryfall field 'produced_mana' contains an invalid value.",
				exception );
		}
		catch ( FormatException exception )
		{
			throw new InvalidDataException(
				"Scryfall field 'produced_mana' contains an invalid value.",
				exception );
		}
	}

	private static CardDefense? ToDefense( string? value ) =>
		value is null ? null : new CardDefense( value );

	private static HandModifier? ToHandModifier( string? value ) =>
		value is null ? null : new HandModifier( value );

	private static LifeModifier? ToLifeModifier( string? value ) =>
		value is null ? null : new LifeModifier( value );

	private static CardLoyalty? ToLoyalty( string? value ) =>
		value is null ? null : new CardLoyalty( value );

	private static CardPower? ToPower( string? value ) =>
		value is null ? null : new CardPower( value );

	private static CardToughness? ToToughness( string? value ) =>
		value is null ? null : new CardToughness( value );

	private static CardLayout ParseLayout( string value )
	{
		return value switch
		{
			"normal" => CardLayout.Normal,
			"split" => CardLayout.Split,
			"flip" => CardLayout.Flip,
			"transform" => CardLayout.Transform,
			"modal_dfc" => CardLayout.ModalDfc,
			"meld" => CardLayout.Meld,
			"leveler" => CardLayout.Leveler,
			"class" => CardLayout.Class,
			"case" => CardLayout.Case,
			"saga" => CardLayout.Saga,
			"adventure" => CardLayout.Adventure,
			"prepare" => CardLayout.Prepare,
			"mutate" => CardLayout.Mutate,
			"prototype" => CardLayout.Prototype,
			"battle" => CardLayout.Battle,
			"planar" => CardLayout.Planar,
			"scheme" => CardLayout.Scheme,
			"vanguard" => CardLayout.Vanguard,
			"token" => CardLayout.Token,
			"double_faced_token" => CardLayout.DoubleFacedToken,
			"emblem" => CardLayout.Emblem,
			"augment" => CardLayout.Augment,
			"host" => CardLayout.Host,
			"art_series" => CardLayout.ArtSeries,
			"reversible_card" => CardLayout.ReversibleCard,
			_ => throw UnknownValue( "layout", value )
		};
	}

	private static CardFrame ParseFrame( string value )
	{
		return value switch
		{
			"1993" => CardFrame.Frame1993,
			"1997" => CardFrame.Frame1997,
			"2003" => CardFrame.Frame2003,
			"2015" => CardFrame.Frame2015,
			"future" => CardFrame.Future,
			_ => throw UnknownValue( "frame", value )
		};
	}

	private static BorderColor ParseBorderColor( string value )
	{
		return value switch
		{
			"black" => BorderColor.Black,
			"white" => BorderColor.White,
			"borderless" => BorderColor.Borderless,
			"silver" => BorderColor.Silver,
			"yellow" => BorderColor.Yellow,
			"gold" => BorderColor.Gold,
			_ => throw UnknownValue( "border_color", value )
		};
	}

	private static CardRarity ParseRarity( string value )
	{
		return value switch
		{
			"common" => CardRarity.Common,
			"uncommon" => CardRarity.Uncommon,
			"rare" => CardRarity.Rare,
			"special" => CardRarity.Special,
			"mythic" => CardRarity.Mythic,
			"bonus" => CardRarity.Bonus,
			_ => throw UnknownValue( "rarity", value )
		};
	}

	private static FrameEffect[]? ParseFrameEffects( string[]? values )
	{
		if ( values is null )
			return null;

		if ( values.Length == 0 )
			return [];

		var results = new FrameEffect[values.Length];

		for ( var index = 0; index < values.Length; index++ )
			results[index] = ParseFrameEffect( values[index] );

		return results;
	}

	private static FrameEffect ParseFrameEffect( string value )
	{
		return value switch
		{
			"legendary" => FrameEffect.Legendary,
			"miracle" => FrameEffect.Miracle,
			"enchantment" => FrameEffect.Enchantment,
			"draft" => FrameEffect.Draft,
			"devoid" => FrameEffect.Devoid,
			"tombstone" => FrameEffect.Tombstone,
			"colorshifted" => FrameEffect.Colorshifted,
			"inverted" => FrameEffect.Inverted,
			"sunmoondfc" => FrameEffect.SunMoonDfc,
			"compasslanddfc" => FrameEffect.CompassLandDfc,
			"originpwdfc" => FrameEffect.OriginPwDfc,
			"mooneldrazidfc" => FrameEffect.MoonEldraziDfc,
			"waxingandwaningmoondfc" =>
				FrameEffect.WaxingAndWaningMoonDfc,
			"showcase" => FrameEffect.Showcase,
			"extendedart" => FrameEffect.ExtendedArt,
			"companion" => FrameEffect.Companion,
			"etched" => FrameEffect.Etched,
			"snow" => FrameEffect.Snow,
			"lesson" => FrameEffect.Lesson,
			"shatteredglass" => FrameEffect.ShatteredGlass,
			"convertdfc" => FrameEffect.ConvertDfc,
			"fandfc" => FrameEffect.FanDfc,
			"upsidedowndfc" => FrameEffect.UpsideDownDfc,
			"spree" => FrameEffect.Spree,
			"fullart" => FrameEffect.Fullart,
			_ => throw UnknownValue( "frame_effects", value )
		};
	}

	private static Guid ParseRequiredGuid( string? value, string field )
	{
		if ( Guid.TryParse( value, out Guid result ) )
			return result;

		throw new InvalidDataException(
			$"Scryfall field '{field}' contains invalid GUID " +
			$"'{value ?? "<null>"}'." );
	}

	private static Guid? ParseOptionalGuid( string? value, string field )
	{
		if ( string.IsNullOrWhiteSpace( value ) )
			return null;

		if ( Guid.TryParse( value, out Guid result ) )
			return result;

		throw new InvalidDataException(
			$"Scryfall field '{field}' contains invalid GUID '{value}'." );
	}

	private static Guid[]? ParseOptionalGuidArray(
		string[]? values,
		string field )
	{
		if ( values is null )
			return null;

		var result = new Guid[values.Length];

		for ( var index = 0; index < values.Length; index++ )
		{
			result[index] = ParseRequiredGuid(
				values[index],
				$"{field}[{index}]" );
		}

		return result;
	}

	private static T[] CopyArray<T>( T[]? values ) =>
		values is null ? [] : [.. values];

	private static T[]? CopyNullableArray<T>( T[]? values ) =>
		values is null ? null : [.. values];

	private static Dictionary<string, string> CopyDictionary(
		Dictionary<string, string>? values )
	{
		return values is null
			? new Dictionary<string, string>(
				StringComparer.OrdinalIgnoreCase )
			: new Dictionary<string, string>(
				values,
				StringComparer.OrdinalIgnoreCase );
	}

	private static Dictionary<string, string>? CopyNullableDictionary(
		Dictionary<string, string>? values )
	{
		return values is null ? null : CopyDictionary( values );
	}

	private static Dictionary<string, JsonElement> CopyExtensions(
		Dictionary<string, JsonElement>? values )
	{
		if ( values is not { Count: > 0 } )
			return [];

		var result = new Dictionary<string, JsonElement>(
			values.Count,
			StringComparer.Ordinal );

		foreach ( KeyValuePair<string, JsonElement> pair in values )
			result.Add( pair.Key, pair.Value.Clone() );

		return result;
	}

	private static InvalidDataException InvalidValue(
		string field,
		string value,
		string valueType,
		Exception innerException )
	{
		return new InvalidDataException(
			$"Scryfall field '{field}' contains invalid {valueType} " +
			$"'{value}'.",
			innerException );
	}

	private static InvalidDataException UnknownValue(
		string field,
		string value )
	{
		return new InvalidDataException(
			$"Unknown Scryfall {field} value '{value}'." );
	}

	private static InvalidDataException MissingField( string field )
	{
		return new InvalidDataException(
			$"Required Scryfall field '{field}' is missing." );
	}

	private static string RequireString(
		string? value,
		string field )
	{
		if ( string.IsNullOrWhiteSpace( value ) )
			throw MissingField( field );

		return value;
	}

	private static string RequireObjectKind(
		string? value,
		string field,
		string expected )
	{
		string actual = RequireString( value, field );

		if ( !string.Equals(
			actual,
			expected,
			StringComparison.Ordinal ) )
		{
			throw new InvalidDataException(
				$"Expected Scryfall field '{field}' to be '{expected}', " +
				$"but received '{actual}'."
			);
		}

		return actual;
	}
}
