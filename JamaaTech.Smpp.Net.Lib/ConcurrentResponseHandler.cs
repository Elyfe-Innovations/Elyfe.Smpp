/************************************************************************
 * Thread-safer response handler implementation.
 * Removes race between initial fetch and waiter registration,
 * cleans up consumed responses & wait contexts.
 ************************************************************************/
using System.Collections.Generic;
using System.Threading.Tasks;
using JamaaTech.Smpp.Net.Lib.Protocol;

namespace JamaaTech.Smpp.Net.Lib
{
    public class ConcurrentResponseHandler : IResponseHandler
    {
        private int vDefaultResponseTimeout;
        private readonly IDictionary<uint, ResponsePDU> _responses = new Dictionary<uint, ResponsePDU>(32);
        private readonly IDictionary<uint, Waiter> _waiters = new Dictionary<uint, Waiter>(32);
        private readonly object _responsesLock = new object();
        private readonly object _waitersLock = new object();
        // Minimum enforced timeout (was hard-coded 5000). Made adjustable for testing.
        private static int sMinTimeout = 5000;

        #region Testing Helpers
        /// <summary>
        /// Adjust minimum timeout (intended for unit testing). Use cautiously in production.
        /// </summary>
        public static void SetMinimumTimeoutForTesting(int milliseconds)
        {
            if (milliseconds < 1) { milliseconds = 1; }
            Interlocked.Exchange(ref sMinTimeout, milliseconds);
        }
        /// <summary>
        /// Returns current minimum enforced timeout.
        /// </summary>
        public static int GetMinimumTimeoutForTesting() { return sMinTimeout; }
        #endregion

        public ConcurrentResponseHandler()
        {
            vDefaultResponseTimeout = sMinTimeout; //Default min
        }

        public ConcurrentResponseHandler(ResponseHandlerOptions options)
            : this()
        {
            if (options != null)
            {
                DefaultResponseTimeout = options.DefaultResponseTimeout;
            }
        }

        public int DefaultResponseTimeout
        {
            get => vDefaultResponseTimeout;
            set
            {
                var min = sMinTimeout;
                if (value > min) min = value;
                Interlocked.Exchange(ref vDefaultResponseTimeout, min);
            }
        }

        public int Count
        {
            get
            {
                lock (_responsesLock)
                {
                    return _responses.Count;
                }
            }
        }

        public void Handle(ResponsePDU pdu)
        {
            var seq = pdu.Header.SequenceNumber;

            // Store response first so a late waiter can fetch it.
            lock (_responsesLock)
            {
                _responses[seq] = pdu;
            }

            Waiter ctx = null;
            lock (_waitersLock)
            {
                if (_waiters.TryGetValue(seq, out ctx))
                {
                    _waiters.Remove(seq);
                }
            }

            if (ctx != null)
            {
                if (ctx.TimedOut)
                {
                    // Remove orphaned response (nobody will consume it).
                    lock (_responsesLock)
                    {
                        _responses.Remove(seq);
                    }
                }
                else
                {
                    ctx.AlertResponseReceived();
                }
            }
        }

        public ResponsePDU WaitResponse(RequestPDU pdu)
            => WaitResponse(pdu, vDefaultResponseTimeout);

        public ResponsePDU WaitResponse(RequestPDU pdu, int timeOut)
        {
            var seq = pdu.Header.SequenceNumber;

            // Fast path
            var existing = Fetch(seq);
            if (existing != null) return existing;

            if (timeOut < sMinTimeout) timeOut = vDefaultResponseTimeout;
            var ctx = new Waiter();

            // Register waiter then re-check to close race
            lock (_waitersLock)
            {
                _waiters[seq] = ctx;
                existing = Fetch(seq);
                if (existing != null)
                {
                    _waiters.Remove(seq);
                    return existing;
                }
            }

            // Await signal or timeout
            ctx.WaitForAlert(timeOut);

            var resp = Fetch(seq);
            if (resp == null)
            {
                // Ensure removal if timeout path taken
                lock (_waitersLock)
                {
                    _waiters.Remove(seq);
                }
                throw new SmppResponseTimedOutException();
            }
            return resp;
        }

        /// <summary>
        /// A one-shot signal for a pending sequence number. Backed by a
        /// TaskCompletionSource rather than an AutoResetEvent, so nothing needs disposing.
        /// </summary>
        private sealed class Waiter
        {
            private readonly TaskCompletionSource<bool> _signal =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            private int _timedOut;

            public bool TimedOut => Volatile.Read(ref _timedOut) != 0;

            /// <summary>Blocks until the response arrives or <paramref name="timeOut"/> elapses.</summary>
            public bool WaitForAlert(int timeOut)
            {
                if (_signal.Task.Wait(timeOut)) { return true; }
                Interlocked.Exchange(ref _timedOut, 1);
                return false;
            }

            /// <inheritdoc cref="WaitForAlert"/>
            public async Task<bool> WaitForAlertAsync(int timeOut, CancellationToken cancellationToken)
            {
                var completed = await Task
                    .WhenAny(_signal.Task, Task.Delay(timeOut, cancellationToken))
                    .ConfigureAwait(false);

                if (completed == _signal.Task) { return true; }

                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Exchange(ref _timedOut, 1);
                return false;
            }

            public void AlertResponseReceived() => _signal.TrySetResult(true);
        }

        public Task<ResponsePDU> WaitResponseAsync(RequestPDU pdu, CancellationToken cancellationToken = default)
            => WaitResponseAsync(pdu, vDefaultResponseTimeout, cancellationToken);

        public async Task<ResponsePDU> WaitResponseAsync(RequestPDU pdu, int timeOut, CancellationToken cancellationToken = default)
        {
            var seq = pdu.Header.SequenceNumber;

            // Fast path
            var existing = Fetch(seq);
            if (existing != null) return existing;

            if (timeOut < sMinTimeout) timeOut = vDefaultResponseTimeout;
            var ctx = new Waiter();

            // Register waiter then re-check to close race
            lock (_waitersLock)
            {
                _waiters[seq] = ctx;
                existing = Fetch(seq);
                if (existing != null)
                {
                    _waiters.Remove(seq);
                    return existing;
                }
            }

            // Await signal or timeout. The waiter must be removed even when the token is
            // cancelled mid-wait, otherwise a cancelled wait leaks its waiter entry.
            try
            {
                await ctx.WaitForAlertAsync(timeOut, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                lock (_waitersLock)
                {
                    _waiters.Remove(seq);
                }
            }

            var resp = Fetch(seq);
            if (resp == null)
            {
                throw new SmppResponseTimedOutException();
            }
            return resp;
        }

        private ResponsePDU Fetch(uint seq)
        {
            lock (_responsesLock)
            {
                if (_responses.TryGetValue(seq, out var pdu))
                {
                    _responses.Remove(seq); // Consume once
                    return pdu;
                }
                return null;
            }
        }
    }
}
