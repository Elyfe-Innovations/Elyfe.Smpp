using System;
using System.Threading;
using System.Threading.Tasks;
using JamaaTech.Smpp.Net.Lib;
using JamaaTech.Smpp.Net.Lib.Protocol;
using JamaaTech.Smpp.Net.Lib.Testing;
using Xunit;

namespace JamaaTech.Smpp.Net.Lib.Tests
{
    public class PDUWaitContextAsyncTests
    {
        // Long enough that the timeout timer cannot fire while a test that is not about
        // timeouts is still running, even on a loaded CI runner.
        private const int NoTimeout = 60_000;

        [Fact]
        public async Task AlertResponseReceived_CompletesTask()
        {
            var tcs = new TaskCompletionSource<ResponsePDU>();
            var context = new PDUWaitContextAsync(123, NoTimeout, tcs, CancellationToken.None);
            var response = new TestResponsePDU(123);

            // Alert that response was received
            context.AlertResponseReceived(response);

            // Task should be completed with the response
            Assert.True(tcs.Task.IsCompleted);
            Assert.Same(response, await tcs.Task);
            Assert.True(context.Completed);
        }

        [Fact]
        public async Task AlertResponseReceived_MultipleCalls_OnlyFirstSucceeds()
        {
            var tcs = new TaskCompletionSource<ResponsePDU>();
            var context = new PDUWaitContextAsync(123, NoTimeout, tcs, CancellationToken.None);
            var response1 = new TestResponsePDU(123);
            var response2 = new TestResponsePDU(123);

            // First alert should succeed
            context.AlertResponseReceived(response1);
            Assert.True(context.Completed);
            Assert.Same(response1, await tcs.Task);

            // Second alert should be ignored
            context.AlertResponseReceived(response2);
            Assert.Same(response1, await tcs.Task); // Should still be first response
        }

        [Fact]
        public async Task Timeout_ThrowsException()
        {
            var tcs = new TaskCompletionSource<ResponsePDU>();
            var context = new PDUWaitContextAsync(123, 50, tcs, CancellationToken.None);

            // Wait for timeout
            var exception = await Assert.ThrowsAsync<SmppResponseTimedOutException>(() => tcs.Task);

            Assert.True(context.Completed);
            Assert.True(context.TimedOut);
        }

        [Fact]
        public async Task Cancellation_CancelsTask()
        {
            var cts = new CancellationTokenSource();
            var tcs = new TaskCompletionSource<ResponsePDU>();
            var context = new PDUWaitContextAsync(123, NoTimeout, tcs, cts.Token);

            // Cancel the operation
            await cts.CancelAsync();

            // Task should be cancelled
            Assert.True(tcs.Task.IsCanceled);
            Assert.True(context.Completed);
        }

        [Fact]
        public void Dispose_CancelsTask()
        {
            var tcs = new TaskCompletionSource<ResponsePDU>();
            var context = new PDUWaitContextAsync(123, NoTimeout, tcs, CancellationToken.None);

            // Dispose the context
            context.Dispose();

            // Task should be cancelled
            Assert.True(tcs.Task.IsCanceled);
            Assert.True(context.Completed);
        }

        [Fact]
        public async Task ConcurrentAccess_ThreadSafe()
        {
            var tcs = new TaskCompletionSource<ResponsePDU>();
            var context = new PDUWaitContextAsync(123, NoTimeout, tcs, CancellationToken.None);
            var response = new TestResponsePDU(123);
            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

            // Multiple threads trying to complete the context
            var tasks = new Task[10];
            for (int i = 0; i < 10; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    try
                    {
                        context.AlertResponseReceived(response);
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                });
            }

            await Task.WhenAll(tasks);

            // Should not have any exceptions
            Assert.Empty(exceptions);
            
            // Task should be completed
            Assert.True(tcs.Task.IsCompleted);
            Assert.Same(response, await tcs.Task);
        }

        [Fact]
        public async Task TimeoutAndResponse_ResponseWins()
        {
            var tcs = new TaskCompletionSource<ResponsePDU>();
            var context = new PDUWaitContextAsync(123, 100, tcs, CancellationToken.None);
            var response = new TestResponsePDU(123);

            // Send response before timeout
            Thread.Sleep(50);
            context.AlertResponseReceived(response);

            // Should get the response, not timeout
            Assert.Same(response,await tcs.Task);
            Assert.True(context.Completed);
            Assert.False(context.TimedOut);
        }

        [Fact]
        public async Task CancellationAndResponse_ResponseWins()
        {
            var cts = new CancellationTokenSource();
            var tcs = new TaskCompletionSource<ResponsePDU>();
            var context = new PDUWaitContextAsync(123, NoTimeout, tcs, cts.Token);
            var response = new TestResponsePDU(123);

            // Send response before cancellation
            context.AlertResponseReceived(response);

            // Cancel (should be ignored since already completed)
            await cts.CancelAsync();

            // Should get the response, not cancellation
            Assert.Same(response, await tcs.Task);
            Assert.True(context.Completed);
        }

        [Fact]
        public void MultipleDispose_NoException()
        {
            var tcs = new TaskCompletionSource<ResponsePDU>();
            var context = new PDUWaitContextAsync(123, NoTimeout, tcs, CancellationToken.None);

            // Multiple dispose calls should not throw
            context.Dispose();
            context.Dispose();
            context.Dispose();

            Assert.True(context.Completed);
        }

        [Fact]
        public void Properties_ReturnCorrectValues()
        {
            var tcs = new TaskCompletionSource<ResponsePDU>();
            var context = new PDUWaitContextAsync(123, NoTimeout, tcs, CancellationToken.None);

            Assert.Equal(123u, context.SequenceNumber);
            Assert.False(context.Completed);
            Assert.False(context.TimedOut);
        }
    }
}
