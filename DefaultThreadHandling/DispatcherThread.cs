// See https://aka.ms/new-console-template for more information
using System.Collections.Concurrent;
using System.Diagnostics;

public class DispatcherThread : IDisposable
{
    private readonly ConcurrentQueue<Action> _actionQueue = new();
    private readonly ManualResetEvent _terminating = new(false);
    private readonly AutoResetEvent _actionAdded = new(false);
    private Task? _task;

    public bool Running { get { lock (this) return _task != null; } }

    public EventHandler<ThreadExceptionEventArgs>? OnUnhandledException;

    public void Start()
    {
        lock (this)
        {
            if (_task != default)
                return;

            _terminating.Reset();

            _task = Task.Factory.StartNew(MainLoop, TaskCreationOptions.LongRunning);
        }
    }

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

    public Task Invoke(Action action)
    {
        TaskCompletionSource taskCompletionSource = new();

        var context = SynchronizationContext.Current;

        AddAction(() =>
        {
            try
            {
                action();

                InvokeOnContext(context, () => taskCompletionSource.SetResult());

            }
            catch (Exception ex)
            {
                InvokeOnContext(context, () => taskCompletionSource.SetException(ex));
            }
        });

        return taskCompletionSource.Task;
    }

    public Task<T> Invoke<T>(Func<T> func)
    {
        TaskCompletionSource<T> taskCompletionSource = new();

        var context = SynchronizationContext.Current;

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

    public void Dispose()
    {
        lock (this)
        {
            Stop();

            _terminating.Dispose();
            _actionAdded.Dispose();
        }
    }

    private void MainLoop()
    {
        var handles = new EventWaitHandle[] { _terminating, _actionAdded };
        // wait on any handle, loop until the WaitAny returns a 0 as index.
        // which is _terminating
        while (EventWaitHandle.WaitAny(handles) != 0)
            while (_actionQueue.TryDequeue(out var action))
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

    private void AddAction(Action action)
    {
        _actionQueue.Enqueue(action);
        _actionAdded.Set();
    }

    private void InvokeOnContext(SynchronizationContext? context, Action action)
    {
        // if there wasn't a SynchronizationContext, use the ThreadPool.
        // Avoid continueing on this thread.
        if (context != null)
            context.Post(state => action(), null);
        else
            ThreadPool.QueueUserWorkItem(state => action(), null);
    }
}