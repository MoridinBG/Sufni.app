using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Svg.Skia;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Runtime.Presentation;

using Sufni.App.Acquisition.Models;
using Sufni.App.Extensibility.Views;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Sessions.Signals.ViewModels.SessionPages;
using Sufni.App.Sessions.Signals.Views.SessionPages;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.ExtensionHost.Contracts.Capabilities;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.MapsAndTracks.Views;
using Sufni.App.Sessions.Media.Views.Controls;
using Sufni.App.Shared.Views.Overlays;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Extensions;

namespace Sufni.App.Tests.Sessions.Signals.Views.SessionPages;

[Collection("Ui")]
public class RecordedSignalsPageViewTests
{
    [AvaloniaFact]
    public async Task RecordedSignalsPageView_AppliesStoredSignalLayoutPreferences()
    {
        var telemetry = TestTelemetryData.CreateProcessed();
        telemetry.ImuData = TestTelemetryData.CreateWithImu().ImuData;
        var workspace = new RecordedSignalsPageWorkspaceStub(
            telemetry,
            SurfacePresentationState.Ready,
            SurfacePresentationState.Ready,
            speedSignalState: SurfacePresentationState.Ready)
        {
            SignalLayoutPreferences = new SignalLayoutPreferences(
            [
                new SignalLayoutRowPreferences(SignalRowIds.Speed, isExpanded: false),
                new SignalLayoutRowPreferences(
                    SignalRowIds.Imu,
                    children:
                    [
                        new SignalLayoutRowPreferences(SignalRowIds.Velocity),
                    ]),
            ]),
        };
        var page = new RecordedSignalsPageViewModel(workspace, CreateMediaWorkspace([]));

        await using var mounted = await MountAsync(page);

        var root = GetSignalRowsRoot(mounted.View);
        Assert.Equal(
            ["GPS speed (km/h)", "Vibration RMS (g)", "Travel (mm)"],
            root.Rows.Select(row => row.Title!).ToArray());
        Assert.False(root.Rows[0].IsExpanded);
        Assert.Equal(["Elevation (m)"], root.Rows[0].ChildRows.Select(row => row.Title!).ToArray());
        Assert.Equal(["Velocity (m/s)", "Frame pitch/roll (deg)"], root.Rows[1].ChildRows.Select(row => row.Title!).ToArray());
    }

    [AvaloniaFact]
    public async Task RecordedSignalsPageView_ShowsNoSignalsDataFallback_WhenBothStatesHidden()
    {
        var workspace = new RecordedSignalsPageWorkspaceStub(
            null,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden);

        var page = new RecordedSignalsPageViewModel(workspace, CreateMediaWorkspace([]));

        await using var mounted = await MountAsync(page);

        var fallback = mounted.View.FindControl<SurfacePlaceholderCard>("NoSignalsDataPlaceholder");
        var root = GetSignalRowsRoot(mounted.View);

        Assert.NotNull(fallback);
        Assert.True(fallback!.IsVisible);
        Assert.All(GetRows(root), row => Assert.False(row.IsVisible));
    }

    [AvaloniaFact]
    public async Task RecordedSignalsPageView_HidesNoSignalsDataPlaceholder_WhenOnlyVelocitySignalIsHidden()
    {
        var telemetry = TestTelemetryData.CreateProcessed();
        telemetry.ImuData = TestTelemetryData.CreateWithImu().ImuData;
        var workspace = new RecordedSignalsPageWorkspaceStub(
            telemetry,
            SurfacePresentationState.Ready,
            SurfacePresentationState.Ready,
            velocitySignalState: SurfacePresentationState.Hidden);
        var page = new RecordedSignalsPageViewModel(workspace, CreateMediaWorkspace([]));

        await using var mounted = await MountAsync(page);

        var fallback = mounted.View.FindControl<SurfacePlaceholderCard>("NoSignalsDataPlaceholder");

        Assert.NotNull(fallback);
        Assert.False(fallback!.IsVisible);
    }

    [AvaloniaFact]
    public async Task RecordedSignalsPageView_RendersMapBelowSignals_WhenMapReady()
    {
        var signalsWorkspace = new RecordedSignalsPageWorkspaceStub(
            TestTelemetryData.CreateProcessed(),
            SurfacePresentationState.Ready,
            SurfacePresentationState.Hidden);
        var mediaWorkspace = CreateMediaWorkspace(
        [
            new TrackPoint(0, 0, 0, null),
            new TrackPoint(1, 100, 100, null),
        ]);

        var page = new RecordedSignalsPageViewModel(signalsWorkspace, mediaWorkspace);

        await using var mounted = await MountAsync(page);

        var mapHost = mounted.View.FindControl<PlaceholderOverlayContainer>("MapHost");
        var mapView = mounted.View.GetVisualDescendants().OfType<MapView>().Single();

        Assert.NotNull(mapHost);
        Assert.True(mapHost!.IsVisible);
        Assert.True(mapView.IsVisible);
        Assert.Same(mediaWorkspace.MapViewModel, mapView.DataContext);
        Assert.Same(mediaWorkspace.ExtensionSlots, mapView.ExtensionSlots);
        Assert.Same(mediaWorkspace.Timeline, mapView.Timeline);
        Assert.NotNull(mapView.FindControl<ComboBox>("TileProviderComboBox"));
    }

    [AvaloniaFact]
    public async Task RecordedSignalsPageView_RendersToolbarContributions()
    {
        var signalsWorkspace = new RecordedSignalsPageWorkspaceStub(
            TestTelemetryData.CreateProcessed(),
            SurfacePresentationState.Ready,
            SurfacePresentationState.Hidden);
        var command = new RelayCommand(() => { });
        signalsWorkspace.ExtensionSlots.SignalToolbarCommands.Add(new RecordedSessionToolbarCommandContribution(
            "extension",
            "toolbar-command",
            Order: 0,
            RecordedSessionToolbarZone.Leading,
            "Match",
            new ToolbarIconDescriptor("/Assets/fa-link.svg", Width: 17, Height: 19),
            command));
        var leadingViewModel = new TestContributionViewModel
        {
            Content = new TextBlock { Name = "MobileToolbarLeadingAction", Text = "Leading" },
        };
        var trailingViewModel = new TestContributionViewModel
        {
            Content = new TextBlock { Name = "MobileToolbarTrailingAction", Text = "Trailing" },
        };
        signalsWorkspace.ExtensionSlots.SignalToolbarViews.Add(new RecordedSessionToolbarViewContribution(
            "extension",
            "toolbar-leading",
            Order: 1,
            RecordedSessionToolbarZone.Leading,
            leadingViewModel));
        signalsWorkspace.ExtensionSlots.SignalToolbarViews.Add(new RecordedSessionToolbarViewContribution(
            "extension",
            "toolbar-trailing",
            Order: 1,
            RecordedSessionToolbarZone.Trailing,
            trailingViewModel));
        var page = new RecordedSignalsPageViewModel(signalsWorkspace, CreateMediaWorkspace([]));

        await using var mounted = await MountAsync(page);

        var toolbarHost = Assert.Single(
            mounted.View.GetVisualDescendants().OfType<RecordedSessionToolbarContributionsView>());
        Assert.NotNull(toolbarHost);
        var leadingCommandBar = toolbarHost.FindControl<CommandBar>("LeadingSignalToolbarCommandBar");
        Assert.NotNull(leadingCommandBar);
        var button = Assert.Single(leadingCommandBar.PrimaryCommands.OfType<CommandBarButton>(), button => button.Label == "Match");
        Assert.Equal("Match", button.Label);
        Assert.Same(command, button.Command);
        var icon = Assert.IsType<Image>(button.Icon);
        Assert.Equal(17, icon.Width);
        Assert.Equal(19, icon.Height);
        var svgImage = Assert.IsType<SvgImage>(icon.Source);
        Assert.NotNull(svgImage.Source?.Picture);
        AssertCommandBarContent(toolbarHost, "LeadingSignalToolbarViewsHost", leadingViewModel.Content!);
        AssertCommandBarContent(toolbarHost, "TrailingSignalToolbarViewsHost", trailingViewModel.Content!);
    }

    [AvaloniaFact]
    public async Task RecordedSignalsPageView_InsertsHostedRowsBeforeBuiltInChildRows()
    {
        var signalsWorkspace = new RecordedSignalsPageWorkspaceStub(
            TestTelemetryData.CreateProcessed(),
            SurfacePresentationState.Ready,
            SurfacePresentationState.Hidden);
        var rowTarget = RecordedSessionSignalRowTarget.Extension("extension", "hosted-row");
        signalsWorkspace.ExtensionSlots.HostedSignalRows.Add(new RecordedSessionHostedSignalRowContribution(
            "extension",
            "hosted-row",
            Order: 0,
            ParentRow: RecordedSessionBuiltInSignalRow.Travel,
            RowTarget: rowTarget,
            Title: "Hosted row",
            SurfacePresentationState.Ready,
            new TestContributionViewModel
            {
                Content = new TextBlock { Name = "HostedRowContent", Text = "Hosted row content" },
            },
            IsInitiallyExpanded: true));
        var page = new RecordedSignalsPageViewModel(signalsWorkspace, CreateMediaWorkspace([]));

        await using var mounted = await MountAsync(page);

        var root = GetSignalRowsRoot(mounted.View);
        var travelRow = GetBaseRow(root, "Travel (mm)");
        Assert.Equal(
            ["Hosted row", "Velocity (m/s)"],
            travelRow.ChildRows.Select(row => row.Title!).ToArray());
        AssertContributionText(mounted.View, "HostedRowContent", "Hosted row content");
    }

    [AvaloniaFact]
    public async Task RecordedSignalsPageView_RendersMediaPaneContributions()
    {
        var signalsWorkspace = new RecordedSignalsPageWorkspaceStub(
            TestTelemetryData.CreateProcessed(),
            SurfacePresentationState.Ready,
            SurfacePresentationState.Hidden);
        var mediaWorkspace = CreateMediaWorkspace([]);
        mediaWorkspace.ExtensionSlots.MediaPanes.Add(new RecordedSessionMediaPaneContribution(
            "extension",
            "media-pane",
            Order: 0,
            new TestContributionViewModel
            {
                Content = new TextBlock { Name = "MobileMediaPane", Text = "Media pane" },
            }));
        var page = new RecordedSignalsPageViewModel(signalsWorkspace, mediaWorkspace);

        await using var mounted = await MountAsync(page);

        var mediaPanes = Assert.Single(
            mounted.View.GetVisualDescendants().OfType<RecordedSessionMediaPanesView>());
        Assert.NotNull(mediaPanes);
        AssertContributionText(mounted.View, "MobileMediaPane", "Media pane");
    }

    private static async Task<MountedRecordedSignalsPageView> MountAsync(RecordedSignalsPageViewModel page)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsurePlotViewStyle();

        var view = new RecordedSignalsPageView
        {
            DataContext = page,
        };

        var host = await ViewTestHelpers.ShowViewAsync(new ScrollViewer { Content = view });
        return new MountedRecordedSignalsPageView(host, view);
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

    private static void AssertCommandBarContent(
        RecordedSessionToolbarContributionsView host,
        string contentHostName,
        object expectedContent)
    {
        var contentHost = host.FindControl<StackPanel>(contentHostName);
        var contentControl = Assert.Single(contentHost!.Children.OfType<ContentControl>());
        Assert.Same(expectedContent, contentControl.Content);
    }

    private static SignalRowsRoot GetSignalRowsRoot(RecordedSignalsPageView view)
    {
        var root = view.GetVisualDescendants()
            .OfType<SignalRowsRoot>()
            .SingleOrDefault(root => root.Name == "SignalRowsRoot");
        Assert.NotNull(root);
        return root!;
    }

    private static SignalRow GetBaseRow(SignalRowsRoot root, string title)
        => Assert.Single(root.Rows, row => row.Title == title);

    private static SignalRow GetChildRow(SignalRow row, string title)
        => Assert.Single(row.ChildRows, child => child.Title == title);

    private static IReadOnlyList<SignalRow> GetRows(SignalRowsRoot root)
        => root.Rows.Concat(root.Rows.SelectMany(row => row.ChildRows)).ToArray();

    private static SessionMediaWorkspaceStub CreateMediaWorkspace(IReadOnlyList<TrackPoint> trackPoints)
    {
        var tileLayerService = Substitute.For<ITileLayerService>().WithDefaultSelectedLayerChanges();
        tileLayerService.AvailableLayers.Returns(new ObservableCollection<TileLayerConfig>());

        var mapViewModel = new MapViewModel(tileLayerService, Substitute.For<IDialogService>(), new InlineUiThreadDispatcher())
        {
            FullTrackPoints = [],
            SessionTrackPoints = trackPoints.ToList(),
        };

        return new SessionMediaWorkspaceStub(mapViewModel);
    }

    private sealed class RecordedSignalsPageWorkspaceStub(
        TelemetryData? telemetryData,
        SurfacePresentationState travelSignalState,
        SurfacePresentationState imuSignalState,
        SurfacePresentationState? pitchRollSignalState = null,
        SurfacePresentationState? velocitySignalState = null,
        SurfacePresentationState? speedSignalState = null,
        SurfacePresentationState? elevationSignalState = null,
        TelemetryTimeRange? analysisRange = null) : IRecordedSessionSignalsWorkspace
    {
        public TelemetryData? TelemetryData { get; } = telemetryData;
        public TelemetryTimeRange? AnalysisRange { get; private set; } = analysisRange;
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
        public IReadOnlyList<TrackPoint>? TrackPoints { get; } =
        [
            new TrackPoint(0, 0, 0, 100, 5),
            new TrackPoint(1, 100, 100, 101, 6),
        ];
        public TrackTimeRange? TrackTimelineContext { get; } = new(0, 1);
        public SurfacePresentationState TravelSignalState { get; } = travelSignalState;
        public SurfacePresentationState VelocitySignalState { get; } = velocitySignalState ?? travelSignalState;
        public SurfacePresentationState ImuSignalState { get; } = imuSignalState;
        public SurfacePresentationState PitchRollSignalState { get; } = pitchRollSignalState ?? SurfacePresentationState.Hidden;
        public SurfacePresentationState SpeedSignalState { get; } = speedSignalState ?? SurfacePresentationState.Hidden;
        public SurfacePresentationState ElevationSignalState { get; } = elevationSignalState ?? SurfacePresentationState.Hidden;
        public SignalDisplayPreferences SignalDisplayPreferences { get; } = new();
        public SignalLayoutPreferences SignalLayoutPreferences { get; set; } = SignalLayoutPreferences.Default;
        public TelemetrySourceVisibilityStore SourceVisibility { get; } = new();
        public SessionTimelineLinkViewModel Timeline { get; } = new();
        public RecordedSessionExtensionSlots ExtensionSlots { get; } = new();
        public IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> SignalPlotContextMenuActionsBySignalRowId { get; } =
            new Dictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>();

        public void SetAnalysisRange(double startSeconds, double endSeconds)
        {
            AnalysisRange = TelemetryTimeRange.TryCreate(startSeconds, endSeconds, out var range)
                ? range
                : null;
        }

        public void ClearAnalysisRange()
        {
            AnalysisRange = null;
        }

        public void SetAnalysisRangeBoundary(double boundarySeconds) { }
    }

    private sealed class SessionMediaWorkspaceStub : ISessionMediaWorkspace
    {
        private readonly MapViewModel mapViewModel;

        public SessionMediaWorkspaceStub(MapViewModel mapViewModel)
        {
            this.mapViewModel = mapViewModel;
        }

        public bool HasMediaContent => MapState.ReservesLayout;
        public MapViewModel? MapViewModel => mapViewModel;
        public SurfacePresentationState MapState => mapViewModel.SessionTrackPoints?.Count > 0
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState MediaPaneState => SurfacePresentationState.Hidden;
        public SessionTimelineLinkViewModel Timeline { get; } = new();
        public RecordedSessionExtensionSlots ExtensionSlots { get; } = new();
        public double? MediaColumnWidth => 400;
        public string? MediaUrl => null;
    }
}

internal sealed record MountedRecordedSignalsPageView(Window Host, RecordedSignalsPageView View) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
