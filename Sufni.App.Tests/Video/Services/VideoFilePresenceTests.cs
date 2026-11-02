using System.Collections.Immutable;
using System.Reactive.Linq;
using Sufni.App.Video.Services;
using Sufni.App.Tests.TestSupport.Async;

namespace Sufni.App.Tests.Video.Services;

public class VideoFilePresenceTests
{
    [Fact]
    public async Task Observe_ReportsFileRemovalAndReturnWithoutDuplicatePollResults()
    {
        using var directory = new TempDirectory("video-presence");
        var path = Path.Combine(directory.Path, "ride.mp4");
        var clock = new SignalingFakeTimeProvider();
        var background = new RecordingBackgroundTaskRunner();
        var presence = new VideoFilePresence(background, clock, new LocalVideoFileAccess(background));
        var observed = new List<ImmutableDictionary<string, bool>>();
        using var subscription = presence.Observe([path]).Subscribe(observed.Add);
        await clock.WaitForTimerAsync(TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.False(Assert.Single(observed)[path]);

        File.WriteAllBytes(path, []);
        clock.Advance(TimeSpan.FromSeconds(5));
        await clock.WaitForTimerAsync(TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.True(observed[1][path]);
        clock.Advance(TimeSpan.FromSeconds(5));
        await clock.WaitForTimerAsync(TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, observed.Count);

        File.Delete(path);
        clock.Advance(TimeSpan.FromSeconds(5));
        await clock.WaitForTimerAsync(TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.False(observed[2][path]);
        subscription.Dispose();
        var calls = background.InvocationCount;
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(calls, background.InvocationCount);
    }
}
