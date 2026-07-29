using Sandbox.Classes.CardDatabase;
#nullable enable

using Sandbox.Classes.Cards.ManaSymbols;
using Sandbox.Classes.Database.Types;
using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
namespace Sandbox.Classes.Database;

/// <summary>
/// Reads Scryfall's JSONL bulk data one card at a time, normalizes each card,
/// and writes the resulting database files to disk.
/// </summary>
public static class DatabaseBuilder
{
	public static void BuildDatabase()
	{
		List<CardSymbolDefinition> symbolDefinitions =
			ReadSymbolDefinitions();
		List<CardSetDefinition> setDefinitions =
			ReadSetDefinitions();
		List<CardRuling> rulings = ReadRulings();

		using Stream input =
			FileSystem.Data.OpenRead( DatabaseFileInfo.SourceFile );

		using Stream outputData =
			FileSystem.Data.OpenWrite( DatabaseFileInfo.CardDataFile );

		PrepareOutputStream(
			outputData,
			DatabaseFileInfo.CardDataFile
		);

		List<CardIndexEntry> indexEntries = [];
		List<CardIdMapping> idMappings = [];

		ReadJsonLines<ScryfallCardDto>(
			input,
			(dto, _) =>
			{
				NormalizedCard card =
					ScryfallCardNormalizer.Normalize( dto );

				int recordId = indexEntries.Count;

				CardIndexEntry entry =
					WriteRecord( outputData, card );

				indexEntries.Add( entry );

				idMappings.Add( new CardIdMapping
				{
					ScryfallId = card.Gameplay.ScryfallId,
					RecordId = recordId
				});
			},
			DatabaseFileInfo.ImportJsonOptions
		);

		outputData.Flush();

		CardIndexFile indexFile = new()
		{
			FormatVersion = DatabaseFileInfo.CurrentFormatVersion,
			CardCount = indexEntries.Count,
			Cards = indexEntries,
			IdMappings = idMappings
		};

		WriteIndexFile( indexFile );
		WriteSymbolDefinitionsFile( symbolDefinitions );
		WriteSetDefinitionsFile( setDefinitions );
		WriteRulingsFile( rulings );

		Log.Info(
			$"Database build complete. Stored {indexEntries.Count:N0} cards " +
			$"{setDefinitions.Count:N0} sets, {rulings.Count:N0} rulings, " +
			$"and {symbolDefinitions.Count:N0} symbol definitions."
		);
	}

	private static List<CardSetDefinition> ReadSetDefinitions()
	{
		using Stream input =
			FileSystem.Data.OpenRead( DatabaseFileInfo.SetSourceFile );

		ScryfallListDto<ScryfallSetDto>? response =
			JsonSerializer.Deserialize<
				ScryfallListDto<ScryfallSetDto>>(
					input,
					DatabaseFileInfo.ImportJsonOptions );

		if ( response is null )
		{
			throw new InvalidDataException(
				"Could not deserialize the Scryfall sets response." );
		}

		if ( !string.Equals(
			response.Object,
			"list",
			StringComparison.Ordinal ) )
		{
			throw new InvalidDataException(
				$"Expected a Scryfall list response for sets, but " +
				$"received '{response.Object}'." );
		}

		if ( response.HasMore )
		{
			throw new InvalidDataException(
				"The Scryfall sets response unexpectedly has another " +
				"page." );
		}

		if ( response.Data is not { Length: > 0 } sourceSets )
		{
			throw new InvalidDataException(
				"The Scryfall sets response contains no definitions." );
		}

		var definitions =
			new List<CardSetDefinition>( sourceSets.Length );
		var ids = new HashSet<Guid>();
		var codes = new HashSet<string>(
			StringComparer.OrdinalIgnoreCase );

		for ( var index = 0; index < sourceSets.Length; index++ )
		{
			CardSetDefinition definition =
				ScryfallSupplementalNormalizer.NormalizeSet(
					sourceSets[index] );

			if ( !string.Equals(
				definition.Object,
				"set",
				StringComparison.Ordinal ) )
			{
				throw new InvalidDataException(
					$"Set at data index {index} has object type " +
					$"'{definition.Object}'." );
			}

			if ( !ids.Add( definition.Id ) )
			{
				throw new InvalidDataException(
					$"Duplicate Scryfall set ID '{definition.Id}'." );
			}

			if ( !codes.Add( definition.Code ) )
			{
				throw new InvalidDataException(
					$"Duplicate Scryfall set code '{definition.Code}'." );
			}

			definitions.Add( definition );
		}

		return definitions;
	}

	private static List<CardRuling> ReadRulings()
	{
		using Stream input =
			FileSystem.Data.OpenRead(
				DatabaseFileInfo.RulingsSourceFile );

		var rulings = new List<CardRuling>();

		ReadJsonLines<ScryfallRulingDto>(
			input,
			(dto, index) =>
			{
				CardRuling ruling =
					ScryfallSupplementalNormalizer.NormalizeRuling( dto );

				if ( !string.Equals(
					ruling.Object,
					"ruling",
					StringComparison.Ordinal ) )
				{
					throw new InvalidDataException(
						$"Ruling at object index {index} has object " +
						$"type '{ruling.Object}'." );
				}

				rulings.Add( ruling );
			},
			DatabaseFileInfo.ImportJsonOptions );

		if ( rulings.Count == 0 )
		{
			throw new InvalidDataException(
				"The Scryfall rulings bulk file contains no rulings." );
		}

		return rulings;
	}

	private static List<CardSymbolDefinition> ReadSymbolDefinitions()
	{
		using Stream input =
			FileSystem.Data.OpenRead( DatabaseFileInfo.SymbolSourceFile );

		ScryfallSymbologyDto? response =
			JsonSerializer.Deserialize<ScryfallSymbologyDto>(
				input,
				DatabaseFileInfo.ImportJsonOptions );

		if ( response is null )
		{
			throw new InvalidDataException(
				"Could not deserialize the Scryfall symbology response." );
		}

		if ( !string.Equals(
			response.Object,
			"list",
			StringComparison.Ordinal ) )
		{
			throw new InvalidDataException(
				$"Expected a Scryfall list response, but received " +
				$"'{response.Object}'." );
		}

		if ( response.HasMore )
		{
			throw new InvalidDataException(
				"The Scryfall symbology response unexpectedly has " +
				"another page." );
		}

		if ( response.Data is not { Length: > 0 } sourceDefinitions )
		{
			throw new InvalidDataException(
				"The Scryfall symbology response contains no definitions." );
		}

		var definitions =
			new List<CardSymbolDefinition>( sourceDefinitions.Length );
		var identifiers = new HashSet<SymbolIdentifier>();

		for ( var index = 0; index < sourceDefinitions.Length; index++ )
		{
			CardSymbolDefinition definition =
				ScryfallSymbolNormalizer.Normalize(
					sourceDefinitions[index] );

			if ( !identifiers.Add( definition.Id ) )
			{
				throw new InvalidDataException(
					$"Duplicate Scryfall symbol identifier " +
					$"'{definition.Id}' at data index {index}." );
			}

			definitions.Add( definition );
		}

		return definitions;
	}

	private static void ReadJsonLines<T>(
		Stream input,
		Action<T, int> onObject,
		JsonSerializerOptions options )
	{
		using Stream decodedInput = OpenDecodedInput(input);
		using StreamReader reader = new(decodedInput);

		int lineNumber = 0;
		int objectIndex = 0;

		while ( reader.ReadLine() is { } line )
		{
			lineNumber++;

			if ( string.IsNullOrWhiteSpace(line) )
				continue;

			T? value;

			try
			{
				value = JsonSerializer.Deserialize<T>(line, options);
			}
			catch ( JsonException exception )
			{
				throw new JsonException(
					$"Invalid JSON object at JSONL line {lineNumber}.",
					exception
				);
			}

			if ( value is null )
				throw new JsonException(
					$"Expected a JSON object at JSONL line {lineNumber}."
				);

			onObject(value, objectIndex);
			objectIndex++;
		}
	}

	private static Stream OpenDecodedInput(Stream input)
	{
		if ( !input.CanSeek )
			return input;

		long originalPosition = input.Position;
		int firstByte = input.ReadByte();
		int secondByte = input.ReadByte();
		input.Position = originalPosition;

		bool isGzip =
			firstByte == 0x1F &&
			secondByte == 0x8B;

		return isGzip
			? new GZipStream(input, CompressionMode.Decompress)
			: input;
	}

	private static CardIndexEntry WriteRecord<T>(
		Stream output,
		T value )
	{
		byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
			value,
			DatabaseFileInfo.DatabaseJsonOptions
		);

		long offset = output.Position;

		output.Write(
			bytes,
			0,
			bytes.Length
		);

		return new CardIndexEntry
		{
			Offset = offset,
			Length = bytes.Length
		};
	}
	
	
	private static void WriteIndexFile(CardIndexFile indexFile ){
		byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(indexFile, DatabaseFileInfo.DatabaseJsonOptions);
		using Stream output = FileSystem.Data.OpenWrite(DatabaseFileInfo.CardIndexFile);
		PrepareOutputStream(output, DatabaseFileInfo.CardIndexFile);
		output.Write(bytes, 0, bytes.Length);
		output.Flush();
	}

	private static void WriteSymbolDefinitionsFile(
		List<CardSymbolDefinition> definitions )
	{
		CardSymbolDefinitionFile symbolFile = new()
		{
			FormatVersion = DatabaseFileInfo.CurrentFormatVersion,
			SymbolCount = definitions.Count,
			Symbols = definitions
		};

		byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
			symbolFile,
			DatabaseFileInfo.DatabaseJsonOptions );

		using Stream output =
			FileSystem.Data.OpenWrite(
				DatabaseFileInfo.SymbolDefinitionsFile );

		PrepareOutputStream(
			output,
			DatabaseFileInfo.SymbolDefinitionsFile );

		output.Write( bytes, 0, bytes.Length );
		output.Flush();
	}

	private static void WriteSetDefinitionsFile(
		List<CardSetDefinition> definitions )
	{
		WriteJsonFile(
			DatabaseFileInfo.SetDefinitionsFile,
			new CardSetDefinitionFile
			{
				FormatVersion =
					DatabaseFileInfo.CurrentFormatVersion,
				SetCount = definitions.Count,
				Sets = definitions
			} );
	}

	private static void WriteRulingsFile(
		List<CardRuling> rulings )
	{
		WriteJsonFile(
			DatabaseFileInfo.RulingsFile,
			new CardRulingFile
			{
				FormatVersion =
					DatabaseFileInfo.CurrentFormatVersion,
				RulingCount = rulings.Count,
				Rulings = rulings
			} );
	}

	private static void WriteJsonFile<T>(
		string fileName,
		T value )
	{
		using Stream output = FileSystem.Data.OpenWrite( fileName );
		PrepareOutputStream( output, fileName );

		JsonSerializer.Serialize(
			output,
			value,
			DatabaseFileInfo.DatabaseJsonOptions );

		output.Flush();
	}

	private static void PrepareOutputStream(Stream stream, string fileName )
	{
		if ( !stream.CanWrite )
			throw new InvalidOperationException($"Output file '{fileName}' is not writable.");

		if ( !stream.CanSeek )
			throw new NotSupportedException($"Output file '{fileName}' must support seeking.");

		// Prevent old trailing data when replacing a larger file.
		stream.SetLength( 0 );
		stream.Position = 0;
	}
}
