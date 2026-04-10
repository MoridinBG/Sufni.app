using System.Net;
using System.Net.Http;
using System.Reflection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ServiceDiscovery;
using Sufni.App.Coordinators;
using Sufni.App.Services;

namespace Sufni.App.Tests.Coordinators;

public class PairingClientCoordinatorTests
{
    private const string DeviceIdKey = "DeviceId";
    private const string DisplayNameKey = "DisplayName";

    private readonly ISecureStorage secureStorage = Substitute.For<ISecureStorage>();
    private readonly IHttpApiService httpApiService = Substitute.For<IHttpApiService>();
    private readonly IServiceDiscovery serviceDiscovery = Substitute.For<IServiceDiscovery>();
    private readonly IFriendlyNameProvider friendlyNameProvider = Substitute.For<IFriendlyNameProvider>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();

    private PairingClientCoordinator CreateCoordinator() => new(
        secureStorage, httpApiService, serviceDiscovery, friendlyNameProvider, shell);

    /// <summary>
    /// `ServiceAnnouncement.Port` and `.Address` have internal setters
    /// and `ServiceDiscovery` does not expose its internals to the test
    /// project, so tests that need to raise a populated
    /// `ServiceAdded` / `ServiceRemoved` event fall back to reflection.
    /// </summary>
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
        secureStorage.GetStringAsync(DeviceIdKey).Returns(Task.FromResult(existingDeviceId));
        secureStorage.GetStringAsync(DisplayNameKey).Returns(Task.FromResult(existingDisplayName));
        friendlyNameProvider.FriendlyName.Returns(friendlyName!);
        httpApiService.IsPairedAsync().Returns(Task.FromResult(isPaired));
    }

    /// <summary>
    /// Forces `Initialization` to drain by calling the cheapest public
    /// method — `RequestPairingAsync` with a null `ServerUrl` returns
    /// `Failed` without touching the http substitute.
    /// </summary>
    private async Task DrainInitializationAsync(PairingClientCoordinator coordinator)
    {
        _ = await coordinator.RequestPairingAsync(null);
    }

    // ----- Constructor startup -----

    [Fact]
    public async Task Constructor_LoadsExistingDeviceId_AndRaisesDeviceIdChanged()
    {
        // Park Init() on its first await so the test can subscribe to
        // DeviceIdChanged before the event fires. NSubstitute's
        // Task.FromResult(...) stubs otherwise let Init() run to
        // completion inside the constructor call.
        var deviceIdGate = new TaskCompletionSource<string?>();
        secureStorage.GetStringAsync(DeviceIdKey).Returns(deviceIdGate.Task);
        secureStorage.GetStringAsync(DisplayNameKey).Returns(Task.FromResult<string?>(null));
        friendlyNameProvider.FriendlyName.Returns((string)null!);
        httpApiService.IsPairedAsync().Returns(Task.FromResult(false));

        var coordinator = CreateCoordinator();
        var eventRaised = false;
        coordinator.DeviceIdChanged += (_, _) => eventRaised = true;

        deviceIdGate.SetResult("stored-id");
        await DrainInitializationAsync(coordinator);

        Assert.Equal("stored-id", coordinator.DeviceId);
        Assert.True(eventRaised);
        await secureStorage.DidNotReceive().SetStringAsync(DeviceIdKey, Arg.Any<string?>());
    }

    [Fact]
    public async Task Constructor_GeneratesAndPersistsNewDeviceId_WhenSecureStorageEmpty()
    {
        SeedInitDefaults(existingDeviceId: null);

        var coordinator = CreateCoordinator();
        await DrainInitializationAsync(coordinator);

        Assert.NotNull(coordinator.DeviceId);
        Assert.True(Guid.TryParse(coordinator.DeviceId, out _));
        await secureStorage.Received(1).SetStringAsync(DeviceIdKey, coordinator.DeviceId);
    }

    [Fact]
    public async Task Constructor_PrefersStoredDisplayName_OverFriendlyName()
    {
        SeedInitDefaults(
            existingDisplayName: "  stored name  ",
            friendlyName: "friendly name");

        var coordinator = CreateCoordinator();
        await DrainInitializationAsync(coordinator);

        Assert.Equal("stored name", coordinator.DisplayName);
        _ = friendlyNameProvider.DidNotReceive().FriendlyName;
    }

    [Fact]
    public async Task Constructor_FallsBackToFriendlyName_WhenNoStoredDisplayName()
    {
        SeedInitDefaults(
            existingDisplayName: null,
            friendlyName: "  friendly name  ");

        var coordinator = CreateCoordinator();
        await DrainInitializationAsync(coordinator);

        Assert.Equal("friendly name", coordinator.DisplayName);
    }

    [Fact]
    public async Task Constructor_FallsBackToNull_WhenNeitherStoredNorFriendlyNameAvailable()
    {
        SeedInitDefaults(
            existingDisplayName: null,
            friendlyName: null);

        var coordinator = CreateCoordinator();
        await DrainInitializationAsync(coordinator);

        Assert.Null(coordinator.DisplayName);
    }

    [Fact]
    public async Task Constructor_NormalizesWhitespaceStoredDisplayName_AndFallsBackToFriendlyName()
    {
        SeedInitDefaults(
            existingDisplayName: "   ",
            friendlyName: "friendly name");

        var coordinator = CreateCoordinator();
        await DrainInitializationAsync(coordinator);

        Assert.Equal("friendly name", coordinator.DisplayName);
    }

    [Fact]
    public async Task Constructor_MirrorsIsPairedFromHttpApiService_AndRaisesIsPairedChanged()
    {
        // Same gating trick as Constructor_LoadsExistingDeviceId — park
        // Init() on its first await so the subscription happens before
        // IsPairedChanged fires.
        var deviceIdGate = new TaskCompletionSource<string?>();
        secureStorage.GetStringAsync(DeviceIdKey).Returns(deviceIdGate.Task);
        secureStorage.GetStringAsync(DisplayNameKey).Returns(Task.FromResult<string?>(null));
        friendlyNameProvider.FriendlyName.Returns((string)null!);
        httpApiService.IsPairedAsync().Returns(Task.FromResult(true));

        var coordinator = CreateCoordinator();
        var eventRaised = false;
        coordinator.IsPairedChanged += (_, _) => eventRaised = true;

        deviceIdGate.SetResult("device-123");
        await DrainInitializationAsync(coordinator);

        Assert.True(coordinator.IsPaired);
        Assert.True(eventRaised);
        await httpApiService.Received(1).IsPairedAsync();
    }

    // ----- StartBrowsing / StopBrowsing -----

    [Fact]
    public void StartBrowsing_DelegatesToServiceDiscovery_WithSyncServiceType()
    {
        SeedInitDefaults();
        var coordinator = CreateCoordinator();

        coordinator.StartBrowsing();

        serviceDiscovery.Received(1).StartBrowse(SynchronizationProtocol.ServiceType);
    }

    [Fact]
    public void StopBrowsing_DelegatesToServiceDiscovery()
    {
        SeedInitDefaults();
        var coordinator = CreateCoordinator();

        coordinator.StopBrowsing();

        serviceDiscovery.Received(1).StopBrowse();
    }

    // ----- ServiceAdded / ServiceRemoved -----

    [Fact]
    public async Task ServiceAdded_BuildsHttpsUrl_ForIPv4Address()
    {
        SeedInitDefaults();
        var coordinator = CreateCoordinator();
        await DrainInitializationAsync(coordinator);

        var urlChanged = false;
        coordinator.ServerUrlChanged += (_, _) => urlChanged = true;

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(MakeAnnouncement(IPAddress.Parse("192.168.1.10"), 8443)));

        Assert.Equal("https://192.168.1.10:8443", coordinator.ServerUrl);
        Assert.True(urlChanged);
    }

    [Fact]
    public async Task ServiceAdded_NormalizesIPv4MappedIPv6Address()
    {
        SeedInitDefaults();
        var coordinator = CreateCoordinator();
        await DrainInitializationAsync(coordinator);

        // ::ffff:192.0.2.1 is an IPv4-mapped IPv6 address.
        var mapped = IPAddress.Parse("::ffff:192.0.2.1");
        Assert.True(mapped.IsIPv4MappedToIPv6);

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(MakeAnnouncement(mapped, 9000)));

        Assert.Equal("https://192.0.2.1:9000", coordinator.ServerUrl);
    }

    [Fact]
    public async Task ServiceAdded_BracketsRawIPv6Address()
    {
        SeedInitDefaults();
        var coordinator = CreateCoordinator();
        await DrainInitializationAsync(coordinator);

        var ipv6 = IPAddress.Parse("fe80::1");

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(MakeAnnouncement(ipv6, 4321)));

        Assert.Equal("https://[fe80::1]:4321", coordinator.ServerUrl);
    }

    [Fact]
    public async Task ServiceRemoved_ClearsServerUrl_AndRaisesServerUrlChanged()
    {
        SeedInitDefaults();
        var coordinator = CreateCoordinator();
        await DrainInitializationAsync(coordinator);

        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(MakeAnnouncement(IPAddress.Parse("10.0.0.1"), 1234)));
        Assert.NotNull(coordinator.ServerUrl);

        var urlChanged = false;
        coordinator.ServerUrlChanged += (_, _) => urlChanged = true;

        serviceDiscovery.ServiceRemoved += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(MakeAnnouncement(IPAddress.Parse("10.0.0.1"), 1234)));

        Assert.Null(coordinator.ServerUrl);
        Assert.True(urlChanged);
    }

    // ----- RequestPairingAsync -----

    [Fact]
    public async Task RequestPairingAsync_ReturnsFailed_AndDoesNotTouchHttp_WhenServerUrlIsNull()
    {
        SeedInitDefaults();
        var coordinator = CreateCoordinator();

        var result = await coordinator.RequestPairingAsync("label");

        Assert.IsType<RequestPairingResult.Failed>(result);
        await httpApiService.DidNotReceiveWithAnyArgs().RequestPairingAsync(default!, default!, default);
    }

    [Fact]
    public async Task RequestPairingAsync_PassesNormalizedDisplayNameToHttp_AndReturnsSent()
    {
        SeedInitDefaults(existingDeviceId: "device-123");
        var coordinator = CreateCoordinator();
        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(MakeAnnouncement(IPAddress.Parse("192.168.1.10"), 8443)));

        var result = await coordinator.RequestPairingAsync("  My Phone  ");

        Assert.IsType<RequestPairingResult.Sent>(result);
        await httpApiService.Received(1).RequestPairingAsync(
            "https://192.168.1.10:8443",
            "device-123",
            "My Phone");
    }

    [Fact]
    public async Task RequestPairingAsync_MapsThrownExceptionsToFailed()
    {
        SeedInitDefaults();
        var coordinator = CreateCoordinator();
        serviceDiscovery.ServiceAdded += Raise.EventWith(
            serviceDiscovery,
            new ServiceAnnouncementEventArgs(MakeAnnouncement(IPAddress.Parse("192.168.1.10"), 8443)));
        httpApiService.RequestPairingAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await coordinator.RequestPairingAsync("label");

        Assert.IsType<RequestPairingResult.Failed>(result);
    }

    // ----- ConfirmPairingAsync -----

    [Fact]
    public async Task ConfirmPairingAsync_ForwardsNormalizedDisplayNameAndPin_ToHttp()
    {
        SeedInitDefaults(existingDeviceId: "device-123");
        var coordinator = CreateCoordinator();

        var result = await coordinator.ConfirmPairingAsync("  My Phone  ", "1234");

        Assert.IsType<ConfirmPairingResult.Paired>(result);
        await httpApiService.Received(1).ConfirmPairingAsync("device-123", "My Phone", "1234");
    }

    [Fact]
    public async Task ConfirmPairingAsync_HappyPath_FromFallbackName_RaisesDisplayNameChanged()
    {
        SeedInitDefaults(
            existingDeviceId: "device-123",
            existingDisplayName: null,
            friendlyName: "phone",
            isPaired: false);
        var coordinator = CreateCoordinator();
        await DrainInitializationAsync(coordinator);
        Assert.Equal("phone", coordinator.DisplayName);

        var displayNameChanged = 0;
        var isPairedChanged = 0;
        var pairingConfirmed = 0;
        coordinator.DisplayNameChanged += (_, _) => displayNameChanged++;
        coordinator.IsPairedChanged += (_, _) => isPairedChanged++;
        coordinator.PairingConfirmed += (_, _) => pairingConfirmed++;

        var result = await coordinator.ConfirmPairingAsync("new name", "1234");

        Assert.IsType<ConfirmPairingResult.Paired>(result);
        Assert.Equal("new name", coordinator.DisplayName);
        Assert.True(coordinator.IsPaired);
        Assert.Equal(1, displayNameChanged);
        Assert.Equal(1, isPairedChanged);
        Assert.Equal(1, pairingConfirmed);
        await secureStorage.Received(1).SetStringAsync(DisplayNameKey, "new name");
        shell.Received(1).GoBack();
    }

    [Fact]
    public async Task ConfirmPairingAsync_HappyPath_ReConfirmWithSameName_DoesNotRaiseDisplayNameChanged()
    {
        SeedInitDefaults(
            existingDeviceId: "device-123",
            existingDisplayName: "same name",
            isPaired: false);
        var coordinator = CreateCoordinator();
        await DrainInitializationAsync(coordinator);
        Assert.Equal("same name", coordinator.DisplayName);

        var displayNameChanged = 0;
        var isPairedChanged = 0;
        var pairingConfirmed = 0;
        coordinator.DisplayNameChanged += (_, _) => displayNameChanged++;
        coordinator.IsPairedChanged += (_, _) => isPairedChanged++;
        coordinator.PairingConfirmed += (_, _) => pairingConfirmed++;

        var result = await coordinator.ConfirmPairingAsync("same name", "1234");

        Assert.IsType<ConfirmPairingResult.Paired>(result);
        Assert.Equal("same name", coordinator.DisplayName);
        Assert.True(coordinator.IsPaired);
        Assert.Equal(0, displayNameChanged);
        Assert.Equal(1, isPairedChanged);
        Assert.Equal(1, pairingConfirmed);
        shell.Received(1).GoBack();
    }

    [Fact]
    public async Task ConfirmPairingAsync_MapsThrownExceptionsToFailed_WithoutMutatingState()
    {
        SeedInitDefaults(
            existingDeviceId: "device-123",
            existingDisplayName: "previous",
            isPaired: false);
        var coordinator = CreateCoordinator();
        await DrainInitializationAsync(coordinator);
        httpApiService.ConfirmPairingAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var displayNameChanged = 0;
        var isPairedChanged = 0;
        var pairingConfirmed = 0;
        coordinator.DisplayNameChanged += (_, _) => displayNameChanged++;
        coordinator.IsPairedChanged += (_, _) => isPairedChanged++;
        coordinator.PairingConfirmed += (_, _) => pairingConfirmed++;

        var result = await coordinator.ConfirmPairingAsync("new name", "1234");

        Assert.IsType<ConfirmPairingResult.Failed>(result);
        Assert.Equal("previous", coordinator.DisplayName);
        Assert.False(coordinator.IsPaired);
        Assert.Equal(0, displayNameChanged);
        Assert.Equal(0, isPairedChanged);
        Assert.Equal(0, pairingConfirmed);
        await secureStorage.DidNotReceive().SetStringAsync(DisplayNameKey, Arg.Any<string?>());
        shell.DidNotReceive().GoBack();
    }

    // ----- UnpairAsync -----

    [Fact]
    public async Task UnpairAsync_ReturnsUnpaired_OnSuccess_AndFlipsIsPairedToFalse()
    {
        SeedInitDefaults(existingDeviceId: "device-123", isPaired: true);
        var coordinator = CreateCoordinator();
        await DrainInitializationAsync(coordinator);
        Assert.True(coordinator.IsPaired);

        var isPairedChanged = 0;
        coordinator.IsPairedChanged += (_, _) => isPairedChanged++;

        var result = await coordinator.UnpairAsync();

        Assert.IsType<UnpairResult.Unpaired>(result);
        Assert.False(coordinator.IsPaired);
        Assert.Equal(1, isPairedChanged);
        await httpApiService.Received(1).UnpairAsync("device-123");
    }

    [Fact]
    public async Task UnpairAsync_MapsHttpRequestException_ToLocalOnly_AndStillFlipsIsPairedToFalse()
    {
        SeedInitDefaults(existingDeviceId: "device-123", isPaired: true);
        var coordinator = CreateCoordinator();
        await DrainInitializationAsync(coordinator);
        httpApiService.UnpairAsync(Arg.Any<string>())
            .ThrowsAsync(new HttpRequestException("network gone"));

        var result = await coordinator.UnpairAsync();

        Assert.IsType<UnpairResult.LocalOnly>(result);
        Assert.False(coordinator.IsPaired);
    }

    [Fact]
    public async Task UnpairAsync_MapsOtherExceptions_ToFailed_AndStillFlipsIsPairedToFalse()
    {
        SeedInitDefaults(existingDeviceId: "device-123", isPaired: true);
        var coordinator = CreateCoordinator();
        await DrainInitializationAsync(coordinator);
        httpApiService.UnpairAsync(Arg.Any<string>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await coordinator.UnpairAsync();

        Assert.IsType<UnpairResult.Failed>(result);
        Assert.False(coordinator.IsPaired);
    }
}
