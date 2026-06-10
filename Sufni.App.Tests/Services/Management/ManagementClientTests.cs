using Sufni.App.Services.Management;

namespace Sufni.App.Tests.Services.Management;

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
}
