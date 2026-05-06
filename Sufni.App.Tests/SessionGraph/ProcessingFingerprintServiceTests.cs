using Sufni.App.Models;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.SessionGraph;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;

namespace Sufni.App.Tests.SessionGraph;

public class ProcessingFingerprintServiceTests
{
    private readonly ProcessingFingerprintService service = new();

    [Fact]
    public void Evaluate_ReturnsCurrent_WhenPersistedFingerprintMatches()
    {
        var context = CreateContext();
        var current = service.CreateCurrent(context.Session, context.Setup, context.Bike, context.Source);
        var session = context.Session with
        {
            HasProcessedData = true,
            ProcessingFingerprintJson = AppJson.Serialize(current)
        };

        var staleness = service.Evaluate(session, context.Setup, context.Bike, context.Source);

        Assert.IsType<SessionStaleness.Current>(staleness);
        Assert.False(staleness.IsStale);
        Assert.False(staleness.CanRecompute);
    }

    [Fact]
    public void EvaluateState_ReturnsCurrentPersistedAndStaleness()
    {
        var context = CreateContext();
        var current = service.CreateCurrent(context.Session, context.Setup, context.Bike, context.Source);
        var session = context.Session with
        {
            HasProcessedData = true,
            ProcessingFingerprintJson = AppJson.Serialize(current)
        };

        var evaluation = service.EvaluateState(session, context.Setup, context.Bike, context.Source);

        Assert.Equal(current, evaluation.Current);
        Assert.Equal(current, evaluation.Persisted);
        Assert.IsType<SessionStaleness.Current>(evaluation.Staleness);
    }

    [Fact]
    public void Evaluate_ReturnsMissingDependencies_WhenSetupOrBikeIsMissing()
    {
        var context = CreateContext();

        var missingSetup = service.Evaluate(context.Session, null, context.Bike, context.Source);
        var missingBike = service.Evaluate(context.Session, context.Setup, null, context.Source);

        var setupStaleness = Assert.IsType<SessionStaleness.MissingDependencies>(missingSetup);
        Assert.True(setupStaleness.SetupMissing);
        Assert.False(setupStaleness.BikeMissing);
        var bikeStaleness = Assert.IsType<SessionStaleness.MissingDependencies>(missingBike);
        Assert.False(bikeStaleness.SetupMissing);
        Assert.True(bikeStaleness.BikeMissing);
    }

    [Fact]
    public void Evaluate_ReturnsMissingRawSource_WhenSourceIsMissing()
    {
        var context = CreateContext();
        var current = service.CreateCurrent(context.Session, context.Setup, context.Bike, context.Source);
        var session = context.Session with { ProcessingFingerprintJson = AppJson.Serialize(current) };

        var staleness = service.Evaluate(session, context.Setup, context.Bike, null);

        Assert.IsType<SessionStaleness.MissingRawSource>(staleness);
        Assert.False(staleness.IsStale);
        Assert.False(staleness.CanRecompute);
    }

    [Fact]
    public void Evaluate_ReturnsMissingRawSource_WhenSourceAndDependenciesAreMissing()
    {
        var context = CreateContext();

        var staleness = service.Evaluate(context.Session, null, null, null);

        Assert.IsType<SessionStaleness.MissingRawSource>(staleness);
        Assert.True(staleness.IsStale);
        Assert.False(staleness.CanRecompute);
    }

    [Fact]
    public void Evaluate_ReturnsStaleMissingRawSource_WhenProcessedDataIsMissing()
    {
        var context = CreateContext();
        var session = context.Session with { HasProcessedData = false };

        var staleness = service.Evaluate(session, context.Setup, context.Bike, null);

        Assert.IsType<SessionStaleness.MissingRawSource>(staleness);
        Assert.True(staleness.IsStale);
        Assert.False(staleness.CanRecompute);
    }

    [Fact]
    public void Evaluate_ReturnsStaleMissingRawSource_WhenDependencyHashChanged()
    {
        var context = CreateContext();
        var persisted = service.CreateCurrent(context.Session, context.Setup, context.Bike, context.Source);
        var session = context.Session with { ProcessingFingerprintJson = AppJson.Serialize(persisted) };
        var changedBike = context.Bike with { HeadAngle = context.Bike.HeadAngle + 1 };

        var staleness = service.Evaluate(session, context.Setup, changedBike, null);

        Assert.IsType<SessionStaleness.MissingRawSource>(staleness);
        Assert.True(staleness.IsStale);
        Assert.False(staleness.CanRecompute);
    }

    [Fact]
    public void Evaluate_ReturnsMissingProcessedData_WhenSourceAndDependenciesArePresent()
    {
        var context = CreateContext();
        var session = context.Session with { HasProcessedData = false };

        var staleness = service.Evaluate(session, context.Setup, context.Bike, context.Source);

        Assert.IsType<SessionStaleness.MissingProcessedData>(staleness);
        Assert.True(staleness.CanRecompute);
    }

    [Fact]
    public void Evaluate_ReturnsUnknownLegacyFingerprint_WhenProcessedDataHasNoFingerprint()
    {
        var context = CreateContext();

        var staleness = service.Evaluate(context.Session, context.Setup, context.Bike, context.Source);

        Assert.IsType<SessionStaleness.UnknownLegacyFingerprint>(staleness);
        Assert.True(staleness.CanRecompute);
    }

    [Fact]
    public void Evaluate_ReturnsProcessingVersionChanged_WhenPersistedVersionDiffers()
    {
        var context = CreateContext();
        var oldFingerprint = service.CreateCurrent(context.Session, context.Setup, context.Bike, context.Source) with
        {
            ProcessingVersion = TelemetryProcessingVersion.Current - 1
        };
        var session = context.Session with { ProcessingFingerprintJson = AppJson.Serialize(oldFingerprint) };

        var staleness = service.Evaluate(session, context.Setup, context.Bike, context.Source);

        var versionChanged = Assert.IsType<SessionStaleness.ProcessingVersionChanged>(staleness);
        Assert.Equal(TelemetryProcessingVersion.Current - 1, versionChanged.Persisted);
        Assert.Equal(TelemetryProcessingVersion.Current, versionChanged.CurrentVersion);
        Assert.True(versionChanged.CanRecompute);
    }

    [Fact]
    public void Evaluate_ReturnsDependencyHashChanged_WhenProcessingDependencyChanges()
    {
        var context = CreateContext();
        var persisted = service.CreateCurrent(context.Session, context.Setup, context.Bike, context.Source);
        var session = context.Session with { ProcessingFingerprintJson = AppJson.Serialize(persisted) };
        var changedBike = context.Bike with { HeadAngle = context.Bike.HeadAngle + 1 };

        var staleness = service.Evaluate(session, context.Setup, changedBike, context.Source);

        Assert.IsType<SessionStaleness.DependencyHashChanged>(staleness);
        Assert.True(staleness.CanRecompute);
    }

    [Fact]
    public void ParsePersisted_ReturnsNull_WhenJsonIsMalformed()
    {
        var session = TestSnapshots.Session(processingFingerprintJson: "{bad json", hasProcessedData: true);

        var parsed = service.ParsePersisted(session);

        Assert.Null(parsed);
    }

    [Fact]
    public void CreateCurrent_RejectsMismatchedSetup()
    {
        var context = CreateContext();
        var mismatchedSetup = context.Setup with { Id = Guid.NewGuid() };

        Assert.Throws<InvalidOperationException>(() =>
            service.CreateCurrent(context.Session, mismatchedSetup, context.Bike, context.Source));
    }

    [Fact]
    public void ProcessingDependencyHash_IgnoresNonProcessingMetadata()
    {
        var context = CreateContext();
        var changedSetup = context.Setup with
        {
            Name = "renamed setup",
            BoardId = Guid.NewGuid(),
            Updated = 99
        };
        var changedBike = context.Bike with
        {
            Name = "renamed bike",
            ImageBytes = [9, 8, 7],
            Updated = 99
        };

        var original = ProcessingDependencyHash.Compute(context.Setup, context.Bike);
        var changed = ProcessingDependencyHash.Compute(changedSetup, changedBike);

        Assert.Equal(original, changed);
    }

    [Fact]
    public void ProcessingDependencyHash_Changes_WhenSensorCalibrationChanges()
    {
        var context = CreateContext();
        var changedSetup = context.Setup with
        {
            FrontSensorConfigurationJson = SensorConfiguration.ToJson(new LinearForkSensorConfiguration
            {
                Length = 11,
                Resolution = 12
            })
        };

        var original = ProcessingDependencyHash.Compute(context.Setup, context.Bike);
        var changed = ProcessingDependencyHash.Compute(changedSetup, context.Bike);

        Assert.NotEqual(original, changed);
    }

    private static TestContext CreateContext()
    {
        var bike = TestSnapshots.Bike(id: Guid.NewGuid());
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), bikeId: bike.Id) with
        {
            FrontSensorConfigurationJson = SensorConfiguration.ToJson(new LinearForkSensorConfiguration
            {
                Length = 10,
                Resolution = 12
            })
        };
        var session = TestSnapshots.Session(
            id: Guid.NewGuid(),
            setupId: setup.Id,
            hasProcessedData: true);
        var source = CreateSource(session.Id);
        return new TestContext(session, setup, bike, source);
    }

    private static RecordedSessionSourceSnapshot CreateSource(Guid sessionId)
    {
        var payload = new byte[] { 4, 3, 2, 1 };
        var hash = RecordedSessionSourceHash.Compute(
            RecordedSessionSourceKind.ImportedSst,
            "session.SST",
            1,
            payload);
        return new RecordedSessionSourceSnapshot(
            sessionId,
            RecordedSessionSourceKind.ImportedSst,
            "session.SST",
            1,
            hash);
    }

    private sealed record TestContext(
        SessionSnapshot Session,
        SetupSnapshot Setup,
        BikeSnapshot Bike,
        RecordedSessionSourceSnapshot Source);
}
