using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Sufni.App.Plots;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using Sufni.App.DesktopViews.Items;
using Sufni.App.Views.Plots;
using Sufni.App.ExtensionHost.RecordedSessions;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Views.Controls;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;
using static Sufni.App.Tests.Infrastructure.TestTelemetryData;
using Sufni.App.ExtensionHost.Models;
using Sufni.App.ExtensionHost.Plots;
using Sufni.App.ExtensionHost.Presentation;
using Sufni.App.ExtensionHost.ViewModels.Editors;
using Sufni.App.ExtensionHost.Views.Controls;

namespace Sufni.App.Tests.Views.Items;

public class RecordedSessionGraphDesktopViewTests
{
    [AvaloniaFact]
    public async Task RecordedSessionGraphDesktopView_RendersToolbarContributions()
    {
        var workspace = new RecordedSessionGraphWorkspaceStub(CreateMinimal());
        workspace.ExtensionSlots.GraphToolbarActions.Add(new RecordedSessionToolbarContribution(
            "extension",
            "toolbar-leading",
            Order: 0,
            RecordedSessionToolbarZone.Leading,
            new TestContributionViewModel
            {
                Content = new TextBlock { Name = "DesktopToolbarLeadingAction", Text = "Leading" },
            }));
        workspace.ExtensionSlots.GraphToolbarActions.Add(new RecordedSessionToolbarContribution(
            "extension",
            "toolbar-trailing",
            Order: 1,
            RecordedSessionToolbarZone.Trailing,
            new TestContributionViewModel
            {
                Content = new TextBlock { Name = "DesktopToolbarTrailingAction", Text = "Trailing" },
            }));

        await using var mounted = await MountAsync(workspace);

        var toolbarHost = Assert.Single(
            mounted.View.GetVisualDescendants().OfType<RecordedSessionToolbarContributionsView>());
        Assert.NotNull(toolbarHost);
        AssertContributionText(mounted.View, "DesktopToolbarLeadingAction", "Leading");
        AssertContributionText(mounted.View, "DesktopToolbarTrailingAction", "Trailing");
    }

    [AvaloniaFact]
    public async Task RecordedSessionGraphDesktopView_RendersPlotRowExtensionContributions()
    {
        var workspace = new RecordedSessionGraphWorkspaceStub(CreateMinimal());
        var hostedContent = new HostedGraphRowContent("Hosted row");
        var hostedRowTarget = RecordedSessionGraphRowTarget.Extension("extension", "hosted-row");
        var headerAction = new TelemetryPlotRowAction
        {
            Id = "ExtensionVelocityAction",
            Kind = TelemetryPlotRowActionKind.Execute,
            IconPathData = "M0 0L12 0L12 12L0 12Z",
            ToolTip = "Extension action",
        };
        var contextAction = new TelemetryPlotContextMenuAction(
            "extension_context",
            "Extension context",
            new TestCommand());
        var overlayRegistration = new RecordedTimeRangeOverlaySetRegistration(
            "extension_range",
            new RecordedTimeRangeOverlaySet(
                [new RecordedTimeRangeOverlay(0.2, 0.4)],
                new RecordedTimeRangeOverlayStyle(
                    new RecordedTimeRangeOverlayColor(255, 100, 149, 237),
                    new RecordedTimeRangeOverlayColor(0, 0, 0, 0),
                    0)),
            IsVisible: true);

        workspace.ExtensionSlots.PlotRowHeaderActions.Add(new RecordedSessionPlotRowActionContribution(
            "extension",
            "velocity-action",
            Order: 0,
            RecordedSessionGraphRowTarget.BuiltIn(RecordedSessionBuiltInGraphRow.Velocity),
            headerAction));
        workspace.ExtensionSlots.PlotContextMenuActions.Add(new RecordedSessionPlotContextMenuContribution(
            "extension",
            "travel-context",
            Order: 0,
            RecordedSessionBuiltInGraphRow.Travel,
            contextAction));
        workspace.ExtensionSlots.HostedGraphRows.Add(new RecordedSessionHostedGraphRowContribution(
            "extension",
            "hosted-row",
            Order: 0,
            ParentRow: RecordedSessionBuiltInGraphRow.Travel,
            RowTarget: hostedRowTarget,
            Title: "Extension row",
            SurfacePresentationState.Ready,
            hostedContent,
            IsInitiallyExpanded: true));
        workspace.ExtensionSlots.TimeRangeOverlays.Add(new RecordedSessionTimeRangeOverlayContribution(
            "extension",
            "travel-range",
            Order: 0,
            RecordedSessionGraphRowTarget.BuiltIn(RecordedSessionBuiltInGraphRow.Travel),
            overlayRegistration));

        await using var mounted = await MountAsync(workspace);

        var velocityRow = Assert.Single(
            mounted.View.GetVisualDescendants().OfType<TelemetryPlotRow>(),
            row => row.RowId == TelemetryGraphRowIds.Velocity);
        Assert.Contains(headerAction, velocityRow.HeaderActions!);
        var hostedRow = Assert.Single(
            mounted.View.GetVisualDescendants().OfType<TelemetryPlotRow>(),
            row => row.RowId == hostedRowTarget.StableKey);
        Assert.Equal("Extension row", hostedRow.Title);
        var hostedPlotContent = Assert.IsType<ContentControl>(hostedRow.PlotContent);
        Assert.Same(hostedContent, hostedPlotContent.Content);
        var graphRoot = Assert.Single(mounted.View.GetVisualDescendants().OfType<TelemetryPlotsRoot>());
        Assert.DoesNotContain(hostedRowTarget.StableKey, FlattenRowIds(graphRoot.CaptureGraphPreferences().Rows));

        var travelView = GetNamedVisual<TravelPlotView>(mounted.View, "Travel");
        Assert.Same(contextAction, Assert.Single(travelView.AdditionalContextMenuActions!));
        Assert.Same(overlayRegistration, Assert.Single(travelView.TimeRangeOverlays!));

        var plot = Assert.Single(travelView.GetVisualDescendants().OfType<AvaPlot>());
        Assert.Contains(
            plot.Plot.PlottableList.OfType<HorizontalSpan>(),
            span => span.IsVisible && Math.Abs(span.X1 - 0.2) < 0.001 && Math.Abs(span.X2 - 0.4) < 0.001);
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

        var travelView = GetNamedVisual<TravelPlotView>(mounted.View, "Travel");
        var velocityView = GetNamedVisual<VelocityPlotView>(mounted.View, "Velocity");
        var imuView = GetNamedVisual<ImuPlotView>(mounted.View, "Imu");
        var pitchRollView = GetNamedVisual<FramePitchRollPlotView>(mounted.View, "PitchRoll");
        var speedView = GetNamedVisual<TrackSignalPlotView>(mounted.View, "Speed");
        var elevationView = GetNamedVisual<TrackSignalPlotView>(mounted.View, "Elevation");
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

        var velocityView = GetNamedVisual<VelocityPlotView>(mounted.View, "Velocity");
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

    private static void AssertContributionText(Control root, string name, string text)
    {
        var textBlocks = root.GetVisualDescendants()
            .OfType<TextBlock>()
            .ToArray();
        var textBlock = textBlocks.SingleOrDefault(textBlock => textBlock.Name == name);
        Assert.True(
            textBlock is not null,
            $"Expected contribution text '{name}'. Actual text blocks: {string.Join(", ", textBlocks.Select(block => $"{block.Name}:{block.Text}"))}");
        Assert.Equal(text, textBlock!.Text);
    }

    private static T GetNamedVisual<T>(Control root, string name)
        where T : Control
    {
        var rowsView = Assert.Single(root.GetVisualDescendants().OfType<RecordedSessionGraphRowsView>());
        var visual = rowsView.FindControl<T>(name);
        Assert.NotNull(visual);
        return visual!;
    }

    private static IEnumerable<string> FlattenRowIds(IEnumerable<SessionGraphRowPreferences> rows)
    {
        foreach (var row in rows)
        {
            yield return row.RowId;
            foreach (var childRowId in FlattenRowIds(row.Children))
            {
                yield return childRowId;
            }
        }
    }

    private sealed record HostedGraphRowContent(string Text) : IRecordedSessionHostedGraphRowContributionViewModel;

    private sealed class TestCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
        }
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
        public IReadOnlyList<TelemetryHighlightRange> StatisticsSelectionHighlightRanges { get; } = [];
        public bool HasStatisticsSelection => false;
        public bool ShowStatisticsSelection => false;
        public bool ShowVelocityStatisticsSelection => false;
        public bool ShowImuStatisticsSelection => false;
        public bool ShowPitchRollStatisticsSelection => false;
        public bool ShowSpeedStatisticsSelection => false;
        public bool ShowElevationStatisticsSelection => false;
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
        public RecordedSessionExtensionSlots ExtensionSlots { get; } = new();
        public IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> PlotContextMenuActionsByRowId { get; } =
            new Dictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>();

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
