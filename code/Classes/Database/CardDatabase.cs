using Sandbox.Classes.CardDatabase;
#nullable enable

using Sandbox.Classes.Cards.ManaSymbols;
using Sandbox.Classes.Database.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Sandbox.Classes.Database;

/// <summary>
/// Provides runtime access to the finished local card database.
/// </summary>
public static class CardDatabase
{
	private static readonly Dictionary<Guid, int> RecordIdByScryfallId = [];
	private static readonly Dictionary<
		SymbolIdentifier,
		CardSymbolDefinition> SymbolDefinitionById = [];
	private static readonly Dictionary<Guid, CardSetDefinition>
		SetDefinitionById = [];
	private static readonly Dictionary<string, CardSetDefinition>
		SetDefinitionByCode =
			new( StringComparer.OrdinalIgnoreCase );
	private static readonly Dictionary<Guid, List<CardRuling>>
		RulingsByOracleId = [];
	private static CardIndexEntry[] Entries = [];
	private static int LoadedRulingCount;

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

	public static int SymbolDefinitionCount
	{
		get
		{
			lock ( StreamLock )
				return SymbolDefinitionById.Count;
		}
	}

	public static int SetDefinitionCount
	{
		get
		{
			lock ( StreamLock )
				return SetDefinitionById.Count;
		}
	}

	public static int RulingCount
	{
		get
		{
			lock ( StreamLock )
				return LoadedRulingCount;
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
		CardSymbolDefinitionFile symbolFile =
			ReadSymbolDefinitionFile();
		CardSetDefinitionFile setFile =
			ReadSetDefinitionFile();
		CardRulingFile rulingFile = ReadRulingFile();

		if ( indexFile.Cards.Count != indexFile.CardCount )
		{
			throw new InvalidDataException(
				$"Index claims to contain {indexFile.CardCount:N0} cards, " +
				$"but contains {indexFile.Cards.Count:N0} entries."
			);
		}

		if ( indexFile.IdMappings.Count != indexFile.CardCount )
		{
			throw new InvalidDataException(
				$"Index claims to contain {indexFile.CardCount:N0} cards, " +
				$"but contains {indexFile.IdMappings.Count:N0} ID mappings."
			);
		}

		// The array position is the database-local record ID.
		CardIndexEntry[] loadedEntries = new CardIndexEntry[indexFile.Cards.Count];

		for ( int i = 0; i < indexFile.Cards.Count; i++ )
		{
			CardIndexEntry entry = indexFile.Cards[i];

			if ( entry.Offset < 0 )
			{
				throw new InvalidDataException(
					$"Record {i} has an invalid offset: {entry.Offset}."
				);
			}

			if ( entry.Length <= 0 )
			{
				throw new InvalidDataException(
					$"Record {i} has an invalid length: {entry.Length}."
				);
			}

			loadedEntries[i] = entry;
		}

		Dictionary<Guid, int> loadedMappings = new( indexFile.CardCount );
		bool[] mappedRecordIds = new bool[loadedEntries.Length];

		foreach ( CardIdMapping mapping in indexFile.IdMappings )
		{
			if ( mapping.RecordId < 0 || mapping.RecordId >= loadedEntries.Length )
			{
				throw new InvalidDataException(
					$"Invalid record ID {mapping.RecordId} for " +
					$"card '{mapping.ScryfallId}'."
				);
			}

			if ( !loadedMappings.TryAdd( mapping.ScryfallId, mapping.RecordId ) )
			{
				throw new InvalidDataException(
					$"Duplicate Scryfall ID: {mapping.ScryfallId}."
				);
			}

			if ( mappedRecordIds[mapping.RecordId] )
			{
				throw new InvalidDataException(
					$"Record ID {mapping.RecordId} is mapped more than once."
				);
			}

			mappedRecordIds[mapping.RecordId] = true;
		}

		if ( symbolFile.Symbols.Count != symbolFile.SymbolCount )
		{
			throw new InvalidDataException(
				$"Symbol file claims to contain " +
				$"{symbolFile.SymbolCount:N0} definitions, but contains " +
				$"{symbolFile.Symbols.Count:N0} entries." );
		}

		if ( setFile.Sets.Count != setFile.SetCount )
		{
			throw new InvalidDataException(
				$"Set file claims to contain {setFile.SetCount:N0} " +
				$"definitions, but contains {setFile.Sets.Count:N0} " +
				"entries." );
		}

		if ( rulingFile.Rulings.Count != rulingFile.RulingCount )
		{
			throw new InvalidDataException(
				$"Ruling file claims to contain " +
				$"{rulingFile.RulingCount:N0} rulings, but contains " +
				$"{rulingFile.Rulings.Count:N0} entries." );
		}

		Dictionary<SymbolIdentifier, CardSymbolDefinition>
			loadedSymbolDefinitions =
				new( symbolFile.SymbolCount );

		foreach ( CardSymbolDefinition definition in symbolFile.Symbols )
		{
			if ( !definition.Id.IsValid )
			{
				throw new InvalidDataException(
					"Symbol file contains an uninitialized identifier." );
			}

			if ( !loadedSymbolDefinitions.TryAdd(
				definition.Id,
				definition ) )
			{
				throw new InvalidDataException(
					$"Duplicate symbol identifier: '{definition.Id}'." );
			}
		}

		var loadedSetsById =
			new Dictionary<Guid, CardSetDefinition>(
				setFile.SetCount );
		var loadedSetsByCode =
			new Dictionary<string, CardSetDefinition>(
				setFile.SetCount,
				StringComparer.OrdinalIgnoreCase );

		foreach ( CardSetDefinition definition in setFile.Sets )
		{
			if ( !loadedSetsById.TryAdd(
				definition.Id,
				definition ) )
			{
				throw new InvalidDataException(
					$"Duplicate set ID: '{definition.Id}'." );
			}

			if ( !loadedSetsByCode.TryAdd(
				definition.Code,
				definition ) )
			{
				throw new InvalidDataException(
					$"Duplicate set code: '{definition.Code}'." );
			}
		}

		var loadedRulings =
			new Dictionary<Guid, List<CardRuling>>();

		foreach ( CardRuling ruling in rulingFile.Rulings )
		{
			if ( !loadedRulings.TryGetValue(
				ruling.OracleId,
				out List<CardRuling>? oracleRulings ) )
			{
				oracleRulings = [];
				loadedRulings.Add(
					ruling.OracleId,
					oracleRulings );
			}

			oracleRulings.Add( ruling );
		}

		Stream dataStream =
			FileSystem.Data.OpenRead( DatabaseFileInfo.CardDataFile );

		if ( !dataStream.CanSeek )
		{
			dataStream.Dispose();

			throw new NotSupportedException(
				$"'{DatabaseFileInfo.CardDataFile}' must support seeking."
			);
		}

		for ( int recordId = 0; recordId < loadedEntries.Length; recordId++ )
		{
			CardIndexEntry entry = loadedEntries[recordId];

			// Avoid unchecked overflow from Offset + Length.
			if ( entry.Offset > dataStream.Length - entry.Length )
			{
				dataStream.Dispose();

				throw new InvalidDataException(
					$"Record {recordId} at byte {entry.Offset} with length " +
					$"{entry.Length} falls outside cards.dat, which is " +
					$"{dataStream.Length} bytes."
				);
			}
		}

		lock ( StreamLock )
		{
			_cardDataStream = dataStream;
			Entries = loadedEntries;

			RecordIdByScryfallId.Clear();

			foreach ( KeyValuePair<Guid, int> mapping in loadedMappings )
			{
				RecordIdByScryfallId.Add( mapping.Key, mapping.Value );
			}

			SymbolDefinitionById.Clear();

			foreach (
				KeyValuePair<SymbolIdentifier, CardSymbolDefinition>
					definition in loadedSymbolDefinitions )
			{
				SymbolDefinitionById.Add(
					definition.Key,
					definition.Value );
			}

			SetDefinitionById.Clear();
			SetDefinitionByCode.Clear();

			foreach (
				KeyValuePair<Guid, CardSetDefinition> definition
					in loadedSetsById )
			{
				SetDefinitionById.Add(
					definition.Key,
					definition.Value );
			}

			foreach (
				KeyValuePair<string, CardSetDefinition> definition
					in loadedSetsByCode )
			{
				SetDefinitionByCode.Add(
					definition.Key,
					definition.Value );
			}

			RulingsByOracleId.Clear();

			foreach (
				KeyValuePair<Guid, List<CardRuling>> group
					in loadedRulings )
			{
				RulingsByOracleId.Add( group.Key, group.Value );
			}

			LoadedRulingCount = rulingFile.RulingCount;
		}

		Log.Info(
			$"Card database opened with {loadedEntries.Length:N0} indexed " +
			$"cards, {loadedSetsById.Count:N0} sets, " +
			$"{rulingFile.RulingCount:N0} rulings, and " +
			$"{loadedSymbolDefinitions.Count:N0} symbol definitions."
		);
	}

	/// <summary>
	/// Retrieves normalized card data using its Scryfall printing ID.
	/// Returns null when the ID is not present in the database.
	/// </summary>
	public static NormalizedCard? GetCard( Guid scryfallId )
	{
		int recordId;

		lock ( StreamLock )
		{
			EnsureDatabaseOpen();

			if ( !RecordIdByScryfallId.TryGetValue(
				scryfallId,
				out recordId
			) )
			{
				return null;
			}
		}

		NormalizedCard card = GetCard( recordId );

		if ( card.Gameplay.ScryfallId != scryfallId )
		{
			throw new InvalidDataException(
				$"Card index mismatch. Requested '{scryfallId}', " +
				$"but record {recordId} contained " +
				$"'{card.Gameplay.ScryfallId}'."
			);
		}

		return card;
	}

	/// <summary>
	/// Retrieves normalized card data using its database-local record ID.
	/// </summary>
	public static NormalizedCard GetCard( int recordId )
	{
		byte[] bytes;

		lock ( StreamLock )
		{
			EnsureDatabaseOpen();

			CardIndexEntry entry = GetIndexEntry( recordId );
			bytes = ReadRecordBytes( entry );
		}

		NormalizedCard? card =
			JsonSerializer.Deserialize<NormalizedCard>(
				bytes,
				DatabaseFileInfo.DatabaseJsonOptions
			);

		if ( card is null )
		{
			throw new InvalidDataException(
				$"Record {recordId} deserialized to null."
			);
		}

		ValidateNormalizedCard( card, recordId );

		return card;
	}

	public static bool TryGetCard(
		Guid scryfallId,
		out NormalizedCard? card
	)
	{
		card = GetCard( scryfallId );
		return card is not null;
	}

	/// <summary>
	/// Retrieves the Scryfall definition for one canonical symbol.
	/// Returns null when the symbol is not in the database.
	/// </summary>
	public static CardSymbolDefinition? GetSymbolDefinition(
		SymbolIdentifier identifier )
	{
		lock ( StreamLock )
		{
			EnsureDatabaseOpen();

			return SymbolDefinitionById.GetValueOrDefault( identifier );
		}
	}

	public static bool TryGetSymbolDefinition(
		SymbolIdentifier identifier,
		out CardSymbolDefinition? definition )
	{
		lock ( StreamLock )
		{
			EnsureDatabaseOpen();

			return SymbolDefinitionById.TryGetValue(
				identifier,
				out definition );
		}
	}

	public static CardSetDefinition? GetSetDefinition( Guid setId )
	{
		lock ( StreamLock )
		{
			EnsureDatabaseOpen();
			return SetDefinitionById.GetValueOrDefault( setId );
		}
	}

	public static CardSetDefinition? GetSetDefinition( string setCode )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( setCode );

		lock ( StreamLock )
		{
			EnsureDatabaseOpen();
			return SetDefinitionByCode.GetValueOrDefault( setCode );
		}
	}

	public static bool TryGetSetDefinition(
		Guid setId,
		out CardSetDefinition? definition )
	{
		lock ( StreamLock )
		{
			EnsureDatabaseOpen();
			return SetDefinitionById.TryGetValue(
				setId,
				out definition );
		}
	}

	public static bool TryGetSetDefinition(
		string setCode,
		out CardSetDefinition? definition )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( setCode );

		lock ( StreamLock )
		{
			EnsureDatabaseOpen();
			return SetDefinitionByCode.TryGetValue(
				setCode,
				out definition );
		}
	}

	/// <summary>
	/// Gets all rulings associated with an Oracle card identity.
	/// The returned array is independent of the database's internal index.
	/// </summary>
	public static CardRuling[] GetRulings( Guid oracleId )
	{
		lock ( StreamLock )
		{
			EnsureDatabaseOpen();

			return RulingsByOracleId.TryGetValue(
				oracleId,
				out List<CardRuling>? rulings )
				? [.. rulings]
				: [];
		}
	}

	public static bool ContainsCard( Guid scryfallId )
	{
		lock ( StreamLock )
		{
			return RecordIdByScryfallId.ContainsKey( scryfallId );
		}
	}

	public static bool TryGetRecordId(
		Guid scryfallId,
		out int recordId
	)
	{
		lock ( StreamLock )
		{
			return RecordIdByScryfallId.TryGetValue(
				scryfallId,
				out recordId
			);
		}
	}

	public static void Shutdown()
	{
		lock ( StreamLock )
		{
			_cardDataStream?.Dispose();
			_cardDataStream = null;

			RecordIdByScryfallId.Clear();
			SymbolDefinitionById.Clear();
			SetDefinitionById.Clear();
			SetDefinitionByCode.Clear();
			RulingsByOracleId.Clear();
			LoadedRulingCount = 0;
			Entries = [];
		}
	}

	private static CardIndexEntry GetIndexEntry( int recordId )
	{
		if ( recordId < 0 || recordId >= Entries.Length )
		{
			throw new ArgumentOutOfRangeException(
				nameof(recordId),
				recordId,
				$"Record ID must be between 0 and {Entries.Length - 1}."
			);
		}

		return Entries[recordId];
	}

	/// <summary>
	/// Must be called while holding StreamLock.
	/// </summary>
	private static byte[] ReadRecordBytes( CardIndexEntry entry )
	{
		if ( _cardDataStream is null )
		{
			throw new InvalidOperationException(
				"Card database is not open."
			);
		}

		byte[] bytes = new byte[entry.Length];

		_cardDataStream.Seek( entry.Offset, SeekOrigin.Begin );
		ReadExactly( _cardDataStream, bytes );

		return bytes;
	}

	private static void EnsureDatabaseOpen()
	{
		if ( _cardDataStream is null )
		{
			throw new InvalidOperationException(
				"Card database is not open. " +
				"Call CardDatabase.Initialize() first."
			);
		}
	}

	private static void ValidateNormalizedCard(
		NormalizedCard card,
		int recordId )
	{
		if ( card.Gameplay is null )
		{
			throw new InvalidDataException(
				$"Record {recordId} has null gameplay data." );
		}

		if ( card.Presentation is null )
		{
			throw new InvalidDataException(
				$"Record {recordId} has null presentation data." );
		}

		if ( card.Identifiers is null )
		{
			throw new InvalidDataException(
				$"Record {recordId} has null identifier data." );
		}

		if ( card.Set is null )
		{
			throw new InvalidDataException(
				$"Record {recordId} has null set data." );
		}

		if ( card.Links is null )
		{
			throw new InvalidDataException(
				$"Record {recordId} has null resource links." );
		}

		if ( card.Source is null )
		{
			throw new InvalidDataException(
				$"Record {recordId} has null source metadata." );
		}

		if ( card.Gameplay.Faces is not { Length: > 0 } faces )
		{
			throw new InvalidDataException(
				$"Record {recordId} has no card faces." );
		}

		for ( var index = 0; index < faces.Length; index++ )
		{
			if ( faces[index] is null )
			{
				throw new InvalidDataException(
					$"Record {recordId}, face {index} is null." );
			}

			if ( faces[index].ManaCost is null )
			{
				throw new InvalidDataException(
					$"Record {recordId}, face {index} has a null mana cost." );
			}

			if ( string.IsNullOrWhiteSpace( faces[index].Name ) )
			{
				throw new InvalidDataException(
					$"Record {recordId}, face {index} has no name." );
			}
		}
	}

	private static CardIndexFile ReadIndexFile()
	{
		using Stream indexStream =
			FileSystem.Data.OpenRead( DatabaseFileInfo.CardIndexFile );

		CardIndexFile? indexFile =
			JsonSerializer.Deserialize<CardIndexFile>(
				indexStream,
				DatabaseFileInfo.DatabaseJsonOptions
			);

		if ( indexFile is null )
		{
			throw new InvalidDataException(
				"Could not deserialize the card index."
			);
		}

		if (
			indexFile.FormatVersion !=
			DatabaseFileInfo.CurrentFormatVersion
		)
		{
			throw new InvalidDataException(
				$"Unsupported card database version: " +
				$"{indexFile.FormatVersion}. Expected " +
				$"{DatabaseFileInfo.CurrentFormatVersion}."
			);
		}

		return indexFile;
	}

	private static CardSymbolDefinitionFile ReadSymbolDefinitionFile()
	{
		using Stream input =
			FileSystem.Data.OpenRead(
				DatabaseFileInfo.SymbolDefinitionsFile );

		CardSymbolDefinitionFile? symbolFile =
			JsonSerializer.Deserialize<CardSymbolDefinitionFile>(
				input,
				DatabaseFileInfo.DatabaseJsonOptions );

		if ( symbolFile is null )
		{
			throw new InvalidDataException(
				"Could not deserialize the symbol-definition file." );
		}

		if (
			symbolFile.FormatVersion !=
			DatabaseFileInfo.CurrentFormatVersion
		)
		{
			throw new InvalidDataException(
				$"Unsupported symbol-definition version: " +
				$"{symbolFile.FormatVersion}. Expected " +
				$"{DatabaseFileInfo.CurrentFormatVersion}." );
		}

		return symbolFile;
	}

	private static CardSetDefinitionFile ReadSetDefinitionFile()
	{
		using Stream input =
			FileSystem.Data.OpenRead(
				DatabaseFileInfo.SetDefinitionsFile );

		CardSetDefinitionFile? setFile =
			JsonSerializer.Deserialize<CardSetDefinitionFile>(
				input,
				DatabaseFileInfo.DatabaseJsonOptions );

		if ( setFile is null )
		{
			throw new InvalidDataException(
				"Could not deserialize the set-definition file." );
		}

		ValidateFormatVersion(
			setFile.FormatVersion,
			"set-definition" );

		return setFile;
	}

	private static CardRulingFile ReadRulingFile()
	{
		using Stream input =
			FileSystem.Data.OpenRead( DatabaseFileInfo.RulingsFile );

		CardRulingFile? rulingFile =
			JsonSerializer.Deserialize<CardRulingFile>(
				input,
				DatabaseFileInfo.DatabaseJsonOptions );

		if ( rulingFile is null )
		{
			throw new InvalidDataException(
				"Could not deserialize the rulings file." );
		}

		ValidateFormatVersion(
			rulingFile.FormatVersion,
			"ruling" );

		return rulingFile;
	}

	private static void ValidateFormatVersion(
		int actual,
		string fileDescription )
	{
		if ( actual != DatabaseFileInfo.CurrentFormatVersion )
		{
			throw new InvalidDataException(
				$"Unsupported {fileDescription} version: {actual}. " +
				$"Expected {DatabaseFileInfo.CurrentFormatVersion}." );
		}
	}

	private static void ReadExactly(
		Stream stream,
		byte[] buffer
	)
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
