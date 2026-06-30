
using Sufni.App.Sessions.Graph.ViewModels.Editors;
namespace Sufni.App.Tests.ViewModels.Editors;

public class SessionTimelineLinkViewModelTests
{
    [Fact]
    public void SetCursorPosition_PansVisibleRangeToCursor_WhenPlaybackCursorPassesVisibleEnd()
    {
        var timeline = new SessionTimelineLinkViewModel();
        timeline.SetVisibleRange(0.2, 0.4);
        timeline.SetPlaybackActive(true);

        timeline.SetCursorPosition(0.45);

        Assert.Equal(0.45, timeline.VisibleRangeStart, precision: 8);
        Assert.Equal(0.65, timeline.VisibleRangeEnd, precision: 8);
        Assert.Same(timeline, timeline.VisibleRangeChangeSource);
    }

    [Fact]
    public void SetCursorPosition_KeepsSpanAtTimelineEnd_WhenPlaybackCursorNearsOne()
    {
        var timeline = new SessionTimelineLinkViewModel();
        timeline.SetVisibleRange(0.0, 0.2);
        timeline.SetPlaybackActive(true);

        timeline.SetCursorPosition(0.95);

        Assert.Equal(0.8, timeline.VisibleRangeStart, precision: 8);
        Assert.Equal(1.0, timeline.VisibleRangeEnd, precision: 8);
    }

    [Fact]
    public void SetCursorPosition_PansVisibleRangeToCursor_WhenPlaybackCursorBeforeVisibleStart()
    {
        var timeline = new SessionTimelineLinkViewModel();
        timeline.SetVisibleRange(0.5, 0.7);
        timeline.SetPlaybackActive(true);

        timeline.SetCursorPosition(0.3);

        Assert.Equal(0.3, timeline.VisibleRangeStart, precision: 8);
        Assert.Equal(0.5, timeline.VisibleRangeEnd, precision: 8);
    }

    [Fact]
    public void SetCursorPosition_DoesNotPan_WhenPlaybackIsInactive()
    {
        var timeline = new SessionTimelineLinkViewModel();
        timeline.SetVisibleRange(0.2, 0.4);

        timeline.SetCursorPosition(0.9);

        Assert.Equal(0.2, timeline.VisibleRangeStart, precision: 8);
        Assert.Equal(0.4, timeline.VisibleRangeEnd, precision: 8);
    }

    [Fact]
    public void SetCursorPosition_DoesNotPan_WhenPlaybackCursorInsideVisibleRange()
    {
        var timeline = new SessionTimelineLinkViewModel();
        timeline.SetVisibleRange(0.2, 0.4);
        timeline.SetPlaybackActive(true);
        var rangeChanges = 0;
        timeline.VisibleRangeChanged += (_, _) => rangeChanges++;

        timeline.SetCursorPosition(0.3);

        Assert.Equal(0, rangeChanges);
    }
}
