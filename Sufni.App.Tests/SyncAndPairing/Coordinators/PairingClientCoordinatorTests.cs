using System.Net;
using System.Net.Http;
using System.Reactive.Subjects;
using System.Reactive.Linq;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

using Sufni.App.Infrastructure;
using Sufni.App.Shared.Models;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Models;
namespace Sufni.App.Tests.SyncAndPairing.Coordinators;

public class PairingClientCoordinatorTests : IDisposable
{
    private const string DisplayNameKey = "DisplayName";

    private readonly ISynchronizationPeer peer = Substitute.For<ISynchronizationPeer>();
    private readonly ILocalDeviceIdentity localDeviceIdentity = Substitute.For<ILocalDeviceIdentity>();
    private readonly ISecureStorage secureStorage = Substitute.For<ISecureStorage>();
    private readonly IHttpApiService httpApiService = Substitute.For<IHttpApiService>();
    private readonly IServiceDiscovery serviceDiscovery = Substitute.For<IServiceDiscovery>();
    private readonly IFriendlyNameProvider friendlyNameProvider = Substitute.For<IFriendlyNameProvider>();
    private readonly BehaviorSubject<ISynchronizationPeer?> peerChanges = new(null);
    private readonly Microsoft.Extensions.Time.Testing.FakeTimeProvider time = new();

    public PairingClientCoordinatorTests()
    {
        httpApiService.GetPeerAsync(Arg.Any<PeerEndpoint>()).Returns(peer);
        httpApiService.PeerChanges.Returns(peerChanges);
        httpApiService.PairedPeer.Returns(_ => peerChanges.Value);
        httpApiService.ConfirmPairingAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>())
            .Returns(_ =>
            {
                peerChanges.OnNext(peer);
                return Task.CompletedTask;
            });
        httpApiService.UnpairAsync(Arg.Any<string>())
            .Returns(_ =>
            {
                peerChanges.OnNext(null);
                return Task.CompletedTask;
            });
    }

    private PairingClientCoordinator CreateCoordinator() => new(
        secureStorage, httpApiService, serviceDiscovery, friendlyNameProvider, time, localDeviceIdentity);

    /// <summary>
    /// Seeds the secure-storage / http substitutes with defaults
    /// sufficient to let `Init()` run to completion without throwing.
    /// Individual tests can override any of these before constructing
    /// the coordinator.
    /// </summary>
    private void SeedInitDefaults(
        string? existingDeviceId = "existing-device-id",
        string? existingDisplayName = null,
        string? friendlyName = null,
        bool isPaired = false)
    {
        localDeviceIdentity.GetIdAsync().Returns(existingDeviceId!);
        secureStorage.GetStringAsync(DisplayNameKey).Returns(Task.FromResult(existingDisplayName));
        friendlyNameProvider.FriendlyName.Returns(friendlyName!);
        peerChanges.OnNext(isPaired ? peer : null);
    }

    // ----- Constructor startup -----

    [Fact]
    public async Task InitializeAsync_LoadsExistingDeviceId_AndRaisesDeviceIdChanged()
    {
        var deviceIdGate = new TaskCompletionSource<string>();
        localDeviceIdentity.GetIdAsync().Returns(deviceIdGate.Task);
        secureStorage.GetStringAsync(DisplayNameKey).Returns(Task.FromResult<string?>(null));
        friendlyNameProvider.FriendlyName.Returns((string)null!);

        using var coordinator = CreateCoordinator();
        var eventRaised = false;
        coordinator.DeviceIdChanged += (_, _) => eventRaised = true;

        deviceIdGate.SetResult("stored-id");
        await coordinator.InitializeAsync();

        Assert.Equal("stored-id", coordinator.DeviceId);
        Assert.True(eventRaised);
    }

    [Theory]
    [InlineData("  stored name  ", "friendly name", "stored name", false)]
    [InlineData(null, "  friendly name  ", "friendly name", true)]
    [InlineData(null, null, null, true)]
    [InlineData("   ", "friendly name", "friendly name", true)]
    public async Task InitializeAsync_ResolvesDisplayName(
        string? existingDisplayName,
        string? friendlyName,
        string? expectedDisplayName,
        bool expectsFriendlyNameRead)
    {
        SeedInitDefaults(
            existingDisplayName: existingDisplayName,
            friendlyName: friendlyName);

        using var coordinator = CreateCoordinator();
        await coordinator.InitializeAsync();

        Assert.Equal(expectedDisplayName, coordinator.DisplayName);
        if (!expectsFriendlyNameRead)
        {
            _ = friendlyNameProvider.DidNotReceive().FriendlyName;
        }
    }

    [Fact]
    public async Task InitializeAsync_DerivesPairingFromLoadedPeer()
    {
        var deviceIdGate = new TaskCompletionSource<string>();
        localDeviceIdentity.GetIdAsync().Returns(deviceIdGate.Task);
        secureStorage.GetStringAsync(DisplayNameKey).Returns(Task.FromResult<string?>(null));
        friendlyNameProvider.FriendlyName.Returns((string)null!);
        peerChanges.OnNext(peer);

        using var coordinator = CreateCoordinator();

        deviceIdGate.SetResult("device-123");
        await coordinator.InitializeAsync();

        Assert.Equal(PairingClientState.Paired, coordinator.State);
        await httpApiService.Received(1).InitializeAsync();
    }

    [Fact]
    public async Task ResolvePeerAsync_EndsTemporaryBrowseWhenDeadlineExpires()
    {
        SeedInitDefaults(isPaired: true);
        using var coordinator = CreateCoordinator();
        await coordinator.InitializeAsync();

        var resolving = coordinator.ResolvePeerAsync(TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromSeconds(4));
        Assert.False(resolving.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(1));

        Assert.Null(await resolving.AwaitBoundedAsync(TimeSpan.FromSeconds(2)));
        serviceDiscovery.Received(1).StopBrowse();
    }

    [Fact]
    public async Task ResolvePeerAsync_ReturnsCurrentDiscoveryUrl_AndUpdatesHttpEndpoint()
    {
        SeedInitDefaults(isPaired: true);
        using var coordinator = CreateCoordinator();
        await coordinator.InitializeAsync();
        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Parse("192.168.1.10"), 8443)));

        var resolvedUrl = await coordinator.ResolvePeerAsync(TimeSpan.FromSeconds(1));

        Assert.Same(peer, resolvedUrl);
        await httpApiService.Received(1).GetPeerAsync(new PeerEndpoint("https://192.168.1.10:8443"));
        serviceDiscovery.DidNotReceive().StartBrowse(Arg.Any<string>());
    }

    [Fact]
    public async Task ResolvePeerAsync_StartsTemporaryBrowseUntilServerIsDiscovered()
    {
        SeedInitDefaults(isPaired: true);
        using var coordinator = CreateCoordinator();
        await coordinator.InitializeAsync();

        var resolving = coordinator.ResolvePeerAsync(TimeSpan.FromSeconds(1));
        serviceDiscovery.Received(1).StartBrowse(SynchronizationProtocol.ServiceType);

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Parse("192.168.1.10"), 8443)));

        var resolvedUrl = await resolving;

        Assert.Same(peer, resolvedUrl);
        await httpApiService.Received(1).GetPeerAsync(new PeerEndpoint("https://192.168.1.10:8443"));
        serviceDiscovery.Received(1).StopBrowse();
    }

    [Fact]
    public async Task ResolvePeerAsync_DoesNotStopExistingBrowseLease()
    {
        SeedInitDefaults(isPaired: true);
        using var coordinator = CreateCoordinator();
        await coordinator.InitializeAsync();
        coordinator.StartBrowsing();

        var resolving = coordinator.ResolvePeerAsync(TimeSpan.FromSeconds(1));

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Parse("192.168.1.10"), 8443)));
        _ = await resolving;

        serviceDiscovery.Received(1).StartBrowse(SynchronizationProtocol.ServiceType);
        serviceDiscovery.DidNotReceive().StopBrowse();

        coordinator.StopBrowsing();
        serviceDiscovery.Received(1).StopBrowse();
    }

    // ----- ServiceAdded / ServiceRemoved -----

    [Theory]
    [InlineData("192.168.1.10", 8443, "https://192.168.1.10:8443", true)]
    [InlineData("::ffff:192.0.2.1", 9000, "https://192.0.2.1:9000", true)]
    [InlineData("2001:db8::1", 4321, "https://[2001:db8::1]:4321", true)]
    [InlineData("fe80::1489:542b:5856:9669", 5575, null, false)]
    public async Task ServiceAdded_FormsExpectedServerUrl(
        string address,
        int port,
        string? expectedServerUrl,
        bool expectedUrlChanged)
    {
        SeedInitDefaults();
        using var coordinator = CreateCoordinator();
        await coordinator.InitializeAsync();

        var urlChanged = false;
        coordinator.ServerUrlChanged += (_, _) => urlChanged = true;

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Parse(address), (ushort)port)));

        Assert.Equal(expectedServerUrl, coordinator.ServerUrl);
        Assert.Equal(expectedUrlChanged, urlChanged);
    }

    [Fact]
    public async Task ServiceAdded_LeavesCurrentServerUrl_WhenIPv6LinkLocalAddressArrives()
    {
        SeedInitDefaults();
        using var coordinator = CreateCoordinator();
        await coordinator.InitializeAsync();

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Parse("192.168.1.10"), 5575)));
        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Parse("fe80::1489:542b:5856:9669"), 5575)));

        Assert.Equal("https://192.168.1.10:5575", coordinator.ServerUrl);
    }

    [Fact]
    public async Task ServiceRemoved_ClearsServerUrl_AndRaisesServerUrlChanged()
    {
        SeedInitDefaults();
        using var coordinator = CreateCoordinator();
        await coordinator.InitializeAsync();

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Parse("10.0.0.1"), 1234)));
        Assert.NotNull(coordinator.ServerUrl);

        var urlChanged = false;
        coordinator.ServerUrlChanged += (_, _) => urlChanged = true;

        serviceDiscovery.ServiceRemoved += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Parse("10.0.0.1"), 1234)));

        Assert.Null(coordinator.ServerUrl);
        Assert.True(urlChanged);
    }

    [Fact]
    public async Task ServiceRemoved_LeavesActiveServerUrl_WhenDifferentServiceIsRemoved()
    {
        SeedInitDefaults();
        using var coordinator = CreateCoordinator();
        await coordinator.InitializeAsync();

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Parse("10.0.0.1"), 1234)));
        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Parse("10.0.0.2"), 2345)));

        Assert.Equal("https://10.0.0.2:2345", coordinator.ServerUrl);

        serviceDiscovery.ServiceRemoved += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Parse("10.0.0.1"), 1234)));

        Assert.Equal("https://10.0.0.2:2345", coordinator.ServerUrl);
    }

    [Fact]
    public async Task ServiceRemoved_FallsBackToPreviousServer_WhenActiveServiceIsRemoved()
    {
        SeedInitDefaults();
        using var coordinator = CreateCoordinator();
        await coordinator.InitializeAsync();

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Parse("10.0.0.1"), 1234)));
        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Parse("10.0.0.2"), 2345)));

        serviceDiscovery.ServiceRemoved += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Parse("10.0.0.2"), 2345)));

        Assert.Equal("https://10.0.0.1:1234", coordinator.ServerUrl);
    }

    // ----- RequestPairingAsync -----

    [Fact]
    public async Task RequestPairingAsync_ReturnsUnavailable_AndDoesNotTouchHttp_WhenServerUrlIsNull()
    {
        SeedInitDefaults();
        using var coordinator = CreateCoordinator();

        var result = await coordinator.RequestPairingAsync("label");

        Assert.IsType<RequestPairingResult.ServerUnavailable>(result);
        await httpApiService.DidNotReceiveWithAnyArgs().RequestPairingAsync(default!, default!, default);
    }

    [Fact]
    public async Task RequestPairingAsync_PassesNormalizedDisplayNameToHttp_AndReturnsSent()
    {
        SeedInitDefaults(existingDeviceId: "device-123");
        using var coordinator = CreateCoordinator();
        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Parse("192.168.1.10"), 8443)));

        var result = await coordinator.RequestPairingAsync("  My Phone  ");

        Assert.IsType<RequestPairingResult.Sent>(result);
        Assert.Equal(PairingClientState.AwaitingPin, coordinator.State);
        await httpApiService.Received(1).RequestPairingAsync(
            new PeerEndpoint("https://192.168.1.10:8443"),
            "device-123",
            "My Phone");
    }

    [Fact]
    public async Task RequestPairingAsync_MapsThrownExceptionsToFailed()
    {
        SeedInitDefaults();
        using var coordinator = CreateCoordinator();
        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(new ServiceAnnouncement(IPAddress.Parse("192.168.1.10"), 8443)));
        httpApiService.RequestPairingAsync(Arg.Any<PeerEndpoint>(), Arg.Any<string>(), Arg.Any<string?>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await coordinator.RequestPairingAsync("label");

        Assert.Equal(PairingFailureReason.Request, Assert.IsType<RequestPairingResult.Failed>(result).Reason);
    }

    // ----- ConfirmPairingAsync -----

    [Fact]
    public async Task ConfirmPairingAsync_HappyPath_FromFallbackName_RaisesDisplayNameChanged()
    {
        SeedInitDefaults(
            existingDeviceId: "device-123",
            existingDisplayName: null,
            friendlyName: "phone",
            isPaired: false);
        using var coordinator = CreateCoordinator();
        await coordinator.InitializeAsync();
        Assert.Equal("phone", coordinator.DisplayName);

        var displayNameChanged = 0;
        var isPairedChanged = 0;
        coordinator.DisplayNameChanged += (_, _) => displayNameChanged++;
        using var observation = coordinator.States.Skip(1).Subscribe(_ => isPairedChanged++);

        var result = await coordinator.ConfirmPairingAsync("  new name  ", "1234");

        Assert.IsType<ConfirmPairingResult.Paired>(result);
        await httpApiService.Received(1).ConfirmPairingAsync("device-123", "new name", "1234");
        Assert.Equal("new name", coordinator.DisplayName);
        Assert.Equal(PairingClientState.Paired, coordinator.State);
        Assert.Equal(1, displayNameChanged);
        Assert.Equal(1, isPairedChanged);
        await secureStorage.Received(1).SetStringAsync(DisplayNameKey, "new name");
    }

    [Fact]
    public async Task ConfirmPairingAsync_MapsThrownExceptionsToFailed_WithoutMutatingState()
    {
        SeedInitDefaults(
            existingDeviceId: "device-123",
            existingDisplayName: "previous",
            isPaired: false);
        using var coordinator = CreateCoordinator();
        await coordinator.InitializeAsync();
        httpApiService.ConfirmPairingAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var displayNameChanged = 0;
        var isPairedChanged = 0;
        coordinator.DisplayNameChanged += (_, _) => displayNameChanged++;
        using var observation = coordinator.States.Skip(1).Subscribe(_ => isPairedChanged++);

        var result = await coordinator.ConfirmPairingAsync("new name", "1234");

        Assert.Equal(PairingFailureReason.Confirm, Assert.IsType<ConfirmPairingResult.Failed>(result).Reason);
        Assert.Equal("previous", coordinator.DisplayName);
        Assert.Equal(PairingClientState.Unpaired, coordinator.State);
        Assert.Equal(0, displayNameChanged);
        Assert.Equal(0, isPairedChanged);
        await secureStorage.DidNotReceive().SetStringAsync(DisplayNameKey, Arg.Any<string?>());
    }

    [Fact]
    public async Task ConfirmPairingAsync_ReturnsPinRejected_WithoutCommittingNameOrConfirmingPairing()
    {
        SeedInitDefaults(existingDisplayName: "previous");
        using var coordinator = CreateCoordinator();
        httpApiService.ConfirmPairingAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>())
            .ThrowsAsync(new HttpRequestException("unauthorized", null, HttpStatusCode.Unauthorized));

        var result = await coordinator.ConfirmPairingAsync("new name", "123456");

        Assert.IsType<ConfirmPairingResult.PinRejected>(result);
        Assert.Equal("previous", coordinator.DisplayName);
        Assert.Equal(PairingClientState.Unpaired, coordinator.State);
        await secureStorage.DidNotReceive().SetStringAsync(DisplayNameKey, Arg.Any<string?>());
    }

    // ----- UnpairAsync -----

    [Fact]
    public async Task UnpairAsync_ReturnsUnpaired_OnSuccess_AndFlipsIsPairedToFalse()
    {
        SeedInitDefaults(existingDeviceId: "device-123", isPaired: true);
        using var coordinator = CreateCoordinator();
        await coordinator.InitializeAsync();
        Assert.Equal(PairingClientState.Paired, coordinator.State);

        var isPairedChanged = 0;
        using var observation = coordinator.States.Skip(1).Subscribe(_ => isPairedChanged++);

        var result = await coordinator.UnpairAsync();

        Assert.IsType<UnpairResult.Unpaired>(result);
        Assert.Equal(PairingClientState.Unpaired, coordinator.State);
        Assert.Equal(1, isPairedChanged);
        await httpApiService.Received(1).UnpairAsync("device-123");
    }

    [Fact]
    public async Task UnpairAsync_MapsHttpRequestException_ToLocalOnly_AndStillFlipsIsPairedToFalse()
    {
        SeedInitDefaults(existingDeviceId: "device-123", isPaired: true);
        using var coordinator = CreateCoordinator();
        await coordinator.InitializeAsync();
        httpApiService.UnpairAsync(Arg.Any<string>())
            .Returns(_ =>
            {
                peerChanges.OnNext(null);
                return Task.FromException(new HttpRequestException("network gone"));
            });

        var result = await coordinator.UnpairAsync();

        Assert.IsType<UnpairResult.LocalOnly>(result);
        Assert.Equal(PairingClientState.Unpaired, coordinator.State);
    }

    [Fact]
    public async Task UnpairAsync_MapsOtherExceptions_ToFailed_AndStillFlipsIsPairedToFalse()
    {
        SeedInitDefaults(existingDeviceId: "device-123", isPaired: true);
        using var coordinator = CreateCoordinator();
        await coordinator.InitializeAsync();
        httpApiService.UnpairAsync(Arg.Any<string>())
            .Returns(_ =>
            {
                peerChanges.OnNext(null);
                return Task.FromException(new InvalidOperationException("boom"));
            });

        var result = await coordinator.UnpairAsync();

        Assert.Equal(PairingFailureReason.Unpair, Assert.IsType<UnpairResult.Failed>(result).Reason);
        Assert.Equal(PairingClientState.Unpaired, coordinator.State);
    }
    public void Dispose() => peerChanges.Dispose();
}
