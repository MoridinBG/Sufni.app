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

    private static T GetNamedVisual<T>(Control root, string name)
        where T : Control
    {
        var rowsView = Assert.Single(root.GetVisualDescendants().OfType<RecordedSessionGraphRowsView>());
        var visual = rowsView.FindControl<T>(name);
        Assert.NotNull(visual);
        return visual!;
    }

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
        public bool ShowAirtime => true;
        public bool ShowVelocityAirtime => false;
        public bool ShowImuAirtime => false;
        public bool ShowPitchRollAirtime => false;
        public bool ShowSpeedAirtime => false;
        public bool ShowElevationAirtime => false;
        public IReadOnlyList<TelemetryPlotRowAction> TravelHeaderActions { get; } = [];
        public IReadOnlyList<TelemetryPlotRowAction> VelocityHeaderActions { get; } = [];
        public IReadOnlyList<TelemetryPlotRowAction> ImuHeaderActions { get; } = [];
        public IReadOnlyList<TelemetryPlotRowAction> PitchRollHeaderActions { get; } = [];
        public IReadOnlyList<TelemetryPlotRowAction> SpeedHeaderActions { get; } = [];
        public IReadOnlyList<TelemetryPlotRowAction> ElevationHeaderActions { get; } = [];
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
