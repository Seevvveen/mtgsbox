#nullable enable
using System.Threading.Tasks;

/*

var gate = new AsyncGate();

//Simple fire and forget
gate.Run( async () => await DoSomethingAsync() );

//Respect Cancel Tokens
gate.Run( TaskSource, async () => 
{
    await TaskSource.Delay( 1000 );
    // Work that respects cancellation
});

//Do work in worker thread
gate.RunInThread( 
    () => ExpensiveComputation(),
    result => UpdateUI( result )  // Runs on main thread
);


// Elsewhere
await gate.WhenReady;

*/

/// <summary>
/// Async gate for signaling completion across threads. Integrates with s&box TaskSource.
/// </summary>
public sealed class AsyncGate
{
	private TaskCompletionSource _tcs = new();
	private Exception? _error;

	public Task WhenReady => _tcs.Task;
	public bool IsCompleted => _tcs.Task.IsCompleted;
	public bool IsSucceeded => _tcs.Task.IsCompletedSuccessfully;
	public bool IsCanceled => _tcs.Task.IsCanceled;
	public bool IsFailed => _tcs.Task.IsFaulted;
	public Exception? Error => _error;

	public void Reset()
	{
		if ( !IsCompleted )
			throw new InvalidOperationException( "AsyncGate.Reset: can't reset while running." );
		_error = null;
		_tcs = new();
	}

	// ========== SIMPLE PRODUCER API ==========

	/// <summary>
	/// Fire-and-forget: Run async work and auto-signal when complete.
	/// </summary>
	public void Run( Func<Task> work )
	{
		_ = RunAsync( work );
	}

	/// <summary>
	/// Fire-and-forget: Run async work with TaskSource and auto-signal.
	/// </summary>
	public void Run( TaskSource ts, Func<Task> work )
	{
		_ = RunAsync( ts, work );
	}

	/// <summary>
	/// Fire-and-forget: Run worker thread computation and auto-signal.
	/// </summary>
	public void RunInThread<T>( Func<T> work, Action<T>? onMainThread = null )
	{
		_ = RunInThreadAsync( work, onMainThread );
	}

	/// <summary>
	/// Fire-and-forget: Run worker thread computation with TaskSource and auto-signal.
	/// </summary>
	public void RunInThread<T>( TaskSource ts, Func<T> work, Action<T>? onMainThread = null )
	{
		_ = RunInThreadAsync( ts, work, onMainThread );
	}

	// ========== AWAITABLE VERSIONS (if you need them) ==========

	public async Task RunAsync( Func<Task> work )
	{
		try
		{
			await work();
			await SucceedAsync();
		}
		catch ( TaskCanceledException )
		{
			await CancelAsync();
		}
		catch ( Exception ex )
		{
			await FailAsync( ex );
		}
	}

	public async Task RunAsync( TaskSource ts, Func<Task> work )
	{
		try
		{
			await work();
			if ( !ts.IsValid ) throw new TaskCanceledException();
			await SucceedAsync();
		}
		catch ( TaskCanceledException )
		{
			await CancelAsync();
		}
		catch ( Exception ex )
		{
			await FailAsync( ex );
		}
	}

	public async Task RunInThreadAsync<T>( Func<T> work, Action<T>? onMainThread = null )
	{
		try
		{
			var result = await GameTask.RunInThreadAsync( work );
			await GameTask.MainThread();
			onMainThread?.Invoke( result );
			_tcs.TrySetResult();
		}
		catch ( TaskCanceledException )
		{
			await CancelAsync();
		}
		catch ( Exception ex )
		{
			await FailAsync( ex );
		}
	}

	public async Task RunInThreadAsync<T>( TaskSource ts, Func<T> work, Action<T>? onMainThread = null )
	{
		try
		{
			var result = await ts.RunInThreadAsync( work );
			await GameTask.MainThread();
			if ( !ts.IsValid ) throw new TaskCanceledException();
			onMainThread?.Invoke( result );
			_tcs.TrySetResult();
		}
		catch ( TaskCanceledException )
		{
			await CancelAsync();
		}
		catch ( Exception ex )
		{
			await FailAsync( ex );
		}
	}

	// ========== MANUAL SIGNALING (advanced use) ==========

	public async Task SucceedAsync()
	{
		await GameTask.MainThread();
		_tcs.TrySetResult();
	}

	public async Task CancelAsync()
	{
		await GameTask.MainThread();
		_tcs.TrySetCanceled();
	}

	public async Task FailAsync( Exception ex )
	{
		ArgumentNullException.ThrowIfNull( ex );
		_error = ex;
		await GameTask.MainThread();
		_tcs.TrySetException( ex );
	}

	// Synchronous versions for when you're already on main thread
	public void Succeed() => _tcs.TrySetResult();
	public void Cancel() => _tcs.TrySetCanceled();
	public void Fail( Exception ex )
	{
		ArgumentNullException.ThrowIfNull( ex );
		_error = ex;
		_tcs.TrySetException( ex );
	}
}
