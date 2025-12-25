using System.Threading.Tasks;

/// <summary>
/// Manages card data dictionaries with async initialization.
/// Contract: Provides non-nullable cards or throws descriptive exceptions.
/// </summary>
public sealed class CardIndexSystem : GameObjectSystem<CardIndexSystem>, ISceneStartup
{
	private BulkCacheSystem? _bulkCacheSystem;
	private readonly CardIndexBuilder _cardIndexBuilder = new();

	private const string OracleIndexKey = "oracle_cards.json";
	private const string DefaultIndexKey = "default_cards.json";

	// Ready Signal
	private readonly AsyncGate _readyGate = new();
	public Task WhenReady => _readyGate.WhenReady;
	public bool IsReady => _readyGate.IsSucceeded;

	private TaskSource _taskSource;

	// Single source of truth - use DefaultCards for gameplay
	public IReadOnlyDictionary<Guid, Card> Cards { get; private set; } =
		new Dictionary<Guid, Card>();

	// Oracle for reference/lookup only
	public IReadOnlyDictionary<Guid, Card> OracleCards { get; private set; } =
		new Dictionary<Guid, Card>();

	public CardIndexSystem( Scene scene ) : base( scene )
	{
		_taskSource = TaskSource.Create();
	}

	void ISceneStartup.OnHostInitialize()
	{
		_readyGate.Run( _taskSource, InitializeAsync );
	}

	private async Task InitializeAsync()
	{
		_bulkCacheSystem = BulkCacheSystem.Current;
		await _bulkCacheSystem.WhenReady;

		// Load oracle (full database)
		if ( FileSystem.Data.FileExists( OracleIndexKey ) )
		{
			OracleCards = await CardIndexStore.GetOrBuildAsync(
				OracleIndexKey,
				_taskSource,
				() => _cardIndexBuilder.BuildFromFileAsync( _taskSource, OracleIndexKey )
			);
			Log.Info( $"[CardIndexSystem] Loaded {OracleCards.Count} oracle cards" );
		}

		// Load default (gameplay subset)
		if ( FileSystem.Data.FileExists( DefaultIndexKey ) )
		{
			Cards = await CardIndexStore.GetOrBuildAsync(
				DefaultIndexKey,
				_taskSource,
				() => _cardIndexBuilder.BuildFromFileAsync( _taskSource, DefaultIndexKey )
			);
			Log.Info( $"[CardIndexSystem] Loaded {Cards.Count} gameplay cards" );
		}

		// Fail fast if no data
		if ( Cards.Count == 0 )
			throw new InvalidOperationException(
				"No gameplay cards loaded - check default_cards.json exists" );

		Log.Info( "[CardIndexSystem] Ready" );
	}

	// ========== PRIMARY API (Non-nullable, fail-fast) ==========

	/// <summary>
	/// Get a gameplay card by ID. Throws if not found.
	/// </summary>
	public Card GetCard( Guid id )
	{
		if ( Cards.TryGetValue( id, out var card ) )
			return card;

		throw new KeyNotFoundException(
			$"Card {id} not found in gameplay cards (loaded: {Cards.Count})" );
	}

	/// <summary>
	/// Get a gameplay card by string ID. Throws if invalid format or not found.
	/// </summary>
	public Card GetCard( string id )
	{
		if ( !Guid.TryParse( id, out var guid ) )
			throw new ArgumentException( $"Invalid card ID format: '{id}'", nameof( id ) );

		return GetCard( guid );
	}

	/// <summary>
	/// Get a random gameplay card.
	/// </summary>
	public Card GetRandomCard()
	{
		var keys = Cards.Keys.ToArray();
		var randomKey = keys[Random.Shared.Next( keys.Length )];
		return Cards[randomKey];
	}

	// ========== OPTIONAL SAFE API (If you need null checks) ==========

	/// <summary>
	/// Try to get a card. Returns false if not found.
	/// Use this if missing cards are expected/valid.
	/// </summary>
	public bool TryGetCard( Guid id, out Card card )
	{
		return Cards.TryGetValue( id, out card );
	}

	/// <summary>
	/// Check if a card exists without retrieving it.
	/// </summary>
	public bool HasCard( Guid id ) => Cards.ContainsKey( id );

	// ========== BULK OPERATIONS ==========

	public IEnumerable<Card> GetCards( IEnumerable<Guid> ids )
	{
		foreach ( var id in ids )
			if ( Cards.TryGetValue( id, out var card ) )
				yield return card;
	}

	public IEnumerable<Card> FindCards( Func<Card, bool> predicate )
	{
		return Cards.Values.Where( predicate );
	}

	public IEnumerable<Card> GetAllCards() => Cards.Values;

	// ========== INDEX MANAGEMENT ==========

	public async Task RebuildIndexes()
	{
		OracleCards = await CardIndexStore.RebuildAsync(
			OracleIndexKey,
			_taskSource,
			() => _cardIndexBuilder.BuildFromFileAsync( _taskSource, OracleIndexKey )
		);

		Cards = await CardIndexStore.RebuildAsync(
			DefaultIndexKey,
			_taskSource,
			() => _cardIndexBuilder.BuildFromFileAsync( _taskSource, DefaultIndexKey )
		);

		Log.Info( $"[CardIndexSystem] Rebuilt indexes: {Cards.Count} gameplay, {OracleCards.Count} oracle" );
	}

	public void ClearIndexes()
	{
		CardIndexStore.Clear( OracleIndexKey );
		CardIndexStore.Clear( DefaultIndexKey );
		Cards = new Dictionary<Guid, Card>();
		OracleCards = new Dictionary<Guid, Card>();
		Log.Info( "[CardIndexSystem] Cleared all indexes" );
	}
}
