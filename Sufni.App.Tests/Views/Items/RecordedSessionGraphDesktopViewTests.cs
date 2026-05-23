using System;
using System.Collections.Generic;
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
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Views.Controls;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;
using static Sufni.App.Tests.Infrastructure.TestTelemetryData;

namespace Sufni.App.Tests.Views.Items;

public class RecordedSessionGraphDesktopViewTests
{
    [AvaloniaFact]
    public async Task RecordedSessionGraphDesktopView_HidesImuRegion_WhenTelemetryHasNoImuData()
    {
        var workspace = new RecordedSessionGraphWorkspaceStub(TestTelemetryData.CreateProcessed());

        await using var mounted = await MountAsync(workspace);

        var travelView = GetNamedVisual<TravelPlotDesktopView>(mounted.View, "Travel");
        var velocityView = GetNamedVisual<VelocityPlotDesktopView>(mounted.View, "Velocity");
        var imuView = GetNamedVisual<ImuPlotDesktopView>(mounted.View, "Imu");
        var root = GetGraphRoot(mounted.View);
        var travelRow = GetBaseRow(root, "Travel (mm)");
        var velocityRow = GetChildRow(travelRow, "Velocity (m/s)");
        var imuRow = GetBaseRow(root, "Vibration RMS (g)");

        Assert.NotNull(travelView);
        Assert.NotNull(velocityView);
        Assert.NotNull(imuView);

        Assert.Same(workspace.TelemetryData, travelView!.Telemetry);
        Assert.Same(workspace.Timeline, travelView.Timeline);

        Assert.Same(workspace.TelemetryData, velocityView!.Telemetry);
        Assert.Same(workspace.Timeline, velocityView.Timeline);

        Assert.Same(workspace.TelemetryData, imuView!.Telemetry);
        Assert.Same(workspace.Timeline, imuView.Timeline);

        Assert.True(travelRow.IsVisible);
        Assert.True(velocityRow.IsVisible);
        Assert.False(imuRow.IsVisible);
    }

    [AvaloniaFact]
    public async Task RecordedSessionGraphDesktopView_HidesTravelRegion_WhenTelemetryHasNoTravelData()
    {
        var telemetry = CreateWithImu();
        telemetry.Front.Present = false;
        telemetry.Rear.Present = false;
        telemetry.Front.Travel = [];
        telemetry.Rear.Travel = [];
        telemetry.Front.Velocity = [];
        telemetry.Rear.Velocity = [];
        var workspace = new RecordedSessionGraphWorkspaceStub(telemetry);

        await using var mounted = await MountAsync(workspace);

        var travelView = GetNamedVisual<TravelPlotDesktopView>(mounted.View, "Travel");
        var velocityView = GetNamedVisual<VelocityPlotDesktopView>(mounted.View, "Velocity");
        var imuView = GetNamedVisual<ImuPlotDesktopView>(mounted.View, "Imu");
        var root = GetGraphRoot(mounted.View);
        var travelRow = GetBaseRow(root, "Travel (mm)");
        var velocityRow = GetChildRow(travelRow, "Velocity (m/s)");
        var imuRow = GetBaseRow(root, "Vibration RMS (g)");

        Assert.NotNull(travelView);
        Assert.NotNull(velocityView);
        Assert.NotNull(imuView);
        Assert.False(travelRow.IsVisible);
        Assert.False(velocityRow.IsVisible);
        Assert.True(imuRow.IsVisible);
    }

    [AvaloniaFact]
    public async Task RecordedSessionGraphDesktopView_HidesVelocityRegion_WhenVelocityStateHidden()
    {
        var telemetry = TestTelemetryData.CreateProcessed();
        telemetry.ImuData = TestTelemetryData.CreateWithImu().ImuData;
        var workspace = new RecordedSessionGraphWorkspaceStub(
            telemetry,
            velocityGraphState: SurfacePresentationState.Hidden);

        await using var mounted = await MountAsync(workspace);

        var root = GetGraphRoot(mounted.View);
        var travelRow = GetBaseRow(root, "Travel (mm)");
        var velocityRow = GetChildRow(travelRow, "Velocity (m/s)");
        var imuRow = GetBaseRow(root, "Vibration RMS (g)");

        Assert.True(travelRow.IsVisible);
        Assert.False(velocityRow.IsVisible);
        Assert.True(imuRow.IsVisible);
    }

    [AvaloniaFact]
    public async Task RecordedSessionGraphDesktopView_ShowsImuRegion_WhenTelemetryHasImuData()
    {
        var telemetry = TestTelemetryData.CreateProcessed();
        telemetry.ImuData = TestTelemetryData.CreateWithImu().ImuData;
        var workspace = new RecordedSessionGraphWorkspaceStub(telemetry);

        await using var mounted = await MountAsync(workspace);

        var imuView = GetNamedVisual<ImuPlotDesktopView>(mounted.View, "Imu");
        var root = GetGraphRoot(mounted.View);
        var imuRow = GetBaseRow(root, "Vibration RMS (g)");

        Assert.NotNull(imuView);
        Assert.True(imuView!.IsVisible);
        Assert.True(imuRow.IsVisible);
    }

    [AvaloniaFact]
    public async Task RecordedSessionGraphDesktopView_ShowsGpsBaseRow_WhenImuIsHidden()
    {
        var workspace = new RecordedSessionGraphWorkspaceStub(
            TestTelemetryData.CreateProcessed(),
            speedGraphState: SurfacePresentationState.Ready);

        await using var mounted = await MountAsync(workspace);

        var root = GetGraphRoot(mounted.View);
        var travelRow = GetBaseRow(root, "Travel (mm)");
        var imuRow = GetBaseRow(root, "Vibration RMS (g)");
        var gpsRow = GetBaseRow(root, "GPS speed (km/h)");
        var elevationRow = GetChildRow(gpsRow, "Elevation (m)");
        var speedView = GetNamedVisual<TrackSignalPlotDesktopView>(mounted.View, "Speed");

        Assert.NotNull(speedView);
        Assert.True(travelRow.IsVisible);
        Assert.False(imuRow.IsVisible);
        Assert.True(gpsRow.IsVisible);
        Assert.False(elevationRow.IsVisible);
    }

    [AvaloniaFact]
    public async Task RecordedSessionGraphDesktopView_UsesNestedTelemetryRows()
    {
        var telemetry = TestTelemetryData.CreateProcessed();
        telemetry.ImuData = TestTelemetryData.CreateWithImu().ImuData;
        var workspace = new RecordedSessionGraphWorkspaceStub(
            telemetry,
            speedGraphState: SurfacePresentationState.Ready);

        await using var mounted = await MountAsync(workspace);

        var root = GetGraphRoot(mounted.View);
        Assert.Equal(
            ["Travel (mm)", "Vibration RMS (g)", "GPS speed (km/h)"],
            root.Rows.Select(row => row.Title!).ToArray());

        var travelRow = GetBaseRow(root, "Travel (mm)");
        var imuRow = GetBaseRow(root, "Vibration RMS (g)");
        var gpsRow = GetBaseRow(root, "GPS speed (km/h)");

        Assert.Equal(["Velocity (m/s)"], travelRow.ChildRows.Select(row => row.Title!).ToArray());
        Assert.Equal(["Frame pitch/roll (deg)"], imuRow.ChildRows.Select(row => row.Title!).ToArray());
        Assert.Equal(["Elevation (m)"], gpsRow.ChildRows.Select(row => row.Title!).ToArray());
    }

    [AvaloniaFact]
    public async Task RecordedSessionGraphDesktopView_AppliesStoredGraphPreferences()
    {
        var telemetry = TestTelemetryData.CreateProcessed();
        telemetry.ImuData = TestTelemetryData.CreateWithImu().ImuData;
        var workspace = new RecordedSessionGraphWorkspaceStub(
            telemetry,
            speedGraphState: SurfacePresentationState.Ready)
        {
            GraphPreferences = new SessionGraphPreferences(
            [
                new SessionGraphRowPreferences(
                    TelemetryGraphRowIds.Imu,
                    isExpanded: false,
                    children:
                    [
                        new SessionGraphRowPreferences(TelemetryGraphRowIds.Velocity),
                    ]),
                new SessionGraphRowPreferences(TelemetryGraphRowIds.Travel),
            ]),
        };

        await using var mounted = await MountAsync(workspace);

        var root = GetGraphRoot(mounted.View);
        Assert.Equal(
            ["Vibration RMS (g)", "Travel (mm)", "GPS speed (km/h)"],
            root.Rows.Select(row => row.Title!).ToArray());
        Assert.False(root.Rows[0].IsExpanded);
        Assert.Equal(["Velocity (m/s)", "Frame pitch/roll (deg)"], root.Rows[0].ChildRows.Select(row => row.Title!).ToArray());
        Assert.Equal(["Elevation (m)"], root.Rows[2].ChildRows.Select(row => row.Title!).ToArray());
    }

    [AvaloniaFact]
    public async Task RecordedSessionGraphDesktopView_AnalysisRangeBindingKeepsAndClearsOverlayOnEveryPlot()
    {
        var telemetry = TestTelemetryData.CreateProcessed();
        telemetry.ImuData = TestTelemetryData.CreateWithImu().ImuData;
        var workspace = new RecordedSessionGraphWorkspaceStub(
            telemetry,
            pitchRollGraphState: SurfacePresentationState.Ready,
            speedGraphState: SurfacePresentationState.Ready,
            elevationGraphState: SurfacePresentationState.Ready);

        await using var mounted = await MountAsync(workspace);

        var travelView = GetNamedVisual<TravelPlotDesktopView>(mounted.View, "Travel");
        var velocityView = GetNamedVisual<VelocityPlotDesktopView>(mounted.View, "Velocity");
        var imuView = GetNamedVisual<ImuPlotDesktopView>(mounted.View, "Imu");
        var pitchRollView = GetNamedVisual<FramePitchRollPlotDesktopView>(mounted.View, "PitchRoll");
        var speedView = GetNamedVisual<TrackSignalPlotDesktopView>(mounted.View, "Speed");
        var elevationView = GetNamedVisual<TrackSignalPlotDesktopView>(mounted.View, "Elevation");
        Assert.NotNull(travelView);
        Assert.NotNull(velocityView);
        Assert.NotNull(imuView);
        Assert.NotNull(pitchRollView);
        Assert.NotNull(speedView);
        Assert.NotNull(elevationView);
        SufniTimeSeriesPlotView[] plotViews =
        [
            travelView!,
            velocityView!,
            imuView!,
            pitchRollView!,
            speedView!,
            elevationView!
        ];

        workspace.SetAnalysisRange(0.25, 0.75);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.NotNull(workspace.AnalysisRange);
        foreach (var plotView in plotViews)
        {
            Assert.Equal(workspace.AnalysisRange, plotView.AnalysisRange);
            var plot = Assert.Single(plotView.GetVisualDescendants().OfType<AvaPlot>());
            Assert.True(plot.Bounds.Width > 0);
            Assert.True(plot.Bounds.Height > 0);
            var visibleSpan = Assert.Single(plot.Plot.PlottableList.OfType<HorizontalSpan>(), span => span.IsVisible);
            Assert.Equal(workspace.AnalysisRange!.Value.StartSeconds, visibleSpan.X1, 3);
            Assert.Equal(workspace.AnalysisRange.Value.EndSeconds, visibleSpan.X2, 3);
        }

        workspace.ClearAnalysisRange();
        await ViewTestHelpers.FlushDispatcherAsync();

        foreach (var plotView in plotViews)
        {
            Assert.Null(plotView.AnalysisRange);
            var plot = Assert.Single(plotView.GetVisualDescendants().OfType<AvaPlot>());
            Assert.DoesNotContain(plot.Plot.PlottableList.OfType<HorizontalSpan>(), span => span.IsVisible);
        }
    }

    [AvaloniaFact]
    public async Task RecordedSessionGraphDesktopView_VelocityPlotClick_ClearsAnalysisRange()
    {
        var workspace = new RecordedSessionGraphWorkspaceStub(CreateMinimal());

        await using var mounted = await MountAsync(workspace);

        var velocityView = GetNamedVisual<VelocityPlotDesktopView>(mounted.View, "Velocity");
        Assert.NotNull(velocityView);
        var plot = Assert.Single(velocityView!.GetVisualDescendants().OfType<AvaPlot>());
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

    private static TelemetryPlotsRoot GetGraphRoot(RecordedSessionGraphDesktopView view)
    {
        var root = view.GetVisualDescendants()
            .OfType<TelemetryPlotsRoot>()
            .SingleOrDefault(root => root.Name == "GraphRoot");
        Assert.NotNull(root);
        return root!;
    }

    private static T GetNamedVisual<T>(Control root, string name)
        where T : Control
    {
        var rowsView = Assert.Single(root.GetVisualDescendants().OfType<RecordedSessionGraphRowsView>());
        var visual = rowsView.FindControl<T>(name);
        Assert.NotNull(visual);
        return visual!;
    }

    private static TelemetryPlotRow GetBaseRow(TelemetryPlotsRoot root, string title)
        => Assert.Single(root.Rows, row => row.Title == title);

    private static TelemetryPlotRow GetChildRow(TelemetryPlotRow row, string title)
        => Assert.Single(row.ChildRows, child => child.Title == title);

    private sealed class RecordedSessionGraphWorkspaceStub(
        TelemetryData telemetryData,
        SurfacePresentationState? travelGraphState = null,
        SurfacePresentationState? velocityGraphState = null,
        SurfacePresentationState? imuGraphState = null,
        SurfacePresentationState? pitchRollGraphState = null,
        SurfacePresentationState? speedGraphState = null,
        SurfacePresentationState? elevationGraphState = null) :
        IRecordedSessionGraphWorkspace,
        INotifyPropertyChanged
    {
        private TelemetryTimeRange? analysisRange;

        public event PropertyChangedEventHandler? PropertyChanged;

        public TelemetryData? TelemetryData { get; } = telemetryData;
        public int ClearAnalysisRangeCallCount { get; private set; }
        public int SetAnalysisRangeBoundaryCallCount { get; private set; }
        public double? LastAnalysisRangeBoundary { get; private set; }
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
        public SurfacePresentationState TravelGraphState => travelGraphState ?? CreateTravelState(TelemetryData);
        public SurfacePresentationState VelocityGraphState => velocityGraphState ?? TravelGraphState;
        public SurfacePresentationState ImuGraphState => imuGraphState ?? CreateImuState(TelemetryData);
        public SurfacePresentationState PitchRollGraphState { get; } = pitchRollGraphState ?? SurfacePresentationState.Hidden;
        public IReadOnlyList<TrackPoint>? TrackPoints { get; } =
        [
            new TrackPoint(0, 0, 0, 100, 5),
            new TrackPoint(1, 1, 1, 101, 6),
        ];
        public TrackTimeRange? TrackTimelineContext { get; } = new(0, 1);
        public SurfacePresentationState SpeedGraphState { get; } = speedGraphState ?? SurfacePresentationState.Hidden;
        public SurfacePresentationState ElevationGraphState { get; } = elevationGraphState ?? SurfacePresentationState.Hidden;
        public SessionPlotPreferences PlotPreferences { get; } = new();
        public SessionGraphPreferences GraphPreferences { get; set; } = SessionGraphPreferences.Default;
        public TelemetrySourceVisibilityStore SourceVisibility { get; } = new();
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

        public void SetAnalysisRangeBoundary(double boundarySeconds)
        {
            SetAnalysisRangeBoundaryCallCount++;
            LastAnalysisRangeBoundary = boundarySeconds;
        }

        private static SurfacePresentationState CreateTravelState(TelemetryData? telemetry)
        {
            return telemetry is { } value && (value.Front.Present || value.Rear.Present)
                ? SurfacePresentationState.Ready
                : SurfacePresentationState.Hidden;
        }

        private static SurfacePresentationState CreateImuState(TelemetryData? telemetry)
        {
            return telemetry?.ImuData is { } imuData &&
                   imuData.Records.Count > 0 &&
                   imuData.ActiveLocations.Count > 0
                ? SurfacePresentationState.Ready
                : SurfacePresentationState.Hidden;
        }
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
