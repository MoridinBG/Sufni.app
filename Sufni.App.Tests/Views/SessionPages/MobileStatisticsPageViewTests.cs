using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.App.ViewModels.SessionPages;
using Sufni.App.Views.Controls;
using Sufni.App.Views.SessionPages;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Views.SessionPages;

public class MobileStatisticsPageViewTests
{
    [AvaloniaFact]
    public async Task SpringPageView_BindsTravelHistogramModeRadioButtons_WhenTelemetryIsAvailable()
    {
        var workspace = MobileStatisticsWorkspaceStub.Create(
            hasFrontStatistics: true,
            hasRearStatistics: true);
        var page = new SpringPageViewModel(workspace);

        await using var mounted = await MountAsync(new SpringPageView { DataContext = page });

        var selector = mounted.View.FindControl<StackPanel>("MobileTravelHistogramModeRadioButtons");
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

        Assert.Equal(TravelHistogramMode.DynamicSag, workspace.SelectedTravelHistogramMode);
        Assert.False(activeSuspension.IsChecked);
        Assert.True(dynamicSag.IsChecked);
    }

    [AvaloniaFact]
    public async Task DamperPageView_BindsVelocityAverageModeRadioButtons_WhenTelemetryIsAvailable()
    {
        var workspace = MobileStatisticsWorkspaceStub.Create(
            hasFrontStatistics: true,
            hasRearStatistics: true);
        var page = new DamperPageViewModel(workspace);

        await using var mounted = await MountAsync(new DamperPageView { DataContext = page });

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
        var workspace = MobileStatisticsWorkspaceStub.Create(
            hasFrontStatistics: true,
            hasRearStatistics: true);
        var page = new BalancePageViewModel(workspace);

        await using var mounted = await MountAsync(new BalancePageView { DataContext = page });

        var selector = mounted.View.FindControl<StackPanel>("MobileBalanceDisplacementModeRadioButtons");
        var zenith = mounted.View.FindControl<RadioButton>("MobileZenithModeRadioButton");
        var travel = mounted.View.FindControl<RadioButton>("MobileTravelModeRadioButton");

        Assert.NotNull(selector);
        Assert.True(selector!.IsVisible);
        Assert.NotNull(zenith);
        Assert.NotNull(travel);
        Assert.True(zenith!.IsChecked);
        Assert.False(travel!.IsChecked);

        travel.IsChecked = true;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(BalanceDisplacementMode.Travel, workspace.SelectedBalanceDisplacementMode);
        Assert.False(zenith.IsChecked);
        Assert.True(travel.IsChecked);
    }

    [AvaloniaFact]
    public async Task StrokesPageView_SideSelector_ShowsOneSuspensionSideAtATime()
    {
        var workspace = MobileStatisticsWorkspaceStub.Create(
            hasFrontStatistics: true,
            hasRearStatistics: true);
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
        var workspace = MobileStatisticsWorkspaceStub.Create(
            hasFrontStatistics: false,
            hasRearStatistics: true);
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
    public async Task VibrationPageView_RendersAvailableVibrationHosts_InOneColumn()
    {
        var workspace = MobileStatisticsWorkspaceStub.Create(
            hasFrontStatistics: true,
            hasRearStatistics: true,
            hasFrontForkVibration: true,
            hasFrontFrameVibration: true,
            hasRearForkVibration: true,
            hasRearFrameVibration: true);
        var page = new VibrationPageViewModel(workspace);

        await using var mounted = await MountAsync(new VibrationPageView { DataContext = page });

        var column = mounted.View.FindControl<StackPanel>("VibrationColumn");
        var hosts = mounted.View.GetVisualDescendants()
            .OfType<PlaceholderOverlayContainer>()
            .Where(host => host.Name?.EndsWith("VibrationHost", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotNull(column);
        Assert.Equal(Orientation.Vertical, column!.Orientation);
        Assert.Equal(4, hosts.Length);
        Assert.All(hosts, host => Assert.True(host.IsVisible));
    }

    [AvaloniaFact]
    public async Task SessionAnalysisPageView_BindsFindingsAndTargetProfile_InOneColumn()
    {
        var workspace = MobileStatisticsWorkspaceStub.Create(
            hasFrontStatistics: true,
            hasRearStatistics: true);
        var page = new SessionAnalysisPageViewModel(workspace);

        await using var mounted = await MountAsync(new SessionAnalysisPageView { DataContext = page });

        var content = mounted.View.FindControl<StackPanel>("MobileAnalysisContent");
        var header = mounted.View.FindControl<StackPanel>("MobileAnalysisHeader");
        var profileComboBox = mounted.View.FindControl<ComboBox>("MobileAnalysisTargetProfileComboBox");
        var findings = mounted.View.FindControl<ItemsControl>("MobileSessionAnalysisFindingsItemsControl");

        Assert.NotNull(content);
        Assert.NotNull(header);
        Assert.Equal(Orientation.Vertical, content!.Orientation);
        Assert.Equal(Orientation.Vertical, header!.Orientation);
        Assert.Equal(SessionAnalysisTargetProfile.Trail, profileComboBox!.SelectedValue);
        Assert.NotNull(findings);
        var finding = Assert.IsType<SessionAnalysisFinding>(Assert.Single(findings!.Items));
        Assert.Equal("Travel use watch", finding.Title);

        profileComboBox.SelectedValue = SessionAnalysisTargetProfile.Enduro;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(SessionAnalysisTargetProfile.Enduro, workspace.SelectedSessionAnalysisTargetProfile);
    }

    private static async Task<MountedMobileStatisticsPageView<TView>> MountAsync<TView>(TView view)
        where TView : Control
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsurePlotViewStyle();

        var host = await ViewTestHelpers.ShowViewAsync(new ScrollViewer { Content = view });
        return new MountedMobileStatisticsPageView<TView>(host, view);
    }

    private sealed class MobileStatisticsWorkspaceStub : ISessionStatisticsWorkspace
    {
        private MobileStatisticsWorkspaceStub(
            TelemetryData telemetryData,
            bool hasFrontStatistics,
            bool hasRearStatistics,
            bool hasFrontForkVibration,
            bool hasFrontFrameVibration,
            bool hasRearForkVibration,
            bool hasRearFrameVibration)
        {
            TelemetryData = telemetryData;
            FrontStatisticsState = hasFrontStatistics ? SurfacePresentationState.Ready : SurfacePresentationState.Hidden;
            RearStatisticsState = hasRearStatistics ? SurfacePresentationState.Ready : SurfacePresentationState.Hidden;
            FrontForkVibrationState = hasFrontForkVibration ? SurfacePresentationState.Ready : SurfacePresentationState.Hidden;
            FrontFrameVibrationState = hasFrontFrameVibration ? SurfacePresentationState.Ready : SurfacePresentationState.Hidden;
            RearForkVibrationState = hasRearForkVibration ? SurfacePresentationState.Ready : SurfacePresentationState.Hidden;
            RearFrameVibrationState = hasRearFrameVibration ? SurfacePresentationState.Ready : SurfacePresentationState.Hidden;
        }

        public static MobileStatisticsWorkspaceStub Create(
            bool hasFrontStatistics,
            bool hasRearStatistics,
            bool hasFrontForkVibration = false,
            bool hasFrontFrameVibration = false,
            bool hasRearForkVibration = false,
            bool hasRearFrameVibration = false)
        {
            var telemetry = TestTelemetryData.Create();
            telemetry.ImuData = TestTelemetryFactories.CreateTelemetryDataWithImu().ImuData;

            return new MobileStatisticsWorkspaceStub(
                telemetry,
                hasFrontStatistics,
                hasRearStatistics,
                hasFrontForkVibration,
                hasFrontFrameVibration,
                hasRearForkVibration,
                hasRearFrameVibration);
        }

        public TelemetryData? TelemetryData { get; }
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
            new(VelocityAverageMode.StrokePeakAveraged, "Stroke-peak average", "Uses one peak-speed event per stroke."),
        ];
        public IReadOnlyList<SessionAnalysisTargetProfileOption> SessionAnalysisTargetProfileOptions { get; } =
        [
            new(SessionAnalysisTargetProfile.Weekend, "Weekend", "Uses conservative speed context."),
            new(SessionAnalysisTargetProfile.Trail, "Trail", "Uses general trail-riding speed context."),
            new(SessionAnalysisTargetProfile.Enduro, "Enduro", "Uses faster rough-descending speed context."),
            new(SessionAnalysisTargetProfile.DH, "DH", "Uses downhill-race speed context."),
        ];
        public string SessionAnalysisRangeText => "Full session";
        public string SessionAnalysisModesText => "Travel: Active suspension  Velocity: Sample-averaged  Balance: Zenith";
        public SurfacePresentationState FrontStatisticsState { get; }
        public SurfacePresentationState RearStatisticsState { get; }
        public SurfacePresentationState CompressionBalanceState { get; } = SurfacePresentationState.Ready;
        public SurfacePresentationState ReboundBalanceState { get; } = SurfacePresentationState.Ready;
        public SurfacePresentationState FrontForkVibrationState { get; }
        public SurfacePresentationState FrontFrameVibrationState { get; }
        public SurfacePresentationState RearForkVibrationState { get; }
        public SurfacePresentationState RearFrameVibrationState { get; }
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

internal sealed record MountedMobileStatisticsPageView<TView>(Window Host, TView View) : IAsyncDisposable
    where TView : Control
{
    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
