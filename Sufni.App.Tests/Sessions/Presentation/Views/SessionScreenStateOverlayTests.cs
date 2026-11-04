using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Sessions.Presentation.Views;
using Sufni.App.Shared.Views.Overlays;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests.Sessions.Presentation.Views;

[Collection("Ui")]
public class SessionScreenStateOverlayTests
{
    [AvaloniaFact]
    public async Task StateChanges_SwitchBetweenProgressFailureIncompleteAndReady()
    {
        ViewTestHelpers.EnsureViewTestResources();
        var view = new SessionScreenStateOverlay { ScreenState = SessionScreenPresentationState.Loading("Reading telemetry", 0.4) };
        await using var mounted = new MountedView<SessionScreenStateOverlay>(await ViewTestHelpers.ShowViewAsync(view), view);
        var busy = view.FindControl<BusyOverlay>("ScreenBusyOverlay")!;
        var error = view.FindControl<Border>("ErrorCard")!;
        var incomplete = view.FindControl<Border>("IncompleteCard")!;
        Assert.True(busy.IsEffectivelyVisible);
        Assert.True(busy.IsActive);
        Assert.Equal(0.4, busy.ProgressValue);
        Assert.Equal("Reading telemetry", busy.Message);
        Assert.False(error.IsVisible);
        Assert.False(incomplete.IsVisible);

        view.ScreenState = SessionScreenPresentationState.Error("Read failed");
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.False(busy.IsActive);
        Assert.False(busy.IsVisible);
        Assert.True(error.IsEffectivelyVisible);
        Assert.Equal("Read failed", view.FindControl<TextBlock>("ScreenErrorText")!.Text);

        view.ScreenState = SessionScreenPresentationState.IncompleteLocalData("Sync the recording");
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.False(error.IsVisible);
        Assert.True(incomplete.IsEffectivelyVisible);
        Assert.Equal("Sync the recording", view.FindControl<TextBlock>("ScreenIncompleteText")!.Text);

        view.ScreenState = SessionScreenPresentationState.Ready;
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.False(busy.IsVisible);
        Assert.False(error.IsVisible);
        Assert.False(incomplete.IsVisible);
    }

    [AvaloniaFact]
    public async Task NullMessage_UsesTheConfiguredFallbackWithoutOverridingLoadingProgress()
    {
        ViewTestHelpers.EnsureViewTestResources();
        var view = new SessionScreenStateOverlay { LoadingFallbackMessage = "Loading session data..." };
        await using var mounted = new MountedView<SessionScreenStateOverlay>(await ViewTestHelpers.ShowViewAsync(view), view);
        var busy = view.FindControl<BusyOverlay>("ScreenBusyOverlay")!;
        // Loading states require a message; Ready is the real null-message state.
        Assert.Equal("Loading session data...", busy.Message);
        Assert.False(busy.IsVisible);

        view.LoadingFallbackMessage = "Preparing";
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.Equal("Preparing", busy.Message);
        view.ScreenState = SessionScreenPresentationState.Loading("Reading", 0.2);
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.Equal("Reading", busy.Message);
        Assert.Equal(0.2, busy.ProgressValue);
    }
}
