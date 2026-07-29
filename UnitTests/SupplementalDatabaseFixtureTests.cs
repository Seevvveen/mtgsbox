#nullable enable

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Classes.CardDatabase;
using Sandbox.Classes.Database;
using Sandbox.Classes.Database.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace Sandbox.UnitTests;

[TestClass]
public sealed class SupplementalDatabaseFixtureTests
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	[TestMethod]
	public void SetAndRuling_NormalizeWithExtensionData()
	{
		using JsonDocument extensionDocument =
			JsonDocument.Parse( """{"future":true}""" );
		JsonElement extension =
			extensionDocument.RootElement.Clone();

		var setSource = new ScryfallSetDto
		{
			Object = "set",
			Id = "30000000-0000-4000-8000-000000000001",
			Code = "test",
			MtgoCode = "tst",
			ArenaCode = "tst",
			TcgplayerId = 17,
			Name = "Synthetic Test Set",
			SetType = "funny",
			ReleasedAt = new DateTime( 2000, 1, 1 ),
			BlockCode = "blk",
			Block = "Test Block",
			ParentSetCode = "parent",
			CardCount = 42,
			PrintedSize = 50,
			Digital = false,
			FoilOnly = false,
			NonfoilOnly = true,
			ScryfallUri = "https://scryfall.com/sets/test",
			Uri = "https://api.scryfall.com/sets/test",
			IconSvgUri =
				"https://svgs.scryfall.io/sets/test.svg",
			SearchUri =
				"https://api.scryfall.com/cards/search?q=e:test",
			AdditionalFields =
			{
				["future_set_field"] = extension
			}
		};

		CardSetDefinition set =
			ScryfallSupplementalNormalizer.NormalizeSet( setSource );
		Assert.AreEqual( setSource.Id, set.Id.ToString() );
		Assert.AreEqual( setSource.Code, set.Code );
		Assert.IsTrue(
			set.SourceExtensions.ContainsKey( "future_set_field" ) );

		var rulingSource = new ScryfallRulingDto
		{
			Object = "ruling",
			OracleId =
				"40000000-0000-4000-8000-000000000001",
			Source = "scryfall",
			PublishedAt = new DateTime( 2026, 7, 29 ),
			Comment = "Synthetic ruling.",
			AdditionalFields =
			{
				["future_ruling_field"] = extension
			}
		};

		CardRuling ruling =
			ScryfallSupplementalNormalizer.NormalizeRuling(
				rulingSource );
		Assert.AreEqual(
			rulingSource.OracleId,
			ruling.OracleId.ToString() );
		Assert.AreEqual( rulingSource.Comment, ruling.Comment );
		Assert.IsTrue(
			ruling.SourceExtensions.ContainsKey(
				"future_ruling_field" ) );

		var file = new CardSetDefinitionFile
		{
			FormatVersion = 3,
			SetCount = 1,
			Sets = [set]
		};

		CardSetDefinitionFile? roundTripped =
			JsonSerializer.Deserialize<CardSetDefinitionFile>(
				JsonSerializer.SerializeToUtf8Bytes(
					file,
					JsonOptions ),
				JsonOptions );

		Assert.IsNotNull( roundTripped );
		Assert.AreEqual(
			JsonSerializer.Serialize( set, JsonOptions ),
			JsonSerializer.Serialize(
				roundTripped.Sets[0],
				JsonOptions ) );
	}

	[TestMethod]
	[TestCategory( "Integration" )]
	public void LocalSetAndRulingSources_AllObjectsNormalize()
	{
		string dataDirectory = GetLocalDataDirectory();
		string setsPath = Path.Combine( dataDirectory, "sets.json" );
		string rulingsPath =
			Path.Combine( dataDirectory, "rulings.json" );

		if ( !File.Exists( setsPath ) || !File.Exists( rulingsPath ) )
		{
			Assert.Inconclusive(
				"Local sets.json and rulings.json are required for " +
				"this integration test." );
			return;
		}

		ScryfallListDto<ScryfallSetDto>? setResponse =
			JsonSerializer.Deserialize<
				ScryfallListDto<ScryfallSetDto>>(
					File.ReadAllBytes( setsPath ),
					JsonOptions );

		Assert.IsNotNull( setResponse );
		Assert.AreEqual( "list", setResponse.Object );
		Assert.IsFalse( setResponse.HasMore );
		Assert.IsTrue( setResponse.Data.Length > 500 );

		var setIds = new HashSet<Guid>();
		var setCodes = new HashSet<string>(
			StringComparer.OrdinalIgnoreCase );

		foreach ( ScryfallSetDto source in setResponse.Data )
		{
			CardSetDefinition set =
				ScryfallSupplementalNormalizer.NormalizeSet( source );
			Assert.AreEqual( "set", set.Object );
			Assert.IsTrue( setIds.Add( set.Id ) );
			Assert.IsTrue( setCodes.Add( set.Code ) );
		}

		using FileStream file = File.OpenRead( rulingsPath );
		using Stream decoded = OpenPossiblyGzipped( file );
		using StreamReader reader = new( decoded );

		var rulingCount = 0;
		var oracleIds = new HashSet<Guid>();

		while ( reader.ReadLine() is { } line )
		{
			if ( string.IsNullOrWhiteSpace( line ) )
				continue;

			ScryfallRulingDto? source =
				JsonSerializer.Deserialize<ScryfallRulingDto>(
					line,
					JsonOptions );
			Assert.IsNotNull( source );

			CardRuling ruling =
				ScryfallSupplementalNormalizer.NormalizeRuling(
					source );
			Assert.AreEqual( "ruling", ruling.Object );

			byte[] databaseJson =
				JsonSerializer.SerializeToUtf8Bytes(
					ruling,
					JsonOptions );
			CardRuling? roundTripped =
				JsonSerializer.Deserialize<CardRuling>(
					databaseJson,
					JsonOptions );

			Assert.IsNotNull( roundTripped );
			Assert.AreEqual(
				JsonSerializer.Serialize( ruling, JsonOptions ),
				JsonSerializer.Serialize(
					roundTripped,
					JsonOptions ) );

			oracleIds.Add( ruling.OracleId );
			rulingCount++;
		}

		Assert.IsTrue( rulingCount > 10_000 );
		Assert.IsTrue( oracleIds.Count > 1_000 );
	}

	[TestMethod]
	[TestCategory( "Integration" )]
	public void LocalSymbology_AllFieldsNormalizeAndJsonRoundTrip()
	{
		string path = Path.Combine(
			GetLocalDataDirectory(),
			"symbology.json" );

		if ( !File.Exists( path ) )
		{
			Assert.Inconclusive(
				"Local symbology.json is unavailable." );
			return;
		}

		ScryfallSymbologyDto? response =
			JsonSerializer.Deserialize<ScryfallSymbologyDto>(
				File.ReadAllBytes( path ),
				JsonOptions );

		Assert.IsNotNull( response );
		Assert.AreEqual( "list", response.Object );
		Assert.IsFalse( response.HasMore );
		Assert.IsTrue( response.Data.Length > 50 );

		foreach ( ScryfallCardSymbolDto source in response.Data )
		{
			CardSymbolDefinition stored =
				ScryfallSymbolNormalizer.Normalize( source );

			Assert.AreEqual( source.Object, stored.Object );
			Assert.AreEqual( source.Symbol, stored.Id.ToString() );
			Assert.AreEqual(
				source.LooseVariant,
				stored.LooseVariant );
			Assert.AreEqual( source.SvgUri, stored.SvgUri );
			Assert.AreEqual( source.English, stored.English );
			Assert.AreEqual(
				source.Transposable,
				stored.Transposable );
			Assert.AreEqual(
				source.RepresentsMana,
				stored.RepresentsMana );
			Assert.AreEqual(
				source.AppearsInManaCosts,
				stored.AppearsInManaCosts );
			Assert.AreEqual( source.Hybrid, stored.Hybrid );
			Assert.AreEqual( source.Phyrexian, stored.Phyrexian );
			Assert.AreEqual( source.Funny, stored.Funny );
			Assert.AreEqual(
				source.ManaValue,
				stored.ManaValue );
			Assert.AreEqual(
				source.Cmc,
				stored.ConvertedManaCost );
			CollectionAssert.AreEqual(
				source.Colors,
				stored.Colors.ToScryfallArray() );
			AssertArraysEqual(
				source.GathererAlternates,
				stored.GathererAlternates );
			Assert.AreEqual(
				source.AdditionalFields.Count,
				stored.SourceExtensions.Count );

			string json = JsonSerializer.Serialize(
				stored,
				JsonOptions );
			CardSymbolDefinition? roundTripped =
				JsonSerializer.Deserialize<CardSymbolDefinition>(
					json,
					JsonOptions );

			Assert.IsNotNull( roundTripped );
			Assert.AreEqual(
				json,
				JsonSerializer.Serialize(
					roundTripped,
					JsonOptions ) );
		}
	}

	[TestMethod]
	[TestCategory( "Integration" )]
	[DoNotParallelize]
	public void LocalSources_BuildAndOpenCompleteDatabase()
	{
		string dataDirectory = GetLocalDataDirectory();

		foreach (
			string fileName in new[]
			{
				"oracle-cards.json",
				"rulings.json",
				"sets.json",
				"symbology.json"
			}
		)
		{
			if ( !File.Exists(
				Path.Combine( dataDirectory, fileName ) ) )
			{
				Assert.Inconclusive(
					$"Local source '{fileName}' is unavailable." );
				return;
			}
		}

		if ( FileSystem.Data is null )
		{
			Assert.Inconclusive(
				"s&box FileSystem.Data is not initialized by this CLI " +
				"test host. Run this test inside the s&box editor." );
			return;
		}

		DatabaseBuilder.BuildDatabase();

		try
		{
			CardDatabase.Initialize();

			Assert.IsTrue( CardDatabase.IndexedCardCount > 30_000 );
			Assert.IsTrue( CardDatabase.SymbolDefinitionCount > 50 );
			Assert.IsTrue( CardDatabase.SetDefinitionCount > 500 );
			Assert.IsTrue( CardDatabase.RulingCount > 10_000 );

			Guid scryfallId = Guid.Parse(
				"a471b306-4941-4e46-a0cb-d92895c16f8a" );
			NormalizedCard? card =
				CardDatabase.GetCard( scryfallId );

			Assert.IsNotNull( card );
			Assert.AreEqual(
				"Nissa, Worldsoul Speaker",
				card.Gameplay.Name );
			Assert.IsNotNull(
				CardDatabase.GetSetDefinition( card.Set.Id ) );
			Assert.IsNotNull(
				CardDatabase.GetSetDefinition( card.Set.Code ) );
			Assert.IsTrue( card.Gameplay.OracleId.HasValue );
			Assert.IsTrue(
				CardDatabase.GetRulings(
					card.Gameplay.OracleId.Value ).Length > 0 );
		}
		finally
		{
			CardDatabase.Shutdown();
		}
	}

	private static string GetLocalDataDirectory()
	{
		return Path.Combine(
			Environment.GetFolderPath(
				Environment.SpecialFolder.ProgramFilesX86 ),
			"Steam",
			"steamapps",
			"common",
			"sbox",
			"data",
			"magikarp",
			"mtgsbox#local" );
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

	private static void AssertArraysEqual<T>(
		T[]? expected,
		T[]? actual )
	{
		if ( expected is null )
		{
			Assert.IsNull( actual );
			return;
		}

		Assert.IsNotNull( actual );
		CollectionAssert.AreEqual( expected, actual );
	}
}
