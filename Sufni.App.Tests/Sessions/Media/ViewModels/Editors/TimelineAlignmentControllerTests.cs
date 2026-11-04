using System.Reactive.Linq;
using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Shared.Models;
using Sufni.App.Shared.Views.Plots;
using Sufni.App.Tests.TestSupport.Sessions;
using Sufni.App.Tests.TestSupport.Video;
using Sufni.App.Video.Coordinators;
using Sufni.App.Video.Models;
using Sufni.App.Video.ViewModels;

namespace Sufni.App.Tests.Sessions.Media.ViewModels.Editors;

[Collection("Ui")]
public class TimelineAlignmentControllerTests
{
    [AvaloniaFact]
    public async Task VideoAlignment_ShiftsClipByDistanceBetweenMarks()
    {
        using var videos = new VideoFixture();
        var clip = videos.CreateClip("source", 1000, 60);
        using var pane = await videos.CreatePaneAsync(new SessionTimelineLinkViewModel(),
            Observable.Return(new VideoSessionTiming("source", 0, 60)), clip);
        using var harness = new SessionPlotActionsFixture(pane);
        videos.Coordinator.ShiftAlignmentAsync(clip.Id, Arg.Any<double>()).Returns(new VideoAlignmentResult.Updated());

        harness.Alignment.MarkVideoEventCommand.Execute(CreatePlotContext(5));
        await harness.Alignment.MarkVideoTelemetryEventCommand
            .ExecuteAsync(CreatePlotContext(clickSeconds: 3));

        await videos.Coordinator.Received(1).ShiftAlignmentAsync(clip.Id, 2);
        Assert.Empty(harness.Errors);
        Assert.False(harness.Alignment.MarkVideoTelemetryEventCommand.CanExecute(CreatePlotContext(3)));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VideoAlignment_DistinguishesMissingClipFromSaveFailure(bool missing)
    {
        using var videos = new VideoFixture();
        var clip = videos.CreateClip("source", 1000, 60);
        using var pane = await videos.CreatePaneAsync(new SessionTimelineLinkViewModel(),
            Observable.Return(new VideoSessionTiming("source", 0, 60)), clip);
        using var harness = new SessionPlotActionsFixture(pane);
        videos.Coordinator.ShiftAlignmentAsync(clip.Id, Arg.Any<double>()).Returns(missing
            ? new VideoAlignmentResult.ClipMissing()
            : new VideoAlignmentResult.Failed(VideoAlignmentFailureReason.Save, "disk full"));

        harness.Alignment.MarkVideoEventCommand.Execute(CreatePlotContext(5));
        await harness.Alignment.MarkVideoTelemetryEventCommand
            .ExecuteAsync(CreatePlotContext(clickSeconds: 3));

        if (missing)
        {
            Assert.Single(harness.Notifications);
            Assert.Empty(harness.Errors);
        }
        else
        {
            Assert.Equal(FailurePresentation.From(VideoAlignmentFailureReason.Save, "disk full"), Assert.Single(harness.Errors));
            Assert.Empty(harness.Notifications);
        }
        Assert.False(harness.Alignment.MarkVideoTelemetryEventCommand.CanExecute(CreatePlotContext(3)));
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task GpsAlignment_ReportsUnavailabilitySeparatelyFromSaveFailure(int outcome)
    {
        using var harness = new SessionPlotActionsFixture();
        GpsOffsetResult result = outcome switch
        {
            0 => new GpsOffsetResult.NoMatchingTrack(),
            1 => new GpsOffsetResult.SessionMissing(),
            2 => new GpsOffsetResult.TelemetryUnavailable(),
            _ => new GpsOffsetResult.Failed(GpsOffsetFailureReason.Save, "disk full"),
        };
        harness.SessionCoordinator.ShiftGpsOffsetAsync(harness.SessionId, Arg.Any<double>(), Arg.Any<CancellationToken>()).Returns(result);

        harness.Alignment.MarkGpsEventCommand.Execute(CreatePlotContext(5));
        await harness.Alignment.MarkGpsTelemetryEventCommand
            .ExecuteAsync(CreatePlotContext(clickSeconds: 3));

        if (outcome < 3)
        {
            Assert.Single(harness.Notifications);
            Assert.Empty(harness.Errors);
        }
        else
        {
            Assert.Equal(FailurePresentation.From(GpsOffsetFailureReason.Save, "disk full"), Assert.Single(harness.Errors));
            Assert.Empty(harness.Notifications);
        }
        Assert.False(harness.Alignment.MarkGpsTelemetryEventCommand.CanExecute(CreatePlotContext(3)));
    }

    [AvaloniaFact]
    public async Task VideoAlignment_Cancel_DropsPendingVideoMark()
    {
        using var videos = new VideoFixture();
        var clip = videos.CreateClip("source", 1000, 60);
        using var pane = await videos.CreatePaneAsync(new SessionTimelineLinkViewModel(),
            Observable.Return(new VideoSessionTiming("source", 0, 60)), clip);
        using var harness = new SessionPlotActionsFixture(pane);

        harness.Alignment.MarkVideoEventCommand.Execute(CreatePlotContext(5));
        harness.Alignment.CancelVideoAlignmentCommand.Execute(CreatePlotContext(5));

        var context = CreatePlotContext(clickSeconds: 3);
        Assert.False(harness.Alignment.MarkVideoTelemetryEventCommand.CanExecute(context));
        Assert.True(harness.Alignment.MarkVideoEventCommand.CanExecute(context));
    }

    [AvaloniaFact]
    public async Task PendingMark_IsSharedAcrossGpsAndVideo()
    {
        using var videos = new VideoFixture();
        var clip = videos.CreateClip("source", 1000, 60);
        using var pane = await videos.CreatePaneAsync(new SessionTimelineLinkViewModel(),
            Observable.Return(new VideoSessionTiming("source", 0, 60)), clip);
        using var harness = new SessionPlotActionsFixture(pane);
        var context = CreatePlotContext(3);
        harness.Alignment.MarkGpsEventCommand.Execute(context);
        Assert.False(harness.Alignment.MarkVideoEventCommand.CanExecute(context));
        harness.Alignment.CancelGpsTimelineAlignmentCommand.Execute(context);
        harness.Alignment.MarkVideoEventCommand.Execute(context);
        Assert.False(harness.Alignment.MarkGpsEventCommand.CanExecute(context));
        Assert.True(harness.Alignment.MarkVideoTelemetryEventCommand.CanExecute(context));
    }

    [Fact]
    public async Task LateAlignmentResult_AfterCancellationAddsNoFeedback()
    {
        using var harness = new SessionPlotActionsFixture();
        var pending = new TaskCompletionSource<GpsOffsetResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.SessionCoordinator.ShiftGpsOffsetAsync(harness.SessionId, 2, Arg.Any<CancellationToken>()).Returns(pending.Task);
        harness.Alignment.MarkGpsEventCommand.Execute(CreatePlotContext(5));
        var operation = harness.Alignment.MarkGpsTelemetryEventCommand.ExecuteAsync(CreatePlotContext(3));
        harness.Lifetime.Cancel();
        pending.SetResult(new GpsOffsetResult.Failed(GpsOffsetFailureReason.Save, "disk full"));
        await operation.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(harness.Errors);
        Assert.Empty(harness.Notifications);
    }

    private static TelemetryPlotContextMenuContext CreatePlotContext(double clickSeconds) => SessionPlotActionsFixture.Context(clickSeconds);
}
