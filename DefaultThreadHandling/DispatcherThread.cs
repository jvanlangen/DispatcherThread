using System.Collections.Concurrent;
using System.Diagnostics;

/// <summary>
/// Represents a dispatcher thread that executes actions asynchronously on a separate thread.
/// </summary>
public class DispatcherThread : IDisposable
{
    /// <summary>
    /// The default synchronization context used when no specific context is captured.
    /// </summary>
    private static readonly SynchronizationContext _defaultSynchronizationContext = new();

    /// <summary>
    /// Queue to hold actions to be executed on the separate thread.
    /// </summary>
    private readonly List<Action> _actionQueue = new();

    /// <summary>
    /// ManualResetEvent to indicate whether the thread is terminating.
    /// </summary>
    private readonly ManualResetEvent _terminating = new(false);

    /// <summary>
    /// AutoResetEvent to signal that an action has been added to the queue.
    /// </summary>
    private readonly AutoResetEvent _actionAdded = new(false);

    /// <summary>
    /// The running task on the separate thread.
    /// </summary>
    private Task? _task;

    /// <summary>
    /// Event handler for unhandled exceptions during action execution.
    /// </summary>
    public EventHandler<ThreadExceptionEventArgs>? OnUnhandledException;

    /// <summary>
    /// Property to check if the thread is currently running.
    /// </summary>
    public bool Running { get { lock (this) return _task != null; } }

    /// <summary>
    /// Constructor starts the thread automatically.
    /// </summary>
    public DispatcherThread() =>
        Start();

    /// <summary>
    /// Starts the main loop on the separate thread.
    /// </summary>
    public void Start()
    {
        lock (this)
        {
            if (_task != default)
                return;

            _terminating.Reset();

            _task = Task.Run(MainLoop);
        }
    }

    /// <summary>
    /// Stops the main loop and waits for the thread to complete.
    /// </summary>
    public void Stop()
    {
        lock (this)
        {
            if (_task == default)
                return;

            _terminating.Set();
            _task.Wait();
            _task = default;
        }
    }

    /// <summary>
    /// Executes an action asynchronously and returns a Task to track its completion.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <returns>A Task representing the asynchronous execution of the action.</returns>
    public Task Invoke(Action action)
    {
        // Capture the current SynchronizationContext.
        var context = SynchronizationContext.Current;

        TaskCompletionSource taskCompletionSource = new();

        // Add an action to the queue.
        AddAction(() =>
        {
            try
            {
                action();

                // Invoke the task completion on the captured context.
                InvokeOnContext(context, () => taskCompletionSource.SetResult());
            }
            catch (Exception ex)
            {
                // If an exception occurs, invoke the exception handling on the captured context.
                InvokeOnContext(context, () => taskCompletionSource.SetException(ex));
            }
        });

        return taskCompletionSource.Task;
    }

    /// <summary>
    /// Executes a function asynchronously and returns a Task with the result.
    /// </summary>
    /// <typeparam name="T">The type of the function result.</typeparam>
    /// <param name="func">The function to execute.</param>
    /// <returns>A Task representing the asynchronous execution of the function with the result.</returns>
    public Task<T> Invoke<T>(Func<T> func)
    {
        var context = SynchronizationContext.Current;

        TaskCompletionSource<T> taskCompletionSource = new();

        AddAction(() =>
        {
            try
            {
                var result = func();
                InvokeOnContext(context, () => taskCompletionSource.SetResult(result));
            }
            catch (Exception ex)
            {
                InvokeOnContext(context, () => taskCompletionSource.SetException(ex));
            }
        });

        return taskCompletionSource.Task;
    }

    /// <summary>
    /// Disposes the DispatcherThread object.
    /// </summary>
    public void Dispose()
    {
        lock (this)
        {
            Stop();

            _terminating.Dispose();
            _actionAdded.Dispose();
        }
    }

    /// <summary>
    /// The main loop that runs on the separate thread.
    /// </summary>
    private void MainLoop()
    {
        var handles = new EventWaitHandle[] { _terminating, _actionAdded };
        // wait on any handle, loop until the WaitAny returns a 0 as index.
        // which is _terminating
        while (EventWaitHandle.WaitAny(handles) != 0)
        {
            List<Action> actionsToProcess;

            lock (_actionQueue)
            {
                if (_actionQueue.Count == 0)
                    continue;

                actionsToProcess = new List<Action>(_actionQueue);
                _actionQueue.Clear();
            }

            foreach (var action in actionsToProcess)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    try
                    {
                        OnUnhandledException?.Invoke(this, new ThreadExceptionEventArgs(ex));
                    }
                    catch (Exception ex2)
                    {
                        Trace.TraceError(ex2.ToString());
                    }
                }
            }
        }
    }

    /// <summary>
    /// Adds an action to the queue and sets the trigger.
    /// </summary>
    /// <param name="action">The action to add to the queue.</param>
    private void AddAction(Action action)
    {
        lock(_actionQueue)
            _actionQueue.Add(action);

        _actionAdded.Set();
    }

    /// <summary>
    /// Invokes an action on the specified synchronization context.
    /// If the context is not captured, the default (ThreadPool) context is used.
    /// </summary>
    /// <param name="context">The synchronization context to invoke the action on.</param>
    /// <param name="action">The action to invoke.</param>
    private void InvokeOnContext(SynchronizationContext? context, Action action) =>
        // if there wasn't a context captured, use the default (ThreadPool)
        (context ?? _defaultSynchronizationContext).Post(s => action(), null);
}