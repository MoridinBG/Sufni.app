using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.SessionPages;
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
        };

        await using var mounted = await MountAsync(viewModel);

        var frontSvg = mounted.View.FindControl<Avalonia.Svg.Skia.Svg>("FrontHistogramSvg");
        var rearSvg = mounted.View.FindControl<Avalonia.Svg.Skia.Svg>("RearHistogramSvg");

        Assert.NotNull(frontSvg);
        Assert.NotNull(rearSvg);
        Assert.True(frontSvg!.IsVisible);
        Assert.False(rearSvg!.IsVisible);
    }

    [AvaloniaFact]
    public async Task SpringPageView_ShowsOnlyRearHistogram_WhenOnlyRearSourceIsPresent()
    {
        var viewModel = new SpringPageViewModel
        {
            RearTravelHistogram = TestSvg,
        };

        await using var mounted = await MountAsync(viewModel);

        var frontSvg = mounted.View.FindControl<Avalonia.Svg.Skia.Svg>("FrontHistogramSvg");
        var rearSvg = mounted.View.FindControl<Avalonia.Svg.Skia.Svg>("RearHistogramSvg");

        Assert.NotNull(frontSvg);
        Assert.NotNull(rearSvg);
        Assert.False(frontSvg!.IsVisible);
        Assert.True(rearSvg!.IsVisible);
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