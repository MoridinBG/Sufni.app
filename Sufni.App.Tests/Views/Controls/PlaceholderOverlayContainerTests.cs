using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Sufni.App.Presentation;
using Sufni.App.Views.Controls;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.Views.Controls;

public class PlaceholderOverlayContainerTests
{
    [AvaloniaFact]
    public async Task PlaceholderOverlayContainer_ReservesLayout_ForLoading()
    {
        await using var mounted = await MountAsync(SurfacePresentationState.Loading("Loading travel data."));

        Assert.True(mounted.View.IsVisible);
        Assert.False(mounted.View.FindControl<ContentControl>("ReadyHost")!.IsVisible);
        Assert.True(mounted.View.FindControl<ContentControl>("PlaceholderHost")!.IsVisible);
        Assert.True(mounted.View.FindControl<Border>("OverlayPanel")!.IsVisible);
        Assert.True(mounted.View.FindControl<Border>("TintOverlay")!.IsVisible);
        Assert.True(mounted.View.FindControl<TextBlock>("StateMessageText")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task PlaceholderOverlayContainer_ReservesLayout_ForWaitingForData()
    {
        await using var mounted = await MountAsync(SurfacePresentationState.WaitingForData("Waiting for map data."));

        Assert.True(mounted.View.IsVisible);
        Assert.False(mounted.View.FindControl<ContentControl>("ReadyHost")!.IsVisible);
        Assert.True(mounted.View.FindControl<ContentControl>("PlaceholderHost")!.IsVisible);
        Assert.True(mounted.View.FindControl<Border>("OverlayPanel")!.IsVisible);
        Assert.True(mounted.View.FindControl<TextBlock>("StateMessageText")!.IsVisible);
        Assert.Equal("Waiting for map data.", mounted.View.FindControl<TextBlock>("StateMessageText")!.Text);
    }

    [AvaloniaFact]
    public async Task PlaceholderOverlayContainer_Collapses_ForHidden()
    {
        await using var mounted = await MountAsync(SurfacePresentationState.Hidden);

        Assert.False(mounted.View.IsVisible);
    }

    [AvaloniaFact]
    public async Task PlaceholderOverlayContainer_ShowsErrorPresentation_ForError()
    {
        await using var mounted = await MountAsync(SurfacePresentationState.Error("Failed to load graph."));

        Assert.True(mounted.View.IsVisible);
        Assert.False(mounted.View.FindControl<ContentControl>("ReadyHost")!.IsVisible);
        Assert.True(mounted.View.FindControl<ContentControl>("PlaceholderHost")!.IsVisible);
        Assert.True(mounted.View.FindControl<Border>("OverlayPanel")!.IsVisible);
        Assert.True(mounted.View.FindControl<TextBlock>("ErrorIconText")!.IsVisible);
        Assert.Equal("Failed to load graph.", mounted.View.FindControl<TextBlock>("StateMessageText")!.Text);
    }

    private static async Task<MountedPlaceholderOverlayContainer> MountAsync(SurfacePresentationState state)
    {
        ViewTestHelpers.EnsureViewTestResources();

        var view = new PlaceholderOverlayContainer
        {
            PresentationState = state,
            ReadyContent = new Border(),
            PlaceholderContent = new Border(),
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedPlaceholderOverlayContainer(host, view);
    }
}

internal sealed class MountedPlaceholderOverlayContainer(Window host, PlaceholderOverlayContainer view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public PlaceholderOverlayContainer View { get; } = view;

    public ValueTask DisposeAsync()
    {
        Host.Close();
        return ValueTask.CompletedTask;
    }
}
