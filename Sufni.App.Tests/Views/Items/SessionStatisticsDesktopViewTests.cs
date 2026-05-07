using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
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
            telemetryData: TestTelemetryData.Create(),
            hasFrontStatistics: true,
            hasRearStatistics: true,
            hasCompressionBalanceTelemetry: true,
            hasReboundBalanceTelemetry: true);

        await using var mounted = await MountAsync(workspace);

        var springRate = mounted.View.FindControl<Grid>("SpringRate");
        var damping = mounted.View.FindControl<Grid>("Damping");
        var balance = mounted.View.FindControl<Grid>("Balance");
        var analysis = mounted.View.FindControl<Grid>("Analysis");
        var contentHost = mounted.View.FindControl<ItemsControl>("StatisticsContentHost");

        Assert.NotNull(springRate);
        Assert.NotNull(damping);
        Assert.NotNull(balance);
        Assert.NotNull(analysis);
        Assert.NotNull(contentHost);

        Assert.Equal(new Thickness(16, 16, 16, 0), contentHost!.Margin);
        Assert.True(springRate!.IsVisible);
        Assert.False(damping!.IsVisible);
        Assert.False(balance!.IsVisible);
        Assert.False(analysis!.IsVisible);
        Assert.Equal(2, springRate.GetVisualDescendants().OfType<PlaceholderOverlayContainer>().Count(host => host.IsVisible));
    }

    [AvaloniaFact]
    public async Task SessionStatisticsDesktopView_ShowsOnlyFrontDampingHosts_WhenOnlyFrontStatisticsAreAvailable()
    {
        var workspace = new SessionStatisticsWorkspaceStub(
            telemetryData: TestTelemetryData.Create(),
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
        Assert.Equal(1, damping.GetVisualDescendants().OfType<PlaceholderOverlayContainer>().Count(host => host.IsVisible));
        Assert.Equal(1, damping.GetVisualDescendants().OfType<TravelPercentageLegend>().Count(legend => legend.IsVisible));
        Assert.Equal(1, damping.GetVisualDescendants().OfType<VelocityBandView>().Count(view => view.IsVisible));
    }

    [AvaloniaFact]
    public async Task SessionStatisticsDesktopView_ShowsOnlyStrokesHosts_WhenStrokesTabSelected()
    {
        var workspace = new SessionStatisticsWorkspaceStub(
            telemetryData: TestTelemetryData.Create(),
            hasFrontStatistics: true,
            hasRearStatistics: true,
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

        tabControl!.SelectedIndex = 1;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(springRate!.IsVisible);
        Assert.True(strokes!.IsVisible);
        Assert.False(damping!.IsVisible);
        Assert.False(balance!.IsVisible);
        Assert.False(vibration!.IsVisible);

        // Strokes is wrapped in a ScrollViewer whose visual children aren't realized
        // off-screen, so traverse the logical tree to find the placeholder hosts.
        var hosts = strokes.GetLogicalDescendants().OfType<PlaceholderOverlayContainer>().ToArray();
        Assert.Equal(2, hosts.Length);
        Assert.Equal(2, hosts.Count(host => host.IsVisible));
    }

    [AvaloniaFact]
    public async Task SessionStatisticsDesktopView_ShowsOnlyAvailableVibrationHosts_WhenVibrationTabSelected()
    {
        var workspace = new SessionStatisticsWorkspaceStub(
            telemetryData: TestTelemetryData.Create(),
            hasFrontStatistics: false,
            hasRearStatistics: false,
            hasCompressionBalanceTelemetry: false,
            hasReboundBalanceTelemetry: false,
            hasFrontForkVibration: true,
            hasFrontFrameVibration: false,
            hasRearForkVibration: true,
            hasRearFrameVibration: false);

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

        tabControl!.SelectedIndex = 4;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(springRate!.IsVisible);
        Assert.False(strokes!.IsVisible);
        Assert.False(damping!.IsVisible);
        Assert.False(balance!.IsVisible);
        Assert.True(vibration!.IsVisible);
        Assert.Equal(2, vibration.GetLogicalDescendants().OfType<PlaceholderOverlayContainer>().Count(host => host.IsVisible));
    }

    [AvaloniaFact]
    public async Task SessionStatisticsDesktopView_ShowsOnlyAvailableBalanceHosts_WhenBalanceTabSelected()
    {
        var workspace = new SessionStatisticsWorkspaceStub(
            telemetryData: TestTelemetryData.Create(),
            hasFrontStatistics: true,
            hasRearStatistics: true,
            hasCompressionBalanceTelemetry: true,
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

        tabControl!.SelectedIndex = 3;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(springRate!.IsVisible);
        Assert.False(strokes!.IsVisible);
        Assert.False(damping!.IsVisible);
        Assert.True(balance!.IsVisible);
        Assert.False(vibration!.IsVisible);
        Assert.Equal(1, balance.GetVisualDescendants().OfType<PlaceholderOverlayContainer>().Count(host => host.IsVisible));
    }

    [AvaloniaFact]
    public async Task SessionStatisticsDesktopView_ShowsAnalysisTab_AndHidesOtherContent_WhenSelected()
    {
        var workspace = new SessionStatisticsWorkspaceStub(
            telemetryData: TestTelemetryData.Create(),
            hasFrontStatistics: true,
            hasRearStatistics: true,
            hasCompressionBalanceTelemetry: true,
            hasReboundBalanceTelemetry: true);

        await using var mounted = await MountAsync(workspace);

        var tabControl = mounted.View.FindControl<TabStrip>("TabControl");
        var springRate = mounted.View.FindControl<Grid>("SpringRate");
        var strokes = mounted.View.FindControl<Grid>("Strokes");
        var damping = mounted.View.FindControl<Grid>("Damping");
        var balance = mounted.View.FindControl<Grid>("Balance");
        var vibration = mounted.View.FindControl<Grid>("Vibration");
        var analysis = mounted.View.FindControl<Grid>("Analysis");

        Assert.NotNull(tabControl);
        Assert.Equal(6, tabControl!.Items.Count);

        tabControl.SelectedIndex = 5;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(springRate!.IsVisible);
        Assert.False(strokes!.IsVisible);
        Assert.False(damping!.IsVisible);
        Assert.False(balance!.IsVisible);
        Assert.False(vibration!.IsVisible);
        Assert.True(analysis!.IsVisible);
    }

    [AvaloniaFact]
    public async Task SessionStatisticsDesktopView_AnalysisTab_BindsFindingsAndTargetProfile()
    {
        var workspace = new SessionStatisticsWorkspaceStub(
            telemetryData: TestTelemetryData.Create(),
            hasFrontStatistics: true,
            hasRearStatistics: true,
            hasCompressionBalanceTelemetry: true,
            hasReboundBalanceTelemetry: true);

        await using var mounted = await MountAsync(workspace);

        var tabControl = mounted.View.FindControl<TabStrip>("TabControl");
        tabControl!.SelectedIndex = 5;
        await ViewTestHelpers.FlushDispatcherAsync();

        var analysis = mounted.View.FindControl<Grid>("Analysis");
        var analysisView = analysis!.GetVisualDescendants().OfType<SessionAnalysisView>().Single();
        var profileComboBox = analysisView.FindControl<ComboBox>("SessionAnalysisTargetProfileComboBox");

        Assert.NotNull(profileComboBox);
        Assert.Equal(SessionAnalysisTargetProfile.Trail, profileComboBox!.SelectedValue);

        profileComboBox.SelectedValue = SessionAnalysisTargetProfile.Enduro;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(SessionAnalysisTargetProfile.Enduro, workspace.SelectedSessionAnalysisTargetProfile);
        var findingsItemsControl = analysisView.FindControl<ItemsControl>("SessionAnalysisFindingsItemsControl");
        Assert.NotNull(findingsItemsControl);
        var finding = Assert.IsType<SessionAnalysisFinding>(Assert.Single(findingsItemsControl!.Items));
        Assert.Equal("Travel use watch", finding.Title);
        Assert.Equal("The fork is not using much travel.", finding.Observation);
        Assert.Contains(finding.Evidence, evidence => evidence.Label == "Max travel");
    }

    [AvaloniaFact]
    public async Task SessionStatisticsDesktopView_BindsStatisticsModeSelectors()
    {
        var workspace = new SessionStatisticsWorkspaceStub(
            telemetryData: TestTelemetryData.Create(),
            hasFrontStatistics: true,
            hasRearStatistics: true,
            hasCompressionBalanceTelemetry: true,
            hasReboundBalanceTelemetry: true);

        await using var mounted = await MountAsync(workspace);

        var travelMode = mounted.View.FindControl<ComboBox>("TravelHistogramModeComboBox");
        var velocityAverageMode = mounted.View.FindControl<ComboBox>("VelocityAverageModeComboBox");
        var balanceMode = mounted.View.FindControl<ComboBox>("BalanceDisplacementModeComboBox");

        Assert.NotNull(travelMode);
        Assert.NotNull(velocityAverageMode);
        Assert.NotNull(balanceMode);

        Assert.Equal(TravelHistogramMode.ActiveSuspension, travelMode!.SelectedValue);
        Assert.Equal(VelocityAverageMode.SampleAveraged, velocityAverageMode!.SelectedValue);
        Assert.Equal(BalanceDisplacementMode.Zenith, balanceMode!.SelectedValue);
        Assert.Equal(
            "Active suspension uses stroke samples. Dynamic sag uses all selected travel samples.",
            ToolTip.GetTip(travelMode));
        Assert.Equal(
            "Sample-averaged uses all stroke samples. Stroke-peak average uses one peak-speed event per stroke.",
            ToolTip.GetTip(velocityAverageMode));
        Assert.Equal(
            "Zenith uses deepest stroke travel. Travel uses start-to-end stroke distance.",
            ToolTip.GetTip(balanceMode));
        Assert.Equal(new Thickness(0, 0, 10, 8), travelMode.Margin);
        Assert.Equal(new Thickness(0, 0, 69, 8), velocityAverageMode.Margin);
        Assert.Equal(new Thickness(0, 0, 10, 8), balanceMode.Margin);

        travelMode.SelectedValue = TravelHistogramMode.DynamicSag;
        velocityAverageMode.SelectedValue = VelocityAverageMode.StrokePeakAveraged;
        balanceMode.SelectedValue = BalanceDisplacementMode.Travel;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(TravelHistogramMode.DynamicSag, workspace.SelectedTravelHistogramMode);
        Assert.Equal(VelocityAverageMode.StrokePeakAveraged, workspace.SelectedVelocityAverageMode);
        Assert.Equal(BalanceDisplacementMode.Travel, workspace.SelectedBalanceDisplacementMode);
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
        public VelocityAverageMode SelectedVelocityAverageMode { get; set; } = VelocityAverageMode.SampleAveraged;
        public SessionAnalysisTargetProfile SelectedSessionAnalysisTargetProfile { get; set; } = SessionAnalysisTargetProfile.Trail;
        public IReadOnlyList<TravelHistogramModeOption> TravelHistogramModeOptions { get; } =
        [
            new(TravelHistogramMode.ActiveSuspension, "Active suspension", "Uses compression and rebound stroke samples only."),
            new(TravelHistogramMode.DynamicSag, "Dynamic sag", "Uses all selected travel samples."),
        ];
        public IReadOnlyList<BalanceDisplacementModeOption> BalanceDisplacementModeOptions { get; } =
        [
            new(BalanceDisplacementMode.Zenith, "Zenith", "Plots each stroke at its deepest travel."),
            new(BalanceDisplacementMode.Travel, "Travel", "Plots each stroke by start-to-end travel distance."),
        ];
        public IReadOnlyList<VelocityAverageModeOption> VelocityAverageModeOptions { get; } =
        [
            new(VelocityAverageMode.SampleAveraged, "Sample-averaged", "Uses every stroke sample for bars and average labels."),
            new(VelocityAverageMode.StrokePeakAveraged, "Stroke-peak average", "Uses one peak-speed event per stroke for bars and average labels."),
        ];
        public IReadOnlyList<SessionAnalysisTargetProfileOption> SessionAnalysisTargetProfileOptions { get; } =
        [
            new(SessionAnalysisTargetProfile.Weekend, "Weekend", "Uses conservative speed context for recreational pace and mixed terrain."),
            new(SessionAnalysisTargetProfile.Trail, "Trail", "Uses general trail-riding speed context."),
            new(SessionAnalysisTargetProfile.Enduro, "Enduro", "Uses faster rough-descending speed context."),
            new(SessionAnalysisTargetProfile.DH, "DH", "Uses downhill-race speed context."),
        ];
        public string SessionAnalysisRangeText => "Full session";
        public string SessionAnalysisModesText => "Travel: Active suspension  Velocity: Sample-averaged  Balance: Zenith";
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
