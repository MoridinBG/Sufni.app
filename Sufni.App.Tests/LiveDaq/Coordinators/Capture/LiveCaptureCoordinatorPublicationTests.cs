using Sufni.App.Tests.TestSupport.LiveDaq;
using System.Reactive.Linq;
using Sufni.App.LiveDaq.Coordinators.Capture;
using Sufni.App.LiveDaq.Services.LiveStreaming;

namespace Sufni.App.Tests.LiveDaq.Coordinators.Capture;

public partial class LiveCaptureCoordinatorTests
{
    private static readonly TimeSpan UiCadence = TimeSpan.FromMilliseconds(33);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForegroundCapture_RefreshesDurationAndRevisionWithoutAnalysis(bool gpsWithoutFix)
    {
        var ui = new ManualUiThreadDispatcher();
        var coordinator = CreateCoordinator(signalPipeline: new ControllableLiveSignalPipeline(false), dispatcher: ui, sendOptions: false, observe: false);
        using var subscription = coordinator.States.Subscribe(_ => { });
        try
        {
            await coordinator.AttachAsync(TestContext.Current.CancellationToken);
            await ui.DrainAsync();
            if (gpsWithoutFix)
            {
                var gps = CreateGpsBatchFrame();
                gps = gps with { Records = [gps.Records[0] with { FixMode = 0 }] };
                frames.OnNext(gps);
                frames.OnNext(gps with
                {
                    Batch = gps.Batch with { FirstMonotonicUs = sessionHeader.SessionStartMonotonicUs + 2_000_000 },
                });
            }
            else
            {
                frames.OnNext(CreateTravelBatchFrame());
                await ui.DrainAsync();
                frames.OnNext(CreateTravelBatchFrame(firstMonotonicUs: sessionHeader.SessionStartMonotonicUs + 1_000_000));
            }
            var before = coordinator.Current;

            time.Advance(UiCadence);
            await ui.DrainAsync();

            Assert.Equal(gpsWithoutFix ? 2 : 1.025, coordinator.Current.Controls.CaptureDuration.TotalSeconds, 6);
            Assert.True(coordinator.Current.CaptureRevision > before.CaptureRevision);
            Assert.Null(coordinator.Current.Analysis);
            Assert.Empty(coordinator.Current.TrackPoints);
            if (!gpsWithoutFix)
            {
                var analysis = NextState(coordinator, state => state.Analysis is not null);
                coordinator.UpdateAnalysisOptions(DefaultOptions);
                await ui.DrainUntilAsync(analysis);
                Assert.NotNull(coordinator.Current.Analysis);
            }
        }
        finally { await ui.DrainUntilAsync(coordinator.DisposeAsync().AsTask()); }
    }

    [Fact]
    public async Task UrgentChanges_CoalesceLatestFactsAndPublishOnTheDispatcher()
    {
        var ui = new ManualUiThreadDispatcher();
        var coordinator = CreateCoordinator(signalPipeline: new ControllableLiveSignalPipeline(false), dispatcher: ui, sendOptions: false, observe: false);
        var received = new List<LiveCaptureState>();
        using var subscription = coordinator.States.Skip(1).Subscribe(state =>
        {
            Assert.True(ui.CheckAccess());
            received.Add(state);
        });
        try
        {
            await coordinator.AttachAsync(TestContext.Current.CancellationToken);
            frames.OnNext(CreateTravelBatchFrame());
            frames.OnNext(CreateGpsBatchFrame());
            Assert.Empty(received);

            await ui.DrainAsync();

            var state = Assert.Single(received);
            Assert.Equal(2, state.CaptureRevision);
            Assert.True(state.Controls.CanSave);
            Assert.True(state.SignalPresence.HasTravelData);
            Assert.Single(state.TrackPoints);
        }
        finally { await ui.DrainUntilAsync(coordinator.DisposeAsync().AsTask()); }
    }

    [Fact]
    public async Task SubscriberReset_PublishesAfterCurrentNotificationInTheSameDispatcherDrain()
    {
        var ui = new ManualUiThreadDispatcher();
        var coordinator = CreateCoordinator(signalPipeline: new ControllableLiveSignalPipeline(false), dispatcher: ui, sendOptions: false, observe: false);
        var received = new List<long>();
        var notifying = false;
        using var subscription = coordinator.States.Skip(1).Subscribe(state =>
        {
            Assert.False(notifying);
            notifying = true;
            received.Add(state.CaptureRevision);
            if (received.Count == 1) coordinator.ResetAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            notifying = false;
        });
        try
        {
            await coordinator.AttachAsync(TestContext.Current.CancellationToken);
            await ui.DrainAsync();
            Assert.Equal(new long[] { 0, 1 }, received);
        }
        finally { await ui.DrainUntilAsync(coordinator.DisposeAsync().AsTask()); }
    }

    [Fact]
    public async Task UnobservedCapture_PublishesUrgentChangesAndDefersContinuousChangesUntilResubscription()
    {
        var ui = new ManualUiThreadDispatcher();
        var coordinator = CreateCoordinator(signalPipeline: new ControllableLiveSignalPipeline(false), dispatcher: ui, sendOptions: false, observe: false);
        try
        {
            await coordinator.AttachAsync(TestContext.Current.CancellationToken);
            frames.OnNext(CreateTravelBatchFrame());
            await ui.DrainAsync();
            var current = coordinator.Current;
            using (coordinator.States.Subscribe(_ => { })) { }
            frames.OnNext(CreateTravelBatchFrame(firstMonotonicUs: sessionHeader.SessionStartMonotonicUs + 1_000_000));
            time.Advance(UiCadence);
            await ui.DrainAsync();
            Assert.Same(current, coordinator.Current);
            var received = new List<LiveCaptureState>();
            using var subscription = coordinator.States.Subscribe(received.Add);
            Assert.Same(current, Assert.Single(received));

            time.Advance(UiCadence);
            await ui.DrainAsync();

            Assert.Equal(2, received.Count);
            Assert.Equal(2, coordinator.Current.CaptureRevision);
        }
        finally { await ui.DrainUntilAsync(coordinator.DisposeAsync().AsTask()); }
    }

    [Fact]
    public async Task Reset_ClearsPresenceAndPublishesBeforeDisposalCompletes()
    {
        var ui = new ManualUiThreadDispatcher();
        var coordinator = CreateCoordinator(signalPipeline: new ControllableLiveSignalPipeline(false), dispatcher: ui, sendOptions: false, observe: false);
        var received = new List<LiveCaptureState>();
        var completed = false;
        using var subscription = coordinator.States.Skip(1).Subscribe(state =>
        {
            Assert.False(completed);
            received.Add(state);
        }, () => completed = true);
        try
        {
            await coordinator.AttachAsync(TestContext.Current.CancellationToken);
            frames.OnNext(CreateTravelBatchFrame());
            frames.OnNext(CreateImuBatchFrame());
            await ui.DrainAsync();
            Assert.True(coordinator.Current.SignalPresence.HasTravelData);
            Assert.True(coordinator.Current.SignalPresence.HasImuData);
            Assert.True(coordinator.Current.SignalPresence.HasPitchRollData);

            await coordinator.ResetAsync(TestContext.Current.CancellationToken);
            var disposal = coordinator.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            await ui.DrainUntilAsync(disposal);

            Assert.True(completed);
            Assert.Equal(default, received[^1].SignalPresence);
            Assert.False(received[^1].Controls.CanSave);
        }
        finally { await ui.DrainUntilAsync(coordinator.DisposeAsync().AsTask()); }
    }
}
