using Avalonia.Headless.XUnit;

using Sufni.App.Infrastructure;
using Sufni.App.Shell.DesktopViews;
using Sufni.App.Shell.Views;
using Sufni.App.Tests.Shell.ViewModels;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests;

[Collection("Ui")]
public class ViewLocatorTests
{
    [AvaloniaFact]
    public void Build_UsesWorkspaceFactory_WhenLayoutProfileIsWorkspaceAndAppIsNotDesktop()
    {
        ViewTestHelpers.EnsureViewTestResources();

        TestApp.SetIsDesktop(false);
        var environment = MainPagesViewModelTestFactory.CreateAppEnvironment(UiLayoutProfile.Workspace);
        var locator = new ViewLocator(environment);

        var view = locator.Build(MainPagesViewModelTestFactory.Create(appEnvironment: environment));

        Assert.IsType<MainPagesDesktopView>(view);
    }

    [AvaloniaFact]
    public void Build_UsesCompactFactory_WhenLayoutProfileIsCompactAndAppIsDesktop()
    {
        ViewTestHelpers.EnsureViewTestResources();

        TestApp.SetIsDesktop(true);
        var environment = MainPagesViewModelTestFactory.CreateAppEnvironment(UiLayoutProfile.Compact);
        var locator = new ViewLocator(environment);

        var view = locator.Build(MainPagesViewModelTestFactory.Create(appEnvironment: environment));

        Assert.IsType<MainPagesView>(view);
    }
}
