using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.Presentation;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.SessionPages;
using Sufni.App.Views.Controls;
using Sufni.App.Views.Plots;
using Sufni.App.Views.SessionPages;

namespace Sufni.App.Tests.Views.SessionPages;

public class DamperPageViewTests
{
    [AvaloniaFact]
    public async Task DamperPageView_ShowsOnlyFrontHistogramRow_WhenOnlyFrontDataIsPresent()
    {
        var viewModel = new DamperPageViewModel
        {
            FrontVelocityHistogram = TestSvg,
            FrontHistogramState = SurfacePresentationState.Ready,
            RearHistogramState = SurfacePresentationState.Hidden,
            FrontHscPercentage = 10,
            FrontLscPercentage = 20,
            FrontLsrPercentage = 30,
            FrontHsrPercentage = 40,
        };

        await using var mounted = await MountAsync(viewModel);

        var hosts = mounted.View.GetVisualDescendants().OfType<PlaceholderOverlayContainer>().ToArray();
        var velocityHosts = mounted.View.GetVisualDescendants().OfType<VelocityStatisticsHost>().ToArray();
        var frontHost = velocityHosts[0];
        var rearHost = velocityHosts[1];
        var frontBands = frontHost.GetVisualDescendants().OfType<VelocityBandView>().SingleOrDefault();

        Assert.Equal(2, hosts.Length);
        Assert.Equal(2, velocityHosts.Length);
        Assert.NotNull(frontBands);
        Assert.True(hosts[0].IsVisible);
        Assert.False(hosts[1].IsVisible);
        Assert.Equal(TestSvg, frontHost.StaticSource);
        Assert.Null(rearHost.StaticSource);
        Assert.Equal(10, frontHost.HscPercentage);
        Assert.Equal(20, frontHost.LscPercentage);
        Assert.Equal(30, frontHost.LsrPercentage);
        Assert.Equal(40, frontHost.HsrPercentage);
        Assert.Equal(10, frontBands!.HscPercentage);
        Assert.Equal(20, frontBands.LscPercentage);
        Assert.Equal(30, frontBands.LsrPercentage);
        Assert.Equal(40, frontBands.HsrPercentage);
    }

    [AvaloniaFact]
    public async Task DamperPageView_ShowsOnlyRearHistogramRow_WhenOnlyRearDataIsPresent()
    {
        var viewModel = new DamperPageViewModel
        {
            RearVelocityHistogram = TestSvg,
            FrontHistogramState = SurfacePresentationState.Hidden,
            RearHistogramState = SurfacePresentationState.Ready,
            RearHscPercentage = 11,
            RearLscPercentage = 21,
            RearLsrPercentage = 31,
            RearHsrPercentage = 41,
        };

        await using var mounted = await MountAsync(viewModel);

        var hosts = mounted.View.GetVisualDescendants().OfType<PlaceholderOverlayContainer>().ToArray();
        var velocityHosts = mounted.View.GetVisualDescendants().OfType<VelocityStatisticsHost>().ToArray();
        var frontHost = velocityHosts[0];
        var rearHost = velocityHosts[1];
        var rearBands = rearHost.GetVisualDescendants().OfType<VelocityBandView>().SingleOrDefault();

        Assert.Equal(2, hosts.Length);
        Assert.Equal(2, velocityHosts.Length);
        Assert.NotNull(rearBands);
        Assert.False(hosts[0].IsVisible);
        Assert.True(hosts[1].IsVisible);
        Assert.Null(frontHost.StaticSource);
        Assert.Equal(TestSvg, rearHost.StaticSource);
        Assert.Equal(11, rearHost.HscPercentage);
        Assert.Equal(21, rearHost.LscPercentage);
        Assert.Equal(31, rearHost.LsrPercentage);
        Assert.Equal(41, rearHost.HsrPercentage);
        Assert.Equal(11, rearBands!.HscPercentage);
        Assert.Equal(21, rearBands.LscPercentage);
        Assert.Equal(31, rearBands.LsrPercentage);
        Assert.Equal(41, rearBands.HsrPercentage);
    }

    [AvaloniaFact]
    public async Task DamperPageView_ShowsBothHistogramRows_WhenFrontAndRearDataArePresent()
    {
        var viewModel = new DamperPageViewModel
        {
            FrontVelocityHistogram = TestSvg,
            RearVelocityHistogram = TestSvg,
            FrontHistogramState = SurfacePresentationState.Ready,
            RearHistogramState = SurfacePresentationState.Ready,
            FrontHscPercentage = 10,
            RearHscPercentage = 11,
        };

        await using var mounted = await MountAsync(viewModel);

        var hosts = mounted.View.GetVisualDescendants().OfType<PlaceholderOverlayContainer>().ToArray();
        var velocityHosts = mounted.View.GetVisualDescendants().OfType<VelocityStatisticsHost>().ToArray();
        var frontBands = velocityHosts[0].GetVisualDescendants().OfType<VelocityBandView>().SingleOrDefault();
        var rearBands = velocityHosts[1].GetVisualDescendants().OfType<VelocityBandView>().SingleOrDefault();

        Assert.Equal(2, hosts.Length);
        Assert.Equal(2, velocityHosts.Length);
        Assert.NotNull(frontBands);
        Assert.NotNull(rearBands);
        Assert.True(hosts[0].IsVisible);
        Assert.True(hosts[1].IsVisible);
        Assert.True(frontBands!.IsVisible);
        Assert.True(rearBands!.IsVisible);
    }

    [AvaloniaFact]
    public async Task DamperPageView_UsesTallMobileVelocityPlots()
    {
        var viewModel = new DamperPageViewModel
        {
            FrontHistogramState = SurfacePresentationState.Ready,
            RearHistogramState = SurfacePresentationState.Ready,
        };

        await using var mounted = await MountAsync(viewModel);

        var frontHost = mounted.View.FindControl<VelocityStatisticsHost>("FrontVelocityStatisticsHost");
        var rearHost = mounted.View.FindControl<VelocityStatisticsHost>("RearVelocityStatisticsHost");

        Assert.NotNull(frontHost);
        Assert.NotNull(rearHost);
        Assert.Equal(360, frontHost!.PlotHeight);
        Assert.Equal(360, rearHost!.PlotHeight);
    }

    private static async Task<MountedDamperPageView> MountAsync(DamperPageViewModel viewModel)
    {
        ViewTestHelpers.EnsureViewTestResources();

        var view = new DamperPageView
        {
            DataContext = viewModel,
        };

        var host = await ViewTestHelpers.ShowViewAsync(new ScrollViewer { Content = view });
        return new MountedDamperPageView(host, view);
    }

    private const string TestSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"12\"><rect width=\"16\" height=\"12\" fill=\"#8899AA\" /></svg>";
}

internal sealed class MountedDamperPageView(Window host, DamperPageView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public DamperPageView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
