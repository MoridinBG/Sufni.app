using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views.Controls;

public class BusyOverlayTests
{
    [AvaloniaFact]
    public async Task BusyOverlay_ShowsTintSpinnerAndPrimaryMessage_WhenActive()
    {
        await using var mounted = await MountAsync(new BusyOverlay
        {
            IsActive = true,
            ShowTint = true,
            Message = "File 1/2",
            IndicatorForeground = Brushes.CornflowerBlue,
            MessageForeground = Brushes.CornflowerBlue,
            TintBackground = Brushes.Black,
            TintOpacity = 0.7,
        });

        Assert.True(mounted.View.FindControl<Border>("TintOverlay")!.IsVisible);
        Assert.True(mounted.View.FindControl<ActivityIndicator>("BusyIndicator")!.IsActive);
        Assert.Equal("File 1/2", mounted.View.FindControl<TextBlock>("BusyMessageText")!.Text);
        Assert.True(mounted.View.FindControl<TextBlock>("BusyMessageText")!.IsVisible);
        Assert.False(mounted.View.FindControl<TextBlock>("SecondaryBusyMessageText")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task BusyOverlay_UsesSecondaryMessage_WhenRequested()
    {
        await using var mounted = await MountAsync(new BusyOverlay
        {
            IsActive = true,
            ShowMessage = false,
            ShowSecondaryMessage = true,
            SecondaryMessage = "Fetching sessions list",
        });

        Assert.False(mounted.View.FindControl<TextBlock>("BusyMessageText")!.IsVisible);
        var secondary = mounted.View.FindControl<TextBlock>("SecondaryBusyMessageText")!;
        Assert.True(secondary.IsVisible);
        Assert.Equal("Fetching sessions list", secondary.Text);
    }

    [AvaloniaFact]
    public async Task BusyOverlay_UsesStackLayout_WhenRequested()
    {
        await using var mounted = await MountAsync(new BusyOverlay
        {
            IsActive = true,
            UseStackLayout = true,
            Message = "Loading session data...",
        });

        Assert.False(mounted.View.FindControl<Grid>("OverlayContent")!.IsVisible);
        Assert.True(mounted.View.FindControl<StackPanel>("StackContent")!.IsVisible);
        Assert.True(mounted.View.FindControl<ActivityIndicator>("StackBusyIndicator")!.IsActive);
        Assert.Equal("Loading session data...", mounted.View.FindControl<TextBlock>("StackBusyMessageText")!.Text);
    }

    private static async Task<MountedBusyOverlay> MountAsync(BusyOverlay view)
    {
        ViewTestHelpers.EnsureViewTestResources();

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedBusyOverlay(host, view);
    }
}

internal sealed class MountedBusyOverlay(Window host, BusyOverlay view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public BusyOverlay View { get; } = view;

    public ValueTask DisposeAsync()
    {
        Host.Close();
        return ValueTask.CompletedTask;
    }
}
