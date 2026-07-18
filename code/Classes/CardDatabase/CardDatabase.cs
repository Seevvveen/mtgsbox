using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Sandbox.Classes.CardDatabase;

/// <summary>
/// Provides runtime access to the finished local card database.
/// </summary>
public static class CardDatabase
{
	private static readonly Dictionary<Guid, int> RecordIdByScryfallId = [];
	private static CardIndexEntry[] Entries = [];

	private static readonly object StreamLock = new();

	private static Stream? _cardDataStream;

	public static bool IsOpen
	{
		get
		{
			lock ( StreamLock )
				return _cardDataStream is not null;
		}
	}

	public static int IndexedCardCount
	{
		get
		{
			lock ( StreamLock )
				return Entries.Length;
		}
	}

	/// <summary>
	/// Loads the index and opens cards.dat.
	/// Does not deserialize every card.
	/// </summary>
	public static void Initialize()
	{
		Shutdown();

		CardIndexFile indexFile = ReadIndexFile();

		if ( indexFile.Cards.Count != indexFile.CardCount )
			throw new InvalidDataException($"Index claims to contain {indexFile.CardCount:N0} cards, " + $"but contains {indexFile.Cards.Count:N0} entries.");

		if ( indexFile.IdMappings.Count != indexFile.CardCount )
			throw new InvalidDataException($"Index claims to contain {indexFile.CardCount:N0} cards, " + $"but contains {indexFile.IdMappings.Count:N0} ID mappings.");

		// The array position is the record ID.
		CardIndexEntry[] loadedEntries = new CardIndexEntry[indexFile.Cards.Count];

		for ( int i = 0; i < indexFile.Cards.Count; i++ )
		{
			CardIndexEntry entry = indexFile.Cards[i];

			if ( entry.Offset < 0 )
				throw new InvalidDataException($"Record {i} has an invalid offset: {entry.Offset}.");

			if ( entry.Length <= 0 )
				throw new InvalidDataException($"Record {i} has an invalid length: {entry.Length}.");

			loadedEntries[i] = entry;
		}

		Dictionary<Guid, int> loadedMappings = new( indexFile.CardCount );

		bool[] mappedRecordIds = new bool[loadedEntries.Length];

		foreach ( CardIdMapping mapping in indexFile.IdMappings )
		{
			if (mapping.RecordId < 0 || mapping.RecordId >= loadedEntries.Length)
				throw new InvalidDataException($"Invalid record ID {mapping.RecordId} for " + $"card '{mapping.ScryfallId}'.");

			if ( !loadedMappings.TryAdd(mapping.ScryfallId, mapping.RecordId) )
				throw new InvalidDataException($"Duplicate Scryfall ID: {mapping.ScryfallId}.");

			if ( mappedRecordIds[mapping.RecordId] )
				throw new InvalidDataException($"Record ID {mapping.RecordId} is mapped more than once.");

			mappedRecordIds[mapping.RecordId] = true;
		}

		Stream dataStream = FileSystem.Data.OpenRead(CardDatabaseFiles.CardDataFile);

		if ( !dataStream.CanSeek )
		{
			dataStream.Dispose();

			throw new NotSupportedException($"'{CardDatabaseFiles.CardDataFile}' must support seeking.");
		}

		// Validate that every entry falls inside cards.dat.
		for ( int recordId = 0; recordId < loadedEntries.Length; recordId++ )
		{
			CardIndexEntry entry = loadedEntries[recordId];

			long endPosition = entry.Offset + entry.Length;

			if ( endPosition > dataStream.Length )
			{
				dataStream.Dispose();

				throw new InvalidDataException($"Record {recordId} ends at byte {endPosition}, " + $"but cards.dat is only {dataStream.Length} bytes.");
			}
		}

		lock ( StreamLock )
		{
			_cardDataStream = dataStream;
			Entries = loadedEntries;

			RecordIdByScryfallId.Clear();

			foreach ( KeyValuePair<Guid, int> mapping in loadedMappings )
				RecordIdByScryfallId.Add(mapping.Key, mapping.Value);
		}

		Log.Info($"Card database opened with " + $"{Entries.Length:N0} indexed cards.");
	}

	/// <summary>
	/// Retrieves a card using its permanent Scryfall ID.
	/// </summary>
	public static CardDefinition? GetCard( Guid scryfallId )
	{
		byte[] bytes;

		lock ( StreamLock )
		{
			EnsureDatabaseOpen();

			if ( !RecordIdByScryfallId.TryGetValue(scryfallId, out int recordId) )
				return null;

			CardIndexEntry entry = GetIndexEntry( recordId );

			bytes = ReadRecordBytes( entry );
		}

		CardDefinition? card = JsonSerializer.Deserialize<CardDefinition>(bytes, CardDatabaseFiles.DatabaseJsonOptions);

		if ( card is null )
			throw new InvalidDataException($"Card '{scryfallId}' deserialized to null.");

		if ( card.ScryfallId != scryfallId )
			throw new InvalidDataException($"Card index mismatch. Requested '{scryfallId}' " + $"but read '{card.ScryfallId}'.");
		

		return card;
	}

	/// <summary>
	/// Retrieves a card directly through its database-local record ID.
	/// </summary>
	public static CardDefinition GetCard( int recordId )
	{
		byte[] bytes;

		lock ( StreamLock )
		{
			EnsureDatabaseOpen();

			CardIndexEntry entry = GetIndexEntry( recordId );

			bytes = ReadRecordBytes( entry );
		}

		CardDefinition? card = JsonSerializer.Deserialize<CardDefinition>(bytes, CardDatabaseFiles.DatabaseJsonOptions);

		return card ?? throw new InvalidDataException(
			$"Record {recordId} deserialized to null."
		);
	}

	public static bool TryGetCard(Guid scryfallId, out CardDefinition? card )
	{
		card = GetCard( scryfallId );
		return card is not null;
	}

	public static bool ContainsCard( Guid scryfallId )
	{
		lock ( StreamLock )
			return RecordIdByScryfallId.ContainsKey( scryfallId );
	}

	public static bool TryGetRecordId(Guid scryfallId, out int recordId )
	{
		lock ( StreamLock )
			return RecordIdByScryfallId.TryGetValue(scryfallId, out recordId);
	}

	public static void Shutdown()
	{
		lock ( StreamLock )
		{
			_cardDataStream?.Dispose();
			_cardDataStream = null;

			RecordIdByScryfallId.Clear();
			Entries = [];
		}
	}

	private static CardIndexEntry GetIndexEntry( int recordId )
	{
		if ( recordId < 0 || recordId >= Entries.Length )
			throw new ArgumentOutOfRangeException(nameof(recordId), recordId, $"Record ID must be between 0 and {Entries.Length - 1}.");

		return Entries[recordId];
	}

	/// <summary>
	/// Must be called while holding StreamLock.
	/// </summary>
	private static byte[] ReadRecordBytes( CardIndexEntry entry )
	{
		if ( _cardDataStream is null )
			throw new InvalidOperationException("Card database is not open.");

		byte[] bytes = new byte[entry.Length];

		_cardDataStream.Seek(entry.Offset, SeekOrigin.Begin);

		ReadExactly(_cardDataStream, bytes);

		return bytes;
	}

	private static void EnsureDatabaseOpen()
	{
		if ( _cardDataStream is null )
			throw new InvalidOperationException("Card database is not open. " + "Call CardDatabase.Initialize() first.");
	}

	private static CardIndexFile ReadIndexFile()
	{
		using Stream indexStream = FileSystem.Data.OpenRead(CardDatabaseFiles.CardIndexFile);

		CardIndexFile? indexFile = JsonSerializer.Deserialize<CardIndexFile>(indexStream, CardDatabaseFiles.DatabaseJsonOptions);

		if ( indexFile is null )
			throw new InvalidDataException("Could not deserialize the card index.");

		if (indexFile.FormatVersion != CardDatabaseFiles.CurrentFormatVersion)
			throw new InvalidDataException($"Unsupported card database version: " + $"{indexFile.FormatVersion}. Expected " + $"{CardDatabaseFiles.CurrentFormatVersion}.");

		return indexFile;
	}

	private static void ReadExactly(Stream stream, byte[] buffer )
	{
		int totalRead = 0;

		while ( totalRead < buffer.Length )
		{
			int bytesRead = stream.Read(buffer, totalRead, buffer.Length - totalRead);

			if ( bytesRead == 0 )
				throw new EndOfStreamException("Reached the end of cards.dat before reading " + "the complete card.");

			totalRead += bytesRead;
		}
	}
}