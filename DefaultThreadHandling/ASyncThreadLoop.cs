using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;

namespace PWR.Common.Threading
{
    /// <summary>
    /// Represents an asynchronous thread loop.
    /// </summary>
    public sealed class ASyncThreadLoop : IDisposable
    {
        private static readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        private static Ticks ElapsedTicks => Ticks.FromStopwatch(_stopwatch);
        private static long ElapsedMilliseconds => _stopwatch.ElapsedMilliseconds;


        private static readonly Ticks ZeroTicks = Ticks.FromTimeSpan(TimeSpan.Zero);
        private readonly ManualResetEvent _terminateEvent = new(false);
        private static readonly SynchronizationContext threadPoolSynchronizationContext = new();
        private int _currentThreadId;

        [ThreadStatic]
        private static ASyncThreadLoop? _current;

        private readonly AutoResetEvent _actionAdded = new(false);
        private readonly List<ActionWithState> _itemBuffer = new();

        private SynchronizationContext? _previousSynchronizationContext;

        private class ASyncThreadSynchronizationContext : SynchronizationContext
        {
            private readonly ASyncThreadLoop _threadLoop;

            public ASyncThreadSynchronizationContext(ASyncThreadLoop threadLoop) =>
                _threadLoop = threadLoop;

            public override void Post(SendOrPostCallback d, object? state) =>
                _threadLoop.Post(d, state);

            public override void Send(SendOrPostCallback d, object? state) =>
                _threadLoop.Send(d);
        }

        private class ActionWithState
        {
            public ActionWithState(Ticks queueTime, SendOrPostCallback action, object? state)
            {
                Action = action ?? throw new ArgumentNullException(nameof(action));
                State = state;
                QueueTime = queueTime;
            }

            public Ticks QueueTime { get; }
            public SendOrPostCallback Action { get; }
            public object? State { get; }
        }

        /// <summary>
        /// Runs the asynchronous thread loop.
        /// </summary>
        public void Run()
        {
            // save the previous synchronization context, to set it back later.
            _previousSynchronizationContext = SynchronizationContext.Current;

            // Set this instance as SynchronizationContext
            SynchronizationContext.SetSynchronizationContext(new ASyncThreadSynchronizationContext(this));

            // set the threadstatic _current to this instance.
            _current = this;

            _currentThreadId = Environment.CurrentManagedThreadId;

            // Create an array of waithandles
            var waitHandles = new WaitHandle[] { _terminateEvent, _actionAdded };

            // Wait on any handle, break the loop if the first waithandle was set. (_terminate)
            while (true)
            {
                if (_terminateEvent.WaitOne(0))
                    break;

                ActionWithState? nextItem;

                lock (_itemBuffer)
                    nextItem = _itemBuffer.OrderBy(item => item.QueueTime).FirstOrDefault();

                if (nextItem != null)
                {
                    // calculate the difference between now and when it should be executed.
                    var delta = nextItem.QueueTime - ElapsedTicks;

                    // if the action should be executed in the future, wait for it.
                    if (delta.Value > 0)
                    {
                        var result = WaitHandle.WaitAny(waitHandles, delta.ToTimeSpan());

                        // if result == 0, terminate the loop
                        if (result == 0)
                            break;

                        // if result == 1, a new item is queue, recheck the queue
                        if (result == 1)
                            continue;

                        // time on the WaitAny, so time to execute the current item.
                    }

                    try
                    {
                        // remove the item from the queue
                        lock (_itemBuffer)
                            _itemBuffer.Remove(nextItem);

                        // execute the item.
                        nextItem.Action(nextItem.State);
                    }
                    catch (Exception exception)
                    {
                        try
                        {
                            OnUnhandledException?.Invoke(this, new ThreadExceptionEventArgs(exception));
                        }
                        catch (Exception ex)
                        {
                            Trace.TraceError(ex.ToString());
                        }
                    }
                }
                // Wait forever, but if an item is added, check the item
                // if waithandle result 0, terminate the loop
                else if (WaitHandle.WaitAny(waitHandles) == 0)
                    break;
            }

            // Reset this instance as SynchronizationContext
            SynchronizationContext.SetSynchronizationContext(_previousSynchronizationContext);

            _terminateEvent.Dispose();
            _actionAdded.Dispose();
        }

        /// <summary>
        /// Releases all resources used by the <see cref="ASyncThreadLoop"/> instance.
        /// </summary>
        public void Dispose()
        {
            _terminateEvent.Set();
        }

        /// <summary>
        /// Sends a synchronous operation to be executed on the current or specified thread.
        /// </summary>
        /// <param name="d">The delegate to execute.</param>
        /// <param name="state">The object that contains data to be used by the delegate.</param>
        public void Send(SendOrPostCallback d, object? state = null) =>
            Send(TimeSpan.Zero, d, state);

        /// <summary>
        /// Sends a synchronous operation to be executed on the current or specified thread, with a specified timeout for starting the operation.
        /// </summary>
        /// <param name="executionDelay">The maximum time to wait for the operation to start.</param>
        /// <param name="d">The delegate to execute.</param>
        /// <param name="state">The object that contains data to be used by the delegate.</param>
        public void Send(TimeSpan executionDelay, SendOrPostCallback d, object? state = null)
        {
            // Execute it directly, if it's on the right thread.
            if (Thread.CurrentThread.ManagedThreadId == _currentThreadId)
            {
                d(state);
                return;
            }

            // create a wait handle to block the current thread until the action is executed
            using (var waitHandle = new ManualResetEvent(false))
            {
                Post(executionDelay, s =>
                {
                    try
                    {
                        d(s);
                    }
                    finally
                    {
                        waitHandle.Set();
                    }
                }, state);

                // wait until the action has been executed.
                waitHandle.WaitOne();
            }
        }

        /// <summary>
        /// Sends an asynchronous operation to be executed on the current or specified thread.
        /// </summary>
        /// <param name="action">The asynchronous action to execute.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public Task SendAsync(Action action) =>
            SendAsync(TimeSpan.Zero, action);

        /// <summary>
        /// Sends an asynchronous operation to be executed on the current or specified thread, with a specified timeout for starting the operation.
        /// </summary>
        /// <param name="executionDelay">The maximum time to wait for the operation to start.</param>
        /// <param name="action">The asynchronous action to execute.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public Task SendAsync(TimeSpan executionDelay, Action action)
        {
            action = action ?? throw new ArgumentNullException(nameof(action));

            var tcs = new TaskCompletionSource();

            var returnContext = SynchronizationContext.Current ?? threadPoolSynchronizationContext;

            Post(executionDelay, _ =>
            {
                try
                {
                    action();
                    returnContext.Post(_ => tcs.SetResult(), null);
                }
                catch (Exception exception)
                {
                    returnContext.Post(_ => tcs.SetException(exception), null);
                }
            });

            return tcs.Task;
        }

        /// <summary>
        /// Sends an asynchronous operation to be executed on the current or specified thread.
        /// </summary>
        /// <param name="func">The asynchronous function to execute.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public Task SendAsync(Func<Task> func) =>
            SendAsync(TimeSpan.Zero, func);

        /// <summary>
        /// Sends an asynchronous operation to be executed on the current or specified thread, with a specified timeout for starting the operation.
        /// </summary>
        /// <param name="executionDelay">The maximum time to wait for the operation to start.</param>
        /// <param name="func">The asynchronous function to execute.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public Task SendAsync(TimeSpan executionDelay, Func<Task> func)
        {
            func = func ?? throw new ArgumentNullException(nameof(func));

            // Execute it directly, if it's on the right thread.
            if (Environment.CurrentManagedThreadId == _currentThreadId)
                return func();

            var tcs = new TaskCompletionSource();

            var returnContext = SynchronizationContext.Current ?? threadPoolSynchronizationContext;

            Post(executionDelay, async _ =>
            {
                try
                {
                    await func();
                    returnContext.Post(_ => tcs.SetResult(), null);
                }
                catch (Exception exception)
                {
                    returnContext.Post(_ => tcs.SetException(exception), null);
                }
            });

            return tcs.Task;
        }

        /// <summary>
        /// Sends an asynchronous operation to be executed on the current or specified thread.
        /// </summary>
        /// <typeparam name="T">The type of the result returned by the asynchronous function.</typeparam>
        /// <param name="func">The asynchronous function to execute.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public Task<T> SendAsync<T>(Func<Task<T>> func) =>
            SendAsync(TimeSpan.Zero, func);

        /// <summary>
        /// Sends an asynchronous operation to be executed on the current or specified thread, with a specified timeout for starting the operation.
        /// </summary>
        /// <typeparam name="T">The type of the result returned by the asynchronous function.</typeparam>
        /// <param name="executionDelay">The maximum time to wait for the operation to start.</param>
        /// <param name="func">The asynchronous function to execute.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public Task<T> SendAsync<T>(TimeSpan executionDelay, Func<Task<T>> func)
        {
            func = func ?? throw new ArgumentNullException(nameof(func));

            // Execute it directly, if it's on the right thread.
            if (Environment.CurrentManagedThreadId == _currentThreadId)
                return func();

            var tcs = new TaskCompletionSource<T>();
            
            var returnContext = SynchronizationContext.Current ?? threadPoolSynchronizationContext;

            Post(executionDelay, async _ =>
            {
                try
                {
                    var result = await func();
                    returnContext.Post(_ => tcs.SetResult(result), null);
                }
                catch (Exception exception)
                {
                    returnContext.Post(_ => tcs.SetException(exception), null);
                }
            });

            return tcs.Task;
        }

        /// <summary>
        /// Posts a callback method to the current or specified thread.
        /// </summary>
        /// <param name="d">The delegate to invoke.</param>
        /// <param name="state">The object to pass to the delegate.</param>

        public void Post(SendOrPostCallback d, object? state = null) =>
            Post(TimeSpan.Zero, d, state);

        /// <summary>
        /// Posts a callback method to the current or specified thread, with a specified delay before execution.
        /// </summary>
        /// <param name="executionDelay">The time span to wait before executing the callback.</param>
        /// <param name="d">The delegate to invoke.</param>
        /// <param name="state">The object to pass to the delegate.</param>

        public void Post(TimeSpan executionDelay, SendOrPostCallback d, object? state = null)
        {
            d = d ?? throw new ArgumentNullException(nameof(d));

            var scheduledTime = ElapsedTicks + Ticks.FromTimeSpan(executionDelay);

            var item = new ActionWithState(scheduledTime, d, state);

            lock (_itemBuffer)
                _itemBuffer.Add(item);

            _actionAdded.Set();
        }

        /// <summary>
        /// Gets the current <see cref="ASyncThreadLoop"/> instance associated with the current thread.
        /// </summary>
        public static ASyncThreadLoop? Current => _current;

        /// <summary>
        /// Occurs when an unhandled exception is encountered in the asynchronous thread loop.
        /// </summary>
        public static event ThreadExceptionEventHandler? OnUnhandledException;
    }
}