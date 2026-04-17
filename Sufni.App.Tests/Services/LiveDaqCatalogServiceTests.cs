using System.Net;
using System.Net.Sockets;
using System.Reflection;
using NSubstitute;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Tests.Services.LiveStreaming;

namespace Sufni.App.Tests.Services;

public class LiveDaqCatalogServiceTests
{
    private readonly IServiceDiscovery serviceDiscovery = Substitute.For<IServiceDiscovery>();
    private readonly ILiveDaqBoardIdInspector boardIdInspector = Substitute.For<ILiveDaqBoardIdInspector>();

    private LiveDaqBrowseOwner CreateBrowseOwner() => new(serviceDiscovery);

    private LiveDaqCatalogService CreateCatalogService(ILiveDaqBrowseOwner? browseOwner = null) =>
        new(serviceDiscovery, browseOwner ?? CreateBrowseOwner(), boardIdInspector);

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
        boardIdInspector.InspectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
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
    public async Task Observe_FallsBackToEndpointIdentity_WhenBoardInspectionFails()
    {
        boardIdInspector.InspectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Guid?>(new InvalidOperationException("inspection failed")));

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
        boardIdInspector.InspectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
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
    public async Task InspectAsync_ReturnsBoardId_FromIdentifyAck()
    {
        var boardSerial = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var expectedBoardId = UuidUtil.CreateDeviceUuid(boardSerial);

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = ServeIdentifyAckAsync(listener, boardSerial);
        var inspector = new LiveDaqBoardIdInspector(new InlineBackgroundTaskRunner());

        var boardId = await inspector.InspectAsync(IPAddress.Loopback, port);

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

    private static async Task ServeIdentifyAckAsync(TcpListener listener, byte[] boardSerial)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();

            // Read the Identify frame (16-byte header, no payload)
            var requestBuffer = new byte[LiveProtocolConstants.FrameHeaderSize];
            var totalRead = 0;
            while (totalRead < requestBuffer.Length)
            {
                var read = await stream.ReadAsync(requestBuffer.AsMemory(totalRead));
                if (read == 0) throw new EndOfStreamException();
                totalRead += read;
            }

            var header = LiveProtocolReader.ParseHeader(requestBuffer);
            Assert.Equal(LiveFrameType.Identify, header.FrameType);
            Assert.Equal((uint)0, header.PayloadLength);

            // Respond with IdentifyAck
            var response = LiveProtocolTestFrames.CreateIdentifyAckFrame(sequence: 1, boardSerial: boardSerial);
            await stream.WriteAsync(response);
        }
        finally
        {
            listener.Stop();
        }
    }
}