using Sufni.App.Acquisition.Services.Management;
namespace Sufni.App.Tests.Acquisition.Services.Management;

// Loopback TCP tests with real timeouts: serialized so parallel CPU/socket
// contention does not turn the timing budgets into flakes.
[CollectionDefinition("ManagementLoopback", DisableParallelization = true)]
public class ManagementLoopbackCollectionDefinition;

[Collection("ManagementLoopback")]
public class ManagementClientTests
{
    [Theory]
    [InlineData(ManagementClientValidationCase.PingWhenDisconnected)]
    [InlineData(ManagementClientValidationCase.NullDownloadDestination)]
    [InlineData(ManagementClientValidationCase.ReadOnlyDownloadDestination)]
    [InlineData(ManagementClientValidationCase.ConfigDownloadRecordId)]
    [InlineData(ManagementClientValidationCase.NullConfigBytes)]
    public async Task Commands_ValidateConnectionAndArguments(ManagementClientValidationCase testCase)
    {
        using var client = new ManagementClient();

        switch (testCase)
        {
            case ManagementClientValidationCase.PingWhenDisconnected:
                await Assert.ThrowsAsync<InvalidOperationException>(() => client.PingAsync());
                break;
            case ManagementClientValidationCase.NullDownloadDestination:
                await Assert.ThrowsAsync<ArgumentNullException>(() =>
                    client.GetFileAsync(DaqFileClass.RootSst, recordId: 1, destination: null!));
                break;
            case ManagementClientValidationCase.ReadOnlyDownloadDestination:
                using (var destination = new MemoryStream(new byte[1], writable: false))
                {
                    await Assert.ThrowsAsync<ArgumentException>(() =>
                        client.GetFileAsync(DaqFileClass.RootSst, recordId: 1, destination));
                }
                break;
            case ManagementClientValidationCase.ConfigDownloadRecordId:
                using (var destination = new MemoryStream())
                {
                    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                        client.GetFileAsync(DaqFileClass.Config, recordId: 1, destination));
                }
                break;
            case ManagementClientValidationCase.NullConfigBytes:
                await Assert.ThrowsAsync<ArgumentNullException>(() =>
                    client.ReplaceConfigAsync(configBytes: null!));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(testCase), testCase, null);
        }
    }

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
    public async Task PingAsync_Throws_WhenServerDisconnectsMidFrame()
    {
        await using var server = new ManagementTestServer();
        var serverTask = server.RunSessionAsync(async stream =>
        {
            var request = await ManagementTestServer.ReadFrameAsync(stream);
            var pong = ManagementProtocolReader.CreateFrame(ManagementFrameType.Pong, request.Header.RequestId, []);
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

    public enum ManagementClientValidationCase
    {
        PingWhenDisconnected,
        NullDownloadDestination,
        ReadOnlyDownloadDestination,
        ConfigDownloadRecordId,
        NullConfigBytes
    }
}
