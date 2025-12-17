using Sandbox.Scryfall;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Write Bulk Api responses into Filesystem.Data at scene startup
/// </summary>
public sealed class BulkCacheSystem : GameObjectSystem, ISceneStartup
{
	//Names
	private const string BulkIndexFileName = "ScryfallBulkResponse.json";
	private const string OracleCardsFileName = "oracle_cards.json";
	private const string DefaultCardsFileName = "default_cards.json";
	//Limits
	private const long MaxFileSizeBytes = 2_000_000_000;
	private const int MaxConcurrentDownloads = 2;
	//Objects
	private readonly ScryfallClient _apiClient = new();
	public ApiList<BulkItem> BulkIndex { get; private set; }
	//Signals
	private readonly TaskCompletionSource<bool> _SignalReady = new();
	public Task WhenReady => _SignalReady.Task;
	public bool IsReady { get; private set; }
	private TaskSource _SignalLife;


	//ctor
	public BulkCacheSystem( Scene scene ) : base( scene ) { }

	// Refactor When Multiplayer
	// Main System
	async void ISceneStartup.OnHostInitialize()
	{
		try
		{
			//Init
			await LoadOrRefreshBulkIndexAsync();
			await DownloadMissingFileAsync();

			bool hasCards = FileSystem.Data.FileExists( OracleCardsFileName )
				|| FileSystem.Data.FileExists( DefaultCardsFileName );

			if ( !hasCards )
			{
				Log.Error( "[BulkCache] No Cards found download" );
				IsReady = false;
			}
			else
			{
				IsReady = true;
				Log.Info( "[BulkCache] Ready" );
			}
		}
		catch ( Exception ex )
		{
			//Failed Init
			Log.Error( ex, "[BulkCacheSystem] Bulk Cache System Failed" );
		}
		finally
		{
			_SignalReady.TrySetResult( IsReady );
		}
	}


	private async Task LoadOrRefreshBulkIndexAsync()
	{
		// Try local cache first
		var cached = FileSystem.Data.ReadJsonOrDefault<ApiList<BulkItem>>( BulkIndexFileName );

		if ( cached?.GetFirstOrDefault()?.IsUpdateNeeded() != false )
		{
			// Cache missing, empty, or stale - fetch fresh
			Log.Info( "[BulkCache] Fetching new bulk index..." );
			BulkIndex = await _apiClient.FetchAsync<ApiList<BulkItem>>( "bulk-data" );
			FileSystem.Data.WriteJson( BulkIndexFileName, BulkIndex );
		}
		else
		{
			// Cache is good
			BulkIndex = cached;
			//Log.Info( "[BulkCache] Using cached index." );
		}
	}

	private async Task DownloadMissingFileAsync()
	{
		if ( BulkIndex?.Data == null ) return;

		//Build List of what we dont have
		var toDownload = BulkIndex.Data
			.Where( item =>
				!string.IsNullOrWhiteSpace( item.Type ) &&
				!string.IsNullOrWhiteSpace( item.DownloadUri ) &&
				item.Size <= MaxFileSizeBytes &&
				!FileSystem.Data.FileExists( $"{item.Type}.json" )
			).ToList();

		if ( !toDownload.Any() )
			return;

		//Downloading
		//Parrallel downloading with Semaphore
		using var semaphore = new SemaphoreSlim( MaxConcurrentDownloads );
		var tasks = toDownload.Select( async item =>
			{
				await semaphore.WaitAsync();
				try { await DownloadFileAsync( item ); }
				finally { semaphore.Release(); }
			}
		);
		await _SignalLife.WhenAll( tasks );
	}

	private async Task DownloadFileAsync( BulkItem item )
	{
		var filename = $"{item.Type}.json";
		var tempFile = $"{filename}.tmp";

		try
		{
			Log.Info( "[BulkCache] Downloading {item.Type}" );

			using var stream = await _apiClient.FetchStreamAsync( item.DownloadUri );
			using var fileStream = FileSystem.Data.OpenWrite( tempFile );

			await stream.CopyToAsync( fileStream );
			await fileStream.FlushAsync();

			if ( FileSystem.Data.FileSize( tempFile ) == 0 )
				throw new Exception( "[BulkCache] Empty Downloaded File" );

			if ( FileSystem.Data.FileExists( filename ) )
				FileSystem.Data.DeleteFile( filename );

			var data = FileSystem.Data.ReadAllBytes( tempFile );
			FileSystem.Data.WriteAllBytes( filename, data.ToArray() );
			FileSystem.Data.DeleteFile( tempFile );


		}
		catch ( Exception ex )
		{
			Log.Error( ex, $"[BulkCache] Failed Download on {item.Type}" );

			if ( FileSystem.Data.FileExists( tempFile ) )
				FileSystem.Data.DeleteFile( tempFile );

			throw;
		}

	}




}
