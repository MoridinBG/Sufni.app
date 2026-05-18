using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.Presentation;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.SessionPages;
using Sufni.App.Views.Controls;
using Sufni.App.Views.SessionPages;

namespace Sufni.App.Tests.Views.SessionPages;

public class SpringPageViewTests
{
    [AvaloniaFact]
    public async Task SpringPageView_ShowsOnlyFrontHistogram_WhenOnlyFrontSourceIsPresent()
    {
        var viewModel = new SpringPageViewModel
        {
            FrontTravelHistogram = TestSvg,
            FrontHistogramState = SurfacePresentationState.Ready,
            RearHistogramState = SurfacePresentationState.Hidden,
        };

        await using var mounted = await MountAsync(viewModel);

        var hosts = mounted.View.GetVisualDescendants().OfType<PlaceholderOverlayContainer>().ToArray();
        var travelHosts = mounted.View.GetVisualDescendants().OfType<TravelStatisticsHost>().ToArray();
        var frontHost = travelHosts[0];
        var rearHost = travelHosts[1];

        Assert.Equal(2, hosts.Length);
        Assert.Equal(2, travelHosts.Length);
        Assert.True(hosts[0].IsVisible);
        Assert.False(hosts[1].IsVisible);
        Assert.Equal(TestSvg, frontHost.StaticSource);
        Assert.Null(rearHost.StaticSource);
    }

    [AvaloniaFact]
    public async Task SpringPageView_ShowsOnlyRearHistogram_WhenOnlyRearSourceIsPresent()
    {
        var viewModel = new SpringPageViewModel
        {
            RearTravelHistogram = TestSvg,
            FrontHistogramState = SurfacePresentationState.Hidden,
            RearHistogramState = SurfacePresentationState.Ready,
        };

        await using var mounted = await MountAsync(viewModel);

        var hosts = mounted.View.GetVisualDescendants().OfType<PlaceholderOverlayContainer>().ToArray();
        var travelHosts = mounted.View.GetVisualDescendants().OfType<TravelStatisticsHost>().ToArray();
        var frontHost = travelHosts[0];
        var rearHost = travelHosts[1];

        Assert.Equal(2, hosts.Length);
        Assert.Equal(2, travelHosts.Length);
        Assert.False(hosts[0].IsVisible);
        Assert.True(hosts[1].IsVisible);
        Assert.Null(frontHost.StaticSource);
        Assert.Equal(TestSvg, rearHost.StaticSource);
    }

    private static async Task<MountedSpringPageView> MountAsync(SpringPageViewModel viewModel)
    {
        ViewTestHelpers.EnsureViewTestResources();

        var view = new SpringPageView
        {
            DataContext = viewModel,
        };

        var host = await ViewTestHelpers.ShowViewAsync(new ScrollViewer { Content = view });
        return new MountedSpringPageView(host, view);
    }

    private const string TestSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"12\"><rect width=\"16\" height=\"12\" fill=\"#8899AA\" /></svg>";
}

internal sealed class MountedSpringPageView(Window host, SpringPageView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public SpringPageView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
