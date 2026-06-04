using Microsoft.Extensions.DependencyInjection;
using Sufni.App.ExtensionHost;

namespace Sufni.App.Tests.ExtensionHost;

public class AppExtensionCollectionTests
{
    [Fact]
    public void Add_RejectsDuplicateModuleIds()
    {
        var extensions = new AppExtensionCollection();
        extensions.Add(new TestExtensionModule("test"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            extensions.Add(new TestExtensionModule("test")));

        Assert.Contains("test", exception.Message);
    }

    [Fact]
    public void RegisterServicesAndCapabilities_PreservesModuleOrder()
    {
        var calls = new List<string>();
        var extensions = new AppExtensionCollection();
        extensions.Add(new TestExtensionModule(
            "first",
            registerServices: () => calls.Add("services:first"),
            registerCapabilities: () => calls.Add("capabilities:first")));
        extensions.Add(new TestExtensionModule(
            "second",
            registerServices: () => calls.Add("services:second"),
            registerCapabilities: () => calls.Add("capabilities:second")));

        var services = new ServiceCollection();
        var context = new AppExtensionServiceRegistrationContext(isDesktop: true);
        var registry = new AppExtensionCapabilityRegistry(new ExtensionViewRegistry());

        extensions.RegisterServices(services, context);
        extensions.RegisterCapabilities(registry);

        Assert.Equal(
            [
                "services:first",
                "services:second",
                "capabilities:first",
                "capabilities:second",
            ],
            calls);
    }

    [Fact]
    public void RegisterEagerService_DeduplicatesTypesAndPreservesOrder()
    {
        var registry = new AppExtensionCapabilityRegistry(new ExtensionViewRegistry());

        registry.RegisterEagerService(typeof(FirstService));
        registry.RegisterEagerService(typeof(SecondService));
        registry.RegisterEagerService(typeof(FirstService));

        Assert.Equal([typeof(FirstService), typeof(SecondService)], registry.EagerServiceTypes);
    }

    private sealed class TestExtensionModule(
        string id,
        Action? registerServices = null,
        Action? registerCapabilities = null) : IAppExtensionModule
    {
        public string Id { get; } = id;

        public void RegisterServices(IServiceCollection services, AppExtensionServiceRegistrationContext context)
        {
            registerServices?.Invoke();
        }

        public void RegisterCapabilities(IAppExtensionCapabilityRegistry registry)
        {
            registerCapabilities?.Invoke();
        }
    }

    private sealed class FirstService;

    private sealed class SecondService;
}
