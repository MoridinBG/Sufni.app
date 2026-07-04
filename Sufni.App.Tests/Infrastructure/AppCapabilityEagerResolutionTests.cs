using Sufni.App.Infrastructure;
using Sufni.App.SyncAndPairing.Coordinators;

namespace Sufni.App.Tests.Infrastructure;

public class AppCapabilityEagerResolutionTests
{
    [Fact]
    public void ResolveCapabilityEagerServices_ResolvesClientCoordinator_WhenClientCapabilityIsAvailable()
    {
        var services = new RecordingServiceProvider();

        App.ResolveCapabilityEagerServices(
            services,
            CreateCapabilities(canPairAsClient: true, canHostSyncServer: false));

        Assert.Contains(typeof(IPairingClientCoordinator), services.RequestedServices);
        Assert.DoesNotContain(typeof(IPairingServerCoordinator), services.RequestedServices);
        Assert.DoesNotContain(typeof(IInboundSyncCoordinator), services.RequestedServices);
    }

    [Fact]
    public void ResolveCapabilityEagerServices_ResolvesServerCoordinators_WhenHostCapabilityIsAvailable()
    {
        var services = new RecordingServiceProvider();

        App.ResolveCapabilityEagerServices(
            services,
            CreateCapabilities(canPairAsClient: false, canHostSyncServer: true));

        Assert.DoesNotContain(typeof(IPairingClientCoordinator), services.RequestedServices);
        Assert.Contains(typeof(IPairingServerCoordinator), services.RequestedServices);
        Assert.Contains(typeof(IInboundSyncCoordinator), services.RequestedServices);
    }

    [Fact]
    public void ResolveCapabilityEagerServices_ResolvesBothSides_WhenBothCapabilitiesAreAvailable()
    {
        var services = new RecordingServiceProvider();

        App.ResolveCapabilityEagerServices(
            services,
            CreateCapabilities(canPairAsClient: true, canHostSyncServer: true));

        Assert.Contains(typeof(IPairingClientCoordinator), services.RequestedServices);
        Assert.Contains(typeof(IPairingServerCoordinator), services.RequestedServices);
        Assert.Contains(typeof(IInboundSyncCoordinator), services.RequestedServices);
    }

    [Fact]
    public void ResolveCapabilityEagerServices_DoesNotResolvePlatformCoordinators_WhenCapabilitiesAreUnavailable()
    {
        var services = new RecordingServiceProvider();

        App.ResolveCapabilityEagerServices(
            services,
            CreateCapabilities(canPairAsClient: false, canHostSyncServer: false));

        Assert.Empty(services.RequestedServices);
    }

    private static AppCapabilities CreateCapabilities(bool canPairAsClient, bool canHostSyncServer) =>
        new(
            CanHostSyncServer: canHostSyncServer,
            CanPairAsClient: canPairAsClient,
            HasHaptics: false,
            SupportsMassStorageImport: false,
            SupportsStorageProviderImport: true,
            SupportsNativeWindowing: false);

    private sealed class RecordingServiceProvider : IServiceProvider
    {
        public List<Type> RequestedServices { get; } = [];

        public object? GetService(Type serviceType)
        {
            RequestedServices.Add(serviceType);
            return null;
        }
    }
}
