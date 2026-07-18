using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Sandbox.Classes.CardDatabase.Models;

namespace Sandbox.Classes.CardDatabase;

/// <summary>
/// Provides runtime access to the finished local card database.
/// </summary>
public static class CardDatabase
{
	private static readonly Dictionary<Guid, CardIndexEntry> ByScryfallId = [];

	private static readonly object StreamLock = new();

	private static Stream? _cardDataStream;

	public static bool IsOpen => _cardDataStream is not null;

	public static int IndexedCardCount => ByScryfallId.Count;

	/// <summary>
	/// Loads the index and opens cards.dat.
	/// Does not deserialize every card.
	/// </summary>
	public static void Initialize()
	{
		Shutdown();

		CardIndexFile indexFile = ReadIndexFile();

		Dictionary<Guid, CardIndexEntry> loadedIndex =
			new( indexFile.CardCount );

		foreach ( CardIndexEntry entry in indexFile.Cards )
		{
			if ( entry.Offset < 0 )
			{
				throw new InvalidDataException(
					$"Card '{entry.ScryfallId}' has an invalid offset."
				);
			}

			if ( entry.Length <= 0 )
			{
				throw new InvalidDataException(
					$"Card '{entry.ScryfallId}' has an invalid length."
				);
			}

			if ( !loadedIndex.TryAdd( entry.ScryfallId, entry ) )
			{
				throw new InvalidDataException(
					$"Duplicate Scryfall ID in card index: " +
					$"{entry.ScryfallId}"
				);
			}
		}

		if ( loadedIndex.Count != indexFile.CardCount )
		{
			throw new InvalidDataException(
				$"Index expected {indexFile.CardCount:N0} cards, " +
				$"but contained {loadedIndex.Count:N0}."
			);
		}

		Stream dataStream = FileSystem.Data.OpenRead(
			CardDatabaseFiles.CardDataFile
		);

		if ( !dataStream.CanSeek )
		{
			dataStream.Dispose();

			throw new NotSupportedException(
				$"'{CardDatabaseFiles.CardDataFile}' must support seeking."
			);
		}

		lock ( StreamLock )
		{
			_cardDataStream = dataStream;

			ByScryfallId.Clear();

			foreach ( var pair in loadedIndex )
			{
				ByScryfallId.Add(
					pair.Key,
					pair.Value
				);
			}
		}

		Log.Info(
			$"Card database opened with " +
			$"{ByScryfallId.Count:N0} indexed cards."
		);
	}

	public static CardDefinition? GetCard(Guid scryfallId )
	{
		lock ( StreamLock )
		{
			if ( _cardDataStream is null )
			{
				throw new InvalidOperationException(
					"Card database is not open. " +
					"Call CardDatabase.Initialize() first."
				);
			}

			if ( !ByScryfallId.TryGetValue(
				scryfallId,
				out CardIndexEntry entry
			) )
			{
				return null;
			}

			byte[] bytes = new byte[entry.Length];

			_cardDataStream.Seek(
				entry.Offset,
				SeekOrigin.Begin
			);

			ReadExactly(
				_cardDataStream,
				bytes
			);

			CardDefinition? card =
				JsonSerializer.Deserialize<CardDefinition>(
					bytes,
					CardDatabaseFiles.DatabaseJsonOptions
				);

			if ( card is null )
			{
				throw new InvalidDataException(
					$"Card '{scryfallId}' deserialized to null."
				);
			}

			if ( card.ScryfallId != scryfallId )
			{
				throw new InvalidDataException(
					$"Card index mismatch. Requested " +
					$"'{scryfallId}' but read '{card.ScryfallId}'."
				);
			}

			return card;
		}
	}

	public static bool TryGetCard(
		Guid scryfallId,
		out CardDefinition? card )
	{
		card = GetCard( scryfallId );
		return card is not null;
	}

	public static bool ContainsCard(
		Guid scryfallId )
	{
		return ByScryfallId.ContainsKey( scryfallId );
	}

	public static void Shutdown()
	{
		lock ( StreamLock )
		{
			_cardDataStream?.Dispose();
			_cardDataStream = null;

			ByScryfallId.Clear();
		}
	}

	private static CardIndexFile ReadIndexFile()
	{
		using Stream indexStream = FileSystem.Data.OpenRead(
			CardDatabaseFiles.CardIndexFile
		);

		CardIndexFile? indexFile =
			JsonSerializer.Deserialize<CardIndexFile>(
				indexStream,
				CardDatabaseFiles.DatabaseJsonOptions
			);

		if ( indexFile is null )
		{
			throw new InvalidDataException(
				"Could not deserialize the card index."
			);
		}

		if (
			indexFile.FormatVersion !=
			CardDatabaseFiles.CurrentFormatVersion
		)
		{
			throw new InvalidDataException(
				$"Unsupported card database version: " +
				$"{indexFile.FormatVersion}. Expected " +
				$"{CardDatabaseFiles.CurrentFormatVersion}."
			);
		}

		return indexFile;
	}

	private static void ReadExactly(
		Stream stream,
		byte[] buffer )
	{
		int totalRead = 0;

		while ( totalRead < buffer.Length )
		{
			int bytesRead = stream.Read(
				buffer,
				totalRead,
				buffer.Length - totalRead
			);

			if ( bytesRead == 0 )
			{
				throw new EndOfStreamException(
					"Reached the end of cards.dat before reading " +
					"the complete card."
				);
			}

			totalRead += bytesRead;
		}
	}
}