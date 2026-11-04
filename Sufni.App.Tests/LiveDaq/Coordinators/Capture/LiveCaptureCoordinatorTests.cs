using Sufni.App.LiveDaq.Coordinators.Capture;
using Sufni.App.Tests.LiveDaq.Services.LiveStreaming;
using Sufni.App.Tests.TestSupport.LiveDaq;
using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Sufni.Telemetry;
using Sufni.App.Bikes.Models;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;

using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Processing.SessionDetails;
namespace Sufni.App.Tests.LiveDaq.Coordinators.Capture;

public partial class LiveCaptureCoordinatorTests
{
    private readonly SignalingFakeTimeProvider time = new();
    private readonly ILiveDaqSharedStream sharedStream = Substitute.For<ILiveDaqSharedStream>();
    private readonly ILiveDaqSharedStreamLease observerLease = Substitute.For<ILiveDaqSharedStreamLease>();
    private readonly ILiveDaqSharedStreamLease configurationLockLease = Substitute.For<ILiveDaqSharedStreamLease>();
    private readonly ISessionPresentationService sessionPresentationService = Substitute.For<ISessionPresentationService>();
    private readonly IBackgroundTaskRunner backgroundTaskRunner = new InlineBackgroundTaskRunner();
    private readonly BehaviorSubject<LiveDaqSharedStreamState> states = new(LiveDaqSharedStreamState.Empty);
    private readonly Subject<LiveProtocolFrame> frames = new();
    private readonly LiveSessionHeader sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(imuMask: LiveImuLocationMask.Frame);

    private LiveDaqSharedStreamState currentState = LiveDaqSharedStreamState.Empty;

    public LiveCaptureCoordinatorTests()
    {
        sharedStream.States.Returns(states);
        sharedStream.Frames.Returns(frames);
        sharedStream.CurrentState.Returns(_ => currentState);
        sharedStream.AcquireLease().Returns(new LiveStreamLeaseResult.Acquired(observerLease));
        sharedStream.AcquireConfigurationLock().Returns(new LiveStreamLeaseResult.Acquired(configurationLockLease));
        observerLease.DisposeAsync().Returns(ValueTask.CompletedTask);
        configurationLockLease.DisposeAsync().Returns(ValueTask.CompletedTask);
        sharedStream.ConnectLiveAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            currentState = new LiveDaqSharedStreamState(
                ConnectionState: new LiveConnectionState.Connected(),
                SessionHeader: sessionHeader,
                SelectedStreamMask: LiveStreamMask.Travel | LiveStreamMask.Imu | LiveStreamMask.Gps,
                IsConfigurationLocked: true);
            states.OnNext(currentState);
            return Task.FromResult<LivePreviewStartResult?>(
                new LivePreviewStartResult.Started(sessionHeader));
        });
        sessionPresentationService.CalculateDampingPercentages(
                Arg.Any<TelemetryData>(),
                Arg.Any<TelemetryTimeRange?>(),
                Arg.Any<VelocityAverageMode>(),
                Arg.Any<DampingSpeedCutoffs?>())
            .Returns(new SessionDampingPercentages(1, 2, 3, 4, 5, 6, 7, 8));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignalBatches_ConvertCalibratedTravelToVelocityAcrossBatchGaps(bool gap)
    {
        var signals = new LiveSignalPipelineFixture();
        await using var coordinator = CreateCoordinator(signalPipeline: signals.Pipeline);
        await coordinator.AttachAsync(TestContext.Current.CancellationToken);
        await signals.WaitForStartAsync();
        var header = sessionHeader with { AcceptedTravelRateMhz = 100_000 };
        PublishSharedState(header, isClosed: false);
        await coordinator.ResetAsync(TestContext.Current.CancellationToken);
        LiveTravelBatchFrame Batch(int start, int count, int deltaMs) => new(
            new LiveFrameMetadata(1),
            new LiveBatchHeader(header.SessionId, 1, (ulong)start,
                header.SessionStartMonotonicUs + (ulong)deltaMs * 1000, (uint)count),
            Enumerable.Range(start, count).Select(index => new LiveTravelRecord((ushort)(10 * index), (ushort)(20 * index))).ToArray());
        if (gap)
        {
            var first = WaitForSignalBatchAsync(coordinator.SignalBatches, batch => batch.FrontTravel.Count == 3, TimeSpan.FromSeconds(2));
            frames.OnNext(Batch(0, 3, 0));
            await signals.FlushTravelAsync();
            var initial = await first;
            Assert.Equal(3, initial.FrontVelocity.Count);
            Assert.All(initial.FrontVelocity, value => Assert.True(double.IsNaN(value)));
        }
        var count = gap ? 4 : 7;
        var next = WaitForSignalBatchAsync(coordinator.SignalBatches, batch => batch.FrontTravel.Count == count, TimeSpan.FromSeconds(2));
        frames.OnNext(Batch(gap ? 3 : 0, count, gap ? 40 : 0));
        await signals.FlushTravelAsync();
        var batch = await next;
        double[] expected = gap ? [80, 80, 100, 100] : [100, 100, 100, 100, 100, 100, 100];
        Assert.Equal(count, batch.FrontVelocity.Count);
        Assert.Equal(count, batch.RearVelocity.Count);
        for (var index = 0; index < count; index++)
        {
            Assert.Equal(expected[index], batch.FrontVelocity[index], 1e-8);
            Assert.Equal(2 * expected[index], batch.RearVelocity[index], 1e-8);
        }
    }

    [Theory]
    [InlineData(1.0, new double[] { 0, 0, 0, 1, 1 })]
    [InlineData(null, new double[] { -2, -1, 0, 1, 2 })]
    public async Task SignalBatches_ClampCalibratedTravelOnlyWhenLimitIsKnown(double? maxTravel, double[] expected)
    {
        var context = CreateSessionContext() with
        {
            BikeData = new BikeData(maxTravel, null, measurement => measurement / 10.0 - 102, null),
        };
        await using var coordinator = CreateCoordinator(
            context: context, signalPipeline: new ControllableLiveSignalPipeline(blockFirstTravel: false));
        await coordinator.AttachAsync(TestContext.Current.CancellationToken);
        var next = WaitForSignalBatchAsync(coordinator.SignalBatches,
            batch => batch.TravelTimes.Count > 0, TimeSpan.FromSeconds(2));

        frames.OnNext(CreateTravelBatchFrame());
        var batch = await next;

        Assert.Equal(expected, batch.FrontTravel);
        Assert.All(batch.RearTravel, value => Assert.True(double.IsNaN(value)));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task SignalBatches_NonFiniteCalibrationProducesMissingSamples(double calibratedTravel)
    {
        var context = CreateSessionContext() with
        {
            BikeData = new BikeData(180, 170, _ => calibratedTravel, measurement => measurement / 10.0),
        };
        await using var coordinator = CreateCoordinator(
            context: context, signalPipeline: new ControllableLiveSignalPipeline(blockFirstTravel: false));
        await coordinator.AttachAsync(TestContext.Current.CancellationToken);
        var next = WaitForSignalBatchAsync(coordinator.SignalBatches,
            batch => batch.TravelTimes.Count > 0, TimeSpan.FromSeconds(2));

        frames.OnNext(CreateTravelBatchFrame());
        var batch = await next;

        Assert.All(batch.FrontTravel, value => Assert.True(double.IsNaN(value)));
        Assert.Equal(new double[] { 110, 111, 112, 113, 114 }, batch.RearTravel);
    }

    [Fact]
    public async Task PrepareForSaveAsync_PreservesMissingTravelIndicesAsSeparateSegments()
    {
        await using var coordinator = CreateCoordinator();
        await coordinator.AttachAsync(TestContext.Current.CancellationToken);
        var header = PublishV3TravelSession();
        var first = CreateV3TravelBatch(header, LiveSensorInstanceMask.ForkTravel);
        frames.OnNext(first);
        frames.OnNext(first with
        {
            Batch = first.Batch with
            {
                FirstIndex = 8,
                FirstMonotonicDeltaUs = 40_000,
                FirstMonotonicUs = header.SessionStartMonotonicUs + 40_000,
            },
        });

        var package = await coordinator.PrepareForSaveAsync(TestContext.Current.CancellationToken);
        var segments = package.TelemetryCapture.FrontSegments;

        Assert.Equal(new ulong[] { 0, 8 }, segments.Select(segment => segment.FirstIndex));
        Assert.Equal(new ulong[] { 0, 40_000 }, segments.Select(segment => segment.FirstMonotonicDeltaUs));
        Assert.All(segments, segment => Assert.Equal(new ushort[] { 1000, 1010, 1020, 1030, 1040 }, segment.Counts));
        Assert.Contains(package.TelemetryCapture.StreamGaps, gap =>
            gap.LocationId == (byte)SstV5ProtocolConstants.SensorForkTravel &&
            gap.FirstMissingIndex == 5 && gap.MissingCount == 3);
    }

    [Theory]
    [InlineData(false, 0.025)]
    [InlineData(true, 3.0)]
    public async Task PrepareForSaveAsync_DurationUsesFinalStatusThenTravelAheadOfGps(
        bool hasFinalStatus, double expectedSeconds)
    {
        await using var coordinator = CreateCoordinator();
        await coordinator.AttachAsync(TestContext.Current.CancellationToken);
        frames.OnNext(CreateTravelBatchFrame(firstMonotonicUs: sessionHeader.SessionStartMonotonicUs + 5_000_000));
        var gps = CreateGpsBatchFrame();
        frames.OnNext(gps with
        {
            Batch = gps.Batch with { FirstMonotonicUs = sessionHeader.SessionStartMonotonicUs + 10_000_000 },
        });
        if (hasFinalStatus)
        {
            frames.OnNext(new LiveSessionResultFrame(new LiveFrameMetadata(22),
                new LiveSessionResult(sessionHeader.SessionId, new SstFinalStatus
                {
                    StoppedMonotonicDeltaUs = 8_000_000,
                    Streams = [],
                })));
        }

        var package = await coordinator.PrepareForSaveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expectedSeconds, package.TelemetryCapture.Metadata.Duration, 6);
    }

    [Fact]
    public async Task AttachAsync_WhenPortBusy_ReturnsHolderAndCanRetry()
    {
        var busy = new LivePreviewStartResult.Busy(new DaqTransportHolder.Management(DaqManagementOperation.ReadConfig));
        sharedStream.ConnectLiveAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<LivePreviewStartResult?>(busy),
                Task.FromResult<LivePreviewStartResult?>(new LivePreviewStartResult.Started(sessionHeader)));
        await using var coordinator = CreateCoordinator();

        Assert.Equal(busy, await coordinator.AttachAsync(TestContext.Current.CancellationToken));
        Assert.False(coordinator.Current.Controls.CanSave);
        Assert.IsType<LivePreviewStartResult.Started>(await coordinator.AttachAsync(TestContext.Current.CancellationToken));
        sharedStream.Received(1).AcquireLease();
        sharedStream.Received(1).AcquireConfigurationLock();
    }

    [Fact]
    public async Task PrepareForSaveAsync_PreservesTravelAfterTerminalClose()
    {
        await using var coordinator = CreateCoordinator();
        await coordinator.AttachAsync(TestContext.Current.CancellationToken);
        frames.OnNext(CreateTravelBatchFrame());
        var before = await coordinator.PrepareForSaveAsync(TestContext.Current.CancellationToken);
        currentState = currentState with { ConnectionState = new LiveConnectionState.Closed(LiveStreamClosedReason.ConnectionLost, "link lost") };
        states.OnNext(currentState);
        var after = await coordinator.PrepareForSaveAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(after.TelemetryCapture.FrontMeasurements);
        Assert.Equal(before.TelemetryCapture.FrontMeasurements, after.TelemetryCapture.FrontMeasurements);
        Assert.Equal(before.TelemetryCapture.RearMeasurements, after.TelemetryCapture.RearMeasurements);
    }

    [Fact]
    public async Task V3Frames_PrepareCaptureForSave_PreservesSegmentsGapsMarkersAndFinalStatus()
    {
        await using var coordinator = CreateCoordinator();
        await coordinator.AttachAsync(cancellationToken: TestContext.Current.CancellationToken);

        var v3Header = PublishV3TravelSession();

        frames.OnNext(CreateV3TravelBatch(v3Header, LiveSensorInstanceMask.ForkTravel));
        frames.OnNext(new LiveMarkerBatchFrame(
            new LiveFrameMetadata(21),
            new LiveBatchHeader(
                SessionId: v3Header.SessionId,
                Stream: LiveStreamMask.Marker,
                StreamSequence: 0,
                FirstIndex: 0,
                FirstMonotonicDeltaUs: 1_500_000,
                FirstMonotonicUs: v3Header.SessionStartMonotonicUs + 1_500_000,
                SampleCount: 1,
                ValidityMask: LiveSensorInstanceMask.None),
            [new LiveMarkerRecord(0, 1_500_000, SstV5ProtocolConstants.MarkerManualUserMark)]));
        frames.OnNext(new LiveSessionResultFrame(
            new LiveFrameMetadata(22),
            new LiveSessionResult(
                v3Header.SessionId,
                new SstFinalStatus
                {
                    SessionResultReason = 3,
                    StoppedMonotonicDeltaUs = 2_000_000,
                    Streams = [],
                })));

        var package = await coordinator.PrepareForSaveAsync(cancellationToken: TestContext.Current.CancellationToken);
        var capture = package.TelemetryCapture;

        var frontSegment = Assert.Single(capture.FrontSegments);
        Assert.Equal((ulong)0, frontSegment.FirstIndex);
        Assert.Equal([1000, 1010, 1020, 1030, 1040], frontSegment.Counts);
        Assert.Empty(capture.RearSegments);
        var gap = Assert.Single(capture.StreamGaps);
        Assert.Equal(SstV5ProtocolConstants.StreamTravel, gap.StreamKind);
        Assert.Equal((byte)SstV5ProtocolConstants.SensorShockTravel, gap.LocationId);
        Assert.Equal((ulong)0, gap.FirstMissingIndex);
        Assert.Equal((ulong)5, gap.MissingCount);
        Assert.Equal("invalid_validity", gap.Reason);
        var marker = Assert.Single(capture.Markers);
        Assert.Equal(1.5, marker.TimestampOffset);
        Assert.NotNull(capture.FinalStatus);
        Assert.Equal((byte)3, capture.FinalStatus.SessionResultReason);
        Assert.False(capture.MissingFinalStatus);
    }

    [Fact]
    public async Task TemperatureFrames_AreSavedSeparatelyFromImu_AndClearedOnReset()
    {
        await using var coordinator = CreateCoordinator();
        await coordinator.AttachAsync(cancellationToken: TestContext.Current.CancellationToken);
        var temperatureHeader = sessionHeader with
        {
            AcceptedTemperatureRateMhz = 30,
            AcceptedStreamMask = LiveStreamMask.Travel | LiveStreamMask.Temperature,
        };
        currentState = currentState with
        {
            SessionHeader = temperatureHeader,
            SelectedStreamMask = LiveStreamMask.Travel | LiveStreamMask.Temperature,
        };
        states.OnNext(currentState);

        frames.OnNext(CreateTravelBatchFrame());
        frames.OnNext(new LiveTemperatureBatchFrame(
            new LiveFrameMetadata(9),
            new LiveBatchHeader(
                temperatureHeader.SessionId,
                LiveStreamMask.Temperature,
                StreamSequence: 9,
                FirstIndex: 0,
                FirstMonotonicDeltaUs: 500_000,
                FirstMonotonicUs: temperatureHeader.SessionStartMonotonicUs + 500_000,
                SampleCount: 1,
                ValidityMask: LiveSensorInstanceMask.FrameImu),
            [
                new LiveTemperatureRecord(
                    0,
                    500_000,
                    LiveSensorInstanceMask.FrameImu,
                    new TemperatureSample(
                        temperatureHeader.SessionStartUtc.ToUnixTimeSeconds(),
                        (byte)LiveImuLocation.Frame,
                        21.5f)),
            ]));

        var capture = await coordinator.PrepareForSaveAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(21.5f, Assert.Single(capture.TelemetryCapture.TemperatureData).TemperatureCelsius);
        Assert.Null(capture.TelemetryCapture.ImuData);

        await coordinator.ResetAsync(cancellationToken: TestContext.Current.CancellationToken);
        frames.OnNext(CreateTravelBatchFrame());
        var resetCapture = await coordinator.PrepareForSaveAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(resetCapture.TelemetryCapture.TemperatureData);
    }

    [Fact]
    public async Task V3Frames_PrepareCaptureForSave_MarksMissingFinalStatus_WhenClosedWithoutSessionResult()
    {
        await using var coordinator = CreateCoordinator();
        await coordinator.AttachAsync(cancellationToken: TestContext.Current.CancellationToken);
        var v3Header = PublishV3TravelSession();

        frames.OnNext(CreateV3TravelBatch(v3Header, LiveSensorInstanceMask.ForkTravel));
        currentState = currentState with { ConnectionState = new LiveConnectionState.Closed(LiveStreamClosedReason.ConnectionLost, "link lost") };
        states.OnNext(currentState);

        var package = await coordinator.PrepareForSaveAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(package.TelemetryCapture.FinalStatus);
        Assert.True(package.TelemetryCapture.MissingFinalStatus);
    }

    [Fact]
    public async Task V3ImuFrames_PrepareCaptureForSave_UsesCanonicalSegmentsWithoutDenseDuplicate()
    {
        await using var coordinator = CreateCoordinator();
        await coordinator.AttachAsync(cancellationToken: TestContext.Current.CancellationToken);
        var v3Header = PublishV3TravelAndImuSession();
        var frame0 = CreateImuRecord(10);
        var fork0 = CreateImuRecord(20);
        var frame1 = CreateImuRecord(30);
        var fork1 = CreateImuRecord(40);

        frames.OnNext(CreateV3TravelBatch(v3Header, LiveSensorInstanceMask.Travel));
        frames.OnNext(CreateV3ImuBatch(
            v3Header,
            firstIndex: 0,
            sampleCount: 2,
            LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.ForkImu,
            [frame0, fork0, frame1, fork1]));

        var package = await coordinator.PrepareForSaveAsync(cancellationToken: TestContext.Current.CancellationToken);
        var imuData = package.TelemetryCapture.ImuData;

        Assert.NotNull(imuData);
        Assert.Equal(new byte[] { (byte)LiveImuLocation.Frame, (byte)LiveImuLocation.Fork }, imuData!.ActiveLocations);
        Assert.False(imuData.HasGaps);
        Assert.Empty(imuData.Records);
        Assert.Equal(2, imuData.Segments.Length);
        Assert.All(imuData.Segments, segment =>
        {
            Assert.Equal((ulong)0, segment.FirstIndex);
            Assert.Equal(2, segment.Records.Length);
        });
        Assert.Equal(
            [frame0, frame1],
            imuData.Segments.Single(segment => segment.LocationId == (byte)LiveImuLocation.Frame).Records);
        Assert.Equal(
            [fork0, fork1],
            imuData.Segments.Single(segment => segment.LocationId == (byte)LiveImuLocation.Fork).Records);
        Assert.DoesNotContain(package.TelemetryCapture.StreamGaps, gap =>
            gap.StreamKind == SstV5ProtocolConstants.StreamImu);
    }

    [Fact]
    public async Task V3ImuFrames_PrepareCaptureForSave_PreservesValidityAndIndexGapsByLocation()
    {
        await using var coordinator = CreateCoordinator();
        await coordinator.AttachAsync(cancellationToken: TestContext.Current.CancellationToken);
        var v3Header = PublishV3TravelAndImuSession();

        frames.OnNext(CreateV3TravelBatch(v3Header, LiveSensorInstanceMask.Travel));
        frames.OnNext(CreateV3ImuBatch(
            v3Header,
            firstIndex: 0,
            sampleCount: 2,
            LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.ForkImu,
            [CreateImuRecord(10), CreateImuRecord(20), CreateImuRecord(30), CreateImuRecord(40)]));
        frames.OnNext(CreateV3ImuBatch(
            v3Header,
            firstIndex: 2,
            sampleCount: 1,
            LiveSensorInstanceMask.FrameImu,
            [CreateImuRecord(50)]));
        frames.OnNext(CreateV3ImuBatch(
            v3Header,
            firstIndex: 4,
            sampleCount: 1,
            LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.ForkImu,
            [CreateImuRecord(60), CreateImuRecord(70)]));

        var package = await coordinator.PrepareForSaveAsync(cancellationToken: TestContext.Current.CancellationToken);
        var imuData = package.TelemetryCapture.ImuData;

        Assert.NotNull(imuData);
        Assert.True(imuData!.HasGaps);
        Assert.Empty(imuData.Records);

        var frameSegments = imuData.Segments
            .Where(segment => segment.LocationId == (byte)LiveImuLocation.Frame)
            .OrderBy(segment => segment.FirstIndex)
            .ToArray();
        var forkSegments = imuData.Segments
            .Where(segment => segment.LocationId == (byte)LiveImuLocation.Fork)
            .OrderBy(segment => segment.FirstIndex)
            .ToArray();
        Assert.Equal([0UL, 4UL], frameSegments.Select(segment => segment.FirstIndex).ToArray());
        Assert.Equal([0UL, 4UL], forkSegments.Select(segment => segment.FirstIndex).ToArray());
        Assert.Equal(3, frameSegments[0].Records.Length);
        Assert.Equal(2, forkSegments[0].Records.Length);

        var imuGaps = package.TelemetryCapture.StreamGaps
            .Where(gap => gap.StreamKind == SstV5ProtocolConstants.StreamImu)
            .ToArray();
        Assert.Contains(imuGaps, gap =>
            gap.LocationId == (byte)LiveImuLocation.Fork &&
            gap.FirstMissingIndex == 2 &&
            gap.MissingCount == 1 &&
            gap.Reason == "invalid_validity");
        Assert.Contains(imuGaps, gap =>
            gap.LocationId == (byte)LiveImuLocation.Frame &&
            gap.FirstMissingIndex == 3 &&
            gap.MissingCount == 1 &&
            gap.Reason == "index_gap");
        Assert.Contains(imuGaps, gap =>
            gap.LocationId == (byte)LiveImuLocation.Fork &&
            gap.FirstMissingIndex == 3 &&
            gap.MissingCount == 1 &&
            gap.Reason == "index_gap");
    }

    [Fact]
    public async Task GpsFrames_DerivingFirstPointSpeed_DoesNotMutatePublishedSnapshot()
    {
        await using var coordinator = CreateCoordinator();
        await coordinator.AttachAsync(TestContext.Current.CancellationToken);
        frames.OnNext(CreateGpsBatchFrame());
        time.Advance(TimeSpan.FromMilliseconds(33));
        var before = coordinator.Current;
        var first = Assert.Single(before.TrackPoints);
        var originalSpeed = first.Speed;

        frames.OnNext(CreateGpsBatchFrame(
            timestamp: new DateTime(2026, 1, 2, 3, 4, 7, DateTimeKind.Utc),
            latitude: 42.6978, longitude: 23.3220, altitude: 601));

        time.Advance(TimeSpan.FromMilliseconds(33));
        Assert.Equal(originalSpeed, first.Speed);
        Assert.NotSame(first, coordinator.Current.TrackPoints[0]);
        Assert.True(coordinator.Current.TrackPoints[0].Speed > 0);
    }

    [Fact]
    public async Task ResetAsync_WhenStreamClosesDuringReset_DoesNotPublishOlderOpenSnapshot()
    {
        var pipeline = new ControllableLiveSignalPipeline();
        await using var coordinator = CreateCoordinator(signalPipeline: pipeline);
        await coordinator.AttachAsync(TestContext.Current.CancellationToken);
        var observed = new List<LiveCaptureState>();
        using var subscription = coordinator.States.Subscribe(observed.Add);
        pipeline.OnReset = () => PublishSharedState(sessionHeader, isClosed: true);

        await coordinator.ResetAsync(TestContext.Current.CancellationToken);

        Assert.IsType<LiveConnectionState.Closed>(coordinator.Current.Controls.ConnectionState);
        Assert.IsType<LiveConnectionState.Closed>(observed[^1].Controls.ConnectionState);
    }

    [Fact]
    public async Task ResetAsync_ClearsAccumulatedCaptureAndAnalysis()
    {
        await using var coordinator = CreateCoordinator();
        await coordinator.AttachAsync(cancellationToken: TestContext.Current.CancellationToken);

        frames.OnNext(CreateTravelBatchFrame());
        frames.OnNext(CreateGpsBatchFrame());

        Assert.True(coordinator.Current.Controls.CanSave);

        await coordinator.ResetAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(coordinator.Current.Controls.CanSave);
        Assert.Null(coordinator.Current.Analysis?.Telemetry);
        Assert.Empty(coordinator.Current.TrackPoints);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.PrepareForSaveAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SessionRollover_ClosesCaptureBeforeReset()
    {
        await using var coordinator = CreateCoordinator();
        await coordinator.AttachAsync(cancellationToken: TestContext.Current.CancellationToken);
        frames.OnNext(CreateTravelBatchFrame());
        var nextHeader = CreateRolloverSessionHeader();

        PublishSharedState(nextHeader, isClosed: false);

        Assert.Equal(LiveCapturePhase.AwaitingReset, coordinator.Current.Controls.Phase);
        Assert.Equal(sessionHeader.SessionId, coordinator.Current.Controls.SessionHeader?.SessionId);
        Assert.True(coordinator.Current.Controls.CanSave);
    }

    [Fact]
    public async Task ResetAsync_AfterSessionRollover_AdoptsCurrentHeaderAndReopensStreaming()
    {
        await using var coordinator = CreateCoordinator();
        await coordinator.AttachAsync(cancellationToken: TestContext.Current.CancellationToken);
        frames.OnNext(CreateTravelBatchFrame());
        frames.OnNext(new LiveSessionStatsFrame(
            new LiveFrameMetadata(10),
            new LiveSessionStats(sessionHeader.SessionId, 4, 5, 6, 7, 8, 9)));
        var nextHeader = CreateRolloverSessionHeader();
        PublishSharedState(nextHeader, isClosed: false);

        await coordinator.ResetAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(nextHeader.SessionId, coordinator.Current.Controls.SessionHeader?.SessionId);
        Assert.Null(coordinator.Current.Controls.LastError);
        Assert.False(coordinator.Current.Controls.CanSave);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ResetAsync_DoesNotReopenWithoutOpenStreamHeader(bool isClosed, bool removeHeaderBeforeReset)
    {
        await using var coordinator = CreateCoordinator();
        await coordinator.AttachAsync(cancellationToken: TestContext.Current.CancellationToken);
        frames.OnNext(CreateTravelBatchFrame());
        var nextHeader = CreateRolloverSessionHeader();
        PublishSharedState(nextHeader, isClosed);
        if (removeHeaderBeforeReset)
        {
            currentState = currentState with { SessionHeader = null };
        }

        await coordinator.ResetAsync(cancellationToken: TestContext.Current.CancellationToken);

        frames.OnNext(CreateTravelBatchFrame());
        frames.OnNext(CreateTravelBatchFrame(nextHeader, measurementBase: 2000));
        Assert.False(coordinator.Current.Controls.CanSave);
    }

    [Fact]
    public async Task ResetAsync_AfterConnectionClosure_DoesNotResumeCaptureOrRestartTransport()
    {
        await using var coordinator = CreateCoordinator();
        await coordinator.AttachAsync(TestContext.Current.CancellationToken);
        frames.OnNext(CreateTravelBatchFrame());
        PublishSharedState(sessionHeader, isClosed: true);

        await coordinator.ResetAsync(TestContext.Current.CancellationToken);
        frames.OnNext(CreateTravelBatchFrame());
        var result = await coordinator.AttachAsync(TestContext.Current.CancellationToken);

        Assert.IsType<LivePreviewStartResult.Closed>(result);
        Assert.False(coordinator.Current.Controls.CanSave);
        await sharedStream.Received(1).ConnectLiveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetAsync_AfterSessionRollover_RejectsOldSessionFramesAndSavesNewSessionFrames()
    {
        await using var coordinator = CreateCoordinator();
        await coordinator.AttachAsync(cancellationToken: TestContext.Current.CancellationToken);
        frames.OnNext(CreateTravelBatchFrame());
        var nextHeader = CreateRolloverSessionHeader();
        PublishSharedState(nextHeader, isClosed: false);
        await coordinator.ResetAsync(cancellationToken: TestContext.Current.CancellationToken);

        PublishIdentityBearingFrames(sessionHeader, measurementBase: 1000, temperature: 11.5f, markerDeltaUs: 1_000_000, resultReason: 1, queueDepth: 99);
        PublishIdentityBearingFrames(nextHeader, measurementBase: 2000, temperature: 22.5f, markerDeltaUs: 2_000_000, resultReason: 2, queueDepth: 3);

        var package = await coordinator.PrepareForSaveAsync(cancellationToken: TestContext.Current.CancellationToken);
        var capture = package.TelemetryCapture;
        Assert.Equal([2000, 2010, 2020, 2030, 2040], capture.FrontMeasurements);
        Assert.Equal(22.5f, Assert.Single(capture.TemperatureData).TemperatureCelsius);
        Assert.Single(capture.GpsData!);
        Assert.Equal(2.0, Assert.Single(capture.Markers).TimestampOffset);
        Assert.Equal((byte)2, capture.FinalStatus?.SessionResultReason);
        var imuSegment = Assert.Single(capture.ImuData!.Segments);
        Assert.Equal((short)20, Assert.Single(imuSegment.Records).Ax);
    }

    [Fact]
    public async Task GpsFrames_OutOfOrderRecordInsideBatch_PreservesEntireRawBatchAndReprojectsAllPoints()
    {
        await using var coordinator = CreateCoordinator();
        await coordinator.AttachAsync(cancellationToken: TestContext.Current.CancellationToken);
        frames.OnNext(CreateTravelBatchFrame());
        var time8 = new DateTime(2026, 1, 2, 3, 4, 8, DateTimeKind.Utc);
        var time7 = time8.AddSeconds(-1);
        var time9 = time8.AddSeconds(1);
        frames.OnNext(CreateGpsBatchFrame(time8, latitude: 42.6978));
        frames.OnNext(new LiveGpsBatchFrame(
            new LiveFrameMetadata(4),
            new LiveBatchHeader(sessionHeader.SessionId, 2, 0, sessionHeader.SessionStartMonotonicUs, 2),
            [
                CreateGpsRecord(time7, latitude: 42.6977),
                CreateGpsRecord(time9, latitude: 42.6979),
            ]));

        time.Advance(TimeSpan.FromMilliseconds(33));
        var points = coordinator.Current.TrackPoints;
        Assert.Equal(3, points.Count);
        Assert.True(points[0].Time < points[1].Time);
        Assert.True(points[1].Time < points[2].Time);

        var package = await coordinator.PrepareForSaveAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            [time8, time7, time9],
            package.TelemetryCapture.GpsData!.Select(record => record.Timestamp).ToArray());
    }

    [Fact]
    public async Task AnalysisOptionsChangedDuringComputation_RejectsOldModeAndPublishesNewResults()
    {
        var runner = new BlockingOnceBackgroundTaskRunner();
        await using var coordinator = CreateCoordinator(runner);
        var options = LiveAnalysisOptions.Default(DampingSpeedCutoffs.Default) with
        {
            VelocityMode = VelocityAverageMode.StrokePeakAveraged
        };
        var ready = coordinator.States.Where(snapshot => snapshot.Analysis?.Options == options)
            .FirstAsync().ToTask().AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        var publishedModes = new List<VelocityAverageMode>();
        using var subscription = coordinator.States.Subscribe(snapshot =>
        {
            if (snapshot.Analysis is { } analysis) publishedModes.Add(analysis.Options.VelocityMode);
        });
        try
        {
            await coordinator.AttachAsync(TestContext.Current.CancellationToken);
            frames.OnNext(CreateTravelBatchFrame());
            await runner.Started.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
            var captureRevision = coordinator.Current.CaptureRevision;
            coordinator.UpdateAnalysisOptions(options);
            runner.Release();
            await time.WaitForTimerAsync(TimeSpan.FromMilliseconds(1500), TestContext.Current.CancellationToken);
            time.Advance(TimeSpan.FromMilliseconds(Sufni.App.Shared.Plots.PlotSettings.LiveAnalysisRefreshIntervalMs));
            var snapshot = await ready;

            Assert.Equal(captureRevision, snapshot.CaptureRevision);
            Assert.All(publishedModes, mode => Assert.Equal(options.VelocityMode, mode));
            var key = snapshot.Analysis!.Inputs.CreateKey(
                Sufni.App.Sessions.Analysis.Services.RecordedSessionAnalysisFamily.VelocityDistribution, SuspensionType.Front);
            Assert.IsType<Sufni.App.Sessions.Analysis.Services.VelocityDistributionAnalysisResult>(snapshot.Analysis.Results[key]);
        }
        finally { runner.Release(); }
    }

    [Fact]
    public async Task AnalysisFailure_PublishesSafeReasonWithoutLosingCapturedSamples()
    {
        sessionPresentationService.CalculateDampingPercentages(Arg.Any<TelemetryData>(),
            Arg.Any<TelemetryTimeRange?>(), Arg.Any<VelocityAverageMode>(), Arg.Any<DampingSpeedCutoffs?>())
            .Returns(_ => throw new InvalidOperationException("private diagnostic"));
        await using var coordinator = CreateCoordinator();
        var failure = coordinator.States.Where(snapshot => snapshot.AnalysisFailure is not null)
            .FirstAsync().ToTask().AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await coordinator.AttachAsync(TestContext.Current.CancellationToken);
        frames.OnNext(CreateTravelBatchFrame());

        var snapshot = await failure;

        Assert.Equal(LiveAnalysisFailureReason.Calculation, snapshot.AnalysisFailure!.Reason);
        Assert.Contains("private diagnostic", snapshot.AnalysisFailure.Details);
        var capture = await coordinator.PrepareForSaveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(5, capture.TelemetryCapture.FrontMeasurements.Length);
    }

    [Fact]
    public async Task TravelFrames_ThrottleAnalysisUpdatesToConfiguredInterval()
    {
        var runner = new BlockingOnceBackgroundTaskRunner();
        await using var coordinator = CreateCoordinator(runner);
        var firstAnalysisUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAnalysisUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var analysisUpdateCount = 0;

        using var snapshotSubscription = coordinator.States.DistinctUntilChanged(snapshot => snapshot.Analysis).Subscribe(snapshot =>
        {
            if (snapshot.Analysis?.Telemetry is null)
            {
                return;
            }

            var count = Interlocked.Increment(ref analysisUpdateCount);
            if (count == 1)
            {
                firstAnalysisUpdate.TrySetResult();
            }
            else if (count == 2)
            {
                secondAnalysisUpdate.TrySetResult();
            }
        });

        await coordinator.AttachAsync(cancellationToken: TestContext.Current.CancellationToken);

        frames.OnNext(CreateTravelBatchFrame());
        await runner.Started.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        frames.OnNext(CreateTravelBatchFrame(firstMonotonicUs: sessionHeader.SessionStartMonotonicUs + 25_000));
        runner.Release();
        await firstAnalysisUpdate.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await time.WaitForTimerAsync(TimeSpan.FromMilliseconds(1500), TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        time.Advance(TimeSpan.FromMilliseconds(1499));
        Assert.False(secondAnalysisUpdate.Task.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1));

        await secondAnalysisUpdate.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(10, coordinator.Current.Analysis!.Telemetry.Front.Travel.Length);
    }

    [Fact]
    public async Task TravelFrames_AnalysisCatchesUpAfterPendingRecompute()
    {
        var blockingRunner = new BlockingOnceBackgroundTaskRunner();
        await using var coordinator = CreateCoordinator(blockingRunner);
        var latestAnalysisReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var snapshotSubscription = coordinator.States.Subscribe(snapshot =>
        {
            if (snapshot.Analysis?.Telemetry.Front.Travel.Length == 15)
            {
                latestAnalysisReady.TrySetResult();
            }
        });

        try
        {
            await coordinator.AttachAsync(cancellationToken: TestContext.Current.CancellationToken);

            frames.OnNext(CreateTravelBatchFrame());
            await blockingRunner.Started.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

            frames.OnNext(CreateTravelBatchFrame(firstMonotonicUs: sessionHeader.SessionStartMonotonicUs + 25_000));
            frames.OnNext(CreateTravelBatchFrame(firstMonotonicUs: sessionHeader.SessionStartMonotonicUs + 50_000));

            blockingRunner.Release();
            await time.WaitForTimerAsync(TimeSpan.FromMilliseconds(1500), TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(2));
            time.Advance(TimeSpan.FromMilliseconds(1500));

            await latestAnalysisReady.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(15, coordinator.Current.Analysis!.Telemetry.Front.Travel.Length);
        }
        finally
        {
            blockingRunner.Release();
            await coordinator.DisposeAsync();
        }
    }

    [Fact]
    public async Task ResetAsync_RebasesSubsequentSignalTimes_AndClearsCaptureDuration()
    {
        var signals = new LiveSignalPipelineFixture();
        await using var coordinator = CreateCoordinator(signalPipeline: signals.Pipeline);

        var firstBatchTask = WaitForSignalBatchAsync(
            coordinator.SignalBatches,
            batch => batch.FrontTravel.Count == 5,
            TimeSpan.FromSeconds(2));

        await coordinator.AttachAsync(cancellationToken: TestContext.Current.CancellationToken);

        await signals.WaitForStartAsync();
        frames.OnNext(CreateTravelBatchFrame(firstMonotonicUs: sessionHeader.SessionStartMonotonicUs));
        await signals.FlushTravelAsync();

        var firstBatch = await firstBatchTask;

        var resetBatchTask = WaitForSignalBatchAsync(
            coordinator.SignalBatches,
            batch => batch.TravelTimes.Count == 0 && batch.Revision > firstBatch.Revision,
            TimeSpan.FromSeconds(2));

        await coordinator.ResetAsync(cancellationToken: TestContext.Current.CancellationToken);

        signals.Flush();
        var resetBatch = await resetBatchTask;

        Assert.Equal(TimeSpan.Zero, coordinator.Current.Controls.CaptureDuration);

        var rebasedBatchTask = WaitForSignalBatchAsync(
            coordinator.SignalBatches,
            batch => batch.FrontTravel.Count == 5 && batch.Revision > resetBatch.Revision,
            TimeSpan.FromSeconds(2));

        frames.OnNext(CreateTravelBatchFrame(firstMonotonicUs: sessionHeader.SessionStartMonotonicUs + 5_000_000));

        await signals.FlushTravelAsync();
        var rebasedBatch = await rebasedBatchTask;
        Assert.Equal(0d, rebasedBatch.TravelTimes[0]);
    }

    [Fact]
    public async Task GpsOnlyDuration_UsesDeviceTimeAndStopsAfterStreamClosure()
    {
        await using var coordinator = CreateCoordinator();
        await coordinator.AttachAsync(TestContext.Current.CancellationToken);
        frames.OnNext(CreateGpsBatchFrame());
        var later = CreateGpsBatchFrame(timestamp: sessionHeader.SessionStartUtc.UtcDateTime.AddDays(1));
        later = later with { Batch = later.Batch with { FirstMonotonicUs = sessionHeader.SessionStartMonotonicUs + 2_000_000 } };
        frames.OnNext(later);
        time.Advance(TimeSpan.FromMilliseconds(33));
        Assert.Equal(TimeSpan.FromSeconds(2), coordinator.Current.Controls.CaptureDuration);

        PublishSharedState(sessionHeader, isClosed: true);
        time.Advance(TimeSpan.FromMinutes(1));
        frames.OnNext(later with { Batch = later.Batch with { FirstMonotonicUs = sessionHeader.SessionStartMonotonicUs + 60_000_000 } });
        time.Advance(TimeSpan.FromMilliseconds(33));
        Assert.Equal(TimeSpan.FromSeconds(2), coordinator.Current.Controls.CaptureDuration);

        await coordinator.ResetAsync(TestContext.Current.CancellationToken);
        Assert.Equal(TimeSpan.Zero, coordinator.Current.Controls.CaptureDuration);
    }

    [Fact]
    public async Task TravelFrames_PublishDurationThroughLatestValidTravelEnd()
    {
        await using var coordinator = CreateCoordinator();
        await coordinator.AttachAsync(cancellationToken: TestContext.Current.CancellationToken);

        frames.OnNext(CreateTravelBatchFrame());

        Assert.Equal(TimeSpan.FromMilliseconds(25), coordinator.Current.Controls.CaptureDuration);

    }

    [Fact]
    public async Task Frames_ImuOnlyInterval_StillEmitsBatchWithImuSamples()
    {
        var signals = new LiveSignalPipelineFixture();
        await using var coordinator = CreateCoordinator(signalPipeline: signals.Pipeline);

        var imuOnlyBatchTask = WaitForSignalBatchAsync(
            coordinator.SignalBatches,
            batch => batch.TravelTimes.Count == 0
                && batch.FrontVelocity.Count == 0
                && batch.RearVelocity.Count == 0
                && batch.ImuTimes.TryGetValue(LiveImuLocation.Frame, out var times)
                && times.Count == 50,
            TimeSpan.FromSeconds(2));

        await coordinator.AttachAsync(cancellationToken: TestContext.Current.CancellationToken);

        await signals.WaitForStartAsync();
        frames.OnNext(CreateImuBatchFrame());
        await signals.FlushImuAsync();

        await imuOnlyBatchTask;

        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task TravelFrames_WhenDisplayPipelineStalls_CaptureRemainsComplete()
    {
        var signalPipeline = new ControllableLiveSignalPipeline();
        await using var coordinator = CreateCoordinator(signalPipeline: signalPipeline, sendOptions: false);
        try
        {
            await coordinator.AttachAsync(cancellationToken: TestContext.Current.CancellationToken);

            frames.OnNext(CreateTravelBatchFrame());
            await signalPipeline.AppendStarted.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

            var publishedRevision = coordinator.Current.Revision;
            for (var index = 0; index < 20; index++)
            {
                frames.OnNext(CreateTravelBatchFrame(
                    firstMonotonicUs: sessionHeader.SessionStartMonotonicUs + (ulong)(index + 1) * 1_000_000));
            }

            Assert.True(coordinator.Current.Controls.CanSave);
            Assert.Equal(publishedRevision, coordinator.Current.Revision);

            var capture = await coordinator.PrepareForSaveAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(105, capture.TelemetryCapture.FrontMeasurements.Length);
        }
        finally
        {
            signalPipeline.Release();
            await coordinator.DisposeAsync();
        }
    }

    private LiveSessionHeader CreateRolloverSessionHeader()
    {
        return LiveProtocolTestFrames.CreateSessionHeaderModel(
            sessionId: sessionHeader.SessionId + 1,
            imuMask: LiveImuLocationMask.Frame,
            requestedSensorMask: LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.Gps,
            acceptedSensorMask: LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.Gps,
            expectsFinalStatus: true) with
        {
            AcceptedTemperatureRateMhz = 30,
            AcceptedStreamMask = LiveStreamMask.Travel |
                LiveStreamMask.Imu |
                LiveStreamMask.Temperature |
                LiveStreamMask.Gps |
                LiveStreamMask.Battery |
                LiveStreamMask.Marker,
        };
    }

    private void PublishSharedState(LiveSessionHeader header, bool isClosed)
    {
        currentState = currentState with
        {
            ConnectionState = isClosed ? new LiveConnectionState.Closed(LiveStreamClosedReason.ConnectionLost) : new LiveConnectionState.Connected(),
            SessionHeader = header,
            SelectedStreamMask = header.AcceptedStreamMask,
        };
        states.OnNext(currentState);
    }

    private void PublishIdentityBearingFrames(
        LiveSessionHeader header,
        ushort measurementBase,
        float temperature,
        ulong markerDeltaUs,
        byte resultReason,
        uint queueDepth)
    {
        frames.OnNext(CreateTravelBatchFrame(header, measurementBase));
        frames.OnNext(CreateV3ImuBatch(
            header,
            firstIndex: 0,
            sampleCount: 1,
            LiveSensorInstanceMask.FrameImu,
            [CreateImuRecord((short)(measurementBase / 100))]));
        frames.OnNext(new LiveTemperatureBatchFrame(
            new LiveFrameMetadata(41),
            new LiveBatchHeader(
                header.SessionId,
                LiveStreamMask.Temperature,
                StreamSequence: 0,
                FirstIndex: 0,
                FirstMonotonicDeltaUs: 500_000,
                FirstMonotonicUs: header.SessionStartMonotonicUs + 500_000,
                SampleCount: 1,
                ValidityMask: LiveSensorInstanceMask.FrameImu),
            [
                new LiveTemperatureRecord(
                    0,
                    500_000,
                    LiveSensorInstanceMask.FrameImu,
                    new TemperatureSample(
                        header.SessionStartUtc.ToUnixTimeSeconds(),
                        (byte)LiveImuLocation.Frame,
                        temperature)),
            ]));
        frames.OnNext(CreateGpsBatchFrame(
            header,
            timestamp: header.SessionStartUtc.UtcDateTime.AddSeconds(1),
            latitude: 42.6977 + header.SessionId / 1_000_000.0));
        frames.OnNext(new LiveBatteryBatchFrame(
            new LiveFrameMetadata(43),
            new LiveBatchHeader(
                header.SessionId,
                LiveStreamMask.Battery,
                StreamSequence: 0,
                FirstIndex: 0,
                FirstMonotonicDeltaUs: 1_000_000,
                FirstMonotonicUs: header.SessionStartMonotonicUs + 1_000_000,
                SampleCount: 1,
                ValidityMask: LiveSensorInstanceMask.None),
            [new LiveBatteryRecord(0, 1_000_000, 4000, 0)]));
        frames.OnNext(new LiveMarkerBatchFrame(
            new LiveFrameMetadata(44),
            new LiveBatchHeader(
                header.SessionId,
                LiveStreamMask.Marker,
                StreamSequence: 0,
                FirstIndex: 0,
                FirstMonotonicDeltaUs: markerDeltaUs,
                FirstMonotonicUs: header.SessionStartMonotonicUs + markerDeltaUs,
                SampleCount: 1,
                ValidityMask: LiveSensorInstanceMask.None),
            [new LiveMarkerRecord(0, markerDeltaUs, SstV5ProtocolConstants.MarkerManualUserMark)]));
        frames.OnNext(new LiveSessionStatsFrame(
            new LiveFrameMetadata(45),
            new LiveSessionStats(header.SessionId, queueDepth, queueDepth, queueDepth, 0, 0, 0)));
        frames.OnNext(new LiveSessionResultFrame(
            new LiveFrameMetadata(46),
            new LiveSessionResult(
                header.SessionId,
                new SstFinalStatus
                {
                    SessionResultReason = resultReason,
                    StoppedMonotonicDeltaUs = 3_000_000,
                    Streams = [],
                })));
    }

    private ILiveCaptureCoordinator CreateCoordinator(
        IBackgroundTaskRunner? runner = null,
        ILiveSignalPipeline? signalPipeline = null,
        LiveDaqSessionContext? context = null,
        IUiThreadDispatcher? dispatcher = null, bool sendOptions = true, bool observe = true)
    {
        var pipeline = signalPipeline ?? new ControllableLiveSignalPipeline(false);
        var coordinator = new LiveCaptureCoordinator(
            context ?? CreateSessionContext(),
            sharedStream,
            sessionPresentationService,
            runner ?? backgroundTaskRunner,
            pipeline,
            time, dispatcher ?? new InlineUiThreadDispatcher());
        if (sendOptions) coordinator.UpdateAnalysisOptions(LiveAnalysisOptions.Default(DampingSpeedCutoffs.Default));
        if (observe) coordinator.States.Subscribe(_ => { });
        return coordinator;
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

    private static Task<LiveSignalBatch> WaitForSignalBatchAsync(
        IObservable<LiveSignalBatch> source,
        Func<LiveSignalBatch, bool> predicate,
        TimeSpan timeout)
    {
        return source
            .Where(predicate)
            .FirstAsync()
            .ToTask()
            .AwaitBoundedAsync(timeout);
    }

    private LiveTravelBatchFrame CreateTravelBatchFrame(ulong? firstMonotonicUs = null) =>
        CreateTravelBatchFrame(sessionHeader, 1000, firstMonotonicUs);

    private static LiveTravelBatchFrame CreateTravelBatchFrame(
        LiveSessionHeader header,
        ushort measurementBase,
        ulong? firstMonotonicUs = null)
    {
        return new LiveTravelBatchFrame(
            Header: new LiveFrameMetadata(1),
            Batch: new LiveBatchHeader(header.SessionId, 1, 0, firstMonotonicUs ?? header.SessionStartMonotonicUs, 5),
            Records:
            [
                new LiveTravelRecord(measurementBase, (ushort)(measurementBase + 100)),
                new LiveTravelRecord((ushort)(measurementBase + 10), (ushort)(measurementBase + 110)),
                new LiveTravelRecord((ushort)(measurementBase + 20), (ushort)(measurementBase + 120)),
                new LiveTravelRecord((ushort)(measurementBase + 30), (ushort)(measurementBase + 130)),
                new LiveTravelRecord((ushort)(measurementBase + 40), (ushort)(measurementBase + 140)),
            ]);
    }

    private LiveSessionHeader PublishV3TravelSession()
    {
        return PublishV3Session(
            requestedSensorMask: LiveSensorInstanceMask.Travel,
            acceptedSensorMask: LiveSensorInstanceMask.Travel,
            activeImuMask: LiveImuLocationMask.None,
            selectedStreamMask: LiveStreamMask.Travel);
    }

    private LiveSessionHeader PublishV3TravelAndImuSession()
    {
        const LiveSensorInstanceMask acceptedSensors =
            LiveSensorInstanceMask.Travel |
            LiveSensorInstanceMask.FrameImu |
            LiveSensorInstanceMask.ForkImu;

        return PublishV3Session(
            requestedSensorMask: acceptedSensors,
            acceptedSensorMask: acceptedSensors,
            activeImuMask: LiveImuLocationMask.Frame | LiveImuLocationMask.Fork,
            selectedStreamMask: LiveStreamMask.Travel | LiveStreamMask.Imu);
    }

    private LiveSessionHeader PublishV3Session(
        LiveSensorInstanceMask requestedSensorMask,
        LiveSensorInstanceMask acceptedSensorMask,
        LiveImuLocationMask activeImuMask,
        LiveStreamMask selectedStreamMask)
    {
        var v3Header = LiveProtocolTestFrames.CreateSessionHeaderModel(
            sessionId: 903,
            imuMask: activeImuMask,
            requestedSensorMask: requestedSensorMask,
            acceptedSensorMask: acceptedSensorMask,
            expectsFinalStatus: true);
        currentState = currentState with
        {
            SessionHeader = v3Header,
            SelectedStreamMask = selectedStreamMask,
            ProtocolVersion = LiveProtocolVersion.V3,
        };
        states.OnNext(currentState);
        return v3Header;
    }

    private static LiveTravelBatchFrame CreateV3TravelBatch(
        LiveSessionHeader header,
        LiveSensorInstanceMask validityMask)
    {
        return new LiveTravelBatchFrame(
            Header: new LiveFrameMetadata(20),
            Batch: new LiveBatchHeader(
                SessionId: header.SessionId,
                Stream: LiveStreamMask.Travel,
                StreamSequence: 0,
                FirstIndex: 0,
                FirstMonotonicDeltaUs: 0,
                FirstMonotonicUs: header.SessionStartMonotonicUs,
                SampleCount: 5,
                ValidityMask: validityMask),
            Records:
            [
                new LiveTravelRecord(1000, 0),
                new LiveTravelRecord(1010, 0),
                new LiveTravelRecord(1020, 0),
                new LiveTravelRecord(1030, 0),
                new LiveTravelRecord(1040, 0),
            ]);
    }

    private static LiveImuBatchFrame CreateV3ImuBatch(
        LiveSessionHeader header,
        ulong firstIndex,
        uint sampleCount,
        LiveSensorInstanceMask validityMask,
        IReadOnlyList<ImuRecord> records)
    {
        var firstMonotonicDeltaUs = SstV5CompactPayloadDecoder.RoundDurationUs(
            firstIndex,
            header.AcceptedImuRateMhz);
        return new LiveImuBatchFrame(
            Header: new LiveFrameMetadata((uint)(30 + firstIndex)),
            Batch: new LiveBatchHeader(
                SessionId: header.SessionId,
                Stream: LiveStreamMask.Imu,
                StreamSequence: (uint)firstIndex,
                FirstIndex: firstIndex,
                FirstMonotonicDeltaUs: firstMonotonicDeltaUs,
                FirstMonotonicUs: header.SessionStartMonotonicUs + firstMonotonicDeltaUs,
                SampleCount: sampleCount,
                ValidityMask: validityMask),
            Records: records);
    }

    private static ImuRecord CreateImuRecord(short value) =>
        new(
            value,
            (short)(value + 1),
            (short)(value + 2),
            (short)(value + 3),
            (short)(value + 4),
            (short)(value + 5));

    private LiveImuBatchFrame CreateImuBatchFrame()
    {
        const int tickCount = 50;

        return new LiveImuBatchFrame(
            Header: new LiveFrameMetadata(2),
            Batch: new LiveBatchHeader(sessionHeader.SessionId, 1, 0, sessionHeader.SessionStartMonotonicUs, tickCount),
            Records: CreateRestImuRecords(tickCount));
    }

    private static ImuRecord[] CreateRestImuRecords(int tickCount)
    {
        var records = new ImuRecord[tickCount * 2];
        for (var index = 0; index < tickCount; index++)
        {
            records[index * 2] = new ImuRecord(0, 0, 16384, 0, 0, 0);
            records[index * 2 + 1] = new ImuRecord(0, 0, 8192, 0, 0, 0);
        }

        return records;
    }

    private LiveGpsBatchFrame CreateGpsBatchFrame(
        DateTime? timestamp = null,
        double latitude = 42.6977,
        double longitude = 23.3219,
        float altitude = 600) =>
        CreateGpsBatchFrame(sessionHeader, timestamp, latitude, longitude, altitude);

    private static LiveGpsBatchFrame CreateGpsBatchFrame(
        LiveSessionHeader header,
        DateTime? timestamp = null,
        double latitude = 42.6977,
        double longitude = 23.3219,
        float altitude = 600)
    {
        return new LiveGpsBatchFrame(
            Header: new LiveFrameMetadata(3),
            Batch: new LiveBatchHeader(header.SessionId, 1, 0, header.SessionStartMonotonicUs, 1),
            Records:
            [
                CreateGpsRecord(
                    timestamp ?? new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc),
                    latitude,
                    longitude,
                    altitude),
            ]);
    }

    private static GpsRecord CreateGpsRecord(
        DateTime timestamp,
        double latitude,
        double longitude = 23.3219,
        float altitude = 600) =>
        new(
            Timestamp: timestamp,
            Latitude: latitude,
            Longitude: longitude,
            Altitude: altitude,
            Speed: 10,
            Heading: 90,
            FixMode: 3,
            Satellites: 12,
            Epe2d: 0.5f,
            Epe3d: 0.8f);

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
                FrontMaxTravel: 180,
                RearMaxTravel: 170,
                FrontMeasurementToTravel: measurement => measurement / 10.0,
                RearMeasurementToTravel: measurement => measurement / 10.0),
            TravelCalibration: new LiveDaqTravelCalibration(null, null));
    }

}
