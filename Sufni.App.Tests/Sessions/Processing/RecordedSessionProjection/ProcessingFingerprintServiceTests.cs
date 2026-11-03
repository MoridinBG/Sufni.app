using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sufni.Kinematics;
using Sufni.Telemetry;

using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Sessions.Processing.RecordedSessionProjection;

public class ProcessingFingerprintServiceTests
{

    private readonly ProcessingFingerprintService service = new();

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

        var evaluation = service.EvaluateState(session, context.Setup, context.Bike, context.Source, dependencyHash: null);

        Assert.Equal(current, evaluation.Current);
        Assert.Equal(current, evaluation.Persisted);
        Assert.IsType<SessionStaleness.Current>(evaluation.Staleness);
    }

    [Fact]
    public void CreateCurrent_RejectsForeignSource_WhenWindowDoesNotTargetIt()
    {
        var context = CreateContext();
        var source = CreateSource(Guid.NewGuid());
        var window = new RecordedSessionDerivationWindow(Guid.NewGuid(), 1.25, 3.5);

        Assert.Throws<InvalidOperationException>(() =>
            service.CreateCurrent(WithWindow(context.Session, window), context.Setup, context.Bike, source));
    }

    [Fact]
    public void EvaluateState_ReturnsStaleRecomputable_WhenOnlyVelocityFilterOptionDiffers()
    {
        var context = CreateContext();
        // Persisted fingerprint produced at the 25 ms default option.
        var persisted = service.CreateCurrent(
            context.Session with { VelocityFilterWindowMilliseconds = 25 }, context.Setup, context.Bike, context.Source);
        var session = context.Session with
        {
            HasProcessedData = true,
            ProcessingFingerprintJson = AppJson.Serialize(persisted)
        };

        var changed = service.EvaluateState(
            session with { VelocityFilterWindowMilliseconds = 100 }, context.Setup, context.Bike, context.Source, dependencyHash: null).Staleness;
        Assert.IsType<SessionStaleness.Stale>(changed);
        Assert.True(changed.IsStale);
        Assert.True(changed.CanRecompute);

        // Reverting to the option the BLOB was produced with re-evaluates as current,
        // proving the option is the only difference above.
        var unchanged = service.EvaluateState(
            session with { VelocityFilterWindowMilliseconds = 25 }, context.Setup, context.Bike, context.Source, dependencyHash: null).Staleness;
        Assert.IsType<SessionStaleness.Current>(unchanged);
    }

    [Fact]
    public void EvaluateState_ReturnsCurrent_WhenPersistedFingerprintMatchesDerivationWindow()
    {
        var context = CreateContext();
        var window = new RecordedSessionDerivationWindow(context.Source.SessionId, 1, 2);
        var session = WithWindow(context.Session, window);
        var current = service.CreateCurrent(session, context.Setup, context.Bike, context.Source);
        session = session with
        {
            HasProcessedData = true,
            ProcessingFingerprintJson = AppJson.Serialize(current)
        };

        var staleness = service.EvaluateState(session, context.Setup, context.Bike, context.Source, dependencyHash: null).Staleness;

        Assert.IsType<SessionStaleness.Current>(staleness);
    }

    [Fact]
    public void EvaluateState_ReturnsStale_WhenOnlyDerivationWindowDiffers()
    {
        var context = CreateContext();
        var persistedWindow = new RecordedSessionDerivationWindow(context.Source.SessionId, 1, 5);
        var currentWindow = persistedWindow with { EndSeconds = 4 };
        var persisted = service.CreateCurrent(
            WithWindow(context.Session, persistedWindow),
            context.Setup,
            context.Bike,
            context.Source);
        var session = WithWindow(context.Session, currentWindow) with
        {
            HasProcessedData = true,
            ProcessingFingerprintJson = AppJson.Serialize(persisted)
        };

        var staleness = service.EvaluateState(session, context.Setup, context.Bike, context.Source, dependencyHash: null).Staleness;

        Assert.IsType<SessionStaleness.Stale>(staleness);
        Assert.True(staleness.IsStale);
        Assert.True(staleness.CanRecompute);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void EvaluateState_ReturnsMissingDependencies_WhenDependencyIsMissing(
        bool setupMissing,
        bool bikeMissing)
    {
        var context = CreateContext();
        SetupSnapshot? setup = setupMissing ? null : context.Setup;
        BikeSnapshot? bike = bikeMissing ? null : context.Bike;

        var staleness = service.EvaluateState(context.Session, setup, bike, context.Source, dependencyHash: null).Staleness;

        var missingDependencies = Assert.IsType<SessionStaleness.MissingDependencies>(staleness);
        Assert.Equal(setupMissing, missingDependencies.SetupMissing);
        Assert.Equal(bikeMissing, missingDependencies.BikeMissing);
    }

    [Theory]
    [InlineData(MissingRawSourceCase.SourceMissing, false)]
    [InlineData(MissingRawSourceCase.DerivationWindowDiffers, true)]
    [InlineData(MissingRawSourceCase.SourceAndDependenciesMissing, true)]
    [InlineData(MissingRawSourceCase.ProcessedDataMissing, true)]
    [InlineData(MissingRawSourceCase.DependencyHashChanged, true)]
    [InlineData(MissingRawSourceCase.TrackProjectionVersionDiffers, true)]
    [InlineData(MissingRawSourceCase.CalibrationVersionDiffers, true)]
    public void EvaluateState_ReturnsMissingRawSource_WhenRawSourceIsUnavailable(
        MissingRawSourceCase testCase,
        bool expectedStale)
    {
        var context = CreateContext();
        var session = context.Session;
        SetupSnapshot? setup = context.Setup;
        BikeSnapshot? bike = context.Bike;
        RecordedSessionDerivationWindow? window = null;

        switch (testCase)
        {
            case MissingRawSourceCase.SourceMissing:
                session = PersistCurrentFingerprint(context);
                break;
            case MissingRawSourceCase.DerivationWindowDiffers:
                session = PersistCurrentFingerprint(context);
                window = new RecordedSessionDerivationWindow(context.Source.SessionId, 1, 2);
                break;
            case MissingRawSourceCase.SourceAndDependenciesMissing:
                setup = null;
                bike = null;
                break;
            case MissingRawSourceCase.ProcessedDataMissing:
                session = context.Session with { HasProcessedData = false };
                break;
            case MissingRawSourceCase.DependencyHashChanged:
                session = PersistCurrentFingerprint(context);
                bike = context.Bike with { HeadAngle = context.Bike.HeadAngle + 1 };
                break;
            case MissingRawSourceCase.CalibrationVersionDiffers:
                var oldCalibration = service.CreateCurrent(context.Session, context.Setup, context.Bike, context.Source) with
                {
                    CalibrationVersion = 0
                };
                session = context.Session with { ProcessingFingerprintJson = AppJson.Serialize(oldCalibration) };
                break;
            case MissingRawSourceCase.TrackProjectionVersionDiffers:
                var persisted = service.CreateCurrent(context.Session, context.Setup, context.Bike, context.Source) with
                {
                    TrackProjectionVersion = 0
                };
                session = context.Session with { ProcessingFingerprintJson = AppJson.Serialize(persisted) };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(testCase), testCase, null);
        }

        if (window is not null)
        {
            session = WithWindow(session, window);
        }

        var staleness = service.EvaluateState(session, setup, bike, null, dependencyHash: null).Staleness;

        Assert.IsType<SessionStaleness.MissingRawSource>(staleness);
        Assert.Equal(expectedStale, staleness.IsStale);
        Assert.False(staleness.CanRecompute);
    }

    [Theory]
    [InlineData(ProcessedDataCase.ProcessedDataMissing)]
    [InlineData(ProcessedDataCase.FingerprintMissing)]
    [InlineData(ProcessedDataCase.SchemaVersionDiffers)]
    public void EvaluateState_ReturnsExpectedStaleness_WhenProcessedDataOrFingerprintStateDiffers(
        ProcessedDataCase testCase)
    {
        var context = CreateContext();
        var session = context.Session;

        switch (testCase)
        {
            case ProcessedDataCase.ProcessedDataMissing:
                session = PersistCurrentFingerprint(context) with { HasProcessedData = false };
                break;
            case ProcessedDataCase.FingerprintMissing:
                break;
            case ProcessedDataCase.SchemaVersionDiffers:
                var oldSchemaFingerprint = service.CreateCurrent(context.Session, context.Setup, context.Bike, context.Source) with
                {
                    SchemaVersion = 1
                };
                session = session with { ProcessingFingerprintJson = AppJson.Serialize(oldSchemaFingerprint) };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(testCase), testCase, null);
        }

        var staleness = service.EvaluateState(session, context.Setup, context.Bike, context.Source, dependencyHash: null).Staleness;

        Assert.IsType<SessionStaleness.Stale>(staleness);
        Assert.True(staleness.CanRecompute);
    }

    [Theory]
    [InlineData(FingerprintDifferenceCase.CalibrationVersion)]
    [InlineData(FingerprintDifferenceCase.ProcessingVersion)]
    [InlineData(FingerprintDifferenceCase.TrackProjectionVersion)]
    [InlineData(FingerprintDifferenceCase.ProcessingDependency)]
    public void EvaluateState_ReturnsExpectedStaleness_WhenFingerprintInputDiffers(
        FingerprintDifferenceCase testCase)
    {
        var context = CreateContext();
        var persisted = service.CreateCurrent(context.Session, context.Setup, context.Bike, context.Source);
        var bike = context.Bike;

        switch (testCase)
        {
            case FingerprintDifferenceCase.CalibrationVersion:
                persisted = persisted with { CalibrationVersion = 0 };
                break;
            case FingerprintDifferenceCase.ProcessingVersion:
                persisted = persisted with { ProcessingVersion = TelemetryProcessingVersion.Current - 1 };
                break;
            case FingerprintDifferenceCase.TrackProjectionVersion:
                persisted = persisted with { TrackProjectionVersion = 0 };
                break;
            case FingerprintDifferenceCase.ProcessingDependency:
                bike = bike with { HeadAngle = bike.HeadAngle + 1 };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(testCase), testCase, null);
        }

        var session = context.Session with { ProcessingFingerprintJson = AppJson.Serialize(persisted) };
        var staleness = service.EvaluateState(session, context.Setup, bike, context.Source, dependencyHash: null).Staleness;

        Assert.IsType<SessionStaleness.Stale>(staleness);
        Assert.True(staleness.CanRecompute);
    }

    [Fact]
    public void Parse_LegacyFingerprintWithoutCalibrationOrWindow_RemainsReadableAndStale()
    {
        const string legacy = """{"SchemaVersion":3,"ProcessingVersion":5,"SetupId":"11111111-1111-1111-1111-111111111111","BikeId":"22222222-2222-2222-2222-222222222222","TrackProjectionVersion":1,"DependencyHash":"old-calibration","SourceHash":"captured"}""";
        var parsed = service.Parse(legacy);
        Assert.NotNull(parsed);
        Assert.Equal(0, parsed.CalibrationVersion);
        Assert.Null(parsed.DerivationWindow);
        var context = CreateContext();

        var result = service.EvaluateState(context.Session with { ProcessingFingerprintJson = legacy },
            context.Setup, context.Bike, context.Source, dependencyHash: null);

        Assert.IsType<SessionStaleness.Stale>(result.Staleness);
        Assert.True(result.Staleness.CanRecompute);
    }

    [Fact]
    public void CreateCurrent_ReorderedIndependentLinkageCollectionsKeepFingerprint()
    {
        var context = CreateContext();
        var linkage = TestSnapshots.FullSuspensionLinkageSpec();
        var bike = context.Bike with { ShockStroke = linkage.ShockStroke, RearSuspension = new RearSuspensionSpec.Linkage(linkage) };
        var reordered = new LinkageSpec(linkage.Joints.Reverse().ToArray(), linkage.Links.Reverse().ToArray(), linkage.Shock, linkage.ShockStroke);

        var original = service.CreateCurrent(context.Session, context.Setup, bike, context.Source);
        var equivalent = service.CreateCurrent(context.Session, context.Setup,
            bike with { RearSuspension = new RearSuspensionSpec.Linkage(reordered) }, context.Source);
        var changed = service.CreateCurrent(context.Session, context.Setup, bike with { HeadAngle = bike.HeadAngle + 1 }, context.Source);

        Assert.Equal(original, equivalent);
        Assert.NotEqual(original, changed);
    }

    [Fact]
    public void Parse_ReturnsNull_WhenJsonIsMalformed()
    {
        var session = TestSnapshots.Session(processingFingerprintJson: "{bad json", hasProcessedData: true);

        var parsed = service.Parse(session.ProcessingFingerprintJson);

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
            Updated = 99
        };
        var changedBike = context.Bike with
        {
            Name = "renamed bike",
            HasImage = true,
            FrontCompressionDampingCutoffMmPerSecond = 120,
            FrontReboundDampingCutoffMmPerSecond = 130,
            RearCompressionDampingCutoffMmPerSecond = 240,
            RearReboundDampingCutoffMmPerSecond = 250,
            Updated = 99
        };

        var original = service.CreateCurrent(context.Session, context.Setup, context.Bike, context.Source);
        var changed = service.CreateCurrent(context.Session, changedSetup, changedBike, context.Source);

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

        var original = service.CreateCurrent(context.Session, context.Setup, context.Bike, context.Source);
        var changed = service.CreateCurrent(context.Session, changedSetup, context.Bike, context.Source);

        Assert.NotEqual(original, changed);
    }

    private SessionSnapshot PersistCurrentFingerprint(TestContext context)
    {
        var current = service.CreateCurrent(context.Session, context.Setup, context.Bike, context.Source);
        return context.Session with { ProcessingFingerprintJson = AppJson.Serialize(current) };
    }

    private static SessionSnapshot WithWindow(SessionSnapshot session, RecordedSessionDerivationWindow window) =>
        session with
        {
            SourceSessionId = window.SourceSessionId,
            WindowStartSeconds = window.StartSeconds,
            WindowEndSeconds = window.EndSeconds
        };

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
        byte[] payload = [4, 3, 2, 1];
        var hash = RecordedSessionSourceHash.ComputeImportedSst(payload);
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

    public enum MissingRawSourceCase
    {
        SourceMissing,
        DerivationWindowDiffers,
        SourceAndDependenciesMissing,
        ProcessedDataMissing,
        DependencyHashChanged,
        TrackProjectionVersionDiffers,
        CalibrationVersionDiffers
    }

    public enum ProcessedDataCase
    {
        ProcessedDataMissing,
        FingerprintMissing,
        SchemaVersionDiffers
    }

    public enum FingerprintDifferenceCase
    {
        ProcessingVersion,
        CalibrationVersion,
        TrackProjectionVersion,
        ProcessingDependency
    }
}
