#nullable enable
using Sandbox.Classes.Cards;
using Sandbox.Classes.Cards.CardFrames;
using Sandbox.Classes.Cards.Legality;
using Sandbox.Classes.Cards.ManaSymbols;
using Sandbox.Classes.Database.Types;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
namespace Sandbox.Classes.Database;

/// <summary>
///     Provides runtime access to the finished local card database.
/// </summary>
public static class CardDatabase
{
	private static readonly object         StateLock = new object();
	private static          DatabaseState? _state;
	private static          int            _leaseCount;

	public static bool IsOpen
	{
		get
		{
			lock ( StateLock )
				return _state is not null;
		}
	}

	public static int IndexedCardCount
	{
		get
		{
			lock ( StateLock )
				return _state?.Entries.Length ?? 0;
		}
	}

	public static int SymbolDefinitionCount
	{
		get
		{
			lock ( StateLock )
				return _state?.SymbolDefinitionById.Count ?? 0;
		}
	}

	public static int SetDefinitionCount
	{
		get
		{
			lock ( StateLock )
				return _state?.SetDefinitionById.Count ?? 0;
		}
	}

	public static int RulingCount
	{
		get
		{
			lock ( StateLock )
				return _state?.RulingCount ?? 0;
		}
	}


	/// <summary>
	///     Opens the database if necessary and keeps it open until the returned
	///     lease is disposed. Scene systems should own a lease instead of
	///     unconditionally shutting down process-wide database state.
	/// </summary>
	public static IDisposable Acquire()
	{
		lock ( StateLock )
		{
			if ( _state is null )
				Initialize();

			_leaseCount++;

			return new DatabaseLease();
		}
	}


	/// <summary>
	///     Loads the index and opens cards.dat.
	///     Does not deserialize every card.
	/// </summary>
	public static void Initialize()
	{
		CardIndexFile            indexFile  = ReadIndexFile();
		CardSymbolDefinitionFile symbolFile = ReadSymbolDefinitionFile();
		CardSetDefinitionFile    setFile    = ReadSetDefinitionFile();
		CardRulingFile           rulingFile = ReadRulingFile();

		if ( indexFile.FormatVersion < 5 )
			throw new InvalidDataException( "The card database predates deck-import printing indexes " + "and must be rebuilt." );

		if ( symbolFile.FormatVersion != indexFile.FormatVersion || setFile.FormatVersion != indexFile.FormatVersion || rulingFile.FormatVersion != indexFile.FormatVersion )
			throw new InvalidDataException( "Card database artifacts come from different format " + "generations." );

		if ( indexFile.Cards.Count != indexFile.CardCount )
			throw new InvalidDataException( $"Index claims to contain {indexFile.CardCount:N0} cards, " + $"but contains {indexFile.Cards.Count:N0} entries." );

		if ( indexFile.IdMappings.Count != indexFile.CardCount )
			throw new InvalidDataException( $"Index claims to contain {indexFile.CardCount:N0} cards, " + $"but contains {indexFile.IdMappings.Count:N0} ID mappings." );

		// The array position is the database-local record ID.
		CardIndexEntry[] loadedEntries      = new CardIndexEntry[indexFile.Cards.Count];
		long             expectedDataLength = 0;

		for ( int i = 0; i < indexFile.Cards.Count; i++ )
		{
			CardIndexEntry entry = indexFile.Cards[i];

			if ( entry.Offset < 0 )
				throw new InvalidDataException( $"Record {i} has an invalid offset: {entry.Offset}." );

			if ( entry.Length <= 0 )
				throw new InvalidDataException( $"Record {i} has an invalid length: {entry.Length}." );

			if ( entry.Length > DatabaseFileInfo.MaxCardRecordBytes )
				throw new InvalidDataException( $"Record {i} is {entry.Length} bytes; the maximum is " + $"{DatabaseFileInfo.MaxCardRecordBytes} bytes." );

			if ( entry.Offset != expectedDataLength )
				throw new InvalidDataException( $"Record {i} begins at byte {entry.Offset}, but the " + $"next contiguous record must begin at byte " + $"{expectedDataLength}." );

			if ( expectedDataLength > long.MaxValue - entry.Length )
				throw new InvalidDataException( $"Record {i} would overflow the database byte range." );

			loadedEntries[i]   =  entry;
			expectedDataLength += entry.Length;
		}

		Dictionary<Guid, int> loadedMappings  = new Dictionary<Guid, int>( indexFile.CardCount );
		bool[]                mappedRecordIds = new bool[loadedEntries.Length];

		foreach ( CardIdMapping mapping in indexFile.IdMappings )
		{
			if ( mapping.ScryfallId == Guid.Empty )
				throw new InvalidDataException( "Card index contains an empty Scryfall ID." );

			if ( mapping.RecordId < 0 || mapping.RecordId >= loadedEntries.Length )
				throw new InvalidDataException( $"Invalid record ID {mapping.RecordId} for " + $"card '{mapping.ScryfallId}'." );

			if ( !loadedMappings.TryAdd( mapping.ScryfallId, mapping.RecordId ) )
				throw new InvalidDataException( $"Duplicate Scryfall ID: {mapping.ScryfallId}." );

			if ( mappedRecordIds[mapping.RecordId] )
				throw new InvalidDataException( $"Record ID {mapping.RecordId} is mapped more than once." );

			mappedRecordIds[mapping.RecordId] = true;
		}

		Dictionary<string, int> loadedPrintings = new Dictionary<string, int>( indexFile.PrintingMappings.Count, StringComparer.OrdinalIgnoreCase );

		foreach ( CardPrintingMapping mapping in indexFile.PrintingMappings )
		{
			ValidateLookupRecordId( mapping.RecordId, loadedEntries.Length, "printing" );

			string key = CreatePrintingKey( mapping.SetCode, mapping.CollectorNumber );

			if ( !loadedPrintings.TryAdd( key, mapping.RecordId ) )
				throw new InvalidDataException( $"Duplicate set/collector printing mapping: " + $"'{mapping.SetCode}' '{mapping.CollectorNumber}'." );
		}

		Dictionary<string, List<int>> loadedNames = new Dictionary<string, List<int>>( StringComparer.OrdinalIgnoreCase );

		foreach ( CardNameMapping mapping in indexFile.NameMappings )
		{
			ValidateLookupRecordId( mapping.RecordId, loadedEntries.Length, "name" );

			string name = NormalizeLookupName( mapping.Name );

			if ( !loadedNames.TryGetValue( name, out List<int>? recordIds ) )
			{
				recordIds = [ ];
				loadedNames.Add( name, recordIds );
			}

			recordIds.Add( mapping.RecordId );
		}

		Dictionary<Guid, List<int>> loadedOraclePrintings = new Dictionary<Guid, List<int>>();

		foreach ( CardOracleMapping mapping in indexFile.OracleMappings )
		{
			if ( mapping.OracleId == Guid.Empty )
				throw new InvalidDataException( "Card index contains an empty Oracle ID mapping." );

			ValidateLookupRecordId( mapping.RecordId, loadedEntries.Length, "Oracle" );

			if ( !loadedOraclePrintings.TryGetValue( mapping.OracleId, out List<int>? recordIds ) )
			{
				recordIds = [ ];
				loadedOraclePrintings.Add( mapping.OracleId, recordIds );
			}

			recordIds.Add( mapping.RecordId );
		}

		if ( symbolFile.Symbols.Count != symbolFile.SymbolCount )
			throw new InvalidDataException( $"Symbol file claims to contain " + $"{symbolFile.SymbolCount:N0} definitions, but contains " + $"{symbolFile.Symbols.Count:N0} entries." );

		if ( setFile.Sets.Count != setFile.SetCount )
			throw new InvalidDataException( $"Set file claims to contain {setFile.SetCount:N0} " + $"definitions, but contains {setFile.Sets.Count:N0} " + "entries." );

		if ( rulingFile.Rulings.Count != rulingFile.RulingCount )
			throw new InvalidDataException( $"Ruling file claims to contain " + $"{rulingFile.RulingCount:N0} rulings, but contains " + $"{rulingFile.Rulings.Count:N0} entries." );

		Dictionary<SymbolIdentifier, CardSymbolDefinition> loadedSymbolDefinitions = new Dictionary<SymbolIdentifier, CardSymbolDefinition>( symbolFile.SymbolCount );

		foreach ( CardSymbolDefinition definition in symbolFile.Symbols )
		{
			if ( !definition.Id.IsValid )
				throw new InvalidDataException( "Symbol file contains an uninitialized identifier." );

			if ( !loadedSymbolDefinitions.TryAdd( definition.Id, definition ) )
				throw new InvalidDataException( $"Duplicate symbol identifier: '{definition.Id}'." );
		}

		Dictionary<Guid, CardSetDefinition>   loadedSetsById   = new Dictionary<Guid, CardSetDefinition>( setFile.SetCount );
		Dictionary<string, CardSetDefinition> loadedSetsByCode = new Dictionary<string, CardSetDefinition>( setFile.SetCount, StringComparer.OrdinalIgnoreCase );

		foreach ( CardSetDefinition definition in setFile.Sets )
		{
			if ( definition.Id == Guid.Empty )
				throw new InvalidDataException( "Set file contains an empty set ID." );

			if ( string.IsNullOrWhiteSpace( definition.Code ) )
				throw new InvalidDataException( $"Set '{definition.Id}' has no set code." );

			if ( !loadedSetsById.TryAdd( definition.Id, definition ) )
				throw new InvalidDataException( $"Duplicate set ID: '{definition.Id}'." );

			if ( !loadedSetsByCode.TryAdd( definition.Code, definition ) )
				throw new InvalidDataException( $"Duplicate set code: '{definition.Code}'." );
		}

		Dictionary<Guid, List<CardRuling>> loadedRulings = new Dictionary<Guid, List<CardRuling>>();

		foreach ( CardRuling ruling in rulingFile.Rulings )
		{
			if ( ruling.OracleId == Guid.Empty )
				throw new InvalidDataException( "Ruling file contains an empty Oracle ID." );

			if ( !loadedRulings.TryGetValue( ruling.OracleId, out List<CardRuling>? oracleRulings ) )
			{
				oracleRulings = [ ];
				loadedRulings.Add( ruling.OracleId, oracleRulings );
			}

			oracleRulings.Add( ruling );
		}

		Stream?       dataStream = null;
		DatabaseState loadedState;

		try
		{
			dataStream = FileSystem.Data.OpenRead( DatabaseFileInfo.CardDataFile );

			if ( !dataStream.CanSeek )
				throw new NotSupportedException( $"'{DatabaseFileInfo.CardDataFile}' must support seeking." );

			if ( dataStream.Length != expectedDataLength )
				throw new InvalidDataException( $"Card data is {dataStream.Length} bytes, but its " + $"contiguous index describes {expectedDataLength} bytes." );

			loadedState = new DatabaseState
						  {
							  FormatVersion        = indexFile.FormatVersion,
							  CardDataStream       = dataStream,
							  Entries              = loadedEntries,
							  RecordIdByScryfallId = loadedMappings,
							  RecordIdByPrinting   = loadedPrintings,
							  RecordIdsByName      = loadedNames,
							  RecordIdsByOracleId  = loadedOraclePrintings,
							  SymbolDefinitionById = loadedSymbolDefinitions,
							  SetDefinitionById    = loadedSetsById,
							  SetDefinitionByCode  = loadedSetsByCode,
							  RulingsByOracleId    = loadedRulings,
							  RulingCount          = rulingFile.RulingCount
						  };

			dataStream = null;
		}
		finally
		{
			dataStream?.Dispose();
		}

		DatabaseState? previousState;

		lock ( StateLock )
		{
			previousState = _state;
			_state        = loadedState;
		}

		previousState?.Dispose();

		Log.Info( $"Card database format v{indexFile.FormatVersion} opened with " + $"{loadedEntries.Length:N0} indexed " + $"cards, {loadedSetsById.Count:N0} sets, " + $"{rulingFile.RulingCount:N0} rulings, and " + $"{loadedSymbolDefinitions.Count:N0} symbol definitions." );
	}


	/// <summary>
	///     Retrieves normalized card data using its Scryfall printing ID.
	///     Returns null when the ID is not present in the database.
	/// </summary>
	public static NormalizedCard? GetCard( Guid scryfallId )
	{
		DatabaseState state;
		int           recordId;
		byte[]        bytes;

		lock ( StateLock )
		{
			state = GetOpenState();

			if ( !state.RecordIdByScryfallId.TryGetValue( scryfallId, out recordId ) )
				return null;

			CardIndexEntry entry = GetIndexEntry( state, recordId );
			bytes = ReadRecordBytes( state, entry );
		}

		NormalizedCard card = DeserializeCard( bytes, recordId, state );

		if ( card.Gameplay.ScryfallId != scryfallId )
			throw new InvalidDataException( $"Card index mismatch. Requested '{scryfallId}', " + $"but record {recordId} contained " + $"'{card.Gameplay.ScryfallId}'." );

		return card;
	}


	/// <summary>
	///     Retrieves normalized card data using its database-local record ID.
	/// </summary>
	internal static NormalizedCard GetCard( int recordId )
	{
		DatabaseState state;
		byte[]        bytes;

		lock ( StateLock )
		{
			state = GetOpenState();

			CardIndexEntry entry = GetIndexEntry( state, recordId );
			bytes = ReadRecordBytes( state, entry );
		}

		return DeserializeCard( bytes, recordId, state );
	}


	public static bool TryGetCard( Guid scryfallId, out NormalizedCard? card )
	{
		card = GetCard( scryfallId );

		return card is not null;
	}


	/// <summary>
	///     Retrieves one exact printing by Scryfall set code and collector number.
	///     Collector numbers are strings and may contain letters, hyphens, or stars.
	/// </summary>
	public static NormalizedCard? FindPrinting( string setCode, string collectorNumber )
	{
		int recordId;

		lock ( StateLock )
		{
			DatabaseState state = GetOpenState();
			string        key   = CreatePrintingKey( setCode, collectorNumber );

			if ( !state.RecordIdByPrinting.TryGetValue( key, out recordId ) )
				return null;
		}

		return GetCard( recordId );
	}


	/// <summary>
	///     Returns every printing whose canonical card name exactly matches.
	///     Name matching ignores case and surrounding whitespace.
	/// </summary>
	public static NormalizedCard[] FindByName( string name )
	{
		int[] recordIds;

		lock ( StateLock )
		{
			DatabaseState state      = GetOpenState();
			string        normalized = NormalizeLookupName( name );

			if ( !state.RecordIdsByName.TryGetValue( normalized, out List<int>? matches ) )
				return [ ];

			recordIds = [ .. matches ];
		}

		return ReadCards( recordIds );
	}


	/// <summary>
	///     Returns all known printings for an Oracle card identity.
	/// </summary>
	public static NormalizedCard[] GetPrintings( Guid oracleId )
	{
		if ( oracleId == Guid.Empty )
			return [ ];

		int[] recordIds;

		lock ( StateLock )
		{
			DatabaseState state = GetOpenState();

			if ( !state.RecordIdsByOracleId.TryGetValue( oracleId, out List<int>? matches ) )
				return [ ];

			recordIds = [ .. matches ];
		}

		return ReadCards( recordIds );
	}


	/// <summary>
	///     Retrieves the Scryfall definition for one canonical symbol.
	///     Returns null when the symbol is not in the database.
	/// </summary>
	public static CardSymbolDefinition? GetSymbolDefinition( SymbolIdentifier identifier )
	{
		return TryGetSymbolDefinition( identifier, out CardSymbolDefinition? definition )? definition : null;
	}


	public static bool TryGetSymbolDefinition( SymbolIdentifier identifier, out CardSymbolDefinition? definition )
	{
		lock ( StateLock )
		{
			DatabaseState state = GetOpenState();

			if ( !state.SymbolDefinitionById.TryGetValue( identifier, out CardSymbolDefinition? storedDefinition ) )
			{
				definition = null;

				return false;
			}

			definition = CopySymbolDefinition( storedDefinition );

			return true;
		}
	}


	public static CardSetDefinition? GetSetDefinition( Guid setId )
	{
		return TryGetSetDefinition( setId, out CardSetDefinition? definition )? definition : null;
	}


	public static CardSetDefinition? GetSetDefinition( string setCode )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( setCode );

		return TryGetSetDefinition( setCode, out CardSetDefinition? definition )? definition : null;
	}


	public static bool TryGetSetDefinition( Guid setId, out CardSetDefinition? definition )
	{
		lock ( StateLock )
		{
			DatabaseState state = GetOpenState();

			if ( !state.SetDefinitionById.TryGetValue( setId, out CardSetDefinition? storedDefinition ) )
			{
				definition = null;

				return false;
			}

			definition = CopySetDefinition( storedDefinition );

			return true;
		}
	}


	public static bool TryGetSetDefinition( string setCode, out CardSetDefinition? definition )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( setCode );

		lock ( StateLock )
		{
			DatabaseState state = GetOpenState();

			if ( !state.SetDefinitionByCode.TryGetValue( setCode, out CardSetDefinition? storedDefinition ) )
			{
				definition = null;

				return false;
			}

			definition = CopySetDefinition( storedDefinition );

			return true;
		}
	}


	/// <summary>
	///     Gets all rulings associated with an Oracle card identity.
	///     The returned array is independent of the database's internal index.
	/// </summary>
	public static CardRuling[] GetRulings( Guid oracleId )
	{
		lock ( StateLock )
		{
			DatabaseState state = GetOpenState();

			if ( !state.RulingsByOracleId.TryGetValue( oracleId, out List<CardRuling>? rulings ) )
				return [ ];

			CardRuling[] result = new CardRuling[rulings.Count];

			for ( int index = 0; index < rulings.Count; index++ )
				result[index] = CopyRuling( rulings[index] );

			return result;
		}
	}


	public static bool ContainsCard( Guid scryfallId )
	{
		lock ( StateLock )
			return GetOpenState().RecordIdByScryfallId.ContainsKey( scryfallId );
	}


	internal static bool TryGetRecordId( Guid scryfallId, out int recordId )
	{
		lock ( StateLock )
			return GetOpenState().RecordIdByScryfallId.TryGetValue( scryfallId, out recordId );
	}


	private static NormalizedCard[] ReadCards( int[] recordIds )
	{
		NormalizedCard[] cards = new NormalizedCard[recordIds.Length];

		for ( int index = 0; index < recordIds.Length; index++ )
			cards[index] = GetCard( recordIds[index] );

		return cards;
	}


	private static string CreatePrintingKey( string setCode, string collectorNumber )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( setCode );
		ArgumentException.ThrowIfNullOrWhiteSpace( collectorNumber );

		return $"{setCode.Trim()}\u001F{collectorNumber.Trim()}";
	}


	private static string NormalizeLookupName( string name )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( name );

		int    newline   = name.IndexOfAny( [ '\r', '\n' ] );
		string firstLine = newline < 0? name : name[..newline];

		return firstLine.Trim();
	}


	private static void ValidateLookupRecordId( int recordId, int recordCount, string mappingKind )
	{
		if ( recordId < 0 || recordId >= recordCount )
			throw new InvalidDataException( $"Invalid record ID {recordId} in {mappingKind} mapping." );
	}


	public static void Shutdown()
	{
		DatabaseState? state;

		lock ( StateLock )
		{
			if ( _leaseCount != 0 )
				throw new InvalidOperationException( "Cannot shut down the card database while it has " + $"{_leaseCount} active lease(s)." );

			state  = _state;
			_state = null;
		}

		state?.Dispose();
	}


	private static void ReleaseLease()
	{
		DatabaseState? state = null;

		lock ( StateLock )
		{
			if ( _leaseCount <= 0 )
				throw new InvalidOperationException( "Card database lease count is already zero." );

			_leaseCount--;

			if ( _leaseCount == 0 )
			{
				state  = _state;
				_state = null;
			}
		}

		state?.Dispose();
	}


	private static CardIndexEntry GetIndexEntry( DatabaseState state, int recordId )
	{
		if ( recordId < 0 || recordId >= state.Entries.Length )
			throw new ArgumentOutOfRangeException( nameof(recordId), recordId, $"Record ID must be between 0 and " + $"{state.Entries.Length - 1}." );

		return state.Entries[recordId];
	}


	/// <summary>
	///     Must be called while holding StateLock.
	/// </summary>
	private static byte[] ReadRecordBytes( DatabaseState state, CardIndexEntry entry )
	{
		byte[] bytes = new byte[entry.Length];

		state.CardDataStream.Seek( entry.Offset, SeekOrigin.Begin );
		ReadExactly( state.CardDataStream, bytes );

		return bytes;
	}


	/// <summary>
	///     Must be called while holding StateLock.
	/// </summary>
	private static DatabaseState GetOpenState()
	{
		return _state ?? throw new InvalidOperationException( "Card database is not open. " + "Call CardDatabase.Acquire() or Initialize() first." );
	}


	private static NormalizedCard DeserializeCard( byte[] bytes, int recordId, DatabaseState state )
	{
		NormalizedCard? card = JsonSerializer.Deserialize<NormalizedCard>( bytes, state.FormatVersion <= 3? DatabaseFileInfo.LegacyDatabaseJsonOptions : DatabaseFileInfo.DatabaseJsonOptions );

		if ( card is null )
			throw new InvalidDataException( $"Record {recordId} deserialized to null." );

		ValidateNormalizedCard( card, recordId, state );

		return card;
	}


	private static CardSymbolDefinition CopySymbolDefinition( CardSymbolDefinition definition )
	{
		return definition with { GathererAlternates = definition.GathererAlternates is null? null : [ .. definition.GathererAlternates ], SourceExtensions = CopyExtensions( definition.SourceExtensions ) };
	}


	private static CardSetDefinition CopySetDefinition( CardSetDefinition definition )
	{
		return definition with { SourceExtensions = CopyExtensions( definition.SourceExtensions ) };
	}


	private static CardRuling CopyRuling( CardRuling ruling )
	{
		return ruling with { SourceExtensions = CopyExtensions( ruling.SourceExtensions ) };
	}


	private static Dictionary<string, JsonElement> CopyExtensions( Dictionary<string, JsonElement>? source )
	{
		return source is null? [ ] : new Dictionary<string, JsonElement>( source, source.Comparer );
	}


	private static void ValidateNormalizedCard( NormalizedCard card, int recordId, DatabaseState state )
	{
		if ( card.Gameplay is null )
			throw new InvalidDataException( $"Record {recordId} has null gameplay data." );

		if ( card.Presentation is null )
			throw new InvalidDataException( $"Record {recordId} has null presentation data." );

		if ( card.Identifiers is null )
			throw new InvalidDataException( $"Record {recordId} has null identifier data." );

		if ( card.Set is null )
			throw new InvalidDataException( $"Record {recordId} has null set data." );

		if ( card.Links is null )
			throw new InvalidDataException( $"Record {recordId} has null resource links." );

		if ( card.Source is null )
			throw new InvalidDataException( $"Record {recordId} has null source metadata." );

		if ( card.Gameplay.ScryfallId == Guid.Empty )
			throw new InvalidDataException( $"Record {recordId} has an empty Scryfall ID." );

		if ( string.IsNullOrWhiteSpace( card.Gameplay.Name ) )
			throw new InvalidDataException( $"Record {recordId} has no card name." );

		if ( !Enum.IsDefined( card.Gameplay.Layout ) || !Enum.IsDefined( card.Presentation.BorderColor ) || !Enum.IsDefined( card.Presentation.Frame ) || !Enum.IsDefined( card.Presentation.Rarity ) )
			throw new InvalidDataException( $"Record {recordId} contains an undefined domain enum." );

		if ( !string.Equals( card.Source.Object, "card", StringComparison.Ordinal ) )
			throw new InvalidDataException( $"Record {recordId} has source object type " + $"'{card.Source.Object}' instead of 'card'." );

		if ( card.Set.Id == Guid.Empty || string.IsNullOrWhiteSpace( card.Set.Code ) )
			throw new InvalidDataException( $"Record {recordId} has an invalid set reference." );

		if ( !state.SetDefinitionById.TryGetValue( card.Set.Id, out CardSetDefinition? setDefinition ) || !string.Equals( card.Set.Code, setDefinition.Code, StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidDataException( $"Record {recordId} references unresolved set " + $"'{card.Set.Id}' with code '{card.Set.Code}'." );

		if ( card.Presentation.Finishes is null )
			throw new InvalidDataException( $"Record {recordId} has null finishes." );

		foreach ( CardFinish finish in card.Presentation.Finishes )
		{
			if ( !Enum.IsDefined( finish ) )
				throw new InvalidDataException( $"Record {recordId} contains undefined finish " + $"'{finish}'." );
		}

		if ( card.Presentation.FrameEffects is not null )
		{
			foreach ( FrameEffect effect in card.Presentation.FrameEffects )
			{
				if ( !Enum.IsDefined( effect ) )
					throw new InvalidDataException( $"Record {recordId} contains undefined frame " + $"effect '{effect}'." );
			}
		}

		if ( card.Gameplay.Legalities is null )
			throw new InvalidDataException( $"Record {recordId} has null format legalities." );

		foreach ( KeyValuePair<string, CardLegality> legality in card.Gameplay.Legalities.ByFormat )
		{
			if ( string.IsNullOrWhiteSpace( legality.Key ) || !Enum.IsDefined( legality.Value ) )
				throw new InvalidDataException( $"Record {recordId} contains invalid format legality " + $"'{legality.Key}' = '{legality.Value}'." );
		}

		if ( card.Gameplay.Faces is not { Length: > 0 } faces )
			throw new InvalidDataException( $"Record {recordId} has no card faces." );

		for ( int index = 0; index < faces.Length; index++ )
		{
			if ( faces[index] is null )
				throw new InvalidDataException( $"Record {recordId}, face {index} is null." );

			if ( faces[index].ManaCost is null )
				throw new InvalidDataException( $"Record {recordId}, face {index} has a null mana cost." );

			if ( string.IsNullOrWhiteSpace( faces[index].Name ) )
				throw new InvalidDataException( $"Record {recordId}, face {index} has no name." );

			if ( !string.Equals( faces[index].Object, "card_face", StringComparison.Ordinal ) )
				throw new InvalidDataException( $"Record {recordId}, face {index} has object type " + $"'{faces[index].Object}' instead of 'card_face'." );
		}
	}


	private static CardIndexFile ReadIndexFile()
	{
		return ReadVersionedFile<CardIndexFile>( DatabaseFileInfo.CardIndexFile, "card database" );
	}


	private static CardSymbolDefinitionFile ReadSymbolDefinitionFile()
	{
		return ReadVersionedFile<CardSymbolDefinitionFile>( DatabaseFileInfo.SymbolDefinitionsFile, "symbol-definition" );
	}


	private static CardSetDefinitionFile ReadSetDefinitionFile()
	{
		return ReadVersionedFile<CardSetDefinitionFile>( DatabaseFileInfo.SetDefinitionsFile, "set-definition" );
	}


	private static CardRulingFile ReadRulingFile()
	{
		return ReadVersionedFile<CardRulingFile>( DatabaseFileInfo.RulingsFile, "ruling" );
	}


	private static T ReadVersionedFile<T>( string path, string fileDescription ) where T : class
	{
		using Stream       input    = FileSystem.Data.OpenRead( path );
		using JsonDocument document = JsonDocument.Parse( input );

		if ( document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty( nameof(CardIndexFile.FormatVersion), out JsonElement versionElement ) || !versionElement.TryGetInt32( out int formatVersion ) )
			throw new InvalidDataException( $"The {fileDescription} file has no valid format version." );

		ValidateFormatVersion( formatVersion, fileDescription );

		JsonSerializerOptions options = formatVersion <= 3? DatabaseFileInfo.LegacyDatabaseJsonOptions : DatabaseFileInfo.DatabaseJsonOptions;

		T? value = document.RootElement.Deserialize<T>( options );

		if ( value is null )
			throw new InvalidDataException( $"Could not deserialize the {fileDescription} file." );

		return value;
	}


	private static void ValidateFormatVersion( int actual, string fileDescription )
	{
		if ( actual < DatabaseFileInfo.OldestReadableFormatVersion || actual > DatabaseFileInfo.CurrentFormatVersion )
			throw new InvalidDataException( $"Unsupported {fileDescription} version: {actual}. " + $"Supported versions are " + $"{DatabaseFileInfo.OldestReadableFormatVersion} through " + $"{DatabaseFileInfo.CurrentFormatVersion}." );
	}


	private static void ReadExactly( Stream stream, byte[] buffer )
	{
		int totalRead = 0;

		while ( totalRead < buffer.Length )
		{
			int bytesRead = stream.Read( buffer, totalRead, buffer.Length - totalRead );

			if ( bytesRead == 0 )
				throw new EndOfStreamException( "Reached the end of cards.dat before reading " + "the complete card." );

			totalRead += bytesRead;
		}
	}


	private sealed class DatabaseState : IDisposable
	{
		public required int                                                FormatVersion        { get; init; }
		public required Stream                                             CardDataStream       { get; init; }
		public required CardIndexEntry[]                                   Entries              { get; init; }
		public required Dictionary<Guid, int>                              RecordIdByScryfallId { get; init; }
		public required Dictionary<string, int>                            RecordIdByPrinting   { get; init; }
		public required Dictionary<string, List<int>>                      RecordIdsByName      { get; init; }
		public required Dictionary<Guid, List<int>>                        RecordIdsByOracleId  { get; init; }
		public required Dictionary<SymbolIdentifier, CardSymbolDefinition> SymbolDefinitionById { get; init; }
		public required Dictionary<Guid, CardSetDefinition>                SetDefinitionById    { get; init; }
		public required Dictionary<string, CardSetDefinition>              SetDefinitionByCode  { get; init; }
		public required Dictionary<Guid, List<CardRuling>>                 RulingsByOracleId    { get; init; }
		public required int                                                RulingCount          { get; init; }


		public void Dispose()
		{
			CardDataStream.Dispose();
		}
	}

	private sealed class DatabaseLease : IDisposable
	{
		private int _disposed;


		public void Dispose()
		{
			if ( Interlocked.Exchange( ref _disposed, 1 ) == 0 )
				ReleaseLease();
		}
	}
}
