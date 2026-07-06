using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Store;
using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.Sessions.Store;

public class SessionSnapshotMappingTests
{
    [Fact]
    public void ToMetadataEntity_RoundTripsEveryMappedSnapshotField_WithoutPayloads()
    {
        var snapshot = new SessionSnapshot(
            Id: Guid.NewGuid(),
            Name: "mapped session",
            Description: "description",
            SetupId: Guid.NewGuid(),
            Timestamp: 1_700_000_000,
            FullTrackId: Guid.NewGuid(),
            HasProcessedData: true,
            ProcessingFingerprintJson: """{"schemaVersion":7}""",
            FrontSpringRate: "80 psi",
            FrontHighSpeedCompression: 1,
            FrontLowSpeedCompression: 2,
            FrontLowSpeedRebound: 3,
            FrontHighSpeedRebound: 4,
            RearSpringRate: "450 lb",
            RearHighSpeedCompression: 5,
            RearLowSpeedCompression: 6,
            RearLowSpeedRebound: 7,
            RearHighSpeedRebound: 8,
            Updated: 42,
            DurationSeconds: 12.5,
            DistanceMeters: 123.4,
            AscentMeters: 56.7,
            DescentMeters: 45.6,
            GpsOffsetSeconds: 0.75);

        var entity = snapshot.ToMetadataEntity();
        var roundTrip = SessionSnapshot.From(entity);

        Assert.Null(entity.ProcessedData);
        Assert.Null(entity.Track);
        Assert.Equal(snapshot, roundTrip);
    }

    [Fact]
    public async Task RepositoryMetadataSave_ThroughMetadataMapper_PreservesDerivedDataAndBlobColumns()
    {
        using var tempDatabase = new TempDatabase("session-metadata-shell-save.db");
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var sessionId = Guid.NewGuid();
        var processedData = PersistenceTestData.CreateTelemetryBlob(65);
        var fullTrack = PersistenceTestData.CreateFullTrack();
        var processed = await database.PutProcessedSessionAsync(
            new Session(sessionId, "processed", "desc", null, 100)
            {
                ProcessedData = processedData,
                ProcessingFingerprintJson = """{"schemaVersion":7}"""
            },
            fullTrack,
            source: null);
        var originalRaw = await database.GetSessionRawPsstAsync(sessionId);
        var originalTrack = await database.GetSessionTrackAsync(sessionId);

        var metadataShell = SessionSnapshot.From(processed) with
        {
            Name = "renamed",
            Description = "metadata only"
        };
        await database.PutSessionAsync(metadataShell.ToMetadataEntity());

        var loaded = await database.GetSessionAsync(sessionId);
        var rawAfterMetadataSave = await database.GetSessionRawPsstAsync(sessionId);
        var trackAfterMetadataSave = await database.GetSessionTrackAsync(sessionId);

        Assert.NotNull(loaded);
        Assert.Equal("renamed", loaded!.Name);
        Assert.Equal("metadata only", loaded.Description);
        Assert.Equal(processed.FullTrack, loaded.FullTrack);
        Assert.Equal(processed.ProcessingFingerprintJson, loaded.ProcessingFingerprintJson);
        Assert.True(loaded.HasProcessedData);
        Assert.Equal(processed.DurationSeconds, loaded.DurationSeconds);
        Assert.Equal(processed.DistanceMeters, loaded.DistanceMeters);
        Assert.Equal(processed.AscentMeters, loaded.AscentMeters);
        Assert.Equal(processed.DescentMeters, loaded.DescentMeters);
        Assert.Equal(originalRaw, rawAfterMetadataSave);
        AssertTrackPointsEqual(originalTrack, trackAfterMetadataSave);
    }

    private static void AssertTrackPointsEqual(
        IReadOnlyList<TrackPoint>? expected,
        IReadOnlyList<TrackPoint>? actual)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        Assert.Equal(expected.Count, actual!.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Time, actual[i].Time);
            Assert.Equal(expected[i].X, actual[i].X);
            Assert.Equal(expected[i].Y, actual[i].Y);
            Assert.Equal(expected[i].Elevation, actual[i].Elevation);
            Assert.Equal(expected[i].Speed, actual[i].Speed);
            Assert.Equal(expected[i].FixMode, actual[i].FixMode);
            Assert.Equal(expected[i].Satellites, actual[i].Satellites);
            Assert.Equal(expected[i].Epe2d, actual[i].Epe2d);
            Assert.Equal(expected[i].Epe3d, actual[i].Epe3d);
        }
    }
}
