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
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.Telemetry;
using AvaloniaColor = Avalonia.Media.Color;
using static Sufni.App.Tests.TestSupport.Fixtures.TestTelemetryData;
using static Sufni.App.Tests.TestSupport.Fixtures.PlotTestHelpers;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Runtime.Presentation;

using Sufni.App.Acquisition.Models;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Sessions.Plots.Views.Plots;
using Sufni.App.Shell.Behaviors;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Sessions.Plots.Views.Plots;

[Collection("Ui")]
public class TravelPlotViewTests
{
    [AvaloniaFact]
    public async Task TravelPlotView_StartsEmpty_BeforeTelemetryIsAssigned()
    {
        var view = new TravelPlotView
        {
            ShowAirtime = true,
        };

        Assert.Null(view.MaximumDisplayHz);

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Empty(plot.Plot.PlottableList);
    }

    [AvaloniaFact]
    public async Task TravelPlotView_LoadsSignalsFromTelemetryProperty()
    {
        var view = new TravelPlotView
        {
            ShowAirtime = true,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = CreateMinimal();
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Empty(plot.Plot.Axes.Title.Label.Text);
        var signals = plot.Plot.PlottableList.OfType<Signal>().ToArray();
        Assert.Single(plot.Plot.PlottableList.OfType<VerticalLine>());
        Assert.Equal(2, signals.Length);
        Assert.All(signals, signal => Assert.Same(plot.Plot.Axes.Left, signal.Axes.YAxis));
        Assert.True(plot.Plot.Axes.Right.IsVisible);
        Assert.Equal(plot.Plot.Axes.Left.Min, plot.Plot.Axes.Right.Min, precision: 6);
        Assert.Equal(plot.Plot.Axes.Left.Max, plot.Plot.Axes.Right.Max, precision: 6);
    }

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
        telemetry.Front.Present = false;
        telemetry.Rear.Present = false;

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = telemetry;
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Empty(plot.Plot.Axes.Title.Label.Text);
        Assert.Empty(plot.Plot.PlottableList.OfType<Signal>());
        Assert.Single(plot.Plot.PlottableList.OfType<Text>());
        Assert.Equal(plot.Plot.Axes.Left.Min, plot.Plot.Axes.Right.Min, precision: 6);
        Assert.Equal(plot.Plot.Axes.Left.Max, plot.Plot.Axes.Right.Max, precision: 6);
    }

    [AvaloniaFact]
    public async Task TravelPlotView_AppliesPlotBackgroundProperties()
    {
        var view = new TravelPlotView
        {
            PlotFigureBackground = AvaloniaColor.Parse("#101820"),
            PlotDataBackground = AvaloniaColor.Parse("#203040"),
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = CreateMinimal();
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Equal(Color.FromHex("#101820"), plot.Plot.FigureBackground.Color);
        Assert.Equal(Color.FromHex("#203040"), plot.Plot.DataBackground.Color);

        view.PlotFigureBackground = AvaloniaColor.Parse("#111213");
        view.PlotDataBackground = AvaloniaColor.Parse("#212223");
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(Color.FromHex("#111213"), plot.Plot.FigureBackground.Color);
        Assert.Equal(Color.FromHex("#212223"), plot.Plot.DataBackground.Color);
    }

    [AvaloniaFact]
    public async Task TravelPlotView_AnalysisRangeUpdatesOverlayWithoutReloadingSignals()
    {
        var view = new TravelPlotView
        {
            ShowAirtime = true,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = CreateMinimal();
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        var originalSignals = plot.Plot.PlottableList.OfType<Signal>().ToArray();

        view.AnalysisRange = new TelemetryTimeRange(0.25, 0.75);
        await ViewTestHelpers.FlushDispatcherAsync();

        var updatedSignals = plot.Plot.PlottableList.OfType<Signal>().ToArray();
        var selectedSpan = Assert.Single(plot.Plot.PlottableList.OfType<HorizontalSpan>());
        Assert.Same(originalSignals[0], updatedSignals[0]);
        Assert.Same(originalSignals[1], updatedSignals[1]);
        Assert.Equal(0.25, selectedSpan.X1, 3);
        Assert.Equal(0.75, selectedSpan.X2, 3);

        view.AnalysisRange = null;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(selectedSpan.IsVisible);
    }

    [AvaloniaFact]
    public async Task TravelPlotView_CullsCollidingAirtimeLabelsAfterTelemetryLoad()
    {
        var view = new TravelPlotView
        {
            ShowAirtime = true,
        };
        var telemetry = CreateMinimal(duration: 10);
        telemetry.Airtimes =
        [
            new Airtime { Start = 1.90, End = 2.10 },
            new Airtime { Start = 1.75, End = 2.25 },
        ];

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = telemetry;
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Equal([false, true], GetAirtimeLabels(plot.Plot).Select(label => label.IsVisible).ToArray());
    }

    [AvaloniaFact]
    public async Task TravelPlotView_ShowAirtime_ForwardsVisibilityToLoadedPlot()
    {
        var view = new TravelPlotView
        {
            ShowAirtime = true,
        };
        var telemetry = CreateMinimal(duration: 10);
        telemetry.Airtimes =
        [
            new Airtime { Start = 1.8, End = 2.2 },
            new Airtime { Start = 7.8, End = 8.2 },
        ];

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
        Assert.Equal([true, true], GetAirtimeLabels(plot.Plot).Select(label => label.IsVisible).ToArray());
    }

    [AvaloniaFact]
    public async Task TravelPlotView_ShowAirtimeFalseBeforeLoad_HidesAirtimeAfterTelemetryLoads()
    {
        var view = new TravelPlotView
        {
            ShowAirtime = false,
        };
        var telemetry = CreateMinimal(duration: 10);
        telemetry.Airtimes = [new Airtime { Start = 1.8, End = 2.2 }];

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = telemetry;
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.All(plot.Plot.PlottableList.OfType<HorizontalSpan>(), span => Assert.False(span.IsVisible));
        Assert.All(GetAirtimeLabels(plot.Plot), label => Assert.False(label.IsVisible));
    }

    [AvaloniaFact]
    public async Task VelocityPlotView_AirtimeDefaultsHiddenAndCanBeShown()
    {
        var view = new VelocityPlotView();
        var telemetry = CreateMinimal(duration: 10);
        telemetry.Airtimes = [new Airtime { Start = 1.8, End = 2.2 }];

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = telemetry;
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        var span = Assert.Single(plot.Plot.PlottableList.OfType<HorizontalSpan>());
        Assert.False(span.IsVisible);

        view.ShowAirtime = true;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(span.IsVisible);
    }

    [AvaloniaFact]
    public async Task TravelPlotView_DefersLatestTelemetryReloadUntilEffectivelyVisible()
    {
        var view = new TravelPlotView();
        var container = new Border { Child = view };
        var oldTelemetry = CreateMinimal();
        oldTelemetry.Markers = [new MarkerData(0.5)];
        var freshTelemetry = CreateMinimal();
        freshTelemetry.Markers = [new MarkerData(0.25), new MarkerData(1.5)];

        await using var mounted = await PlotViewTestSupport.MountAsync(container);

        view.Telemetry = oldTelemetry;
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Equal(2, plot.Plot.PlottableList.OfType<VerticalLine>().Count());

        container.IsVisible = false;
        view.Telemetry = null;
        view.Telemetry = freshTelemetry;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(2, plot.Plot.PlottableList.OfType<VerticalLine>().Count());

        container.IsVisible = true;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(3, plot.Plot.PlottableList.OfType<VerticalLine>().Count());
    }

    [AvaloniaFact]
    public async Task TravelPlotView_TimelineSyncConstrainsOverscrolledFullRange()
    {
        var timeline = new SessionTimelineLinkViewModel();
        var view = new TestableTravelPlotView
        {
            Timeline = timeline,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = CreateMinimal(duration: 10);
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        plot.Plot.Axes.SetLimitsX(-2, 8);
        mounted.View.UpdateTimelineRangeForTest();

        Assert.Equal(0, timeline.VisibleRangeStart, 6);
        Assert.Equal(1, timeline.VisibleRangeEnd, 6);
    }

    [AvaloniaFact]
    public async Task TravelPlotView_ContextMenuContext_UsesRowIdClickSecondsAndAnalysisRange()
    {
        var telemetry = CreateMinimal(duration: 10);
        var analysisRange = new TelemetryTimeRange(2, 4);
        var view = new ContextMenuTravelPlotView
        {
            Telemetry = telemetry,
            SignalsWorkspace = new RecordedSessionSignalsWorkspaceStub(telemetry),
            SignalRowId = SignalRowIds.Travel,
            AnalysisRange = analysisRange,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        RenderPlotInMemory(plot);
        var pixel = GetDataAreaPixelAtX(plot.Plot, 3);

        var context = view.CreateContextForTest(pixel);

        Assert.NotNull(context);
        Assert.Equal(SignalRowIds.Travel, context.RowId);
        Assert.Equal(3, context.ClickSeconds, 1);
        Assert.Equal(10, context.DurationSeconds, 6);
        Assert.Equal(analysisRange, context.AnalysisRange);
        Assert.True(context.IsClickInsideAnalysisRange);
    }

    [AvaloniaFact]
    public async Task TravelPlotView_ContextMenuActions_ResolveFromWorkspaceRowId()
    {
        var telemetry = CreateMinimal(duration: 10);
        var action = new TelemetryPlotContextMenuAction("test", "Test", Substitute.For<ICommand>());
        var workspace = new RecordedSessionSignalsWorkspaceStub(
            telemetry,
            new Dictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>
            {
                [SignalRowIds.Travel] = [action],
            });
        var view = new ContextMenuTravelPlotView
        {
            Telemetry = telemetry,
            SignalsWorkspace = workspace,
            SignalRowId = SignalRowIds.Travel,
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        var context = new TelemetryPlotContextMenuContext(SignalRowIds.Travel, 3, 10, null);
        var actions = view.GetActionsForTest(context);

        Assert.Same(action, Assert.Single(actions));
    }

    [AvaloniaTheory]
    [InlineData(MobileMenuGesture.SecondaryPointer)]
    [InlineData(MobileMenuGesture.LongPress)]
    [InlineData(MobileMenuGesture.LongPressInsideAnalysisRange)]
    public async Task TravelPlotView_MobileMenuGestures_ShowInstalledPlotMenu(MobileMenuGesture gesture)
    {
        using var input = TestApp.UseTouchInput();
        var telemetry = CreateMinimal(duration: 10);
        var workspace = new RecordedSessionSignalsWorkspaceStub(telemetry);
        var view = CreateMobileContextMenuView(gesture, telemetry, workspace);

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

    private sealed class TestableTravelPlotView : TravelPlotView
    {
        public void UpdateTimelineRangeForTest()
        {
            UpdateTimelineRange();
        }
    }

    private sealed class ContextMenuTravelPlotView : TravelPlotView
    {
        private Func<Pixel, TelemetryPlotContextMenuContext?>? createContext;
        private Func<TelemetryPlotContextMenuContext, IReadOnlyList<TelemetryPlotContextMenuAction>>? getActions;

        public TelemetryPlotContextMenuContext? CreateContextForTest(Pixel pixel)
        {
            return createContext?.Invoke(pixel);
        }

        public IReadOnlyList<TelemetryPlotContextMenuAction> GetActionsForTest(TelemetryPlotContextMenuContext context)
        {
            return getActions?.Invoke(context) ?? [];
        }

        protected override IPlotMenu CreateTelemetryPlotContextMenu(
            Func<Pixel, TelemetryPlotContextMenuContext?> createContext,
            Func<TelemetryPlotContextMenuContext, IReadOnlyList<TelemetryPlotContextMenuAction>> getActions)
        {
            this.createContext = createContext;
            this.getActions = getActions;
            return new NoOpPlotMenu();
        }
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

    private static Pixel GetDataAreaPixelAtX(Plot plot, double x)
    {
        var dataRect = plot.LastRender.DataRect;
        Assert.True(dataRect.HasArea);
        return new Pixel(plot.GetPixel(new Coordinates(x, 0)).X, dataRect.Center.Y);
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
        public RecordedSessionExtensionSlots ExtensionSlots { get; } = new();
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
