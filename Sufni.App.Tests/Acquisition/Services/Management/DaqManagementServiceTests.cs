using System;
using System.Collections.Generic;
using Microsoft.Extensions.Time.Testing;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;

using Sufni.App.Acquisition.Services.Management;
namespace Sufni.App.Tests.Acquisition.Services.Management;

public class DaqManagementServiceTests
{
    [Fact]
    public async Task ListDirectoryAsync_ReturnsTypedDirectoryRecord_OnSuccess()
    {
        await using var server = new ManagementTestServer();
        var serverTask = server.RunSessionAsync(async stream =>
        {
            var reader = new ManagementRequestReader();
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

        var result = await CreateService().ListDirectoryAsync(server.Host, server.Port, DaqDirectoryId.Root, cancellationToken: TestContext.Current.CancellationToken);
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
            var reader = new ManagementRequestReader();
            var request = Assert.IsType<ManagementListDirectoryRequestFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateErrorFrame(request.RequestId, (int)DaqManagementErrorCode.Busy));
        });

        var result = await CreateService().ListDirectoryAsync(server.Host, server.Port, DaqDirectoryId.Root, cancellationToken: TestContext.Current.CancellationToken);
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
            var reader = new ManagementRequestReader();
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

        var result = await CreateService().ListDirectoryAsync(server.Host, server.Port, DaqDirectoryId.Root, cancellationToken: TestContext.Current.CancellationToken);
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
    public async Task GetFileAsync_StreamsBytesToDestination_OnSuccess()
    {
        await using var server = new ManagementTestServer();
        byte[] expectedBytes = [1, 2, 3, 4, 5, 6];
        var serverTask = server.RunSessionAsync(async stream =>
        {
            var reader = new ManagementRequestReader();
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
                    maxChunkPayload: 4,
                    name: "00042.SST"));
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateFileChunkFrame(request.RequestId, expectedBytes[..2]));
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateFileChunkFrame(request.RequestId, expectedBytes[2..]));
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateFileEndFrame(request.RequestId));
        });

        using var destination = new MemoryStream();
        var result = await CreateService().GetFileAsync(server.Host, server.Port, DaqFileClass.RootSst, 42, destination, cancellationToken: TestContext.Current.CancellationToken);
        await serverTask;

        var loaded = Assert.IsType<DaqGetFileResult.Downloaded>(result);
        Assert.Equal("00042.SST", loaded.Name);
        Assert.Equal((ulong)expectedBytes.Length, loaded.FileSizeBytes);
        Assert.Equal(expectedBytes, destination.ToArray());
    }

    [Fact]
    public async Task GetFileAsync_RejectsChunksAboveAdvertisedLimit()
    {
        await using var server = new ManagementTestServer();
        var serverTask = server.RunSessionAsync(async stream =>
        {
            var reader = new ManagementRequestReader();
            var request = Assert.IsType<ManagementGetFileRequestFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateFileBeginFrame(
                    request.RequestId,
                    fileClass: DaqFileClass.RootSst,
                    recordId: request.RecordId,
                    fileSizeBytes: 3,
                    maxChunkPayload: 2,
                    name: "99999.SST"));
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateFileChunkFrame(request.RequestId, [1, 2, 3]));
        });

        using var destination = new MemoryStream();
        _ = await Assert.ThrowsAsync<DaqManagementException>(() =>
            CreateService().GetFileAsync(server.Host, server.Port, DaqFileClass.RootSst, 12, destination, cancellationToken: TestContext.Current.CancellationToken));
        await serverTask;

        Assert.Empty(destination.ToArray());
    }

    [Fact]
    public async Task MarkSstUploadedAsync_ReturnsTypedResult()
    {
        await using var server = new ManagementTestServer();
        var serverTask = server.RunSessionAsync(async stream =>
        {
            var reader = new ManagementRequestReader();
            var request = Assert.IsType<ManagementMarkSstUploadedRequestFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            Assert.Equal((uint)1, request.RequestId);
            Assert.Equal(42, request.RecordId);
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateActionResultFrame(request.RequestId, 0));
        });

        var result = await CreateService().MarkSstUploadedAsync(server.Host, server.Port, 42, cancellationToken: TestContext.Current.CancellationToken);
        await serverTask;

        Assert.IsType<DaqManagementResult.Ok>(result);
    }

    [Fact]
    public async Task TrashFileAsync_ReturnsTypedError_WhenActionResultIsKnownFailure()
    {
        await using var server = new ManagementTestServer();
        var serverTask = server.RunSessionAsync(async stream =>
        {
            var reader = new ManagementRequestReader();
            var request = Assert.IsType<ManagementTrashFileRequestFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateActionResultFrame(request.RequestId, (int)DaqManagementErrorCode.Busy));
        });

        var result = await CreateService().TrashFileAsync(server.Host, server.Port, 15, cancellationToken: TestContext.Current.CancellationToken);
        await serverTask;

        var error = Assert.IsType<DaqManagementResult.Error>(result);
        Assert.Equal(DaqManagementErrorCode.Busy, error.ErrorCode);
    }

    [Fact]
    public async Task SetTimeAsync_UsesTrimmedAverageAcrossFivePingSamples()
    {
        await using var server = new ManagementTestServer();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        int[] roundTripMilliseconds = [10, 50, 20, 30, 40];
        var observedRequestIds = new List<uint>();
        var serverTask = server.RunSessionAsync(async stream =>
        {
            var reader = new ManagementRequestReader();
            for (var i = 0; i < 5; i++)
            {
                var ping = Assert.IsType<ManagementPingFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
                observedRequestIds.Add(ping.RequestId);
                timeProvider.Advance(TimeSpan.FromMilliseconds(roundTripMilliseconds[i]));
                await ManagementTestServer.WriteFrameAsync(stream, ManagementProtocolTestFrames.CreatePongFrame(ping.RequestId));
            }

            var setTime = Assert.IsType<ManagementSetTimeRequestFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            observedRequestIds.Add(setTime.RequestId);
            Assert.Equal((uint)1_700_000_000, setTime.UtcSeconds);
            Assert.Equal((uint)98_333, setTime.Microseconds);
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateActionResultFrame(setTime.RequestId, 0));
        });

        var result = await CreateService(timeProvider).SetTimeAsync(server.Host, server.Port, cancellationToken: TestContext.Current.CancellationToken);
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
            var reader = new ManagementRequestReader();
            var begin = Assert.IsType<ManagementPutFileBeginRequestFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            Assert.Equal((uint)1, begin.RequestId);
            Assert.Equal(DaqFileClass.Config, begin.FileClass);
            Assert.Equal((ulong)expectedBytes.Length, begin.FileSizeBytes);
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateActionResultFrame(begin.RequestId, 0));

            var chunk1 = Assert.IsType<ManagementPutFileChunkFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            Assert.Equal((uint)1, chunk1.RequestId);
            Assert.Equal(expectedBytes[..512], chunk1.Bytes);

            var chunk2 = Assert.IsType<ManagementPutFileChunkFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            Assert.Equal((uint)1, chunk2.RequestId);
            Assert.Equal(expectedBytes[512..], chunk2.Bytes);

            var commit = Assert.IsType<ManagementPutFileCommitFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            Assert.Equal((uint)1, commit.RequestId);
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateActionResultFrame(commit.RequestId, 0));
        });

        var result = await CreateService().ReplaceConfigAsync(server.Host, server.Port, expectedBytes, cancellationToken: TestContext.Current.CancellationToken);
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
            var reader = new ManagementRequestReader();
            _ = Assert.IsType<ManagementListDirectoryRequestFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            await releaseServer.Task;
        });

        await Assert.ThrowsAsync<DaqManagementException>(() =>
            CreateService(ioTimeout: TimeSpan.FromMilliseconds(50)).ListDirectoryAsync(server.Host, server.Port, DaqDirectoryId.Root, cancellationToken: TestContext.Current.CancellationToken));

        releaseServer.TrySetResult();
        await serverTask;
    }

    private static DaqManagementService CreateService(
        TimeProvider? timeProvider = null,
        TimeSpan? connectTimeout = null,
        TimeSpan? ioTimeout = null,
        TimeSpan? commitTimeout = null) =>
        new(
            timeProvider ?? new FakeTimeProvider(),
            connectTimeout ?? TimeSpan.FromSeconds(1),
            ioTimeout ?? TimeSpan.FromSeconds(1),
            commitTimeout ?? TimeSpan.FromSeconds(1));

}
