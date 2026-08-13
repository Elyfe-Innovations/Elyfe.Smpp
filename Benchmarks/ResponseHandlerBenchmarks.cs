using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using JamaaTech.Smpp.Net.Lib;
using JamaaTech.Smpp.Net.Lib.Testing;
using JamaaTech.Smpp.Net.Lib.Protocol;

namespace JamaaTech.Smpp.Net.Lib.Benchmarks;

[MemoryDiagnoser]
[ThreadingDiagnoser]
public class ResponseHandlerBenchmarks
{
    private ResponseHandlerV2 _handlerV2 = null!;
    private ConcurrentResponseHandler _handlerV3 = null!;

    [Params(1, 10, 100)]
    public int Pending;

    [Params(1, 4, 16)]
    public int Parallelism;

    private TestRequestPDU[] _requests = null!;
    private TestResponsePDU[] _responses = null!;

    [GlobalSetup]
    public void Setup()
    {
        ResponseHandlerV2.SetMinimumTimeoutForTesting(1);
        ConcurrentResponseHandler.SetMinimumTimeoutForTesting(1);
        _handlerV2 = new ResponseHandlerV2();
        _handlerV3 = new ConcurrentResponseHandler();
        _requests = new TestRequestPDU[Pending];
        _responses = new TestResponsePDU[Pending];
        for (uint i = 0; i < Pending; i++)
        {
            _requests[i] = new TestRequestPDU(i + 1);
            _responses[i] = new TestResponsePDU(i + 1);
        }
    }


    [Benchmark]
    public void V2_WaitAndHandleSequential()
    {
        for (int i = 0; i < Pending; i++)
        {
            var req = _requests[i];
            var resp = _responses[i];
            var waitThread = new Thread(() => _handlerV2.WaitResponse(req, 5));
            waitThread.Start();
            _handlerV2.Handle(resp);
            waitThread.Join();
        }
    }

    [Benchmark]
    public void V3_WaitAndHandleSequential()
    {
        for (int i = 0; i < Pending; i++)
        {
            var req = _requests[i];
            var resp = _responses[i];
            var waitThread = new Thread(() => _handlerV3.WaitResponse(req, 5));
            waitThread.Start();
            _handlerV3.Handle(resp);
            waitThread.Join();
        }
    }


    [Benchmark]
    public void V2_HandleOnly()
    {
        for (int i = 0; i < Pending; i++)
        {
            _handlerV2.Handle(_responses[i]);
        }
    }

    [Benchmark]
    public void V3_HandleOnly()
    {
        for (int i = 0; i < Pending; i++)
        {
            _handlerV3.Handle(_responses[i]);
        }
    }

    // Parallel variants (sync wait/handle)

    [Benchmark]
    public void V2_WaitAndHandleParallel()
    {
        var options = new ParallelOptions { MaxDegreeOfParallelism = Parallelism };
        Parallel.For(0, Pending, options, i =>
        {
            var req = _requests[i];
            var resp = _responses[i];
            var t = Task.Run(() => _handlerV2.WaitResponse(req, 5));
            _handlerV2.Handle(resp);
            t.GetAwaiter().GetResult();
        });
    }

    [Benchmark]
    public void V3_WaitAndHandleParallel()
    {
        var options = new ParallelOptions { MaxDegreeOfParallelism = Parallelism };
        Parallel.For(0, Pending, options, i =>
        {
            var req = _requests[i];
            var resp = _responses[i];
            var t = Task.Run(() => _handlerV3.WaitResponse(req, 5));
            _handlerV3.Handle(resp);
            t.GetAwaiter().GetResult();
        });
    }

    // Async variants for V2
    [Benchmark]
    public async Task V2_WaitAndHandleAsync_Sequential()
    {
        for (int i = 0; i < Pending; i++)
        {
            var req = _requests[i];
            var resp = _responses[i];
            var waitTask = _handlerV2.WaitResponseAsync(req, 5);
            _handlerV2.Handle(resp);
            await waitTask.ConfigureAwait(false);
        }
    }

    [Benchmark]
    public async Task V2_WaitAndHandleAsync_Parallel()
    {
        var tasks = new Task<ResponsePDU>[Pending];
        for (int i = 0; i < Pending; i++)
        {
            tasks[i] = _handlerV2.WaitResponseAsync(_requests[i], 5);
        }
        for (int i = 0; i < Pending; i++)
        {
            _handlerV2.Handle(_responses[i]);
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    // Response-first (no blocking) scenarios

    [Benchmark]
    public void V2_ResponseBeforeWait()
    {
        for (int i = 0; i < Pending; i++)
        {
            _handlerV2.Handle(_responses[i]);
            _ = _handlerV2.WaitResponse(_requests[i], 5);
        }
    }

    [Benchmark]
    public void V3_ResponseBeforeWait()
    {
        for (int i = 0; i < Pending; i++)
        {
            _handlerV3.Handle(_responses[i]);
            _ = _handlerV3.WaitResponse(_requests[i], 5);
        }
    }

    // Timeout scenarios (catch and ignore expected timeouts)

    [Benchmark]
    public void V2_Timeouts()
    {
        for (int i = 0; i < Pending; i++)
        {
            try { _ = _handlerV2.WaitResponse(_requests[i], 1); }
            catch (SmppResponseTimedOutException) { }
        }
    }

    [Benchmark]
    public void V3_Timeouts()
    {
        for (int i = 0; i < Pending; i++)
        {
            try { _ = _handlerV3.WaitResponse(_requests[i], 1); }
            catch (SmppResponseTimedOutException) { }
        }
    }
}
