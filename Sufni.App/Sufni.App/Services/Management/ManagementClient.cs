using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Services.Management;

internal sealed class ManagementClient : IDisposable
{
    private static readonly long MinUnixTimeSeconds = DateTimeOffset.MinValue.ToUnixTimeSeconds();
    private static readonly long MaxUnixTimeSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();
    private const int SocketReceiveBufferSize = 128 * 1024;
    private const int ReadBufferSize = 64 * 1024;

    private readonly ManagementProtocolReader reader = new();
    private readonly TimeSpan connectTimeout;
    private readonly TimeSpan ioTimeout;
    private readonly TimeSpan commitTimeout;

    private TcpClient? tcpClient;
    private NetworkStream? stream;
    private uint nextRequestId;

    public ManagementClient(
        TimeSpan? connectTimeout = null,
        TimeSpan? ioTimeout = null,
        TimeSpan? commitTimeout = null)
    {
        this.connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(5);
        this.ioTimeout = ioTimeout ?? TimeSpan.FromSeconds(5);
        this.commitTimeout = commitTimeout ?? TimeSpan.FromSeconds(15);
    }

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        if (stream is not null)
        {
            throw new InvalidOperationException("Management client is already connected.");
        }

        tcpClient = new TcpClient { ReceiveBufferSize = SocketReceiveBufferSize };
        try
        {
            await ExecuteWithTimeoutAsync(
                async token => await tcpClient.ConnectAsync(host, port, token),
                connectTimeout,
                $"Timed out connecting to the DAQ management endpoint at {host}:{port}.",
                cancellationToken);
            stream = tcpClient.GetStream();
        }
        catch (SocketException ex)
        {
            tcpClient.Dispose();
            tcpClient = null;
            throw new DaqManagementException($"Failed to connect to the DAQ management endpoint at {host}:{port}.", ex);
        }
        catch
        {
            tcpClient.Dispose();
            tcpClient = null;
            throw;
        }
    }

    public async Task<DaqListDirectoryResult> ListDirectoryAsync(
        DaqDirectoryId directoryId,
        CancellationToken cancellationToken = default)
    {
        var requestId = GetNextRequestId();
        await SendFrameAsync(
            ManagementProtocolReader.CreateListDirectoryRequest(requestId, directoryId),
            ioTimeout,
            cancellationToken);

        var files = new List<DaqFileRecord>();
        while (true)
        {
            var response = await ReadFrameAsync(ioTimeout, cancellationToken);
            EnsureRequestId(response, requestId);

            switch (response)
            {
                case ManagementListDirectoryEntryFrame entry:
                    if (entry.DirectoryId != directoryId)
                    {
                        throw new DaqManagementException(
                            $"LIST_DIR response directory {entry.DirectoryId} did not match requested directory {directoryId}.");
                    }

                    files.Add(CreateFileRecord(directoryId, entry));
                    break;

                case ManagementListDirectoryDoneFrame done:
                    if (done.EntryCount != files.Count)
                    {
                        throw new DaqManagementException(
                            $"LIST_DIR completion entry count {done.EntryCount} did not match the {files.Count} entries received.");
                    }

                    return new DaqListDirectoryResult.Listed(CreateDirectoryRecord(directoryId, files));

                case ManagementErrorFrame error when files.Count == 0:
                    return new DaqListDirectoryResult.Error(
                        MapKnownErrorCode(error.ErrorCode),
                        CreateErrorMessage(error.ErrorCode));

                case ManagementErrorFrame error:
                    throw new DaqManagementException(
                        $"LIST_DIR returned an ERROR frame after {files.Count} entries were already received (error {error.ErrorCode}).");

                default:
                    throw UnexpectedFrame("LIST_DIR", response, "LIST_DIR_ENTRY, LIST_DIR_DONE, or ERROR");
            }
        }
    }

    public async Task<DaqGetFileResult> GetFileAsync(
        DaqFileClass fileClass,
        int recordId,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The GET_FILE destination stream must be writable.", nameof(destination));
        }

        if (fileClass == DaqFileClass.Config && recordId != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recordId), "CONFIG reads must use record id 0.");
        }

        var requestId = GetNextRequestId();
        await SendFrameAsync(
            ManagementProtocolReader.CreateGetFileRequest(requestId, fileClass, recordId),
            ioTimeout,
            cancellationToken);

        var fileStarted = false;
        string? fileName = null;
        ulong declaredSize = 0;
        ulong receivedSize = 0;
        uint maxChunkPayload = 0;
        while (true)
        {
            var response = await ReadFrameAsync(ioTimeout, cancellationToken);
            EnsureRequestId(response, requestId);

            switch (response)
            {
                case ManagementErrorFrame error when !fileStarted:
                    return new DaqGetFileResult.Error(
                        MapKnownErrorCode(error.ErrorCode),
                        CreateErrorMessage(error.ErrorCode));

                case ManagementErrorFrame error:
                    throw new DaqManagementException(
                        $"GET_FILE returned an ERROR frame after FILE_BEGIN was already received (error {error.ErrorCode}).");

                case ManagementFileBeginFrame begin when !fileStarted:
                    ValidateFileBegin(begin, fileClass, recordId);

                    if (begin.FileSizeBytes > 0 && begin.MaxChunkPayload == 0)
                    {
                        throw new DaqManagementException(
                            "GET_FILE declared a non-empty file with a zero max chunk payload.");
                    }

                    fileStarted = true;
                    declaredSize = begin.FileSizeBytes;
                    maxChunkPayload = begin.MaxChunkPayload;
                    fileName = begin.Name;
                    break;

                case ManagementFileChunkFrame chunk when fileStarted:
                    if ((uint)chunk.Bytes.Length > maxChunkPayload)
                    {
                        throw new DaqManagementException(
                            $"GET_FILE delivered a chunk of {chunk.Bytes.Length} bytes, exceeding the advertised limit of {maxChunkPayload} bytes.");
                    }

                    if (receivedSize + (ulong)chunk.Bytes.Length > declaredSize)
                    {
                        throw new DaqManagementException(
                            $"GET_FILE delivered more bytes than the declared file size {declaredSize}.");
                    }

                    await destination.WriteAsync(chunk.Bytes, cancellationToken);
                    receivedSize += (ulong)chunk.Bytes.Length;
                    break;

                case ManagementFileEndFrame when fileStarted:
                    if (receivedSize != declaredSize)
                    {
                        throw new DaqManagementException(
                            $"GET_FILE ended after {receivedSize} bytes but declared {declaredSize} bytes.");
                    }

                    await destination.FlushAsync(cancellationToken);
                    return new DaqGetFileResult.Downloaded(fileName ?? string.Empty, declaredSize);

                default:
                    throw UnexpectedFrame("GET_FILE", response, "FILE_BEGIN, FILE_CHUNK, FILE_END, or ERROR");
            }
        }
    }

    public async Task<DaqManagementResult> TrashFileAsync(
        int recordId,
        CancellationToken cancellationToken = default)
    {
        var requestId = GetNextRequestId();
        await SendFrameAsync(
            ManagementProtocolReader.CreateTrashFileRequest(requestId, recordId),
            ioTimeout,
            cancellationToken);

        var response = await ReadFrameAsync(ioTimeout, cancellationToken);
        EnsureRequestId(response, requestId);
        return response switch
        {
            ManagementActionResultFrame result => MapActionResult(result.ResultCode),
            ManagementErrorFrame error => throw new DaqManagementException(
                $"TRASH_FILE returned an unexpected ERROR frame with code {error.ErrorCode}."),
            _ => throw UnexpectedFrame("TRASH_FILE", response, "ACTION_RESULT")
        };
    }

    public async Task<DaqManagementResult> MarkSstUploadedAsync(
        int recordId,
        CancellationToken cancellationToken = default)
    {
        var requestId = GetNextRequestId();
        await SendFrameAsync(
            ManagementProtocolReader.CreateMarkSstUploadedRequest(requestId, recordId),
            ioTimeout,
            cancellationToken);

        var response = await ReadFrameAsync(ioTimeout, cancellationToken);
        EnsureRequestId(response, requestId);
        return response switch
        {
            ManagementActionResultFrame result => MapActionResult(result.ResultCode),
            ManagementErrorFrame error => throw new DaqManagementException(
                $"MARK_SST_UPLOADED returned an unexpected ERROR frame with code {error.ErrorCode}."),
            _ => throw UnexpectedFrame("MARK_SST_UPLOADED", response, "ACTION_RESULT")
        };
    }

    public async Task PingAsync(CancellationToken cancellationToken = default)
    {
        var requestId = GetNextRequestId();
        await SendFrameAsync(
            ManagementProtocolReader.CreatePingRequest(requestId),
            ioTimeout,
            cancellationToken);

        var response = await ReadFrameAsync(ioTimeout, cancellationToken);
        EnsureRequestId(response, requestId);
        switch (response)
        {
            case ManagementPongFrame:
                return;
            case ManagementErrorFrame error:
                throw new DaqManagementException(
                    $"PING returned an unexpected ERROR frame with code {error.ErrorCode}.");
            default:
                throw UnexpectedFrame("PING", response, "PONG");
        }
    }

    public async Task<DaqManagementResult> SetTimeAsync(
        uint utcSeconds,
        uint microseconds,
        CancellationToken cancellationToken = default)
    {
        var requestId = GetNextRequestId();
        await SendFrameAsync(
            ManagementProtocolReader.CreateSetTimeRequest(requestId, utcSeconds, microseconds),
            ioTimeout,
            cancellationToken);

        var response = await ReadFrameAsync(ioTimeout, cancellationToken);
        EnsureRequestId(response, requestId);
        return response switch
        {
            ManagementActionResultFrame result => MapActionResult(result.ResultCode),
            ManagementErrorFrame error => throw new DaqManagementException(
                $"SET_TIME returned an unexpected ERROR frame with code {error.ErrorCode}."),
            _ => throw UnexpectedFrame("SET_TIME", response, "ACTION_RESULT")
        };
    }

    public async Task<DaqManagementResult> ReplaceConfigAsync(
        byte[] configBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configBytes);

        var requestId = GetNextRequestId();
        await SendFrameAsync(
            ManagementProtocolReader.CreatePutFileBeginRequest(requestId, DaqFileClass.Config, (ulong)configBytes.Length),
            ioTimeout,
            cancellationToken);

        var beginResponse = await ReadFrameAsync(ioTimeout, cancellationToken);
        EnsureRequestId(beginResponse, requestId);
        var beginResult = beginResponse switch
        {
            ManagementActionResultFrame result => MapActionResult(result.ResultCode),
            ManagementErrorFrame error => throw new DaqManagementException(
                $"PUT_FILE_BEGIN returned an unexpected ERROR frame with code {error.ErrorCode}."),
            _ => throw UnexpectedFrame("PUT_FILE_BEGIN", beginResponse, "ACTION_RESULT")
        };

        if (beginResult is DaqManagementResult.Error beginError)
        {
            return beginError;
        }

        for (var offset = 0; offset < configBytes.Length; offset += ManagementProtocolConstants.MaxPutFileChunkPayloadSize)
        {
            var chunkLength = Math.Min(ManagementProtocolConstants.MaxPutFileChunkPayloadSize, configBytes.Length - offset);
            await SendFrameAsync(
                ManagementProtocolReader.CreatePutFileChunkFrame(
                    requestId,
                    configBytes.AsSpan(offset, chunkLength)),
                ioTimeout,
                cancellationToken);
        }

        await SendFrameAsync(
            ManagementProtocolReader.CreatePutFileCommitRequest(requestId),
            ioTimeout,
            cancellationToken);

        var commitResponse = await ReadFrameAsync(commitTimeout, cancellationToken);
        EnsureRequestId(commitResponse, requestId);
        return commitResponse switch
        {
            ManagementActionResultFrame result => MapActionResult(result.ResultCode),
            ManagementErrorFrame error => throw new DaqManagementException(
                $"PUT_FILE_COMMIT returned an unexpected ERROR frame with code {error.ErrorCode}."),
            _ => throw UnexpectedFrame("PUT_FILE_COMMIT", commitResponse, "ACTION_RESULT")
        };
    }

    public void Dispose()
    {
        stream?.Dispose();
        tcpClient?.Dispose();
        stream = null;
        tcpClient = null;
    }

    private NetworkStream GetStream() =>
        stream ?? throw new InvalidOperationException("Management client is not connected.");

    private uint GetNextRequestId()
    {
        nextRequestId = nextRequestId == uint.MaxValue ? 1u : nextRequestId + 1u;
        return nextRequestId;
    }

    private async Task SendFrameAsync(byte[] frame, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var activeStream = GetStream();
        await ExecuteWithTimeoutAsync(
            async token =>
            {
                await activeStream.WriteAsync(frame, token);
                await activeStream.FlushAsync(token);
            },
            timeout,
            $"Timed out writing a management frame after {timeout.TotalSeconds:0} seconds.",
            cancellationToken);
    }

    private async Task<ManagementProtocolFrame> ReadFrameAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (reader.TryReadFrame(out var bufferedFrame) && bufferedFrame is not null)
        {
            return bufferedFrame;
        }

        var activeStream = GetStream();
        var buffer = new byte[ReadBufferSize];
        while (true)
        {
            var read = await ExecuteWithTimeoutAsync(
                async token => await activeStream.ReadAsync(buffer, token),
                timeout,
                $"Timed out waiting for a management response after {timeout.TotalSeconds:0} seconds.",
                cancellationToken);

            if (read == 0)
            {
                throw new DaqManagementException("Connection closed before a management response frame was received.");
            }

            reader.Append(buffer.AsSpan(0, read));
            if (reader.TryReadFrame(out var frame) && frame is not null)
            {
                return frame;
            }
        }
    }

    private static DaqDirectoryRecord CreateDirectoryRecord(DaqDirectoryId directoryId, IReadOnlyList<DaqFileRecord> files) => directoryId switch
    {
        DaqDirectoryId.Root => new DaqRootDirectoryRecord(files),
        DaqDirectoryId.Uploaded => new DaqUploadedDirectoryRecord(files),
        DaqDirectoryId.Trash => new DaqTrashDirectoryRecord(files),
        _ => throw new DaqManagementException($"Unsupported directory id {directoryId}.")
    };

    private static DaqFileRecord CreateFileRecord(
        DaqDirectoryId directoryId,
        ManagementListDirectoryEntryFrame entry)
    {
        if (entry.FileClass == DaqFileClass.Config)
        {
            if (directoryId != DaqDirectoryId.Root)
            {
                throw new DaqManagementException(
                    $"Directory {directoryId} returned a CONFIG entry, which is invalid.");
            }

            return new DaqConfigFileRecord(entry.Name, entry.FileSizeBytes);
        }

        var expectedFileClass = directoryId switch
        {
            DaqDirectoryId.Root => DaqFileClass.RootSst,
            DaqDirectoryId.Uploaded => DaqFileClass.UploadedSst,
            DaqDirectoryId.Trash => DaqFileClass.TrashSst,
            _ => throw new DaqManagementException($"Unsupported directory id {directoryId}.")
        };

        if (entry.FileClass != expectedFileClass)
        {
            throw new DaqManagementException(
                $"Directory {directoryId} returned file class {entry.FileClass}, expected {expectedFileClass}.");
        }

        if (!TryCreateTimestamp(entry.TimestampUtcSeconds, out var timestampUtc))
        {
            return new DaqMalformedSstFileRecord(
                entry.FileClass,
                entry.Name,
                entry.FileSizeBytes,
                entry.RecordId,
                TimestampUtc: null,
                Duration: TimeSpan.FromMilliseconds(entry.DurationMilliseconds),
                entry.SstVersion,
                $"The device reported an invalid SST timestamp ({entry.TimestampUtcSeconds}).");
        }

        return new DaqSstFileRecord(
            entry.FileClass,
            entry.Name,
            entry.FileSizeBytes,
            entry.RecordId,
            timestampUtc,
            TimeSpan.FromMilliseconds(entry.DurationMilliseconds),
            entry.SstVersion);
    }

    private static bool TryCreateTimestamp(long timestampUtcSeconds, out DateTimeOffset timestampUtc)
    {
        if (timestampUtcSeconds < MinUnixTimeSeconds || timestampUtcSeconds > MaxUnixTimeSeconds)
        {
            timestampUtc = default;
            return false;
        }

        timestampUtc = DateTimeOffset.FromUnixTimeSeconds(timestampUtcSeconds);
        return true;
    }

    private static void ValidateFileBegin(ManagementFileBeginFrame begin, DaqFileClass requestedClass, int requestedRecordId)
    {
        if (begin.FileClass != requestedClass)
        {
            throw new DaqManagementException(
                $"GET_FILE response class {begin.FileClass} did not match requested class {requestedClass}.");
        }

        var expectedRecordId = requestedClass == DaqFileClass.Config ? 0 : requestedRecordId;
        if (begin.RecordId != expectedRecordId)
        {
            throw new DaqManagementException(
                $"GET_FILE response record id {begin.RecordId} did not match requested record id {expectedRecordId}.");
        }
    }

    private static void EnsureRequestId(ManagementProtocolFrame frame, uint requestId)
    {
        if (frame.RequestId != requestId)
        {
            throw new DaqManagementException(
                $"Management response request id {frame.RequestId} did not match active request id {requestId}.");
        }
    }

    private static DaqManagementResult MapActionResult(int rawResultCode)
    {
        if (rawResultCode == (int)ManagementResultCode.Ok)
        {
            return new DaqManagementResult.Ok();
        }

        var errorCode = MapKnownErrorCode(rawResultCode);
        return new DaqManagementResult.Error(errorCode, ManagementProtocolHelpers.ToUserMessage(errorCode));
    }

    private static DaqManagementErrorCode MapKnownErrorCode(int rawErrorCode)
    {
        if (!ManagementProtocolHelpers.TryMapErrorCode(rawErrorCode, out var errorCode))
        {
            throw new DaqManagementException($"Management response returned unknown error code {rawErrorCode}.");
        }

        return errorCode;
    }

    private static string CreateErrorMessage(int rawErrorCode) =>
        ManagementProtocolHelpers.ToUserMessage(MapKnownErrorCode(rawErrorCode));

    private static DaqManagementException UnexpectedFrame(
        string operation,
        ManagementProtocolFrame frame,
        string expectedFrames) =>
        new($"{operation} received unexpected frame {frame.Header.FrameType}; expected {expectedFrames}.");

    private static async Task ExecuteWithTimeoutAsync(
        Func<CancellationToken, Task> action,
        TimeSpan timeout,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(timeout);
        try
        {
            await action(linkedCts.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DaqManagementException(timeoutMessage, ex);
        }
        catch (IOException ex)
        {
            throw new DaqManagementException("Management transport I/O failed.", ex);
        }
    }

    private static async Task<T> ExecuteWithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> action,
        TimeSpan timeout,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(timeout);
        try
        {
            return await action(linkedCts.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DaqManagementException(timeoutMessage, ex);
        }
        catch (IOException ex)
        {
            throw new DaqManagementException("Management transport I/O failed.", ex);
        }
    }
}