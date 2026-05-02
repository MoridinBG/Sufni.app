using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
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

    [AvaloniaFact]
    public async Task RecordedSessionGraphDesktopView_AnalysisRangeBindingKeepsAndClearsOverlay()
    {
        var workspace = new RecordedSessionGraphWorkspaceStub(CreateTelemetryData());

        await using var mounted = await MountAsync(workspace);

        var travelView = mounted.View.FindControl<TravelPlotDesktopView>("Travel");
        Assert.NotNull(travelView);
        var plot = Assert.Single(travelView!.GetVisualDescendants().OfType<AvaPlot>());
        Assert.True(plot.Bounds.Width > 0);
        Assert.True(plot.Bounds.Height > 0);

        workspace.SetAnalysisRange(0.25, 0.75);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.NotNull(workspace.AnalysisRange);
        Assert.Equal(workspace.AnalysisRange, travelView.AnalysisRange);
        var visibleSpan = Assert.Single(plot.Plot.PlottableList.OfType<HorizontalSpan>(), span => span.IsVisible);
        Assert.Equal(workspace.AnalysisRange!.Value.StartSeconds, visibleSpan.X1, 3);
        Assert.Equal(workspace.AnalysisRange.Value.EndSeconds, visibleSpan.X2, 3);
        workspace.ClearAnalysisRange();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Null(travelView.AnalysisRange);
        Assert.DoesNotContain(plot.Plot.PlottableList.OfType<HorizontalSpan>(), span => span.IsVisible);
    }

    [AvaloniaFact]
    public async Task RecordedSessionGraphDesktopView_TravelPlotClick_ClearsAnalysisRange()
    {
        var workspace = new RecordedSessionGraphWorkspaceStub(CreateTelemetryData());

        await using var mounted = await MountAsync(workspace);

        var travelView = mounted.View.FindControl<TravelPlotDesktopView>("Travel");
        Assert.NotNull(travelView);
        var plot = Assert.Single(travelView!.GetVisualDescendants().OfType<AvaPlot>());
        workspace.SetAnalysisRange(0.25, 0.75);
        await ViewTestHelpers.FlushDispatcherAsync();

        var clickPoint = plot.TranslatePoint(
            new Point(plot.Bounds.Width / 2, plot.Bounds.Height / 2),
            mounted.Host);
        Assert.True(plot.Bounds.Width > 0 && plot.Bounds.Height > 0, $"Plot bounds were {plot.Bounds}.");
        Assert.NotNull(clickPoint);

        mounted.Host.MouseDown(clickPoint.Value, MouseButton.Left, RawInputModifiers.None);
        mounted.Host.MouseUp(clickPoint.Value, MouseButton.Left, RawInputModifiers.None);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(1, workspace.ClearAnalysisRangeCallCount);
        Assert.Null(workspace.AnalysisRange);
        Assert.DoesNotContain(plot.Plot.PlottableList.OfType<HorizontalSpan>(), span => span.IsVisible);
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

    private sealed class RecordedSessionGraphWorkspaceStub(TelemetryData telemetryData) :
        IRecordedSessionGraphWorkspace,
        INotifyPropertyChanged
    {
        private TelemetryTimeRange? analysisRange;

        public event PropertyChangedEventHandler? PropertyChanged;

        public TelemetryData? TelemetryData { get; } = telemetryData;
        public int ClearAnalysisRangeCallCount { get; private set; }
        public TelemetryTimeRange? AnalysisRange
        {
            get => analysisRange;
            private set
            {
                if (analysisRange == value)
                {
                    return;
                }

                analysisRange = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AnalysisRange)));
            }
        }
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

        public void SetAnalysisRange(double startSeconds, double endSeconds)
        {
            AnalysisRange = TelemetryTimeRange.TryCreate(startSeconds, endSeconds, out var range)
                ? range
                : null;
        }

        public void ClearAnalysisRange()
        {
            ClearAnalysisRangeCallCount++;
            AnalysisRange = null;
        }

        public void SetAnalysisRangeBoundaryFromMarker(double markerSeconds) { }
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