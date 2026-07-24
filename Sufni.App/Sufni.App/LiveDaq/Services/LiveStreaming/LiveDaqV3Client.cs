using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Serilog;
#if SUFNI_PROFILING_DIAGNOSTICS
using Sufni.App.LiveDaq.Services;
#endif
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

    private enum LifecyclePhase
    {
        Disconnected,
        AwaitingHello,
        Ready,
        StartPending,
        AwaitingSessionHeader,
        Active,
        Stopping,
    }

    private readonly record struct RawFrameEnvelope(
        LiveV3FrameHeader Header,
        byte[] FrameBytes,
        LiveV3SessionDecodeContext DecodeContext,
        bool IsTelemetry);

    private readonly record struct ParsedFrameEnvelope(
        LiveProtocolFrame Frame,
        bool IsTelemetry);

    private TcpClient? tcpClient;
    private NetworkStream? stream;
    private CancellationTokenSource? receiveLoopCts;
    private Channel<RawFrameEnvelope>? rawFrames;
    private Channel<ParsedFrameEnvelope>? parsedFrames;
    private Task? receiveLoopTask;
    private Task? parseLoopTask;
    private Task? publishLoopTask;
    private TaskCompletionSource<LiveV3Capabilities>? pendingCapabilities;
    private TaskCompletionSource<LiveV3DeviceState>? pendingDeviceState;
    private TaskCompletionSource<uint>? pendingPong;
    private TaskCompletionSource<LivePreviewStartResult>? pendingStartResult;
    private TaskCompletionSource<LiveStopResult>? pendingStopResult;
    private TaskCompletionSource<LiveSessionResult>? pendingSessionResult;
    private LiveV3Capabilities? capabilities;
    private LiveV3ServerHello? serverHello;
    private byte? activeSessionId;
    private byte? startResultAwaitingHeaderSessionId;
    private LiveStartRequest? pendingStartRequest;
    private LiveStreamMask pendingRequestedStreamMask;
    private uint? pendingPingNonce;
    private byte? pendingPingSessionId;
    private uint nextSequence;
    private int rawFramesInFlight;
    private int parsedFramesInFlight;
    private LiveDaqClientDropCounters dropCounters = LiveDaqClientDropCounters.Empty;
    private ulong lastPublishedDropTotal;
    private bool isDisposed;
    private bool intentionalDisconnect;
    private LifecyclePhase lifecyclePhase = LifecyclePhase.Disconnected;

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
            lifecyclePhase = LifecyclePhase.AwaitingHello;
            capabilities = null;
            serverHello = null;
            nextSequence = 0;
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
                nextTcpClient.NoDelay = true;
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
                lifecyclePhase = LifecyclePhase.Ready;
            }
            catch
            {
                nextTcpClient.Dispose();
                tcpClient = null;
                stream = null;
                lifecyclePhase = LifecyclePhase.Disconnected;
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
            parsedFrames = Channel.CreateBounded<ParsedFrameEnvelope>(new BoundedChannelOptions(Math.Max(1, parsedFrameCapacity))
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

            if (lifecyclePhase is not (LifecyclePhase.Ready or LifecyclePhase.Active))
            {
                throw new InvalidOperationException(
                    $"A LIVE v3 device state request is not valid while the client is {lifecyclePhase}.");
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
            // There is no request ID on the wire. Keep the operation pending until its response
            // arrives so a later request cannot consume the canceled caller's response.
            throw;
        }
    }

    internal async Task PingAsync(uint nonce, CancellationToken cancellationToken = default)
    {
        Task<uint> task;
        TaskCompletionSource<uint>? createdRequest = null;
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (!IsConnected || stream is null)
            {
                throw new InvalidOperationException("Live client is not connected.");
            }
            if (lifecyclePhase is LifecyclePhase.AwaitingHello or LifecyclePhase.Disconnected)
            {
                throw new InvalidOperationException(
                    $"A LIVE v3 ping is not valid while the client is {lifecyclePhase}.");
            }
            if (pendingPong is not null)
            {
                throw new InvalidOperationException("A LIVE v3 ping is already in progress.");
            }

            createdRequest = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingPong = createdRequest;
            pendingPingNonce = nonce;
            pendingPingSessionId = activeSessionId ?? 0;
            await sendBytesAsync(
                stream,
                LiveV3ProtocolReader.CreatePingFrame(pendingPingSessionId.Value, GetNextSequence(), nonce),
                cancellationToken);
            task = createdRequest.Task;
        }
        catch
        {
            if (createdRequest is not null && pendingPong == createdRequest)
            {
                pendingPong = null;
                pendingPingNonce = null;
                pendingPingSessionId = null;
            }
            throw;
        }
        finally
        {
            lifecycleGate.Release();
        }

        try
        {
            _ = await task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Preserve the outstanding nonce until PONG arrives; LIVE v3 has no request ID.
            throw;
        }
    }

    public async Task<LivePreviewStartResult> StartPreviewAsync(
        LiveStartRequest request,
        CancellationToken cancellationToken = default)
    {
        LiveV3Capabilities loadedCapabilities;
        try
        {
            loadedCapabilities = await EnsureCapabilitiesLoadedAsync(cancellationToken);
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

            if (lifecyclePhase != LifecyclePhase.Ready)
            {
                return new LivePreviewStartResult.Failed(
                    $"Live preview cannot start while the LIVE v3 client is {lifecyclePhase}.");
            }

            if (pendingStartResult is not null)
            {
                return new LivePreviewStartResult.Failed("A live preview request is already in progress.");
            }

            var tcs = new TaskCompletionSource<LivePreviewStartResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var protocolRequest = CreateStartRequest(request, loadedCapabilities);
            pendingStartResult = tcs;
            startResultAwaitingHeaderSessionId = null;
            pendingStartRequest = request;
            pendingRequestedStreamMask = CreateRequestedStreamMask(protocolRequest);
            lifecyclePhase = LifecyclePhase.StartPending;
            var frame = LiveV3ProtocolReader.CreateStartRequestFrame(
                GetNextSequence(),
                protocolRequest);
            await sendBytesAsync(stream, frame, cancellationToken);
            task = tcs.Task;
        }
        catch (Exception ex)
        {
            pendingStartResult = null;
            startResultAwaitingHeaderSessionId = null;
            pendingStartRequest = null;
            pendingRequestedStreamMask = LiveStreamMask.None;
            lifecyclePhase = LifecyclePhase.Ready;
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
            // The start remains in flight on the connection. Its eventual result must still
            // drive the protocol state before another start can be sent.
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
            if (!IsConnected ||
                stream is null ||
                activeSessionId is not { } sessionId ||
                lifecyclePhase != LifecyclePhase.Active)
            {
                return;
            }

            if (pendingStopResult is not null || pendingSessionResult is not null)
            {
                return;
            }

            var stopTcs = new TaskCompletionSource<LiveStopResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sessionResultTcs = new TaskCompletionSource<LiveSessionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingStopResult = stopTcs;
            pendingSessionResult = sessionResultTcs;
            lifecyclePhase = LifecyclePhase.Stopping;

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
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            await HandleDisconnectAsync("Timed out waiting for LIVE v3 STOP_RESULT.");
            return;
        }

        try
        {
            _ = await sessionResultTask.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }


            await HandleDisconnectAsync("Timed out waiting for LIVE v3 SESSION_RESULT after STOP_RESULT.");
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
            lifecyclePhase = LifecyclePhase.Disconnected;
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

            if (lifecyclePhase != LifecyclePhase.Ready)
            {
                throw new InvalidOperationException(
                    $"LIVE v3 capabilities cannot be requested while the client is {lifecyclePhase}.");
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
            // Keep the single request pending so a canceled waiter cannot cause a duplicate
            // CAPABILITIES_REQ on this connection.
            throw;
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

                    if (!writer.TryWrite(new RawFrameEnvelope(
                            header,
                            frameBytes,
                            protocolReader.Context,
                            IsTelemetry: true)))
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

                if (header.FrameType == LiveV3FrameType.SessionResult)
                {
                    if (writer is null)
                    {
                        throw new IOException("LIVE v3 receive pipeline is unavailable.");
                    }
                    await writer.WriteAsync(
                        new RawFrameEnvelope(
                            header,
                            controlFrameBytes,
                            protocolReader.Context,
                            IsTelemetry: false),
                        cancellationToken);
                    continue;
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
                if (rawFrame.IsTelemetry)
                {
                    ReleaseRawFrame();
                }
                var frame = LiveV3ProtocolReader.ParseFrame(rawFrame.FrameBytes, rawFrame.DecodeContext);
                if (!rawFrame.IsTelemetry)
                {
                    if (writer is null)
                    {
                        throw new IOException("LIVE v3 publish pipeline is unavailable.");
                    }
                    await writer.WriteAsync(
                        new ParsedFrameEnvelope(frame, IsTelemetry: false));
                    continue;
                }
                if (writer is null || !TryReserveParsedFrame())
                {
                    NoteDropCounters(LiveDaqClientDropCounters.Empty with { ParsedTelemetryFramesDropped = 1 });
                    continue;
                }

                if (!writer.TryWrite(new ParsedFrameEnvelope(frame, IsTelemetry: true)))
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
            await foreach (var envelope in readerChannel.ReadAllAsync())
            {
                if (envelope.IsTelemetry)
                {
                    ReleaseParsedFrame();
                }
                await HandleFrameAsync(envelope.Frame);
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
                    RequireLifecyclePhase(
                        LiveV3FrameType.CapabilitiesResp,
                        LifecyclePhase.Ready);
                    if (pendingCapabilities is null)
                    {
                        throw new FormatException("LIVE v3 CAPABILITIES_RESP arrived without an outstanding request.");
                    }
                    capabilities = capabilitiesFrame.Payload;
                    pendingCapabilities?.TrySetResult(capabilitiesFrame.Payload);
                    pendingCapabilities = null;
                    break;

                case LiveV3DeviceStateFrame deviceStateFrame:
                    RequireLifecyclePhase(
                        LiveV3FrameType.DeviceStateResp,
                        LifecyclePhase.Ready,
                        LifecyclePhase.Active);
                    if (pendingDeviceState is null)
                    {
                        throw new FormatException("LIVE v3 DEVICE_STATE_RESP arrived without an outstanding request.");
                    }
                    pendingDeviceState?.TrySetResult(deviceStateFrame.Payload);
                    pendingDeviceState = null;
                    break;

                case LiveV3StartResultFrame startResultFrame:
                    RequireLifecyclePhase(LiveV3FrameType.StartResult, LifecyclePhase.StartPending);
                    if (pendingStartResult is null)
                    {
                        throw new FormatException("LIVE v3 START_RESULT arrived without an outstanding start.");
                    }
                    HandleStartResultFrame(startResultFrame);
                    break;

                case LiveSessionHeaderFrame sessionHeaderFrame:
                    RequireLifecyclePhase(
                        LiveV3FrameType.SessionHeader,
                        LifecyclePhase.AwaitingSessionHeader);
                    var requestedSensorMask = pendingStartRequest?.RequestedSensorMask ??
                                              LiveSensorInstanceMask.None;
                    var requestedStreamMask = pendingRequestedStreamMask;
                    ValidateSessionHeaderAgainstPendingStart(
                        sessionHeaderFrame.Payload,
                        requestedStreamMask);
                    var combinedHeader = sessionHeaderFrame.Payload with
                    {
                        RequestedSensorMask = requestedSensorMask,
                        RequestedStreamMask = requestedStreamMask,
                    };
                    sessionHeaderFrame = sessionHeaderFrame with { Payload = combinedHeader };
                    frame = sessionHeaderFrame;
                    activeSessionId = checked((byte)combinedHeader.SessionId);
                    if (pendingStartResult is null ||
                        startResultAwaitingHeaderSessionId != activeSessionId)
                    {
                        throw new FormatException("LIVE v3 SESSION_HEADER does not match the pending start.");
                    }
                    lifecyclePhase = LifecyclePhase.Active;
                    pendingStartResult.TrySetResult(new LivePreviewStartResult.Started(combinedHeader));
                    pendingStartResult = null;
                    startResultAwaitingHeaderSessionId = null;
                    pendingStartRequest = null;
                    pendingRequestedStreamMask = LiveStreamMask.None;
                    break;

                case LiveStopResultFrame stopResultFrame:
                    RequireLifecyclePhase(LiveV3FrameType.StopResult, LifecyclePhase.Stopping);
                    if (pendingStopResult is null)
                    {
                        throw new FormatException("LIVE v3 STOP_RESULT arrived without an outstanding stop.");
                    }
                    pendingStopResult?.TrySetResult(stopResultFrame.Payload);
                    pendingStopResult = null;
                    break;

                case LiveSessionResultFrame sessionResultFrame:
                    RequireLifecyclePhase(
                        LiveV3FrameType.SessionResult,
                        LifecyclePhase.AwaitingSessionHeader,
                        LifecyclePhase.Active,
                        LifecyclePhase.Stopping);
                    if (lifecyclePhase == LifecyclePhase.Stopping && pendingStopResult is not null)
                    {
                        throw new FormatException("LIVE v3 SESSION_RESULT arrived before STOP_RESULT.");
                    }
                    if (lifecyclePhase == LifecyclePhase.AwaitingSessionHeader)
                    {
                        if (pendingStartResult is null)
                        {
                            throw new FormatException("LIVE v3 pre-header SESSION_RESULT has no pending start.");
                        }
                        pendingStartResult.TrySetResult(new LivePreviewStartResult.Failed(
                            LiveV3ProtocolHelpers.CreateTerminalSessionMessage(
                                sessionResultFrame.Payload.FinalStatus.SessionResultReason)));
                        pendingStartResult = null;
                        startResultAwaitingHeaderSessionId = null;
                        pendingStartRequest = null;
                        pendingRequestedStreamMask = LiveStreamMask.None;
                    }

                    pendingSessionResult?.TrySetResult(sessionResultFrame.Payload);
                    pendingSessionResult = null;
                    activeSessionId = null;
                    lifecyclePhase = LifecyclePhase.Ready;
                    protocolReader.ResetSessionContext();
                    break;

                case LivePongFrame pongFrame:
                    if (pendingPong is null)
                    {
                        throw new FormatException("LIVE v3 PONG arrived without an outstanding PING.");
                    }
                    if (pongFrame.SessionId != pendingPingSessionId)
                    {
                        throw new FormatException("LIVE v3 PONG session ID did not match the outstanding PING.");
                    }
                    if (pongFrame.Nonce == pendingPingNonce)
                    {
                        pendingPong.TrySetResult(pongFrame.Nonce!.Value);
                    }
                    else
                    {
                        pendingPong.TrySetException(
                            new IOException("LIVE v3 PONG nonce did not match the outstanding PING."));
                    }
                    pendingPong = null;
                    pendingPingNonce = null;
                    pendingPingSessionId = null;
                    break;

                case LiveV3ErrorFrame errorFrame:
                    HandleErrorFrame(errorFrame);
                    break;

                case LiveV3CapabilitiesRequestFrame or
                     LiveV3StartRequestFrame or
                     LiveStopRequestFrame or
                     LivePingFrame or
                     LiveV3DeviceStateRequestFrame:
                    throw new FormatException($"LIVE v3 server sent invalid client-direction frame {frame.GetType().Name}.");
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
            lifecyclePhase = LifecyclePhase.AwaitingSessionHeader;
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
            pendingStartRequest = null;
            pendingRequestedStreamMask = LiveStreamMask.None;
            activeSessionId = null;
            lifecyclePhase = LifecyclePhase.Ready;
            protocolReader.ResetSessionContext();
        }
    }

    private void ValidateSessionHeaderAgainstPendingStart(
        LiveSessionHeader header,
        LiveStreamMask requestedStreamMask)
    {
        if (capabilities is { } loadedCapabilities &&
            header.BoardId != loadedCapabilities.BoardId)
        {
            throw new FormatException("LIVE v3 SESSION_HEADER board ID does not match capabilities.");
        }
        if (requestedStreamMask == LiveStreamMask.None ||
            (header.AcceptedStreamMask & ~requestedStreamMask) != 0)
        {
            throw new FormatException("LIVE v3 SESSION_HEADER accepted streams are not a subset of the request.");
        }
        if (header.AdmissionOmissions.Any(
                omission => (omission.Stream & requestedStreamMask) == 0))
        {
            throw new FormatException("LIVE v3 SESSION_HEADER contains an omission for an unrequested stream.");
        }
    }

    private void HandleErrorFrame(LiveV3ErrorFrame frame)
    {
        var message = LiveV3ProtocolHelpers.CreateErrorMessage(frame.Payload);
        pendingCapabilities?.TrySetException(new IOException(message));
        pendingCapabilities = null;
        pendingDeviceState?.TrySetException(new IOException(message));
        pendingDeviceState = null;
        pendingPong?.TrySetException(new IOException(message));
        pendingPong = null;
        pendingPingNonce = null;
        pendingPingSessionId = null;

        if (pendingStartResult is not null)
        {
            pendingStartResult.TrySetResult(new LivePreviewStartResult.Failed(message));
            pendingStartResult = null;
            startResultAwaitingHeaderSessionId = null;
            pendingStartRequest = null;
            pendingRequestedStreamMask = LiveStreamMask.None;
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
            lifecyclePhase = LifecyclePhase.Disconnected;
            protocolReader.Reset();
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
        pendingPong?.TrySetException(new IOException("Disconnected before PONG was received."));
        pendingPong = null;
        pendingPingNonce = null;
        pendingPingSessionId = null;
        pendingStartResult?.TrySetResult(new LivePreviewStartResult.Failed(startupMessage));
        pendingStartResult = null;
        pendingStopResult?.TrySetException(new IOException("Disconnected before STOP_RESULT was received."));
        pendingStopResult = null;
        pendingSessionResult?.TrySetException(new IOException("Disconnected before SESSION_RESULT was received."));
        pendingSessionResult = null;
        startResultAwaitingHeaderSessionId = null;
        pendingStartRequest = null;
        pendingRequestedStreamMask = LiveStreamMask.None;
    }

    private void RequireLifecyclePhase(
        LiveV3FrameType frameType,
        params LifecyclePhase[] allowedPhases)
    {
        if (!allowedPhases.Contains(lifecyclePhase))
        {
            throw new FormatException(
                $"LIVE v3 {frameType} is invalid while the client is {lifecyclePhase}.");
        }
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

    private uint GetNextSequence() => unchecked(nextSequence++);

    private LiveV3StartRequest CreateStartRequest(
        LiveStartRequest request,
        LiveV3Capabilities capabilities)
    {
#if SUFNI_PROFILING_DIAGNOSTICS
        if (expectedBoardId is not null && ProfilingLiveDaqReplay.Matches(expectedBoardId))
        {
            return ProfilingLiveDaqReplay.CreateStartRequest(capabilities);
        }
#endif

        var requestedStreams = request.RequestedStreamMask == LiveStreamMask.None
            ? CreateLegacyRequestedStreamMask(request)
            : request.RequestedStreamMask;
        const LiveStreamMask knownStreams =
            LiveStreamMask.Travel |
            LiveStreamMask.Imu |
            LiveStreamMask.Temperature |
            LiveStreamMask.Gps |
            LiveStreamMask.Battery |
            LiveStreamMask.Marker;
        if (requestedStreams == LiveStreamMask.None)
        {
            throw new ArgumentException("LIVE v3 start requires at least one explicit stream.", nameof(request));
        }
        if ((requestedStreams & ~knownStreams) != 0)
        {
            throw new ArgumentException("LIVE v3 start contains an unknown stream selection.", nameof(request));
        }
        if (request.NoGpsHeaderWait && (requestedStreams & LiveStreamMask.Gps) == 0)
        {
            throw new ArgumentException("NO_GPS_HEADER_WAIT requires a GPS stream.", nameof(request));
        }
        const LiveStreamMask telemetryStreams =
            LiveStreamMask.Travel |
            LiveStreamMask.Imu |
            LiveStreamMask.Temperature |
            LiveStreamMask.Gps;
        if ((requestedStreams & LiveStreamMask.Marker) != 0 &&
            (requestedStreams & telemetryStreams) == 0)
        {
            throw new ArgumentException(
                "LIVE v3 marker requires at least one telemetry stream.",
                nameof(request));
        }

        var records = new List<LiveV3StreamRequestRecord>(6);
        if ((requestedStreams & LiveStreamMask.Travel) != 0)
        {
            var capability = RequireCapability(capabilities, LiveStreamMask.Travel);
            records.Add(CreateRateRequest(
                SstV5ProtocolConstants.StreamTravel,
                ResolveRequestedSources(request.RequestedSensorMask, LiveSensorInstanceMask.Travel, capability),
                request.TravelRateMhz,
                request.TravelBatchDurationMs,
                capability));
        }
        if ((requestedStreams & LiveStreamMask.Imu) != 0)
        {
            var capability = RequireCapability(capabilities, LiveStreamMask.Imu);
            records.Add(CreateRateRequest(
                SstV5ProtocolConstants.StreamImu,
                ResolveRequestedSources(request.RequestedSensorMask, LiveSensorInstanceMask.Imu, capability),
                request.ImuRateMhz,
                request.ImuBatchDurationMs,
                capability));
        }
        if ((requestedStreams & LiveStreamMask.Temperature) != 0)
        {
            var capability = RequireCapability(capabilities, LiveStreamMask.Temperature);
            records.Add(CreateRateRequest(
                SstV5ProtocolConstants.StreamTemperature,
                ResolveRequestedSources(request.RequestedSensorMask, LiveSensorInstanceMask.Imu, capability),
                request.TemperatureRateMhz,
                batchDurationMs: null,
                capability));
        }
        if ((requestedStreams & LiveStreamMask.Gps) != 0)
        {
            var capability = RequireCapability(capabilities, LiveStreamMask.Gps);
            var extensionMask = request.RequestGpsDiagnostics
                ? capability.SupportedExtensionMask & SstV5ProtocolConstants.ExtensionGpsDiagPublicV1
                : 0u;
            records.Add(CreateRateRequest(
                SstV5ProtocolConstants.StreamGps,
                RequireFixedSource(capability, LiveSensorInstanceMask.Gps),
                request.GpsRateMhz,
                batchDurationMs: null,
                capability,
                extensionMask));
        }
        if ((requestedStreams & LiveStreamMask.Battery) != 0)
        {
            var capability = RequireCapability(capabilities, LiveStreamMask.Battery);
            records.Add(new LiveV3StreamRequestRecord(
                SstV5ProtocolConstants.StreamBattery,
                RecordFlags: 0,
                SourceMask: RequireFixedSource(capability, LiveSensorInstanceMask.Battery),
                ExtensionMask: 0,
                RateMhz: 0,
                BatchDurationMs: 0));
        }
        if ((requestedStreams & LiveStreamMask.Marker) != 0)
        {
            _ = RequireCapability(capabilities, LiveStreamMask.Marker);
            records.Add(new LiveV3StreamRequestRecord(
                SstV5ProtocolConstants.StreamMarker,
                RecordFlags: 0,
                SourceMask: LiveSensorInstanceMask.None,
                ExtensionMask: 0,
                RateMhz: 0,
                BatchDurationMs: 0));
        }

        return new LiveV3StartRequest
        {
            StartFlags = (ushort)((request.Priority ? LiveV3ProtocolConstants.StartFlagPriority : 0) |
                                  (request.NoGpsHeaderWait ? LiveV3ProtocolConstants.StartFlagNoGpsHeaderWait : 0)),
            StreamRequests = records,
        };
    }

    private static LiveStreamMask CreateRequestedStreamMask(LiveV3StartRequest request)
    {
        var streams = LiveStreamMask.None;
        foreach (var streamRequest in request.StreamRequests)
        {
            streams |= (LiveStreamMask)SstV5ProtocolConstants.StreamMaskForKind(
                streamRequest.StreamKind);
        }

        return streams;
    }

    private static LiveStreamMask CreateLegacyRequestedStreamMask(LiveStartRequest request)
    {
        var streams = LiveStreamMask.None;
        if (request.TravelRateMhz > 0 &&
            (request.RequestedSensorMask & LiveSensorInstanceMask.Travel) != 0)
        {
            streams |= LiveStreamMask.Travel;
        }
        if (request.ImuRateMhz > 0 &&
            (request.RequestedSensorMask & LiveSensorInstanceMask.Imu) != 0)
        {
            streams |= LiveStreamMask.Imu;
        }
        if (request.GpsRateMhz > 0 &&
            (request.RequestedSensorMask & LiveSensorInstanceMask.Gps) != 0)
        {
            streams |= LiveStreamMask.Gps;
        }
        return streams;
    }

    private static LiveV3StreamCapability RequireCapability(
        LiveV3Capabilities capabilities,
        LiveStreamMask stream)
    {
        var capability = capabilities.Streams.SingleOrDefault(candidate => candidate.Stream == stream);
        if (capability is null || (capabilities.SupportedStreamMask & stream) == 0)
        {
            throw new ArgumentException($"LIVE v3 stream {stream} is not supported by this DAQ.");
        }
        return capability;
    }

    private static LiveSensorInstanceMask ResolveRequestedSources(
        LiveSensorInstanceMask requestedSources,
        LiveSensorInstanceMask streamSources,
        LiveV3StreamCapability capability)
    {
        var selected = requestedSources & streamSources;
        if (selected == LiveSensorInstanceMask.None)
        {
            selected = capability.SupportedSourceMask;
        }
        else
        {
            selected &= capability.SupportedSourceMask;
        }
        if (selected == LiveSensorInstanceMask.None)
        {
            throw new ArgumentException($"LIVE v3 stream {capability.Stream} has no supported selected sources.");
        }
        return selected;
    }

    private static LiveSensorInstanceMask RequireFixedSource(
        LiveV3StreamCapability capability,
        LiveSensorInstanceMask source)
    {
        if ((capability.SupportedSourceMask & source) == 0)
        {
            throw new ArgumentException($"LIVE v3 stream {capability.Stream} source is not supported.");
        }
        return source;
    }

    private static LiveV3StreamRequestRecord CreateRateRequest(
        byte streamKind,
        LiveSensorInstanceMask sourceMask,
        uint rateMhz,
        uint? batchDurationMs,
        LiveV3StreamCapability capability,
        uint extensionMask = 0)
    {
        ushort recordFlags = 0;
        if (rateMhz > 0)
        {
            if (rateMhz < capability.MinRateMhz || rateMhz > capability.MaxRateMhz)
            {
                throw new ArgumentOutOfRangeException(nameof(rateMhz), $"LIVE v3 rate is outside the DAQ capability range for {capability.Stream}.");
            }
            recordFlags |= LiveV3ProtocolConstants.StreamRequestFlagRateOverride;
        }
        if (batchDurationMs is { } duration)
        {
            if (duration == 0 || duration > capability.MaxBatchDurationMs)
            {
                throw new ArgumentOutOfRangeException(nameof(batchDurationMs), $"LIVE v3 batch duration is outside the DAQ capability range for {capability.Stream}.");
            }
            recordFlags |= LiveV3ProtocolConstants.StreamRequestFlagBatchDurationOverride;
        }

        return new LiveV3StreamRequestRecord(
            streamKind,
            recordFlags,
            sourceMask,
            extensionMask,
            rateMhz,
            batchDurationMs ?? 0);
    }

    private static bool IsQueuedDataFrameType(LiveV3FrameType frameType) => frameType switch
    {
        LiveV3FrameType.TravelData => true,
        LiveV3FrameType.ImuData => true,
        LiveV3FrameType.TemperatureData => true,
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
