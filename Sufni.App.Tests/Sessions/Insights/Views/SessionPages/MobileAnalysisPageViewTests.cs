using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Sufni.App.Sessions.Insights.ViewModels.SessionPages;
using Sufni.App.Sessions.Insights.Views.SessionPages;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Pages.Views.SessionPages;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Sessions;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Insights.Views.SessionPages;

[Collection("Ui")]
public class MobileAnalysisPageViewTests
{
    [AvaloniaFact]
    public async Task StrokesPageView_SideSelector_ShowsOneSuspensionSideAtATime()
    {
        var workspace = new TestSessionAnalysisWorkspace(
            telemetryData: TestTelemetryData.CreateProcessed(),
            hasFrontAnalysis: true,
            hasRearAnalysis: true);
        var page = new StrokesPageViewModel(workspace);

        await using var mounted = await MountAsync(new StrokesPageView { DataContext = page });

        var selector = mounted.View.FindControl<StackPanel>("MobileStrokesSideRadioButtons");
        var frontSide = mounted.View.FindControl<RadioButton>("MobileFrontStrokesSideRadioButton");
        var rearSide = mounted.View.FindControl<RadioButton>("MobileRearStrokesSideRadioButton");
        var frontPanel = mounted.View.FindControl<Grid>("FrontStrokesPanel");
        var rearPanel = mounted.View.FindControl<Grid>("RearStrokesPanel");

        Assert.NotNull(selector);
        Assert.True(selector!.IsVisible);
        Assert.True(frontSide!.IsChecked);
        Assert.False(rearSide!.IsChecked);
        Assert.True(frontPanel!.IsVisible);
        Assert.False(rearPanel!.IsVisible);

        rearSide.IsChecked = true;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(SuspensionType.Rear, page.SelectedSuspensionType);
        Assert.False(frontSide.IsChecked);
        Assert.True(rearSide.IsChecked);
        Assert.False(frontPanel.IsVisible);
        Assert.True(rearPanel.IsVisible);
    }

    private static async Task<MountedMobileAnalysisPageView<TView>> MountAsync<TView>(TView view)
        where TView : Control
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsurePlotViewStyle();

        var host = await ViewTestHelpers.ShowViewAsync(new ScrollViewer { Content = view });
        return new MountedMobileAnalysisPageView<TView>(host, view);
    }
}

internal sealed record MountedMobileAnalysisPageView<TView>(Window Host, TView View) : IAsyncDisposable
    where TView : Control
{
    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
