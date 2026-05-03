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
        var firstSplitter = mounted.View.FindControl<GridSplitter>("FirstGraphSplitter");
        var secondSplitter = mounted.View.FindControl<GridSplitter>("SecondGraphSplitter");
        var graphGrid = mounted.View.FindControl<Grid>("GraphGrid");

        Assert.NotNull(travelView);
        Assert.NotNull(velocityView);
        Assert.NotNull(imuView);
        Assert.NotNull(firstSplitter);
        Assert.NotNull(secondSplitter);
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

        Assert.True(firstSplitter!.IsVisible);
        Assert.False(secondSplitter!.IsVisible);
        Assert.Equal(graphGrid!.RowDefinitions[0].Height.Value, graphGrid.RowDefinitions[2].Height.Value);
        Assert.Equal(GridUnitType.Star, graphGrid.RowDefinitions[0].Height.GridUnitType);
        Assert.Equal(GridUnitType.Star, graphGrid.RowDefinitions[2].Height.GridUnitType);
        Assert.Equal(0, graphGrid!.RowDefinitions[4].Height.Value);
        Assert.Equal(GridUnitType.Pixel, graphGrid.RowDefinitions[4].Height.GridUnitType);
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
        var firstSplitter = mounted.View.FindControl<GridSplitter>("FirstGraphSplitter");
        var secondSplitter = mounted.View.FindControl<GridSplitter>("SecondGraphSplitter");
        var graphGrid = mounted.View.FindControl<Grid>("GraphGrid");

        Assert.NotNull(travelView);
        Assert.NotNull(velocityView);
        Assert.NotNull(imuView);
        Assert.NotNull(firstSplitter);
        Assert.NotNull(secondSplitter);
        Assert.NotNull(graphGrid);
        Assert.NotEqual(0, graphGrid!.RowDefinitions[0].Height.Value);
        Assert.Equal(GridUnitType.Star, graphGrid.RowDefinitions[0].Height.GridUnitType);
        Assert.Equal(0, graphGrid.RowDefinitions[2].Height.Value);
        Assert.Equal(GridUnitType.Pixel, graphGrid.RowDefinitions[2].Height.GridUnitType);
        Assert.False(firstSplitter!.IsVisible);
        Assert.False(secondSplitter!.IsVisible);
    }

    [AvaloniaFact]
    public async Task RecordedSessionGraphDesktopView_HidesVelocityRegion_WhenVelocityStateHidden()
    {
        var telemetry = TestTelemetryData.Create();
        telemetry.ImuData = TestTelemetryFactories.CreateTelemetryDataWithImu().ImuData;
        var workspace = new RecordedSessionGraphWorkspaceStub(
            telemetry,
            velocityGraphState: SurfacePresentationState.Hidden);

        await using var mounted = await MountAsync(workspace);

        var firstSplitter = mounted.View.FindControl<GridSplitter>("FirstGraphSplitter");
        var secondSplitter = mounted.View.FindControl<GridSplitter>("SecondGraphSplitter");
        var graphGrid = mounted.View.FindControl<Grid>("GraphGrid");

        Assert.NotNull(firstSplitter);
        Assert.NotNull(secondSplitter);
        Assert.NotNull(graphGrid);
        Assert.NotEqual(0, graphGrid!.RowDefinitions[0].Height.Value);
        Assert.NotEqual(0, graphGrid.RowDefinitions[2].Height.Value);
        Assert.Equal(0, graphGrid.RowDefinitions[4].Height.Value);
        Assert.Equal(graphGrid.RowDefinitions[0].Height.Value, graphGrid.RowDefinitions[2].Height.Value);
        Assert.Equal(GridUnitType.Star, graphGrid.RowDefinitions[0].Height.GridUnitType);
        Assert.Equal(GridUnitType.Star, graphGrid.RowDefinitions[2].Height.GridUnitType);
        Assert.True(firstSplitter!.IsVisible);
        Assert.False(secondSplitter!.IsVisible);
    }

    [AvaloniaFact]
    public async Task RecordedSessionGraphDesktopView_ShowsImuRegion_WhenTelemetryHasImuData()
    {
        var telemetry = TestTelemetryData.Create();
        telemetry.ImuData = TestTelemetryFactories.CreateTelemetryDataWithImu().ImuData;
        var workspace = new RecordedSessionGraphWorkspaceStub(telemetry);

        await using var mounted = await MountAsync(workspace);

        var imuView = mounted.View.FindControl<ImuPlotDesktopView>("Imu");
        var secondSplitter = mounted.View.FindControl<GridSplitter>("SecondGraphSplitter");

        Assert.NotNull(imuView);
        Assert.NotNull(secondSplitter);
        Assert.True(imuView!.IsVisible);
        Assert.True(secondSplitter!.IsVisible);
    }

    [AvaloniaFact]
    public async Task RecordedSessionGraphDesktopView_ShowsSplitterBetweenVelocityAndSpeed_WhenImuIsHidden()
    {
        var workspace = new RecordedSessionGraphWorkspaceStub(
            TestTelemetryData.Create(),
            speedGraphState: SurfacePresentationState.Ready);

        await using var mounted = await MountAsync(workspace);

        var graphGrid = mounted.View.FindControl<Grid>("GraphGrid");
        var firstSplitter = mounted.View.FindControl<GridSplitter>("FirstGraphSplitter");
        var secondSplitter = mounted.View.FindControl<GridSplitter>("SecondGraphSplitter");
        var thirdSplitter = mounted.View.FindControl<GridSplitter>("ThirdGraphSplitter");
        var speedView = mounted.View.FindControl<TrackSignalPlotDesktopView>("Speed");

        Assert.NotNull(graphGrid);
        Assert.NotNull(firstSplitter);
        Assert.NotNull(secondSplitter);
        Assert.NotNull(thirdSplitter);
        Assert.NotNull(speedView);
        var speedHost = speedView!.GetVisualAncestors().OfType<PlaceholderOverlayContainer>().First();
        Assert.Equal(4, Grid.GetRow(speedHost));
        Assert.True(firstSplitter!.IsVisible);
        Assert.True(secondSplitter!.IsVisible);
        Assert.False(thirdSplitter!.IsVisible);
        Assert.Equal(GridUnitType.Star, graphGrid!.RowDefinitions[0].Height.GridUnitType);
        Assert.Equal(GridUnitType.Star, graphGrid.RowDefinitions[2].Height.GridUnitType);
        Assert.Equal(GridUnitType.Star, graphGrid.RowDefinitions[4].Height.GridUnitType);
    }

    [AvaloniaFact]
    public async Task RecordedSessionGraphDesktopView_ResetVisiblePlotRows_MakesVisibleRowsEqualHeight()
    {
        var telemetry = TestTelemetryData.Create();
        telemetry.ImuData = TestTelemetryFactories.CreateTelemetryDataWithImu().ImuData;
        var workspace = new RecordedSessionGraphWorkspaceStub(
            telemetry,
            speedGraphState: SurfacePresentationState.Ready);

        await using var mounted = await MountAsync(workspace);

        var graphGrid = mounted.View.FindControl<Grid>("GraphGrid");
        Assert.NotNull(graphGrid);

        graphGrid!.RowDefinitions[0].Height = new GridLength(4, GridUnitType.Star);
        graphGrid.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
        graphGrid.RowDefinitions[4].Height = new GridLength(2, GridUnitType.Star);
        graphGrid.RowDefinitions[6].Height = new GridLength(5, GridUnitType.Star);
        graphGrid.RowDefinitions[8].Height = new GridLength(7, GridUnitType.Star);

        SessionGraphGridSizing.ResetVisiblePlotRows(graphGrid);

        Assert.Equal(new GridLength(1, GridUnitType.Star), graphGrid.RowDefinitions[0].Height);
        Assert.Equal(new GridLength(1, GridUnitType.Star), graphGrid.RowDefinitions[2].Height);
        Assert.Equal(new GridLength(1, GridUnitType.Star), graphGrid.RowDefinitions[4].Height);
        Assert.Equal(new GridLength(1, GridUnitType.Star), graphGrid.RowDefinitions[6].Height);
        Assert.Equal(new GridLength(0, GridUnitType.Pixel), graphGrid.RowDefinitions[8].Height);
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

    private sealed class RecordedSessionGraphWorkspaceStub(
        TelemetryData telemetryData,
        SurfacePresentationState? travelGraphState = null,
        SurfacePresentationState? velocityGraphState = null,
        SurfacePresentationState? imuGraphState = null,
        SurfacePresentationState? speedGraphState = null,
        SurfacePresentationState? elevationGraphState = null) :
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
        public SurfacePresentationState TravelGraphState => travelGraphState ?? CreateTravelState(TelemetryData);
        public SurfacePresentationState VelocityGraphState => velocityGraphState ?? TravelGraphState;
        public SurfacePresentationState ImuGraphState => imuGraphState ?? CreateImuState(TelemetryData);
        public IReadOnlyList<TrackPoint>? TrackPoints { get; } =
        [
            new TrackPoint(0, 0, 0, 100, 5),
            new TrackPoint(1, 1, 1, 101, 6),
        ];
        public TrackTimeRange? TrackTimelineContext { get; } = new(0, 1);
        public SurfacePresentationState SpeedGraphState { get; } = speedGraphState ?? SurfacePresentationState.Hidden;
        public SurfacePresentationState ElevationGraphState { get; } = elevationGraphState ?? SurfacePresentationState.Hidden;
        public SessionGraphLayout GraphLayout => SessionGraphLayout.Create(
            TravelGraphState,
            VelocityGraphState,
            ImuGraphState,
            SpeedGraphState,
            ElevationGraphState);
        public SessionPlotPreferences PlotPreferences { get; } = new();
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
