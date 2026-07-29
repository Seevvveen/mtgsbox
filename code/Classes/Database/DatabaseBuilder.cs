using Sandbox.Classes.CardDatabase;
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

		Log.Info(
			$"Database build complete. Stored {indexEntries.Count:N0} cards."
		);
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
