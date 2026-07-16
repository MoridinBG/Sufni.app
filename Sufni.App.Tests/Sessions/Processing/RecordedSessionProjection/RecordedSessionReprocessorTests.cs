using System.Text;
using System.Threading;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.Setups.Stores;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Sessions.Processing.RecordedSessionProjection;

public class RecordedSessionReprocessorTests
{
    [Fact]
    public async Task ReprocessAsync_ImportedSst_DecompressesStoredPayloadBeforeParsing()
    {
        var session = TestSnapshots.Session(id: Guid.NewGuid(), setupId: Guid.NewGuid());
        var bike = TestSnapshots.Bike(id: Guid.NewGuid());
        var setup = TestSnapshots.Setup(id: session.SetupId!.Value, bikeId: bike.Id) with
        {
            FrontSensorConfigurationJson = SensorConfiguration.ToJson(new LinearForkSensorConfiguration
            {
                Length = 10,
                Resolution = 12
            })
        };
        var sstBytes = TestSstFiles.CreateV3WithFrontOnly();
        var payload = RecordedSessionSourcePayloadCodec.CompressImportedSst(sstBytes);
        var source = new RecordedSessionSource
        {
            SessionId = session.Id,
            SourceKind = RecordedSessionSourceKind.ImportedSst,
            SourceName = "compressed-source.SST",
            SchemaVersion = 1,
            SourceHash = RecordedSessionSourceHash.Compute(
                RecordedSessionSourceKind.ImportedSst,
                "compressed-source.SST",
                1,
                payload),
            Payload = payload
        };
        var domain = new RecordedSessionDomainSnapshot(
            session,
            setup,
            bike,
            null,
            null,
            RecordedSessionSourceSnapshot.From(source),
            null,
            new SessionStaleness.MissingProcessedData(),
            DerivedChangeKind.None);
        var reprocessor = CreateReprocessor();

        var result = await reprocessor.ReprocessAsync(domain, source);

        var telemetryData = result.ProcessedTelemetry.TelemetryData;
        Assert.Equal("compressed-source.SST", telemetryData.Metadata.SourceName);
        Assert.Equal(3, telemetryData.Metadata.Version);
        Assert.NotEmpty(telemetryData.Front.Travel);
        Assert.Equal(telemetryData.BinaryForm, result.ProcessedTelemetry.Data);
        Assert.Equal(AppJson.Serialize(result.Fingerprint), result.ProcessedTelemetry.FingerprintJson);
    }

    [Fact]
    public async Task ReprocessAsync_ImportedSst_WithDerivationWindow_SlicesBeforeProcessing()
    {
        var session = TestSnapshots.Session(id: Guid.NewGuid(), setupId: Guid.NewGuid());
        var bike = TestSnapshots.Bike(id: Guid.NewGuid());
        var setup = TestSnapshots.Setup(id: session.SetupId!.Value, bikeId: bike.Id) with
        {
            FrontSensorConfigurationJson = SensorConfiguration.ToJson(new LinearForkSensorConfiguration
            {
                Length = 10,
                Resolution = 12
            })
        };
        var sourceSessionId = Guid.NewGuid();
        var sstBytes = TestSstFiles.CreateV3WithFrontOnly(timestamp: 1_700_000_000, sampleCount: 200);
        var payload = RecordedSessionSourcePayloadCodec.CompressImportedSst(sstBytes);
        var source = new RecordedSessionSource
        {
            SessionId = sourceSessionId,
            SourceKind = RecordedSessionSourceKind.ImportedSst,
            SourceName = "window-source.SST",
            SchemaVersion = 1,
            SourceHash = RecordedSessionSourceHash.Compute(
                RecordedSessionSourceKind.ImportedSst,
                "window-source.SST",
                1,
                payload),
            Payload = payload
        };
        var window = new RecordedSessionDerivationWindow(sourceSessionId, 1.1, 1.3);
        var domain = new RecordedSessionDomainSnapshot(
            session,
            setup,
            bike,
            null,
            null,
            RecordedSessionSourceSnapshot.From(source),
            window,
            new SessionStaleness.SourceWindowChanged(),
            DerivedChangeKind.None);
        var reprocessor = CreateReprocessor();

        var result = await reprocessor.ReprocessAsync(domain, source);
        var telemetryData = result.ProcessedTelemetry.TelemetryData;

        Assert.Equal(1_700_000_001, telemetryData.Metadata.Timestamp);
        Assert.Equal(0.2, telemetryData.Metadata.Duration, precision: 6);
        Assert.Equal(20, telemetryData.Front.Travel.Length);
        Assert.Equal(window, result.Fingerprint.DerivationWindow);
        Assert.Equal(source.SourceHash, result.Fingerprint.SourceHash);
    }

    [Fact]
    public async Task ReprocessAsync_LiveCaptureSource_RebuildsTelemetryAndGeneratedTrack()
    {
        var session = TestSnapshots.Session(id: Guid.NewGuid(), setupId: Guid.NewGuid());
        var bike = TestSnapshots.Bike(id: Guid.NewGuid()) with
        {
            HeadAngle = 63,
            ForkStroke = 180
        };
        var setup = TestSnapshots.Setup(id: session.SetupId!.Value, bikeId: bike.Id) with
        {
            FrontSensorConfigurationJson = SensorConfiguration.ToJson(new LinearForkSensorConfiguration
            {
                Length = 10,
                Resolution = 12
            })
        };
        var capture = new LiveTelemetryCapture(
            Metadata: new Metadata
            {
                SourceName = "live",
                Version = 4,
                SampleRate = 100,
                Timestamp = 1_700_000_000,
                Duration = 0.64
            },
            BikeData: new BikeData(180, null, measurement => measurement / 10.0, null),
            FrontMeasurements: Enumerable.Range(0, 64).Select(sample => (ushort)(1200 + sample)).ToArray(),
            RearMeasurements: [],
            ImuData: null,
            GpsData:
            [
                new GpsRecord(
                    Timestamp: new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc),
                    Latitude: 42.6977,
                    Longitude: 23.3219,
                    Altitude: 600,
                    Speed: 10,
                    Heading: 90,
                    FixMode: 3,
                    Satellites: 12,
                    Epe2d: 0.5f,
                    Epe3d: 0.8f)
            ],
            Markers: [])
        {
            TemperatureData =
            [
                new TemperatureSample(1_700_000_000, 0, 21.5f),
                new TemperatureSample(1_700_000_001, 0, 22.5f),
            ],
        };
        var source = RecordedSessionSourceFactory.CreateLiveCapture(session.Id, capture);
        var domain = new RecordedSessionDomainSnapshot(
            session,
            setup,
            bike,
            null,
            null,
            RecordedSessionSourceSnapshot.From(source),
            null,
            new SessionStaleness.MissingProcessedData(),
            DerivedChangeKind.None);
        var reprocessor = CreateReprocessor();

        var result = await reprocessor.ReprocessAsync(domain, source);

        var telemetryData = result.ProcessedTelemetry.TelemetryData;
        Assert.Equal("live", telemetryData.Metadata.SourceName);
        Assert.NotEmpty(telemetryData.Front.Travel);
        Assert.NotNull(result.GeneratedFullTrack);
        Assert.Single(result.GeneratedFullTrack.Points);
        Assert.Equal(22.0, Assert.Single(telemetryData.TemperatureAverages).TemperatureCelsius);
        Assert.Equal(source.SourceHash, result.Fingerprint.SourceHash);
    }

    [Fact]
    public async Task ReprocessAsync_ReusesCurrentDomainFingerprint_WhenProcessingOptionMatches()
    {
        var session = TestSnapshots.Session(id: Guid.NewGuid(), setupId: Guid.NewGuid());
        var bike = TestSnapshots.Bike(id: Guid.NewGuid());
        var setup = TestSnapshots.Setup(id: session.SetupId!.Value, bikeId: bike.Id) with
        {
            FrontSensorConfigurationJson = SensorConfiguration.ToJson(new LinearForkSensorConfiguration
            {
                Length = 10,
                Resolution = 12
            })
        };
        var sstBytes = TestSstFiles.CreateV3WithFrontOnly();
        var payload = RecordedSessionSourcePayloadCodec.CompressImportedSst(sstBytes);
        var source = new RecordedSessionSource
        {
            SessionId = session.Id,
            SourceKind = RecordedSessionSourceKind.ImportedSst,
            SourceName = "reuse-fingerprint.SST",
            SchemaVersion = 1,
            SourceHash = RecordedSessionSourceHash.Compute(
                RecordedSessionSourceKind.ImportedSst,
                "reuse-fingerprint.SST",
                1,
                payload),
            Payload = payload
        };
        var currentFingerprint = new ProcessingFingerprint(
            3,
            TelemetryProcessingVersion.Current,
            setup.Id,
            bike.Id,
            1,
            "domain-dependency-hash",
            source.SourceHash);
        var domain = new RecordedSessionDomainSnapshot(
            session,
            setup,
            bike,
            currentFingerprint,
            null,
            RecordedSessionSourceSnapshot.From(source),
            null,
            new SessionStaleness.MissingProcessedData(),
            DerivedChangeKind.None,
            currentFingerprint.DependencyHash);
        var fingerprintService = Substitute.For<IProcessingFingerprintService>();
        var reprocessor = new RecordedSessionReprocessor(
            fingerprintService,
            new TelemetryBikeProcessingContextFactory(
                new RearTravelCalibrationBuilder(
                    new KinematicSolutionCache())));

        var result = await reprocessor.ReprocessAsync(domain, source, TelemetryProcessingOptions.Default);

        Assert.Equal(currentFingerprint, result.Fingerprint);
        fingerprintService.DidNotReceive().CreateCurrent(
            Arg.Any<SessionSnapshot>(),
            Arg.Any<SetupSnapshot>(),
            Arg.Any<BikeSnapshot>(),
            Arg.Any<RecordedSessionSourceSnapshot>(),
            Arg.Any<TelemetryProcessingOptions?>());
        fingerprintService.DidNotReceive().CreateCurrent(
            Arg.Any<SessionSnapshot>(),
            Arg.Any<SetupSnapshot>(),
            Arg.Any<BikeSnapshot>(),
            Arg.Any<RecordedSessionSourceSnapshot>(),
            Arg.Any<string>(),
            Arg.Any<TelemetryProcessingOptions?>());
    }

    [Fact]
    public async Task ReprocessAsync_LiveCaptureSource_WithDerivationWindow_SlicesBeforeProcessing()
    {
        var session = TestSnapshots.Session(id: Guid.NewGuid(), setupId: Guid.NewGuid());
        var bike = TestSnapshots.Bike(id: Guid.NewGuid()) with
        {
            HeadAngle = 63,
            ForkStroke = 180
        };
        var setup = TestSnapshots.Setup(id: session.SetupId!.Value, bikeId: bike.Id) with
        {
            FrontSensorConfigurationJson = SensorConfiguration.ToJson(new LinearForkSensorConfiguration
            {
                Length = 10,
                Resolution = 12
            })
        };
        var sourceSessionId = Guid.NewGuid();
        var capture = new LiveTelemetryCapture(
            Metadata: new Metadata
            {
                SourceName = "live-window-source",
                Version = 4,
                SampleRate = 100,
                Timestamp = 1_700_000_000,
                Duration = 2.0
            },
            BikeData: new BikeData(180, null, measurement => measurement / 10.0, null),
            FrontMeasurements: Enumerable.Range(0, 200).Select(sample => (ushort)(1200 + sample)).ToArray(),
            RearMeasurements: [],
            ImuData: null,
            GpsData: null,
            Markers: []);
        var source = RecordedSessionSourceFactory.CreateLiveCapture(sourceSessionId, capture);
        var window = new RecordedSessionDerivationWindow(sourceSessionId, 1.1, 1.3);
        var domain = new RecordedSessionDomainSnapshot(
            session,
            setup,
            bike,
            null,
            null,
            RecordedSessionSourceSnapshot.From(source),
            window,
            new SessionStaleness.SourceWindowChanged(),
            DerivedChangeKind.None);
        var reprocessor = CreateReprocessor();

        var result = await reprocessor.ReprocessAsync(domain, source);
        var telemetryData = result.ProcessedTelemetry.TelemetryData;

        Assert.Equal(1_700_000_001, telemetryData.Metadata.Timestamp);
        Assert.Equal(0.2, telemetryData.Metadata.Duration, precision: 6);
        Assert.Equal(20, telemetryData.Front.Travel.Length);
        Assert.Equal(window, result.Fingerprint.DerivationWindow);
    }

    [Fact]
    public async Task ReprocessAsync_LiveCaptureSource_WithSegmentPayload_PreservesStreamGaps()
    {
        var sessionId = Guid.NewGuid();
        var capture = new LiveTelemetryCapture(
            Metadata: new Metadata
            {
                SourceName = "live-segmented",
                Version = 4,
                SampleRate = 100,
                Timestamp = 1_700_000_000,
                Duration = 0.7
            },
            BikeData: new BikeData(180, null, measurement => measurement / 10.0, null),
            FrontSegments:
            [
                new RawCountSegment
                {
                    FirstIndex = 0,
                    FirstMonotonicDeltaUs = 0,
                    Counts = Enumerable.Range(0, 32).Select(sample => (ushort)(1200 + sample)).ToArray(),
                },
                new RawCountSegment
                {
                    FirstIndex = 36,
                    FirstMonotonicDeltaUs = 360_000,
                    Counts = Enumerable.Range(0, 32).Select(sample => (ushort)(1300 + sample)).ToArray(),
                }
            ],
            RearSegments: [],
            ImuData: null,
            GpsData: null,
            Markers: [],
            StreamGaps:
            [
                new RawStreamGap
                {
                    StreamKind = SstV5ProtocolConstants.StreamTravel,
                    LocationId = (byte)SstV5ProtocolConstants.SensorForkTravel,
                    FirstMissingIndex = 32,
                    MissingCount = 4,
                    MissingTimeUs = 40_000,
                    Reason = "index_gap",
                }
            ],
            FinalStatus: null,
            MissingFinalStatus: true);
        var source = RecordedSessionSourceFactory.CreateLiveCapture(sessionId, capture);
        var sourceJson = Encoding.UTF8.GetString(source.Payload);
        var domain = CreateLiveCaptureDomain(source);
        var reprocessor = CreateReprocessor();

        var result = await reprocessor.ReprocessAsync(domain, source);

        Assert.Contains("\"front_segments\"", sourceJson);
        Assert.DoesNotContain("\"front_measurements\"", sourceJson);
        var telemetryData = result.ProcessedTelemetry.TelemetryData;
        Assert.Equal(5, telemetryData.Metadata.Version);
        Assert.True(telemetryData.Front.HasGaps);
        Assert.Single(telemetryData.StreamGaps);
        Assert.True(telemetryData.MissingFinalStatus);
    }

    [Fact]
    public async Task ReprocessAsync_LiveCaptureSource_WithOldFlatPayload_RebuildsDenseCapture()
    {
        var sessionId = Guid.NewGuid();
        var payload = new RecordedLiveCaptureSourcePayload
        {
            SchemaVersion = 1,
            Metadata = new Metadata
            {
                SourceName = "old-live",
                Version = 4,
                SampleRate = 100,
                Timestamp = 1_700_000_000,
                Duration = 0.64
            },
            FrontMeasurements = Enumerable.Range(0, 64).Select(sample => (ushort)(1200 + sample)).ToArray(),
            RearMeasurements = [],
            ImuData = null,
            GpsData = null,
            Markers = []
        };
        var source = CreateLiveCaptureSource(sessionId, "old-live", payload);
        var domain = CreateLiveCaptureDomain(source);
        var reprocessor = CreateReprocessor();

        var result = await reprocessor.ReprocessAsync(domain, source);

        var telemetryData = result.ProcessedTelemetry.TelemetryData;
        Assert.Equal(4, telemetryData.Metadata.Version);
        Assert.NotEmpty(telemetryData.Front.Travel);
        Assert.False(telemetryData.Front.HasGaps);
        Assert.Empty(telemetryData.StreamGaps);
    }

    [Fact]
    public void MetadataFromRaw_UsesRecordingDurationSecondsWhenPresent()
    {
        var raw = new RawTelemetryData
        {
            Version = 5,
            SampleRate = 100,
            Timestamp = 1_700_000_000,
            Front = [1, 2],
            Rear = [1, 2],
            RecordingDurationSeconds = 12.5,
        };

        var metadata = RecordedSessionReprocessor.MetadataFromRaw("v5-gap.sst", raw);

        Assert.Equal("v5-gap.sst", metadata.SourceName);
        Assert.Equal(5, metadata.Version);
        Assert.Equal(100, metadata.SampleRate);
        Assert.Equal(1_700_000_000, metadata.Timestamp);
        Assert.Equal(12.5, metadata.Duration);
    }

    [Fact]
    public async Task ReprocessAsync_RecordsTheProcessingOptionInTheFingerprint()
    {
        var session = TestSnapshots.Session(id: Guid.NewGuid(), setupId: Guid.NewGuid());
        var bike = TestSnapshots.Bike(id: Guid.NewGuid());
        var setup = TestSnapshots.Setup(id: session.SetupId!.Value, bikeId: bike.Id) with
        {
            FrontSensorConfigurationJson = SensorConfiguration.ToJson(new LinearForkSensorConfiguration
            {
                Length = 10,
                Resolution = 12
            })
        };
        var sstBytes = TestSstFiles.CreateV3WithFrontOnly();
        var payload = RecordedSessionSourcePayloadCodec.CompressImportedSst(sstBytes);
        var source = new RecordedSessionSource
        {
            SessionId = session.Id,
            SourceKind = RecordedSessionSourceKind.ImportedSst,
            SourceName = "source.SST",
            SchemaVersion = 1,
            SourceHash = RecordedSessionSourceHash.Compute(
                RecordedSessionSourceKind.ImportedSst,
                "source.SST",
                1,
                payload),
            Payload = payload
        };
        var domain = new RecordedSessionDomainSnapshot(
            session,
            setup,
            bike,
            null,
            null,
            RecordedSessionSourceSnapshot.From(source),
            null,
            new SessionStaleness.MissingProcessedData(),
            DerivedChangeKind.None);
        var reprocessor = CreateReprocessor();

        // The single derivation path (import / live-save / recompute all flow through
        // here) stamps the option it was produced with into the v3 fingerprint, so a
        // later velocity-filter change makes the stored BLOB read as stale.
        var result = await reprocessor.ReprocessAsync(
            domain, source, new TelemetryProcessingOptions(100), CancellationToken.None);

        Assert.Equal(3, result.Fingerprint.SchemaVersion);
        Assert.Equal(100, result.Fingerprint.VelocityFilterWindowMilliseconds);
    }

    private static RecordedSessionDomainSnapshot CreateLiveCaptureDomain(RecordedSessionSource source)
    {
        var session = TestSnapshots.Session(id: source.SessionId, setupId: Guid.NewGuid());
        var bike = TestSnapshots.Bike(id: Guid.NewGuid()) with
        {
            HeadAngle = 63,
            ForkStroke = 180
        };
        var setup = TestSnapshots.Setup(id: session.SetupId!.Value, bikeId: bike.Id) with
        {
            FrontSensorConfigurationJson = SensorConfiguration.ToJson(new LinearForkSensorConfiguration
            {
                Length = 10,
                Resolution = 12
            })
        };

        return new RecordedSessionDomainSnapshot(
            session,
            setup,
            bike,
            null,
            null,
            RecordedSessionSourceSnapshot.From(source),
            null,
            new SessionStaleness.MissingProcessedData(),
            DerivedChangeKind.None);
    }

    private static RecordedSessionReprocessor CreateReprocessor() =>
        new(
            new ProcessingFingerprintService(),
            new TelemetryBikeProcessingContextFactory(
                new RearTravelCalibrationBuilder(
                    new KinematicSolutionCache())));

    private static RecordedSessionSource CreateLiveCaptureSource(
        Guid sessionId,
        string sourceName,
        RecordedLiveCaptureSourcePayload payload)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(AppJson.Serialize(payload));
        return new RecordedSessionSource
        {
            SessionId = sessionId,
            SourceKind = RecordedSessionSourceKind.LiveCapture,
            SourceName = sourceName,
            SchemaVersion = 1,
            SourceHash = RecordedSessionSourceHash.Compute(
                RecordedSessionSourceKind.LiveCapture,
                sourceName,
                1,
                payloadBytes),
            Payload = payloadBytes
        };
    }
}
