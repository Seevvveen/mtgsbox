#nullable enable
using System.Threading.Tasks;

/// <summary>
/// Provides an asynchronous gate for coordinating task completion across threads.
/// Allows fire-and-forget task execution with centralized completion signaling.
/// Integrates with s&box TaskSource for cancellation support.
/// </summary>
/// <remarks>
/// Use this class when you need to:
/// - Execute async work and notify waiters when complete
/// - Run expensive computations on worker threads with main thread callbacks
/// - Coordinate multiple async operations with a single completion signal
/// - Track success, cancellation, or failure states of background work
/// </remarks>
/// <example>
/// <code>
/// var gate = new AsyncGate();
/// 
/// // Fire and forget
/// gate.Run(async () => await DoSomethingAsync());
/// 
/// // With cancellation support
/// gate.Run(taskSource, async () => await DoWorkAsync());
/// 
/// // Worker thread computation
/// gate.RunInThread(
///     () => ExpensiveComputation(),
///     result => UpdateUI(result)
/// );
/// 
/// // Wait for completion
/// await gate.WhenReady;
/// </code>
/// </example>
public sealed class AsyncGate
{
	private TaskCompletionSource _tcs = new();
	private Exception? _error;

	/// <summary>
	/// Gets a task that completes when the gate is signaled (success, cancel, or failure).
	/// </summary>
	public Task WhenReady => _tcs.Task;

	/// <summary>
	/// Gets whether the gate has been signaled with any completion state.
	/// </summary>
	public bool IsCompleted => _tcs.Task.IsCompleted;

	/// <summary>
	/// Gets whether the gate completed successfully without cancellation or exception.
	/// </summary>
	public bool IsSucceeded => _tcs.Task.IsCompletedSuccessfully;

	/// <summary>
	/// Gets whether the gate was canceled via <see cref="Cancel"/> or <see cref="CancelAsync"/>.
	/// </summary>
	public bool IsCanceled => _tcs.Task.IsCanceled;

	/// <summary>
	/// Gets whether the gate failed with an exception via <see cref="Fail"/> or <see cref="FailAsync"/>.
	/// </summary>
	public bool IsFailed => _tcs.Task.IsFaulted;

	/// <summary>
	/// Gets the exception that caused the gate to fail, or null if not failed.
	/// </summary>
	public Exception? Error => _error;

	/// <summary>
	/// Resets the gate to allow reuse after completion.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown if the gate is not yet completed.</exception>
	/// <remarks>
	/// Only call this method after the gate has completed. Check <see cref="IsCompleted"/> before resetting.
	/// </remarks>
	public void Reset()
	{
		if ( !IsCompleted )
			throw new InvalidOperationException( "AsyncGate.Reset: can't reset while running." );
		_error = null;
		_tcs = new();
	}

	// ========== SIMPLE PRODUCER API ==========

	/// <summary>
	/// Fire-and-forget: Executes async work and automatically signals the gate on completion, cancellation, or failure.
	/// </summary>
	/// <param name="work">The async function to execute.</param>
	/// <remarks>
	/// The gate will be signaled as succeeded on normal completion, canceled on TaskCanceledException, 
	/// or failed on any other exception. Exceptions are captured and accessible via <see cref="Error"/>.
	/// </remarks>
	public void Run( Func<Task> work )
	{
		_ = RunAsync( work );
	}

	/// <summary>
	/// Fire-and-forget: Executes async work with TaskSource cancellation support and automatically signals the gate on completion.
	/// </summary>
	/// <param name="ts">The TaskSource to check for cancellation.</param>
	/// <param name="work">The async function to execute.</param>
	/// <remarks>
	/// If the TaskSource becomes invalid during execution, the gate will be canceled.
	/// This integrates with s&box's TaskSource cancellation system.
	/// </remarks>
	public void Run( TaskSource ts, Func<Task> work )
	{
		_ = RunAsync( ts, work );
	}

	/// <summary>
	/// Fire-and-forget: Executes a computation on a worker thread and automatically signals the gate on completion.
	/// </summary>
	/// <typeparam name="T">The return type of the worker computation.</typeparam>
	/// <param name="work">The synchronous function to execute on a worker thread.</param>
	/// <param name="onMainThread">Optional callback invoked on the main thread with the computation result.</param>
	/// <remarks>
	/// The worker computation runs on a background thread. The optional callback is guaranteed to run
	/// on the main thread after the computation completes. The gate signals after the callback (if provided) completes.
	/// </remarks>
	public void RunInThread<T>( Func<T> work, Action<T>? onMainThread = null )
	{
		_ = RunInThreadAsync( work, onMainThread );
	}

	/// <summary>
	/// Fire-and-forget: Executes a computation on a worker thread with TaskSource cancellation and automatically signals the gate.
	/// </summary>
	/// <typeparam name="T">The return type of the worker computation.</typeparam>
	/// <param name="ts">The TaskSource to check for cancellation.</param>
	/// <param name="work">The synchronous function to execute on a worker thread.</param>
	/// <param name="onMainThread">Optional callback invoked on the main thread with the computation result.</param>
	/// <remarks>
	/// The worker computation runs on a background thread. If the TaskSource becomes invalid,
	/// the gate will be canceled. The callback (if provided) only runs if the TaskSource remains valid.
	/// </remarks>
	public void RunInThread<T>( TaskSource ts, Func<T> work, Action<T>? onMainThread = null )
	{
		_ = RunInThreadAsync( ts, work, onMainThread );
	}

	// ========== AWAITABLE VERSIONS (if you need them) ==========

	/// <summary>
	/// Executes async work and signals the gate on completion. Returns an awaitable task for the execution.
	/// </summary>
	/// <param name="work">The async function to execute.</param>
	/// <returns>A task representing the execution of the work and gate signaling.</returns>
	/// <remarks>
	/// Unlike <see cref="Run(Func{Task})"/>, this method allows you to await the execution.
	/// The gate will be signaled as succeeded, canceled, or failed based on the work outcome.
	/// </remarks>
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

	/// <summary>
	/// Executes async work with TaskSource cancellation and signals the gate on completion. Returns an awaitable task.
	/// </summary>
	/// <param name="ts">The TaskSource to check for cancellation.</param>
	/// <param name="work">The async function to execute.</param>
	/// <returns>A task representing the execution of the work and gate signaling.</returns>
	/// <remarks>
	/// If the TaskSource becomes invalid after work completion, the gate will be canceled instead of succeeded.
	/// This provides integration with s&box's TaskSource cancellation system.
	/// </remarks>
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

	/// <summary>
	/// Executes a computation on a worker thread and signals the gate on completion. Returns an awaitable task.
	/// </summary>
	/// <typeparam name="T">The return type of the worker computation.</typeparam>
	/// <param name="work">The synchronous function to execute on a worker thread.</param>
	/// <param name="onMainThread">Optional callback invoked on the main thread with the computation result.</param>
	/// <returns>A task representing the worker thread execution, main thread callback, and gate signaling.</returns>
	/// <remarks>
	/// The worker computation runs on a background thread via <see cref="GameTask.RunInThreadAsync{T}"/>.
	/// Execution returns to the main thread before invoking the callback and signaling the gate.
	/// </remarks>
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

	/// <summary>
	/// Executes a computation on a worker thread with TaskSource cancellation and signals the gate. Returns an awaitable task.
	/// </summary>
	/// <typeparam name="T">The return type of the worker computation.</typeparam>
	/// <param name="ts">The TaskSource to check for cancellation.</param>
	/// <param name="work">The synchronous function to execute on a worker thread.</param>
	/// <param name="onMainThread">Optional callback invoked on the main thread with the computation result.</param>
	/// <returns>A task representing the worker thread execution, main thread callback, and gate signaling.</returns>
	/// <remarks>
	/// If the TaskSource becomes invalid after the worker thread completes, the callback will not run
	/// and the gate will be canceled. This ensures clean cancellation handling across thread boundaries.
	/// </remarks>
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

	/// <summary>
	/// Signals the gate as successfully completed. Switches to the main thread before signaling.
	/// </summary>
	/// <returns>A task that completes after switching to the main thread and signaling the gate.</returns>
	/// <remarks>
	/// Use this for manual control over gate signaling. The gate can only be signaled once;
	/// subsequent calls are ignored. Always switches to the main thread before setting the result.
	/// </remarks>
	public async Task SucceedAsync()
	{
		await GameTask.MainThread();
		_tcs.TrySetResult();
	}

	/// <summary>
	/// Signals the gate as canceled. Switches to the main thread before signaling.
	/// </summary>
	/// <returns>A task that completes after switching to the main thread and signaling the gate.</returns>
	/// <remarks>
	/// Use this for manual cancellation signaling. The gate can only be signaled once;
	/// subsequent calls are ignored. Always switches to the main thread before setting the cancellation.
	/// </remarks>
	public async Task CancelAsync()
	{
		await GameTask.MainThread();
		_tcs.TrySetCanceled();
	}

	/// <summary>
	/// Signals the gate as failed with the specified exception. Switches to the main thread before signaling.
	/// </summary>
	/// <param name="ex">The exception that caused the failure.</param>
	/// <returns>A task that completes after switching to the main thread and signaling the gate.</returns>
	/// <exception cref="ArgumentNullException">Thrown if <paramref name="ex"/> is null.</exception>
	/// <remarks>
	/// Use this for manual failure signaling. The exception is stored in <see cref="Error"/> for later inspection.
	/// The gate can only be signaled once; subsequent calls are ignored. Always switches to the main thread before setting the exception.
	/// </remarks>
	public async Task FailAsync( Exception ex )
	{
		ArgumentNullException.ThrowIfNull( ex );
		_error = ex;
		await GameTask.MainThread();
		_tcs.TrySetException( ex );
	}

	/// <summary>
	/// Signals the gate as successfully completed. Must be called from the main thread.
	/// </summary>
	/// <remarks>
	/// Synchronous version of <see cref="SucceedAsync"/>. Only use this when you're already on the main thread.
	/// The gate can only be signaled once; subsequent calls are ignored.
	/// </remarks>
	public void Succeed() => _tcs.TrySetResult();

	/// <summary>
	/// Signals the gate as canceled. Must be called from the main thread.
	/// </summary>
	/// <remarks>
	/// Synchronous version of <see cref="CancelAsync"/>. Only use this when you're already on the main thread.
	/// The gate can only be signaled once; subsequent calls are ignored.
	/// </remarks>
	public void Cancel() => _tcs.TrySetCanceled();

	/// <summary>
	/// Signals the gate as failed with the specified exception. Must be called from the main thread.
	/// </summary>
	/// <param name="ex">The exception that caused the failure.</param>
	/// <exception cref="ArgumentNullException">Thrown if <paramref name="ex"/> is null.</exception>
	/// <remarks>
	/// Synchronous version of <see cref="FailAsync"/>. Only use this when you're already on the main thread.
	/// The exception is stored in <see cref="Error"/> for later inspection.
	/// The gate can only be signaled once; subsequent calls are ignored.
	/// </remarks>
	public void Fail( Exception ex )
	{
		ArgumentNullException.ThrowIfNull( ex );
		_error = ex;
		_tcs.TrySetException( ex );
	}
}
