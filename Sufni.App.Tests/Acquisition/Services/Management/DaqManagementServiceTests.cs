using System.Net;
using Sufni.App.Infrastructure;
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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadConfigAsync_UsesConfigTargetAndReturnsDocumentOrTypedRejection(bool rejected)
    {
        await using var server = new ManagementTestServer();
        var bytes = System.Text.Encoding.UTF8.GetBytes("STA_SSID=trail\n");
        var serving = server.RunSessionAsync(async stream =>
        {
            var request = Assert.IsType<ManagementGetFileRequestFrame>(
                await ManagementTestServer.ReadFrameAsync(stream, new ManagementRequestReader()));
            Assert.Equal((ushort)1, (ushort)request.FileClass);
            Assert.Equal(0, request.RecordId);
            if (rejected)
            {
                await ManagementTestServer.WriteFrameAsync(stream,
                    ManagementProtocolTestFrames.CreateErrorFrame(request.RequestId, -3));
                return;
            }
            await ManagementTestServer.WriteFrameAsync(stream, ManagementProtocolTestFrames.CreateFileBeginFrame(
                request.RequestId, DaqFileClass.Config, 0, (ulong)bytes.Length, 128, "CONFIG"));
            await ManagementTestServer.WriteFrameAsync(stream, ManagementProtocolTestFrames.CreateFileChunkFrame(request.RequestId, bytes));
            await ManagementTestServer.WriteFrameAsync(stream, ManagementProtocolTestFrames.CreateFileEndFrame(request.RequestId));
        });

        var result = await CreateService().ReadConfigAsync(new ServiceEndpoint(server.Host, server.Port), TestContext.Current.CancellationToken);
        await serving.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        if (rejected) Assert.Equal(DaqManagementErrorCode.Busy, Assert.IsType<DaqConfigReadResult.Error>(result).ErrorCode);
        else Assert.Equal("trail", Assert.IsType<DaqConfigReadResult.Read>(result).Document.GetValue("STA_SSID"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListDirectoryAsync_ReturnsTypedDirectoryRecord_OnSuccess(bool unreachableFirstAddress)
    {
        await using var server = new ManagementTestServer();
        var serverTask = server.RunSessionAsync(async stream =>
        {
            var reader = new ManagementRequestReader();
            var request = Assert.IsType<ManagementListDirectoryRequestFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            Assert.Equal((uint)1, request.RequestId);
            Assert.Equal((ushort)1, (ushort)request.DirectoryId);

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

        var result = await CreateService().ListDirectoryAsync(new ServiceEndpoint(unreachableFirstAddress
                    ? [IPAddress.IPv6Loopback, IPAddress.Loopback]
                    : [IPAddress.Loopback], server.Port), cancellationToken: TestContext.Current.CancellationToken);
        await serverTask.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

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
                ManagementProtocolTestFrames.CreateErrorFrame(request.RequestId, -3));
        });

        var result = await CreateService().ListDirectoryAsync(new ServiceEndpoint(server.Host, server.Port), cancellationToken: TestContext.Current.CancellationToken);
        await serverTask.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

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

        var result = await CreateService().ListDirectoryAsync(new ServiceEndpoint(server.Host, server.Port), cancellationToken: TestContext.Current.CancellationToken);
        await serverTask.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        var listed = Assert.IsType<DaqListDirectoryResult.Listed>(result);
        var directory = Assert.IsType<DaqRootDirectoryRecord>(listed.Directory);
        var malformed = Assert.IsType<DaqMalformedSstFileRecord>(Assert.Single(directory.Files));
        Assert.Equal("00042.SST", malformed.Name);
        Assert.Null(malformed.TimestampUtc);
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
            Assert.Equal((ushort)2, (ushort)request.FileClass);
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
        await using var session = await CreateService().OpenSessionAsync(new ServiceEndpoint(server.Host, server.Port), TestContext.Current.CancellationToken);
        var result = await session.GetFileAsync(DaqFileClass.RootSst, 42, destination, cancellationToken: TestContext.Current.CancellationToken);
        await serverTask.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

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
        await using var session = await CreateService().OpenSessionAsync(new ServiceEndpoint(server.Host, server.Port), TestContext.Current.CancellationToken);
        _ = await Assert.ThrowsAsync<DaqManagementException>(() =>
            session.GetFileAsync(DaqFileClass.RootSst, 12, destination, cancellationToken: TestContext.Current.CancellationToken));
        await serverTask.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

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

        var result = await CreateService().MarkSstUploadedAsync(new ServiceEndpoint(server.Host, server.Port), 42, cancellationToken: TestContext.Current.CancellationToken);
        await serverTask.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

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
                ManagementProtocolTestFrames.CreateActionResultFrame(request.RequestId, -3));
        });

        var result = await CreateService().TrashFileAsync(new ServiceEndpoint(server.Host, server.Port), 15, cancellationToken: TestContext.Current.CancellationToken);
        await serverTask.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        var error = Assert.IsType<DaqManagementResult.Error>(result);
        Assert.Equal(DaqManagementErrorCode.Busy, error.ErrorCode);
    }

    [Fact]
    public async Task SetTimeAsync_UsesTrimmedAverageAcrossFivePingSamples()
    {
        await using var server = new ManagementTestServer();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        int[] roundTripMilliseconds = [10, 50, 20, 30, 40];
        var serverTask = server.RunSessionAsync(async stream =>
        {
            var reader = new ManagementRequestReader();
            for (var i = 0; i < 5; i++)
            {
                var ping = Assert.IsType<ManagementPingFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
                timeProvider.Advance(TimeSpan.FromMilliseconds(roundTripMilliseconds[i]));
                await ManagementTestServer.WriteFrameAsync(stream, ManagementProtocolTestFrames.CreatePongFrame(ping.RequestId));
            }

            var setTime = Assert.IsType<ManagementSetTimeRequestFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            Assert.Equal((uint)1_700_000_000, setTime.UtcSeconds);
            Assert.InRange(setTime.Microseconds, 98_332u, 98_334u);
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateActionResultFrame(setTime.RequestId, 0));
        });

        var result = await CreateService(timeProvider).SetTimeAsync(new ServiceEndpoint(server.Host, server.Port), cancellationToken: TestContext.Current.CancellationToken);
        await serverTask.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        var ok = Assert.IsType<DaqSetTimeResult.Ok>(result);
        Assert.InRange(ok.RoundTripTime, TimeSpan.FromMilliseconds(29.9), TimeSpan.FromMilliseconds(30.1));
    }

    [Fact]
    public async Task WriteConfigAsync_SendsChunkedUpload_AndReturnsCommitResult()
    {
        await using var server = new ManagementTestServer();
        var expectedBytes = Enumerable.Range(0, 600).Select(i => (byte)(i % 251)).ToArray();
        var serverTask = server.RunSessionAsync(async stream =>
        {
            var reader = new ManagementRequestReader();
            var begin = Assert.IsType<ManagementPutFileBeginRequestFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            Assert.Equal((ushort)1, (ushort)begin.FileClass);
            Assert.Equal((ulong)expectedBytes.Length, begin.FileSizeBytes);
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateActionResultFrame(begin.RequestId, 0));

            var chunk1 = Assert.IsType<ManagementPutFileChunkFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            Assert.Equal(begin.RequestId, chunk1.RequestId);
            Assert.Equal(expectedBytes[..512], chunk1.Bytes);

            var chunk2 = Assert.IsType<ManagementPutFileChunkFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            Assert.Equal(begin.RequestId, chunk2.RequestId);
            Assert.Equal(expectedBytes[512..], chunk2.Bytes);

            var commit = Assert.IsType<ManagementPutFileCommitFrame>(await ManagementTestServer.ReadFrameAsync(stream, reader));
            Assert.Equal(begin.RequestId, commit.RequestId);
            await ManagementTestServer.WriteFrameAsync(stream,
                ManagementProtocolTestFrames.CreateActionResultFrame(commit.RequestId, 0));
        });

        var result = await CreateService().WriteConfigAsync(new ServiceEndpoint(server.Host, server.Port), expectedBytes, cancellationToken: TestContext.Current.CancellationToken);
        await serverTask.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

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
            await releaseServer.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        });

        await Assert.ThrowsAsync<DaqManagementException>(() =>
            CreateService(ioTimeout: TimeSpan.FromMilliseconds(50)).ListDirectoryAsync(new ServiceEndpoint(server.Host, server.Port), cancellationToken: TestContext.Current.CancellationToken));

        releaseServer.TrySetResult();
        await serverTask.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 3)]
    [InlineData(12, 3)]
    [InlineData(14, 16)]
    [InlineData(-1, 0)]
    public async Task ListDirectoryAsync_RejectsMalformedOrTruncatedWireReply(int offset, byte value)
    {
        await using var server = new ManagementTestServer();
        var serving = server.RunSessionAsync(async stream =>
        {
            var request = await ManagementTestServer.ReadFrameAsync(stream);
            var bytes = Convert.FromHexString("4D474D5402001100000000000400000000000000");
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), request.RequestId);
            if (offset < 0) bytes = bytes[..^2];
            else bytes[offset] = value;
            await ManagementTestServer.WriteFrameAsync(stream, bytes);
        });
        var operation = () => CreateService().ListDirectoryAsync(new ServiceEndpoint(server.Host, server.Port), TestContext.Current.CancellationToken);
        if (offset < 0) await Assert.ThrowsAsync<DaqManagementException>(operation);
        else await Assert.ThrowsAsync<FormatException>(operation);
        await serving.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
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
