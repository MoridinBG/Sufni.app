
using Sufni.App.Acquisition.Services.Management;
namespace Sufni.App.Tests.Services.Management;

// Loopback TCP tests with real timeouts: serialized so parallel CPU/socket
// contention does not turn the timing budgets into flakes.
[CollectionDefinition("ManagementLoopback", DisableParallelization = true)]
public class ManagementLoopbackCollectionDefinition;

[Collection("ManagementLoopback")]
public class ManagementClientTests
{
    [Fact]
    public async Task ConnectAsync_RejectsSecondConnection_WhenAlreadyConnected()
    {
        await using var server = new ManagementTestServer();
        var releaseServer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = server.RunSessionAsync(async _ => await releaseServer.Task);
        using var client = new ManagementClient(
            connectTimeout: TimeSpan.FromSeconds(1),
            ioTimeout: TimeSpan.FromSeconds(1),
            commitTimeout: TimeSpan.FromSeconds(1));

        await client.ConnectAsync(server.Host, server.Port);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ConnectAsync(server.Host, server.Port));

        releaseServer.SetResult();
        await serverTask;
    }

    [Fact]
    public async Task PingAsync_Throws_WhenClientIsNotConnected()
    {
        using var client = new ManagementClient();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.PingAsync());
    }

    [Fact]
    public async Task GetFileAsync_RejectsNullDestination()
    {
        using var client = new ManagementClient();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.GetFileAsync(DaqFileClass.RootSst, recordId: 1, destination: null!));
    }

    [Fact]
    public async Task GetFileAsync_RejectsReadOnlyDestination()
    {
        using var client = new ManagementClient();
        using var destination = new MemoryStream(new byte[1], writable: false);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.GetFileAsync(DaqFileClass.RootSst, recordId: 1, destination));
    }

    [Fact]
    public async Task GetFileAsync_RejectsNonZeroConfigRecordId()
    {
        using var client = new ManagementClient();
        using var destination = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.GetFileAsync(DaqFileClass.Config, recordId: 1, destination));
    }

    [Fact]
    public async Task ReplaceConfigAsync_RejectsNullConfigBytes()
    {
        using var client = new ManagementClient();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.ReplaceConfigAsync(configBytes: null!));
    }

    [Fact]
    public async Task PingAsync_RoundTripsThroughLoopbackServer()
    {
        await using var server = new ManagementTestServer();
        var serverTask = server.RunSessionAsync(async stream =>
        {
            var request = await ManagementTestServer.ReadFrameAsync(stream);
            Assert.IsType<ManagementPingFrame>(request);
            await ManagementTestServer.WriteFrameAsync(
                stream,
                ManagementProtocolReader.CreateFrame(ManagementFrameType.Pong, request.Header.RequestId, []));
        });
        using var client = CreateConnectedClient();
        await client.ConnectAsync(server.Host, server.Port);

        await client.PingAsync();

        await serverTask;
    }

    [Fact]
    public async Task PingAsync_SurfacesErrorFrames()
    {
        await using var server = new ManagementTestServer();
        var serverTask = server.RunSessionAsync(async stream =>
        {
            var request = await ManagementTestServer.ReadFrameAsync(stream);
            var payload = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(payload, 2);
            await ManagementTestServer.WriteFrameAsync(
                stream,
                ManagementProtocolReader.CreateFrame(ManagementFrameType.Error, request.Header.RequestId, payload));
        });
        using var client = CreateConnectedClient();
        await client.ConnectAsync(server.Host, server.Port);

        await Assert.ThrowsAsync<DaqManagementException>(() => client.PingAsync());

        await serverTask;
    }

    [Fact]
    public async Task GetFileAsync_DownloadsAFramedFile()
    {
        var fileBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        await using var server = new ManagementTestServer();
        var serverTask = server.RunSessionAsync(async stream =>
        {
            var request = await ManagementTestServer.ReadFrameAsync(stream);
            var requestId = request.Header.RequestId;

            await ManagementTestServer.WriteFrameAsync(
                stream,
                ManagementProtocolReader.CreateFrame(
                    ManagementFrameType.FileBegin,
                    requestId,
                    CreateFileBeginPayload(DaqFileClass.RootSst, recordId: 42, fileSize: (ulong)fileBytes.Length, maxChunkPayload: 6, name: "RIDE.SST")));
            await ManagementTestServer.WriteFrameAsync(
                stream,
                ManagementProtocolReader.CreateFrame(ManagementFrameType.FileChunk, requestId, fileBytes.AsSpan(0, 6)));
            await ManagementTestServer.WriteFrameAsync(
                stream,
                ManagementProtocolReader.CreateFrame(ManagementFrameType.FileChunk, requestId, fileBytes.AsSpan(6)));
            await ManagementTestServer.WriteFrameAsync(
                stream,
                ManagementProtocolReader.CreateFrame(ManagementFrameType.FileEnd, requestId, []));
        });
        using var client = CreateConnectedClient();
        await client.ConnectAsync(server.Host, server.Port);
        using var destination = new MemoryStream();

        var result = await client.GetFileAsync(DaqFileClass.RootSst, recordId: 42, destination);

        var downloaded = Assert.IsType<DaqGetFileResult.Downloaded>(result);
        Assert.Equal("RIDE.SST", downloaded.Name);
        Assert.Equal((ulong)fileBytes.Length, downloaded.FileSizeBytes);
        Assert.Equal(fileBytes, destination.ToArray());
        await serverTask;
    }

    [Fact]
    public async Task PingAsync_Throws_WhenServerDisconnectsMidFrame()
    {
        await using var server = new ManagementTestServer();
        var serverTask = server.RunSessionAsync(async stream =>
        {
            var request = await ManagementTestServer.ReadFrameAsync(stream);
            var pong = ManagementProtocolReader.CreateFrame(ManagementFrameType.Pong, request.Header.RequestId, []);
            // Half a header, then hang up.
            await ManagementTestServer.WriteFrameAsync(stream, pong[..6]);
        });
        using var client = CreateConnectedClient();
        await client.ConnectAsync(server.Host, server.Port);

        await Assert.ThrowsAsync<DaqManagementException>(() => client.PingAsync());

        await serverTask;
    }

    [Fact]
    public async Task PingAsync_TimesOut_WhenServerNeverReplies()
    {
        await using var server = new ManagementTestServer();
        var releaseServer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = server.RunSessionAsync(async _ => await releaseServer.Task);
        using var client = new ManagementClient(
            connectTimeout: TimeSpan.FromSeconds(1),
            ioTimeout: TimeSpan.FromMilliseconds(200),
            commitTimeout: TimeSpan.FromSeconds(1));
        await client.ConnectAsync(server.Host, server.Port);

        await Assert.ThrowsAsync<DaqManagementException>(() => client.PingAsync());

        releaseServer.SetResult();
        await serverTask;
    }

    private static ManagementClient CreateConnectedClient() => new(
        connectTimeout: TimeSpan.FromSeconds(2),
        ioTimeout: TimeSpan.FromSeconds(2),
        commitTimeout: TimeSpan.FromSeconds(2));

    private static byte[] CreateFileBeginPayload(
        DaqFileClass fileClass,
        int recordId,
        ulong fileSize,
        uint maxChunkPayload,
        string name)
    {
        var payload = new byte[32];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), (ushort)fileClass);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), recordId);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(8, 8), fileSize);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), maxChunkPayload);
        System.Text.Encoding.ASCII.GetBytes(name).CopyTo(payload.AsSpan(20));
        return payload;
    }
}
