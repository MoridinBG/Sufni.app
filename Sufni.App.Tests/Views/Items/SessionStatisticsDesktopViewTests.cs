using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.DesktopViews.Items;
using Sufni.App.DesktopViews.Plots;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.App.Views.Controls;
using Sufni.App.Views.Items;
using Sufni.App.Views.Plots;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Views.Items;

public class SessionStatisticsDesktopViewTests
{
    [AvaloniaFact]
    public async Task SessionStatisticsDesktopView_ShowsSpringSectionInitially_WhenFrontAndRearStatisticsAreAvailable()
    {
        var workspace = new SessionStatisticsWorkspaceStub(
            telemetryData: TestTelemetryData.CreateProcessed(),
            hasFrontStatistics: true,
            hasRearStatistics: true,
            hasCompressionBalanceTelemetry: true,
            hasReboundBalanceTelemetry: true);

        await using var mounted = await MountAsync(workspace);

        var springRate = mounted.View.FindControl<Grid>("SpringRate");
        var damping = mounted.View.FindControl<Grid>("Damping");
        var balance = mounted.View.FindControl<Grid>("Balance");
        var analysis = mounted.View.FindControl<Grid>("Analysis");

        Assert.NotNull(springRate);
        Assert.NotNull(damping);
        Assert.NotNull(balance);
        Assert.NotNull(analysis);

        Assert.True(springRate!.IsVisible);
        Assert.False(damping!.IsVisible);
        Assert.False(balance!.IsVisible);
        Assert.False(analysis!.IsVisible);
        Assert.Equal(
            2,
            springRate.GetVisualDescendants()
                .OfType<TravelStatisticsHost>()
                .Count(host => host.PresentationState.ReservesLayout && host.ShowFrequencyHistogram));
    }

    [AvaloniaFact]
    public async Task SessionStatisticsDesktopView_ShowsOnlyFrontDampingHosts_WhenOnlyFrontStatisticsAreAvailable()
    {
        var workspace = new SessionStatisticsWorkspaceStub(
            telemetryData: TestTelemetryData.CreateProcessed(),
            hasFrontStatistics: true,
            hasRearStatistics: false,
            hasCompressionBalanceTelemetry: false,
            hasReboundBalanceTelemetry: false);

        await using var mounted = await MountAsync(workspace);

        var tabControl = mounted.View.FindControl<TabStrip>("TabControl");
        var springRate = mounted.View.FindControl<Grid>("SpringRate");
        var strokes = mounted.View.FindControl<Grid>("Strokes");
        var damping = mounted.View.FindControl<Grid>("Damping");
        var balance = mounted.View.FindControl<Grid>("Balance");
        var vibration = mounted.View.FindControl<Grid>("Vibration");

        Assert.NotNull(tabControl);
        Assert.NotNull(springRate);
        Assert.NotNull(strokes);
        Assert.NotNull(damping);
        Assert.NotNull(balance);
        Assert.NotNull(vibration);

        tabControl!.SelectedIndex = 2;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(springRate!.IsVisible);
        Assert.False(strokes!.IsVisible);
        Assert.True(damping!.IsVisible);
        Assert.False(balance!.IsVisible);
        Assert.False(vibration!.IsVisible);
        var readyDampingHosts = damping.GetVisualDescendants()
            .OfType<VelocityStatisticsHost>()
            .Where(host => host.PresentationState.ReservesLayout)
            .ToArray();
        var frontDampingHost = Assert.Single(readyDampingHosts);
        Assert.True(frontDampingHost.ShowTravelLegend);
        Assert.Equal(workspace.DamperPercentages.FrontHscPercentage, frontDampingHost.HscPercentage);
    }

    [AvaloniaFact]
    public async Task SessionStatisticsDesktopView_AnalysisTab_BindsFindingsAndTargetProfile()
    {
        var workspace = new SessionStatisticsWorkspaceStub(
            telemetryData: TestTelemetryData.CreateProcessed(),
            hasFrontStatistics: true,
            hasRearStatistics: true,
            hasCompressionBalanceTelemetry: true,
            hasReboundBalanceTelemetry: true);

        await using var mounted = await MountAsync(workspace);

        var tabControl = mounted.View.FindControl<TabStrip>("TabControl");
        tabControl!.SelectedIndex = 5;
        await ViewTestHelpers.FlushDispatcherAsync();

        var analysis = mounted.View.FindControl<Grid>("Analysis");
        var springRate = mounted.View.FindControl<Grid>("SpringRate");
        var strokes = mounted.View.FindControl<Grid>("Strokes");
        var damping = mounted.View.FindControl<Grid>("Damping");
        var balance = mounted.View.FindControl<Grid>("Balance");
        var vibration = mounted.View.FindControl<Grid>("Vibration");
        var analysisView = analysis!.GetVisualDescendants().OfType<SessionAnalysisView>().Single();
        var profileComboBox = analysisView.FindControl<ComboBox>("SessionAnalysisTargetProfileComboBox");

        Assert.False(springRate!.IsVisible);
        Assert.False(strokes!.IsVisible);
        Assert.False(damping!.IsVisible);
        Assert.False(balance!.IsVisible);
        Assert.False(vibration!.IsVisible);
        Assert.True(analysis!.IsVisible);
        Assert.NotNull(profileComboBox);
        Assert.Equal(SessionAnalysisTargetProfile.Trail, profileComboBox!.SelectedValue);

        profileComboBox.SelectedValue = SessionAnalysisTargetProfile.Enduro;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(SessionAnalysisTargetProfile.Enduro, workspace.SelectedSessionAnalysisTargetProfile);
        var stepsItemsControl = analysisView.FindControl<ItemsControl>("SessionAnalysisStepsItemsControl");
        Assert.NotNull(stepsItemsControl);
        var step = Assert.IsType<SessionAnalysisStep>(Assert.Single(stepsItemsControl!.Items));
        Assert.Equal(SessionAnalysisStepId.Sag, step.Id);
        var finding = Assert.Single(step.Findings);
        Assert.Equal("Travel use watch", finding.Title);
        Assert.Equal("The fork is not using much travel.", finding.Observation);
        Assert.Contains(step.Metrics, metric => metric.Label == "Max travel");
    }

    [AvaloniaFact]
    public async Task SessionStatisticsDesktopView_BindsStatisticsModeSelectors()
    {
        var workspace = new SessionStatisticsWorkspaceStub(
            telemetryData: TestTelemetryData.CreateProcessed(),
            hasFrontStatistics: true,
            hasRearStatistics: true,
            hasCompressionBalanceTelemetry: true,
            hasReboundBalanceTelemetry: true);

        await using var mounted = await MountAsync(workspace);

        var travelMode = mounted.View.FindControl<ComboBox>("TravelHistogramModeComboBox");

        Assert.NotNull(travelMode);

        Assert.Equal(TravelHistogramMode.ActiveSuspension, travelMode!.SelectedValue);

        travelMode.SelectedValue = TravelHistogramMode.DynamicSag;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(TravelHistogramMode.DynamicSag, workspace.SelectedTravelHistogramMode);
    }

    private static async Task<MountedSessionStatisticsDesktopView> MountAsync(SessionStatisticsWorkspaceStub workspace)
    {
        ViewTestHelpers.EnsureSessionDetailViewSetup(isDesktop: true);

        var view = new SessionStatisticsDesktopView
        {
            DataContext = workspace,
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedSessionStatisticsDesktopView(host, view);
    }

    private sealed class SessionStatisticsWorkspaceStub(
        TelemetryData telemetryData,
        bool hasFrontStatistics,
        bool hasRearStatistics,
        bool hasCompressionBalanceTelemetry,
        bool hasReboundBalanceTelemetry,
        bool hasFrontForkVibration = false,
        bool hasFrontFrameVibration = false,
        bool hasRearForkVibration = false,
        bool hasRearFrameVibration = false) : ISessionStatisticsWorkspace
    {
        public TelemetryData? TelemetryData { get; } = telemetryData;
        public TelemetryTimeRange? AnalysisRange => null;
        public TravelHistogramMode SelectedTravelHistogramMode { get; set; } = TravelHistogramMode.ActiveSuspension;
        public BalanceDisplacementMode SelectedBalanceDisplacementMode { get; set; } = BalanceDisplacementMode.Zenith;
        public BalanceSpeedMode SelectedBalanceSpeedMode { get; set; } = BalanceSpeedMode.Both;
        public VelocityAverageMode SelectedVelocityAverageMode { get; set; } = VelocityAverageMode.SampleAveraged;
        public SessionAnalysisTargetProfile SelectedSessionAnalysisTargetProfile { get; set; } = SessionAnalysisTargetProfile.Trail;
        public IReadOnlyList<TravelHistogramModeOption> TravelHistogramModeOptions { get; } =
        [
            new(TravelHistogramMode.ActiveSuspension, "Active suspension", "Uses only compression and rebound stroke samples. Best for travel use while the suspension is actively moving."),
            new(TravelHistogramMode.DynamicSag, "Dynamic sag", "Uses every selected travel sample. Best for ride height over the segment, including quiet or steady sections."),
        ];
        public IReadOnlyList<BalanceDisplacementModeOption> BalanceDisplacementModeOptions { get; } =
        [
            new(BalanceDisplacementMode.Zenith, "Zenith", "Plots each stroke at its deepest travel."),
            new(BalanceDisplacementMode.Travel, "Travel", "Plots each stroke by start-to-end travel distance."),
        ];
        public IReadOnlyList<BalanceSpeedModeOption> BalanceSpeedModeOptions { get; } =
        [
            new(BalanceSpeedMode.Both, "Both", "Uses all matching compression or rebound strokes."),
            new(BalanceSpeedMode.LowSpeed, "Low speed", "Uses strokes below the high-speed threshold."),
            new(BalanceSpeedMode.HighSpeed, "High speed", "Uses strokes at or above the high-speed threshold."),
        ];
        public IReadOnlyList<VelocityAverageModeOption> VelocityAverageModeOptions { get; } =
        [
            new(VelocityAverageMode.SampleAveraged, "Sample-averaged", "Counts every sample inside compression and rebound strokes. Best for where the damper spent time."),
            new(VelocityAverageMode.StrokePeakAveraged, "Stroke-peak average", "Counts each stroke once by peak velocity and peak travel. Best for what events the damper saw."),
        ];
        public IReadOnlyList<SessionAnalysisTargetProfileOption> SessionAnalysisTargetProfileOptions { get; } =
        [
            new(SessionAnalysisTargetProfile.Weekend, "Weekend", "Uses conservative speed context for recreational pace and mixed terrain."),
            new(SessionAnalysisTargetProfile.Trail, "Trail", "Uses general trail-riding speed context."),
            new(SessionAnalysisTargetProfile.Enduro, "Enduro", "Uses faster rough-descending speed context."),
            new(SessionAnalysisTargetProfile.DH, "DH", "Uses downhill-race speed context."),
        ];
        public string SessionAnalysisRangeText => "Full session";
        public string SessionAnalysisModesText => "Travel: Active suspension  Velocity: Sample-averaged  Balance: Zenith / Both";
        public SurfacePresentationState FrontStatisticsState { get; } = hasFrontStatistics
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState RearStatisticsState { get; } = hasRearStatistics
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState CompressionBalanceState { get; } = hasCompressionBalanceTelemetry
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState ReboundBalanceState { get; } = hasReboundBalanceTelemetry
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState FrontForkVibrationState { get; } = hasFrontForkVibration
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState FrontFrameVibrationState { get; } = hasFrontFrameVibration
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState RearForkVibrationState { get; } = hasRearForkVibration
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState RearFrameVibrationState { get; } = hasRearFrameVibration
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SessionDamperPercentages DamperPercentages { get; } = new(10, 20, 30, 40, 50, 60, 70, 80);
        public SessionAnalysisResult SessionAnalysis { get; } = new(
            SurfacePresentationState.Ready,
            [new SessionAnalysisStep(
                SessionAnalysisStepId.Sag,
                "Sag & travel use",
                SessionAnalysisSeverity.Watch,
                true,
                [new SessionAnalysisMetric("Max travel", "52.0", "%", "Fork", ">= 85 % on hard terrain")],
                null,
                [],
                [new SessionAnalysisFinding(
                    SessionAnalysisCategory.TravelUse,
                    SessionAnalysisSeverity.Watch,
                    SessionAnalysisConfidence.Medium,
                    "Travel use watch",
                    "The fork is not using much travel.",
                    "Try a small pressure experiment and rerun the same section.",
                    [new SessionAnalysisEvidence("Max travel", "52.0", "%", "Fork", "Active suspension travel stats")])])],
            [],
            null,
            [new SessionAnalysisFinding(
                SessionAnalysisCategory.TravelUse,
                SessionAnalysisSeverity.Watch,
                SessionAnalysisConfidence.Medium,
                "Travel use watch",
                "The fork is not using much travel.",
                "Try a small pressure experiment and rerun the same section.",
                [new SessionAnalysisEvidence("Max travel", "52.0", "%", "Fork", "Active suspension travel stats")])]);
    }
}

internal sealed class MountedSessionStatisticsDesktopView(Window host, SessionStatisticsDesktopView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public SessionStatisticsDesktopView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
