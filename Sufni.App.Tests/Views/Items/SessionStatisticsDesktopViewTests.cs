using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.DesktopViews.Items;
using Sufni.App.DesktopViews.Plots;
using Sufni.App.Models;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
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
        Assert.Equal(4, springRate.GetVisualDescendants().OfType<SessionStatisticsPlotView>().Count(plot => plot.IsVisible));
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
        var damping = mounted.View.FindControl<Grid>("Damping");
        var balance = mounted.View.FindControl<Grid>("Balance");

        Assert.NotNull(tabControl);
        Assert.NotNull(springRate);
        Assert.NotNull(damping);
        Assert.NotNull(balance);

        tabControl!.SelectedIndex = 1;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(springRate!.IsVisible);
        Assert.True(damping!.IsVisible);
        Assert.False(balance!.IsVisible);
        Assert.Equal(1, damping.GetVisualDescendants().OfType<SessionStatisticsPlotView>().Count(plot => plot.IsVisible));
        Assert.Equal(1, damping.GetVisualDescendants().OfType<TravelPercentageLegend>().Count(legend => legend.IsVisible));
        Assert.Equal(1, damping.GetVisualDescendants().OfType<VelocityBandView>().Count(view => view.IsVisible));
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
        var damping = mounted.View.FindControl<Grid>("Damping");
        var balance = mounted.View.FindControl<Grid>("Balance");

        Assert.NotNull(tabControl);
        Assert.NotNull(springRate);
        Assert.NotNull(damping);
        Assert.NotNull(balance);

        tabControl!.SelectedIndex = 2;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(springRate!.IsVisible);
        Assert.False(damping!.IsVisible);
        Assert.True(balance!.IsVisible);
        Assert.Equal(1, balance.GetVisualDescendants().OfType<SessionStatisticsPlotView>().Count(plot => plot.IsVisible));
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
        bool hasReboundBalanceTelemetry) : ISessionStatisticsWorkspace
    {
        public TelemetryData? TelemetryData { get; } = telemetryData;
        public bool HasFrontStatistics { get; } = hasFrontStatistics;
        public bool HasRearStatistics { get; } = hasRearStatistics;
        public bool HasCompressionBalanceTelemetry { get; } = hasCompressionBalanceTelemetry;
        public bool HasReboundBalanceTelemetry { get; } = hasReboundBalanceTelemetry;
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