using System;
using System.IO;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Services;

namespace Sufni.App.Services.LiveStreaming;

internal sealed class LiveDaqClient(IBackgroundTaskRunner backgroundTaskRunner) : ILiveDaqClient
{
    private readonly LiveProtocolReader reader = new();
    private readonly Subject<LiveDaqClientEvent> events = new();
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);

    private TcpClient? tcpClient;
    private NetworkStream? stream;
    private CancellationTokenSource? receiveLoopCts;
    private Task? receiveLoopTask;
    private TaskCompletionSource<LivePreviewStartResult>? pendingStartResult;
    private TaskCompletionSource<uint>? pendingStopAck;
    private LiveStartAck? startAckAwaitingHeader;
    private uint? activeSessionId;
    private uint nextSequence;
    private bool isDisposed;
    private bool intentionalDisconnect;

    public bool IsConnected => tcpClient?.Connected == true && stream is not null;

    public IObservable<LiveDaqClientEvent> Events => events.AsObservable();

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (IsConnected)
            {
                return;
            }

            reader.Reset();
            intentionalDisconnect = false;
            receiveLoopCts?.Dispose();
            tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(host, port, cancellationToken);
            stream = tcpClient.GetStream();
            receiveLoopCts = new CancellationTokenSource();
            receiveLoopTask = backgroundTaskRunner.RunAsync(
                async () => await ReceiveLoopAsync(receiveLoopCts.Token),
                receiveLoopCts.Token);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async Task<LivePreviewStartResult> StartPreviewAsync(LiveStartRequest request, CancellationToken cancellationToken = default)
    {
        Task<LivePreviewStartResult> task;
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (!IsConnected || stream is null)
            {
                return new LivePreviewStartResult.Failed("Live client is not connected.");
            }

            if (pendingStartResult is not null)
            {
                return new LivePreviewStartResult.Failed("A live preview request is already in progress.");
            }

            var tcs = new TaskCompletionSource<LivePreviewStartResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingStartResult = tcs;
            startAckAwaitingHeader = null;
            var frame = LiveProtocolReader.CreateStartLiveFrame(GetNextSequence(), request);
            await stream.WriteAsync(frame, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            task = tcs.Task;
        }
        catch (Exception ex)
        {
            return new LivePreviewStartResult.Failed(ex.Message);
        }
        finally
        {
            lifecycleGate.Release();
        }

        try
        {
            return await task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await lifecycleGate.WaitAsync(CancellationToken.None);
            try
            {
                if (pendingStartResult?.Task == task)
                {
                    pendingStartResult = null;
                    startAckAwaitingHeader = null;
                }
            }
            finally
            {
                lifecycleGate.Release();
            }

            throw;
        }
    }

    public async Task StopPreviewAsync(CancellationToken cancellationToken = default)
    {
        Task<uint>? waitTask = null;
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsConnected || stream is null)
            {
                return;
            }

            var tcs = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingStopAck = tcs;
            var frame = LiveProtocolReader.CreateStopLiveFrame(GetNextSequence());
            await stream.WriteAsync(frame, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            waitTask = tcs.Task;
        }
        finally
        {
            lifecycleGate.Release();
        }

        if (waitTask is not null)
        {
            await waitTask.WaitAsync(cancellationToken);
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        Task? receiveLoopToAwait = null;
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (tcpClient is null && stream is null && receiveLoopTask is null)
            {
                return;
            }

            intentionalDisconnect = true;
            if (stream is not null && activeSessionId is not null)
            {
                try
                {
                    var frame = LiveProtocolReader.CreateStopLiveFrame(GetNextSequence());
                    await stream.WriteAsync(frame, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }
                catch
                {
                }
            }

            receiveLoopCts?.Cancel();
            stream?.Close();
            tcpClient?.Close();
            stream = null;
            tcpClient = null;
            activeSessionId = null;
            receiveLoopToAwait = receiveLoopTask;
            receiveLoopTask = null;
            pendingStartResult?.TrySetResult(new LivePreviewStartResult.Failed("Live preview disconnected before startup completed."));
            pendingStartResult = null;
            pendingStopAck?.TrySetException(new IOException("Disconnected before STOP_LIVE_ACK was received."));
            pendingStopAck = null;
            startAckAwaitingHeader = null;
        }
        finally
        {
            lifecycleGate.Release();
        }

        if (receiveLoopToAwait is not null)
        {
            try
            {
                await receiveLoopToAwait;
            }
            catch
            {
            }
        }

        events.OnNext(new LiveDaqClientEvent.Disconnected(null));
    }

    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        await DisconnectAsync();
        events.OnCompleted();
        lifecycleGate.Dispose();
        receiveLoopCts?.Dispose();
        tcpClient?.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var buffer = new byte[4096];
            while (!cancellationToken.IsCancellationRequested)
            {
                if (stream is null)
                {
                    return;
                }

                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    await HandleDisconnectAsync(null);
                    return;
                }

                reader.Append(buffer.AsSpan(0, read));
                while (reader.TryReadFrame(out var frame))
                {
                    await HandleFrameAsync(frame!);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await HandleDisconnectAsync(ex.Message);
        }
    }

    private async Task HandleFrameAsync(LiveProtocolFrame frame)
    {
        await lifecycleGate.WaitAsync(CancellationToken.None);
        try
        {
            switch (frame)
            {
                case LiveStartAckFrame startAckFrame:
                    if (startAckFrame.Payload.Result == LiveStartErrorCode.Ok)
                    {
                        startAckAwaitingHeader = startAckFrame.Payload;
                    }
                    else
                    {
                        pendingStartResult?.TrySetResult(
                            new LivePreviewStartResult.Rejected(
                                startAckFrame.Payload.Result,
                                startAckFrame.Payload.Result.ToUserMessage()));
                        pendingStartResult = null;
                        startAckAwaitingHeader = null;
                    }
                    break;

                case LiveSessionHeaderFrame sessionHeaderFrame:
                    activeSessionId = sessionHeaderFrame.Payload.SessionId;
                    if (pendingStartResult is not null && startAckAwaitingHeader is not null)
                    {
                        pendingStartResult.TrySetResult(new LivePreviewStartResult.Started(sessionHeaderFrame.Payload));
                        pendingStartResult = null;
                        startAckAwaitingHeader = null;
                    }
                    break;

                case LiveErrorFrame errorFrame:
                    if (pendingStartResult is not null)
                    {
                        pendingStartResult.TrySetResult(
                            new LivePreviewStartResult.Rejected(
                                errorFrame.Payload.ErrorCode,
                                errorFrame.Payload.ErrorCode.ToUserMessage()));
                        pendingStartResult = null;
                        startAckAwaitingHeader = null;
                    }
                    break;

                case LiveStopAckFrame stopAckFrame:
                    activeSessionId = null;
                    pendingStopAck?.TrySetResult(stopAckFrame.Payload.SessionId);
                    pendingStopAck = null;
                    break;
            }
        }
        finally
        {
            lifecycleGate.Release();
        }

        events.OnNext(new LiveDaqClientEvent.FrameReceived(frame));
    }

    private async Task HandleDisconnectAsync(string? errorMessage)
    {
        await lifecycleGate.WaitAsync(CancellationToken.None);
        try
        {
            stream?.Close();
            tcpClient?.Close();
            stream = null;
            tcpClient = null;
            activeSessionId = null;
            receiveLoopCts?.Cancel();
            receiveLoopCts?.Dispose();
            receiveLoopCts = null;
            receiveLoopTask = null;

            if (pendingStartResult is not null)
            {
                pendingStartResult.TrySetResult(new LivePreviewStartResult.Failed(
                    errorMessage ?? "Live preview disconnected before startup completed."));
                pendingStartResult = null;
            }

            if (pendingStopAck is not null)
            {
                pendingStopAck.TrySetException(new IOException(errorMessage ?? "Disconnected before STOP_LIVE_ACK was received."));
                pendingStopAck = null;
            }

            startAckAwaitingHeader = null;
        }
        finally
        {
            lifecycleGate.Release();
        }

        if (intentionalDisconnect)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            events.OnNext(new LiveDaqClientEvent.Disconnected(null));
        }
        else
        {
            events.OnNext(new LiveDaqClientEvent.Faulted(errorMessage));
            events.OnNext(new LiveDaqClientEvent.Disconnected(errorMessage));
        }
    }

    private uint GetNextSequence() => unchecked(++nextSequence);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
    }
}

public sealed class LiveDaqClientFactory(IBackgroundTaskRunner backgroundTaskRunner) : ILiveDaqClientFactory
{
    public ILiveDaqClient CreateClient() => new LiveDaqClient(backgroundTaskRunner);
}