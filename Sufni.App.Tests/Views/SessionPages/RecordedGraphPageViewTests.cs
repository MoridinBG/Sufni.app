using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.DesktopViews.Plots;
using Sufni.App.Presentation;
using Sufni.App.SessionDetails;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.App.ViewModels.SessionPages;
using Sufni.App.Views.Controls;
using Sufni.App.Views.SessionPages;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Views.SessionPages;

public class RecordedGraphPageViewTests
{
    [AvaloniaFact]
    public async Task RecordedGraphPageView_RendersPlots_WhenStatesReady()
    {
        var telemetry = TestTelemetryData.Create();
        telemetry.ImuData = TestTelemetryFactories.CreateTelemetryDataWithImu().ImuData;
        var workspace = new RecordedGraphPageWorkspaceStub(
            telemetry,
            SurfacePresentationState.Ready,
            SurfacePresentationState.Ready);

        var page = new RecordedGraphPageViewModel(workspace);

        await using var mounted = await MountAsync(page);

        var travelView = mounted.View.FindControl<TravelPlotDesktopView>("Travel");
        var velocityView = mounted.View.FindControl<VelocityPlotDesktopView>("Velocity");
        var imuView = mounted.View.FindControl<ImuPlotDesktopView>("Imu");

        Assert.NotNull(travelView);
        Assert.NotNull(velocityView);
        Assert.NotNull(imuView);
        Assert.True(travelView!.IsVisible);
        Assert.True(velocityView!.IsVisible);
        Assert.True(imuView!.IsVisible);
        Assert.Equal(RecordedSessionGraphDisplaySettings.DefaultMobileMaximumDisplayHz, travelView.MaximumDisplayHz);
        Assert.Equal(RecordedSessionGraphDisplaySettings.DefaultMobileMaximumDisplayHz, velocityView.MaximumDisplayHz);
        Assert.Equal(RecordedSessionGraphDisplaySettings.DefaultMobileMaximumDisplayHz, imuView.MaximumDisplayHz);
        Assert.False(mounted.View.FindControl<SurfacePlaceholderCard>("NoGraphDataPlaceholder")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task RecordedGraphPageView_ShowsPlaceholders_WhenStatesWaiting()
    {
        var workspace = new RecordedGraphPageWorkspaceStub(
            TestTelemetryData.Create(),
            SurfacePresentationState.WaitingForData("Waiting for travel data."),
            SurfacePresentationState.WaitingForData("Waiting for IMU data."));

        var page = new RecordedGraphPageViewModel(workspace);

        await using var mounted = await MountAsync(page);

        var hosts = mounted.View.GetVisualDescendants().OfType<PlaceholderOverlayContainer>().ToArray();
        Assert.Equal(2, hosts.Length);
        Assert.Equal(SurfaceStateKind.WaitingForData, hosts[0].PresentationState.Kind);
        Assert.Equal(SurfaceStateKind.WaitingForData, hosts[1].PresentationState.Kind);
        Assert.False(mounted.View.FindControl<SurfacePlaceholderCard>("NoGraphDataPlaceholder")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task RecordedGraphPageView_ShowsNoGraphDataFallback_WhenBothStatesHidden()
    {
        var workspace = new RecordedGraphPageWorkspaceStub(
            null,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden);

        var page = new RecordedGraphPageViewModel(workspace);

        await using var mounted = await MountAsync(page);

        var fallback = mounted.View.FindControl<SurfacePlaceholderCard>("NoGraphDataPlaceholder");
        var graphGrid = mounted.View.FindControl<Grid>("GraphGrid");

        Assert.NotNull(fallback);
        Assert.NotNull(graphGrid);
        Assert.True(fallback!.IsVisible);
        Assert.Equal(0, graphGrid!.RowDefinitions[0].Height.Value);
        Assert.Equal(0, graphGrid.RowDefinitions[1].Height.Value);
    }

    private static async Task<MountedRecordedGraphPageView> MountAsync(RecordedGraphPageViewModel page)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsurePlotViewStyle();

        var view = new RecordedGraphPageView
        {
            DataContext = page,
        };

        var host = await ViewTestHelpers.ShowViewAsync(new ScrollViewer { Content = view });
        return new MountedRecordedGraphPageView(host, view);
    }

    private sealed class RecordedGraphPageWorkspaceStub(
        TelemetryData? telemetryData,
        SurfacePresentationState travelGraphState,
        SurfacePresentationState imuGraphState) : IRecordedSessionGraphWorkspace
    {
        public TelemetryData? TelemetryData { get; } = telemetryData;
        public SurfacePresentationState TravelGraphState { get; } = travelGraphState;
        public SurfacePresentationState ImuGraphState { get; } = imuGraphState;
        public SessionTimelineLinkViewModel Timeline { get; } = new();
    }
}

internal sealed record MountedRecordedGraphPageView(Window Host, RecordedGraphPageView View) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}