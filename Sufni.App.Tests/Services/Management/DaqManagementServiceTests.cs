using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Sufni.App.Services.Management;

namespace Sufni.App.Tests.Services.Management;

public class DaqManagementServiceTests
{
    [Fact]
    public async Task ListDirectoryAsync_ReturnsTypedDirectoryRecord_OnSuccess()
    {
        await using var server = new ManagementTestServer();
        var serverTask = server.RunSessionAsync(async stream =>
        {
            var reader = new ManagementProtocolReader();
            var request = Assert.IsType<ManagementListDirectoryRequestFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            Assert.Equal((uint)1, request.RequestId);
            Assert.Equal(DaqDirectoryId.Root, request.DirectoryId);

            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateListDirectoryEntryFrame(
                    request.RequestId,
                    directoryId: DaqDirectoryId.Root,
                    fileClass: DaqFileClass.Config,
                    recordId: 0,
                    fileSizeBytes: 48,
                    timestampUtcSeconds: 0,
                    durationMilliseconds: 0,
                    sstVersion: 0,
                    name: "CONFIG"));
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateListDirectoryEntryFrame(
                    request.RequestId,
                    directoryId: DaqDirectoryId.Root,
                    fileClass: DaqFileClass.RootSst,
                    recordId: 42,
                    fileSizeBytes: 4096,
                    timestampUtcSeconds: 1_700_000_100,
                    durationMilliseconds: 6500,
                    sstVersion: 4,
                    name: "00042.SST"));
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateListDirectoryDoneFrame(request.RequestId, 2));
        });

        var result = await CreateService().ListDirectoryAsync(server.Host, server.Port, DaqDirectoryId.Root);
        await serverTask;

        var listed = Assert.IsType<DaqListDirectoryResult.Listed>(result);
        var directory = Assert.IsType<DaqRootDirectoryRecord>(listed.Directory);
        Assert.Collection(
            directory.Files,
            first =>
            {
                var config = Assert.IsType<DaqConfigFileRecord>(first);
                Assert.Equal("CONFIG", config.Name);
                Assert.Equal((ulong)48, config.FileSizeBytes);
            },
            second =>
            {
                var sst = Assert.IsType<DaqSstFileRecord>(second);
                Assert.Equal(DaqFileClass.RootSst, sst.FileClass);
                Assert.Equal(42, sst.RecordId);
                Assert.Equal("00042.SST", sst.Name);
                Assert.Equal((ulong)4096, sst.FileSizeBytes);
                Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_100), sst.TimestampUtc);
                Assert.Equal(TimeSpan.FromMilliseconds(6500), sst.Duration);
                Assert.Equal((byte)4, sst.SstVersion);
            });
    }

    [Fact]
    public async Task ListDirectoryAsync_ReturnsTypedError_WhenDeviceRejectsRequest()
    {
        await using var server = new ManagementTestServer();
        var serverTask = server.RunSessionAsync(async stream =>
        {
            var reader = new ManagementProtocolReader();
            var request = Assert.IsType<ManagementListDirectoryRequestFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateErrorFrame(request.RequestId, (int)DaqManagementErrorCode.Busy));
        });

        var result = await CreateService().ListDirectoryAsync(server.Host, server.Port, DaqDirectoryId.Uploaded);
        await serverTask;

        var error = Assert.IsType<DaqListDirectoryResult.Error>(result);
        Assert.Equal(DaqManagementErrorCode.Busy, error.ErrorCode);
    }

    [Fact]
    public async Task ListDirectoryAsync_ReturnsMalformedSstRecord_WhenDeviceReportsOutOfRangeTimestamp()
    {
        await using var server = new ManagementTestServer();
        var serverTask = server.RunSessionAsync(async stream =>
        {
            var reader = new ManagementProtocolReader();
            var request = Assert.IsType<ManagementListDirectoryRequestFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));

            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateListDirectoryEntryFrame(
                    request.RequestId,
                    directoryId: DaqDirectoryId.Root,
                    fileClass: DaqFileClass.RootSst,
                    recordId: 42,
                    fileSizeBytes: 4096,
                    timestampUtcSeconds: 7_809_639_177_543_108_896L,
                    durationMilliseconds: 6500,
                    sstVersion: 4,
                    name: "00042.SST"));
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateListDirectoryDoneFrame(request.RequestId, 1));
        });

        var result = await CreateService().ListDirectoryAsync(server.Host, server.Port, DaqDirectoryId.Root);
        await serverTask;

        var listed = Assert.IsType<DaqListDirectoryResult.Listed>(result);
        var directory = Assert.IsType<DaqRootDirectoryRecord>(listed.Directory);
        var malformed = Assert.IsType<DaqMalformedSstFileRecord>(Assert.Single(directory.Files));
        Assert.Equal(DaqFileClass.RootSst, malformed.FileClass);
        Assert.Equal(42, malformed.RecordId);
        Assert.Equal("00042.SST", malformed.Name);
        Assert.Equal((byte)4, malformed.SstVersion);
        Assert.Equal(TimeSpan.FromMilliseconds(6500), malformed.Duration);
        Assert.Null(malformed.TimestampUtc);
        Assert.Contains("invalid SST timestamp", malformed.MalformedMessage);
    }

    [Fact]
    public async Task GetFileAsync_ReturnsLoadedBytes_OnSuccess()
    {
        await using var server = new ManagementTestServer();
        var expectedBytes = new byte[] { 1, 2, 3, 4, 5, 6 };
        var serverTask = server.RunSessionAsync(async stream =>
        {
            var reader = new ManagementProtocolReader();
            var request = Assert.IsType<ManagementGetFileRequestFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            Assert.Equal((uint)1, request.RequestId);
            Assert.Equal(DaqFileClass.RootSst, request.FileClass);
            Assert.Equal(42, request.RecordId);

            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateFileBeginFrame(
                    request.RequestId,
                    fileClass: DaqFileClass.RootSst,
                    recordId: 42,
                    fileSizeBytes: (ulong)expectedBytes.Length,
                    name: "00042.SST"));
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateFileChunkFrame(request.RequestId, expectedBytes[..2]));
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateFileChunkFrame(request.RequestId, expectedBytes[2..]));
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateFileEndFrame(request.RequestId));
        });

        var result = await CreateService().GetFileAsync(server.Host, server.Port, DaqFileClass.RootSst, 42);
        await serverTask;

        var loaded = Assert.IsType<DaqGetFileResult.Loaded>(result);
        Assert.Equal("00042.SST", loaded.Name);
        Assert.Equal(expectedBytes, loaded.Bytes);
    }

    [Fact]
    public async Task GetFileAsync_RejectsOversizedFiles_BeforeAllocation()
    {
        await using var server = new ManagementTestServer();
        var serverTask = server.RunSessionAsync(async stream =>
        {
            var reader = new ManagementProtocolReader();
            var request = Assert.IsType<ManagementGetFileRequestFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateFileBeginFrame(
                    request.RequestId,
                    fileClass: DaqFileClass.RootSst,
                    recordId: request.RecordId,
                    fileSizeBytes: (ulong)int.MaxValue + 1UL,
                    name: "99999.SST"));
        });

        var exception = await Assert.ThrowsAsync<DaqManagementException>(() =>
            CreateService().GetFileAsync(server.Host, server.Port, DaqFileClass.RootSst, 12));
        await serverTask;

        Assert.Equal((ulong)int.MaxValue + 1UL, exception.DeclaredFileSizeBytes);
        Assert.Equal((ulong)int.MaxValue, exception.MaximumSupportedFileSizeBytes);
    }

    [Fact]
    public async Task TrashFileAsync_ReturnsTypedError_WhenActionResultIsKnownFailure()
    {
        await using var server = new ManagementTestServer();
        var serverTask = server.RunSessionAsync(async stream =>
        {
            var reader = new ManagementProtocolReader();
            var request = Assert.IsType<ManagementTrashFileRequestFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateActionResultFrame(request.RequestId, (int)DaqManagementErrorCode.Busy));
        });

        var result = await CreateService().TrashFileAsync(server.Host, server.Port, 15);
        await serverTask;

        var error = Assert.IsType<DaqManagementResult.Error>(result);
        Assert.Equal(DaqManagementErrorCode.Busy, error.ErrorCode);
    }

    [Fact]
    public async Task SetTimeAsync_UsesTrimmedAverageAcrossFivePingSamples()
    {
        await using var server = new ManagementTestServer();
        var observedRequestIds = new List<uint>();
        var serverTask = server.RunSessionAsync(async stream =>
        {
            var reader = new ManagementProtocolReader();
            for (var i = 0; i < 5; i++)
            {
                var ping = Assert.IsType<ManagementPingFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
                observedRequestIds.Add(ping.RequestId);
                await ManagementTestServer.WriteFrameAsync(stream, ManagementProtocolTestFrames.CreatePongFrame(ping.RequestId));
            }

            var setTime = Assert.IsType<ManagementSetTimeRequestFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            observedRequestIds.Add(setTime.RequestId);
            Assert.Equal((uint)1_700_000_000, setTime.UtcSeconds);
            Assert.Equal((uint)315_000, setTime.Microseconds);
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateActionResultFrame(setTime.RequestId, 0));
        });

        var baseInstant = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var roundTrips = new[]
        {
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(30),
            TimeSpan.FromMilliseconds(40)
        };
        var timestampValues = new List<long>();
        long cursor = 0;
        foreach (var roundTrip in roundTrips)
        {
            timestampValues.Add(cursor);
            cursor += ToStopwatchTicks(roundTrip);
            timestampValues.Add(cursor);
            cursor += ToStopwatchTicks(TimeSpan.FromMilliseconds(1));
        }

        var timeProvider = new ScriptedTimeProvider(
            utcNowValues:
            [
                baseInstant,
                baseInstant.AddMilliseconds(100),
                baseInstant.AddMilliseconds(200),
                baseInstant.AddMilliseconds(300),
                baseInstant.AddMilliseconds(400)
            ],
            timestampValues: timestampValues);

        var result = await CreateService(timeProvider).SetTimeAsync(server.Host, server.Port);
        await serverTask;

        var ok = Assert.IsType<DaqSetTimeResult.Ok>(result);
        Assert.InRange(ok.RoundTripTime, TimeSpan.FromMilliseconds(29.9), TimeSpan.FromMilliseconds(30.1));
        Assert.Equal(new uint[] { 1, 2, 3, 4, 5, 6 }, observedRequestIds);
    }

    [Fact]
    public async Task ReplaceConfigAsync_SendsChunkedUpload_AndReturnsCommitResult()
    {
        await using var server = new ManagementTestServer();
        var expectedBytes = Enumerable.Range(0, 600).Select(i => (byte)(i % 251)).ToArray();
        var serverTask = server.RunSessionAsync(async stream =>
        {
            var reader = new ManagementProtocolReader();
            var begin = Assert.IsType<ManagementPutFileBeginRequestFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            Assert.Equal((uint)1, begin.RequestId);
            Assert.Equal(DaqFileClass.Config, begin.FileClass);
            Assert.Equal((ulong)expectedBytes.Length, begin.FileSizeBytes);
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateActionResultFrame(begin.RequestId, 0));

            var chunk1 = Assert.IsType<ManagementPutFileChunkFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            Assert.Equal((uint)2, chunk1.RequestId);
            Assert.Equal(expectedBytes[..512], chunk1.Bytes);

            var chunk2 = Assert.IsType<ManagementPutFileChunkFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            Assert.Equal((uint)3, chunk2.RequestId);
            Assert.Equal(expectedBytes[512..], chunk2.Bytes);

            var commit = Assert.IsType<ManagementPutFileCommitFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            Assert.Equal((uint)4, commit.RequestId);
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateActionResultFrame(commit.RequestId, 0));
        });

        var result = await CreateService().ReplaceConfigAsync(server.Host, server.Port, expectedBytes);
        await serverTask;

        Assert.IsType<DaqManagementResult.Ok>(result);
    }

    [Fact]
    public async Task ListDirectoryAsync_ThrowsWhenResponseStallsPastTimeout()
    {
        await using var server = new ManagementTestServer();
        var releaseServer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = server.RunSessionAsync(async stream =>
        {
            var reader = new ManagementProtocolReader();
            _ = Assert.IsType<ManagementListDirectoryRequestFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            await releaseServer.Task;
        });

        await Assert.ThrowsAsync<DaqManagementException>(() =>
            CreateService(ioTimeout: TimeSpan.FromMilliseconds(50)).ListDirectoryAsync(server.Host, server.Port, DaqDirectoryId.Root));

        releaseServer.TrySetResult();
        await serverTask;
    }

    private static DaqManagementService CreateService(
        TimeProvider? timeProvider = null,
        TimeSpan? connectTimeout = null,
        TimeSpan? ioTimeout = null,
        TimeSpan? commitTimeout = null) =>
        new(
            timeProvider ?? TimeProvider.System,
            connectTimeout ?? TimeSpan.FromSeconds(1),
            ioTimeout ?? TimeSpan.FromSeconds(1),
            commitTimeout ?? TimeSpan.FromSeconds(1));

    private sealed class ScriptedTimeProvider : TimeProvider
    {
        private readonly Queue<DateTimeOffset> utcNowValues;
        private readonly Queue<long> timestampValues;

        public ScriptedTimeProvider(
            IEnumerable<DateTimeOffset> utcNowValues,
            IEnumerable<long> timestampValues)
        {
            this.utcNowValues = new Queue<DateTimeOffset>(utcNowValues);
            this.timestampValues = new Queue<long>(timestampValues);
        }

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => utcNowValues.Dequeue();

        public override long GetTimestamp() => timestampValues.Dequeue();
    }

    private static long ToStopwatchTicks(TimeSpan duration) =>
        (long)Math.Round(duration.TotalSeconds * Stopwatch.Frequency, MidpointRounding.AwayFromZero);
}