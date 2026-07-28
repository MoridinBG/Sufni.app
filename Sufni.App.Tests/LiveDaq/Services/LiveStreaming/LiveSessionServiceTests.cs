using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
#if SUFNI_PROFILING_DIAGNOSTICS
using System.IO;
using System.Text.Json;
#endif
using NSubstitute;
using Serilog.Core;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

using Sufni.App.LiveDaq.Queries;
#if SUFNI_PROFILING_DIAGNOSTICS
using Sufni.App.LiveDaq.Services;
using Sufni.Profiling;
#endif
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Processing.SessionDetails;
namespace Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

#if SUFNI_PROFILING_DIAGNOSTICS
[Collection("ProfilingRuntime")]
#endif
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
                SelectedStreamMask: LiveStreamMask.Travel | LiveStreamMask.Imu | LiveStreamMask.Gps,
                IsConfigurationLocked: true,
                IsClosed: false);
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

#if SUFNI_PROFILING_DIAGNOSTICS
    [Fact]
    public async Task ProfilingCapture_EmitsTimeResourceAndSaveBoundaries()
    {
        ProfilingRuntime.Shutdown();
        var outputPath = Path.Combine(
            Path.GetTempPath(),
            $"sufni-bench01-{Guid.NewGuid():N}.jsonl");
        ProfilingRuntime.Initialize(new ProfilingOptions(
            ProfilingMode.Timing,
            RunId: "capture-boundaries-test",
            OutputPath: outputPath,
            Corpus: ProfilingLiveDaqReplay.Corpus,
            AppDataPath: Path.Combine(Path.GetTempPath(), $"sufni-bench01-state-{Guid.NewGuid():N}")));
        ILiveSessionService? service = null;
        try
        {
            service = CreateService();
            await service.EnsureAttachedAsync();
            var records = Enumerable.Range(0, 24_001)
                .Select(index => new LiveTravelRecord(
                    ForkAngle: (ushort)(1000 + index % 100),
                    ShockAngle: (ushort)(1100 + index % 100)))
                .ToArray();
            frames.OnNext(new LiveTravelBatchFrame(
                Header: new LiveFrameMetadata(1),
                Batch: new LiveBatchHeader(
                    sessionHeader.SessionId,
                    LiveStreamMask.Travel,
                    StreamSequence: 0,
                    FirstIndex: 0,
                    FirstMonotonicDeltaUs: 0,
                    FirstMonotonicUs: sessionHeader.SessionStartMonotonicUs,
                    SampleCount: (uint)records.Length,
                    ValidityMask: LiveSensorInstanceMask.Travel),
                Records: records));
            frames.OnNext(CreateV3ImuBatch(
                sessionHeader,
                firstIndex: 5,
                sampleCount: 12_000,
                validityMask: LiveSensorInstanceMask.FrameImu,
                records: Enumerable.Range(0, 12_000)
                    .Select(index => CreateImuRecord((short)index))
                    .ToArray()));
            frames.OnNext(CreateGpsBatchFrame());

            _ = await service.PrepareCaptureForSaveAsync();
            await service.ResetCaptureAsync();
            await service.DisposeAsync();
            service = null;
            ProfilingRuntime.Shutdown();

            var timing = await File.ReadAllTextAsync(outputPath);
            Assert.Contains("\"name\":\"CaptureStart\"", timing);
            Assert.Contains("\"name\":\"Capture.t15\"", timing);
            Assert.Contains("\"name\":\"Capture.t30\"", timing);
            Assert.Contains("\"name\":\"Capture.t60\"", timing);
            Assert.Contains("\"name\":\"Capture.t90\"", timing);
            Assert.Contains("\"name\":\"Capture.t120\"", timing);
            using (var t120 = JsonDocument.Parse(
                       timing.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                           .Single(line => line.Contains("\"name\":\"Capture.t120\"", StringComparison.Ordinal))))
            {
                Assert.Equal(59_996, t120.RootElement.GetProperty("payload").GetProperty("itemCount").GetInt64());
            }
            Assert.Contains("checkpoint=t120;durationSeconds=120.000000;observedDurationSeconds=120.005000;overrunItems=7", timing);
            Assert.Contains("imu=Frame:11995", timing);
            Assert.Contains("\"name\":\"Capture.save\"", timing);
            Assert.Contains("\"name\":\"Capture.Resources\"", timing);
            Assert.Contains("\"name\":\"Capture.FrontTravel.Samples\"", timing);
            Assert.Contains("\"stage\":\"LiveCapture.BuildCapture\"", timing);
        }
        finally
        {
            if (service is not null)
            {
                await service.DisposeAsync();
            }

            ProfilingRuntime.Shutdown();
            File.Delete(outputPath);
        }
    }
#endif

    [Fact]
    public async Task Frames_AccumulateSignalTrackAndAnalysis_AndRemainSaveableAfterTerminalClose()
    {
        var service = CreateService();
        var analysisReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var snapshotSubscription = service.Snapshots.Subscribe(snapshot =>
        {
            if (snapshot.AnalysisTelemetry is not null)
            {
                analysisReady.TrySetResult();
            }
        });

        var travelBatch = WaitForSignalBatchAsync(
            service.SignalBatches,
            batch => batch.FrontTravel.Count == 5 && batch.RearTravel.Count == 5,
            TimeSpan.FromSeconds(2));
        var imuBatch = WaitForSignalBatchAsync(
            service.SignalBatches,
            batch => batch.ImuTimes.TryGetValue(LiveImuLocation.Frame, out var imuTimes) && imuTimes.Count == 50,
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
        await analysisReady.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(service.Current.Controls.CanSave);
        Assert.NotNull(service.Current.AnalysisTelemetry);
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
    public async Task V3Frames_PrepareCaptureForSave_PreservesSegmentsGapsMarkersAndFinalStatus()
    {
        var service = CreateService();
        await service.EnsureAttachedAsync();

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

        var package = await service.PrepareCaptureForSaveAsync();
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
        var service = CreateService();
        await service.EnsureAttachedAsync();
        var temperatureHeader = sessionHeader with
        {
            AcceptedTemperatureRateMhz = 30,
            AcceptedStreamMask = LiveStreamMask.Travel | LiveStreamMask.Temperature,
            StreamDescriptors =
            [
                new SstV5StreamDescriptor
                {
                    StreamKind = SstV5ProtocolConstants.StreamTemperature,
                    TimingModelId = SstV5ProtocolConstants.TimingMonotonicEventStatus,
                    SourceDescriptorCount = 1,
                    AcceptedSensorMask = SstV5ProtocolConstants.SensorFrameImu,
                    AcceptedRateMhz = 30,
                    CompactPayloadRecordBytes = 2,
                    Sources =
                    [
                        new SstV5SourceDescriptor
                        {
                            StreamKind = SstV5ProtocolConstants.StreamTemperature,
                            SourceBitMask = SstV5ProtocolConstants.SensorFrameImu,
                            PayloadEncodingId = SstV5ProtocolConstants.EncodingTemperatureRawI16,
                            PayloadRecordBytes = 2,
                            PayloadValueCount = 1,
                            PayloadValueWidthBits = 16,
                            TemperatureLsbPerCelsius = 340,
                            TemperatureCelsiusAtRawZero = 36.53f,
                        },
                    ],
                },
            ],
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

        var capture = await service.PrepareCaptureForSaveAsync();
        Assert.Equal(21.5f, Assert.Single(capture.TelemetryCapture.TemperatureData).TemperatureCelsius);
        Assert.Null(capture.TelemetryCapture.ImuData);

        await service.ResetCaptureAsync();
        frames.OnNext(CreateTravelBatchFrame());
        var resetCapture = await service.PrepareCaptureForSaveAsync();
        Assert.Empty(resetCapture.TelemetryCapture.TemperatureData);
    }

    [Fact]
    public async Task V3Frames_PrepareCaptureForSave_MarksMissingFinalStatus_WhenClosedWithoutSessionResult()
    {
        var service = CreateService();
        await service.EnsureAttachedAsync();
        var v3Header = PublishV3TravelSession();

        frames.OnNext(CreateV3TravelBatch(v3Header, LiveSensorInstanceMask.ForkTravel));
        currentState = currentState with { IsClosed = true, LastError = "link lost" };
        states.OnNext(currentState);

        var package = await service.PrepareCaptureForSaveAsync();

        Assert.Null(package.TelemetryCapture.FinalStatus);
        Assert.True(package.TelemetryCapture.MissingFinalStatus);
    }

    [Fact]
    public async Task V3ImuFrames_PrepareCaptureForSave_UsesCanonicalSegmentsWithoutDenseDuplicate()
    {
        var service = CreateService();
        await service.EnsureAttachedAsync();
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

        var package = await service.PrepareCaptureForSaveAsync();
        var imuData = package.TelemetryCapture.ImuData;

        Assert.NotNull(imuData);
        Assert.Equal(new byte[] { (byte)LiveImuLocation.Frame, (byte)LiveImuLocation.Fork }, imuData!.ActiveLocations);
        Assert.False(imuData.HasGaps);
        Assert.Empty(imuData.Records);
        Assert.Equal(2, imuData.Segments.Count);
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
        var service = CreateService();
        await service.EnsureAttachedAsync();
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

        var package = await service.PrepareCaptureForSaveAsync();
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
    public async Task Frames_CalculateDampingPercentagesWithContextCutoffs()
    {
        var cutoffs = DampingSpeedCutoffs.FromValues(150, 250, 350, 450);
        var service = CreateService(context: CreateSessionContext(cutoffs));
        var analysisReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var snapshotSubscription = service.Snapshots.Subscribe(snapshot =>
        {
            if (snapshot.AnalysisTelemetry is not null)
            {
                analysisReady.TrySetResult();
            }
        });

        await service.EnsureAttachedAsync();
        frames.OnNext(CreateTravelBatchFrame());
        await analysisReady.Task.WaitAsync(TimeSpan.FromSeconds(2));

        sessionPresentationService.Received(1).CalculateDampingPercentages(
            Arg.Any<TelemetryData>(),
            Arg.Any<TelemetryTimeRange?>(),
            Arg.Any<VelocityAverageMode>(),
            Arg.Is<DampingSpeedCutoffs?>(value => value == cutoffs));
    }

    [Fact]
    public async Task ResetCaptureAsync_ClearsAccumulatedCaptureAndAnalysis()
    {
        var service = CreateService();
        await service.EnsureAttachedAsync();

        frames.OnNext(CreateTravelBatchFrame());
        frames.OnNext(CreateGpsBatchFrame());

        Assert.True(service.Current.Controls.CanSave);

        await service.ResetCaptureAsync();

        Assert.False(service.Current.Controls.CanSave);
        Assert.Null(service.Current.AnalysisTelemetry);
        Assert.Empty(service.Current.SessionTrackPoints);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PrepareCaptureForSaveAsync());
    }

    [Fact]
    public async Task SessionRollover_ClosesCaptureBeforeReset()
    {
        var service = CreateService();
        await service.EnsureAttachedAsync();
        frames.OnNext(CreateTravelBatchFrame());
        var nextHeader = CreateRolloverSessionHeader();

        PublishSharedState(nextHeader, isClosed: false);

        var closed = Assert.IsType<LiveSessionStreamPresentation.Closed>(service.Current.Stream);
        Assert.Equal("DAQ started a new live session.", closed.ErrorMessage);
        Assert.Equal(sessionHeader.SessionId, service.Current.Controls.SessionHeader?.SessionId);
        Assert.True(service.Current.Controls.CanSave);
    }

    [Fact]
    public async Task ResetCaptureAsync_AfterSessionRollover_AdoptsCurrentHeaderAndReopensStreaming()
    {
        var service = CreateService();
        await service.EnsureAttachedAsync();
        frames.OnNext(CreateTravelBatchFrame());
        frames.OnNext(new LiveSessionStatsFrame(
            new LiveFrameMetadata(10),
            new LiveSessionStats(sessionHeader.SessionId, 4, 5, 6, 7, 8, 9)));
        var nextHeader = CreateRolloverSessionHeader();
        PublishSharedState(nextHeader, isClosed: false);

        await service.ResetCaptureAsync();

        var streaming = Assert.IsType<LiveSessionStreamPresentation.Streaming>(service.Current.Stream);
        Assert.Equal(nextHeader.SessionId, streaming.SessionHeader.SessionId);
        Assert.Equal(nextHeader.SessionId, service.Current.Controls.SessionHeader?.SessionId);
        Assert.Null(service.Current.Controls.LastError);
        Assert.False(service.Current.Controls.CanSave);
        Assert.Equal(0u, service.Current.Controls.TravelQueueDepth);
        Assert.Equal(0u, service.Current.Controls.ImuQueueDepth);
        Assert.Equal(0u, service.Current.Controls.GpsQueueDepth);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ResetCaptureAsync_DoesNotReopenWithoutOpenStreamHeader(bool isClosed, bool removeHeaderBeforeReset)
    {
        var service = CreateService();
        await service.EnsureAttachedAsync();
        frames.OnNext(CreateTravelBatchFrame());
        var nextHeader = CreateRolloverSessionHeader();
        PublishSharedState(nextHeader, isClosed);
        if (removeHeaderBeforeReset)
        {
            currentState = currentState with { SessionHeader = null };
        }

        await service.ResetCaptureAsync();

        Assert.IsType<LiveSessionStreamPresentation.Closed>(service.Current.Stream);
        Assert.NotEqual(nextHeader.SessionId, service.Current.Controls.SessionHeader?.SessionId);
    }

    [Fact]
    public async Task ResetCaptureAsync_AfterSessionRollover_RejectsOldSessionFramesAndSavesNewSessionFrames()
    {
        var service = CreateService();
        await service.EnsureAttachedAsync();
        frames.OnNext(CreateTravelBatchFrame());
        var nextHeader = CreateRolloverSessionHeader();
        PublishSharedState(nextHeader, isClosed: false);
        await service.ResetCaptureAsync();

        PublishIdentityBearingFrames(sessionHeader, measurementBase: 1000, temperature: 11.5f, markerDeltaUs: 1_000_000, resultReason: 1, queueDepth: 99);
        PublishIdentityBearingFrames(nextHeader, measurementBase: 2000, temperature: 22.5f, markerDeltaUs: 2_000_000, resultReason: 2, queueDepth: 3);

        var package = await service.PrepareCaptureForSaveAsync();
        var capture = package.TelemetryCapture;
        Assert.Equal([2000, 2010, 2020, 2030, 2040], capture.FrontMeasurements);
        Assert.Equal(22.5f, Assert.Single(capture.TemperatureData).TemperatureCelsius);
        Assert.Single(capture.GpsData!);
        Assert.Equal(2.0, Assert.Single(capture.Markers).TimestampOffset);
        Assert.Equal((byte)2, capture.FinalStatus?.SessionResultReason);
        Assert.Equal(3u, service.Current.Controls.TravelQueueDepth);
        var imuSegment = Assert.Single(capture.ImuData!.Segments);
        Assert.Equal((short)20, Assert.Single(imuSegment.Records).Ax);
    }

    [Theory]
    [InlineData(GpsSpeedScenario.Incremental)]
    [InlineData(GpsSpeedScenario.OutOfOrderFallback)]
    public async Task GpsFrames_CalculateSpeeds_ForOrderedAndOutOfOrderFrames(GpsSpeedScenario scenario)
    {
        var service = CreateService();
        await service.EnsureAttachedAsync();

        frames.OnNext(CreateGpsBatchFrame());
        if (scenario is GpsSpeedScenario.OutOfOrderFallback)
        {
            frames.OnNext(CreateGpsBatchFrame(
                timestamp: new DateTime(2026, 1, 2, 3, 4, 8, DateTimeKind.Utc),
                latitude: 42.6979,
                longitude: 23.3221,
                altitude: 602));
        }

        frames.OnNext(CreateGpsBatchFrame(
            timestamp: new DateTime(2026, 1, 2, 3, 4, 7, DateTimeKind.Utc),
            latitude: 42.6978,
            longitude: 23.3220,
            altitude: 601));

        var points = service.Current.SessionTrackPoints;
        if (scenario is GpsSpeedScenario.Incremental)
        {
            Assert.Equal(2, points.Count);
            Assert.NotNull(points[1].Speed);
            Assert.True(points[1].Speed > 0);
            Assert.Equal(points[1].Speed, points[0].Speed);
            return;
        }

        Assert.Equal(3, points.Count);
        Assert.True(points[0].Time < points[1].Time);
        Assert.True(points[1].Time < points[2].Time);
        Assert.All(points, point => Assert.NotNull(point.Speed));
    }

    [Fact]
    public async Task TravelFrames_ThrottleAnalysisUpdatesToConfiguredInterval()
    {
        var service = CreateService();
        var firstAnalysisUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAnalysisUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var analysisUpdateCount = 0;

        using var snapshotSubscription = service.Snapshots.Subscribe(snapshot =>
        {
            if (snapshot.AnalysisTelemetry is null)
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

        await service.EnsureAttachedAsync();

        frames.OnNext(CreateTravelBatchFrame());
        await firstAnalysisUpdate.Task.WaitAsync(TimeSpan.FromSeconds(2));

        frames.OnNext(CreateTravelBatchFrame());

        var secondUpdateArrivedTooSoon = await Task.WhenAny(secondAnalysisUpdate.Task, Task.Delay(250)) == secondAnalysisUpdate.Task;
        Assert.False(secondUpdateArrivedTooSoon);

        await secondAnalysisUpdate.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task TravelFrames_SkipIntermediateAnalysisRevisions_WhenRecomputeAlreadyRunning()
    {
        var blockingRunner = new BlockingOnceBackgroundTaskRunner();
        var service = CreateService(blockingRunner);
        var skippedRecomputesReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var snapshotSubscription = service.Snapshots.Subscribe(snapshot =>
        {
            if (snapshot.Controls.ClientDropCounters.AnalysisRecomputesSkipped > 0)
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

            Assert.True(service.Current.Controls.ClientDropCounters.AnalysisRecomputesSkipped > 0);
        }
        finally
        {
            blockingRunner.Release();
            await service.DisposeAsync();
        }
    }

    [Fact]
    public async Task ResetCaptureAsync_RebasesSubsequentSignalTimes_AndClearsCaptureDuration()
    {
        var service = CreateService();

        var firstBatchTask = WaitForSignalBatchAsync(
            service.SignalBatches,
            batch => batch.FrontTravel.Count == 5,
            TimeSpan.FromSeconds(2));

        await service.EnsureAttachedAsync();

        frames.OnNext(CreateTravelBatchFrame(firstMonotonicUs: sessionHeader.SessionStartMonotonicUs));

        var firstBatch = await firstBatchTask;

        var resetBatchTask = WaitForSignalBatchAsync(
            service.SignalBatches,
            batch => batch.TravelTimes.Count == 0 && batch.Revision > firstBatch.Revision,
            TimeSpan.FromSeconds(2));

        await service.ResetCaptureAsync();

        var resetBatch = await resetBatchTask;

        Assert.Equal(TimeSpan.Zero, service.Current.Controls.CaptureDuration);

        var rebasedBatchTask = WaitForSignalBatchAsync(
            service.SignalBatches,
            batch => batch.FrontTravel.Count == 5 && batch.Revision > resetBatch.Revision,
            TimeSpan.FromSeconds(2));

        frames.OnNext(CreateTravelBatchFrame(firstMonotonicUs: sessionHeader.SessionStartMonotonicUs + 5_000_000));

        var rebasedBatch = await rebasedBatchTask;
        Assert.Equal(0d, rebasedBatch.TravelTimes[0]);
    }

    [Fact]
    public async Task TravelFrames_PublishDurationThroughLatestValidTravelEnd()
    {
        var service = CreateService();
        await service.EnsureAttachedAsync();

        frames.OnNext(CreateTravelBatchFrame());

        var expectedDurationUs = SstV5CompactPayloadDecoder.RoundDurationUs(
            5,
            sessionHeader.AcceptedTravelRateMhz);
        Assert.Equal(
            TimeSpan.FromMilliseconds(expectedDurationUs / 1000.0),
            service.Current.Controls.CaptureDuration);

        await service.DisposeAsync();
    }

    [Fact]
    public async Task Frames_TwoTravelFramesBeforeFlush_ProduceSingleMergedBatchWithIndependentSignalRevision()
    {
        var pipeline = new LiveSignalPipeline(TimeSpan.FromMilliseconds(200), Logger.None);
        var service = new LiveSessionService(
            CreateSessionContext(),
            sharedStream,
            sessionPresentationService,
            backgroundTaskRunner,
            pipeline);

        var mergedBatchTask = WaitForSignalBatchAsync(
            service.SignalBatches,
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

        var imuOnlyBatchTask = WaitForSignalBatchAsync(
            service.SignalBatches,
            batch => batch.TravelTimes.Count == 0
                && batch.FrontVelocity.Count == 0
                && batch.RearVelocity.Count == 0
                && batch.ImuTimes.TryGetValue(LiveImuLocation.Frame, out var times)
                && times.Count == 50,
            TimeSpan.FromSeconds(2));

        await service.EnsureAttachedAsync();

        frames.OnNext(CreateImuBatchFrame());

        await imuOnlyBatchTask;

        await service.DisposeAsync();
    }

    [Fact]
    public async Task TravelFrames_WhenDisplayPipelineStalls_CaptureContinuesAndDisplayDropsAreCounted()
    {
        var signalPipeline = new BlockingSignalPipeline();
        var service = CreateService(signalPipeline: signalPipeline);
        var displayDropsReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var snapshotSubscription = service.Snapshots.Subscribe(snapshot =>
        {
            if (snapshot.Controls.ClientDropCounters.SignalBatchesCoalesced > 0
                && snapshot.Controls.ClientDropCounters.AnalysisRecomputesSkipped > 0)
            {
                displayDropsReady.TrySetResult();
            }
        });

        try
        {
            await service.EnsureAttachedAsync();

            frames.OnNext(CreateTravelBatchFrame());
            await signalPipeline.AppendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            for (var index = 0; index < 20; index++)
            {
                frames.OnNext(CreateTravelBatchFrame(
                    firstMonotonicUs: sessionHeader.SessionStartMonotonicUs + (ulong)(index + 1) * 1_000_000));
            }

            await displayDropsReady.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.True(service.Current.Controls.CanSave);
            Assert.True(service.Current.Controls.ClientDropCounters.SignalBatchesCoalesced > 0);
            Assert.True(service.Current.Controls.ClientDropCounters.SignalSamplesDiscarded > 0);
            Assert.True(service.Current.Controls.ClientDropCounters.AnalysisRecomputesSkipped > 0);

            var capture = await service.PrepareCaptureForSaveAsync();
            Assert.Equal(105, capture.TelemetryCapture.FrontMeasurements.Length);
        }
        finally
        {
            signalPipeline.Release();
            await service.DisposeAsync();
        }
    }

    private LiveSessionHeader CreateRolloverSessionHeader()
    {
        return LiveProtocolTestFrames.CreateSessionHeaderModel(
            sessionId: sessionHeader.SessionId + 1,
            imuMask: LiveImuLocationMask.Frame,
            requestedSensorMask: LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.Gps,
            acceptedSensorMask: LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.Gps,
            protocolVersion: LiveProtocolVersion.V3) with
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
            ConnectionState = LiveConnectionState.Connected,
            LastError = null,
            SessionHeader = header,
            SelectedStreamMask = header.AcceptedStreamMask,
            IsClosed = isClosed,
            ProtocolVersion = header.ProtocolVersion,
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

    private ILiveSessionService CreateService(
        IBackgroundTaskRunner? runner = null,
        ILiveSignalPipeline? signalPipeline = null,
        LiveDaqSessionContext? context = null)
    {
        var pipeline = signalPipeline ?? new LiveSignalPipeline(TimeSpan.FromMilliseconds(5), Logger.None);
        return new LiveSessionService(
            context ?? CreateSessionContext(),
            sharedStream,
            sessionPresentationService,
            runner ?? backgroundTaskRunner,
            pipeline);
    }

    private sealed class BlockingSignalPipeline : ILiveSignalPipeline
    {
        private readonly Subject<LiveSignalBatch> signalBatches = new();
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int shouldBlock = 1;

        public TaskCompletionSource AppendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IObservable<LiveSignalBatch> SignalBatches => signalBatches.AsObservable();

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

        public void AppendImuSamples(LiveImuLocation location, ReadOnlySpan<double> times, ReadOnlySpan<double> vibrationRms)
        {
        }

        public void AppendFramePitchRollSamples(ReadOnlySpan<double> times, ReadOnlySpan<double> pitchDegrees, ReadOnlySpan<double> rollDegrees)
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
            signalBatches.OnCompleted();
            signalBatches.Dispose();
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

    private static Task<LiveSignalBatch> WaitForSignalBatchAsync(
        IObservable<LiveSignalBatch> source,
        Func<LiveSignalBatch, bool> predicate,
        TimeSpan timeout)
    {
        return source
            .Where(predicate)
            .FirstAsync()
            .ToTask()
            .WaitAsync(timeout);
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
            protocolVersion: LiveProtocolVersion.V3);
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

    private static LiveDaqSessionContext CreateSessionContext(DampingSpeedCutoffs? cutoffs = null)
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
            TravelCalibration: new LiveDaqTravelCalibration(null, null),
            DampingSpeedCutoffs: cutoffs ?? DampingSpeedCutoffs.Default,
            DampingSpeedCutoffOwner: new DampingSpeedCutoffOwner(Guid.Empty, 0));
    }

    public enum GpsSpeedScenario
    {
        Incremental,
        OutOfOrderFallback
    }
}
