using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Store;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Sessions;
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;

[Collection("Ui")]
public class GpsOffsetSuggestionTests
{
    [AvaloniaTheory]
    [InlineData("apply", true)]
    [InlineData("later", false)]
    [InlineData(null, false)]
    public async Task HandledOfferIsRememberedForTheSelectedTrack(string? choice, bool apply)
    {
        using var harness = new SessionDetailHarness();
        var session = harness.CreateRecordedSession();
        var (loaded, trackId) = RideWithTrack(2);
        harness.DialogService.ShowChoiceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<DialogChoice>>())
            .Returns(Task.FromResult(choice));
        harness.SessionCoordinator.ShiftGpsOffsetAsync(session.Id, Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(new GpsOffsetResult.Applied());
        for (var open = 0; open < 2; open++)
        {
            await OpenAndCloseAsync(harness, session, loaded);
        }
        Assert.Equal(trackId, await HandledTrack(harness, session));
        await harness.DialogService.Received(1).ShowChoiceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<DialogChoice>>());
        await harness.SessionCoordinator.Received(apply ? 1 : 0).ShiftGpsOffsetAsync(session.Id,
            Arg.Is<double>(lag => Math.Abs(lag - 2) <= 0.3), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task ReturningToAnEarlierTrackCanOfferItAgain()
    {
        using var harness = new SessionDetailHarness();
        var session = harness.CreateRecordedSession();
        var (a, firstTrack) = RideWithTrack(2);
        var (b, secondTrack) = RideWithTrack(-2);
        await OpenAndCloseAsync(harness, session, a);
        await OpenAndCloseAsync(harness, session, b);
        Assert.Equal(secondTrack, await HandledTrack(harness, session));
        await OpenAndCloseAsync(harness, session, a);
        Assert.Equal(firstTrack, await HandledTrack(harness, session));
        await harness.DialogService.ReceivedWithAnyArgs(3).ShowChoiceAsync(default!, default!, default!);
    }

    [AvaloniaFact]
    public async Task DifferentTrackWaitsForActivationBeforeOfferingAgain()
    {
        using var harness = new SessionDetailHarness();
        var session = harness.CreateRecordedSession();
        var (loaded, id) = RideWithTrack(2);
        harness.PublishPreferences(SessionPreferences.Default with { LastGpsOffsetTrackId = Guid.NewGuid() });
        var editor = harness.CreateRecordedEditor(session, loaded, deferDomainHandlingWhenInactive: false);
        editor.SetTabActive(false);
        await harness.LoadAsync(editor);
        await ViewTestHelpers.FlushDispatcherAsync();
        await harness.DialogService.DidNotReceiveWithAnyArgs().ShowChoiceAsync(default!, default!, default!);
        editor.SetTabActive(true);
        await ViewTestHelpers.FlushDispatcherAsync();
        await harness.CloseAsync(editor);
        Assert.Equal(id, await HandledTrack(harness, session));
    }

    [AvaloniaFact]
    public async Task SubThresholdLagLeavesTrackEligibleForAFutureEstimate()
    {
        using var harness = new SessionDetailHarness();
        var session = harness.CreateRecordedSession();
        var (loaded, _) = RideWithTrack(0.3);
        var editor = harness.CreateRecordedEditor(session, loaded);
        await harness.LoadAsync(editor);
        await ViewTestHelpers.FlushDispatcherAsync();
        await harness.CloseAsync(editor);
        await harness.DialogService.DidNotReceiveWithAnyArgs().ShowChoiceAsync(default!, default!, default!);
        Assert.Null(await HandledTrack(harness, session));
    }

    [AvaloniaFact]
    public async Task ClosingWhileTheEstimateIsQueuedDropsTheOffer()
    {
        var runner = new DeferredBackgroundTaskRunner();
        using var harness = new SessionDetailHarness { BackgroundRunner = runner };
        var session = harness.CreateRecordedSession();
        var (loaded, _) = RideWithTrack(2);
        var editor = harness.CreateRecordedEditor(session, loaded);
        await harness.LoadAsync(editor);
        await ViewTestHelpers.FlushDispatcherAsync();
        var close = harness.CloseAsync(editor);
        await runner.RunNextAsync();
        await close;
        await harness.DialogService.DidNotReceiveWithAnyArgs().ShowChoiceAsync(default!, default!, default!);
        Assert.Null(await HandledTrack(harness, session));
    }

    private static async Task OpenAndCloseAsync(SessionDetailHarness harness, SessionSnapshot session,
        SessionDetailLoadResult loaded)
    {
        var editor = harness.CreateRecordedEditor(session, loaded);
        await harness.LoadAsync(editor);
        await ViewTestHelpers.FlushDispatcherAsync();
        await harness.CloseAsync(editor);
    }

    private static async Task<Guid?> HandledTrack(SessionDetailHarness harness, SessionSnapshot session) =>
        Assert.IsType<SessionPreferenceLoadResult.Loaded>(await harness.SessionCoordinator.LoadPreferencesAsync(session.Id))
            .Preferences.LastGpsOffsetTrackId;

    private static (SessionDetailLoadResult.Loaded, Guid) RideWithTrack(double shift)
    {
        var loaded = SessionDetailHarness.CreateLoadedRide(SyntheticRide.TurningRide(new RideRecording(TrackShift: shift)));
        return (loaded, loaded.Data.TelemetryPresentation.FullTrackId!.Value);
    }
}
