using System;
using System.Collections.Generic;
using System.Diagnostics;
using JamaaTech.Smpp.Net.Lib.Protocol;
using System.Threading.Tasks;

namespace JamaaTech.Smpp.Net.Lib
{

    public class ResponseHandlerV2 : IResponseHandler
    {
        #region Variables
        private int vDefaultResponseTimeout;
        private readonly IDictionary<uint, RetainedResponse> vResponseQueue;
        private readonly IDictionary<uint, PDUWaitContextAsync> vWaitingQueue;
        private readonly Queue<RetentionKey> vRetentionOrder;
        // A single lock covers both queues. Registering a waiter and retaining a response
        // have to be atomic with respect to each other: with separate locks a response can
        // be retained just after a waiter looked for it and just before it begins waiting,
        // and the waiter then blocks for its whole timeout with the response sitting in
        // the queue beside it.
        private readonly object vSyncRoot = new object();
        // Minimum enforced timeout (was hard-coded 5000). Made adjustable for testing.
        private static int sMinTimeout = 5000;
        // Small scheduling slack to avoid flakiness under heavy test concurrency
        private const int SchedulingSlackMs = 20;
        private static readonly double TicksPerMillisecond = Stopwatch.Frequency / 1000.0;
        #endregion

        #region Nested Types
        private sealed class RetainedResponse
        {
            internal RetainedResponse(ResponsePDU pdu, long stamp)
            {
                Pdu = pdu;
                Stamp = stamp;
            }

            internal ResponsePDU Pdu { get; }
            internal long Stamp { get; }
        }

        private struct RetentionKey
        {
            internal RetentionKey(uint sequenceNumber, long stamp)
            {
                SequenceNumber = sequenceNumber;
                Stamp = stamp;
            }

            internal uint SequenceNumber { get; }
            internal long Stamp { get; }
        }
        #endregion

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

        #region Constructors
        public ResponseHandlerV2()
        {
            vDefaultResponseTimeout = sMinTimeout; //Default min
            vWaitingQueue = new Dictionary<uint, PDUWaitContextAsync>(32);
            vResponseQueue = new Dictionary<uint, RetainedResponse>(32);
            vRetentionOrder = new Queue<RetentionKey>();
        }
        #endregion

        #region Properties
        public int DefaultResponseTimeout
        {
            get { return vDefaultResponseTimeout; }
            set
            {
                int timeOut = sMinTimeout;
                if (value > timeOut) { timeOut = value; }
                Interlocked.Exchange(ref vDefaultResponseTimeout, timeOut);
            }
        }

        /// <summary>
        /// Number of responses currently retained for waiters that have not registered yet.
        /// A response is removed as soon as it is delivered, so this stays near zero in
        /// normal traffic rather than growing with the age of the connection.
        /// </summary>
        public int Count
        {
            get { lock (vSyncRoot) { return vResponseQueue.Count; } }
        }
        #endregion

        #region Methods
        #region Interface Methods
        public void Handle(ResponsePDU pdu)
        {
            uint sequenceNumber = pdu.Header.SequenceNumber;

            lock (vSyncRoot)
            {
                PDUWaitContextAsync waitContext;
                if (vWaitingQueue.TryGetValue(sequenceNumber, out waitContext))
                {
                    vWaitingQueue.Remove(sequenceNumber);
                    // A context that has already timed out or been cancelled refuses the
                    // response. Nobody received it then, so fall through and retain it -
                    // the caller's final take still picks it up.
                    if (waitContext.TryAlertResponseReceived(pdu)) { return; }
                }

                RetainResponse(sequenceNumber, pdu);
            }
        }

        public ResponsePDU WaitResponse(RequestPDU pdu)
        {
            return WaitResponse(pdu, vDefaultResponseTimeout);
        }

        public ResponsePDU WaitResponse(RequestPDU pdu, int timeOut)
        {
            uint sequenceNumber = pdu.Header.SequenceNumber;

            if (timeOut < sMinTimeout) { timeOut = vDefaultResponseTimeout; }
            int effectiveTimeout = checked(timeOut + SchedulingSlackMs);

            // The synchronous and asynchronous paths share one wait context, so a response
            // signals both the same way. This one is TaskCompletionSource-based; the
            // AutoResetEvent variant it replaced is gone.
            var tcs = new TaskCompletionSource<ResponsePDU>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (vSyncRoot)
            {
                ResponsePDU retained = TakeResponse(sequenceNumber);
                if (retained != null) { return retained; }

                vWaitingQueue[sequenceNumber] =
                    new PDUWaitContextAsync(sequenceNumber, effectiveTimeout, tcs, CancellationToken.None);
            }

            try
            {
                return tcs.Task.GetAwaiter().GetResult();
            }
            catch (SmppResponseTimedOutException)
            {
                ResponsePDU late = TakeResponseLocked(sequenceNumber);
                if (late != null) { return late; }
                throw;
            }
            finally
            {
                lock (vSyncRoot) { vWaitingQueue.Remove(sequenceNumber); }
            }
        }

        public async Task<ResponsePDU> WaitResponseAsync(RequestPDU pdu, int timeOut, CancellationToken cancellationToken = default)
        {
            uint sequenceNumber = pdu.Header.SequenceNumber;

            if (timeOut < sMinTimeout) { timeOut = vDefaultResponseTimeout; }
            int effectiveTimeout = checked(timeOut + SchedulingSlackMs);

            var tcs = new TaskCompletionSource<ResponsePDU>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (vSyncRoot)
            {
                ResponsePDU retained = TakeResponse(sequenceNumber);
                if (retained != null) { return retained; }

                vWaitingQueue[sequenceNumber] =
                    new PDUWaitContextAsync(sequenceNumber, effectiveTimeout, tcs, cancellationToken);
            }

            try
            {
                return await tcs.Task.ConfigureAwait(false);
            }
            catch (SmppResponseTimedOutException)
            {
                ResponsePDU late = TakeResponseLocked(sequenceNumber);
                if (late != null) { return late; }
                throw;
            }
            finally
            {
                // Ensure removal of the waiting entry on completion (success/timeout/cancel)
                lock (vSyncRoot) { vWaitingQueue.Remove(sequenceNumber); }
            }
        }

        public async Task<ResponsePDU> WaitResponseAsync(RequestPDU pdu, CancellationToken cancellationToken = default)
        {
            return await WaitResponseAsync(pdu, vDefaultResponseTimeout, cancellationToken).ConfigureAwait(false);
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Retains a response nobody is waiting for yet, so a waiter registering moments
        /// from now still finds it. Caller must hold <see cref="vSyncRoot"/>.
        /// </summary>
        private void RetainResponse(uint sequenceNumber, ResponsePDU pdu)
        {
            long now = Stopwatch.GetTimestamp();
            PruneExpiredResponses(now);
            vResponseQueue[sequenceNumber] = new RetainedResponse(pdu, now);
            vRetentionOrder.Enqueue(new RetentionKey(sequenceNumber, now));
        }

        /// <summary>
        /// Removes and returns a retained response. Delivery is once-only: leaving it in
        /// place is what used to grow the queue without bound for the life of the session.
        /// Caller must hold <see cref="vSyncRoot"/>.
        /// </summary>
        private ResponsePDU TakeResponse(uint sequenceNumber)
        {
            RetainedResponse retained;
            if (!vResponseQueue.TryGetValue(sequenceNumber, out retained)) { return null; }
            vResponseQueue.Remove(sequenceNumber);

            // Every queued key refers to an entry of this dictionary, so once it is empty
            // each remaining key is stale. Dropping them here keeps a burst of retained
            // responses from leaving its bookkeeping behind until the next one arrives.
            if (vResponseQueue.Count == 0) { vRetentionOrder.Clear(); }

            return retained.Pdu;
        }

        private ResponsePDU TakeResponseLocked(uint sequenceNumber)
        {
            lock (vSyncRoot) { return TakeResponse(sequenceNumber); }
        }

        /// <summary>
        /// Drops retained responses that no waiter can still claim. Without this, a peer
        /// that answers requests their sender has already abandoned would grow the queue
        /// for as long as the session lives. Caller must hold <see cref="vSyncRoot"/>.
        /// </summary>
        private void PruneExpiredResponses(long now)
        {
            long maxAge = (long)(vDefaultResponseTimeout * TicksPerMillisecond);

            while (vRetentionOrder.Count > 0)
            {
                RetentionKey oldest = vRetentionOrder.Peek();
                if (now - oldest.Stamp < maxAge) { break; }
                vRetentionOrder.Dequeue();

                // Only drop the entry this key actually refers to. The sequence number may
                // have been reused since, and that newer response is not expired yet.
                RetainedResponse retained;
                if (vResponseQueue.TryGetValue(oldest.SequenceNumber, out retained)
                    && retained.Stamp == oldest.Stamp)
                {
                    vResponseQueue.Remove(oldest.SequenceNumber);
                }
            }
        }
        #endregion
        #endregion
    }
}
