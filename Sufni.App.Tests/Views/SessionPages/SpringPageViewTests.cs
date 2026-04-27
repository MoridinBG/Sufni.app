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
        var frontSvg = mounted.View.FindControl<Avalonia.Svg.Skia.Svg>("FrontHistogramSvg");
        var rearSvg = mounted.View.FindControl<Avalonia.Svg.Skia.Svg>("RearHistogramSvg");

        Assert.Equal(2, hosts.Length);
        Assert.NotNull(frontSvg);
        Assert.NotNull(rearSvg);
        Assert.True(hosts[0].IsVisible);
        Assert.False(hosts[1].IsVisible);
        Assert.NotNull(frontSvg!.Source);
        Assert.Null(rearSvg!.Source);
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
        var frontSvg = mounted.View.FindControl<Avalonia.Svg.Skia.Svg>("FrontHistogramSvg");
        var rearSvg = mounted.View.FindControl<Avalonia.Svg.Skia.Svg>("RearHistogramSvg");

        Assert.Equal(2, hosts.Length);
        Assert.NotNull(frontSvg);
        Assert.NotNull(rearSvg);
        Assert.False(hosts[0].IsVisible);
        Assert.True(hosts[1].IsVisible);
        Assert.Null(frontSvg!.Source);
        Assert.NotNull(rearSvg!.Source);
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