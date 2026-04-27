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

public class BalancePageViewTests
{
    [AvaloniaFact]
    public async Task BalancePageView_ShowsBothBalanceSvgs_WhenSourcesArePresent()
    {
        var viewModel = new BalancePageViewModel
        {
            CompressionBalance = TestSvg,
            ReboundBalance = TestSvg,
            CompressionBalanceState = SurfacePresentationState.Ready,
            ReboundBalanceState = SurfacePresentationState.Ready,
        };

        await using var mounted = await MountAsync(viewModel);

        var hosts = mounted.View.GetVisualDescendants().OfType<PlaceholderOverlayContainer>().ToArray();
        var svgs = mounted.View.GetVisualDescendants().OfType<Avalonia.Svg.Skia.Svg>().ToArray();

        Assert.Equal(2, hosts.Length);
        Assert.All(hosts, host => Assert.True(host.IsVisible));
        Assert.Equal(2, svgs.Length);
        Assert.All(svgs, svg => Assert.NotNull(svg.Source));
    }

    [AvaloniaFact]
    public async Task BalancePageView_HidesMissingBalanceSvg_WhenOnlyCompressionSourceIsPresent()
    {
        var viewModel = new BalancePageViewModel
        {
            CompressionBalance = TestSvg,
            CompressionBalanceState = SurfacePresentationState.Ready,
            ReboundBalanceState = SurfacePresentationState.Hidden,
        };

        await using var mounted = await MountAsync(viewModel);

        var hosts = mounted.View.GetVisualDescendants().OfType<PlaceholderOverlayContainer>().ToArray();
        var compressionSvg = mounted.View.FindControl<Avalonia.Svg.Skia.Svg>("CompressionBalanceSvg");

        Assert.Equal(2, hosts.Length);
        Assert.True(hosts[0].IsVisible);
        Assert.False(hosts[1].IsVisible);
        Assert.NotNull(compressionSvg);
        Assert.NotNull(compressionSvg!.Source);
    }

    private static async Task<MountedBalancePageView> MountAsync(BalancePageViewModel viewModel)
    {
        ViewTestHelpers.EnsureViewTestResources();

        var view = new BalancePageView
        {
            DataContext = viewModel,
        };

        var host = await ViewTestHelpers.ShowViewAsync(new ScrollViewer { Content = view });
        return new MountedBalancePageView(host, view);
    }

    private const string TestSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"12\"><rect width=\"16\" height=\"12\" fill=\"#8899AA\" /></svg>";
}

internal sealed class MountedBalancePageView(Window host, BalancePageView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public BalancePageView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}