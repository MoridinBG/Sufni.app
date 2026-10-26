using Sufni.App.Video.Models;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Video;
using Sufni.App.Video.ViewModels;
using Sufni.App.Video.Views;

namespace Sufni.App.Tests.Video.Views;

[Collection("Ui")]
public class VideoPaneViewTests : IDisposable
{
    private const long SessionTimestamp = 1_000;

    private readonly VideoFixture fixture = new();
    private readonly TestRecordedSessionTimeline timeline = new();

    [AvaloniaFact]
    public async Task VideoPaneView_LoadsActiveClipIntoNewPlayer_WhenAttached()
    {
        var pane = await CreatePaneAsync();

        var host = await ShowAsync(pane);

        var player = Assert.Single(fixture.PlayerFactory.CreatedPlayers);
        Assert.Equal(pane.VideoUri, player.LoadedUris.Last()?.AbsoluteUri);
        Assert.True(pane.Playback.PlayPauseCommand.CanExecute(null));
        host.Close();
    }

    [AvaloniaFact]
    public async Task VideoPaneView_DisposesPlayerAndReleasesTimelinePlayback_WhenDetached()
    {
        var pane = await CreatePaneAsync();
        var host = await ShowAsync(pane);
        timeline.RequestPlaybackToggle();

        host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(Assert.Single(fixture.PlayerFactory.CreatedPlayers).IsDisposed);
        Assert.False(timeline.IsPlaybackActive);
        Assert.False(pane.Playback.PlayPauseCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task VideoPaneView_ShowsUnavailableText_WhenPlayerCannotBeCreated()
    {
        fixture.PlayerFactory.CreateException = new DllNotFoundException("libvlc");
        var pane = await CreatePaneAsync();

        var host = await ShowAsync(pane);

        var view = (VideoPaneView)host.Content!;
        Assert.True(view.FindControl<TextBlock>("PlayerUnavailableText")!.IsVisible);
        host.Close();
    }

    private async Task<VideoPaneViewModel> CreatePaneAsync()
    {
        var sessionId = Guid.NewGuid();
        var pane = await fixture.CreatePaneAsync(
            sessionId,
            timeline,
            Observable.Return(new VideoSessionTiming(SessionTimestamp, DurationSeconds: 30)),
            fixture.CreateClip(sessionId, SessionTimestamp, durationSeconds: 60));
        timeline.SetCursorPosition(0);
        return pane;
    }

    private static Task<Window> ShowAsync(VideoPaneViewModel pane)
    {
        ViewTestHelpers.EnsureViewTestResources();
        return ViewTestHelpers.ShowViewAsync(new VideoPaneView { DataContext = pane });
    }

    public void Dispose() => fixture.Dispose();
}
