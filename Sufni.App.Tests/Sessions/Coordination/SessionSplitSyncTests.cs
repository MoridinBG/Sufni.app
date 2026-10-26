using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.Setups.Stores;
using Sufni.App.Tests.TestSupport.Persistence;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Coordination;

public class SessionSplitSyncTests
{
    private const int SampleRate = 100;
    private const double DurationSeconds = 10;
    private const long Timestamp = 1_700_000_000;

    private readonly RecordedSessionReprocessor reprocessor = new(
        new ProcessingFingerprintService(),
        new TelemetryBikeProcessingContextFactory(new RearTravelCalibrationBuilder(new KinematicSolutionCache())));

    [Fact]
    public async Task SplitSession_SyncsToFreshDatabase_AndReprocessesSecondPartFromSharedSource()
    {
        using var sourceDatabase = new TempDatabase("split-sync-source.db");
        using var targetDatabase = new TempDatabase("split-sync-target.db");
        var origin = new TestPersistenceHarness(sourceDatabase.DatabasePath);
        var target = new TestPersistenceHarness(targetDatabase.DatabasePath);
        var (session, setup, bike, source) = await SeedProcessedSessionAsync(origin);

        var split = Assert.IsType<SessionEditCommit.Committed>(
            await origin.SessionTransactions.SplitAsync(SessionSnapshot.From(session), clickSeconds: 5));
        await target.ApplyRemoteSynchronizationDataAsync(await origin.GetSynchronizationDataAsync(0, long.MaxValue));
        await target.PutRecordedSessionSourceAsync(source);

        var part2 = SessionSnapshot.From((await target.GetSessionAsync(split.CreatedSessionId!.Value))!);
        Assert.Equal(new RecordedSessionDerivationWindow(session.Id, 5, null), part2.DerivationWindow);
        var result = await reprocessor.ReprocessAsync(
            CreateDomain(part2, setup, bike, source),
            source,
            TestContext.Current.CancellationToken);
        Assert.Equal(5, result.ProcessedTelemetry.TelemetryData.Metadata.Duration, precision: 3);
        Assert.Equal(500, result.ProcessedTelemetry.TelemetryData.Front.Travel.Length);
        Assert.NotNull(await target.SessionTelemetryWriter.UpdateProcessedDerivedDataAsync(
            (await target.GetSessionAsync(part2.Id))!,
            result.ProcessedTelemetry,
            result.GeneratedFullTrack,
            result.Fingerprint));
    }

    private async Task<(Session Session, Setup Setup, Bike Bike, RecordedSessionSource Source)> SeedProcessedSessionAsync(
        TestPersistenceHarness database)
    {
        var bike = new Bike(Guid.NewGuid(), "bike")
        {
            HeadAngle = 65,
            ForkStroke = 150,
            RearSuspension = new RearSuspensionSpec.Hardtail()
        };
        var setup = new Setup(Guid.NewGuid(), "setup")
        {
            BikeId = bike.Id,
            FrontSensorConfigurationJson = SensorConfiguration.ToJson(
                new LinearForkSensorConfiguration { Length = 140, Resolution = 12 })
        };
        var session = new Session(Guid.NewGuid(), "ride", "", setup.Id, Timestamp) { DurationSeconds = DurationSeconds };
        var source = RecordedSessionSourceFactory.CreateLiveCapture(session.Id, CreateCapture());
        await database.PutAsync(bike);
        await database.PutAsync(setup);

        var result = await reprocessor.ReprocessAsync(
            CreateDomain(SessionSnapshot.From(session), setup, bike, source),
            source,
            TestContext.Current.CancellationToken);
        var persisted = await database.SessionTelemetryWriter.PutProcessedSessionAsync(
            session,
            result.ProcessedTelemetry,
            result.GeneratedFullTrack,
            source);
        return (persisted, setup, bike, source);
    }

    private static RecordedSessionDomainSnapshot CreateDomain(
        SessionSnapshot session,
        Setup setup,
        Bike bike,
        RecordedSessionSource source) =>
        new(
            session,
            SetupSnapshot.From(setup, boardId: null),
            BikeSnapshot.From(bike),
            CurrentFingerprint: null,
            PersistedFingerprint: null,
            RecordedSessionSourceSnapshot.From(source),
            new SessionStaleness.MissingProcessedData(),
            DerivedChangeKind.None);

    private static LiveTelemetryCapture CreateCapture() =>
        new(
            new Metadata
            {
                SourceName = "split capture",
                Version = 4,
                SampleRate = SampleRate,
                Timestamp = Timestamp,
                Duration = DurationSeconds
            },
            new BikeData(null, null, null, null),
            Enumerable.Range(0, (int)(SampleRate * DurationSeconds))
                .Select(index => (ushort)Math.Round(1_800 + 450 * Math.Sin(index / 16.0)))
                .ToArray(),
            [],
            ImuData: null,
            GpsData: null,
            Markers: []);
}
