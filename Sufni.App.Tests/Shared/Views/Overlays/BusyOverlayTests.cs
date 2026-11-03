using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;

using Sufni.App.Shared.Views.Overlays;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Shared.Views.Overlays;

[Collection("Ui")]
public class BusyOverlayTests
{

    [AvaloniaFact]
    public async Task BusyOverlay_HidesIndicatorButKeepsProgress_WhenShowIndicatorIsFalse()
    {
        await using var mounted = await MountAsync(new BusyOverlay
        {
            IsActive = true,
            UseStackLayout = true,
            ShowIndicator = false,
            ShowProgress = true,
            ProgressValue = 0.75,
        });

        var indicator = mounted.View.FindControl<ActivityIndicator>("StackBusyIndicator")
            ?? throw new InvalidOperationException("Stack busy indicator was not found.");
        var progressBar = mounted.View.FindControl<ProgressBar>("StackBusyProgressBar")
            ?? throw new InvalidOperationException("Stack busy progress bar was not found.");

        Assert.False(indicator.IsVisible);
        Assert.True(progressBar.IsVisible);
        Assert.Equal(0.75, progressBar.Value);
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
