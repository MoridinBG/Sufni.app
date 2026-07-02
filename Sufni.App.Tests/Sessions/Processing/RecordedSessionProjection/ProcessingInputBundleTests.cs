using SQLite;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.Setups.Stores;
using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.Sessions.Processing.RecordedSessionProjection;

public class ProcessingInputBundleTests
{
    [Fact]
    public async Task GetProcessingInputBundleAsync_ReturnsProcessingInputs()
    {
        using var tempDatabase = new TempDatabase("processing-input-bundle.db");
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var seeded = await SeedProcessingInputsAsync(database);

        var bundle = await database.GetProcessingInputBundleAsync(seeded.Session.Id);

        Assert.NotNull(bundle);
        Assert.Equal(seeded.Session.Id, bundle!.Session.Id);
        Assert.Equal(seeded.Setup.Id, bundle.Session.SetupId);
        Assert.Equal(seeded.Setup.Id, bundle.Setup.Id);
        Assert.Equal(seeded.Bike.Id, bundle.Setup.BikeId);
        Assert.Equal(seeded.Bike.Id, bundle.Bike.Id);
        Assert.Equal(seeded.Bike.HeadAngle, bundle.Bike.HeadAngle);
        Assert.Equal(seeded.Bike.ForkStroke, bundle.Bike.ForkStroke);
        Assert.Equal(seeded.Bike.ShockStroke, bundle.Bike.ShockStroke);
        Assert.Equal(seeded.Source.SourceHash, bundle.Source.SourceHash);
    }

    [Theory]
    [InlineData(MissingInput.Session)]
    [InlineData(MissingInput.DeletedSession)]
    [InlineData(MissingInput.Setup)]
    [InlineData(MissingInput.DeletedSetup)]
    [InlineData(MissingInput.Bike)]
    [InlineData(MissingInput.DeletedBike)]
    [InlineData(MissingInput.Source)]
    public async Task GetProcessingInputBundleAsync_ReturnsNull_WhenRequiredInputIsMissing(MissingInput missingInput)
    {
        using var tempDatabase = new TempDatabase($"processing-input-missing-{missingInput}.db");
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var seeded = await SeedProcessingInputsAsync(database);
        var sessionId = seeded.Session.Id;

        using (var connection = new SQLiteConnection(tempDatabase.DatabasePath))
        {
            ApplyMissingInput(connection, seeded, missingInput);
        }

        var bundle = await database.GetProcessingInputBundleAsync(
            missingInput == MissingInput.Session ? Guid.NewGuid() : sessionId);

        Assert.Null(bundle);
    }

    [Fact]
    public async Task BundleHashMatchesSnapshotHash_ForSameProcessingInputs()
    {
        using var tempDatabase = new TempDatabase("processing-input-hash.db");
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var seeded = await SeedProcessingInputsAsync(database);
        var fingerprintService = new ProcessingFingerprintService();

        var bundle = await database.GetProcessingInputBundleAsync(seeded.Session.Id);

        Assert.NotNull(bundle);
        var bundleHash = ProcessingDependencyHash.Compute(bundle!.Setup, bundle.Bike);
        var snapshotHash = ProcessingDependencyHash.Compute(
            SetupSnapshot.From(seeded.Setup, boardId: null),
            BikeSnapshot.From(seeded.Bike));
        var bundleFingerprint = fingerprintService.CreateCurrentDatabaseInputs(bundle);
        var snapshotFingerprint = fingerprintService.CreateCurrentDatabaseInputs(
            SessionSnapshot.From(seeded.Session),
            SetupSnapshot.From(seeded.Setup, boardId: null),
            BikeSnapshot.From(seeded.Bike),
            RecordedSessionSourceSnapshot.From(seeded.Source));

        Assert.Equal(snapshotHash, bundleHash);
        Assert.Equal(snapshotFingerprint, bundleFingerprint);
    }

    [Fact]
    public async Task GetProcessingInputBundleAsync_DoesNotRequireSourcePayloadOrBikeImage()
    {
        using var tempDatabase = new TempDatabase("processing-input-no-large-columns.db");
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var seeded = await SeedProcessingInputsAsync(database);

        using (var connection = new SQLiteConnection(tempDatabase.DatabasePath))
        {
            connection.Execute("UPDATE bike SET image = zeroblob(1048576) WHERE id = ?", seeded.Bike.Id);
            connection.Execute("UPDATE session_recording_source SET payload = zeroblob(1048576) WHERE session_id = ?", seeded.Session.Id);
        }

        var bundle = await database.GetProcessingInputBundleAsync(seeded.Session.Id);

        Assert.NotNull(bundle);
        Assert.Equal(seeded.Bike.Id, bundle!.Bike.Id);
        Assert.Equal(seeded.Source.SourceHash, bundle.Source.SourceHash);
    }

    private static async Task<SeededProcessingInputs> SeedProcessingInputsAsync(TestPersistenceHarness database)
    {
        var bike = new Bike(Guid.NewGuid(), "processing bike")
        {
            HeadAngle = 64,
            ForkStroke = 160,
            ShockStroke = 55,
            RearSuspension = new RearSuspensionSpec.Hardtail(),
            ImageBytes = [1, 2, 3]
        };
        var setup = new Setup(Guid.NewGuid(), "processing setup")
        {
            BikeId = bike.Id,
            FrontSensorConfigurationJson = SensorConfiguration.ToJson(new LinearForkSensorConfiguration
            {
                Length = 10,
                Resolution = 12
            })
        };
        var session = new Session(Guid.NewGuid(), "processing session", "desc", setup.Id, 100);
        var source = PersistenceTestData.CreateRecordedSessionSource(session.Id);

        await database.PutAsync(bike);
        await database.PutAsync(setup);
        await database.PutSessionAsync(session);
        await database.PutRecordedSessionSourceAsync(source);

        return new SeededProcessingInputs(session, setup, bike, source);
    }

    private static void ApplyMissingInput(
        SQLiteConnection connection,
        SeededProcessingInputs seeded,
        MissingInput missingInput)
    {
        switch (missingInput)
        {
            case MissingInput.Session:
                break;
            case MissingInput.DeletedSession:
                connection.Execute("UPDATE session SET deleted = ? WHERE id = ?", 123, seeded.Session.Id);
                break;
            case MissingInput.Setup:
                connection.Execute("UPDATE session SET setup_id = ? WHERE id = ?", Guid.NewGuid(), seeded.Session.Id);
                break;
            case MissingInput.DeletedSetup:
                connection.Execute("UPDATE setup SET deleted = ? WHERE id = ?", 123, seeded.Setup.Id);
                break;
            case MissingInput.Bike:
                connection.Execute("UPDATE setup SET bike_id = ? WHERE id = ?", Guid.NewGuid(), seeded.Setup.Id);
                break;
            case MissingInput.DeletedBike:
                connection.Execute("UPDATE bike SET deleted = ? WHERE id = ?", 123, seeded.Bike.Id);
                break;
            case MissingInput.Source:
                connection.Execute("DELETE FROM session_recording_source WHERE session_id = ?", seeded.Session.Id);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(missingInput), missingInput, null);
        }
    }

    public enum MissingInput
    {
        Session,
        DeletedSession,
        Setup,
        DeletedSetup,
        Bike,
        DeletedBike,
        Source
    }

    private sealed record SeededProcessingInputs(
        Session Session,
        Setup Setup,
        Bike Bike,
        RecordedSessionSource Source);
}
