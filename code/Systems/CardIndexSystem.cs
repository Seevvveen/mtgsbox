#nullable enable

using System.Threading.Tasks;

/// <summary>
/// Assemble a dictionary from a set of cards
/// </summary>
public sealed class CardIndexSystem : GameObjectSystem<CardIndexSystem>, ISceneStartup
{
	// Composition
	private BulkCacheSystem? _bulkCacheSystem;
	private readonly CardIndexBuilder _cardIndexBuilder = new();

	private const string OracleIndexKey = "oracle_cards.json";
	private const string DefaultIndexKey = "default_cards.json";

	// Ready Signal
	private readonly AsyncGate _readyGate = new();
	public Task WhenReady => _readyGate.WhenReady;
	public bool IsReady => _readyGate.IsSucceeded;
	public bool HasFailed => _readyGate.IsFailed;
	public Exception? Error => _readyGate.Error;

	private TaskSource _taskSource;

	// Card Dictionaries
	public IReadOnlyDictionary<Guid, Card> OracleDictionary { get; private set; } =
		new Dictionary<Guid, Card>();
	public IReadOnlyDictionary<Guid, Card> DefaultCardsDictionary { get; private set; } =
		new Dictionary<Guid, Card>();

	public CardIndexSystem( Scene scene ) : base( scene )
	{
		_taskSource = TaskSource.Create();
	}

	/// <summary>
	/// Orchestrate Building Dictionaries
	/// </summary>
	void ISceneStartup.OnHostInitialize()
	{
		// Fire and forget - gate handles all signaling
		_readyGate.Run( _taskSource, InitializeAsync );
	}

	private async Task InitializeAsync()
	{
		_bulkCacheSystem = BulkCacheSystem.Current;

		Log.Info( "[CardIndexSystem] Waiting for Bulk System" );
		await _bulkCacheSystem.WhenReady;

		if ( !_bulkCacheSystem.IsReady )
		{
			throw new Exception( "BulkCacheSystem not ready after WhenReady" );
		}

		// Load card indexes with TaskSource for cancellation support
		if ( FileSystem.Data.FileExists( OracleIndexKey ) )
		{
			OracleDictionary = await CardIndexStore.GetOrBuildAsync(
				OracleIndexKey,
				_taskSource,
				() => _cardIndexBuilder.BuildFromFileAsync( _taskSource, OracleIndexKey )
			);
			Log.Info( $"[CardIndexSystem] Loaded {OracleDictionary.Count} oracle cards" );
		}

		if ( FileSystem.Data.FileExists( DefaultIndexKey ) )
		{
			DefaultCardsDictionary = await CardIndexStore.GetOrBuildAsync(
				DefaultIndexKey,
				_taskSource,
				() => _cardIndexBuilder.BuildFromFileAsync( _taskSource, DefaultIndexKey )
			);
			Log.Info( $"[CardIndexSystem] Loaded {DefaultCardsDictionary.Count} default cards" );
		}

		// Ensure we have at least one index
		if ( OracleDictionary.Count == 0 && DefaultCardsDictionary.Count == 0 )
		{
			throw new Exception( "No card indexes loaded - both oracle and default are empty" );
		}

		Log.Info( "[CardIndexSystem] Ready" );
	}

	// ========== CARD LOOKUP METHODS ==========

	public Card? GetCard( Guid id )
	{
		return DefaultCardsDictionary.TryGetValue( id, out var card ) ? card : null;
	}

	public Card? GetCard( string id )
	{
		if ( !Guid.TryParse( id, out var guid ) )
		{
			Log.Warning( $"[CardIndexSystem] Invalid GUID format: {id}" );
			return null;
		}
		return GetCard( guid );
	}

	public Card? GetCardOrNull( Guid id )
	{
		return OracleDictionary.TryGetValue( id, out var card ) ? card : null;
	}

	public Card GetCardOrDefault( Guid id, Card defaultCard )
	{
		return OracleDictionary.TryGetValue( id, out var card ) ? card : defaultCard;
	}

	public bool HasCard( Guid id )
	{
		return OracleDictionary.ContainsKey( id );
	}

	public IEnumerable<Card> GetCards( IEnumerable<Guid> ids )
	{
		foreach ( var id in ids )
		{
			if ( OracleDictionary.TryGetValue( id, out var card ) )
				yield return card;
		}
	}

	public List<Card> GetCardsAsList( IEnumerable<Guid> ids )
	{
		var result = new List<Card>();
		foreach ( var id in ids )
		{
			if ( OracleDictionary.TryGetValue( id, out var card ) )
				result.Add( card );
		}
		return result;
	}

	public IEnumerable<Card> FindCards( Func<Card, bool> predicate )
	{
		return OracleDictionary.Values.Where( predicate );
	}

	public IEnumerable<Card> GetAllCards()
	{
		return OracleDictionary.Values;
	}

	// ========== INDEX MANAGEMENT ==========

	/// <summary>
	/// Rebuild the oracle index and update the cached copy.
	/// </summary>
	public async Task<IReadOnlyDictionary<Guid, Card>> RebuildOracleIndex()
	{
		OracleDictionary = await CardIndexStore.RebuildAsync(
			OracleIndexKey,
			_taskSource,
			() => _cardIndexBuilder.BuildFromFileAsync( _taskSource, OracleIndexKey )
		);

		Log.Info( $"[CardIndexSystem] Rebuilt oracle index: {OracleDictionary.Count} cards" );
		return OracleDictionary;
	}

	/// <summary>
	/// Rebuild the default card index and update the cached copy.
	/// </summary>
	public async Task<IReadOnlyDictionary<Guid, Card>> RebuildDefaultIndex()
	{
		DefaultCardsDictionary = await CardIndexStore.RebuildAsync(
			DefaultIndexKey,
			_taskSource,
			() => _cardIndexBuilder.BuildFromFileAsync( _taskSource, DefaultIndexKey )
		);

		Log.Info( $"[CardIndexSystem] Rebuilt default index: {DefaultCardsDictionary.Count} cards" );
		return DefaultCardsDictionary;
	}

	/// <summary>
	/// Clear all cached card indexes so they rebuild on next load.
	/// </summary>
	public void ClearIndexes()
	{
		CardIndexStore.Clear( OracleIndexKey );
		CardIndexStore.Clear( DefaultIndexKey );

		OracleDictionary = new Dictionary<Guid, Card>();
		DefaultCardsDictionary = new Dictionary<Guid, Card>();

		Log.Info( "[CardIndexSystem] Cleared all indexes" );
	}
}
