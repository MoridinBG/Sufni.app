
using Sufni.App.MapsAndTracks.Views;
namespace Sufni.App.Tests.MapsAndTracks.Views;

public class MapInteractionControllerTests
{
    private readonly List<Action> posted = [];
    private readonly MapInteractionController controller;
    private int notifications;

    public MapInteractionControllerTests()
    {
        controller = new MapInteractionController(posted.Add);
        controller.ViewportNotificationDue += () => notifications++;
    }

    private void DrainPosted()
    {
        while (posted.Count > 0)
        {
            var action = posted[0];
            posted.RemoveAt(0);
            action();
        }
    }

    [Fact]
    public void PointerMoved_WithoutPress_DoesNotQueue()
    {
        controller.PointerMoved();

        Assert.Empty(posted);
    }

    [Fact]
    public void PointerDrag_CoalescesNotificationsUntilPostRuns()
    {
        controller.PointerPressed();
        controller.PointerMoved();
        controller.PointerMoved();
        controller.NavigatorViewportChanged();

        Assert.Single(posted);
        DrainPosted();
        Assert.Equal(1, notifications);

        controller.PointerMoved();
        Assert.Single(posted);
    }

    [Fact]
    public void PointerRelease_QueuesAndEndsInteraction()
    {
        controller.PointerPressed();
        controller.PointerReleasedOrCaptureLost();
        DrainPosted();

        Assert.Equal(1, notifications);
        Assert.False(controller.IsPointerInteractionActive);

        controller.NavigatorViewportChanged();
        Assert.Empty(posted);
    }

    [Fact]
    public void WheelChanged_QueuesWithoutPointerInteraction()
    {
        controller.WheelChanged();
        DrainPosted();

        Assert.Equal(1, notifications);
    }

    [Fact]
    public void QueueIsSuppressed_WhileApplyingTimelineUpdates()
    {
        controller.RunWithoutViewportTimelineUpdates(() =>
        {
            Assert.True(controller.IsApplyingTimelineUpdate);
            controller.WheelChanged();
        });

        Assert.Empty(posted);
        Assert.False(controller.IsApplyingTimelineUpdate);
    }

    [Fact]
    public void RunWithoutViewportTimelineUpdates_IsReentrant()
    {
        controller.RunWithoutViewportTimelineUpdates(() =>
            controller.RunWithoutViewportTimelineUpdates(() =>
                Assert.True(controller.IsApplyingTimelineUpdate)));

        Assert.False(controller.IsApplyingTimelineUpdate);
    }
}
