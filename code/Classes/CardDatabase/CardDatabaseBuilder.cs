using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Sandbox.Classes.CardDatabase;

/// <summary>
/// Uses JsonArrayReader to iterate then CardNormalizer to produce CardDefinitions which are then saved to disk
/// </summary>
public static class CardDatabaseBuilder
{
	public static void BuildDatabase()
	{
		using Stream input = FileSystem.Data.OpenRead(CardDatabaseFiles.SourceFile);

		using Stream output = FileSystem.Data.OpenWrite(CardDatabaseFiles.CardDataFile);

		PrepareOutputStream(output, CardDatabaseFiles.CardDataFile);

		List<CardIndexEntry> indexEntries = [];
		List<CardIdMapping> idMappings = [];
		HashSet<Guid> encounteredIds = [];
		int processedCount = 0;

		JsonArrayReader.ReadObjects<ScryfallCardDto>(input, (dto, arrayIndex) => 
			{
				CardDefinition definition = ScryfallCardNormalizer.Normalize(dto);
				
				byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(definition, CardDatabaseFiles.DatabaseJsonOptions);

				long offset = output.Position;
				output.Write(bytes, 0, bytes.Length);

				int recordId = indexEntries.Count;
				
				indexEntries.Add( new CardIndexEntry
				{
					Offset = offset,
					Length = bytes.Length
				});

				idMappings.Add( new CardIdMapping()
				{
					ScryfallId = definition.ScryfallId,
					RecordId = recordId,
				});
				
				processedCount++;
			},
			CardDatabaseFiles.ImportJsonOptions
		);

		output.Flush();

		CardIndexFile indexFile = new()
		{
			FormatVersion = CardDatabaseFiles.CurrentFormatVersion,
			CardCount = indexEntries.Count,
			Cards = indexEntries,
			IdMappings =  idMappings,
		};

		WriteIndexFile( indexFile );

		Log.Info($"Database build complete. Stored " + $"{processedCount:N0} cards.");
	}

	private static void WriteIndexFile(CardIndexFile indexFile ){
		byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(indexFile, CardDatabaseFiles.DatabaseJsonOptions);
		using Stream output = FileSystem.Data.OpenWrite(CardDatabaseFiles.CardIndexFile);
		PrepareOutputStream(output, CardDatabaseFiles.CardIndexFile);
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