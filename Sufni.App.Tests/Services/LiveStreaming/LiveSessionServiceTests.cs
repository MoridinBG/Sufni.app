using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Serilog.Core;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Services.LiveStreaming;

public class LiveSessionServiceTests
{
    private readonly ILiveDaqSharedStream sharedStream = Substitute.For<ILiveDaqSharedStream>();
    private readonly ILiveDaqSharedStreamLease observerLease = Substitute.For<ILiveDaqSharedStreamLease>();
    private readonly ILiveDaqSharedStreamLease configurationLockLease = Substitute.For<ILiveDaqSharedStreamLease>();
    private readonly ISessionPresentationService sessionPresentationService = Substitute.For<ISessionPresentationService>();
    private readonly IBackgroundTaskRunner backgroundTaskRunner = new InlineBackgroundTaskRunner();
    private readonly BehaviorSubject<LiveDaqSharedStreamState> states = new(LiveDaqSharedStreamState.Empty);
    private readonly Subject<LiveProtocolFrame> frames = new();
    private readonly LiveSessionHeader sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(imuMask: LiveImuLocationMask.Frame);

    private LiveDaqSharedStreamState currentState = LiveDaqSharedStreamState.Empty;

    public LiveSessionServiceTests()
    {
        sharedStream.States.Returns(states);
        sharedStream.Frames.Returns(frames);
        sharedStream.CurrentState.Returns(_ => currentState);
        sharedStream.AcquireLease().Returns(observerLease);
        sharedStream.AcquireConfigurationLock().Returns(configurationLockLease);
        observerLease.DisposeAsync().Returns(ValueTask.CompletedTask);
        configurationLockLease.DisposeAsync().Returns(ValueTask.CompletedTask);
        sharedStream.EnsureStartedAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            currentState = new LiveDaqSharedStreamState(
                ConnectionState: LiveConnectionState.Connected,
                LastError: null,
                SessionHeader: sessionHeader,
                SelectedSensorMask: LiveSensorMask.Travel | LiveSensorMask.Imu | LiveSensorMask.Gps,
                IsConfigurationLocked: true,
                IsClosed: false);
            states.OnNext(currentState);
            return Task.FromResult<LivePreviewStartResult?>(
                new LivePreviewStartResult.Started(sessionHeader));
        });
        sessionPresentationService.CalculateDamperPercentages(Arg.Any<TelemetryData>())
            .Returns(new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8));
    }

    [Fact]
    public async Task EnsureAttachedAsync_AcquiresLeases_StartsStream_AndPublishesStreamingState()
    {
        var service = CreateService();

        await service.EnsureAttachedAsync();

        sharedStream.Received(1).AcquireLease();
        sharedStream.Received(1).AcquireConfigurationLock();
        await sharedStream.Received(1).EnsureStartedAsync(Arg.Any<CancellationToken>());

        var streaming = Assert.IsType<LiveSessionStreamPresentation.Streaming>(service.Current.Stream);
        Assert.Equal(sessionHeader.SessionId, streaming.SessionHeader.SessionId);

        await service.DisposeAsync();

        await observerLease.Received(1).DisposeAsync();
        await configurationLockLease.Received(1).DisposeAsync();
    }

    [Fact]
    public void CreateService_DoesNotAcquireLeasesBeforeEnsureAttachedAsync()
    {
        _ = CreateService();

        sharedStream.DidNotReceive().AcquireLease();
        sharedStream.DidNotReceive().AcquireConfigurationLock();
    }

    [Fact]
    public async Task EnsureAttachedAsync_DisposesFreshLeases_WhenStartThrows()
    {
        sharedStream.EnsureStartedAsync(Arg.Any<CancellationToken>())
            .Returns<Task<LivePreviewStartResult?>>(_ => throw new InvalidOperationException("boom"));
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureAttachedAsync());

        await observerLease.Received(1).DisposeAsync();
        await configurationLockLease.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task Frames_AccumulateGraphTrackAndStatistics_AndRemainSaveableAfterTerminalClose()
    {
        var service = CreateService();
        var statisticsReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var snapshotSubscription = service.Snapshots.Subscribe(snapshot =>
        {
            if (snapshot.StatisticsTelemetry is not null)
            {
                statisticsReady.TrySetResult();
            }
        });

        var travelBatch = WaitForGraphBatchAsync(
            service.GraphBatches,
            batch => batch.FrontTravel.Count == 5 && batch.RearTravel.Count == 5,
            TimeSpan.FromSeconds(2));
        var imuBatch = WaitForGraphBatchAsync(
            service.GraphBatches,
            batch => batch.ImuTimes.TryGetValue(LiveImuLocation.Frame, out var imuTimes) && imuTimes.Count == 1,
            TimeSpan.FromSeconds(2));

        await service.EnsureAttachedAsync();

        frames.OnNext(CreateTravelBatchFrame());
        frames.OnNext(CreateImuBatchFrame());
        frames.OnNext(CreateGpsBatchFrame());
        frames.OnNext(CreateGpsBatchFrame(
            timestamp: new DateTime(2026, 1, 2, 3, 4, 7, DateTimeKind.Utc),
            latitude: 42.6978,
            longitude: 23.3220,
            altitude: 601));

        await Task.WhenAll(travelBatch, imuBatch);
        await statisticsReady.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(service.Current.Controls.CanSave);
        Assert.NotNull(service.Current.StatisticsTelemetry);
        Assert.Equal(2, service.Current.SessionTrackPoints.Count);
        Assert.True(service.Current.SessionTrackPoints[0].Time < service.Current.SessionTrackPoints[1].Time);

        var capture = await service.PrepareCaptureForSaveAsync();
        Assert.Equal(5, capture.TelemetryCapture.FrontMeasurements.Length);
        Assert.Equal(2, capture.TelemetryCapture.GpsData!.Length);

        currentState = currentState with { IsClosed = true, LastError = "link lost" };
        states.OnNext(currentState);

        Assert.IsType<LiveSessionStreamPresentation.Closed>(service.Current.Stream);

        var partialCapture = await service.PrepareCaptureForSaveAsync();
        Assert.Equal(5, partialCapture.TelemetryCapture.RearMeasurements.Length);
    }

    [Fact]
    public async Task ResetCaptureAsync_ClearsAccumulatedCaptureAndStatistics()
    {
        var service = CreateService();
        await service.EnsureAttachedAsync();

        frames.OnNext(CreateTravelBatchFrame());
        frames.OnNext(CreateGpsBatchFrame());

        Assert.True(service.Current.Controls.CanSave);

        await service.ResetCaptureAsync();

        Assert.False(service.Current.Controls.CanSave);
        Assert.Null(service.Current.StatisticsTelemetry);
        Assert.Empty(service.Current.SessionTrackPoints);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PrepareCaptureForSaveAsync());
    }

    [Fact]
    public async Task TravelFrames_ThrottleStatisticsUpdatesToConfiguredInterval()
    {
        var service = CreateService();
        var firstStatisticsUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStatisticsUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var statisticsUpdateCount = 0;

        using var snapshotSubscription = service.Snapshots.Subscribe(snapshot =>
        {
            if (snapshot.StatisticsTelemetry is null)
            {
                return;
            }

            var count = Interlocked.Increment(ref statisticsUpdateCount);
            if (count == 1)
            {
                firstStatisticsUpdate.TrySetResult();
            }
            else if (count == 2)
            {
                secondStatisticsUpdate.TrySetResult();
            }
        });

        await service.EnsureAttachedAsync();

        frames.OnNext(CreateTravelBatchFrame());
        await firstStatisticsUpdate.Task.WaitAsync(TimeSpan.FromSeconds(2));

        frames.OnNext(CreateTravelBatchFrame());

        var secondUpdateArrivedTooSoon = await Task.WhenAny(secondStatisticsUpdate.Task, Task.Delay(250)) == secondStatisticsUpdate.Task;
        Assert.False(secondUpdateArrivedTooSoon);

        await secondStatisticsUpdate.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task TravelFrames_SkipIntermediateStatisticsRevisions_WhenRecomputeAlreadyRunning()
    {
        var blockingRunner = new BlockingOnceBackgroundTaskRunner();
        var service = CreateService(blockingRunner);
        var skippedRecomputesReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var snapshotSubscription = service.Snapshots.Subscribe(snapshot =>
        {
            if (snapshot.Controls.ClientDropCounters.StatisticsRecomputesSkipped > 0)
            {
                skippedRecomputesReady.TrySetResult();
            }
        });

        try
        {
            await service.EnsureAttachedAsync();

            frames.OnNext(CreateTravelBatchFrame());
            await blockingRunner.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            frames.OnNext(CreateTravelBatchFrame());
            frames.OnNext(CreateTravelBatchFrame());

            blockingRunner.Release();

            await skippedRecomputesReady.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.True(service.Current.Controls.ClientDropCounters.StatisticsRecomputesSkipped > 0);
        }
        finally
        {
            blockingRunner.Release();
            await service.DisposeAsync();
        }
    }

    [Fact]
    public async Task ResetCaptureAsync_RebasesSubsequentGraphTimes_AndClearsCaptureDuration()
    {
        var service = CreateService();

        var firstBatchTask = WaitForGraphBatchAsync(
            service.GraphBatches,
            batch => batch.FrontTravel.Count == 5,
            TimeSpan.FromSeconds(2));

        await service.EnsureAttachedAsync();

        frames.OnNext(CreateTravelBatchFrame(firstMonotonicUs: sessionHeader.SessionStartMonotonicUs));

        var firstBatch = await firstBatchTask;

        var resetBatchTask = WaitForGraphBatchAsync(
            service.GraphBatches,
            batch => batch.TravelTimes.Count == 0 && batch.Revision > firstBatch.Revision,
            TimeSpan.FromSeconds(2));

        await service.ResetCaptureAsync();

        var resetBatch = await resetBatchTask;

        Assert.Equal(TimeSpan.Zero, service.Current.Controls.CaptureDuration);

        var rebasedBatchTask = WaitForGraphBatchAsync(
            service.GraphBatches,
            batch => batch.FrontTravel.Count == 5 && batch.Revision > resetBatch.Revision,
            TimeSpan.FromSeconds(2));

        frames.OnNext(CreateTravelBatchFrame(firstMonotonicUs: sessionHeader.SessionStartMonotonicUs + 5_000_000));

        var rebasedBatch = await rebasedBatchTask;
        Assert.Equal(0d, rebasedBatch.TravelTimes[0]);
    }

    [Fact]
    public async Task Frames_TwoTravelFramesBeforeFlush_ProduceSingleMergedBatchWithIndependentGraphRevision()
    {
        var pipeline = new LiveGraphPipeline(TimeSpan.FromMilliseconds(200), Logger.None);
        var service = new LiveSessionService(
            CreateSessionContext(),
            sharedStream,
            sessionPresentationService,
            backgroundTaskRunner,
            pipeline);

        var mergedBatchTask = WaitForGraphBatchAsync(
            service.GraphBatches,
            batch => batch.FrontTravel.Count == 10,
            TimeSpan.FromSeconds(2));

        await service.EnsureAttachedAsync();

        frames.OnNext(CreateTravelBatchFrame());
        frames.OnNext(CreateTravelBatchFrame());

        var mergedBatch = await mergedBatchTask;

        Assert.Equal(1L, mergedBatch.Revision);
        Assert.True(service.Current.CaptureRevision >= 2L);

        await service.DisposeAsync();
    }

    [Fact]
    public async Task Frames_ImuOnlyInterval_StillEmitsBatchWithImuSamples()
    {
        var service = CreateService();

        var imuOnlyBatchTask = WaitForGraphBatchAsync(
            service.GraphBatches,
            batch => batch.TravelTimes.Count == 0
                && batch.FrontVelocity.Count == 0
                && batch.RearVelocity.Count == 0
                && batch.ImuTimes.TryGetValue(LiveImuLocation.Frame, out var times)
                && times.Count == 1,
            TimeSpan.FromSeconds(2));

        await service.EnsureAttachedAsync();

        frames.OnNext(CreateImuBatchFrame());

        await imuOnlyBatchTask;

        await service.DisposeAsync();
    }

    [Fact]
    public async Task TravelFrames_WhenDisplayPipelineStalls_CaptureContinuesAndDisplayDropsAreCounted()
    {
        var graphPipeline = new BlockingGraphPipeline();
        var service = CreateService(graphPipeline: graphPipeline);
        var displayDropsReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var snapshotSubscription = service.Snapshots.Subscribe(snapshot =>
        {
            if (snapshot.Controls.ClientDropCounters.GraphBatchesCoalesced > 0
                && snapshot.Controls.ClientDropCounters.StatisticsRecomputesSkipped > 0)
            {
                displayDropsReady.TrySetResult();
            }
        });

        try
        {
            await service.EnsureAttachedAsync();

            frames.OnNext(CreateTravelBatchFrame());
            await graphPipeline.AppendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            for (var index = 0; index < 20; index++)
            {
                frames.OnNext(CreateTravelBatchFrame(
                    firstMonotonicUs: sessionHeader.SessionStartMonotonicUs + (ulong)(index + 1) * 1_000_000));
            }

            await displayDropsReady.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.True(service.Current.Controls.CanSave);
            Assert.True(service.Current.Controls.ClientDropCounters.GraphBatchesCoalesced > 0);
            Assert.True(service.Current.Controls.ClientDropCounters.GraphSamplesDiscarded > 0);
            Assert.True(service.Current.Controls.ClientDropCounters.StatisticsRecomputesSkipped > 0);

            var capture = await service.PrepareCaptureForSaveAsync();
            Assert.Equal(105, capture.TelemetryCapture.FrontMeasurements.Length);
        }
        finally
        {
            graphPipeline.Release();
            await service.DisposeAsync();
        }
    }

    private ILiveSessionService CreateService(IBackgroundTaskRunner? runner = null, ILiveGraphPipeline? graphPipeline = null)
    {
        var pipeline = graphPipeline ?? new LiveGraphPipeline(TimeSpan.FromMilliseconds(5), Logger.None);
        return new LiveSessionService(
            CreateSessionContext(),
            sharedStream,
            sessionPresentationService,
            runner ?? backgroundTaskRunner,
            pipeline);
    }

    private sealed class BlockingGraphPipeline : ILiveGraphPipeline
    {
        private readonly Subject<LiveGraphBatch> graphBatches = new();
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int shouldBlock = 1;

        public TaskCompletionSource AppendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IObservable<LiveGraphBatch> GraphBatches => graphBatches.AsObservable();

        public void Start()
        {
        }

        public void AppendTravelSamples(ReadOnlySpan<double> times, ReadOnlySpan<double> frontTravel, ReadOnlySpan<double> rearTravel)
        {
            if (Interlocked.Exchange(ref shouldBlock, 0) == 1)
            {
                AppendStarted.TrySetResult();
                release.Task.Wait(TimeSpan.FromSeconds(5));
            }
        }

        public void AppendImuSamples(LiveImuLocation location, ReadOnlySpan<double> times, ReadOnlySpan<double> magnitudes)
        {
        }

        public void Reset()
        {
        }

        public void Release()
        {
            release.TrySetResult();
        }

        public ValueTask DisposeAsync()
        {
            Release();
            graphBatches.OnCompleted();
            graphBatches.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingOnceBackgroundTaskRunner : IBackgroundTaskRunner
    {
        private int shouldBlock = 1;
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task RunAsync(Func<Task> work, CancellationToken cancellationToken = default)
        {
            await WaitBeforeFirstWorkAsync(cancellationToken);
            await work();
        }

        public async Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken = default)
        {
            await WaitBeforeFirstWorkAsync(cancellationToken);
            return work();
        }

        public async Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default)
        {
            await WaitBeforeFirstWorkAsync(cancellationToken);
            return await work();
        }

        public void Release()
        {
            release.TrySetResult();
        }

        private async Task WaitBeforeFirstWorkAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref shouldBlock, 0) == 0)
            {
                return;
            }

            Started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        }
    }

    private static Task<LiveGraphBatch> WaitForGraphBatchAsync(
        IObservable<LiveGraphBatch> source,
        Func<LiveGraphBatch, bool> predicate,
        TimeSpan timeout)
    {
        return source
            .Where(predicate)
            .FirstAsync()
            .ToTask()
            .WaitAsync(timeout);
    }

    private LiveTravelBatchFrame CreateTravelBatchFrame(ulong? firstMonotonicUs = null)
    {
        return new LiveTravelBatchFrame(
            Header: new LiveFrameHeader(LiveProtocolConstants.Magic, LiveProtocolConstants.Version, LiveFrameType.TravelBatch, 0, 1),
            Batch: new LiveBatchHeader(sessionHeader.SessionId, 1, 0, firstMonotonicUs ?? sessionHeader.SessionStartMonotonicUs, 5),
            Records:
            [
                new LiveTravelRecord(1000, 1100),
                new LiveTravelRecord(1010, 1110),
                new LiveTravelRecord(1020, 1120),
                new LiveTravelRecord(1030, 1130),
                new LiveTravelRecord(1040, 1140),
            ]);
    }

    private LiveImuBatchFrame CreateImuBatchFrame()
    {
        return new LiveImuBatchFrame(
            Header: new LiveFrameHeader(LiveProtocolConstants.Magic, LiveProtocolConstants.Version, LiveFrameType.ImuBatch, 0, 2),
            Batch: new LiveBatchHeader(sessionHeader.SessionId, 1, 0, sessionHeader.SessionStartMonotonicUs, 1),
            Records:
            [
                new ImuRecord(0, 0, 16384, 0, 0, 0),
            ]);
    }

    private LiveGpsBatchFrame CreateGpsBatchFrame(
        DateTime? timestamp = null,
        double latitude = 42.6977,
        double longitude = 23.3219,
        float altitude = 600)
    {
        return new LiveGpsBatchFrame(
            Header: new LiveFrameHeader(LiveProtocolConstants.Magic, LiveProtocolConstants.Version, LiveFrameType.GpsBatch, 0, 3),
            Batch: new LiveBatchHeader(sessionHeader.SessionId, 1, 0, sessionHeader.SessionStartMonotonicUs, 1),
            Records:
            [
                new GpsRecord(
                    Timestamp: timestamp ?? new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc),
                    Latitude: latitude,
                    Longitude: longitude,
                    Altitude: altitude,
                    Speed: 10,
                    Heading: 90,
                    FixMode: 3,
                    Satellites: 12,
                    Epe2d: 0.5f,
                    Epe3d: 0.8f),
            ]);
    }

    private static LiveDaqSessionContext CreateSessionContext()
    {
        return new LiveDaqSessionContext(
            IdentityKey: "board-1",
            BoardId: Guid.NewGuid(),
            DisplayName: "Board 1",
            SetupId: Guid.NewGuid(),
            SetupName: "race",
            BikeId: Guid.NewGuid(),
            BikeName: "demo",
            BikeData: new BikeData(
                HeadAngle: 63,
                FrontMaxTravel: 180,
                RearMaxTravel: 170,
                FrontMeasurementToTravel: measurement => measurement / 10.0,
                RearMeasurementToTravel: measurement => measurement / 10.0),
            TravelCalibration: new LiveDaqTravelCalibration(null, null));
    }
}