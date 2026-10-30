using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Sufni.App.Infrastructure;
using Sufni.Telemetry;

using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.Models;
using Sufni.App.Shared.Models;
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
            SourceHash = RecordedSessionSourceHash.Compute(RecordedSessionSourceKind.ImportedSst, payload),
            Payload = payload
        };
        var domain = new RecordedSessionDomainSnapshot(
            session,
            setup,
            bike,
            null,
            null,
            RecordedSessionSourceSnapshot.From(source),
            new SessionStaleness.MissingProcessedData(),
            DerivedChangeKind.None);
        var reprocessor = CreateReprocessor();

        var result = Assert.IsType<RecordedSessionReprocessResult.Processed>(await reprocessor.ReprocessAsync(TestDerivationInputs.Create(domain, source, TelemetryProcessingOptions.Default), cancellationToken: TestContext.Current.CancellationToken));

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
            SourceHash = RecordedSessionSourceHash.Compute(RecordedSessionSourceKind.ImportedSst, payload),
            Payload = payload
        };
        var window = new RecordedSessionDerivationWindow(sourceSessionId, 1.1, 1.3);
        session = session with
        {
            SourceSessionId = sourceSessionId,
            WindowStartSeconds = 1.1,
            WindowEndSeconds = 1.3
        };
        var domain = new RecordedSessionDomainSnapshot(
            session,
            setup,
            bike,
            null,
            null,
            RecordedSessionSourceSnapshot.From(source),
            new SessionStaleness.SourceWindowChanged(),
            DerivedChangeKind.None);
        var reprocessor = CreateReprocessor();

        var result = Assert.IsType<RecordedSessionReprocessResult.Processed>(await reprocessor.ReprocessAsync(TestDerivationInputs.Create(domain, source, TelemetryProcessingOptions.Default), cancellationToken: TestContext.Current.CancellationToken));
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
            FrontSegments: [new RawCountSegment { Counts = Enumerable.Range(0, 64).Select(sample => (ushort)(1200 + sample)).ToArray() }],
            RearSegments: [],
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
            Markers: [],
            StreamGaps: [],
            FinalStatus: null,
            MissingFinalStatus: false)
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
            new SessionStaleness.MissingProcessedData(),
            DerivedChangeKind.None);
        var reprocessor = CreateReprocessor();

        var result = Assert.IsType<RecordedSessionReprocessResult.Processed>(await reprocessor.ReprocessAsync(TestDerivationInputs.Create(domain, source, TelemetryProcessingOptions.Default), cancellationToken: TestContext.Current.CancellationToken));

        var telemetryData = result.ProcessedTelemetry.TelemetryData;
        Assert.Equal("live", telemetryData.Metadata.SourceName);
        Assert.NotEmpty(telemetryData.Front.Travel);
        Assert.NotNull(result.GeneratedFullTrack);
        Assert.Single(result.GeneratedFullTrack.Points);
        Assert.Equal(22.0, Assert.Single(telemetryData.TemperatureAverages).TemperatureCelsius);
        Assert.Equal(source.SourceHash, result.Fingerprint.SourceHash);
    }

    [Fact]
    public async Task ReprocessAsync_IgnoresProjectedFingerprintAndHashes_WhenInputsHaveChanged()
    {
        var source = CreateLiveCaptureSource(Guid.NewGuid(), "fingerprint", CreateTemperatureCompatibilityPayload([]));
        var domain = CreateLiveCaptureDomain(source);
        var reprocessor = CreateReprocessor();
        var original = Assert.IsType<RecordedSessionReprocessResult.Processed>(await reprocessor.ReprocessAsync(TestDerivationInputs.Create(domain, source, TelemetryProcessingOptions.Default), TestContext.Current.CancellationToken));
        var changed = domain with
        {
            Setup = domain.Setup! with
            {
                FrontSensorConfigurationJson = SensorConfiguration.ToJson(new LinearForkSensorConfiguration
                {
                    Length = 20,
                    Resolution = 12
                })
            },
            CurrentFingerprint = original.Fingerprint,
            DependencyHash = original.Fingerprint.DependencyHash,
            Source = domain.Source! with { SourceHash = "outdated-projection-hash" }
        };

        var result = Assert.IsType<RecordedSessionReprocessResult.Processed>(await reprocessor.ReprocessAsync(TestDerivationInputs.Create(changed, source, TelemetryProcessingOptions.Default), TestContext.Current.CancellationToken));

        Assert.NotEqual(original.Fingerprint.DependencyHash, result.Fingerprint.DependencyHash);
        Assert.Equal(source.SourceHash, result.Fingerprint.SourceHash);
        Assert.Equal(original.Fingerprint.VelocityFilterWindowMilliseconds, result.Fingerprint.VelocityFilterWindowMilliseconds);
        Assert.Equal(original.Fingerprint.DerivationWindow, result.Fingerprint.DerivationWindow);
        Assert.True(result.ProcessedTelemetry.TelemetryData.Front.Travel.Max() > original.ProcessedTelemetry.TelemetryData.Front.Travel.Max());
        Assert.Equal(AppJson.Serialize(result.Fingerprint), result.ProcessedTelemetry.FingerprintJson);
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
            FrontSegments: [new RawCountSegment { Counts = Enumerable.Range(0, 200).Select(sample => (ushort)(1200 + sample)).ToArray() }],
            RearSegments: [],
            ImuData: null,
            GpsData: null,
            Markers: [],
            StreamGaps: [],
            FinalStatus: null,
            MissingFinalStatus: false);
        var source = RecordedSessionSourceFactory.CreateLiveCapture(sourceSessionId, capture);
        var window = new RecordedSessionDerivationWindow(sourceSessionId, 1.1, 1.3);
        session = session with
        {
            SourceSessionId = sourceSessionId,
            WindowStartSeconds = 1.1,
            WindowEndSeconds = 1.3
        };
        var domain = new RecordedSessionDomainSnapshot(
            session,
            setup,
            bike,
            null,
            null,
            RecordedSessionSourceSnapshot.From(source),
            new SessionStaleness.SourceWindowChanged(),
            DerivedChangeKind.None);
        var reprocessor = CreateReprocessor();

        var result = Assert.IsType<RecordedSessionReprocessResult.Processed>(await reprocessor.ReprocessAsync(TestDerivationInputs.Create(domain, source, TelemetryProcessingOptions.Default), cancellationToken: TestContext.Current.CancellationToken));
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
            ImuData: new RawImuData
            {
                SampleRate = 100,
                Meta = [new ImuMetaEntry(0, 16_384, 131)],
                ActiveLocations = [0],
                Segments =
                [
                    new RawImuSegment
                    {
                        LocationId = 0,
                        FirstIndex = 0,
                        FirstMonotonicDeltaUs = 0,
                        Records = [new ImuRecord(1, 2, 3, 4, 5, 6)],
                    }
                ],
            },
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

        var result = Assert.IsType<RecordedSessionReprocessResult.Processed>(await reprocessor.ReprocessAsync(TestDerivationInputs.Create(domain, source, TelemetryProcessingOptions.Default), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("\"front_segments\"", sourceJson);
        Assert.DoesNotContain("\"front_measurements\"", sourceJson);
        using var sourceDocument = JsonDocument.Parse(source.Payload);
        var sourceImuRecords = sourceDocument.RootElement
            .GetProperty("imu_data")
            .GetProperty(nameof(RawImuData.Records));
        Assert.Equal(0, sourceImuRecords.GetArrayLength());
        var telemetryData = result.ProcessedTelemetry.TelemetryData;
        Assert.Equal(5, telemetryData.Metadata.Version);
        Assert.True(telemetryData.Front.HasGaps);
        var persistedTelemetry = TelemetryData.FromBinary(result.ProcessedTelemetry.Data);
        Assert.NotNull(persistedTelemetry.ImuData);
        Assert.Empty(persistedTelemetry.ImuData.Records);
        Assert.Single(persistedTelemetry.ImuData.Segments);
        Assert.Equal(1, persistedTelemetry.ImuData.Segments[0].Records[0].Ax);
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
            ImuData = new RawImuData
            {
                SampleRate = 100,
                Meta = [new ImuMetaEntry(0, 16_384, 131)],
                ActiveLocations = [0],
                Records = [new ImuRecord(10, 20, 30, 40, 50, 60)],
            },
            GpsData = null,
            Markers = []
        };
        var source = CreateLiveCaptureSource(sessionId, "old-live", payload);
        var domain = CreateLiveCaptureDomain(source);
        var reprocessor = CreateReprocessor();

        var result = Assert.IsType<RecordedSessionReprocessResult.Processed>(await reprocessor.ReprocessAsync(TestDerivationInputs.Create(domain, source, TelemetryProcessingOptions.Default), cancellationToken: TestContext.Current.CancellationToken));

        var telemetryData = result.ProcessedTelemetry.TelemetryData;
        Assert.Equal(4, telemetryData.Metadata.Version);
        Assert.NotEmpty(telemetryData.Front.Travel);
        Assert.False(telemetryData.Front.HasGaps);
        var persistedTelemetry = TelemetryData.FromBinary(result.ProcessedTelemetry.Data);
        Assert.NotNull(persistedTelemetry.ImuData);
        Assert.Empty(persistedTelemetry.ImuData.Records);
        var segment = Assert.Single(persistedTelemetry.ImuData.Segments);
        Assert.Equal(10, segment.Records[0].Ax);
    }

    [Fact]
    public async Task ReprocessAsync_LiveCaptureSource_WithNullTemperatureData_TreatsTemperatureAsAbsent()
    {
        var sessionId = Guid.NewGuid();
        var payload = CreateTemperatureCompatibilityPayload(temperatureData: null);
        var source = CreateLiveCaptureSource(sessionId, "null-temperature", payload);
        using var sourceDocument = JsonDocument.Parse(source.Payload);
        var domain = CreateLiveCaptureDomain(source);
        var reprocessor = CreateReprocessor();

        var result = Assert.IsType<RecordedSessionReprocessResult.Processed>(await reprocessor.ReprocessAsync(TestDerivationInputs.Create(domain, source, TelemetryProcessingOptions.Default), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(JsonValueKind.Null, sourceDocument.RootElement.GetProperty("temperature_data").ValueKind);
        Assert.NotEmpty(result.ProcessedTelemetry.TelemetryData.Front.Travel);
        Assert.Empty(result.ProcessedTelemetry.TelemetryData.TemperatureAverages);
        Assert.Empty(TelemetryData.FromBinary(result.ProcessedTelemetry.Data).TemperatureAverages);
    }

    [Fact]
    public async Task ReprocessAsync_LiveCaptureSource_WithoutTemperatureData_TreatsTemperatureAsAbsent()
    {
        var sessionId = Guid.NewGuid();
        var payload = CreateTemperatureCompatibilityPayload([]);
        var payloadJson = JsonNode.Parse(AppJson.Serialize(payload))!.AsObject();
        Assert.True(payloadJson.Remove("temperature_data"));
        var source = CreateLiveCaptureSource(
            sessionId,
            "missing-temperature",
            Encoding.UTF8.GetBytes(payloadJson.ToJsonString()));
        var domain = CreateLiveCaptureDomain(source);
        var reprocessor = CreateReprocessor();

        var result = Assert.IsType<RecordedSessionReprocessResult.Processed>(await reprocessor.ReprocessAsync(TestDerivationInputs.Create(domain, source, TelemetryProcessingOptions.Default), cancellationToken: TestContext.Current.CancellationToken));

        Assert.NotEmpty(result.ProcessedTelemetry.TelemetryData.Front.Travel);
        Assert.Empty(result.ProcessedTelemetry.TelemetryData.TemperatureAverages);
        Assert.Empty(TelemetryData.FromBinary(result.ProcessedTelemetry.Data).TemperatureAverages);
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
            SourceHash = RecordedSessionSourceHash.Compute(RecordedSessionSourceKind.ImportedSst, payload),
            Payload = payload
        };
        var domain = new RecordedSessionDomainSnapshot(
            session,
            setup,
            bike,
            null,
            null,
            RecordedSessionSourceSnapshot.From(source),
            new SessionStaleness.MissingProcessedData(),
            DerivedChangeKind.None);
        var reprocessor = CreateReprocessor();

        // The single derivation path (import / live-save / recompute all flow through
        // here) stamps the option it was produced with into the v3 fingerprint, so a
        // later velocity-filter change makes the stored BLOB read as stale.
        var result = Assert.IsType<RecordedSessionReprocessResult.Processed>(await reprocessor.ReprocessAsync(TestDerivationInputs.Create(domain, source, new TelemetryProcessingOptions(100)), CancellationToken.None));

        Assert.Equal(4, result.Fingerprint.SchemaVersion);
        Assert.Equal(100, result.Fingerprint.VelocityFilterWindowMilliseconds);
    }

    [Fact]
    public async Task ReprocessAsync_PinsCalibrationVersionOneToFrontAndRearTravel()
    {
        var payload = new RecordedLiveCaptureSourcePayload
        {
            SchemaVersion = 1,
            Metadata = new Metadata { SampleRate = 100, Duration = 0.64, Timestamp = 1_700_000_000 },
            FrontMeasurements = Enumerable.Repeat((ushort)1024, 64).ToArray(),
            RearMeasurements = Enumerable.Repeat((ushort)2048, 64).ToArray()
        };
        var source = CreateLiveCaptureSource(Guid.NewGuid(), "calibration-v1", payload);
        var domain = CreateLiveCaptureDomain(source);
        var input = RecordedSessionDerivationInput.Create(
            domain.Session,
            domain.Setup! with
            {
                FrontSensorConfigurationJson = SensorConfiguration.ToJson(new LinearForkSensorConfiguration
                {
                    Length = 160, Resolution = 12
                }),
                RearSensorConfigurationJson = SensorConfiguration.ToJson(new LinearShockSensorConfiguration
                {
                    Type = SensorType.LinearShockStroke, Length = 50, Resolution = 12
                })
            },
            domain.Bike! with
            {
                HeadAngle = 90, ForkStroke = 160, ShockStroke = 50,
                RearSuspension = new RearSuspensionSpec.LeverageRatio(TestSnapshots.LeverageRatioCurve((0, 0), (50, 150)))
            },
            source);

        var result = Assert.IsType<RecordedSessionReprocessResult.Processed>(await CreateReprocessor().ReprocessAsync(
            input, TestContext.Current.CancellationToken));

        Assert.Equal(1, result.Fingerprint.CalibrationVersion);
        var telemetry = result.ProcessedTelemetry.TelemetryData;
        Assert.Equal(64, telemetry.Front.Travel.Length);
        Assert.Equal(64, telemetry.Rear.Travel.Length);
        Assert.All(telemetry.Front.Travel, travel => Assert.Equal(40.00976800976801, travel, precision: 8));
        Assert.All(telemetry.Rear.Travel, travel => Assert.Equal(75.01831501831502, travel, precision: 8));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReprocessAsync_DistinguishesUnconfiguredRearFromInvalidCalibration(bool configured)
    {
        var source = CreateLiveCaptureSource(Guid.NewGuid(), "calibration", CreateTemperatureCompatibilityPayload([]));
        var domain = CreateLiveCaptureDomain(source);
        domain = domain with
        {
            Bike = domain.Bike! with { RearSuspension = new RearSuspensionSpec.LinkageDraft() },
            Setup = domain.Setup! with
            {
                RearSensorConfigurationJson = configured
                    ? SensorConfiguration.ToJson(new LinearShockSensorConfiguration { Length = 60, Resolution = 12 })
                    : null
            }
        };

        var result = await CreateReprocessor().ReprocessAsync(TestDerivationInputs.Create(domain, source, TelemetryProcessingOptions.Default), TestContext.Current.CancellationToken);

        if (configured)
            Assert.Equal(SessionProcessingFailureReason.InvalidCalibration, Assert.IsType<RecordedSessionReprocessResult.Failed>(result).Reason);
        else
        {
            var processed = Assert.IsType<RecordedSessionReprocessResult.Processed>(result);
            Assert.True(processed.ProcessedTelemetry.TelemetryData.Front.Present);
            Assert.False(processed.ProcessedTelemetry.TelemetryData.Rear.Present);
        }
    }

    [Fact]
    public async Task ReprocessAsync_MapsUnusableTelemetryToTypedFailure()
    {
        var payload = CreateTemperatureCompatibilityPayload([], new ushort[64]);
        var source = CreateLiveCaptureSource(Guid.NewGuid(), "no-travel", payload);

        var result = await CreateReprocessor().ReprocessAsync(TestDerivationInputs.Create(CreateLiveCaptureDomain(source), source, TelemetryProcessingOptions.Default), TestContext.Current.CancellationToken);

        Assert.Equal(SessionProcessingFailureReason.NoTravelData, Assert.IsType<RecordedSessionReprocessResult.Failed>(result).Reason);
    }

    [Fact]
    public async Task ReprocessAsync_PropagatesCancellation()
    {
        var source = CreateLiveCaptureSource(Guid.NewGuid(), "canceled", CreateTemperatureCompatibilityPayload([]));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateReprocessor().ReprocessAsync(TestDerivationInputs.Create(CreateLiveCaptureDomain(source), source, TelemetryProcessingOptions.Default), cancellation.Token));
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
            new SessionStaleness.MissingProcessedData(),
            DerivedChangeKind.None);
    }

    private static RecordedSessionReprocessor CreateReprocessor() =>
        new(
            new ProcessingFingerprintService(),
            new TelemetryBikeProcessingContextFactory(
                new RearTravelCalibrationBuilder(
                    new KinematicSolutionCache())));

    private static RecordedLiveCaptureSourcePayload CreateTemperatureCompatibilityPayload(
        TemperatureSample[]? temperatureData, ushort[]? frontMeasurements = null) =>
        new()
        {
            SchemaVersion = 1,
            Metadata = new Metadata
            {
                SourceName = "temperature-compatibility",
                Version = 4,
                SampleRate = 100,
                Timestamp = 1_700_000_000,
                Duration = 0.64,
            },
            FrontMeasurements = frontMeasurements ?? Enumerable.Range(0, 64).Select(sample => (ushort)(1200 + sample)).ToArray(),
            RearMeasurements = [],
            TemperatureData = temperatureData,
            Markers = [],
        };

    private static RecordedSessionSource CreateLiveCaptureSource(
        Guid sessionId,
        string sourceName,
        RecordedLiveCaptureSourcePayload payload) =>
        CreateLiveCaptureSource(
            sessionId,
            sourceName,
            Encoding.UTF8.GetBytes(AppJson.Serialize(payload)));

    private static RecordedSessionSource CreateLiveCaptureSource(
        Guid sessionId,
        string sourceName,
        byte[] payloadBytes) =>
        new()
        {
            SessionId = sessionId,
            SourceKind = RecordedSessionSourceKind.LiveCapture,
            SourceName = sourceName,
            SchemaVersion = 1,
            SourceHash = RecordedSessionSourceHash.Compute(RecordedSessionSourceKind.LiveCapture, payloadBytes),
            Payload = payloadBytes,
        };
}
