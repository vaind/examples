using System.Diagnostics;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.EventPipe;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

internal static class Program
{
    internal static EventPipeProvider[] Providers = new[]
    {
        new EventPipeProvider(ClrTraceEventParser.ProviderName, EventLevel.Informational, (long) ClrTraceEventParser.Keywords.Default),
        new EventPipeProvider(SampleProfilerTraceEventParser.ProviderName, EventLevel.Informational),
    };

    private static async Task Main()
    {
        var client = new DiagnosticsClient(Environment.ProcessId);

        using var session = client.StartEventPipeSession(Providers, requestRundown: false);

        using var eventSource = CreateFromEventPipeSession(session, client);

        async Task WaitForFirstEventAsync()
        {
            var tcs = new TaskCompletionSource();
            var cb = (TraceEvent _) => { tcs.TrySetResult(); };
            eventSource.AllEvents += cb;
            try
            {
                // Wait for the first event to be processed.
                await tcs.Task.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                eventSource.AllEvents -= cb;
            }
        }

        eventSource.Clr.MethodLoadVerbose += (MethodLoadUnloadVerboseTraceData data) => LogIfRelevant("MethodLoadVerbose", data.MethodNamespace, data.MethodName);
        eventSource.Clr.MethodUnloadVerbose += (MethodLoadUnloadVerboseTraceData data) => LogIfRelevant("MethodUnloadVerbose", data.MethodNamespace, data.MethodName);

        // Start EventSource processing in the background.
        var processingTask = Task.Run(eventSource.Process);
        await WaitForFirstEventAsync().ConfigureAwait(false);

        // Do some work.
        await Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 1000)
            {
                Console.WriteLine("computation result: {0}", FindPrimeNumber(100000));
                Thread.Sleep(100);
            }
        });

        // Clean up and finish gracefully.
        Console.WriteLine("stopping session...");
        await session.StopAsync(CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine("waiting for the background processing to finish...");
        try
        {
            await processingTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException e)
        {
            Console.WriteLine("Processing task: {0}", e.Message);
        }

        Console.WriteLine("program finished");
    }

    // Copied from TraceLog.cs and modified to do the rundown here so we can log the methods.
    public static TraceLogEventSource CreateFromEventPipeSession(EventPipeSession session, DiagnosticsClient rundownDiagnosticsClient)
    {
        using (var rundownSession = rundownDiagnosticsClient.StartEventPipeSession(
            new EventPipeProvider(ClrTraceEventParser.ProviderName, EventLevel.Informational, (long)ClrTraceEventParser.Keywords.Default),
            requestRundown: true
        ))
        {
            ProcessInitialRundown(rundownSession);
        }

        return TraceLog.CreateFromEventPipeSession(session);
    }

    // Copied from TraceLog.cs
    private static void ProcessInitialRundown(EventPipeSession session)
    {
        using (var source = new EventPipeEventSource(session.EventStream))
        {
            // SetupInitialRundownCallbacks(source);
            var clrRundownParser = new ClrRundownTraceEventParser(source);
            clrRundownParser.MethodDCStopVerbose += (MethodLoadUnloadVerboseTraceData data) => LogIfRelevant("MethodDCStopVerbose", data.MethodNamespace, data.MethodName);

            // Only stopping the session will cause the rundown events to be sent.
            // However, we cannot stop it before starting to process the source.
            // Therefore, we need attach to the first event and only then stop the session.
            var completionSource = new TaskCompletionSource<bool>();
            source.AllEvents += delegate (TraceEvent _)
            {
                completionSource.TrySetResult(true);
            };
            var task = Task.Run(source.Process);
            completionSource.Task.Wait();
            session.Stop();
            task.Wait();
        }
    }

    private static void LogIfRelevant(String eventName, String methodNamespace, String methodName)
    {
        if (methodNamespace.StartsWith("Program"))
        {
            Console.WriteLine("{0}: {1} {2}", eventName, methodNamespace, methodName);
        }
    }

    private static long FindPrimeNumber(int n)
    {
        int count = 0;
        long a = 2;
        while (count < n)
        {
            long b = 2;
            int prime = 1;// to check if found a prime
            while (b * b <= a)
            {
                if (a % b == 0)
                {
                    prime = 0;
                    break;
                }
                b++;
            }
            if (prime > 0)
            {
                count++;
            }
            a++;
        }
        return (--a);
    }
}
