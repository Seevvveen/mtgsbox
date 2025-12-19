#nullable enable
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// Takes a group of Card Objects and compiles them into an index for fast lookup
/// </summary>
public sealed class CardIndexBuilder
{
	private readonly AsyncGate _indexGate = new();
	private IReadOnlyDictionary<Guid, Card> _index = new Dictionary<Guid, Card>();

	public Task WhenReady => _indexGate.WhenReady;
	public bool IsReady => _indexGate.IsSucceeded;
	public bool HasFailed => _indexGate.IsFailed;
	public Exception? Error => _indexGate.Error;

	/// <summary>
	/// The indexed cards. Empty until indexing completes.
	/// </summary>
	public IReadOnlyDictionary<Guid, Card> Index => _index;

	/// <summary>
	/// Start building the index from a large file. Fire and forget.
	/// </summary>
	public void BuildFromFile( string file )
	{
		_indexGate.RunInThread(
			() => BuildIndexFromFile( file ),
			result => _index = result
		);
	}

	/// <summary>
	/// Start building the index with TaskSource for cancellation support.
	/// </summary>
	public void BuildFromFile( TaskSource ts, string file )
	{
		_indexGate.RunInThread(
			ts,
			() => BuildIndexFromFile( file ),
			result => _index = result
		);
	}

	/// <summary>
	/// Build and immediately return the index (awaitable version).
	/// </summary>
	public async Task<IReadOnlyDictionary<Guid, Card>> BuildFromFileAsync( string file )
	{
		await _indexGate.RunInThreadAsync(
			() => BuildIndexFromFile( file ),
			result => _index = result
		);
		return _index;
	}

	/// <summary>
	/// Build with TaskSource and return the index (awaitable version).
	/// </summary>
	public async Task<IReadOnlyDictionary<Guid, Card>> BuildFromFileAsync( TaskSource ts, string file )
	{
		await _indexGate.RunInThreadAsync(
			ts,
			() => BuildIndexFromFile( file ),
			result => _index = result
		);
		return _index;
	}

	/// <summary>
	/// Core indexing logic - runs on worker thread
	/// </summary>
	private IReadOnlyDictionary<Guid, Card> BuildIndexFromFile( string file )
	{
		if ( !FileSystem.Data.FileExists( file ) )
		{
			Log.Warning( $"[CardIndexBuilder] File not found: {file}" );
			return new Dictionary<Guid, Card>();
		}

		var json = FileSystem.Data.ReadAllText( file );
		if ( string.IsNullOrWhiteSpace( json ) )
		{
			Log.Warning( $"[CardIndexBuilder] File is empty: {file}" );
			return new Dictionary<Guid, Card>();
		}

		var cards = JsonSerializer.Deserialize<List<Card>>( json );
		if ( cards == null || cards.Count == 0 )
		{
			Log.Warning( $"[CardIndexBuilder] No cards in: {file}" );
			return new Dictionary<Guid, Card>();
		}

		var dict = new Dictionary<Guid, Card>( capacity: cards.Count );
		foreach ( var card in cards )
		{
			if ( card?.Id == null || card.Id == Guid.Empty )
				continue;
			dict[card.Id] = card;
		}

		Log.Info( $"[CardIndexBuilder] Loaded {dict.Count} cards from {file}" );
		return dict;
	}

	/// <summary>
	/// Reset the builder to index a new file
	/// </summary>
	public void Reset()
	{
		_indexGate.Reset();
		_index = new Dictionary<Guid, Card>();
	}
}
