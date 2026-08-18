/************************************************************************
 * Copyright (C) 2007 Jamaa Technologies
 *
 * This file is part of Jamaa SMPP Library.
 *
 * Jamaa SMPP Library is free software. You can redistribute it and/or modify
 * it under the terms of the Microsoft Reciprocal License (Ms-RL)
 *
 * You should have received a copy of the Microsoft Reciprocal License
 * along with Jamaa SMPP Library; See License.txt for more details.
 *
 * Author: Benedict J. Tesha
 * benedict.tesha@jamaatech.com, www.jamaatech.com
 *
 ************************************************************************/

using System.Threading.Tasks;
using JamaaTech.Smpp.Net.Lib.Logging;
using Microsoft.Extensions.Logging;

namespace JamaaTech.Smpp.Net.Lib.Util
{
    /// <summary>
    /// Base class for components that own a long-running work loop.
    /// </summary>
    public abstract class RunningComponent : IDisposable, IAsyncDisposable
    {
        private static readonly ILogger Logger = SmppLog.For(typeof(RunningComponent));

        /// <summary>How long a stop waits for the work loop to unwind.</summary>
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

        #region Variables
        protected bool vRunning;
        protected object vSyncRoot;
        private bool vStopOnNextCycle;
        private Task vRunningTask;
        private CancellationTokenSource vCancellation;
        private CancellationToken vStopToken;
        #endregion

        #region Constructors
        public RunningComponent()
        {
            //Initit vSyncRoot
            vSyncRoot = new object();
            vStopToken = new CancellationToken(canceled: true);
            //vRunning = false; //false is the default boolean value anyway,  not need to set it
        }

        #endregion

        #region Properties
        public bool Running
        {
            get { lock (vSyncRoot) { return vRunning; } }
        }

        /// <summary>
        /// A token that is cancelled when the component is asked to stop. It is signalled
        /// before a stop starts waiting, so a work loop that honours it unwinds promptly.
        /// </summary>
        protected CancellationToken StopToken
        {
            get { lock (vSyncRoot) { return vStopToken; } }
        }
        #endregion

        #region Methods
        #region Interface Methods
        public void Start()
        {
            lock (vSyncRoot)
            {
                if (vRunning) { return; } //If this component is already running, do nothing

                // Mark as running before the work loop starts so concurrent callers cannot
                // start multiple loops against the same component.
                vRunning = true;
                vStopOnNextCycle = false;
                vCancellation = new CancellationTokenSource();
                vStopToken = vCancellation.Token;

                //Initialize component before running the work loop
                InitializeComponent();

                // LongRunning: RunNow() is expected to block for the lifetime of the
                // component, so it gets a dedicated thread rather than a pool thread.
                vRunningTask = Task.Factory.StartNew(
                    ThreadCallback,
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default);
            }
        }

        public void Stop()
        {
            Stop(false);
        }

        /// <summary>
        /// Stops the component.
        /// </summary>
        /// <param name="allowCompleteCycle">
        /// When <see langword="true"/>, asks the work loop to finish its current cycle and
        /// returns immediately. When <see langword="false"/>, cancels the loop and waits for
        /// it to unwind.
        /// </param>
        public void Stop(bool allowCompleteCycle)
        {
            if (!TryBeginStop(allowCompleteCycle, out var runningTask)) { return; }

            // Deliberately outside the lock: the work loop takes vSyncRoot as it unwinds,
            // so waiting for it while holding that lock would deadlock until the timeout.
            if (runningTask != null && !runningTask.Wait(StopTimeout))
            {
                Logger.LogWarning("{Component} did not stop gracefully within {Timeout}", GetType().Name, StopTimeout);
            }

            CompleteStop();
        }

        /// <inheritdoc cref="Stop(bool)"/>
        /// <param name="allowCompleteCycle">See <see cref="Stop(bool)"/>.</param>
        /// <param name="cancellationToken">Abandons the wait for the work loop.</param>
        public async Task StopAsync(bool allowCompleteCycle = false, CancellationToken cancellationToken = default)
        {
            if (!TryBeginStop(allowCompleteCycle, out var runningTask)) { return; }

            if (runningTask != null)
            {
                var completed = await Task
                    .WhenAny(runningTask, Task.Delay(StopTimeout, cancellationToken))
                    .ConfigureAwait(false);

                if (completed != runningTask)
                {
                    Logger.LogWarning("{Component} did not stop gracefully within {Timeout}", GetType().Name, StopTimeout);
                }
            }

            CompleteStop();
        }

        public void Dispose()
        {
            Stop(false);
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        protected abstract void RunNow();

        protected virtual void ThreadCallback()
        {
            try
            {
                RunNow();
            }
            catch (OperationCanceledException) when (StopToken.IsCancellationRequested)
            {
                // Cooperative shutdown, not an error.
            }
            catch (System.Exception ex)
            {
                // Swallow to prevent crashing the process, but log for diagnostics
                Logger.LogError(ex, "{Component} terminated due to an unhandled exception", GetType().Name);
            }
            finally
            {
                lock (vSyncRoot)
                {
                    // Only the currently-registered worker may clear the running state. A
                    // stop that times out (or a cancelled StopAsync) leaves the old worker
                    // running; a later Start swaps in a new task, and the old worker's
                    // finally must not clobber the new one's state.
                    if (vRunningTask != null && Task.CurrentId == vRunningTask.Id)
                    {
                        vRunning = false;
                        vRunningTask = null;
                        vStopOnNextCycle = true;
                    }
                }
            }
        }

        protected virtual void InitializeComponent() { }

        protected virtual bool CanContinue()
        {
            lock (vSyncRoot) { return !vStopOnNextCycle; }
        }

        protected virtual void StopOnNextCycle()
        {
            lock (vSyncRoot) { vStopOnNextCycle = true; }
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Signals the work loop to stop. Returns <see langword="false"/> when there is
        /// nothing to wait for: either the component is not running, or the caller allowed
        /// the current cycle to complete on its own.
        /// </summary>
        private bool TryBeginStop(bool allowCompleteCycle, out Task runningTask)
        {
            runningTask = null;
            lock (vSyncRoot)
            {
                if (!vRunning) { return false; } //If this component is stopped, do nothing
                vStopOnNextCycle = true; //Prevent the work loop from continuing to loop
                if (allowCompleteCycle) { return false; }

                runningTask = vRunningTask;
                // Cancel under the lock so a racing Start() cannot swap the token first.
                try { vCancellation?.Cancel(); }
                catch (ObjectDisposedException) { }
                return true;
            }
        }

        private void CompleteStop()
        {
            lock (vSyncRoot)
            {
                // The work loop clears these in its finally block, but it may have timed
                // out above, in which case the component is still reported as stopped.
                vRunning = false;
                vRunningTask = null;
                vCancellation?.Dispose();
                vCancellation = null;
            }
        }
        #endregion
        #endregion
    }
}
