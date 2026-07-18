using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Sandbox.Classes.CardDatabase.Models;

namespace Sandbox.Classes.CardDatabase;

/// <summary>
/// Uses JsonArrayReader to iterate then CardNormalizer to produce CardDefinitions which are then saved to disk
/// </summary>
public static class CardDatabaseBuilder
{
	public static void BuildDatabase()
	{
		using Stream input = FileSystem.Data.OpenRead(
			CardDatabaseFiles.SourceFile
		);

		using Stream output = FileSystem.Data.OpenWrite(
			CardDatabaseFiles.CardDataFile
		);

		PrepareOutputStream(
			output,
			CardDatabaseFiles.CardDataFile
		);

		List<CardIndexEntry> indexEntries = [];
		HashSet<Guid> encounteredIds = [];

		int processedCount = 0;

		JsonArrayReader.ReadObjects<Models.ScryfallCardDto>(
			input,
			(dto, arrayIndex) =>
			{
				CardDefinition cardDefinition;

				try
				{
					cardDefinition = ScryfallCardNormalizer.Normalize( dto );
				}
				catch ( Exception exception )
				{
					throw new InvalidDataException(
						$"Failed to process card at array index {arrayIndex}.",
						exception
					);
				}

				if ( !encounteredIds.Add( cardDefinition.ScryfallId ) )
				{
					throw new InvalidDataException(
						$"Duplicate Scryfall ID found during import: " +
						$"{cardDefinition.ScryfallId}"
					);
				}

				byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
					cardDefinition,
					CardDatabaseFiles.DatabaseJsonOptions
				);

				long offset = output.Position;

				output.Write(
					bytes,
					0,
					bytes.Length
				);

				indexEntries.Add( new CardIndexEntry
				{
					ScryfallId = cardDefinition.ScryfallId,
					Offset = offset,
					Length = bytes.Length
				} );

				processedCount++;

				if ( processedCount % 1000 == 0 )
				{
					Log.Info(
						$"Processed {processedCount:N0} cards."
					);
				}
			},
			CardDatabaseFiles.ImportJsonOptions
		);

		output.Flush();

		CardIndexFile indexFile = new()
		{
			FormatVersion = CardDatabaseFiles.CurrentFormatVersion,
			CardCount = processedCount,
			Cards = indexEntries
		};

		WriteIndexFile( indexFile );

		Log.Info(
			$"Database build complete. Stored " +
			$"{processedCount:N0} cards."
		);
	}

	private static void WriteIndexFile(
		CardIndexFile indexFile )
	{
		byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
			indexFile,
			CardDatabaseFiles.DatabaseJsonOptions
		);

		using Stream output = FileSystem.Data.OpenWrite(
			CardDatabaseFiles.CardIndexFile
		);

		PrepareOutputStream(
			output,
			CardDatabaseFiles.CardIndexFile
		);

		output.Write(
			bytes,
			0,
			bytes.Length
		);

		output.Flush();
	}

	private static void PrepareOutputStream(
		Stream stream,
		string fileName )
	{
		if ( !stream.CanWrite )
		{
			throw new InvalidOperationException(
				$"Output file '{fileName}' is not writable."
			);
		}

		if ( !stream.CanSeek )
		{
			throw new NotSupportedException(
				$"Output file '{fileName}' must support seeking."
			);
		}

		// Prevent old trailing data when replacing a larger file.
		stream.SetLength( 0 );
		stream.Position = 0;
	}
}