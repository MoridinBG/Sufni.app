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
        var frontRow = mounted.View.FindControl<Grid>("FrontHistogramRow");
        var rearRow = mounted.View.FindControl<Grid>("RearHistogramRow");
        var frontSvg = mounted.View.FindControl<Avalonia.Svg.Skia.Svg>("FrontVelocityHistogramSvg");
        var rearSvg = mounted.View.FindControl<Avalonia.Svg.Skia.Svg>("RearVelocityHistogramSvg");
        var frontBands = mounted.View.FindControl<VelocityBandView>("FrontVelocityBands");
        var rearBands = mounted.View.FindControl<VelocityBandView>("RearVelocityBands");

        Assert.Equal(2, hosts.Length);
        Assert.NotNull(frontRow);
        Assert.NotNull(rearRow);
        Assert.NotNull(frontSvg);
        Assert.NotNull(rearSvg);
        Assert.NotNull(frontBands);
        Assert.NotNull(rearBands);
        Assert.True(hosts[0].IsVisible);
        Assert.False(hosts[1].IsVisible);
        Assert.NotNull(frontSvg!.Source);
        Assert.Null(rearSvg!.Source);
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
        var frontRow = mounted.View.FindControl<Grid>("FrontHistogramRow");
        var rearRow = mounted.View.FindControl<Grid>("RearHistogramRow");
        var frontSvg = mounted.View.FindControl<Avalonia.Svg.Skia.Svg>("FrontVelocityHistogramSvg");
        var rearSvg = mounted.View.FindControl<Avalonia.Svg.Skia.Svg>("RearVelocityHistogramSvg");
        var rearBands = mounted.View.FindControl<VelocityBandView>("RearVelocityBands");

        Assert.Equal(2, hosts.Length);
        Assert.NotNull(frontRow);
        Assert.NotNull(rearRow);
        Assert.NotNull(frontSvg);
        Assert.NotNull(rearSvg);
        Assert.NotNull(rearBands);
        Assert.False(hosts[0].IsVisible);
        Assert.True(hosts[1].IsVisible);
        Assert.Null(frontSvg!.Source);
        Assert.NotNull(rearSvg!.Source);
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
        var frontRow = mounted.View.FindControl<Grid>("FrontHistogramRow");
        var rearRow = mounted.View.FindControl<Grid>("RearHistogramRow");
        var frontBands = mounted.View.FindControl<VelocityBandView>("FrontVelocityBands");
        var rearBands = mounted.View.FindControl<VelocityBandView>("RearVelocityBands");

        Assert.Equal(2, hosts.Length);
        Assert.NotNull(frontRow);
        Assert.NotNull(rearRow);
        Assert.NotNull(frontBands);
        Assert.NotNull(rearBands);
        Assert.True(hosts[0].IsVisible);
        Assert.True(hosts[1].IsVisible);
        Assert.True(frontBands!.IsVisible);
        Assert.True(rearBands!.IsVisible);
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