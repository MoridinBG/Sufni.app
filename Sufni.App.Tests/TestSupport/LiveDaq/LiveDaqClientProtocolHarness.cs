using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using Sufni.App.LiveDaq.Services.LiveStreaming;

namespace Sufni.App.Tests.TestSupport.LiveDaq;

internal sealed class LiveDaqClientProtocolHarness<TClient>
    where TClient : ILiveDaqClient
{
    private readonly Func<TClient> createClient;

    public LiveDaqClientProtocolHarness(Func<TClient> createClient)
    {
        this.createClient = createClient;
    }

    public async Task<TResult> WithServerAsync<TResult>(
        Func<NetworkStream, Task> serverScript,
        Func<TClient, int, Task<TResult>> clientScript)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var client = createClient();
        TcpClient? acceptedPeer = null;
        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync(lifetime.Token);
            Interlocked.Exchange(ref acceptedPeer, serverClient);
            lifetime.Token.ThrowIfCancellationRequested();
            await using var stream = serverClient.GetStream();
            await serverScript(stream);
        });
        // A script held by a test gate may outlive the bounded failure cleanup.
        _ = serverTask.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        var failed = false;
        try
        {
            var result = await clientScript(client, port);
            await serverTask.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
            return result;
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            lifetime.Cancel();
            listener.Stop();
            Interlocked.Exchange(ref acceptedPeer, null)?.Dispose();
            try
            {
                try { await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)); }
                catch when (failed) { }
            }
            finally
            {
                // Cleanup also runs after runner cancellation, with its own bounded wait.
                try { await serverTask.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch when (failed) { }
            }
        }
    }

    public static async Task<IReadOnlyList<LiveProtocolFrame>> ObserveFramesAsync(
        ILiveDaqClient client,
        int expectedCount,
        Func<Task> action)
    {
        var observedFrames = new List<LiveProtocolFrame>();
        var framesObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Events.Subscribe(clientEvent =>
        {
            if (clientEvent is not LiveDaqClientEvent.FrameReceived frameReceived)
            {
                return;
            }

            lock (observedFrames)
            {
                observedFrames.Add(frameReceived.Frame);
                if (observedFrames.Count >= expectedCount)
                {
                    framesObserved.TrySetResult();
                }
            }
        });

        await action();
        await framesObserved.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        return observedFrames;
    }

    public static async Task<LiveDaqClientDropCounters> ObserveDropCountersAsync(
        ILiveDaqClient client,
        Func<LiveDaqClientDropCounters, bool> predicate,
        Func<Task> action)
    {
        var observed = new TaskCompletionSource<LiveDaqClientDropCounters>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Events.Subscribe(clientEvent =>
        {
            if (clientEvent is LiveDaqClientEvent.DropCountersChanged countersChanged &&
                predicate(countersChanged.Counters))
            {
                observed.TrySetResult(countersChanged.Counters);
            }
        });

        await action();
        return await observed.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
    }
}
