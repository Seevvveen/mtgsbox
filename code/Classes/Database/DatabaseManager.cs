#nullable enable

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RuntimeCardDatabase = Sandbox.Classes.Database.CardDatabase;

namespace Sandbox.Classes.Database;

public enum DatabaseStartupState
{
	NotStarted,
	Opening,
	Provisioning,
	Ready,
	Failed,
	Stopped
}

public enum DatabaseFailureKind
{
	None,
	SourceCompatibility,
	SourceCorrupt,
	Network,
	GenerationMismatch,
	Unknown
}

/// <summary>
///     Owns the local card-definition database for the lifetime of this scene.
///     Existing validated data opens immediately. On a clean host install, the
///     public Scryfall source catalogs are downloaded once and built locally.
/// </summary>
public sealed class DatabaseManager : GameObjectSystem<DatabaseManager>, ISceneStartup
{
	private static readonly SemaphoreSlim                              ProvisioningGate = new SemaphoreSlim( 1, 1 );
	private readonly        TaskCompletionSource<DatabaseStartupState> _completion      = new TaskCompletionSource<DatabaseStartupState>( TaskCreationOptions.RunContinuationsAsynchronously );

	private readonly object                  _lifecycleLock        = new object();
	private readonly CancellationTokenSource _lifetimeCancellation = new CancellationTokenSource();

	private IDisposable? _databaseLease;
	private bool         _disposed;
	private Task?        _initializationTask;
	private Task?        _repairTask;
	private string?      _repairTarget;
	private int          _runId;
	private bool         _started;


	public DatabaseManager( Scene scene ) : base( scene ) { }


	public DatabaseStartupState State         { get; private set; } = DatabaseStartupState.NotStarted;
	public string               StatusMessage { get; private set; } = "Card database has not started.";

	public bool IsReady
	{
		get { return State == DatabaseStartupState.Ready && _databaseLease is not null; }
	}

	public string? FailureReason { get; private set; }
	public DatabaseFailureKind FailureKind { get; private set; }
	public string? DatabaseChecksum { get; private set; }
	public DatabaseSourceSnapshot? SourceSnapshot { get; private set; }

	public Task<DatabaseStartupState> Completion
	{
		get { return _completion.Task; }
	}


	void ISceneStartup.OnHostInitialize()
	{
		StartDatabase( allowProvisioning: true );
	}


	void ISceneStartup.OnClientInitialize()
	{
		// FileSystem.Data is deliberately game-scoped. A fresh client therefore
		// provisions its own copy, then the Match compares it with the host's
		// authoritative checksum before allowing gameplay.
		StartDatabase( allowProvisioning: true );
	}


	/// <summary>
	///     Ensures this installation matches the generation advertised by the
	///     host. One forced source refresh and rebuild is attempted. If Scryfall
	///     can no longer reproduce the host generation, the manager fails with a
	///     clear compatibility error instead of silently using different data.
	/// </summary>
	public Task EnsureHostGenerationAsync( string expectedChecksum, DatabaseSourceSnapshot? hostSource = null )
	{
		if ( string.IsNullOrWhiteSpace( expectedChecksum ) )
			throw new ArgumentException( "The host database checksum is empty.", nameof(expectedChecksum) );

		lock ( _lifecycleLock )
		{
			if ( _disposed )
				throw new ObjectDisposedException( nameof(DatabaseManager) );

			if ( IsReady && string.Equals( DatabaseChecksum, expectedChecksum, StringComparison.Ordinal ) )
				return Task.CompletedTask;

			if ( _repairTask is { IsCompleted: false } )
			{
				if ( string.Equals( _repairTarget, expectedChecksum, StringComparison.Ordinal ) )
					return _repairTask;

				throw new InvalidOperationException( "A repair for another host database generation is already in progress." );
			}

			int runId = ++_runId;
			_repairTarget = expectedChecksum;
			State = DatabaseStartupState.Provisioning;
			StatusMessage = "Synchronizing card definitions with the host.";
			FailureReason = null;
			FailureKind = DatabaseFailureKind.None;

			IDisposable? previousLease = _databaseLease;
			_databaseLease = null;
			DatabaseChecksum = null;
			previousLease?.Dispose();

			_repairTask = RepairToHostGenerationAsync( expectedChecksum, hostSource, runId, _lifetimeCancellation.Token );
			return _repairTask;
		}
	}


	public override void Dispose()
	{
		StopDatabase();
		base.Dispose();
	}


	private void StartDatabase( bool allowProvisioning )
	{
		lock ( _lifecycleLock )
		{
			if ( _started || _disposed )
				return;

			_started = true;
			int runId = ++_runId;

			State         = DatabaseStartupState.Opening;
			StatusMessage = "Opening card definitions.";

			_initializationTask = InitializeDatabaseAsync( allowProvisioning, runId, _lifetimeCancellation.Token );
		}
	}


	private async Task InitializeDatabaseAsync( bool allowProvisioning, int runId, CancellationToken cancellationToken )
	{
		IDisposable? acquiredLease = null;
		bool         gateHeld      = false;

		try
		{
			try
			{
				acquiredLease = await AcquireDatabaseAsync();
			}
			catch ( Exception openFailure ) when ( IsProvisionableDatabaseFailure( openFailure ) )
			{
				if ( !allowProvisioning )
					throw new InvalidOperationException( "This client has no usable card-definition " + "database. Ship the same validated database " + "generation with the game before joining a match.", openFailure );

				if ( !TrySetState( runId, DatabaseStartupState.Provisioning, "Preparing card definitions for the first run." ) )
					return;

				Log.Info( "No usable card-definition database was found. " + "Starting one-time first-run setup." );

				await ProvisioningGate.WaitAsync( cancellationToken );
				gateHeld = true;
				cancellationToken.ThrowIfCancellationRequested();

				// Another scene may have completed setup while this one waited.
				try
				{
					acquiredLease = await AcquireDatabaseAsync();
				}
				catch ( Exception retryFailure ) when ( IsProvisionableDatabaseFailure( retryFailure ) )
				{
					await ProvisionDatabaseAsync( runId, cancellationToken );

					cancellationToken.ThrowIfCancellationRequested();
					acquiredLease = await AcquireDatabaseAsync();
				}
			}

			string checksum = await ComputeChecksumAsync();

			await GameTask.MainThread( cancellationToken );
			cancellationToken.ThrowIfCancellationRequested();

			if ( !TryPublishLease( runId, acquiredLease, checksum, cancellationToken ) )
				return;

			acquiredLease = null;
			_completion.TrySetResult( DatabaseStartupState.Ready );
			Log.Info( "Card definitions are ready." );
		}
		catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
		{
			// Dispose owns the terminal state and completion notification.
		}
		catch ( Exception exception )
		{
			bool publishFailure;

			lock ( _lifecycleLock )
			{
				publishFailure = !_disposed && runId == _runId;

				if ( publishFailure )
				{
					FailureReason = exception.Message;
					FailureKind   = ClassifyFailure( exception );
					State         = DatabaseStartupState.Failed;
					StatusMessage = "Card definitions are unavailable.";
				}
			}

			if ( publishFailure )
			{
				Log.Error( "Card definitions are unavailable. " + $"{exception}" );
				_completion.TrySetResult( DatabaseStartupState.Failed );
			}
		}
		finally
		{
			if ( gateHeld )
				ProvisioningGate.Release();

			acquiredLease?.Dispose();
		}
	}


	private async Task RepairToHostGenerationAsync( string expectedChecksum, DatabaseSourceSnapshot? hostSource, int runId, CancellationToken cancellationToken )
	{
		IDisposable? acquiredLease = null;
		bool gateHeld = false;

		try
		{
			await ProvisioningGate.WaitAsync( cancellationToken );
			gateHeld = true;
			cancellationToken.ThrowIfCancellationRequested();

			Log.Warning( "The local card database differs from the host. Rebuilding from the host's source generation once." );
			TrySetState( runId, DatabaseStartupState.Provisioning, "Downloading fresh card-definition sources." );

			if ( hostSource is { DownloadUri.Length: > 0 } )
				await Scryfall.Client.DownloadDefaultCardsSnapshot( hostSource, cancellationToken );
			else
				await Scryfall.Client.UpdateBulk( cancellationToken, force: true );
			await Scryfall.Client.UpdateRulings( cancellationToken, force: true );
			await Scryfall.Client.UpdateSets( cancellationToken );
			await Scryfall.Client.UpdateSymbology( cancellationToken );
			await BuildDatabaseAsync( runId, cancellationToken );

			cancellationToken.ThrowIfCancellationRequested();
			acquiredLease = await AcquireDatabaseAsync();
			string actualChecksum = await ComputeChecksumAsync();

			if ( !string.Equals( actualChecksum, expectedChecksum, StringComparison.Ordinal ) )
				throw new DatabaseGenerationMismatchException( $"The rebuilt card database still differs from the host. Host checksum: {expectedChecksum}; local checksum: {actualChecksum}. The exact host source generation may no longer be available." );

			await GameTask.MainThread( cancellationToken );

			if ( !TryPublishLease( runId, acquiredLease, actualChecksum, cancellationToken ) )
				return;

			acquiredLease = null;
			Log.Info( $"Card definitions now match the host ({actualChecksum})." );
		}
		catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
		{
			// Dispose owns the terminal state.
		}
		catch ( Exception exception )
		{
			bool publishFailure;

			lock ( _lifecycleLock )
			{
				publishFailure = !_disposed && runId == _runId;

				if ( publishFailure )
				{
					FailureReason = exception.Message;
					FailureKind = ClassifyFailure( exception );
					State = DatabaseStartupState.Failed;
					StatusMessage = "Card definitions do not match the host.";
				}
			}

			if ( publishFailure )
				Log.Error( $"Could not synchronize card definitions with the host. {exception}" );
		}
		finally
		{
			if ( gateHeld )
				ProvisioningGate.Release();

			acquiredLease?.Dispose();
		}
	}


	private async Task ProvisionDatabaseAsync( int runId, CancellationToken cancellationToken )
	{
		bool cachedSourcesAvailable = HaveAllSourceFiles();

		if ( cachedSourcesAvailable )
		{
			await EnsureCachedSourceProvenanceAsync( cancellationToken );
			Log.Info( "Found cached Scryfall source catalogs. Building card " + "definitions without a network download." );

			try
			{
				await BuildDatabaseAsync( runId, cancellationToken );

				return;
			}
			catch ( Exception exception ) when ( IsSourceDataFailure( exception ) && !cancellationToken.IsCancellationRequested )
			{
				Log.Warning( "Cached Scryfall source data is invalid. Downloading " + $"a fresh copy. {exception.Message}" );
			}
		}

		TrySetState( runId, DatabaseStartupState.Provisioning, "Downloading public card-definition sources." );

		Log.Info( "Downloading Scryfall card, ruling, set, and symbol catalogs." );

		await Scryfall.Client.UpdateBulk( cancellationToken, cachedSourcesAvailable );
		await Scryfall.Client.UpdateRulings( cancellationToken, cachedSourcesAvailable );
		await Scryfall.Client.UpdateSets( cancellationToken );
		await Scryfall.Client.UpdateSymbology( cancellationToken );

		cancellationToken.ThrowIfCancellationRequested();
		await BuildDatabaseAsync( runId, cancellationToken );
	}


	private static async Task EnsureCachedSourceProvenanceAsync( CancellationToken cancellationToken )
	{
		DatabaseSourceSnapshot metadata = Scryfall.Client.ReadLocalBulkMetadata( "default-cards" );

		if ( !string.IsNullOrWhiteSpace( metadata.DownloadUri ) )
			return;

		try
		{
			Log.Info( "Upgrading cached source metadata to a reproducible Scryfall snapshot descriptor." );
			await Scryfall.Client.UpdateBulk( cancellationToken );
			await Scryfall.Client.UpdateRulings( cancellationToken );
			await Scryfall.Client.UpdateSets( cancellationToken );
			await Scryfall.Client.UpdateSymbology( cancellationToken );
		}
		catch ( Exception exception ) when ( !cancellationToken.IsCancellationRequested )
		{
			// Cached sources remain useful offline. The generation will still carry
			// their checksum, but another machine may be unable to reproduce it.
			Log.Warning( $"Could not attach remote provenance to cached card sources; continuing offline. {exception.Message}" );
		}
	}


	private async Task BuildDatabaseAsync( int runId, CancellationToken cancellationToken )
	{
		TrySetState( runId, DatabaseStartupState.Provisioning, "Building and validating card definitions." );

		Log.Info( "Building and validating the local card-definition database. " + "This can take a moment on the first run." );

		// s&box's whitelist does not permit System.Threading.Tasks.Task.Run.
		// Database construction must use the engine-provided worker scheduler.
		await GameTask.RunInThreadAsync( () => DatabaseBuilder.BuildDatabase( cancellationToken ) );
	}


	private static Task<IDisposable> AcquireDatabaseAsync()
	{
		return GameTask.RunInThreadAsync( () => RuntimeCardDatabase.Acquire() );
	}


	private static Task<string> ComputeChecksumAsync()
	{
		return GameTask.RunInThreadAsync( CardDatabaseChecksum.Compute );
	}


	private bool TryPublishLease( int runId, IDisposable lease, string checksum, CancellationToken cancellationToken )
	{
		lock ( _lifecycleLock )
		{
			if ( _disposed || runId != _runId || cancellationToken.IsCancellationRequested )
				return false;

			_databaseLease = lease;
			DatabaseChecksum = checksum;
			SourceSnapshot = DatabaseGenerationStore.TryGetActiveManifest()?.CardSource;
			FailureReason  = null;
			FailureKind    = DatabaseFailureKind.None;
			State          = DatabaseStartupState.Ready;
			StatusMessage  = "Card definitions are ready.";

			return true;
		}
	}


	private bool TrySetState( int runId, DatabaseStartupState state, string statusMessage )
	{
		lock ( _lifecycleLock )
		{
			if ( _disposed || runId != _runId )
				return false;

			State         = state;
			StatusMessage = statusMessage;

			return true;
		}
	}


	private void StopDatabase()
	{
		Task?        initializationTask;
		IDisposable? databaseLease;

		lock ( _lifecycleLock )
		{
			if ( _disposed )
				return;

			_disposed = true;
			_runId++;

			State         = DatabaseStartupState.Stopped;
			StatusMessage = "Card database startup stopped.";

			initializationTask = _initializationTask;
			databaseLease      = _databaseLease;
			_databaseLease     = null;
			DatabaseChecksum   = null;
			SourceSnapshot     = null;
		}

		_lifetimeCancellation.Cancel();
		databaseLease?.Dispose();
		_completion.TrySetResult( DatabaseStartupState.Stopped );

		if ( initializationTask is null || initializationTask.IsCompleted )
			_lifetimeCancellation.Dispose();
		else
			_ = DisposeCancellationAfterAsync( initializationTask, _lifetimeCancellation );
	}


	private static async Task DisposeCancellationAfterAsync( Task initializationTask, CancellationTokenSource cancellation )
	{
		try
		{
			await initializationTask;
		}
		catch
		{
			// InitializeDatabaseAsync observes expected startup failures.
		}
		finally
		{
			cancellation.Dispose();
		}
	}


	private static bool HaveAllSourceFiles()
	{
		return FileSystem.Data.FileExists( DatabaseFileInfo.SourceFile ) && FileSystem.Data.FileExists( DatabaseFileInfo.RulingsSourceFile ) && FileSystem.Data.FileExists( DatabaseFileInfo.SetSourceFile ) && FileSystem.Data.FileExists( DatabaseFileInfo.SymbolSourceFile );
	}


	private static bool IsProvisionableDatabaseFailure( Exception exception )
	{
		return exception is FileNotFoundException or DirectoryNotFoundException or InvalidDataException or EndOfStreamException or JsonException || exception.InnerException is not null && IsProvisionableDatabaseFailure( exception.InnerException );
	}


	private static bool IsSourceDataFailure( Exception exception )
	{
		if ( exception is DatabaseSourceCompatibilityException )
			return false;

		return exception is InvalidDataException or EndOfStreamException or JsonException || exception.InnerException is not null && IsSourceDataFailure( exception.InnerException );
	}


	private static DatabaseFailureKind ClassifyFailure( Exception exception )
	{
		if ( exception is DatabaseSourceCompatibilityException )
			return DatabaseFailureKind.SourceCompatibility;

		if ( exception is DatabaseGenerationMismatchException )
			return DatabaseFailureKind.GenerationMismatch;

		if ( exception is JsonException or EndOfStreamException )
			return DatabaseFailureKind.SourceCorrupt;

		if ( exception is TimeoutException || exception.GetType().Name.Contains( "Http", StringComparison.OrdinalIgnoreCase ) )
			return DatabaseFailureKind.Network;

		return exception.InnerException is null? DatabaseFailureKind.Unknown : ClassifyFailure( exception.InnerException );
	}
}
