using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Shared.Common;
using Sufni.Telemetry;

namespace Sufni.App.LiveDaq.Services.LiveStreaming;

internal sealed class LiveDaqV3Client : ILiveDaqClient
{
    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(5);
    private const int DefaultRawFrameCapacity = 128;
    private const int DefaultParsedFrameCapacity = 256;
    private const ulong DropCounterPublishStride = 64;

    private static readonly ILogger logger = Log.ForContext<LiveDaqV3Client>();

    private readonly TimeSpan stopTimeout;
    private readonly string? expectedBoardId;
    private readonly int rawFrameCapacity;
    private readonly int parsedFrameCapacity;
    private readonly Func<TcpClient> tcpClientFactory;
    private readonly Func<NetworkStream, byte[], CancellationToken, Task> sendBytesAsync;
    private readonly Subject<LiveDaqClientEvent> events = new();
    private readonly System.Threading.Lock eventsGate = new();
    private readonly System.Threading.Lock dropCountersGate = new();
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly LiveV3ProtocolReader protocolReader = new();

    private readonly record struct RawFrameEnvelope(LiveV3FrameHeader Header, byte[] FrameBytes);

    private TcpClient? tcpClient;
    private NetworkStream? stream;
    private CancellationTokenSource? receiveLoopCts;
    private Channel<RawFrameEnvelope>? rawFrames;
    private Channel<LiveProtocolFrame>? parsedFrames;
    private Task? receiveLoopTask;
    private Task? parseLoopTask;
    private Task? publishLoopTask;
    private TaskCompletionSource<LiveV3Capabilities>? pendingCapabilities;
    private TaskCompletionSource<LiveV3DeviceState>? pendingDeviceState;
    private TaskCompletionSource<LivePreviewStartResult>? pendingStartResult;
    private TaskCompletionSource<LiveStopResult>? pendingStopResult;
    private TaskCompletionSource<LiveSessionResult>? pendingSessionResult;
    private LiveV3Capabilities? capabilities;
    private LiveV3ServerHello? serverHello;
    private byte? activeSessionId;
    private byte? startResultAwaitingHeaderSessionId;
    private uint nextSequence;
    private int rawFramesInFlight;
    private int parsedFramesInFlight;
    private LiveDaqClientDropCounters dropCounters = LiveDaqClientDropCounters.Empty;
    private ulong lastPublishedDropTotal;
    private bool isDisposed;
    private bool intentionalDisconnect;

    public LiveDaqV3Client()
        : this(expectedBoardId: null)
    {
    }

    internal LiveDaqV3Client(string? expectedBoardId)
        : this(DefaultStopTimeout, expectedBoardId, static () => new TcpClient(), SendBytesAsync)
    {
    }

    internal LiveDaqV3Client(TimeSpan stopTimeout)
        : this(stopTimeout, expectedBoardId: null, static () => new TcpClient(), SendBytesAsync)
    {
    }

    internal LiveDaqV3Client(TimeSpan stopTimeout, Func<TcpClient> tcpClientFactory)
        : this(stopTimeout, expectedBoardId: null, tcpClientFactory, SendBytesAsync)
    {
    }

    internal LiveDaqV3Client(
        TimeSpan stopTimeout,
        string? expectedBoardId,
        Func<TcpClient> tcpClientFactory,
        Func<NetworkStream, byte[], CancellationToken, Task> sendBytesAsync,
        int rawFrameCapacity = DefaultRawFrameCapacity,
        int parsedFrameCapacity = DefaultParsedFrameCapacity)
    {
        this.stopTimeout = stopTimeout;
        this.expectedBoardId = expectedBoardId;
        this.rawFrameCapacity = Math.Max(0, rawFrameCapacity);
        this.parsedFrameCapacity = Math.Max(0, parsedFrameCapacity);
        this.tcpClientFactory = tcpClientFactory;
        this.sendBytesAsync = sendBytesAsync;
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

            logger.Debug("Connecting LIVE v3 client to {Host} {Port}", host, port);

            intentionalDisconnect = false;
            capabilities = null;
            serverHello = null;
            protocolReader.Reset();
            lock (dropCountersGate)
            {
                dropCounters = LiveDaqClientDropCounters.Empty;
                lastPublishedDropTotal = 0;
            }

            receiveLoopCts?.Dispose();
            var nextTcpClient = tcpClientFactory();
            try
            {
                await nextTcpClient.ConnectAsync(host, port, cancellationToken);
                var nextStream = nextTcpClient.GetStream();
                await sendBytesAsync(nextStream, LiveV3ProtocolReader.CreateHandshake(), cancellationToken);
                var helloBytes = new byte[LiveV3ProtocolConstants.ServerHelloSize];
                if (!await TryReadExactAsync(nextStream, helloBytes, cancellationToken))
                {
                    throw new IOException("LIVE v3 server closed before sending hello.");
                }

                var hello = LiveV3ProtocolReader.ParseServerHello(helloBytes);
                ValidateExpectedBoardId(hello);

                tcpClient = nextTcpClient;
                stream = nextStream;
                serverHello = hello;
            }
            catch
            {
                nextTcpClient.Dispose();
                tcpClient = null;
                stream = null;
                throw;
            }

            receiveLoopCts = new CancellationTokenSource();
            rawFramesInFlight = 0;
            parsedFramesInFlight = 0;
            rawFrames = Channel.CreateBounded<RawFrameEnvelope>(new BoundedChannelOptions(Math.Max(1, rawFrameCapacity))
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
            parsedFrames = Channel.CreateBounded<LiveProtocolFrame>(new BoundedChannelOptions(Math.Max(1, parsedFrameCapacity))
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
            parseLoopTask = Task.Factory
                .StartNew(
                    ParseLoopAsync,
                    receiveLoopCts.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default)
                .Unwrap();
            publishLoopTask = Task.Factory
                .StartNew(
                    () => PublishLoopAsync(receiveLoopCts.Token),
                    receiveLoopCts.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default)
                .Unwrap();
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    internal async Task<LiveV3DeviceState> RequestDeviceStateAsync(CancellationToken cancellationToken = default)
    {
        Task<LiveV3DeviceState> task;
        TaskCompletionSource<LiveV3DeviceState>? createdRequest = null;
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (!IsConnected || stream is null)
            {
                throw new InvalidOperationException("Live client is not connected.");
            }

            if (pendingDeviceState is not null)
            {
                throw new InvalidOperationException("A LIVE v3 device state request is already in progress.");
            }

            createdRequest = new TaskCompletionSource<LiveV3DeviceState>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingDeviceState = createdRequest;
            await sendBytesAsync(
                stream,
                LiveV3ProtocolReader.CreateDeviceStateRequestFrame(GetNextSequence()),
                cancellationToken);
            task = pendingDeviceState.Task;
        }
        catch
        {
            if (createdRequest is not null && pendingDeviceState == createdRequest)
            {
                pendingDeviceState = null;
            }

            throw;
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
                if (pendingDeviceState?.Task == task)
                {
                    pendingDeviceState = null;
                }
            }
            finally
            {
                lifecycleGate.Release();
            }

            throw;
        }
    }

    public async Task<LivePreviewStartResult> StartPreviewAsync(
        LiveStartRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await EnsureCapabilitiesLoadedAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new LivePreviewStartResult.Failed(ex.Message);
        }

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
            startResultAwaitingHeaderSessionId = null;
            var frame = LiveV3ProtocolReader.CreateStartRequestFrame(
                GetNextSequence(),
                CreateStartRequest(request));
            await sendBytesAsync(stream, frame, cancellationToken);
            task = tcs.Task;
        }
        catch (Exception ex)
        {
            pendingStartResult = null;
            startResultAwaitingHeaderSessionId = null;
            logger.Warning(ex, "Failed to send LIVE v3 START_REQ");
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
                    startResultAwaitingHeaderSessionId = null;
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
        Task<LiveStopResult>? stopTask = null;
        Task<LiveSessionResult>? sessionResultTask = null;
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsConnected || stream is null || activeSessionId is not { } sessionId)
            {
                return;
            }

            var stopTcs = new TaskCompletionSource<LiveStopResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sessionResultTcs = new TaskCompletionSource<LiveSessionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingStopResult = stopTcs;
            pendingSessionResult = sessionResultTcs;

            var frame = LiveV3ProtocolReader.CreateStopRequestFrame(sessionId, GetNextSequence());
            await sendBytesAsync(stream, frame, cancellationToken);
            stopTask = stopTcs.Task;
            sessionResultTask = sessionResultTcs.Task;
        }
        finally
        {
            lifecycleGate.Release();
        }

        if (stopTask is null || sessionResultTask is null)
        {
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(stopTimeout);
        try
        {
            _ = await stopTask.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            await ClearPendingStopResultAsync(stopTask);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
        }

        try
        {
            _ = await sessionResultTask.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            await ClearPendingSessionResultAsync(sessionResultTask);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        Task? receiveLoopToAwait = null;
        Task? parseLoopToAwait = null;
        Task? publishLoopToAwait = null;
        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (tcpClient is null && stream is null && receiveLoopTask is null && parseLoopTask is null && publishLoopTask is null)
            {
                return;
            }

            intentionalDisconnect = true;
            if (stream is not null && activeSessionId is { } sessionId)
            {
                try
                {
                    var frame = LiveV3ProtocolReader.CreateStopRequestFrame(sessionId, GetNextSequence());
                    await sendBytesAsync(stream, frame, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Failed to send LIVE v3 STOP_REQ during disconnect");
                }
            }

            receiveLoopCts?.Cancel();
            rawFrames?.Writer.TryComplete();
            parsedFrames?.Writer.TryComplete();
            stream?.Close();
            tcpClient?.Close();
            stream = null;
            tcpClient = null;
            activeSessionId = null;
            receiveLoopToAwait = receiveLoopTask;
            parseLoopToAwait = parseLoopTask;
            publishLoopToAwait = publishLoopTask;
            receiveLoopTask = null;
            parseLoopTask = null;
            publishLoopTask = null;
            rawFrames = null;
            parsedFrames = null;
            CompletePendingForDisconnect("Live preview disconnected before startup completed.");
        }
        finally
        {
            lifecycleGate.Release();
        }

        await AwaitLoopDuringDisconnectAsync(receiveLoopToAwait, "receive");
        await AwaitLoopDuringDisconnectAsync(parseLoopToAwait, "parse");
        await AwaitLoopDuringDisconnectAsync(publishLoopToAwait, "publish");

        EmitEvent(new LiveDaqClientEvent.Disconnected(null));
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
        lock (eventsGate)
        {
            events.OnCompleted();
        }

        receiveLoopCts?.Dispose();
        tcpClient?.Dispose();
        lifecycleGate.Dispose();
    }

    private async Task<LiveV3Capabilities> EnsureCapabilitiesLoadedAsync(CancellationToken cancellationToken)
    {
        Task<LiveV3Capabilities> task;
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (capabilities is { } loadedCapabilities)
            {
                return loadedCapabilities;
            }

            if (!IsConnected || stream is null)
            {
                throw new InvalidOperationException("Live client is not connected.");
            }

            if (pendingCapabilities is null)
            {
                pendingCapabilities = new TaskCompletionSource<LiveV3Capabilities>(TaskCreationOptions.RunContinuationsAsynchronously);
                var frame = LiveV3ProtocolReader.CreateCapabilitiesRequestFrame(GetNextSequence());
                await sendBytesAsync(stream, frame, cancellationToken);
            }

            task = pendingCapabilities.Task;
        }
        catch
        {
            pendingCapabilities = null;
            throw;
        }
        finally
        {
            lifecycleGate.Release();
        }

        try
        {
            var loadedCapabilities = await task.WaitAsync(cancellationToken);
            if (loadedCapabilities.MaxFramePayloadBytes != LiveV3ProtocolConstants.MaxPayloadLength)
            {
                throw new InvalidOperationException("LIVE v3 max frame payload is unsupported.");
            }

            if (serverHello is { } hello &&
                loadedCapabilities.MaxFramePayloadBytes != hello.MaxFramePayloadBytes)
            {
                throw new InvalidOperationException("LIVE v3 capabilities do not match the server hello.");
            }

            return loadedCapabilities;
        }
        catch (OperationCanceledException)
        {
            await lifecycleGate.WaitAsync(CancellationToken.None);
            try
            {
                if (pendingCapabilities?.Task == task)
                {
                    pendingCapabilities = null;
                }
            }
            finally
            {
                lifecycleGate.Release();
            }

            throw;
        }
    }

    private async Task ClearPendingStopResultAsync(Task<LiveStopResult> waitTask)
    {
        await lifecycleGate.WaitAsync(CancellationToken.None);
        try
        {
            if (pendingStopResult?.Task == waitTask)
            {
                pendingStopResult = null;
            }
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    private async Task ClearPendingSessionResultAsync(Task<LiveSessionResult> waitTask)
    {
        await lifecycleGate.WaitAsync(CancellationToken.None);
        try
        {
            if (pendingSessionResult?.Task == waitTask)
            {
                pendingSessionResult = null;
            }
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        ChannelWriter<RawFrameEnvelope>? writer = null;
        try
        {
            var headerBytes = new byte[LiveV3ProtocolConstants.FrameHeaderSize];
            var skipBuffer = new byte[LiveV3ProtocolConstants.MaxPayloadLength];
            writer = rawFrames?.Writer;
            while (!cancellationToken.IsCancellationRequested)
            {
                var currentStream = stream;
                if (currentStream is null)
                {
                    return;
                }

                if (!await TryReadExactAsync(currentStream, headerBytes, cancellationToken))
                {
                    return;
                }

                var header = LiveV3ProtocolReader.ParseHeader(headerBytes, allowUnknownFrameType: true);
                if (!IsKnownFrameType(header.FrameType))
                {
                    await SkipPayloadAsync(currentStream, header.PayloadLength, skipBuffer, cancellationToken);
                    continue;
                }

                if (IsQueuedDataFrameType(header.FrameType))
                {
                    if (writer is null || !TryReserveRawFrame())
                    {
                        await SkipPayloadAsync(currentStream, header.PayloadLength, skipBuffer, cancellationToken);
                        NoteDropCounters(LiveDaqClientDropCounters.Empty with { RawTelemetryFramesSkipped = 1 });
                        continue;
                    }

                    var frameBytes = new byte[header.TotalFrameLength];
                    Buffer.BlockCopy(headerBytes, 0, frameBytes, 0, headerBytes.Length);
                    if (!await TryReadExactAsync(
                        currentStream,
                        frameBytes.AsMemory(LiveV3ProtocolConstants.FrameHeaderSize),
                        cancellationToken))
                    {
                        ReleaseRawFrame();
                        return;
                    }

                    if (!writer.TryWrite(new RawFrameEnvelope(header, frameBytes)))
                    {
                        ReleaseRawFrame();
                        NoteDropCounters(LiveDaqClientDropCounters.Empty with { RawTelemetryFramesSkipped = 1 });
                    }

                    continue;
                }

                var controlFrameBytes = new byte[header.TotalFrameLength];
                Buffer.BlockCopy(headerBytes, 0, controlFrameBytes, 0, headerBytes.Length);
                if (!await TryReadExactAsync(
                    currentStream,
                    controlFrameBytes.AsMemory(LiveV3ProtocolConstants.FrameHeaderSize),
                    cancellationToken))
                {
                    return;
                }

                var frame = LiveV3ProtocolReader.ParseFrame(controlFrameBytes, protocolReader.Context);
                await HandleFrameAsync(frame);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            writer ??= rawFrames?.Writer;
            writer?.TryComplete(ex);
            return;
        }
        finally
        {
            writer ??= rawFrames?.Writer;
            writer?.TryComplete();
        }
    }

    private async Task ParseLoopAsync()
    {
        var readerChannel = rawFrames?.Reader;
        var writer = parsedFrames?.Writer;
        if (readerChannel is null)
        {
            return;
        }

        try
        {
            await foreach (var rawFrame in readerChannel.ReadAllAsync())
            {
                ReleaseRawFrame();
                var frame = LiveV3ProtocolReader.ParseFrame(rawFrame.FrameBytes, protocolReader.Context);
                if (writer is null || !TryReserveParsedFrame())
                {
                    NoteDropCounters(LiveDaqClientDropCounters.Empty with { ParsedTelemetryFramesDropped = 1 });
                    continue;
                }

                if (!writer.TryWrite(frame))
                {
                    ReleaseParsedFrame();
                    NoteDropCounters(LiveDaqClientDropCounters.Empty with { ParsedTelemetryFramesDropped = 1 });
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            writer ??= parsedFrames?.Writer;
            writer?.TryComplete(ex);
            return;
        }
        finally
        {
            writer ??= parsedFrames?.Writer;
            writer?.TryComplete();
        }
    }

    private async Task PublishLoopAsync(CancellationToken cancellationToken)
    {
        var readerChannel = parsedFrames?.Reader;
        if (readerChannel is null)
        {
            return;
        }

        try
        {
            await foreach (var frame in readerChannel.ReadAllAsync())
            {
                ReleaseParsedFrame();
                await HandleFrameAsync(frame);
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                await HandleDisconnectAsync(null);
            }
        }
        catch (OperationCanceledException)
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
            if (isDisposed || intentionalDisconnect)
            {
                return;
            }

            switch (frame)
            {
                case LiveV3CapabilitiesFrame capabilitiesFrame:
                    capabilities = capabilitiesFrame.Payload;
                    pendingCapabilities?.TrySetResult(capabilitiesFrame.Payload);
                    pendingCapabilities = null;
                    break;

                case LiveV3DeviceStateFrame deviceStateFrame:
                    pendingDeviceState?.TrySetResult(deviceStateFrame.Payload);
                    pendingDeviceState = null;
                    break;

                case LiveV3StartResultFrame startResultFrame:
                    HandleStartResultFrame(startResultFrame);
                    break;

                case LiveSessionHeaderFrame sessionHeaderFrame:
                    activeSessionId = checked((byte)sessionHeaderFrame.Payload.SessionId);
                    if (pendingStartResult is not null &&
                        startResultAwaitingHeaderSessionId == activeSessionId)
                    {
                        pendingStartResult.TrySetResult(new LivePreviewStartResult.Started(sessionHeaderFrame.Payload));
                        pendingStartResult = null;
                        startResultAwaitingHeaderSessionId = null;
                    }
                    break;

                case LiveStopResultFrame stopResultFrame:
                    pendingStopResult?.TrySetResult(stopResultFrame.Payload);
                    pendingStopResult = null;
                    break;

                case LiveSessionResultFrame sessionResultFrame:
                    if (pendingStartResult is not null)
                    {
                        pendingStartResult.TrySetResult(new LivePreviewStartResult.Failed(
                            LiveV3ProtocolHelpers.CreateTerminalSessionMessage(
                                sessionResultFrame.Payload.FinalStatus.SessionResultReason)));
                        pendingStartResult = null;
                        startResultAwaitingHeaderSessionId = null;
                    }

                    pendingSessionResult?.TrySetResult(sessionResultFrame.Payload);
                    pendingSessionResult = null;
                    activeSessionId = null;
                    break;

                case LiveV3ErrorFrame errorFrame:
                    HandleErrorFrame(errorFrame);
                    break;
            }
        }
        finally
        {
            lifecycleGate.Release();
        }

        EmitEvent(new LiveDaqClientEvent.FrameReceived(frame));
    }

    private void HandleStartResultFrame(LiveV3StartResultFrame frame)
    {
        if (frame.Payload.IsPending)
        {
            activeSessionId = frame.Payload.SessionId;
            startResultAwaitingHeaderSessionId = frame.Payload.SessionId;
            return;
        }

        if (frame.Payload.IsDenied)
        {
            var firstReason = frame.Payload.AdmissionReasons.Count == 0
                ? (LiveStartAdmissionReason?)null
                : frame.Payload.AdmissionReasons[0];
            pendingStartResult?.TrySetResult(
                new LivePreviewStartResult.Rejected(
                    LiveV3ProtocolHelpers.MapAdmissionReasonToStartErrorCode(firstReason),
                    LiveV3ProtocolHelpers.CreateAdmissionMessage(firstReason))
                {
                    AdmissionReasons = frame.Payload.AdmissionReasons,
                });
            pendingStartResult = null;
            startResultAwaitingHeaderSessionId = null;
        }
    }

    private void HandleErrorFrame(LiveV3ErrorFrame frame)
    {
        var message = LiveV3ProtocolHelpers.CreateErrorMessage(frame.Payload.Code);
        pendingCapabilities?.TrySetException(new IOException(message));
        pendingCapabilities = null;
        pendingDeviceState?.TrySetException(new IOException(message));
        pendingDeviceState = null;

        if (pendingStartResult is not null)
        {
            pendingStartResult.TrySetResult(new LivePreviewStartResult.Failed(message));
            pendingStartResult = null;
            startResultAwaitingHeaderSessionId = null;
            return;
        }

        _ = Task.Run(() => HandleDisconnectAsync(message));
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
            rawFrames?.Writer.TryComplete();
            parsedFrames?.Writer.TryComplete();
            receiveLoopCts?.Dispose();
            receiveLoopCts = null;
            receiveLoopTask = null;
            parseLoopTask = null;
            publishLoopTask = null;
            rawFrames = null;
            parsedFrames = null;
            CompletePendingForDisconnect(errorMessage ?? "Live preview disconnected before startup completed.");
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
            EmitEvent(new LiveDaqClientEvent.Disconnected(null));
            return;
        }

        if (exception is not null)
        {
            logger.Error(exception, "LIVE v3 client disconnected unexpectedly: {ErrorMessage}", errorMessage);
        }
        else
        {
            logger.Error("LIVE v3 client disconnected unexpectedly: {ErrorMessage}", errorMessage);
        }

        EmitEvent(new LiveDaqClientEvent.Faulted(errorMessage));
        EmitEvent(new LiveDaqClientEvent.Disconnected(errorMessage));
    }

    private void CompletePendingForDisconnect(string startupMessage)
    {
        pendingCapabilities?.TrySetException(new IOException(startupMessage));
        pendingCapabilities = null;
        pendingDeviceState?.TrySetException(new IOException("Disconnected before DEVICE_STATE_RESP was received."));
        pendingDeviceState = null;
        pendingStartResult?.TrySetResult(new LivePreviewStartResult.Failed(startupMessage));
        pendingStartResult = null;
        pendingStopResult?.TrySetException(new IOException("Disconnected before STOP_RESULT was received."));
        pendingStopResult = null;
        pendingSessionResult?.TrySetException(new IOException("Disconnected before SESSION_RESULT was received."));
        pendingSessionResult = null;
        startResultAwaitingHeaderSessionId = null;
    }

    private bool TryReserveRawFrame()
    {
        if (rawFrameCapacity == 0)
        {
            return false;
        }

        if (Interlocked.Increment(ref rawFramesInFlight) <= rawFrameCapacity)
        {
            return true;
        }

        ReleaseRawFrame();
        return false;
    }

    private void ReleaseRawFrame()
    {
        Interlocked.Decrement(ref rawFramesInFlight);
    }

    private bool TryReserveParsedFrame()
    {
        if (parsedFrameCapacity == 0)
        {
            return false;
        }

        if (Interlocked.Increment(ref parsedFramesInFlight) <= parsedFrameCapacity)
        {
            return true;
        }

        ReleaseParsedFrame();
        return false;
    }

    private void ReleaseParsedFrame()
    {
        Interlocked.Decrement(ref parsedFramesInFlight);
    }

    private void NoteDropCounters(LiveDaqClientDropCounters delta)
    {
        LiveDaqClientDropCounters nextCounters;
        ulong totalDrops;
        var shouldPublish = false;
        lock (dropCountersGate)
        {
            dropCounters = dropCounters.Add(delta);
            nextCounters = dropCounters;
            totalDrops = nextCounters.RawTelemetryFramesSkipped + nextCounters.ParsedTelemetryFramesDropped;
            if (totalDrops == 1 || totalDrops - lastPublishedDropTotal >= DropCounterPublishStride)
            {
                lastPublishedDropTotal = totalDrops;
                shouldPublish = true;
            }
        }

        if (shouldPublish)
        {
            EmitEvent(new LiveDaqClientEvent.DropCountersChanged(nextCounters));
        }
    }

    private void EmitEvent(LiveDaqClientEvent clientEvent)
    {
        lock (eventsGate)
        {
            events.OnNext(clientEvent);
        }
    }

    private uint GetNextSequence() => unchecked(++nextSequence);

    private static LiveV3StartRequest CreateStartRequest(LiveStartRequest request)
    {
        var records = new List<LiveV3StreamRequestRecord>(3);
        var travelRequested = request.TravelRateMhz > 0 && (request.RequestedSensorMask & LiveSensorInstanceMask.Travel) != LiveSensorInstanceMask.None;
        var imuRequested = request.ImuRateMhz > 0 && (request.RequestedSensorMask & LiveSensorInstanceMask.Imu) != LiveSensorInstanceMask.None;
        var gpsRequested = request.GpsRateMhz > 0 && (request.RequestedSensorMask & LiveSensorInstanceMask.Gps) != LiveSensorInstanceMask.None;

        if (travelRequested)
        {
            records.Add(new LiveV3StreamRequestRecord(
                SstV5ProtocolConstants.StreamTravel,
                LiveV3ProtocolConstants.StreamRequestFlagRateOverride | LiveV3ProtocolConstants.StreamRequestFlagBatchDurationOverride,
                LiveSensorInstanceMask.Travel,
                ExtensionMask: 0,
                request.TravelRateMhz,
                BatchDurationMs: 50));
        }

        if (imuRequested)
        {
            records.Add(new LiveV3StreamRequestRecord(
                SstV5ProtocolConstants.StreamImu,
                LiveV3ProtocolConstants.StreamRequestFlagRateOverride | LiveV3ProtocolConstants.StreamRequestFlagBatchDurationOverride,
                LiveSensorInstanceMask.Imu,
                ExtensionMask: 0,
                request.ImuRateMhz,
                BatchDurationMs: 50));
        }

        if (gpsRequested)
        {
            records.Add(new LiveV3StreamRequestRecord(
                SstV5ProtocolConstants.StreamGps,
                LiveV3ProtocolConstants.StreamRequestFlagRateOverride,
                LiveSensorInstanceMask.Gps,
                ExtensionMask: SstV5ProtocolConstants.ExtensionGpsDiagPublicV1,
                request.GpsRateMhz,
                BatchDurationMs: 0));
        }

        return new LiveV3StartRequest
        {
            StreamRequests = records,
        };
    }

    private static bool IsQueuedDataFrameType(LiveV3FrameType frameType) => frameType switch
    {
        LiveV3FrameType.TravelData => true,
        LiveV3FrameType.ImuData => true,
        LiveV3FrameType.GpsData => true,
        LiveV3FrameType.BatteryData => true,
        LiveV3FrameType.MarkerData => true,
        _ => false,
    };

    private static bool IsKnownFrameType(LiveV3FrameType frameType) => frameType switch
    {
        LiveV3FrameType.CapabilitiesReq => true,
        LiveV3FrameType.CapabilitiesResp => true,
        LiveV3FrameType.StartReq => true,
        LiveV3FrameType.StartResult => true,
        LiveV3FrameType.StopReq => true,
        LiveV3FrameType.StopResult => true,
        LiveV3FrameType.SessionHeader => true,
        LiveV3FrameType.SessionResult => true,
        LiveV3FrameType.TravelData => true,
        LiveV3FrameType.ImuData => true,
        LiveV3FrameType.TemperatureData => true,
        LiveV3FrameType.GpsData => true,
        LiveV3FrameType.BatteryData => true,
        LiveV3FrameType.MarkerData => true,
        LiveV3FrameType.Status => true,
        LiveV3FrameType.Ping => true,
        LiveV3FrameType.Pong => true,
        LiveV3FrameType.Error => true,
        LiveV3FrameType.DeviceStateReq => true,
        LiveV3FrameType.DeviceStateResp => true,
        _ => false,
    };

    private void ValidateExpectedBoardId(LiveV3ServerHello hello)
    {
        if (string.IsNullOrWhiteSpace(expectedBoardId))
        {
            return;
        }

        var bid = hello.UniqueBoardId.ToString("x16", CultureInfo.InvariantCulture);
        var actualBoardId = UuidUtil.CreateDeviceUuid(bid).ToString();
        if (!string.Equals(actualBoardId, expectedBoardId, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("LIVE v3 server hello board ID does not match the discovered DAQ identity.");
        }
    }

    private static async Task<bool> TryReadExactAsync(
        NetworkStream currentStream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await currentStream.ReadAsync(buffer[totalRead..], cancellationToken);
            if (read == 0)
            {
                return false;
            }

            totalRead += read;
        }

        return true;
    }

    private static async Task SkipPayloadAsync(
        NetworkStream currentStream,
        uint payloadLength,
        byte[] skipBuffer,
        CancellationToken cancellationToken)
    {
        var remaining = payloadLength;
        while (remaining > 0)
        {
            var nextRead = (int)Math.Min((uint)skipBuffer.Length, remaining);
            if (!await TryReadExactAsync(currentStream, skipBuffer.AsMemory(0, nextRead), cancellationToken))
            {
                return;
            }

            remaining -= (uint)nextRead;
        }
    }

    private static async Task SendBytesAsync(NetworkStream stream, byte[] bytes, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task AwaitLoopDuringDisconnectAsync(Task? task, string loopName)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "LIVE v3 {LoopName} loop threw during disconnect", loopName);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
    }
}
