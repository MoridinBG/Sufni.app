using System;
using System.Linq;
using System.Threading.Tasks;
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

        Assert.NotNull(springRate);
        Assert.NotNull(damping);
        Assert.NotNull(balance);

        Assert.True(springRate!.IsVisible);
        Assert.False(damping!.IsVisible);
        Assert.False(balance!.IsVisible);
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