using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Store;
using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.Sessions.Store;

public class SessionSnapshotMappingTests
{
    [Fact]
    public void ToMetadataEntity_ExcludesReadOnlyMaterializations()
    {
        var snapshot = new SessionSnapshot(
            Id: Guid.NewGuid(),
            Name: "mapped session",
            Description: "description",
            SetupId: Guid.NewGuid(),
            Timestamp: 1_700_000_000,

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
        Assert.Equal(snapshot with { HasProcessedData = false, DistanceMeters = null, AscentMeters = null, DescentMeters = null, GpsMetricsKey = null }, roundTrip);
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
        var originalTrack = await database.GetAsync<Track>(fullTrack.Id);

        var metadataShell = SessionSnapshot.From(processed) with
        {
            Name = "renamed",
            Description = "metadata only"
        };
        await database.PutSessionAsync(metadataShell.ToMetadataEntity());

        var loaded = await database.GetSessionAsync(sessionId);
        var rawAfterMetadataSave = await database.GetSessionRawPsstAsync(sessionId);
        var trackAfterMetadataSave = await database.GetAsync<Track>(fullTrack.Id);

        Assert.NotNull(loaded);
        Assert.Equal("renamed", loaded!.Name);
        Assert.Equal("metadata only", loaded.Description);

        Assert.Equal(processed.ProcessingFingerprintJson, loaded.ProcessingFingerprintJson);
        Assert.True(loaded.HasProcessedData);
        Assert.Equal(processed.DurationSeconds, loaded.DurationSeconds);
        Assert.Equal(processed.DistanceMeters, loaded.DistanceMeters);
        Assert.Equal(processed.AscentMeters, loaded.AscentMeters);
        Assert.Equal(processed.DescentMeters, loaded.DescentMeters);
        Assert.Equal(originalRaw, rawAfterMetadataSave);
        Assert.Equal(originalTrack!.PointsJson, trackAfterMetadataSave!.PointsJson);
    }

}
