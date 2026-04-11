using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using NSubstitute;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.Services;

public class LiveDaqCatalogServiceTests
{
    private readonly IServiceDiscovery serviceDiscovery = Substitute.For<IServiceDiscovery>();
    private readonly ILiveDaqBoardIdProbe boardIdProbe = Substitute.For<ILiveDaqBoardIdProbe>();

    private LiveDaqBrowseOwner CreateBrowseOwner() => new(serviceDiscovery);

    private LiveDaqCatalogService CreateCatalogService(ILiveDaqBrowseOwner? browseOwner = null) =>
        new(serviceDiscovery, browseOwner ?? CreateBrowseOwner(), boardIdProbe);

    [Fact]
    public void AcquireBrowse_StartsUnderlyingBrowseOnFirstLease_AndStopsOnLastLease()
    {
        var owner = CreateBrowseOwner();

        var lease1 = owner.AcquireBrowse();
        var lease2 = owner.AcquireBrowse();

        serviceDiscovery.Received(1).StartBrowse("_gosst._tcp");

        lease1.Dispose();
        serviceDiscovery.DidNotReceive().StopBrowse();

        lease2.Dispose();
        serviceDiscovery.Received(1).StopBrowse();
    }

    [Fact]
    public async Task Observe_AddsBoardIdEnrichedEntry_WhenServiceAdded()
    {
        var boardId = Guid.NewGuid();
        boardIdProbe.ProbeAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Guid?>(boardId));

        using var service = CreateCatalogService();
        var updateTask = WaitForEntriesAsync(service.Observe(), entries => entries.Count == 1);

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(MakeAnnouncement(IPAddress.Parse("192.168.1.10"), 4567)));

        var entries = await updateTask;
        var entry = Assert.Single(entries);
        Assert.Equal(boardId.ToString(), entry.IdentityKey);
        Assert.Equal(boardId.ToString(), entry.DisplayName);
        Assert.Equal(boardId.ToString(), entry.BoardId);
        Assert.Equal("192.168.1.10", entry.Host);
        Assert.Equal(4567, entry.Port);
        Assert.Equal("192.168.1.10:4567", entry.Endpoint);
    }

    [Fact]
    public async Task Observe_FallsBackToEndpointIdentity_WhenBoardProbeFails()
    {
        boardIdProbe.ProbeAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Guid?>(new InvalidOperationException("probe failed")));

        using var service = CreateCatalogService();
        var updateTask = WaitForEntriesAsync(service.Observe(), entries => entries.Count == 1);

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(MakeAnnouncement(IPAddress.Parse("192.168.1.11"), 6789)));

        var entry = Assert.Single(await updateTask);
        Assert.Null(entry.BoardId);
        Assert.Equal("192.168.1.11:6789", entry.IdentityKey);
        Assert.Equal("192.168.1.11:6789", entry.DisplayName);
    }

    [Fact]
    public async Task Observe_RemovesEntry_WhenServiceRemoved()
    {
        var boardId = Guid.NewGuid();
        boardIdProbe.ProbeAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Guid?>(boardId));

        using var service = CreateCatalogService();
        var added = WaitForEntriesAsync(service.Observe(), entries => entries.Count == 1);
        var announcement = MakeAnnouncement(IPAddress.Parse("192.168.1.12"), 9001);

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(announcement));
        await added;

        var removed = WaitForEntriesAsync(service.Observe(), entries => entries.Count == 0);
        serviceDiscovery.ServiceRemoved += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(announcement));

        Assert.Empty(await removed);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsBoardId_FromDirectoryListing()
    {
        var boardBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var expectedBoardId = UuidUtil.CreateDeviceUuid(boardBytes);
        var directoryListing = CreateDirectoryListing(boardBytes);

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = ServeDirectoryListingAsync(listener, directoryListing);
        var probe = new LiveDaqBoardIdProbe(new InlineBackgroundTaskRunner());

        var boardId = await probe.ProbeAsync(IPAddress.Loopback, port);

        Assert.Equal(expectedBoardId, boardId);
        await serverTask;
    }

    private static ServiceAnnouncement MakeAnnouncement(IPAddress address, ushort port)
    {
        var announcement = new ServiceAnnouncement();
        typeof(ServiceAnnouncement)
            .GetProperty(nameof(ServiceAnnouncement.Address), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(announcement, address);
        typeof(ServiceAnnouncement)
            .GetProperty(nameof(ServiceAnnouncement.Port), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(announcement, port);
        return announcement;
    }

    private static async Task<IReadOnlyList<LiveDaqCatalogEntry>> WaitForEntriesAsync(
        IObservable<IReadOnlyList<LiveDaqCatalogEntry>> source,
        Func<IReadOnlyList<LiveDaqCatalogEntry>, bool> predicate)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<LiveDaqCatalogEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable? subscription = null;
        subscription = source.Subscribe(entries =>
        {
            if (!predicate(entries))
            {
                return;
            }

            subscription?.Dispose();
            tcs.TrySetResult(entries);
        });

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static byte[] CreateDirectoryListing(byte[] boardBytes)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);
        writer.Write(boardBytes);
        writer.Write((ushort)1000);
        writer.Flush();
        return ms.ToArray();
    }

    private static async Task ServeDirectoryListingAsync(TcpListener listener, byte[] directoryListing)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();

            var request = await ReadExactAsync(stream, 8);
            Assert.Equal(new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, request);

            var sizeBuffer = new byte[8];
            BitConverter.GetBytes(directoryListing.Length).CopyTo(sizeBuffer, 0);
            await stream.WriteAsync(sizeBuffer);

            var headerOk = await ReadExactAsync(stream, 4);
            Assert.Equal(new byte[] { 0x04, 0x00, 0x00, 0x00 }, headerOk);

            await stream.WriteAsync(directoryListing);

            var received = await ReadExactAsync(stream, 4);
            Assert.Equal(new byte[] { 0x05, 0x00, 0x00, 0x00 }, received);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int length)
    {
        var buffer = new byte[length];
        var totalRead = 0;
        while (totalRead < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, length - totalRead));
            if (read == 0)
            {
                throw new EndOfStreamException($"Expected {length} bytes but stream closed after {totalRead}.");
            }

            totalRead += read;
        }

        return buffer;
    }
}