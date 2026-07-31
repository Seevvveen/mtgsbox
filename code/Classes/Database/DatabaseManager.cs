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
		// Published clients should receive the same validated generation as the
		// host. They must not independently build "latest" content at startup.
		StartDatabase( allowProvisioning: false );
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

			await GameTask.MainThread( cancellationToken );
			cancellationToken.ThrowIfCancellationRequested();

			if ( !TryPublishLease( runId, acquiredLease, cancellationToken ) )
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


	private async Task ProvisionDatabaseAsync( int runId, CancellationToken cancellationToken )
	{
		bool cachedSourcesAvailable = HaveAllSourceFiles();

		if ( cachedSourcesAvailable )
		{
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


	private async Task BuildDatabaseAsync( int runId, CancellationToken cancellationToken )
	{
		TrySetState( runId, DatabaseStartupState.Provisioning, "Building and validating card definitions." );

		Log.Info( "Building and validating the local card-definition database. " + "This can take a moment on the first run." );

		await GameTask.RunInThreadAsync( () => DatabaseBuilder.BuildDatabase( cancellationToken ) );
	}


	private static Task<IDisposable> AcquireDatabaseAsync()
	{
		return GameTask.RunInThreadAsync( () => RuntimeCardDatabase.Acquire() );
	}


	private bool TryPublishLease( int runId, IDisposable lease, CancellationToken cancellationToken )
	{
		lock ( _lifecycleLock )
		{
			if ( _disposed || runId != _runId || cancellationToken.IsCancellationRequested )
				return false;

			_databaseLease = lease;
			FailureReason  = null;
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
		return exception is InvalidDataException or EndOfStreamException or JsonException || exception.InnerException is not null && IsSourceDataFailure( exception.InnerException );
	}
}
