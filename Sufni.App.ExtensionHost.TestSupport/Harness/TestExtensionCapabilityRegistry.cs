using Avalonia.Controls;

using Sufni.App.Extensibility.Capabilities;
using Sufni.App.Extensibility.Views;
using Sufni.App.ExtensionHost.Contracts.Capabilities;
using Sufni.App.Infrastructure;
namespace Sufni.App.ExtensionHost.TestSupport.Harness;

/// <summary>
/// Capability registry for module-registration tests, backed by the real
/// host registry and view registry so registration semantics (including
/// duplicate-view rejection) match the app.
/// </summary>
public sealed class TestExtensionCapabilityRegistry : IAppExtensionCapabilityRegistry
{
    private readonly ExtensionViewRegistry viewRegistry = new();
    private readonly AppExtensionCapabilityRegistry inner;

    public TestExtensionCapabilityRegistry()
    {
        inner = new AppExtensionCapabilityRegistry(viewRegistry);
    }

    public IReadOnlyList<Type> EagerServiceTypes => inner.EagerServiceTypes;

    public void RegisterEagerService(Type serviceType) => inner.RegisterEagerService(serviceType);

    public void RegisterView(
        Type viewModelType,
        Func<Control> sharedFactory,
        Func<Control>? compactFactory = null,
        Func<Control>? workspaceFactory = null) =>
        inner.RegisterView(viewModelType, sharedFactory, compactFactory, workspaceFactory);

    public void RegisterView(
        Type viewModelType,
        Func<IServiceProvider, Control> sharedFactory,
        Func<IServiceProvider, Control>? compactFactory = null,
        Func<IServiceProvider, Control>? workspaceFactory = null) =>
        inner.RegisterView(viewModelType, sharedFactory, compactFactory, workspaceFactory);

    public bool Matches(Type viewModelType, UiLayoutProfile layoutProfile) =>
        viewRegistry.Matches(viewModelType, layoutProfile);
}
