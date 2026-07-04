using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using Sufni.App.ExtensionHost.Contracts.Capabilities;
using Sufni.App.Extensibility.Views;
using Sufni.App.Infrastructure;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Extensibility.Views;

[Collection("Ui")]
public class ExtensionViewRegistryTests
{
    [Fact]
    public void TryBuild_UsesWorkspaceFactory_WhenWorkspaceFactoryRegistered()
    {
        var registry = new ExtensionViewRegistry();
        registry.Register(
            typeof(ExtensionViewModel),
            static () => new TextBlock { Text = "shared" },
            compactFactory: null,
            workspaceFactory: static () => new TextBlock { Text = "workspace" });

        var matched = registry.TryBuild(new ExtensionViewModel(), UiLayoutProfile.Workspace, out var control);

        Assert.True(matched);
        Assert.Equal("workspace", Assert.IsType<TextBlock>(control).Text);
    }

    [Fact]
    public void TryBuild_UsesCompactFactory_WhenCompactFactoryRegistered()
    {
        var registry = new ExtensionViewRegistry();
        registry.Register(
            typeof(ExtensionViewModel),
            static () => new TextBlock { Text = "shared" },
            compactFactory: static () => new TextBlock { Text = "compact" },
            workspaceFactory: null);

        var matched = registry.TryBuild(new ExtensionViewModel(), UiLayoutProfile.Compact, out var control);

        Assert.True(matched);
        Assert.Equal("compact", Assert.IsType<TextBlock>(control).Text);
    }

    [Fact]
    public void TryBuild_FallsBackToSharedFactory_OnWorkspaceWithoutWorkspaceFactory()
    {
        var registry = new ExtensionViewRegistry();
        registry.Register(
            typeof(ExtensionViewModel),
            static () => new TextBlock { Text = "shared" },
            compactFactory: null,
            workspaceFactory: null);

        var matched = registry.TryBuild(new ExtensionViewModel(), UiLayoutProfile.Workspace, out var control);

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
            compactFactory: null,
            workspaceFactory: null);
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
            compactFactory: null,
            workspaceFactory: null);
        var locator = new ViewLocator(registry);
        var page = new RecordedSessionExtensionPageViewModel("Extension", () => new ExtensionViewModel());

        var control = locator.Build(page);

        Assert.Equal("recorded extension", Assert.IsType<TextBlock>(control).Text);
    }

    [AvaloniaFact]
    public void ViewLocator_Match_ReturnsTrue_ForRecordedSessionExtensionPage_WithRegisteredContent()
    {
        TestApp.SetIsDesktop(false);

        var registry = new ExtensionViewRegistry();
        registry.Register(
            typeof(ExtensionViewModel),
            static () => new TextBlock(),
            compactFactory: null,
            workspaceFactory: null);
        var locator = new ViewLocator(registry);
        var page = new RecordedSessionExtensionPageViewModel("Extension", () => new ExtensionViewModel());

        Assert.True(locator.Match(page));
    }

    [AvaloniaFact]
    public void ViewLocator_Match_ReturnsFalse_ForRecordedSessionExtensionPage_WithUnregisteredContent()
    {
        TestApp.SetIsDesktop(false);

        var locator = new ViewLocator(new ExtensionViewRegistry());
        var page = new RecordedSessionExtensionPageViewModel("Extension", () => new ExtensionViewModel());

        Assert.False(locator.Match(page));
    }

    [AvaloniaFact]
    public void ViewLocator_Match_ReturnsTrue_ForExtensionViewModel()
    {
        TestApp.SetIsDesktop(false);

        var registry = new ExtensionViewRegistry();
        registry.Register(
            typeof(ExtensionViewModel),
            static () => new TextBlock(),
            compactFactory: null,
            workspaceFactory: null);
        var locator = new ViewLocator(registry);

        Assert.True(locator.Match(new ExtensionViewModel()));
    }

    private sealed class ExtensionViewModel : IExtensionViewModel;
}
