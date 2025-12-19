#nullable enable
using System.Collections.Concurrent;
using System.Threading.Tasks;

/// <summary>
/// Global static storage for card indexes with build state tracking.
/// Prevents duplicate builds and provides detailed status per index.
/// </summary>
public static class CardIndexStore
{
	private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<Guid, Card>> _indexes = new();
	private static readonly ConcurrentDictionary<string, AsyncGate> _buildGates = new();

	/// <summary>
	/// Get an index if it has already been built, otherwise build and cache it.
	/// </summary>
	public static async Task<IReadOnlyDictionary<Guid, Card>> GetOrBuildAsync(
		string key,
		Func<Task<IReadOnlyDictionary<Guid, Card>>> builder
	)
	{
		// Fast path: already built
		if ( _indexes.TryGetValue( key, out var existingIndex ) )
			return existingIndex;

		// Get or create gate for this key
		var gate = _buildGates.GetOrAdd( key, _ => new AsyncGate() );

		// If already building, just wait
		if ( gate.IsCompleted )
		{
			// Build finished (success or failure)
			if ( gate.IsSucceeded && _indexes.TryGetValue( key, out var cached ) )
				return cached;

			// Failed previously - remove gate so we can retry
			_buildGates.TryRemove( key, out _ );
			gate = _buildGates.GetOrAdd( key, _ => new AsyncGate() );
		}

		// Start build if not already running
		if ( !gate.IsCompleted )
		{
			gate.Run( async () => await BuildAndStoreAsync( key, builder ) );
		}

		// Wait for build to complete
		await gate.WhenReady;

		// Return the result (or throw if failed)
		if ( gate.IsSucceeded && _indexes.TryGetValue( key, out var result ) )
			return result;

		// Build failed
		throw gate.Error ?? new Exception( $"Failed to build index '{key}'" );
	}

	/// <summary>
	/// Get or build with TaskSource for cancellation support.
	/// </summary>
	public static async Task<IReadOnlyDictionary<Guid, Card>> GetOrBuildAsync(
		string key,
		TaskSource ts,
		Func<Task<IReadOnlyDictionary<Guid, Card>>> builder
	)
	{
		// Fast path: already built
		if ( _indexes.TryGetValue( key, out var existingIndex ) )
			return existingIndex;

		// Get or create gate for this key
		var gate = _buildGates.GetOrAdd( key, _ => new AsyncGate() );

		// If already building, just wait
		if ( gate.IsCompleted )
		{
			if ( gate.IsSucceeded && _indexes.TryGetValue( key, out var cached ) )
				return cached;

			_buildGates.TryRemove( key, out _ );
			gate = _buildGates.GetOrAdd( key, _ => new AsyncGate() );
		}

		// Start build if not already running
		if ( !gate.IsCompleted )
		{
			gate.Run( ts, async () => await BuildAndStoreAsync( key, builder ) );
		}

		// Wait for build to complete
		await gate.WhenReady;

		if ( gate.IsSucceeded && _indexes.TryGetValue( key, out var result ) )
			return result;

		throw gate.Error ?? new Exception( $"Failed to build index '{key}'" );
	}

	/// <summary>
	/// Force a rebuild of the given index, replacing the cached copy.
	/// </summary>
	public static async Task<IReadOnlyDictionary<Guid, Card>> RebuildAsync(
		string key,
		Func<Task<IReadOnlyDictionary<Guid, Card>>> builder
	)
	{
		// Clear existing
		_indexes.TryRemove( key, out _ );
		_buildGates.TryRemove( key, out _ );

		// Build fresh
		var gate = new AsyncGate();
		_buildGates[key] = gate;

		gate.Run( async () => await BuildAndStoreAsync( key, builder ) );
		await gate.WhenReady;

		if ( gate.IsSucceeded && _indexes.TryGetValue( key, out var result ) )
			return result;

		throw gate.Error ?? new Exception( $"Failed to rebuild index '{key}'" );
	}

	/// <summary>
	/// Force rebuild with TaskSource for cancellation support.
	/// </summary>
	public static async Task<IReadOnlyDictionary<Guid, Card>> RebuildAsync(
		string key,
		TaskSource ts,
		Func<Task<IReadOnlyDictionary<Guid, Card>>> builder
	)
	{
		_indexes.TryRemove( key, out _ );
		_buildGates.TryRemove( key, out _ );

		var gate = new AsyncGate();
		_buildGates[key] = gate;

		gate.Run( ts, async () => await BuildAndStoreAsync( key, builder ) );
		await gate.WhenReady;

		if ( gate.IsSucceeded && _indexes.TryGetValue( key, out var result ) )
			return result;

		throw gate.Error ?? new Exception( $"Failed to rebuild index '{key}'" );
	}

	/// <summary>
	/// Check if an index is currently being built.
	/// </summary>
	public static bool IsBuilding( string key )
	{
		return _buildGates.TryGetValue( key, out var gate ) && !gate.IsCompleted;
	}

	/// <summary>
	/// Check if an index is ready to use.
	/// </summary>
	public static bool IsReady( string key )
	{
		return _indexes.ContainsKey( key );
	}

	/// <summary>
	/// Check if an index build failed.
	/// </summary>
	public static bool HasFailed( string key )
	{
		return _buildGates.TryGetValue( key, out var gate ) && gate.IsFailed;
	}

	/// <summary>
	/// Get the error from a failed build, if any.
	/// </summary>
	public static Exception? GetError( string key )
	{
		return _buildGates.TryGetValue( key, out var gate ) ? gate.Error : null;
	}

	/// <summary>
	/// Get the gate for a specific index (for advanced state checking).
	/// </summary>
	public static AsyncGate? GetGate( string key )
	{
		_buildGates.TryGetValue( key, out var gate );
		return gate;
	}

	/// <summary>
	/// Try to get an already-built index without triggering a build.
	/// </summary>
	public static bool TryGet( string key, out IReadOnlyDictionary<Guid, Card>? index )
	{
		return _indexes.TryGetValue( key, out index );
	}

	/// <summary>
	/// Clear an individual cached index so it will be rebuilt on next request.
	/// </summary>
	public static void Clear( string key )
	{
		_indexes.TryRemove( key, out _ );
		_buildGates.TryRemove( key, out _ );
	}

	/// <summary>
	/// Clear all cached indexes.
	/// </summary>
	public static void ClearAll()
	{
		_indexes.Clear();
		_buildGates.Clear();
	}

	private static async Task BuildAndStoreAsync(
		string key,
		Func<Task<IReadOnlyDictionary<Guid, Card>>> builder
	)
	{
		var builtIndex = await builder();
		_indexes[key] = builtIndex;
		Log.Info( $"[CardIndexStore] Cached index '{key}' with {builtIndex.Count} cards" );
	}
}
