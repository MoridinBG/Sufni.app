using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using Sufni.App.ExtensionHost.Contracts.Capabilities;
using Sufni.App.Extensibility.Views;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Extensibility.Views;

[Collection("Ui")]
public class ExtensionViewRegistryTests
{
    [Fact]
    public void TryBuild_UsesDesktopFactory_WhenDesktopFactoryRegistered()
    {
        var registry = new ExtensionViewRegistry();
        registry.Register(
            typeof(ExtensionViewModel),
            static () => new TextBlock { Text = "shared" },
            static () => new TextBlock { Text = "desktop" });

        var matched = registry.TryBuild(new ExtensionViewModel(), isDesktop: true, out var control);

        Assert.True(matched);
        Assert.Equal("desktop", Assert.IsType<TextBlock>(control).Text);
    }

    [Fact]
    public void TryBuild_FallsBackToSharedFactory_OnDesktopWithoutDesktopFactory()
    {
        var registry = new ExtensionViewRegistry();
        registry.Register(
            typeof(ExtensionViewModel),
            static () => new TextBlock { Text = "shared" },
            desktopFactory: null);

        var matched = registry.TryBuild(new ExtensionViewModel(), isDesktop: true, out var control);

        Assert.True(matched);
        Assert.Equal("shared", Assert.IsType<TextBlock>(control).Text);
    }

    [AvaloniaFact]
    public void ViewLocator_Build_UsesExtensionRegistryBeforeFallback()
    {
        TestApp.SetIsDesktop(false);

        var registry = new ExtensionViewRegistry();
        registry.Register(
            typeof(ExtensionViewModel),
            static () => new TextBlock { Text = "extension" },
            desktopFactory: null);
        var locator = new ViewLocator(registry);

        var control = locator.Build(new ExtensionViewModel());

        Assert.Equal("extension", Assert.IsType<TextBlock>(control).Text);
    }

    [AvaloniaFact]
    public void ViewLocator_Build_UsesExtensionRegistryForRecordedSessionExtensionPage()
    {
        TestApp.SetIsDesktop(false);

        var registry = new ExtensionViewRegistry();
        registry.Register(
            typeof(ExtensionViewModel),
            static () => new TextBlock { Text = "recorded extension" },
            desktopFactory: null);
        var locator = new ViewLocator(registry);
        var page = new RecordedSessionExtensionPageViewModel("Extension", new ExtensionViewModel());

        var control = locator.Build(page);

        Assert.Equal("recorded extension", Assert.IsType<TextBlock>(control).Text);
    }

    [AvaloniaFact]
    public void ViewLocator_Match_ReturnsTrue_ForExtensionViewModel()
    {
        TestApp.SetIsDesktop(false);

        var registry = new ExtensionViewRegistry();
        registry.Register(
            typeof(ExtensionViewModel),
            static () => new TextBlock(),
            desktopFactory: null);
        var locator = new ViewLocator(registry);

        Assert.True(locator.Match(new ExtensionViewModel()));
    }

    private sealed class ExtensionViewModel : IExtensionViewModel;
}
