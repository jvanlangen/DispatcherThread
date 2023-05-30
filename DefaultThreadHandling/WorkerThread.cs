using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace PWR.Common.Threading
{
    /// <summary>
    /// Represents an asynchronous thread that runs a message loop for executing actions asynchronously.
    /// </summary>
    public sealed class WorkerThread : IDisposable
    {
        private readonly ThreadLoop _mainLoop;
        private readonly Task _mainTask;
        private readonly Thread _thread;

        /// <summary>
        /// Gets or sets the name of the thread.
        /// </summary>
        public string? ThreadName
        {
            get => _thread.Name;
            set => _thread.Name = value;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkerThread"/> class.
        /// </summary>
        public WorkerThread()
        {
            _mainLoop = new ThreadLoop();

            using (ManualResetEvent threadStarted = new(false))
            {
                Thread? thread = null;

                // Create the thread using Task.Factory.StartNew with TaskCreationOptions.LongRunning.
                _mainTask = Task.Factory.StartNew(() =>
                    {
                        // assign the local thread variable, to capture the loop-thread reference.
                        thread = Thread.CurrentThread;
                        // Signal a ready.
                        threadStarted.Set();
                        // run the mainloop.
                        _mainLoop.Run();
                    },
                    CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);

                threadStarted.WaitOne();

                // assign the captured thread variable to a field.
                _thread = thread!;
            }
        }

        /// <summary>
        /// Sends an synchronous operation to be executed on the thread.
        /// </summary>
        /// <param name="func">The asynchronous function to execute.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public void Send(Action action) =>
            _mainLoop.Send(_ => action());

        /// <summary>
        /// Sends an asynchronous operation to be executed on the thread.
        /// </summary>
        /// <param name="func">The asynchronous function to execute.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public Task SendAsync(Func<Task> func) =>
            _mainLoop.SendAsync(() => func());

        /// <summary>
        /// Sends an asynchronous operation to be executed on the thread.
        /// </summary>
        /// <param name="executionDelay">The amount of time to delay the execution.</param>
        /// <param name="func">The asynchronous function to execute.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public Task SendAsync(TimeSpan executionDelay, Func<Task> func) =>
            _mainLoop.SendAsync(executionDelay, () => func());

        /// <summary>
        /// Sends an asynchronous operation to be executed on the thread.
        /// </summary>
        /// <param name="func">The asynchronous function to execute.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public Task<T> SendAsync<T>(Func<Task<T>> func) =>
            _mainLoop.SendAsync(func);

        /// <summary>
        /// Sends an asynchronous operation to be executed on the thread.
        /// </summary>
        /// <param name="executionDelay">The amount of time to delay the execution.</param>
        /// <param name="func">The asynchronous function to execute.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public Task<T> SendAsync<T>(TimeSpan executionDelay, Func<Task<T>> func) =>
            _mainLoop.SendAsync(executionDelay, func);

        /// <summary>
        /// Sends an asynchronous operation to be executed on the thread.
        /// </summary>
        /// <param name="action">The action to execute.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public Task SendAsync(Action action) =>
            _mainLoop.SendAsync(() =>
            {
                action();
                return Task.CompletedTask;
            });

        /// <summary>
        /// Sends an asynchronous operation to be executed on the thread.
        /// </summary>
        /// <param name="executionDelay">The amount of time to delay the execution.</param>
        /// <param name="action">The action to execute.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public Task SendAsync(TimeSpan executionDelay, Action action) =>
            _mainLoop.SendAsync(executionDelay, () =>
            {
                action();
                return Task.CompletedTask;
            });


        /// <summary>
        /// Posts a message to the thread's message queue and returns immediately.
        /// </summary>
        /// <param name="executionDelay">The amount of time to delay the execution.</param>
        /// <param name="d">The delegate to invoke.</param>
        /// <param name="state">An object containing data to be used by the delegate.</param>
        public void Post(TimeSpan executionDelay, Action action)
            => _mainLoop.Post(executionDelay, s => action());

        /// <summary>
        /// Posts a message to the thread's message queue and returns immediately.
        /// </summary>
        /// <param name="action">The action to execute.</param>
        public void Post(Action action) =>
            _mainLoop.Post(s => action());

        /// <summary>
        /// Stops the thread and disposes it.
        /// </summary>
        public void Dispose()
        {
            _mainLoop.Dispose();

            // Wait for the thread to end.
            _mainTask.Wait();
        }
    }
}