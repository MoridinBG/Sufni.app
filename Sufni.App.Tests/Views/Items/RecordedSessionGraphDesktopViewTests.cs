using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.DesktopViews.Items;
using Sufni.App.DesktopViews.Plots;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;

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

        Assert.NotNull(travelView);
        Assert.NotNull(velocityView);
        Assert.NotNull(imuView);
        Assert.NotNull(imuSplitter);

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

        Assert.False(imuView.IsVisible);
        Assert.False(imuSplitter!.IsVisible);
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