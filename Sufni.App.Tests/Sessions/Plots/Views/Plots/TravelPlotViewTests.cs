using Sufni.App.Sessions.Signals.Views.Controls;
using Sufni.App.Sessions.Models;
using Sufni.App.Shared.Plots;
using System;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using NSubstitute;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Shared.Views.Plots;
using Sufni.Telemetry;
using static Sufni.App.Tests.TestSupport.Fixtures.TestTelemetryData;
using static Sufni.App.Tests.TestSupport.Fixtures.PlotTestHelpers;

using Sufni.App.Acquisition.Models;
using Sufni.App.Infrastructure;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Sessions.Plots.Views.Plots;
using Sufni.App.Shell.Behaviors;
using Sufni.App.Shared.Views.Input;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Sessions.Plots.Views.Plots;

[Collection("Ui")]
public class TravelPlotViewTests
{
    [AvaloniaFact]
    public async Task TravelPlotView_ClickingLegendEntry_HidesSource()
    {
        var view = new TravelPlotView
        {
            SourceVisibility = new TelemetrySourceVisibilityStore(),
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = CreateMinimal();
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        var rear = Assert.Single(plot.Plot.PlottableList.OfType<Signal>(), signal => signal.LegendText == "Rear");
        var clickPoint = plot.TranslatePoint(GetLegendItemCenter(plot, rear), mounted.Host);
        Assert.NotNull(clickPoint);

        mounted.Host.MouseDown(clickPoint.Value, MouseButton.Left, RawInputModifiers.None);
        mounted.Host.MouseUp(clickPoint.Value, MouseButton.Left, RawInputModifiers.None);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(rear.IsVisible);
    }

    [AvaloniaFact]
    public async Task TravelPlotView_ShowsEmptyState_WhenTelemetryHasNoTravelData()
    {
        var view = new TravelPlotView
        {
            ShowAirtime = true,
        };
        var telemetry = CreateMinimal();
        telemetry = telemetry with { Front = telemetry.Front with { Present = false } };
        telemetry = telemetry with { Rear = telemetry.Rear with { Present = false } };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = CreateMinimal();
        await ViewTestHelpers.FlushDispatcherAsync();
        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.NotEmpty(plot.Plot.PlottableList.OfType<Signal>());

        view.Telemetry = telemetry;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Empty(plot.Plot.PlottableList.OfType<Signal>());
        Assert.True(Assert.Single(plot.Plot.PlottableList.OfType<Text>()).IsVisible);
    }

    [AvaloniaFact]
    public async Task TravelPlotView_AnalysisRangeTogglesOverlayWithoutChangingViewport()
    {
        var view = new TravelPlotView
        {
            ShowAirtime = true,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = CreateMinimal();
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        var initialLimits = plot.Plot.Axes.GetLimits();

        view.AnalysisRange = new TelemetryTimeRange(0.25, 0.75);
        await ViewTestHelpers.FlushDispatcherAsync();

        var selectedSpan = Assert.Single(plot.Plot.PlottableList.OfType<HorizontalSpan>());
        AssertAxisLimitsEqual(initialLimits, plot.Plot.Axes.GetLimits());
        Assert.Equal(0.25, selectedSpan.X1, 3);
        Assert.Equal(0.75, selectedSpan.X2, 3);

        view.AnalysisRange = null;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(selectedSpan.IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TravelPlotView_ShowAirtime_ForwardsVisibilityToLoadedPlot(bool velocity)
    {
        SufniTelemetryPlotView view = velocity ? new VelocityPlotView() : new TravelPlotView();
        view.ShowAirtime = true;
        var telemetry = CreateMinimal(duration: 10);
        telemetry = telemetry with { Airtimes = [
            new Airtime { Start = 1.8, End = 2.2 },
            new Airtime { Start = 7.8, End = 8.2 },
        ] };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = telemetry;
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.All(plot.Plot.PlottableList.OfType<HorizontalSpan>(), span => Assert.True(span.IsVisible));

        view.ShowAirtime = false;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.All(plot.Plot.PlottableList.OfType<HorizontalSpan>(), span => Assert.False(span.IsVisible));
        Assert.All(GetAirtimeLabels(plot.Plot), label => Assert.False(label.IsVisible));

        view.ShowAirtime = true;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.All(plot.Plot.PlottableList.OfType<HorizontalSpan>(), span => Assert.True(span.IsVisible));
        Assert.Equal(2, plot.Plot.PlottableList.OfType<HorizontalSpan>().Count());
        if (!velocity) Assert.Equal([true, true], GetAirtimeLabels(plot.Plot).Select(label => label.IsVisible).ToArray());
    }

    [AvaloniaFact]
    public async Task TravelPlotView_ShowAirtimeFalseBeforeLoad_HidesAirtimeAfterTelemetryLoads()
    {
        var view = new TravelPlotView
        {
            ShowAirtime = false,
        };
        var telemetry = CreateMinimal(duration: 10);
        telemetry = telemetry with { Airtimes = [new Airtime { Start = 1.8, End = 2.2 }] };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = telemetry;
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        var span = Assert.Single(plot.Plot.PlottableList.OfType<HorizontalSpan>());
        Assert.Equal(1.8, span.X1, precision: 6);
        Assert.Equal(2.2, span.X2, precision: 6);
        Assert.False(span.IsVisible);
        Assert.False(Assert.Single(GetAirtimeLabels(plot.Plot)).IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(MobileMenuGesture.SecondaryPointer)]
    [InlineData(MobileMenuGesture.LongPress)]
    [InlineData(MobileMenuGesture.LongPressInsideAnalysisRange)]
    public async Task TravelPlotView_MobileMenuGestures_ShowInstalledPlotMenu(MobileMenuGesture gesture)
    {
        var telemetry = CreateMinimal(duration: 10);
        var workspace = new RecordedSessionSignalsWorkspaceStub(telemetry);
        var view = CreateMobileContextMenuView(gesture, telemetry, workspace);
        PointerGesture.SetSupportsTouchLongPressContextMenu(view, true);

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        RenderPlotInMemory(plot);
        var point = gesture == MobileMenuGesture.LongPressInsideAnalysisRange
            ? GetDataAreaPointAtX(plot, 3)
            : GetDataAreaCenterPoint(plot);
        var pressPoint = plot.TranslatePoint(point, mounted.Host);
        Assert.True(plot.Bounds.Width > 0 && plot.Bounds.Height > 0, $"Plot bounds were {plot.Bounds}.");
        Assert.NotNull(pressPoint);

        var button = gesture == MobileMenuGesture.SecondaryPointer
            ? MouseButton.Right
            : MouseButton.Left;
        mounted.Host.MouseDown(pressPoint.Value, button, RawInputModifiers.None);
        await ViewTestHelpers.FlushDispatcherAsync();

        if (view is LongPressContextMenuTravelPlotView longPressView)
        {
            longPressView.TriggerLongPress();
            mounted.Host.MouseUp(pressPoint.Value, MouseButton.Left, RawInputModifiers.None);
            await ViewTestHelpers.FlushDispatcherAsync();
        }

        Assert.NotNull(GetPlotMenu(view).LastShowPixel);
        Assert.Equal(0, workspace.SetAnalysisRangeBoundaryCallCount);
        Assert.Equal(0, workspace.ClearAnalysisRangeCallCount);
        Assert.Null(workspace.LastAnalysisRangeBoundary);
        Assert.Equal(
            gesture == MobileMenuGesture.LongPressInsideAnalysisRange ? new TelemetryTimeRange(2, 4) : null,
            view.AnalysisRange);
    }

    private static TravelPlotView CreateMobileContextMenuView(
        MobileMenuGesture gesture,
        TelemetryData telemetry,
        RecordedSessionSignalsWorkspaceStub workspace)
    {
        return gesture == MobileMenuGesture.SecondaryPointer
            ? new MobileContextMenuTravelPlotView
            {
                Telemetry = telemetry,
                SignalsWorkspace = workspace,
                SignalRowId = SignalRowIds.Travel,
            }
            : new LongPressContextMenuTravelPlotView
            {
                Telemetry = telemetry,
                AnalysisRange = gesture == MobileMenuGesture.LongPressInsideAnalysisRange
                    ? new TelemetryTimeRange(2, 4)
                    : null,
                SignalsWorkspace = workspace,
                SignalRowId = SignalRowIds.Travel,
            };
    }

    private static NoOpPlotMenu GetPlotMenu(TravelPlotView view)
    {
        return view switch
        {
            MobileContextMenuTravelPlotView mobile => mobile.PlotMenu,
            LongPressContextMenuTravelPlotView longPress => longPress.PlotMenu,
            _ => throw new ArgumentOutOfRangeException(nameof(view)),
        };
    }

    public enum MobileMenuGesture
    {
        SecondaryPointer,
        LongPress,
        LongPressInsideAnalysisRange,
    }

    private sealed class MobileContextMenuTravelPlotView : TravelPlotView
    {
        public NoOpPlotMenu PlotMenu { get; } = new();

        protected override IPlotMenu CreateTelemetryPlotContextMenu(
            Func<Pixel, TelemetryPlotContextMenuContext?> createContext,
            Func<TelemetryPlotContextMenuContext, IReadOnlyList<TelemetryPlotContextMenuAction>> getActions)
        {
            return PlotMenu;
        }
    }

    private static Point GetDataAreaCenterPoint(ScottPlot.Avalonia.AvaPlot plot)
    {
        var dataRect = plot.Plot.LastRender.DataRect;
        var figureRect = plot.Plot.LastRender.FigureRect;
        Assert.True(dataRect.HasArea);
        Assert.True(figureRect.HasArea);

        return new Point(
            (dataRect.Center.X - figureRect.Left) / figureRect.Width * plot.Bounds.Width,
            (dataRect.Center.Y - figureRect.Top) / figureRect.Height * plot.Bounds.Height);
    }

    private static Point GetDataAreaPointAtX(ScottPlot.Avalonia.AvaPlot plot, double x)
    {
        var dataRect = plot.Plot.LastRender.DataRect;
        var figureRect = plot.Plot.LastRender.FigureRect;
        Assert.True(dataRect.HasArea);
        Assert.True(figureRect.HasArea);

        var pixel = new Pixel(plot.Plot.GetPixel(new Coordinates(x, 0)).X, dataRect.Center.Y);
        return new Point(
            (pixel.X - figureRect.Left) / figureRect.Width * plot.Bounds.Width,
            (pixel.Y - figureRect.Top) / figureRect.Height * plot.Bounds.Height);
    }

    private static void RenderPlotInMemory(ScottPlot.Avalonia.AvaPlot plot)
    {
        var width = Math.Max(1, (int)plot.Bounds.Width);
        var height = Math.Max(1, (int)plot.Bounds.Height);
        plot.Plot.RenderInMemory(width, height);
        plot.Refresh();
    }

    private sealed class NoOpPlotMenu : IPlotMenu
    {
        public List<ContextMenuItem> ContextMenuItems { get; set; } = [];
        public Pixel? LastShowPixel { get; private set; }

        public void Reset()
        {
            ContextMenuItems.Clear();
        }

        public void Clear()
        {
            ContextMenuItems.Clear();
        }

        public void Add(string Label, Action<Plot> action)
        {
            ContextMenuItems.Add(new ContextMenuItem
            {
                Label = Label,
                OnInvoke = action
            });
        }

        public void AddSeparator()
        {
            ContextMenuItems.Add(new ContextMenuItem
            {
                IsSeparator = true
            });
        }

        public void ShowContextMenu(Pixel pixel)
        {
            LastShowPixel = pixel;
        }
    }

    private sealed class LongPressContextMenuTravelPlotView : TravelPlotView
    {
        private Action? scheduledLongPress;
        public NoOpPlotMenu PlotMenu { get; } = new();

        public void TriggerLongPress()
        {
            var callback = scheduledLongPress ?? throw new InvalidOperationException("No long press was scheduled.");
            callback();
        }

        protected override IDisposable ScheduleTouchContextMenuLongPress(Action callback)
        {
            scheduledLongPress = callback;
            return new TestSubscription(() => scheduledLongPress = null);
        }

        protected override IPlotMenu CreateTelemetryPlotContextMenu(
            Func<Pixel, TelemetryPlotContextMenuContext?> createContext,
            Func<TelemetryPlotContextMenuContext, IReadOnlyList<TelemetryPlotContextMenuAction>> getActions)
        {
            return PlotMenu;
        }
    }

    private sealed class TestSubscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    private sealed class RecordedSessionSignalsWorkspaceStub(
        TelemetryData telemetryData,
        IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>? plotContextMenuActionsBySignalRowId = null)
        : IRecordedSessionSignalsWorkspace
    {
        public TelemetryData? TelemetryData { get; } = telemetryData;
        public IRecordedSessionAnalysisResultState AnalysisResultState { get; } =
            Substitute.For<IRecordedSessionAnalysisResultState>();
        public TelemetryTimeRange? AnalysisRange { get; private set; }
        public bool ShowAirtime => true;
        public bool ShowVelocityAirtime => false;
        public bool ShowImuAirtime => false;
        public bool ShowPitchRollAirtime => false;
        public bool ShowSpeedAirtime => false;
        public bool ShowElevationAirtime => false;
        public IReadOnlyList<TelemetryHighlightRange> AnalysisSelectionHighlightRanges { get; } = [];
        public bool HasAnalysisSelection => false;
        public bool ShowAnalysisSelection => false;
        public bool ShowVelocityAnalysisSelection => false;
        public bool ShowImuAnalysisSelection => false;
        public bool ShowPitchRollAnalysisSelection => false;
        public bool ShowSpeedAnalysisSelection => false;
        public bool ShowElevationAnalysisSelection => false;
        public IReadOnlyList<SignalRowAction> TravelHeaderActions { get; } = [];
        public IReadOnlyList<SignalRowAction> VelocityHeaderActions { get; } = [];
        public IReadOnlyList<SignalRowAction> ImuHeaderActions { get; } = [];
        public IReadOnlyList<SignalRowAction> PitchRollHeaderActions { get; } = [];
        public IReadOnlyList<SignalRowAction> SpeedHeaderActions { get; } = [];
        public IReadOnlyList<SignalRowAction> ElevationHeaderActions { get; } = [];
        public IReadOnlyList<TrackPoint>? TrackPoints => null;
        public TrackTimeRange? TrackTimelineContext => null;
        public SurfacePresentationState TravelSignalState => SurfacePresentationState.Ready;
        public SurfacePresentationState VelocitySignalState => SurfacePresentationState.Hidden;
        public SurfacePresentationState ImuSignalState => SurfacePresentationState.Hidden;
        public SurfacePresentationState PitchRollSignalState => SurfacePresentationState.Hidden;
        public SurfacePresentationState SpeedSignalState => SurfacePresentationState.Hidden;
        public SurfacePresentationState ElevationSignalState => SurfacePresentationState.Hidden;
        public SignalDisplayPreferences SignalDisplayPreferences { get; } = new();
        public SignalLayoutPreferences SignalLayoutPreferences { get; set; } = SignalLayoutPreferences.Default;
        public TelemetrySourceVisibilityStore SourceVisibility { get; } = new();
        public SessionTimelineLinkViewModel Timeline { get; } = new();
        public SegmentPickerViewModel SegmentPicker { get; } = TestSegmentPickers.Create();
        public SegmentComparisonViewModel SegmentComparison { get; } = TestSegmentComparisons.Create();
        public IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> SignalPlotContextMenuActionsBySignalRowId { get; } =
            plotContextMenuActionsBySignalRowId ?? new Dictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>();
        public int ClearAnalysisRangeCallCount { get; private set; }
        public int SetAnalysisRangeBoundaryCallCount { get; private set; }
        public double? LastAnalysisRangeBoundary { get; private set; }

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
    }
}
