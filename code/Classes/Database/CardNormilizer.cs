using Sandbox.Classes.CardDatabase;
using Sandbox.Classes.Cards;
using Sandbox.Classes.Cards.CardFrames;
using Sandbox.Classes.Cards.Colors;
using Sandbox.Classes.Database.Types;
using System;
using System.IO;

namespace Sandbox.Classes.Database;

public static class ScryfallCardNormalizer
{
	public static NormalizedCard Normalize( ScryfallCardDto dto )
	{
		ArgumentNullException.ThrowIfNull( dto );

		return new NormalizedCard
		{
			Gameplay = new CardGameplayData
			{
				ScryfallId = ParseRequiredGuid( dto.Id, "id" ),
				OracleId = ParseOptionalGuid( dto.OracleId, "oracle_id" ),
				Layout = ParseLayout( dto.Layout ),
				ManaValue = dto.Cmc,
				Name = dto.Name,
				OracleText = dto.OracleText ?? "",

				// Requires normalizing either card_faces or synthesizing
				// one face from the top-level card fields.
				// Faces = NormalizeFaces( dto ),

				// Requires a ManaCost parser for values such as:
				// "{2}{W}{W}", "{X}{R}", and "{W/U}".
				//ManaCost = ManaCost.Parse( dto.ManaCost ),
				
				ColorIdentity = ColorSet.FromScryfall(
					dto.ColorIdentity
					?? throw MissingField( "color_identity" ) ),

				// Top-level Colors can be absent on multifaced cards.
				// Define whether this represents the combined card colors
				// or only colors explicitly provided at the top level.
				Colors = ColorSet.FromNullableScryfall( dto.Colors ),

				// Defense is not guaranteed to be a plain integer.
				// Requires CardDefense.Parse or TryParse.
				// Defense = ParseDefense( dto.Defense ),

				// Requires support for missing values and values such as "+1".
				// HandModifer = ParseHandModifier( dto.HandModifier ),

				// Requires mapping the string array into KeywordAbility values.
				// Parameterized abilities will eventually need ability instances.
				// Keywords = ParseKeywords( dto.Keywords ),

				// Requires converting the open-ended format dictionary into
				// CardLegalities. Keeping unknown formats should be considered.
				// Legalities = ParseLegalities( dto.Legalities ),

				// Requires support for missing values and values such as "+5".
				// LifeModifer = ParseLifeModifier( dto.LifeModifier ),

				// Loyalty can contain non-integer values in source data.
				// Requires CardLoyalty.Parse or TryParse.
				// Loyalty = ParseLoyalty( dto.Loyalty ),

				// Power can contain "*", "1+*", and other non-integer values.
				// Requires CardPower.Parse or a symbolic representation.
				// Power = ParsePower( dto.Power ),

				// Requires a ColorSet constructor/parser.
				ProducedMana =
					ProducedManaSet.FromNullableScryfall( dto.ProducedMana ),

				// Toughness can contain "*" and other non-integer values.
				// Requires CardToughness.Parse or a symbolic representation.
				// Toughness = ParseToughness( dto.Toughness ),

				// Parsing a type line requires separating:
				// supertypes, card types and subtypes.
				// Types = ParseTypeLine( dto.TypeLine ).Types,
			},

			Presentation = new CardPresentationData
			{
				BorderColor = ParseBorderColor( dto.BorderColor ),
				CardBack = ParseOptionalGuid( dto.CardBackId, nameof( dto.CardBackId ) ),
				Frame = ParseFrame( dto.Frame ),
				FrameEffects = ParseFrameEffects( dto.FrameEffects ),
				Rarity = ParseRarity( dto.Rarity ),
				FullArt = dto.FullArt,
				Oversized = dto.Oversized,
				FlavorName = dto.FlavorName,
				FlavorText = dto.FlavorText,
				PrintedName = dto.PrintedName,
				PrintedText = dto.PrintedText,
				PrintedTypeLine = dto.PrintedTypeLine,

				// Requires converting each Scryfall finish into CardFinish.
				// Finishes = ParseFinishes( dto.Finishes ),

				// Multifaced cards may store image_uris on their faces.
				// Decide whether CardImages represents only the parent image
				// or includes an image set for every face.
				// Images = NormalizeImages( dto ),
			}
		};
	}
	
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
	
	private static FrameEffect[] ParseFrameEffects( string[]? values )
	{
		if ( values is not { Length: > 0 } )
			return [];

		var results = new FrameEffect[values.Length];

		for ( var i = 0; i < values.Length; i++ )
			results[i] = ParseFrameEffect( values[i] );

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
			"waxingandwaningmoondfc" => FrameEffect.WaxingAndWaningMoonDfc,
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
	
	private static Guid ParseRequiredGuid( string value, string field )
	{
		if ( Guid.TryParse( value, out var result ) )
			return result;

		throw new InvalidDataException(
			$"Scryfall field '{field}' contains invalid GUID '{value}'." );
	}

	private static Guid? ParseOptionalGuid( string? value, string field )
	{
		if ( string.IsNullOrWhiteSpace( value ) )
			return null;

		if ( Guid.TryParse( value, out var result ) )
			return result;

		throw new InvalidDataException(
			$"Scryfall field '{field}' contains invalid GUID '{value}'." );
	}

	private static InvalidDataException UnknownValue( string field, string value )
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
}