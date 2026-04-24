using System;
using System.IO;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Sufni.App.Services;
using Serilog;

namespace Sufni.App.Services.LiveStreaming;

// Socket-backed live preview transport client. One instance per DAQ connection.
//
// Lifecycle: ConnectAsync (TCP) → StartPreviewAsync (sends START_LIVE, awaits ACK + SESSION_HEADER)
//   → background socket receive loop drains bytes into a channel → background parse loop decodes
//   frames and emits Events → StopPreviewAsync (sends STOP_LIVE, awaits STOP_ACK) →
//   DisconnectAsync (tears down socket and both background loops).
//
// The socket drain loop and the parse loop both run off the UI thread on background tasks started
// with Task.Factory.StartNew. Start/stop handshakes use a TaskCompletionSource that the caller
// awaits while the parse loop completes it on the matching ACK or error frame. All state mutations
// are serialized through lifecycleGate.
internal sealed class LiveDaqClient : ILiveDaqClient
{
    // Default upper bound on the STOP_ACK wait so an unresponsive firmware cannot
    // hang StopPreviewAsync indefinitely when the caller passed CancellationToken.None.
    private static readonly TimeSpan DefaultStopAckTimeout = TimeSpan.FromSeconds(5);

    private static readonly ILogger logger = Log.ForContext<LiveDaqClient>();

    private readonly TimeSpan stopAckTimeout;
    private readonly Func<TcpClient> tcpClientFactory;
    private readonly Func<NetworkStream, byte[], CancellationToken, Task> sendFrameAsync;
    private readonly LiveProtocolReader reader = new();
    private readonly Subject<LiveDaqClientEvent> events = new();
    // Serializes all public lifecycle methods (Connect, Start, Stop, Disconnect, Dispose) and
    // the background receive loop's frame/disconnect handlers so that state mutations (pending TCS
    // completions, stream/CTS swaps, disposed flag) never interleave.
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);

    private readonly record struct ReceivedChunk(byte[] Bytes);

    private TcpClient? tcpClient;
    private NetworkStream? stream;
    private CancellationTokenSource? receiveLoopCts;
    private Channel<ReceivedChunk>? receiveChunks;
    private Task? receiveLoopTask;
    private Task? receiveParseTask;
    private TaskCompletionSource<LivePreviewStartResult>? pendingStartResult;
    private TaskCompletionSource<uint>? pendingStopAck;
    private LiveStartAck? startAckAwaitingHeader;
    private uint? activeSessionId;
    private uint nextSequence;
    private bool isDisposed;
    private bool intentionalDisconnect;

    public LiveDaqClient()
        : this(DefaultStopAckTimeout, static () => new TcpClient(), SendFrameAsync)
    {
    }

    internal LiveDaqClient(TimeSpan stopAckTimeout)
        : this(stopAckTimeout, static () => new TcpClient(), SendFrameAsync)
    {
    }

    internal LiveDaqClient(TimeSpan stopAckTimeout, Func<TcpClient> tcpClientFactory)
        : this(stopAckTimeout, tcpClientFactory, SendFrameAsync)
    {
    }

    internal LiveDaqClient(
        TimeSpan stopAckTimeout,
        Func<TcpClient> tcpClientFactory,
        Func<NetworkStream, byte[], CancellationToken, Task> sendFrameAsync)
    {
        this.stopAckTimeout = stopAckTimeout;
        this.tcpClientFactory = tcpClientFactory;
        this.sendFrameAsync = sendFrameAsync;
    }

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

            logger.Debug("Connecting live DAQ client to {Host} {Port}", host, port);

            reader.Reset();
            intentionalDisconnect = false;
            receiveLoopCts?.Dispose();
            var nextTcpClient = tcpClientFactory();
            try
            {
                await nextTcpClient.ConnectAsync(host, port, cancellationToken);
            }
            catch
            {
                nextTcpClient.Dispose();
                throw;
            }

            tcpClient = nextTcpClient;
            stream = tcpClient.GetStream();
            receiveLoopCts = new CancellationTokenSource();
            receiveChunks = Channel.CreateUnbounded<ReceivedChunk>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
            receiveLoopTask = Task.Factory
                .StartNew(
                    () => ReceiveLoopAsync(receiveLoopCts.Token),
                    receiveLoopCts.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default)
                .Unwrap();
            receiveParseTask = Task.Factory
                .StartNew(
                    () => ParseLoopAsync(receiveLoopCts.Token),
                    receiveLoopCts.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default)
                .Unwrap();
            logger.Debug("Live DAQ client socket connected to {Host} {Port}", host, port);
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
            logger.Debug(
                "Sending live DAQ preview start request with sensors {SensorMask}, travel {TravelHz}, imu {ImuHz}, gps {GpsFixHz}",
                request.SensorMask,
                request.TravelHz,
                request.ImuHz,
                request.GpsFixHz);
            var frame = LiveProtocolReader.CreateStartLiveFrame(GetNextSequence(), request);
            await sendFrameAsync(stream, frame, cancellationToken);
            task = tcs.Task;
        }
        catch (Exception ex)
        {
            pendingStartResult = null;
            startAckAwaitingHeader = null;
            logger.Warning(ex, "Failed to send live preview start request");
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
            if (!IsConnected || stream is null || activeSessionId is null)
            {
                return;
            }

            var tcs = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingStopAck = tcs;
            logger.Debug("Sending live DAQ preview stop request");
            var frame = LiveProtocolReader.CreateStopLiveFrame(GetNextSequence());
            await sendFrameAsync(stream, frame, cancellationToken);
            waitTask = tcs.Task;
        }
        finally
        {
            lifecycleGate.Release();
        }

        if (waitTask is null)
        {
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(stopAckTimeout);
        try
        {
            await waitTask.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            await ClearPendingStopAckAsync(waitTask);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
        }
    }

    private async Task ClearPendingStopAckAsync(Task<uint> waitTask)
    {
        await lifecycleGate.WaitAsync(CancellationToken.None);
        try
        {
            if (pendingStopAck?.Task == waitTask)
            {
                pendingStopAck = null;
            }
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        Task? receiveLoopToAwait = null;
        Task? receiveParseToAwait = null;
        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (tcpClient is null && stream is null && receiveLoopTask is null && receiveParseTask is null)
            {
                return;
            }

            logger.Debug("Disconnecting live DAQ client intentionally");

            intentionalDisconnect = true;
            if (stream is not null && activeSessionId is not null)
            {
                try
                {
                    var frame = LiveProtocolReader.CreateStopLiveFrame(GetNextSequence());
                    await sendFrameAsync(stream, frame, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Failed to send STOP_LIVE frame during disconnect");
                }
            }

            receiveLoopCts?.Cancel();
            receiveChunks?.Writer.TryComplete();
            stream?.Close();
            tcpClient?.Close();
            stream = null;
            tcpClient = null;
            activeSessionId = null;
            receiveLoopToAwait = receiveLoopTask;
            receiveParseToAwait = receiveParseTask;
            receiveLoopTask = null;
            receiveParseTask = null;
            receiveChunks = null;
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
                await receiveLoopToAwait.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Receive loop threw during disconnect");
            }
        }

        if (receiveParseToAwait is not null)
        {
            try
            {
                await receiveParseToAwait.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Parse loop threw during disconnect");
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

        await lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
        }
        finally
        {
            lifecycleGate.Release();
        }

        await DisconnectAsync().ConfigureAwait(false);
        events.OnCompleted();
        receiveLoopCts?.Dispose();
        tcpClient?.Dispose();
        lifecycleGate.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        ChannelWriter<ReceivedChunk>? writer = null;
        try
        {
            var buffer = new byte[4096];
            writer = receiveChunks?.Writer;
            while (!cancellationToken.IsCancellationRequested)
            {
                if (stream is null)
                {
                    return;
                }

                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    return;
                }

                if (writer is null)
                {
                    return;
                }

                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                await writer.WriteAsync(new ReceivedChunk(chunk), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            writer ??= receiveChunks?.Writer;
            writer?.TryComplete(ex);
            return;
        }
        finally
        {
            writer ??= receiveChunks?.Writer;
            writer?.TryComplete();
        }
    }

    private async Task ParseLoopAsync(CancellationToken cancellationToken)
    {
        var readerChannel = receiveChunks?.Reader;
        if (readerChannel is null)
        {
            return;
        }

        try
        {
            await foreach (var chunk in readerChannel.ReadAllAsync(cancellationToken))
            {
                reader.Append(chunk.Bytes);
                while (reader.TryReadFrame(out var frame))
                {
                    await HandleFrameAsync(frame!);
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                await HandleDisconnectAsync(null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await HandleDisconnectAsync(ex.Message, ex);
        }
    }

    private async Task HandleFrameAsync(LiveProtocolFrame frame)
    {
        await lifecycleGate.WaitAsync(CancellationToken.None);
        try
        {
            // Ignore frames that arrive after disconnect/dispose has begun — a late
            // SESSION_HEADER must not repopulate activeSessionId, and no lifecycle TCS
            // should be completed against a stale transport.
            if (isDisposed || intentionalDisconnect)
            {
                return;
            }

            switch (frame)
            {
                case LiveStartAckFrame startAckFrame:
                    if (startAckFrame.Payload.Result == LiveStartErrorCode.Ok)
                    {
                        logger.Debug(
                            "Received live DAQ preview start ACK for session {SessionId} with sensors {SelectedSensorMask}",
                            startAckFrame.Payload.SessionId,
                            startAckFrame.Payload.SelectedSensorMask);
                        startAckAwaitingHeader = startAckFrame.Payload;
                    }
                    else
                    {
                        logger.Debug(
                            "Received live DAQ preview start rejection {ErrorCode}",
                            startAckFrame.Payload.Result);
                        pendingStartResult?.TrySetResult(
                            new LivePreviewStartResult.Rejected(
                                startAckFrame.Payload.Result,
                                startAckFrame.Payload.Result.ToUserMessage()));
                        pendingStartResult = null;
                        startAckAwaitingHeader = null;
                    }
                    break;

                case LiveSessionHeaderFrame sessionHeaderFrame:
                    logger.Debug(
                        "Received live DAQ session header for session {SessionId}",
                        sessionHeaderFrame.Payload.SessionId);
                    activeSessionId = sessionHeaderFrame.Payload.SessionId;
                    if (pendingStartResult is not null && startAckAwaitingHeader is not null)
                    {
                        pendingStartResult.TrySetResult(new LivePreviewStartResult.Started(
                            sessionHeaderFrame.Payload,
                            startAckAwaitingHeader.Value.SelectedSensorMask));
                        pendingStartResult = null;
                        startAckAwaitingHeader = null;
                    }
                    break;

                case LiveErrorFrame errorFrame:
                    logger.Debug(
                        "Received live DAQ error frame with {ErrorCode}",
                        errorFrame.Payload.ErrorCode);
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
                    logger.Debug(
                        "Received live DAQ preview stop ACK for session {SessionId}",
                        stopAckFrame.Payload.SessionId);
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

    private async Task HandleDisconnectAsync(string? errorMessage, Exception? exception = null)
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
            receiveChunks?.Writer.TryComplete();
            receiveLoopCts?.Dispose();
            receiveLoopCts = null;
            receiveLoopTask = null;
            receiveParseTask = null;
            receiveChunks = null;

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
            logger.Verbose("Live DAQ client disconnected gracefully");
            events.OnNext(new LiveDaqClientEvent.Disconnected(null));
        }
        else
        {
            if (exception is not null)
            {
                logger.Error(exception, "Live DAQ client disconnected unexpectedly: {ErrorMessage}", errorMessage);
            }
            else
            {
                logger.Error("Live DAQ client disconnected unexpectedly: {ErrorMessage}", errorMessage);
            }

            events.OnNext(new LiveDaqClientEvent.Faulted(errorMessage));
            events.OnNext(new LiveDaqClientEvent.Disconnected(errorMessage));
        }
    }

    private uint GetNextSequence() => unchecked(++nextSequence);

    private static async Task SendFrameAsync(NetworkStream stream, byte[] frame, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    // Must be called inside lifecycleGate — the disposed flag is set under the gate in
    // DisposeAsync, so callers that already hold the gate get a consistent read.
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
    }
}

public sealed class LiveDaqClientFactory : ILiveDaqClientFactory
{
    public LiveDaqClientFactory()
    {
    }

    public LiveDaqClientFactory(IBackgroundTaskRunner _)
    {
    }

    // Returns a new transport client for one live preview tab.
    public ILiveDaqClient CreateClient() => new LiveDaqClient();
}