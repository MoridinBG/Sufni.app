using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Extensibility.Views;
using Sufni.App.Sessions.Insights.ViewModels.SessionPages;
using Sufni.App.Sessions.Insights.Views.SessionPages;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Pages.Views.SessionPages;
using Sufni.App.Sessions.Analysis.Views.Controls;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Sessions.Insights.Views.SessionPages;

[Collection("Ui")]
public class MobileAnalysisPageViewTests
{
    [AvaloniaFact]
    public async Task SpringPageView_BindsTravelDistributionModeRadioButtons_WhenTelemetryIsAvailable()
    {
        var workspace = MobileAnalysisWorkspaceStub.Create(
            hasFrontAnalysis: true,
            hasRearAnalysis: true);
        var page = new SpringPageViewModel(workspace);

        await using var mounted = await MountAsync(new SpringPageView { DataContext = page });

        var selector = mounted.View.FindControl<StackPanel>("MobileTravelDistributionModeRadioButtons");
        var activeSuspension = mounted.View.FindControl<RadioButton>("MobileActiveSuspensionModeRadioButton");
        var dynamicSag = mounted.View.FindControl<RadioButton>("MobileDynamicSagModeRadioButton");

        Assert.NotNull(selector);
        Assert.True(selector!.IsVisible);
        Assert.NotNull(activeSuspension);
        Assert.NotNull(dynamicSag);
        Assert.True(activeSuspension!.IsChecked);
        Assert.False(dynamicSag!.IsChecked);

        dynamicSag.IsChecked = true;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(TravelDistributionMode.DynamicSag, workspace.SelectedTravelDistributionMode);
        Assert.False(activeSuspension.IsChecked);
        Assert.True(dynamicSag.IsChecked);
    }

    [AvaloniaFact]
    public async Task SpringPageView_RendersStatisticsBannerContributions()
    {
        var workspace = MobileAnalysisWorkspaceStub.Create(
            hasFrontAnalysis: true,
            hasRearAnalysis: true);
        workspace.ExtensionSlots.AnalysisBanners.Add(new RecordedSessionAnalysisBannerContribution(
            "extension",
            "statistics-banner",
            Order: 0,
            new TestContributionViewModel
            {
                Content = new TextBlock { Name = "MobileSpringAnalysisBanner", Text = "Analysis banner" },
            }));
        var page = new SpringPageViewModel(workspace);

        await using var mounted = await MountAsync(new SpringPageView { DataContext = page });

        var contributionHost = Assert.Single(
            mounted.View.GetVisualDescendants().OfType<RecordedSessionAnalysisContributionsView>());
        Assert.NotNull(contributionHost);
        AssertContributionText(mounted.View, "MobileSpringAnalysisBanner", "Analysis banner");
    }

    [AvaloniaFact]
    public async Task SpringPageView_ConstrainsAnalysisContentToMobileViewport()
    {
        var workspace = MobileAnalysisWorkspaceStub.Create(
            hasFrontAnalysis: true,
            hasRearAnalysis: true);
        workspace.ExtensionSlots.AnalysisBanners.Add(new RecordedSessionAnalysisBannerContribution(
            "extension",
            "wide-statistics-banner",
            Order: 0,
            new TestContributionViewModel
            {
                Content = new TextBlock
                {
                    Name = "WideMobileSpringAnalysisBanner",
                    Text = string.Join(' ', Enumerable.Repeat("wide extension analysis banner text", 8)),
                },
            }));
        var page = new SpringPageViewModel(workspace);

        await using var mounted = await MountAsync(new SpringPageView { DataContext = page }, viewportWidth: 390);

        var scrollViewer = mounted.View.FindControl<ScrollViewer>("PageScrollViewer");
        var content = mounted.View.FindControl<StackPanel>("MobileSpringAnalysisContent");
        var travelHosts = mounted.View.GetVisualDescendants().OfType<TravelAnalysisHost>().ToArray();

        Assert.NotNull(scrollViewer);
        Assert.NotNull(content);
        Assert.Equal(ScrollBarVisibility.Disabled, scrollViewer!.HorizontalScrollBarVisibility);
        Assert.True(scrollViewer.ClipToBounds);
        Assert.True(content!.ClipToBounds);
        Assert.InRange(scrollViewer.Bounds.Width, 389, 391);
        Assert.InRange(content.Bounds.Width, 389, 391);
        Assert.NotEmpty(travelHosts);
        Assert.All(travelHosts, host => Assert.True(
            host.Bounds.Width <= scrollViewer.Bounds.Width + 0.5,
            $"Expected travel statistics host width {host.Bounds.Width} to fit viewport width {scrollViewer.Bounds.Width}."));
    }

    [AvaloniaFact]
    public async Task DampingPageView_BindsVelocityAverageModeRadioButtons_WhenTelemetryIsAvailable()
    {
        var workspace = MobileAnalysisWorkspaceStub.Create(
            hasFrontAnalysis: true,
            hasRearAnalysis: true);
        var page = new DampingPageViewModel(workspace);

        await using var mounted = await MountAsync(new DampingPageView { DataContext = page });

        var selector = mounted.View.FindControl<StackPanel>("MobileVelocityAverageModeRadioButtons");
        var sampleAveraged = mounted.View.FindControl<RadioButton>("MobileSampleAveragedModeRadioButton");
        var strokePeakAveraged = mounted.View.FindControl<RadioButton>("MobileStrokePeakAveragedModeRadioButton");

        Assert.NotNull(selector);
        Assert.True(selector!.IsVisible);
        Assert.NotNull(sampleAveraged);
        Assert.NotNull(strokePeakAveraged);
        Assert.True(sampleAveraged!.IsChecked);
        Assert.False(strokePeakAveraged!.IsChecked);

        strokePeakAveraged.IsChecked = true;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(VelocityAverageMode.StrokePeakAveraged, workspace.SelectedVelocityAverageMode);
        Assert.False(sampleAveraged.IsChecked);
        Assert.True(strokePeakAveraged.IsChecked);
    }

    [AvaloniaFact]
    public async Task BalancePageView_BindsBalanceDisplacementModeRadioButtons_WhenTelemetryIsAvailable()
    {
        var workspace = MobileAnalysisWorkspaceStub.Create(
            hasFrontAnalysis: true,
            hasRearAnalysis: true);
        var page = new BalancePageViewModel(workspace);

        await using var mounted = await MountAsync(new BalancePageView { DataContext = page });

        var selector = mounted.View.FindControl<StackPanel>("MobileBalanceDisplacementModeRadioButtons");
        var zenith = mounted.View.FindControl<RadioButton>("MobileZenithModeRadioButton");
        var travel = mounted.View.FindControl<RadioButton>("MobileTravelModeRadioButton");
        var speed = mounted.View.FindControl<RadioButton>("MobileSpeedModeRadioButton");

        Assert.NotNull(selector);
        Assert.True(selector!.IsVisible);
        Assert.NotNull(zenith);
        Assert.NotNull(travel);
        Assert.NotNull(speed);
        Assert.True(zenith!.IsChecked);
        Assert.False(travel!.IsChecked);
        Assert.False(speed!.IsChecked);

        speed.IsChecked = true;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(BalanceDisplacementMode.Speed, workspace.SelectedBalanceDisplacementMode);
        Assert.False(zenith.IsChecked);
        Assert.False(travel.IsChecked);
        Assert.True(speed.IsChecked);
    }

    [AvaloniaFact]
    public async Task BalancePageView_BindsBalanceSpeedModeRadioButtons_WhenTelemetryIsAvailable()
    {
        var workspace = MobileAnalysisWorkspaceStub.Create(
            hasFrontAnalysis: true,
            hasRearAnalysis: true);
        var page = new BalancePageViewModel(workspace);

        await using var mounted = await MountAsync(new BalancePageView { DataContext = page });

        var selector = mounted.View.FindControl<StackPanel>("MobileBalanceSpeedModeRadioButtons");
        var both = mounted.View.FindControl<RadioButton>("MobileBothSpeedModeRadioButton");
        var low = mounted.View.FindControl<RadioButton>("MobileLowSpeedModeRadioButton");
        var high = mounted.View.FindControl<RadioButton>("MobileHighSpeedModeRadioButton");

        Assert.NotNull(selector);
        Assert.True(selector!.IsVisible);
        Assert.NotNull(both);
        Assert.NotNull(low);
        Assert.NotNull(high);
        Assert.True(both!.IsChecked);
        Assert.False(low!.IsChecked);
        Assert.False(high!.IsChecked);

        high.IsChecked = true;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(BalanceSpeedMode.HighSpeed, workspace.SelectedBalanceSpeedMode);
        Assert.False(both.IsChecked);
        Assert.False(low.IsChecked);
        Assert.True(high.IsChecked);
    }

    [AvaloniaFact]
    public async Task StrokesPageView_SideSelector_ShowsOneSuspensionSideAtATime()
    {
        var workspace = MobileAnalysisWorkspaceStub.Create(
            hasFrontAnalysis: true,
            hasRearAnalysis: true);
        var page = new StrokesPageViewModel(workspace);

        await using var mounted = await MountAsync(new StrokesPageView { DataContext = page });

        var selector = mounted.View.FindControl<StackPanel>("MobileStrokesSideRadioButtons");
        var frontSide = mounted.View.FindControl<RadioButton>("MobileFrontStrokesSideRadioButton");
        var rearSide = mounted.View.FindControl<RadioButton>("MobileRearStrokesSideRadioButton");
        var frontPanel = mounted.View.FindControl<Grid>("FrontStrokesPanel");
        var rearPanel = mounted.View.FindControl<Grid>("RearStrokesPanel");

        Assert.NotNull(selector);
        Assert.NotNull(frontSide);
        Assert.NotNull(rearSide);
        Assert.NotNull(frontPanel);
        Assert.NotNull(rearPanel);
        Assert.True(selector!.IsVisible);
        Assert.True(frontSide!.IsChecked);
        Assert.False(rearSide!.IsChecked);
        Assert.True(frontPanel!.IsVisible);
        Assert.False(rearPanel!.IsVisible);

        rearSide.IsChecked = true;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(SuspensionType.Rear, page.SelectedSuspensionType);
        Assert.False(frontSide.IsChecked);
        Assert.True(rearSide.IsChecked);
        Assert.False(frontPanel.IsVisible);
        Assert.True(rearPanel.IsVisible);
    }

    [AvaloniaFact]
    public async Task StrokesPageView_SelectsRear_WhenOnlyRearStatisticsAreAvailable()
    {
        var workspace = MobileAnalysisWorkspaceStub.Create(
            hasFrontAnalysis: false,
            hasRearAnalysis: true);
        var page = new StrokesPageViewModel(workspace);

        await using var mounted = await MountAsync(new StrokesPageView { DataContext = page });

        var selector = mounted.View.FindControl<StackPanel>("MobileStrokesSideRadioButtons");
        var frontPanel = mounted.View.FindControl<Grid>("FrontStrokesPanel");
        var rearPanel = mounted.View.FindControl<Grid>("RearStrokesPanel");

        Assert.Equal(SuspensionType.Rear, page.SelectedSuspensionType);
        Assert.False(selector!.IsVisible);
        Assert.False(frontPanel!.IsVisible);
        Assert.True(rearPanel!.IsVisible);
    }

    [AvaloniaFact]
    public async Task StrokesPageView_RendersStatisticsBannerContributions()
    {
        var workspace = MobileAnalysisWorkspaceStub.Create(
            hasFrontAnalysis: true,
            hasRearAnalysis: true);
        workspace.ExtensionSlots.AnalysisBanners.Add(new RecordedSessionAnalysisBannerContribution(
            "extension",
            "statistics-banner",
            Order: 0,
            new TestContributionViewModel
            {
                Content = new TextBlock { Name = "MobileStrokesStatisticsBanner", Text = "Analysis banner" },
            }));
        var page = new StrokesPageViewModel(workspace);

        await using var mounted = await MountAsync(new StrokesPageView { DataContext = page });

        var contributionHost = Assert.Single(
            mounted.View.GetVisualDescendants().OfType<RecordedSessionAnalysisContributionsView>());
        Assert.NotNull(contributionHost);
        AssertContributionText(mounted.View, "MobileStrokesStatisticsBanner", "Analysis banner");
    }

    [AvaloniaFact]
    public async Task SessionInsightsPageView_BindsFindingsAndTargetProfile_InOneColumn()
    {
        var workspace = MobileAnalysisWorkspaceStub.Create(
            hasFrontAnalysis: true,
            hasRearAnalysis: true);
        var page = new SessionInsightsPageViewModel(workspace);

        await using var mounted = await MountAsync(new SessionInsightsPageView { DataContext = page });

        var profileComboBox = mounted.View.FindControl<ComboBox>("MobileAnalysisTargetProfileComboBox");

        Assert.Equal(SessionInsightsTargetProfile.Trail, profileComboBox!.SelectedValue);

        profileComboBox.SelectedValue = SessionInsightsTargetProfile.Enduro;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(SessionInsightsTargetProfile.Enduro, workspace.SelectedSessionInsightsTargetProfile);
    }

    private static async Task<MountedMobileStatisticsPageView<TView>> MountAsync<TView>(
        TView view,
        double? viewportWidth = null)
        where TView : Control
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsurePlotViewStyle();

        var hostView = new ScrollViewer { Content = view };
        if (viewportWidth is { } width)
        {
            hostView.Width = width;
        }

        var host = await ViewTestHelpers.ShowViewAsync(hostView);
        return new MountedMobileStatisticsPageView<TView>(host, view);
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

    private sealed class MobileAnalysisWorkspaceStub : ISessionAnalysisWorkspace
    {
        private MobileAnalysisWorkspaceStub(
            TelemetryData telemetryData,
            bool hasFrontAnalysis,
            bool hasRearAnalysis,
            bool hasFrontForkVibration,
            bool hasFrontFrameVibration,
            bool hasRearForkVibration,
            bool hasRearFrameVibration)
        {
            TelemetryData = telemetryData;
            FrontAnalysisState = hasFrontAnalysis ? SurfacePresentationState.Ready : SurfacePresentationState.Hidden;
            RearAnalysisState = hasRearAnalysis ? SurfacePresentationState.Ready : SurfacePresentationState.Hidden;
            FrontForkVibrationState = hasFrontForkVibration ? SurfacePresentationState.Ready : SurfacePresentationState.Hidden;
            FrontFrameVibrationState = hasFrontFrameVibration ? SurfacePresentationState.Ready : SurfacePresentationState.Hidden;
            RearForkVibrationState = hasRearForkVibration ? SurfacePresentationState.Ready : SurfacePresentationState.Hidden;
            RearFrameVibrationState = hasRearFrameVibration ? SurfacePresentationState.Ready : SurfacePresentationState.Hidden;
        }

        public static MobileAnalysisWorkspaceStub Create(
            bool hasFrontAnalysis,
            bool hasRearAnalysis,
            bool hasFrontForkVibration = false,
            bool hasFrontFrameVibration = false,
            bool hasRearForkVibration = false,
            bool hasRearFrameVibration = false)
        {
            var telemetry = TestTelemetryData.CreateProcessed();
            telemetry.ImuData = TestTelemetryData.CreateWithImu().ImuData;

            return new MobileAnalysisWorkspaceStub(
                telemetry,
                hasFrontAnalysis,
                hasRearAnalysis,
                hasFrontForkVibration,
                hasFrontFrameVibration,
                hasRearForkVibration,
                hasRearFrameVibration);
        }

        public TelemetryData? TelemetryData { get; }
        public TelemetryTimeRange? AnalysisRange => null;
        public RecordedSessionExtensionSlots ExtensionSlots { get; } = new();
        public TravelDistributionMode SelectedTravelDistributionMode { get; set; } = TravelDistributionMode.ActiveSuspension;
        public BalanceDisplacementMode SelectedBalanceDisplacementMode { get; set; } = BalanceDisplacementMode.Zenith;
        public BalanceSpeedMode SelectedBalanceSpeedMode { get; set; } = BalanceSpeedMode.Both;
        public VelocityAverageMode SelectedVelocityAverageMode { get; set; } = VelocityAverageMode.SampleAveraged;
        public SessionInsightsTargetProfile SelectedSessionInsightsTargetProfile { get; set; } = SessionInsightsTargetProfile.Trail;
        public IReadOnlyList<TravelDistributionModeOption> TravelDistributionModeOptions { get; } =
        [
            new(TravelDistributionMode.ActiveSuspension, "Active suspension", "Uses only compression and rebound stroke samples. Best for travel use while the suspension is actively moving."),
            new(TravelDistributionMode.DynamicSag, "Dynamic sag", "Uses every selected travel sample. Best for ride height over the segment, including quiet or steady sections."),
        ];
        public IReadOnlyList<BalanceDisplacementModeOption> BalanceDisplacementModeOptions { get; } =
        [
            new(BalanceDisplacementMode.Zenith, "Zenith", "Plots each stroke at its deepest travel."),
            new(BalanceDisplacementMode.Travel, "Travel", "Plots each stroke by start-to-end travel distance."),
            new(BalanceDisplacementMode.Speed, "Speed", "Plots each stroke at the travel position where peak speed occurs."),
        ];
        public IReadOnlyList<BalanceSpeedModeOption> BalanceSpeedModeOptions { get; } =
        [
            new(BalanceSpeedMode.Both, "Both", "Uses all matching compression or rebound strokes."),
            new(BalanceSpeedMode.LowSpeed, "Low speed", "Uses strokes below the high-speed threshold."),
            new(BalanceSpeedMode.HighSpeed, "High speed", "Uses strokes at or above the high-speed threshold."),
        ];
        public IReadOnlyList<VelocityAverageModeOption> VelocityAverageModeOptions { get; } =
        [
            new(VelocityAverageMode.SampleAveraged, "Sample-averaged", "Counts every sample inside compression and rebound strokes. Best for where damping spent time."),
            new(VelocityAverageMode.StrokePeakAveraged, "Stroke-peak average", "Counts each stroke once by peak velocity and peak travel. Best for what events damping saw."),
        ];
        public IReadOnlyList<SessionInsightsTargetProfileOption> SessionInsightsTargetProfileOptions { get; } =
        [
            new(SessionInsightsTargetProfile.Weekend, "Weekend", "Uses conservative speed context."),
            new(SessionInsightsTargetProfile.Trail, "Trail", "Uses general trail-riding speed context."),
            new(SessionInsightsTargetProfile.Enduro, "Enduro", "Uses faster rough-descending speed context."),
            new(SessionInsightsTargetProfile.DH, "DH", "Uses downhill-race speed context."),
        ];
        public string SessionAnalysisRangeText => "Full session";
        public string SessionAnalysisModesText => "Travel: Active suspension  Velocity: Sample-averaged  Balance: Zenith / Both";
        public SurfacePresentationState FrontAnalysisState { get; }
        public SurfacePresentationState RearAnalysisState { get; }
        public SurfacePresentationState CompressionBalanceState { get; } = SurfacePresentationState.Ready;
        public SurfacePresentationState ReboundBalanceState { get; } = SurfacePresentationState.Ready;
        public SurfacePresentationState FrontForkVibrationState { get; }
        public SurfacePresentationState FrontFrameVibrationState { get; }
        public SurfacePresentationState RearForkVibrationState { get; }
        public SurfacePresentationState RearFrameVibrationState { get; }
        public SessionDampingPercentages DampingPercentages { get; } = new(10, 20, 30, 40, 50, 60, 70, 80);
        public DampingSpeedCutoffs DampingSpeedCutoffs { get; } = DampingSpeedCutoffs.Default;
        public DampingSpeedCutoffs PlotDampingSpeedCutoffs => DampingSpeedCutoffs;
        public bool CanEditDampingSpeedCutoffs => true;
        public IRelayCommand<TelemetryRangeSelection?> SelectAnalysisRangeCommand { get; } =
            new RelayCommand<TelemetryRangeSelection?>(_ => { });
        public TelemetryRangeSelection? ActiveFrontAnalysisSelection => null;
        public TelemetryRangeSelection? ActiveRearAnalysisSelection => null;
        public void PreviewDampingSpeedCutoff(SuspensionType side, DampingSpeedCircuit circuit, double cutoffMmPerSecond) { }
        public void CancelDampingSpeedCutoffPreview() { }
        public Task CommitDampingSpeedCutoffAsync(SuspensionType side, DampingSpeedCircuit circuit, double cutoffMmPerSecond) =>
            Task.CompletedTask;
        public SessionInsightsResult SessionInsights { get; } = new(
            SurfacePresentationState.Ready,
            [new SessionInsightsStep(
                SessionInsightsStepId.Sag,
                "Sag & travel use",
                SessionInsightsSeverity.Watch,
                true,
                [new SessionInsightsMetric("Max travel", "52.0", "%", "Fork", ">= 85 % on hard terrain")],
                null,
                [],
                [new SessionInsightsFinding(
                    SessionInsightsCategory.TravelUse,
                    SessionInsightsSeverity.Watch,
                    SessionInsightsConfidence.Medium,
                    "Travel use watch",
                    "The fork is not using much travel.",
                    "Try a small pressure experiment and rerun the same section.",
                    [new SessionInsightsEvidence("Max travel", "52.0", "%", "Fork", "Active suspension travel stats")])])],
            [],
            null,
            [new SessionInsightsFinding(
                SessionInsightsCategory.TravelUse,
                SessionInsightsSeverity.Watch,
                SessionInsightsConfidence.Medium,
                "Travel use watch",
                "The fork is not using much travel.",
                "Try a small pressure experiment and rerun the same section.",
                [new SessionInsightsEvidence("Max travel", "52.0", "%", "Fork", "Active suspension travel stats")])]);
    }
}

internal sealed record MountedMobileStatisticsPageView<TView>(Window Host, TView View) : IAsyncDisposable
    where TView : Control
{
    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
