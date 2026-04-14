using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.SessionDetails;
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
                new LivePreviewStartResult.Started(sessionHeader, LiveSensorMask.Travel | LiveSensorMask.Imu | LiveSensorMask.Gps));
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
    public async Task Frames_AccumulateGraphTrackAndStatistics_AndRemainSaveableAfterTerminalClose()
    {
        var service = CreateService();
        var statisticsReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var graphBatches = new List<LiveGraphBatch>();

        using var snapshotSubscription = service.Snapshots.Subscribe(snapshot =>
        {
            if (snapshot.StatisticsTelemetry is not null)
            {
                statisticsReady.TrySetResult();
            }
        });
        using var graphSubscription = service.GraphBatches.Subscribe(graphBatches.Add);

        await service.EnsureAttachedAsync();

        frames.OnNext(CreateTravelBatchFrame());
        frames.OnNext(CreateImuBatchFrame());
        frames.OnNext(CreateGpsBatchFrame());
        frames.OnNext(CreateGpsBatchFrame(
            timestamp: new DateTime(2026, 1, 2, 3, 4, 7, DateTimeKind.Utc),
            latitude: 42.6978,
            longitude: 23.3220,
            altitude: 601));

        await statisticsReady.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Contains(graphBatches, batch => batch.FrontTravel.Count == 5 && batch.RearTravel.Count == 5);
        Assert.Contains(graphBatches, batch =>
            batch.ImuTimes.TryGetValue(LiveImuLocation.Frame, out var imuTimes) && imuTimes.Count == 1);
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

    private ILiveSessionService CreateService()
    {
        return new LiveSessionService(CreateSessionContext(), sharedStream, sessionPresentationService, backgroundTaskRunner);
    }

    private LiveTravelBatchFrame CreateTravelBatchFrame()
    {
        return new LiveTravelBatchFrame(
            Header: new LiveFrameHeader(LiveProtocolConstants.Magic, LiveProtocolConstants.Version, LiveFrameType.TravelBatch, 0, 1),
            Batch: new LiveBatchHeader(sessionHeader.SessionId, 1, 0, sessionHeader.SessionStartMonotonicUs, 5),
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