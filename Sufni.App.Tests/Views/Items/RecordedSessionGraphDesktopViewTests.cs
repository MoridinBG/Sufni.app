using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.DesktopViews.Items;
using Sufni.App.DesktopViews.Plots;
using Sufni.App.Presentation;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;
using static Sufni.App.Tests.Infrastructure.TestTelemetryFactories;

namespace Sufni.App.Tests.Views.Items;

public class RecordedSessionGraphDesktopViewTests
{
    [AvaloniaFact]
    public async Task RecordedSessionGraphDesktopView_HidesImuRegion_WhenTelemetryHasNoImuData()
    {
        var workspace = new RecordedSessionGraphWorkspaceStub(TestTelemetryData.Create());

        await using var mounted = await MountAsync(workspace);

        var travelView = mounted.View.FindControl<TravelPlotDesktopView>("Travel");
        var velocityView = mounted.View.FindControl<VelocityPlotDesktopView>("Velocity");
        var imuView = mounted.View.FindControl<ImuPlotDesktopView>("Imu");
        var imuSplitter = mounted.View.FindControl<GridSplitter>("ImuSplitter");
        var graphGrid = mounted.View.FindControl<Grid>("GraphGrid");

        Assert.NotNull(travelView);
        Assert.NotNull(velocityView);
        Assert.NotNull(imuView);
        Assert.NotNull(imuSplitter);
        Assert.NotNull(graphGrid);

        Assert.Same(workspace.TelemetryData, travelView!.Telemetry);
        Assert.Same(workspace.Timeline, travelView.Timeline);
        Assert.Same(velocityView, travelView.VelocityPlotView);
        Assert.Same(imuView, travelView.ImuPlotView);

        Assert.Same(workspace.TelemetryData, velocityView!.Telemetry);
        Assert.Same(workspace.Timeline, velocityView.Timeline);
        Assert.Same(travelView, velocityView.TravelPlotView);
        Assert.Same(imuView, velocityView.ImuPlotView);

        Assert.Same(workspace.TelemetryData, imuView!.Telemetry);
        Assert.Same(workspace.Timeline, imuView.Timeline);
        Assert.Same(travelView, imuView.TravelPlotView);
        Assert.Same(velocityView, imuView.VelocityPlotView);

        Assert.False(imuSplitter!.IsVisible);
        Assert.Equal(0, graphGrid!.RowDefinitions[2].Height.Value);
        Assert.Equal(GridUnitType.Pixel, graphGrid.RowDefinitions[2].Height.GridUnitType);
    }

    [AvaloniaFact]
    public async Task RecordedSessionGraphDesktopView_HidesTravelRegion_WhenTelemetryHasNoTravelData()
    {
        var telemetry = CreateTelemetryDataWithImu();
        telemetry.Front.Present = false;
        telemetry.Rear.Present = false;
        telemetry.Front.Travel = [];
        telemetry.Rear.Travel = [];
        telemetry.Front.Velocity = [];
        telemetry.Rear.Velocity = [];
        var workspace = new RecordedSessionGraphWorkspaceStub(telemetry);

        await using var mounted = await MountAsync(workspace);

        var travelView = mounted.View.FindControl<TravelPlotDesktopView>("Travel");
        var velocityView = mounted.View.FindControl<VelocityPlotDesktopView>("Velocity");
        var imuView = mounted.View.FindControl<ImuPlotDesktopView>("Imu");
        var imuSplitter = mounted.View.FindControl<GridSplitter>("ImuSplitter");
        var graphGrid = mounted.View.FindControl<Grid>("GraphGrid");

        Assert.NotNull(travelView);
        Assert.NotNull(velocityView);
        Assert.NotNull(imuView);
        Assert.NotNull(imuSplitter);
        Assert.NotNull(graphGrid);
        Assert.Equal(0, graphGrid!.RowDefinitions[0].Height.Value);
        Assert.Equal(GridUnitType.Pixel, graphGrid.RowDefinitions[0].Height.GridUnitType);
        Assert.False(imuSplitter!.IsVisible);
        Assert.NotEqual(0, graphGrid.RowDefinitions[2].Height.Value);
        Assert.Equal(GridUnitType.Star, graphGrid.RowDefinitions[2].Height.GridUnitType);
    }

    [AvaloniaFact]
    public async Task RecordedSessionGraphDesktopView_ShowsImuRegion_WhenTelemetryHasImuData()
    {
        var telemetry = TestTelemetryData.Create();
        telemetry.ImuData = TestTelemetryFactories.CreateTelemetryDataWithImu().ImuData;
        var workspace = new RecordedSessionGraphWorkspaceStub(telemetry);

        await using var mounted = await MountAsync(workspace);

        var imuView = mounted.View.FindControl<ImuPlotDesktopView>("Imu");
        var imuSplitter = mounted.View.FindControl<GridSplitter>("ImuSplitter");

        Assert.NotNull(imuView);
        Assert.NotNull(imuSplitter);
        Assert.True(imuView!.IsVisible);
        Assert.True(imuSplitter!.IsVisible);
    }

    private static async Task<MountedRecordedSessionGraphDesktopView> MountAsync(RecordedSessionGraphWorkspaceStub workspace)
    {
        ViewTestHelpers.EnsureSessionDetailViewSetup(isDesktop: true);

        var view = new RecordedSessionGraphDesktopView
        {
            DataContext = workspace,
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedRecordedSessionGraphDesktopView(host, view);
    }

    private sealed class RecordedSessionGraphWorkspaceStub(TelemetryData telemetryData) : IRecordedSessionGraphWorkspace
    {
        public TelemetryData? TelemetryData { get; } = telemetryData;
        public SurfacePresentationState TravelGraphState =>
            TelemetryData is { } telemetry && (telemetry.Front.Present || telemetry.Rear.Present)
                ? SurfacePresentationState.Ready
                : SurfacePresentationState.Hidden;
        public SurfacePresentationState ImuGraphState =>
            TelemetryData?.ImuData is { } imuData &&
            imuData.Records.Count > 0 &&
            imuData.ActiveLocations.Count > 0
                ? SurfacePresentationState.Ready
                : SurfacePresentationState.Hidden;
        public SessionTimelineLinkViewModel Timeline { get; } = new();
    }
}

internal sealed class MountedRecordedSessionGraphDesktopView(Window host, RecordedSessionGraphDesktopView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public RecordedSessionGraphDesktopView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}