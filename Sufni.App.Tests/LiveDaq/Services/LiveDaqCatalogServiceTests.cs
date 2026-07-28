using System.Net;
using System.Net.Sockets;
#if SUFNI_PROFILING_DIAGNOSTICS
using System.Reactive.Subjects;
#endif
using NSubstitute;
using Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Shared.Common;
#if SUFNI_PROFILING_DIAGNOSTICS
using Sufni.Profiling;
#endif
namespace Sufni.App.Tests.LiveDaq.Services;

#if SUFNI_PROFILING_DIAGNOSTICS
[Collection("ProfilingRuntime")]
#endif
public class LiveDaqCatalogServiceTests
{
    private readonly IServiceDiscovery serviceDiscovery = Substitute.For<IServiceDiscovery>();

    private DaqBrowseOwner CreateBrowseOwner() => new(serviceDiscovery);

    private LiveDaqCatalogService CreateCatalogService(IDaqBrowseOwner? browseOwner = null) =>
        new(serviceDiscovery, browseOwner ?? CreateBrowseOwner());

#if SUFNI_PROFILING_DIAGNOSTICS
    [Fact]
    public void ProfilingCatalog_AppendsExactlyOneReplayEntry_WhenLiveLongIsActive()
    {
        ProfilingRuntime.Shutdown();
        ProfilingRuntime.Initialize(new ProfilingOptions(
            ProfilingMode.EventPipe,
            RunId: "catalog-test",
            OutputPath: null,
            Corpus: ProfilingLiveDaqReplay.Corpus,
            AppDataPath: "catalog-test-app-data"));
        try
        {
            var ordinary = new LiveDaqCatalogEntry(
                IdentityKey: "ordinary",
                DisplayName: "Ordinary DAQ",
                BoardId: null,
                Host: "192.168.1.20",
                Port: 1557,
                ProtocolVersion: LiveProtocolVersion.V3);
            var duplicate = ProfilingLiveDaqReplay.CatalogEntry with
            {
                DisplayName = "stale replay",
                Host = "192.168.1.21",
            };
            using var entries = new BehaviorSubject<IReadOnlyList<LiveDaqCatalogEntry>>(
                [ordinary, duplicate]);
            var inner = Substitute.For<ILiveDaqCatalogService>();
            inner.Observe().Returns(entries);
            var service = new ProfilingLiveDaqCatalogService(inner);
            IReadOnlyList<LiveDaqCatalogEntry>? observed = null;

            using var subscription = service.Observe().Subscribe(value => observed = value);

            Assert.NotNull(observed);
            Assert.Equal(2, observed.Count);
            Assert.Contains(ordinary, observed);
            Assert.Equal(
                ProfilingLiveDaqReplay.CatalogEntry,
                Assert.Single(observed, entry => ProfilingLiveDaqReplay.Matches(entry.IdentityKey)));
        }
        finally
        {
            ProfilingRuntime.Shutdown();
        }
    }
#endif

    [Fact]
    public void AcquireBrowse_StartsUnderlyingBrowseOnFirstLease_AndStopsOnLastLease()
    {
        var owner = CreateBrowseOwner();

        var lease1 = owner.AcquireBrowse();
        var lease2 = owner.AcquireBrowse();

        serviceDiscovery.Received(1).StartBrowse("_sufni._tcp");

        lease1.Dispose();
        serviceDiscovery.DidNotReceive().StopBrowse();

        lease2.Dispose();
        serviceDiscovery.Received(1).StopBrowse();
    }

    [Fact]
    public void AcquireBrowse_WhenFirstStartThrows_RunsCompensatingStop_AndRetryStartsCleanly()
    {
        var failingDiscovery = Substitute.For<IServiceDiscovery>();
        var shouldThrowOnStart = true;
        failingDiscovery
            .When(d => d.StartBrowse("_sufni._tcp"))
            .Do(_ =>
            {
                if (shouldThrowOnStart)
                {
                    throw new InvalidOperationException("boom");
                }
            });

        var owner = new DaqBrowseOwner(failingDiscovery);

        Assert.Throws<InvalidOperationException>(() => owner.AcquireBrowse());
        failingDiscovery.Received(1).StartBrowse("_sufni._tcp");
        failingDiscovery.Received(1).StopBrowse();

        shouldThrowOnStart = false;
        failingDiscovery.ClearReceivedCalls();

        var lease = owner.AcquireBrowse();
        failingDiscovery.Received(1).StartBrowse("_sufni._tcp");

        lease.Dispose();
        failingDiscovery.Received(1).StopBrowse();
    }

    [Fact]
    public async Task Observe_AddsBidEnrichedEntry_WhenServiceAdded()
    {
        var expectedBoardId = UuidUtil.CreateDeviceUuid("0102030405060708").ToString();

        using var service = CreateCatalogService();
        var updateTask = WaitForEntriesAsync(service.Observe(), entries => entries.Count == 1);

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(CreateAnnouncement(
                "192.168.1.10",
                4567,
                LiveProtocolVersion.V3,
                bid: "0102030405060708")));

        var entries = await updateTask;
        var entry = Assert.Single(entries);
        Assert.Equal(expectedBoardId, entry.IdentityKey);
        Assert.Equal(expectedBoardId, entry.DisplayName);
        Assert.Equal(expectedBoardId, entry.BoardId);
        Assert.Equal("192.168.1.10", entry.Host);
        Assert.Equal(4567, entry.Port);
        Assert.Equal("192.168.1.10:4567", entry.Endpoint);
        Assert.Equal(LiveProtocolVersion.V3, entry.ProtocolVersion);
    }

    [Fact]
    public async Task Observe_FallsBackToProtocolQualifiedEndpointIdentity_WhenBidIsAbsent()
    {
        using var service = CreateCatalogService();
        var updateTask = WaitForEntriesAsync(service.Observe(), entries => entries.Count == 1);

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(CreateAnnouncement("192.168.1.11", 6789, LiveProtocolVersion.V3)));

        var entry = Assert.Single(await updateTask);
        Assert.Null(entry.BoardId);
        Assert.Equal("v3:192.168.1.11:6789", entry.IdentityKey);
        Assert.Equal("v3:192.168.1.11:6789", entry.DisplayName);
        Assert.Equal(LiveProtocolVersion.V3, entry.ProtocolVersion);
    }

    [Fact]
    public async Task Observe_RemovesEntry_WhenServiceRemoved()
    {
        using var service = CreateCatalogService();
        var added = WaitForEntriesAsync(service.Observe(), entries => entries.Count == 1);
        var announcement = CreateAnnouncement("192.168.1.12", 9001, LiveProtocolVersion.V2);

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Observe_DuplicateBoardIdentity_SelectsLowestAnnouncementKey_RegardlessOfArrivalOrder(
        bool preferredArrivesFirst)
    {
        const string bid = "0102030405060708";
        var preferred = CreateAnnouncement(
            "192.168.1.20",
            9002,
            LiveProtocolVersion.V3,
            bid,
            instanceName: "daq-a");
        var alternate = CreateAnnouncement(
            "192.168.1.21",
            9003,
            LiveProtocolVersion.V3,
            bid,
            instanceName: "daq-b");
        using var service = CreateCatalogService();
        IReadOnlyList<LiveDaqCatalogEntry> observed = [];
        using var subscription = service.Observe().Subscribe(entries => observed = entries);

        var announcements = preferredArrivesFirst
            ? new[] { preferred, alternate }
            : new[] { alternate, preferred };
        foreach (var announcement in announcements)
        {
            serviceDiscovery.ServiceAdded += Raise.EventWith(
                serviceDiscovery,
                new ServiceAnnouncementEventArgs(announcement));
        }

        var entry = Assert.Single(observed);
        Assert.Equal("192.168.1.20", entry.Host);
        Assert.Equal(9002, entry.Port);
    }

    [Fact]
    public void Observe_PreferredDuplicateRemoval_PromotesRetainedAlternate()
    {
        const string bid = "0102030405060708";
        var preferred = CreateAnnouncement(
            "192.168.1.20",
            9002,
            LiveProtocolVersion.V3,
            bid,
            instanceName: "daq-a");
        var alternate = CreateAnnouncement(
            "192.168.1.21",
            9003,
            LiveProtocolVersion.V3,
            bid,
            instanceName: "daq-b");
        using var service = CreateCatalogService();
        IReadOnlyList<LiveDaqCatalogEntry> observed = [];
        using var subscription = service.Observe().Subscribe(entries => observed = entries);
        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(preferred));
        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(alternate));
        var identityKey = Assert.Single(observed).IdentityKey;

        serviceDiscovery.ServiceRemoved += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(preferred));

        var promoted = Assert.Single(observed);
        Assert.Equal(identityKey, promoted.IdentityKey);
        Assert.Equal("192.168.1.21", promoted.Host);
        Assert.Equal(9003, promoted.Port);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("4")]
    public void Observe_IgnoresAnnouncementsWithMissingOrUnsupportedProtocol(string? liveProto)
    {
        using var service = CreateCatalogService();
        var observed = new List<IReadOnlyList<LiveDaqCatalogEntry>>();
        using var subscription = service.Observe().Subscribe(observed.Add);

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(CreateAnnouncement(
                "192.168.1.13",
                9002,
                liveProto)));

        Assert.Empty(observed[^1]);
    }

    [Fact]
    public async Task Observe_ReannouncementWithDifferentProtocol_UpdatesExistingInstance()
    {
        using var service = CreateCatalogService();
        var added = WaitForEntriesAsync(service.Observe(), entries => entries.Count == 1);
        var v2 = CreateAnnouncement("192.168.1.14", 9003, LiveProtocolVersion.V2, instanceName: "daq-1");

        serviceDiscovery.ServiceAdded += Raise.EventWith(serviceDiscovery, new ServiceAnnouncementEventArgs(v2));
        await added;

        var updated = WaitForEntriesAsync(service.Observe(), entries =>
            entries.Count == 1 &&
            entries[0].ProtocolVersion == LiveProtocolVersion.V3);
        var v3 = CreateAnnouncement("192.168.1.14", 9003, LiveProtocolVersion.V3, instanceName: "daq-1");

        serviceDiscovery.ServiceAdded += Raise.EventWith(serviceDiscovery, new ServiceAnnouncementEventArgs(v3));

        var entry = Assert.Single(await updated);
        Assert.Equal(LiveProtocolVersion.V3, entry.ProtocolVersion);
        Assert.Equal("v3:192.168.1.14:9003", entry.IdentityKey);
    }

    [Fact]
    public async Task InspectAsync_TimesOut_WhenServerNeverRespondsWithAck()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var acceptTask = listener.AcceptTcpClientAsync(cancellationToken: TestContext.Current.CancellationToken);
        var inspector = new LiveDaqBoardIdInspector(
            new InlineBackgroundTaskRunner(),
            TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => inspector.InspectAsync(IPAddress.Loopback, port, cancellationToken: TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(2)));

        using var accepted = await acceptTask.AsTask().AwaitBoundedAsync(TimeSpan.FromSeconds(2));
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

        var boardId = await inspector.InspectAsync(IPAddress.Loopback, port, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(expectedBoardId, boardId);
        await serverTask;
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

        return await tcs.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
    }

    private static ServiceAnnouncement CreateAnnouncement(
        string host,
        ushort port,
        LiveProtocolVersion protocolVersion,
        string? bid = null,
        string? instanceName = null) =>
        CreateAnnouncement(
            host,
            port,
            protocolVersion switch
            {
                LiveProtocolVersion.V2 => "2",
                LiveProtocolVersion.V3 => "3",
                _ => null,
            },
            bid,
            instanceName);

    private static ServiceAnnouncement CreateAnnouncement(
        string host,
        ushort port,
        string? liveProto,
        string? bid = null,
        string? instanceName = null)
    {
        var txtRecords = new Dictionary<string, string>(StringComparer.Ordinal);
        if (liveProto is not null)
        {
            txtRecords.Add("live_proto", liveProto);
        }

        if (bid is not null)
        {
            txtRecords.Add("bid", bid);
        }

        return new ServiceAnnouncement(IPAddress.Parse(host), port, instanceName, txtRecords);
    }

    private static async Task ServeIdentifyAckAsync(TcpListener listener, byte[] boardSerial)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();

            // Read the Identify frame (16-byte header, no payload)
            var requestBuffer = new byte[LiveV2ProtocolConstants.FrameHeaderSize];
            var totalRead = 0;
            while (totalRead < requestBuffer.Length)
            {
                var read = await stream.ReadAsync(requestBuffer.AsMemory(totalRead));
                if (read == 0) throw new EndOfStreamException();
                totalRead += read;
            }

            var header = LiveV2ProtocolReader.ParseHeader(requestBuffer);
            Assert.Equal(LiveV2FrameType.Identify, header.FrameType);
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
