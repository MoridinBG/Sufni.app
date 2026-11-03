using Sufni.App.LiveDaq.Services.LiveStreaming;

namespace Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

public partial class LiveDaqSharedStreamTests
{
    public enum AcquisitionKind { Streaming, Management, Import, Identify }

    public static IEnumerable<object[]> AcquisitionPairs =>
        from holder in Enum.GetValues<AcquisitionKind>()
        from requester in Enum.GetValues<AcquisitionKind>()
        select new object[] { holder, requester };

    [Theory]
    [MemberData(nameof(AcquisitionPairs))]
    public async Task Acquisitions_ReportExistingHolder_AndOnlyLiveJoinsExistingSocket(
        AcquisitionKind holderKind, AcquisitionKind requester)
    {
        await using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board", "192.168.0.10", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        var stream = registry.GetOrCreate(snapshot);
        await using var observer = Assert.IsType<LiveStreamLeaseResult.Acquired>(stream.AcquireLease()).Lease;
        DaqTransportHolder? holder = null;
        using var observation = registry.ObserveTransport(snapshot.IdentityKey).Subscribe(value => holder = value);
        IDisposable? heldLease = null;
        if (holderKind == AcquisitionKind.Streaming)
            Assert.IsType<LivePreviewStartResult.Started>(await stream.ConnectLiveAsync(TestContext.Current.CancellationToken));
        else
            heldLease = Assert.IsType<DaqTransportAcquisition.Acquired>(await AcquireOtherAsync(registry, snapshot.IdentityKey, holderKind)).Lease;
        using var held = heldLease;
        Assert.NotNull(holder);

        if (requester == AcquisitionKind.Streaming)
        {
            var result = await stream.ConnectLiveAsync(TestContext.Current.CancellationToken);
            if (holderKind == AcquisitionKind.Streaming)
            {
                Assert.Null(result);
                Assert.Equal(1, Assert.Single(clientFactory.CreatedClients).ConnectCalls);
            }
            else
            {
                Assert.Equal(holder, Assert.IsType<LivePreviewStartResult.Busy>(result).Holder);
                Assert.Empty(clientFactory.CreatedClients);
            }
        }
        else
        {
            var result = await AcquireOtherAsync(registry, snapshot.IdentityKey, requester);
            Assert.Equal(holder, Assert.IsType<DaqTransportAcquisition.Busy>(result).Holder);
        }

        held?.Dispose();
        if (holderKind == AcquisitionKind.Streaming) await stream.StopAsync(TestContext.Current.CancellationToken);
        Assert.Null(holder);
        using var available = Assert.IsType<DaqTransportAcquisition.Acquired>(
            await registry.AcquireManagementAsync(snapshot.IdentityKey, DaqManagementOperation.ReadConfig)).Lease;
    }

    [Fact]
    public async Task OpenDisconnectedTab_DoesNotReservePort_AndDisconnectRetainsStreamingUntilSocketCloses()
    {
        await using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board", "192.168.0.10", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        var stream = registry.GetOrCreate(snapshot);
        await using var observer = Assert.IsType<LiveStreamLeaseResult.Acquired>(stream.AcquireLease()).Lease;
        using (Assert.IsType<DaqTransportAcquisition.Acquired>(
                   await registry.AcquireManagementAsync(snapshot.IdentityKey, DaqManagementOperation.SetTime)).Lease) { }
        await stream.ConnectLiveAsync(TestContext.Current.CancellationToken);
        var client = Assert.Single(clientFactory.CreatedClients);
        client.PendingDisconnectCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var stopping = stream.StopAsync(TestContext.Current.CancellationToken);
        await client.DisconnectEntered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.IsType<DaqTransportHolder.Streaming>(Assert.IsType<DaqTransportAcquisition.Busy>(
            await registry.AcquireManagementAsync(snapshot.IdentityKey, DaqManagementOperation.ReadConfig)).Holder);
        client.PendingDisconnectCompletion.SetResult();
        await stopping.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        using var acquired = Assert.IsType<DaqTransportAcquisition.Acquired>(
            await registry.AcquireManagementAsync(snapshot.IdentityKey, DaqManagementOperation.ReadConfig)).Lease;
    }

    [Fact]
    public async Task DisposeAsync_DuringDisconnect_AwaitsSocketCleanupBeforeReleasingPort()
    {
        await using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board", "192.168.0.10", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        var stream = registry.GetOrCreate(snapshot);
        await using var observer = Assert.IsType<LiveStreamLeaseResult.Acquired>(stream.AcquireLease()).Lease;
        await stream.ConnectLiveAsync(TestContext.Current.CancellationToken);
        var client = Assert.Single(clientFactory.CreatedClients);
        client.PendingDisconnectCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var stopping = stream.StopAsync(TestContext.Current.CancellationToken);
        await client.DisconnectEntered.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        var disposing = stream.DisposeAsync().AsTask();
        Assert.False(disposing.IsCompleted);
        Assert.IsType<DaqTransportAcquisition.Busy>(await registry.AcquireIdentifyAsync(snapshot.IdentityKey));
        client.PendingDisconnectCompletion.SetResult();
        await Task.WhenAll(stopping, disposing).AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        using var available = Assert.IsType<DaqTransportAcquisition.Acquired>(
            await registry.AcquireIdentifyAsync(snapshot.IdentityKey)).Lease;
    }

    [Fact]
    public async Task StopAsync_WithCaptureLock_ReturnsLockedAndKeepsTransportUntilCaptureReleasesIt()
    {
        await using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board", "192.168.0.10", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        var stream = registry.GetOrCreate(snapshot);
        await using var observer = Assert.IsType<LiveStreamLeaseResult.Acquired>(stream.AcquireLease()).Lease;
        var capture = Assert.IsType<LiveStreamLeaseResult.Acquired>(stream.AcquireConfigurationLock()).Lease;
        await stream.ConnectLiveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LiveDisconnectResult.Locked, await stream.StopAsync(TestContext.Current.CancellationToken));
        Assert.Empty(Assert.Single(clientFactory.CreatedClients).StopLifecycleCalls);
        Assert.IsType<DaqTransportAcquisition.Busy>(await registry.AcquireIdentifyAsync(snapshot.IdentityKey));
        await capture.DisposeAsync();
        Assert.Equal(LiveDisconnectResult.Stopped, await stream.StopAsync(TestContext.Current.CancellationToken));

        using var available = Assert.IsType<DaqTransportAcquisition.Acquired>(
            await registry.AcquireIdentifyAsync(snapshot.IdentityKey)).Lease;
    }

    [Fact]
    public async Task ClosingStream_KeepsPortReservedUntilDisposalCompletes_ForReplacementTab()
    {
        await using var registry = CreateRegistry();
        var snapshot = CreateSnapshot("board", "192.168.0.10", 1557);
        catalogEntries.OnNext([CreateCatalogEntry(snapshot)]);
        var old = registry.GetOrCreate(snapshot);
        var observer = Assert.IsType<LiveStreamLeaseResult.Acquired>(old.AcquireLease()).Lease;
        await old.ConnectLiveAsync(TestContext.Current.CancellationToken);
        var client = Assert.Single(clientFactory.CreatedClients);
        client.BlockDisposeAsync = true;

        var closing = observer.DisposeAsync().AsTask();
        await client.DisposeStarted.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        var replacement = registry.GetOrCreate(snapshot);
        await using var nextObserver = Assert.IsType<LiveStreamLeaseResult.Acquired>(replacement.AcquireLease()).Lease;
        Assert.IsType<LivePreviewStartResult.Busy>(await replacement.ConnectLiveAsync(TestContext.Current.CancellationToken));
        Assert.Single(clientFactory.CreatedClients);
        client.ReleaseDispose();
        await closing.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.IsType<LivePreviewStartResult.Started>(await replacement.ConnectLiveAsync(TestContext.Current.CancellationToken));
    }

    private static Task<DaqTransportAcquisition> AcquireOtherAsync(
        ILiveDaqSharedStreamRegistry registry, string identityKey, AcquisitionKind kind) => kind switch
    {
        AcquisitionKind.Management => registry.AcquireManagementAsync(identityKey, DaqManagementOperation.ReadConfig),
        AcquisitionKind.Import => registry.AcquireImportAsync(identityKey, Guid.NewGuid()),
        AcquisitionKind.Identify => registry.AcquireIdentifyAsync(identityKey),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
