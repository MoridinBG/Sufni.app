using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.Infrastructure;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.ViewModels;

namespace Sufni.App.Tests.Infrastructure;

public class MobileAppBootstrapperTests
{
    [Fact]
    public void RegisterMobileSync_RegistersPlatformServicesAndMobileWorkflowsAsSingletons()
    {
        var services = new ServiceCollection();
        var secureStorage = Substitute.For<ISecureStorage>();
        var friendlyNameProvider = Substitute.For<IFriendlyNameProvider>();
        var hapticFeedback = Substitute.For<IHapticFeedback>();
        var discoveryKeys = new List<string>();
        var discoveries = new Dictionary<string, IServiceDiscovery>();

        MobileAppBootstrapper.RegisterMobileSync(
            services,
            () => secureStorage,
            () => friendlyNameProvider,
            key =>
            {
                discoveryKeys.Add(key);
                var discovery = Substitute.For<IServiceDiscovery>();
                discoveries.Add(key, discovery);
                return discovery;
            },
            () => hapticFeedback);

        using var provider = services.BuildServiceProvider();
        Assert.Same(secureStorage, provider.GetRequiredService<ISecureStorage>());
        Assert.Same(secureStorage, provider.GetRequiredService<ISecureStorage>());
        Assert.Same(friendlyNameProvider, provider.GetRequiredService<IFriendlyNameProvider>());
        Assert.Same(hapticFeedback, provider.GetRequiredService<IHapticFeedback>());

        var daqDiscovery = provider.GetRequiredKeyedService<IServiceDiscovery>("daq");
        var syncDiscovery = provider.GetRequiredKeyedService<IServiceDiscovery>("sync");
        Assert.Same(daqDiscovery, provider.GetRequiredKeyedService<IServiceDiscovery>("daq"));
        Assert.Same(syncDiscovery, provider.GetRequiredKeyedService<IServiceDiscovery>("sync"));
        Assert.NotSame(daqDiscovery, syncDiscovery);
        Assert.Same(discoveries["daq"], daqDiscovery);
        Assert.Same(discoveries["sync"], syncDiscovery);
        Assert.Equal(["daq", "sync"], discoveryKeys);

        AssertSingletonRegistration<ISynchronizationClientService>(services);
        AssertSingletonRegistration<IPairingClientCoordinator>(services, typeof(PairingClientCoordinator));
        AssertSingletonRegistration<PairingClientViewModel>(services, typeof(PairingClientViewModel));
    }

    private static void AssertSingletonRegistration<TService>(
        IServiceCollection services,
        Type? implementationType = null)
    {
        var descriptor = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TService));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        if (implementationType is not null)
        {
            Assert.Equal(implementationType, descriptor.ImplementationType);
        }
    }
}
