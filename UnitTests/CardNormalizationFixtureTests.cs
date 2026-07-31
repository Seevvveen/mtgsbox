#nullable enable

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Classes.Cards;
using Sandbox.Classes.Cards.Colors;
using Sandbox.Classes.Cards.Legality;
using Sandbox.Classes.Cards.ManaSymbols;
using Sandbox.Classes.Database;
using Sandbox.Classes.Database.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox.UnitTests;

[TestClass]
public sealed class CardNormalizationFixtureTests
{
	private const int CurrentSuiteSchemaVersion = 1;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		WriteIndented = false
	};

	private static readonly string[] SupportedLayouts =
	[
		"normal",
		"split",
		"flip",
		"transform",
		"modal_dfc",
		"meld",
		"leveler",
		"class",
		"case",
		"saga",
		"adventure",
		"prepare",
		"mutate",
		"prototype",
		"battle",
		"planar",
		"scheme",
		"vanguard",
		"token",
		"double_faced_token",
		"emblem",
		"augment",
		"host",
		"art_series",
		"reversible_card"
	];

	public static IEnumerable<object[]> ValidCaseIds()
	{
		return LoadSuite().Cases
			.Where( fixture => fixture.Kind == "valid" )
			.Select( fixture => new object[] { fixture.Id } );
	}

	public static IEnumerable<object[]> InvalidCaseIds()
	{
		return LoadSuite().Cases
			.Where( fixture => fixture.Kind == "invalid" )
			.Select( fixture => new object[] { fixture.Id } );
	}

	[TestMethod]
	public void FixtureSuite_IsWellFormedAndCoversSupportedLayouts()
	{
		FixtureSuite suite = LoadSuite();

		Assert.AreEqual(
			CurrentSuiteSchemaVersion,
			suite.SchemaVersion );
		Assert.IsTrue( suite.Cases.Length >= 40 );

		string[] duplicateCaseIds = suite.Cases
			.GroupBy( fixture => fixture.Id, StringComparer.Ordinal )
			.Where( group => group.Count() > 1 )
			.Select( group => group.Key )
			.ToArray();

		CollectionAssert.AreEqual(
			Array.Empty<string>(),
			duplicateCaseIds,
			"Fixture case IDs must be unique." );

		string[] actualLayouts = suite.Cases
			.Where( fixture => fixture.Kind == "valid" )
			.Select( fixture =>
				fixture.Card.GetProperty( "layout" ).GetString()
				?? "" )
			.Distinct( StringComparer.Ordinal )
			.OrderBy( value => value, StringComparer.Ordinal )
			.ToArray();

		string[] expectedLayouts = SupportedLayouts
			.OrderBy( value => value, StringComparer.Ordinal )
			.ToArray();

		CollectionAssert.AreEqual(
			expectedLayouts,
			actualLayouts,
			"The fixture suite must cover every layout recognized by " +
			"ScryfallCardNormalizer." );

		string[] producedManaStates = suite.Cases
			.Where( fixture => fixture.Kind == "valid" )
			.Select( fixture =>
				fixture.Expected?.ProducedManaState ?? "" )
			.Distinct( StringComparer.Ordinal )
			.OrderBy( value => value, StringComparer.Ordinal )
			.ToArray();

		CollectionAssert.AreEqual(
			new[] { "empty", "null", "values" },
			producedManaStates,
			"The suite must distinguish every produced_mana source state." );

		Assert.IsTrue(
			suite.Cases.Any( fixture =>
				fixture.Expected?.ProducedMana?.Contains(
					"T",
					StringComparer.Ordinal ) == true ),
			"The suite must retain Scryfall's funny-card T symbol." );

		AssertCostPatternCoverage( suite );
		AssertUniqueValidScryfallIds( suite );
	}

	[TestMethod]
	[DynamicData(
		nameof( ValidCaseIds ),
		DynamicDataSourceType.Method )]
	public void ValidFixture_NormalizesAndDatabaseJsonRoundTrips(
		string fixtureId )
	{
		FixtureCase fixture = GetCase( fixtureId );
		ExpectedResult expected = fixture.Expected
			?? throw new InvalidDataException(
				$"Valid fixture '{fixtureId}' has no expected result." );
		ScryfallCardDto dto = DeserializeCard( fixture );

		NormalizedCard normalized =
			ScryfallCardNormalizer.Normalize( dto );

		AssertSourceMapping( fixtureId, dto, normalized );

		AssertNormalizedCard(
			fixtureId,
			expected,
			normalized );

		byte[] databaseJson = JsonSerializer.SerializeToUtf8Bytes(
			normalized,
			JsonOptions );

		using (
			JsonDocument document =
				JsonDocument.Parse( databaseJson )
		)
		{
			Assert.IsFalse(
				document.RootElement
					.GetProperty( "Gameplay" )
					.TryGetProperty( "ManaCost", out _ ),
				$"{fixtureId}: gameplay-level ManaCost must not return." );
		}

		NormalizedCard roundTripped =
			JsonSerializer.Deserialize<NormalizedCard>(
				databaseJson,
				JsonOptions )
			?? throw new InvalidDataException(
				$"{fixtureId}: database JSON deserialized to null." );

		AssertSourceMapping(
			$"{fixtureId} after JSON round-trip",
			dto,
			roundTripped );

		AssertNormalizedCard(
			$"{fixtureId} after JSON round-trip",
			expected,
			roundTripped );
	}

	[TestMethod]
	[DynamicData(
		nameof( InvalidCaseIds ),
		DynamicDataSourceType.Method )]
	public void InvalidFixture_IsRejectedWithContext(
		string fixtureId )
	{
		FixtureCase fixture = GetCase( fixtureId );
		string expectedError = fixture.ExpectedErrorContains
			?? throw new InvalidDataException(
				$"Invalid fixture '{fixtureId}' has no expected error." );
		ScryfallCardDto dto = DeserializeCard( fixture );

		InvalidDataException exception =
			Assert.ThrowsException<InvalidDataException>(
				() => ScryfallCardNormalizer.Normalize( dto ),
				$"{fixtureId}: normalization unexpectedly succeeded." );

		StringAssert.Contains(
			exception.ToString(),
			expectedError,
			$"{fixtureId}: rejection lacked useful source context." );
	}

	[TestMethod]
	public void SymbolIdentifier_DatabaseJsonRoundTripsAsValueAndKey()
	{
		SymbolIdentifier identifier =
			SymbolIdentifier.Parse( "{W/U}" );
		var source =
			new Dictionary<SymbolIdentifier, string>
			{
				[identifier] = "hybrid"
			};

		string json = JsonSerializer.Serialize(
			source,
			JsonOptions );
		Dictionary<SymbolIdentifier, string> roundTripped =
			JsonSerializer.Deserialize<
				Dictionary<SymbolIdentifier, string>>(
				json,
				JsonOptions )
			?? throw new InvalidDataException(
				"Symbol dictionary deserialized to null." );

		Assert.AreEqual( "hybrid", roundTripped[identifier] );
	}

	[TestMethod]
	public void ExtensionDataFixtures_PreserveUnknownNestedFields()
	{
		FixtureCase rootFixture = GetCase( "produced-mana-empty" );
		NormalizedCard rootCard = ScryfallCardNormalizer.Normalize(
			DeserializeCard( rootFixture ) );

		Assert.IsTrue(
			rootCard.Source.Extensions.ContainsKey(
				"future_fixture_field" ) );
		Assert.IsTrue(
			rootCard.Presentation.Images?.SourceExtensions.ContainsKey(
				"future_image_field" ) == true );
		Assert.IsTrue(
			rootCard.Presentation.Prices.SourceExtensions.ContainsKey(
				"future_price_field" ) );
		Assert.IsTrue(
			rootCard.Presentation.Preview?.SourceExtensions.ContainsKey(
				"future_preview_field" ) == true );
		Assert.IsTrue(
			rootCard.Gameplay.AllParts?[0].SourceExtensions.ContainsKey(
				"future_related_card_field" ) == true );

		FixtureCase faceFixture =
			GetCase( "face-null-cost-becomes-none" );
		NormalizedCard faceCard = ScryfallCardNormalizer.Normalize(
			DeserializeCard( faceFixture ) );

		Assert.IsTrue(
			faceCard.Gameplay.Faces[0].SourceExtensions.ContainsKey(
				"future_face_field" ) );
	}

	[TestMethod]
	[TestCategory( "Integration" )]
	public void LocalOracleBulk_AllCardsNormalizeAndJsonRoundTrip()
	{
		string path = Path.Combine(
			Environment.GetFolderPath(
				Environment.SpecialFolder.ProgramFilesX86 ),
			"Steam",
			"steamapps",
			"common",
			"sbox",
			"data",
			"magikarp",
			"mtgsbox#local",
			"oracle-cards.json" );

		if ( !File.Exists( path ) )
		{
			Assert.Inconclusive(
				$"Local Scryfall bulk file was not found at '{path}'." );
			return;
		}

		using FileStream file = File.OpenRead( path );
		using Stream decoded = OpenPossiblyGzipped( file );
		using StreamReader reader = new( decoded );

		var ids = new HashSet<Guid>();
		var layouts = new HashSet<string>( StringComparer.Ordinal );
		var count = 0;

		while ( reader.ReadLine() is { } line )
		{
			if ( string.IsNullOrWhiteSpace( line ) )
				continue;

			count++;
			ScryfallCardDto? source = null;

			try
			{
				source = JsonSerializer.Deserialize<ScryfallCardDto>(
					line,
					JsonOptions );
				Assert.IsNotNull( source );

				NormalizedCard normalized =
					ScryfallCardNormalizer.Normalize( source );
				Assert.IsTrue( ids.Add( normalized.Gameplay.ScryfallId ) );
				layouts.Add( source.Layout );

				byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
					normalized,
					JsonOptions );
				NormalizedCard? roundTripped =
					JsonSerializer.Deserialize<NormalizedCard>(
						bytes,
						JsonOptions );

				Assert.IsNotNull( roundTripped );
				Assert.AreEqual(
					normalized.Gameplay.ScryfallId,
					roundTripped.Gameplay.ScryfallId );
				Assert.AreEqual(
					normalized.Gameplay.Faces.Length,
					roundTripped.Gameplay.Faces.Length );
			}
			catch ( Exception exception )
			{
				Assert.Fail(
					$"Bulk card #{count} '{source?.Name ?? "<unknown>"}' " +
					$"({source?.Layout ?? "<unknown>"}) failed: " +
					exception );
			}
		}

		Assert.IsTrue(
			count > 30_000,
			$"Expected a full Oracle Cards corpus, found {count} cards." );

		string[] unsupportedLayouts = layouts
			.Except( SupportedLayouts, StringComparer.Ordinal )
			.OrderBy( value => value, StringComparer.Ordinal )
			.ToArray();

		CollectionAssert.AreEqual(
			Array.Empty<string>(),
			unsupportedLayouts,
			"The local bulk data contains layouts the normalizer " +
			"does not recognize." );
	}

	private static void AssertSourceMapping(
		string label,
		ScryfallCardDto source,
		NormalizedCard card )
	{
		Assert.AreEqual(
			Guid.Parse( source.Id ),
			card.Gameplay.ScryfallId,
			$"{label}: id changed." );
		Assert.AreEqual(
			ParseOptionalGuid( source.OracleId ),
			card.Gameplay.OracleId,
			$"{label}: oracle_id changed." );
		Assert.AreEqual(
			source.ManaCost,
			card.Gameplay.SourceManaCost,
			$"{label}: top-level mana_cost changed." );
		Assert.AreEqual(
			source.Cmc,
			card.Gameplay.ManaValue,
			$"{label}: cmc changed." );
		Assert.AreEqual(
			ColorSet.FromScryfall( source.ColorIdentity ),
			card.Gameplay.ColorIdentity,
			$"{label}: color_identity changed." );
		AssertOptionalColors(
			$"{label}: colors",
			source.Colors,
			card.Gameplay.Colors );
		AssertOptionalColors(
			$"{label}: color_indicator",
			source.ColorIndicator,
			card.Gameplay.ColorIndicator );
		Assert.AreEqual(
			source.Defense,
			card.Gameplay.Defense?.Value,
			$"{label}: defense changed." );
		Assert.AreEqual(
			source.HandModifier,
			card.Gameplay.HandModifier?.Value,
			$"{label}: hand_modifier changed." );
		AssertArrayEqual(
			$"{label}: keywords",
			source.Keywords,
			card.Gameplay.Keywords.Values );
		AssertLegalities(
			$"{label}: legalities",
			source.Legalities,
			card.Gameplay.Legalities );
		Assert.AreEqual(
			source.LifeModifier,
			card.Gameplay.LifeModifier?.Value,
			$"{label}: life_modifier changed." );
		Assert.AreEqual(
			source.Loyalty,
			card.Gameplay.Loyalty?.Value,
			$"{label}: loyalty changed." );
		Assert.AreEqual(
			source.Name,
			card.Gameplay.Name,
			$"{label}: name changed." );
		Assert.AreEqual(
			source.OracleText,
			card.Gameplay.OracleText,
			$"{label}: oracle_text changed." );
		Assert.AreEqual(
			source.Power,
			card.Gameplay.Power?.Value,
			$"{label}: power changed." );
		Assert.AreEqual(
			source.Reserved,
			card.Gameplay.Reserved,
			$"{label}: reserved changed." );
		Assert.AreEqual(
			source.Toughness,
			card.Gameplay.Toughness?.Value,
			$"{label}: toughness changed." );
		Assert.AreEqual(
			source.TypeLine,
			card.Gameplay.TypeLine,
			$"{label}: type_line changed." );
		Assert.AreEqual(
			source.EdhrecRank,
			card.Gameplay.EdhrecRank,
			$"{label}: edhrec_rank changed." );
		Assert.AreEqual(
			source.PennyRank,
			card.Gameplay.PennyRank,
			$"{label}: penny_rank changed." );
		Assert.AreEqual(
			source.GameChanger,
			card.Gameplay.GameChanger,
			$"{label}: game_changer changed." );

		AssertFaces( label, source, card.Gameplay.Faces );
		AssertRelatedCards(
			$"{label}: all_parts",
			source.AllParts,
			card.Gameplay.AllParts );

		CardPresentationData presentation = card.Presentation;

		Assert.AreEqual(
			source.Artist,
			presentation.Artist,
			$"{label}: artist changed." );
		AssertArrayEqual(
			$"{label}: artist_ids",
			ParseOptionalGuidArray( source.ArtistIds ),
			presentation.ArtistIds );
		AssertArrayEqual(
			$"{label}: attraction_lights",
			source.AttractionLights,
			presentation.AttractionLights );
		Assert.AreEqual( source.Booster, presentation.Booster );
		Assert.AreEqual(
			source.BorderColor,
			presentation.BorderColor.ToString().ToLowerInvariant(),
			$"{label}: border_color changed." );
		Assert.AreEqual(
			ParseOptionalGuid( source.CardBackId ),
			presentation.CardBack,
			$"{label}: card_back_id changed." );
		Assert.AreEqual(
			source.CollectorNumber,
			presentation.CollectorNumber,
			$"{label}: collector_number changed." );
		Assert.AreEqual(
			source.ContentWarning,
			presentation.ContentWarning );
		Assert.AreEqual( source.Digital, presentation.Digital );
		AssertArrayEqual(
			$"{label}: finishes",
			source.Finishes,
			presentation.Finishes
				.Select( value =>
					value.ToString().ToLowerInvariant() )
				.ToArray() );
		Assert.AreEqual( source.Foil, presentation.Foil );
		Assert.AreEqual( source.Nonfoil, presentation.Nonfoil );
		Assert.AreEqual( source.FlavorName, presentation.FlavorName );
		Assert.AreEqual( source.FlavorText, presentation.FlavorText );
		Assert.IsTrue(
			string.Equals(
				source.Frame,
				presentation.Frame.ToString()["Frame".Length..],
				StringComparison.OrdinalIgnoreCase ),
			$"{label}: frame changed." );
		AssertArrayEqual(
			$"{label}: frame_effects",
			source.FrameEffects,
			presentation.FrameEffects?
				.Select( value =>
					value.ToString().ToLowerInvariant() )
				.ToArray() );
		Assert.AreEqual( source.FullArt, presentation.FullArt );
		AssertArrayEqual(
			$"{label}: games",
			source.Games,
			presentation.Games );
		Assert.AreEqual(
			source.HighresImage,
			presentation.HighResolutionImage );
		Assert.AreEqual(
			ParseOptionalGuid( source.IllustrationId ),
			presentation.IllustrationId );
		AssertImages(
			$"{label}: image_uris",
			source.ImageUris,
			presentation.Images );
		Assert.AreEqual(
			source.ImageStatus,
			presentation.ImageStatus );
		Assert.AreEqual(
			source.ImageUpdatedAt,
			presentation.ImageUpdatedAt );
		Assert.AreEqual( source.Oversized, presentation.Oversized );
		AssertPrices(
			$"{label}: prices",
			source.Prices,
			presentation.Prices );
		Assert.AreEqual( source.PrintedName, presentation.PrintedName );
		Assert.AreEqual( source.PrintedText, presentation.PrintedText );
		Assert.AreEqual(
			source.PrintedTypeLine,
			presentation.PrintedTypeLine );
		Assert.AreEqual( source.Promo, presentation.Promo );
		AssertArrayEqual(
			$"{label}: promo_types",
			source.PromoTypes,
			presentation.PromoTypes );
		Assert.AreEqual(
			source.Rarity,
			presentation.Rarity.ToString().ToLowerInvariant(),
			$"{label}: rarity changed." );
		Assert.AreEqual( source.ReleasedAt, presentation.ReleasedAt );
		Assert.AreEqual( source.Reprint, presentation.Reprint );
		Assert.AreEqual(
			source.SecurityStamp,
			presentation.SecurityStamp );
		Assert.AreEqual(
			source.StorySpotlight,
			presentation.StorySpotlight );
		Assert.AreEqual( source.Textless, presentation.Textless );
		Assert.AreEqual( source.Variation, presentation.Variation );
		Assert.AreEqual(
			ParseOptionalGuid( source.VariationOf ),
			presentation.VariationOf );
		Assert.AreEqual( source.Watermark, presentation.Watermark );
		AssertPreview(
			$"{label}: preview",
			source.Preview,
			presentation.Preview );

		Assert.AreEqual( source.ArenaId, card.Identifiers.ArenaId );
		Assert.AreEqual( source.MtgoId, card.Identifiers.MtgoId );
		Assert.AreEqual(
			source.MtgoFoilId,
			card.Identifiers.MtgoFoilId );
		AssertArrayEqual(
			$"{label}: multiverse_ids",
			source.MultiverseIds,
			card.Identifiers.MultiverseIds );
		Assert.AreEqual(
			source.TcgplayerId,
			card.Identifiers.TcgplayerId );
		Assert.AreEqual(
			source.TcgplayerEtchedId,
			card.Identifiers.TcgplayerEtchedId );
		Assert.AreEqual(
			source.CardmarketId,
			card.Identifiers.CardmarketId );
		Assert.AreEqual(
			source.ResourceId,
			card.Identifiers.ResourceId );

		Assert.AreEqual( Guid.Parse( source.SetId ), card.Set.Id );
		Assert.AreEqual( source.SetCode, card.Set.Code );
		Assert.AreEqual( source.SetName, card.Set.Name );
		Assert.AreEqual( source.SetType, card.Set.Type );
		Assert.AreEqual( source.SetUri, card.Set.ApiUri );
		Assert.AreEqual( source.SetSearchUri, card.Set.SearchUri );
		Assert.AreEqual(
			source.ScryfallSetUri,
			card.Set.ScryfallUri );

		Assert.AreEqual( source.Uri, card.Links.ApiUri );
		Assert.AreEqual(
			source.ScryfallUri,
			card.Links.ScryfallUri );
		Assert.AreEqual(
			source.PrintsSearchUri,
			card.Links.PrintsSearchUri );
		Assert.AreEqual(
			source.RulingsUri,
			card.Links.RulingsUri );
		AssertDictionaryEqual(
			$"{label}: purchase_uris",
			source.PurchaseUris,
			card.Links.PurchaseUris );
		AssertDictionaryEqual(
			$"{label}: related_uris",
			source.RelatedUris,
			card.Links.RelatedUris );

		Assert.AreEqual( source.Object, card.Source.Object );
		Assert.AreEqual( source.Lang, card.Source.Language );
		AssertExtensions(
			$"{label}: root extensions",
			source.AdditionalFields,
			card.Source.Extensions );
	}

	private static void AssertFaces(
		string label,
		ScryfallCardDto source,
		CardFace[] actual )
	{
		if ( source.CardFaces is null )
		{
			Assert.AreEqual( 1, actual.Length );
			CardFace face = actual[0];

			Assert.AreEqual( "card_face", face.Object );
			Assert.AreEqual( source.Name, face.Name );
			Assert.AreEqual( source.ManaCost, face.SourceManaCost );
			Assert.AreEqual( source.TypeLine, face.TypeLine );
			Assert.AreEqual( source.OracleText, face.OracleText );
			AssertOptionalColors(
				$"{label}: synthesized face colors",
				source.Colors,
				face.Colors );
			AssertOptionalColors(
				$"{label}: synthesized face color_indicator",
				source.ColorIndicator,
				face.ColorIndicator );
			Assert.AreEqual( source.Power, face.Power?.Value );
			Assert.AreEqual( source.Toughness, face.Toughness?.Value );
			Assert.AreEqual( source.Loyalty, face.Loyalty?.Value );
			Assert.AreEqual( source.Defense, face.Defense?.Value );
			Assert.AreEqual( source.Artist, face.Artist );
			Assert.AreEqual(
				ParseOnlyGuid( source.ArtistIds ),
				face.ArtistId );
			Assert.AreEqual(
				ParseOptionalGuid( source.IllustrationId ),
				face.IllustrationId );
			AssertImages(
				$"{label}: synthesized face images",
				source.ImageUris,
				face.Images );
			Assert.AreEqual( source.FlavorName, face.FlavorName );
			Assert.AreEqual( source.FlavorText, face.FlavorText );
			Assert.AreEqual( source.PrintedName, face.PrintedName );
			Assert.AreEqual( source.PrintedText, face.PrintedText );
			Assert.AreEqual(
				source.PrintedTypeLine,
				face.PrintedTypeLine );
			Assert.AreEqual(
				ParseOptionalGuid( source.OracleId ),
				face.OracleId );
			Assert.AreEqual( source.Watermark, face.Watermark );
			return;
		}

		Assert.AreEqual( source.CardFaces.Length, actual.Length );

		for ( var index = 0; index < source.CardFaces.Length; index++ )
		{
			ScryfallCardFaceDto expected = source.CardFaces[index];
			CardFace face = actual[index];
			string prefix = $"{label}: face {index}";

			Assert.AreEqual( expected.Object, face.Object, prefix );
			Assert.AreEqual( expected.Name, face.Name, prefix );
			Assert.AreEqual(
				expected.ManaCost,
				face.SourceManaCost,
				prefix );
			Assert.AreEqual( expected.TypeLine, face.TypeLine, prefix );
			Assert.AreEqual(
				expected.OracleText,
				face.OracleText,
				prefix );
			AssertOptionalColors(
				$"{prefix} colors",
				expected.Colors,
				face.Colors );
			AssertOptionalColors(
				$"{prefix} color_indicator",
				expected.ColorIndicator,
				face.ColorIndicator );
			Assert.AreEqual( expected.Power, face.Power?.Value, prefix );
			Assert.AreEqual(
				expected.Toughness,
				face.Toughness?.Value,
				prefix );
			Assert.AreEqual(
				expected.Loyalty,
				face.Loyalty?.Value,
				prefix );
			Assert.AreEqual(
				expected.Defense,
				face.Defense?.Value,
				prefix );
			Assert.AreEqual( expected.Artist, face.Artist, prefix );
			Assert.AreEqual(
				ParseOptionalGuid( expected.ArtistId ),
				face.ArtistId,
				prefix );
			Assert.AreEqual(
				ParseOptionalGuid( expected.IllustrationId ),
				face.IllustrationId,
				prefix );
			AssertImages(
				$"{prefix} image_uris",
				expected.ImageUris,
				face.Images );
			Assert.AreEqual(
				expected.FlavorName,
				face.FlavorName,
				prefix );
			Assert.AreEqual(
				expected.FlavorText,
				face.FlavorText,
				prefix );
			Assert.AreEqual(
				expected.PrintedName,
				face.PrintedName,
				prefix );
			Assert.AreEqual(
				expected.PrintedText,
				face.PrintedText,
				prefix );
			Assert.AreEqual(
				expected.PrintedTypeLine,
				face.PrintedTypeLine,
				prefix );
			Assert.AreEqual(
				ParseOptionalGuid( expected.OracleId ),
				face.OracleId,
				prefix );
			Assert.AreEqual( expected.Layout, face.Layout, prefix );
			Assert.AreEqual( expected.Cmc, face.ManaValue, prefix );
			Assert.AreEqual(
				expected.Watermark,
				face.Watermark,
				prefix );
			AssertExtensions(
				$"{prefix} extensions",
				expected.AdditionalFields,
				face.SourceExtensions );
		}
	}

	private static void AssertRelatedCards(
		string label,
		ScryfallRelatedCardDto[]? expected,
		RelatedCard[]? actual )
	{
		if ( expected is null )
		{
			Assert.IsNull( actual, $"{label}: null state changed." );
			return;
		}

		Assert.IsNotNull( actual, $"{label}: became null." );
		Assert.AreEqual( expected.Length, actual.Length, label );

		for ( var index = 0; index < expected.Length; index++ )
		{
			ScryfallRelatedCardDto source = expected[index];
			RelatedCard stored = actual[index];
			string prefix = $"{label}[{index}]";

			Assert.AreEqual( Guid.Parse( source.Id ), stored.Id, prefix );
			Assert.AreEqual( source.Object, stored.Object, prefix );
			Assert.AreEqual( source.Component, stored.Component, prefix );
			Assert.AreEqual( source.Name, stored.Name, prefix );
			Assert.AreEqual( source.TypeLine, stored.TypeLine, prefix );
			Assert.AreEqual( source.Uri, stored.ApiUri, prefix );
			AssertExtensions(
				$"{prefix} extensions",
				source.AdditionalFields,
				stored.SourceExtensions );
		}
	}

	private static void AssertImages(
		string label,
		ScryfallImageUrisDto? expected,
		CardImages? actual )
	{
		if ( expected is null )
		{
			Assert.IsNull( actual, $"{label}: null state changed." );
			return;
		}

		Assert.IsNotNull( actual, $"{label}: became null." );
		Assert.AreEqual( expected.Small, actual.Small, label );
		Assert.AreEqual( expected.Normal, actual.Normal, label );
		Assert.AreEqual( expected.Large, actual.Large, label );
		Assert.AreEqual( expected.Png, actual.Png, label );
		Assert.AreEqual( expected.ArtCrop, actual.ArtCrop, label );
		Assert.AreEqual( expected.BorderCrop, actual.BorderCrop, label );
		Assert.AreEqual( expected.Thumb, actual.Thumb, label );
		Assert.AreEqual( expected.Grid, actual.Grid, label );
		Assert.AreEqual( expected.Display, actual.Display, label );
		Assert.AreEqual( expected.Art, actual.Art, label );
		Assert.AreEqual( expected.Crop, actual.Crop, label );
		AssertExtensions(
			$"{label} extensions",
			expected.AdditionalFields,
			actual.SourceExtensions );
	}

	private static void AssertPrices(
		string label,
		ScryfallPricesDto expected,
		CardPrices actual )
	{
		Assert.AreEqual( expected.Usd, actual.Usd, label );
		Assert.AreEqual( expected.UsdFoil, actual.UsdFoil, label );
		Assert.AreEqual( expected.UsdEtched, actual.UsdEtched, label );
		Assert.AreEqual( expected.Eur, actual.Eur, label );
		Assert.AreEqual( expected.EurFoil, actual.EurFoil, label );
		Assert.AreEqual( expected.EurEtched, actual.EurEtched, label );
		Assert.AreEqual( expected.Tix, actual.Tix, label );
		AssertExtensions(
			$"{label} extensions",
			expected.AdditionalFields,
			actual.SourceExtensions );
	}

	private static void AssertPreview(
		string label,
		ScryfallPreviewDto? expected,
		CardPreview? actual )
	{
		if ( expected is null )
		{
			Assert.IsNull( actual, $"{label}: null state changed." );
			return;
		}

		Assert.IsNotNull( actual, $"{label}: became null." );
		Assert.AreEqual(
			expected.PreviewedAt,
			actual.PreviewedAt,
			label );
		Assert.AreEqual( expected.SourceUri, actual.SourceUri, label );
		Assert.AreEqual( expected.Source, actual.Source, label );
		AssertExtensions(
			$"{label} extensions",
			expected.AdditionalFields,
			actual.SourceExtensions );
	}

	private static void AssertLegalities(
		string label,
		Dictionary<string, string> expected,
		FormatLegalities actual )
	{
		Assert.AreEqual(
			expected.Count,
			actual.ByFormat.Count,
			$"{label}: format count changed." );

		foreach (
			KeyValuePair<string, string> pair in expected
		)
		{
			Assert.IsTrue(
				actual.ByFormat.TryGetValue(
					pair.Key,
					out CardLegality legality ),
				$"{label}: format '{pair.Key}' is missing." );

			string stored = legality == CardLegality.NotLegal
				? "not_legal"
				: legality.ToString().ToLowerInvariant();

			Assert.AreEqual(
				pair.Value,
				stored,
				$"{label}: format '{pair.Key}' changed." );
		}
	}

	private static void AssertOptionalColors(
		string label,
		string[]? expected,
		ColorSet? actual )
	{
		if ( expected is null )
		{
			Assert.IsFalse(
				actual.HasValue,
				$"{label}: null state changed." );
			return;
		}

		Assert.IsTrue( actual.HasValue, $"{label}: became null." );
		Assert.AreEqual(
			ColorSet.FromScryfall( expected ),
			actual.Value,
			label );
	}

	private static void AssertArrayEqual<T>(
		string label,
		T[]? expected,
		T[]? actual )
	{
		if ( expected is null )
		{
			Assert.IsNull( actual, $"{label}: null state changed." );
			return;
		}

		Assert.IsNotNull( actual, $"{label}: became null." );
		CollectionAssert.AreEqual( expected, actual, label );
	}

	private static void AssertDictionaryEqual(
		string label,
		Dictionary<string, string>? expected,
		Dictionary<string, string>? actual )
	{
		if ( expected is null )
		{
			Assert.IsNull( actual, $"{label}: null state changed." );
			return;
		}

		Assert.IsNotNull( actual, $"{label}: became null." );
		Assert.AreEqual( expected.Count, actual.Count, label );

		foreach ( KeyValuePair<string, string> pair in expected )
		{
			Assert.IsTrue(
				actual.TryGetValue( pair.Key, out string? value ),
				$"{label}: key '{pair.Key}' is missing." );
			Assert.AreEqual(
				pair.Value,
				value,
				$"{label}: value for '{pair.Key}' changed." );
		}
	}

	private static void AssertExtensions(
		string label,
		Dictionary<string, JsonElement> expected,
		Dictionary<string, JsonElement> actual )
	{
		Assert.AreEqual( expected.Count, actual.Count, label );

		foreach (
			KeyValuePair<string, JsonElement> pair in expected
		)
		{
			Assert.IsTrue(
				actual.TryGetValue(
					pair.Key,
					out JsonElement stored ),
				$"{label}: field '{pair.Key}' is missing." );
			Assert.AreEqual(
				JsonSerializer.Serialize( pair.Value ),
				JsonSerializer.Serialize( stored ),
				$"{label}: field '{pair.Key}' changed." );
		}
	}

	private static Guid? ParseOptionalGuid( string? value )
	{
		return string.IsNullOrWhiteSpace( value )
			? null
			: Guid.Parse( value );
	}

	private static Guid[]? ParseOptionalGuidArray(
		string[]? values )
	{
		return values?.Select( Guid.Parse ).ToArray();
	}

	private static Guid? ParseOnlyGuid( string[]? values )
	{
		return values is { Length: 1 }
			? Guid.Parse( values[0] )
			: null;
	}

	private static Stream OpenPossiblyGzipped( FileStream input )
	{
		int first = input.ReadByte();
		int second = input.ReadByte();
		input.Position = 0;

		return first == 0x1F && second == 0x8B
			? new GZipStream(
				input,
				CompressionMode.Decompress,
				leaveOpen: false )
			: input;
	}

	private static void AssertNormalizedCard(
		string label,
		ExpectedResult expected,
		NormalizedCard card )
	{
		Assert.IsNotNull( card.Gameplay, $"{label}: missing gameplay." );
		Assert.AreEqual(
			expected.Layout,
			card.Gameplay.Layout.ToString(),
			$"{label}: layout mismatch." );
		Assert.IsNotNull(
			card.Gameplay.Faces,
			$"{label}: faces are null." );
		Assert.AreNotEqual(
			0,
			card.Gameplay.Faces.Length,
			$"{label}: faces are empty." );

		string[] actualCosts = card.Gameplay.Faces
			.Select( face =>
			{
				Assert.IsNotNull(
					face,
					$"{label}: a face is null." );
				Assert.IsNotNull(
					face.ManaCost,
					$"{label}: a face mana cost is null." );

				return face.ManaCost.ToString();
			})
			.ToArray();

		CollectionAssert.AreEqual(
			expected.FaceManaCosts,
			actualCosts,
			$"{label}: per-face costs changed." );

		switch ( expected.ProducedManaState )
		{
			case "null":
				Assert.IsFalse(
					card.Gameplay.ProducedMana.HasValue,
					$"{label}: produced_mana should be null." );
				break;

			case "empty":
				Assert.IsTrue(
					card.Gameplay.ProducedMana.HasValue,
					$"{label}: produced_mana should be present." );
				Assert.IsTrue(
					card.Gameplay.ProducedMana.Value.IsEmpty,
					$"{label}: produced_mana should be explicitly empty." );
				break;

			case "values":
				Assert.IsTrue(
					card.Gameplay.ProducedMana.HasValue,
					$"{label}: produced_mana should be present." );

				ProducedManaSet expectedMana =
					ProducedManaSet.FromScryfall(
						expected.ProducedMana ?? [] );

				Assert.AreEqual(
					expectedMana,
					card.Gameplay.ProducedMana.Value,
					$"{label}: produced_mana values changed." );
				break;

			default:
				Assert.Fail(
					$"{label}: unknown produced_mana state " +
					$"'{expected.ProducedManaState}'." );
				break;
		}
	}

	private static void AssertCostPatternCoverage(
		FixtureSuite suite )
	{
		string[] costs = suite.Cases
			.Where( fixture => fixture.Kind == "valid" )
			.SelectMany( fixture =>
				fixture.Expected?.FaceManaCosts ??
				Array.Empty<string>() )
			.ToArray();

		Assert.IsTrue( costs.Contains( "", StringComparer.Ordinal ) );
		Assert.IsTrue( costs.Any( value => value.Contains( "{X}" ) ) );
		Assert.IsTrue( costs.Any( value => value.Contains( "/P}" ) ) );
		Assert.IsTrue(
			costs.Any( value =>
				value.Contains( "/W}" ) ||
				value.Contains( "/U}" ) ||
				value.Contains( "/B}" ) ||
				value.Contains( "/R}" ) ||
				value.Contains( "/G}" ) ) );
		Assert.IsTrue( costs.Any( value => value.Contains( "{0}" ) ) );
		Assert.IsTrue( costs.Any( value => value.Contains( "{D}" ) ) );

		Assert.IsTrue(
			suite.Cases
				.Where( fixture => fixture.Kind == "valid" )
				.Any( fixture =>
					fixture.Card.TryGetProperty(
						"mana_cost",
						out JsonElement manaCost ) &&
					manaCost.ValueKind == JsonValueKind.String &&
					manaCost.GetString()?.Contains(
						"//",
						StringComparison.Ordinal ) == true ),
			"The suite must include combined top-level face costs." );
	}

	private static void AssertUniqueValidScryfallIds(
		FixtureSuite suite )
	{
		string[] duplicates = suite.Cases
			.Where( fixture => fixture.Kind == "valid" )
			.Select( DeserializeCard )
			.GroupBy(
				dto => dto.Id,
				StringComparer.OrdinalIgnoreCase )
			.Where( group => group.Count() > 1 )
			.Select( group => group.Key )
			.ToArray();

		CollectionAssert.AreEqual(
			Array.Empty<string>(),
			duplicates,
			"Valid fixtures must not create duplicate database IDs." );
	}

	private static ScryfallCardDto DeserializeCard(
		FixtureCase fixture )
	{
		return JsonSerializer.Deserialize<ScryfallCardDto>(
			fixture.Card.GetRawText(),
			JsonOptions )
			?? throw new InvalidDataException(
				$"{fixture.Id}: Scryfall DTO deserialized to null." );
	}

	private static FixtureCase GetCase( string fixtureId )
	{
		return LoadSuite().Cases.Single(
			fixture => string.Equals(
				fixture.Id,
				fixtureId,
				StringComparison.Ordinal ) );
	}

	private static FixtureSuite LoadSuite()
	{
		string path = GetFixturePath();

		Assert.IsTrue(
			File.Exists( path ),
			$"Fixture suite was not found at '{path}'." );

		return JsonSerializer.Deserialize<FixtureSuite>(
			File.ReadAllText( path ),
			JsonOptions )
			?? throw new InvalidDataException(
				"Fixture suite deserialized to null." );
	}

	private static string GetFixturePath(
		[CallerFilePath] string sourceFilePath = "" )
	{
		string unitTestDirectory =
			Path.GetDirectoryName( sourceFilePath )
			?? throw new InvalidDataException(
				"Could not resolve the UnitTests directory." );

		return Path.GetFullPath(
			Path.Combine(
				unitTestDirectory,
				"..",
				"code",
				"TestData",
				"TestCards.json" ) );
	}

	private sealed class FixtureSuite
	{
		[JsonPropertyName( "schema_version" )]
		public int SchemaVersion { get; set; }

		[JsonPropertyName( "cases" )]
		public FixtureCase[] Cases { get; set; } = [];
	}

	private sealed class FixtureCase
	{
		[JsonPropertyName( "id" )]
		public string Id { get; set; } = "";

		[JsonPropertyName( "kind" )]
		public string Kind { get; set; } = "";

		[JsonPropertyName( "expected" )]
		public ExpectedResult? Expected { get; set; }

		[JsonPropertyName( "expected_error_contains" )]
		public string? ExpectedErrorContains { get; set; }

		[JsonPropertyName( "card" )]
		public JsonElement Card { get; set; }
	}

	private sealed class ExpectedResult
	{
		[JsonPropertyName( "layout" )]
		public string Layout { get; set; } = "";

		[JsonPropertyName( "face_mana_costs" )]
		public string[] FaceManaCosts { get; set; } = [];

		[JsonPropertyName( "produced_mana_state" )]
		public string ProducedManaState { get; set; } = "";

		[JsonPropertyName( "produced_mana" )]
		public string[]? ProducedMana { get; set; }
	}
}
