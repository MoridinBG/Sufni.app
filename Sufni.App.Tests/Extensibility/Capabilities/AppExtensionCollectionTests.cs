using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.ExtensionHost.Contracts;

using Sufni.App.ExtensionHost.Contracts.Capabilities;
using Sufni.App.Extensibility.Capabilities;
using Sufni.App.Extensibility.Views;
namespace Sufni.App.Tests.Extensibility.Capabilities;

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
    public void Add_RejectsBlankModuleIds()
    {
        var extensions = new AppExtensionCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            extensions.Add(new TestExtensionModule(" ")));

        Assert.Contains("id is required", exception.Message);
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

    [Fact]
    public void AddExtensionSingletonAlias_ReusesSingleImplementationForMultipleServiceTypes()
    {
        var services = new ServiceCollection();
        services.AddExtensionSingletonAlias<IFirstAlias, AliasedService>();
        services.AddExtensionSingletonAlias<ISecondAlias, AliasedService>();
        using var provider = services.BuildServiceProvider();

        var concrete = provider.GetRequiredService<AliasedService>();
        var firstAlias = provider.GetRequiredService<IFirstAlias>();
        var secondAlias = provider.GetRequiredService<ISecondAlias>();

        Assert.Same(concrete, firstAlias);
        Assert.Same(concrete, secondAlias);
    }

    [Fact]
    public void RegisterView_GenericOverload_RegistersSharedAndDesktopFactories()
    {
        var viewRegistry = new ExtensionViewRegistry();
        var registry = new AppExtensionCapabilityRegistry(viewRegistry);
        var viewModel = new TestViewModel();

        registry.RegisterView<TestViewModel, SharedView, DesktopView>();

        Assert.True(viewRegistry.TryBuild(viewModel, isDesktop: false, out var sharedView));
        Assert.IsType<SharedView>(sharedView);
        Assert.True(viewRegistry.TryBuild(viewModel, isDesktop: true, out var desktopView));
        Assert.IsType<DesktopView>(desktopView);
    }

    [Fact]
    public void RegisterView_RejectsDuplicateViewModelTypes()
    {
        var viewRegistry = new ExtensionViewRegistry();
        var registry = new AppExtensionCapabilityRegistry(viewRegistry);

        registry.RegisterView<TestViewModel, SharedView>();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterView<TestViewModel, DesktopView>());

        Assert.Contains(typeof(TestViewModel).FullName!, exception.Message);
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

    private interface IFirstAlias;

    private interface ISecondAlias;

    private sealed class AliasedService : IFirstAlias, ISecondAlias;

    private sealed class TestViewModel;

    private sealed class SharedView : Control;

    private sealed class DesktopView : Control;
}
