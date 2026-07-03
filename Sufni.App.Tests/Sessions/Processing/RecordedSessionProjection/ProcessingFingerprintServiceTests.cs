using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sufni.Kinematics;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

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
    private static readonly Guid DependencyHashSetupId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DependencyHashBikeId = Guid.Parse("22222222-2222-2222-2222-222222222222");

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
    public void CreateCurrent_IncludesTrackProjectionVersion()
    {
        var context = CreateContext();

        var current = service.CreateCurrent(context.Session, context.Setup, context.Bike, context.Source);

        Assert.Equal(3, current.SchemaVersion);
        Assert.Equal(1, current.TrackProjectionVersion);
    }

    [Fact]
    public void CreateCurrent_UsesPrecomputedDependencyHash()
    {
        var context = CreateContext();
        const string dependencyHash = "precomputed-dependency-hash";

        var current = service.CreateCurrent(
            context.Session,
            context.Setup,
            context.Bike,
            context.Source,
            dependencyHash,
            new TelemetryProcessingOptions(100));

        Assert.Equal(dependencyHash, current.DependencyHash);
        Assert.Equal(100, current.VelocityFilterWindowMilliseconds);
    }

    [Fact]
    public void CreateCurrent_OmitsNullDerivationWindow_FromLegacyJsonShape()
    {
        var context = CreateContext();
        var current = service.CreateCurrent(context.Session, context.Setup, context.Bike, context.Source);

        var json = AppJson.Serialize(current);
        var roundTripped = AppJson.Serialize(service.Parse(json));

        Assert.DoesNotContain(nameof(ProcessingFingerprint.DerivationWindow), json);
        Assert.Equal(json, roundTripped);
    }

    [Fact]
    public void CreateCurrent_IncludesDerivationWindow_AndAllowsMatchingForeignSource()
    {
        var context = CreateContext();
        var sourceSessionId = Guid.NewGuid();
        var source = CreateSource(sourceSessionId);
        var window = new RecordedSessionDerivationWindow(sourceSessionId, 1.25, 3.5);

        var current = service.CreateCurrent(context.Session, context.Setup, context.Bike, source, window: window);

        Assert.Equal(window, current.DerivationWindow);
    }

    [Fact]
    public void CreateCurrent_RejectsForeignSource_WhenWindowDoesNotTargetIt()
    {
        var context = CreateContext();
        var source = CreateSource(Guid.NewGuid());
        var window = new RecordedSessionDerivationWindow(Guid.NewGuid(), 1.25, 3.5);

        Assert.Throws<InvalidOperationException>(() =>
            service.CreateCurrent(context.Session, context.Setup, context.Bike, source, window: window));
    }

    [Fact]
    public void Evaluate_ReturnsStaleRecomputable_WhenOnlyVelocityFilterOptionDiffers()
    {
        var context = CreateContext();
        // Persisted fingerprint produced at the 25 ms default option.
        var persisted = service.CreateCurrent(
            context.Session, context.Setup, context.Bike, context.Source, new TelemetryProcessingOptions(25));
        var session = context.Session with
        {
            HasProcessedData = true,
            ProcessingFingerprintJson = AppJson.Serialize(persisted)
        };

        // Same DB inputs (setup/bike/source/versions), only the velocity-filter window
        // changed: the stored BLOB is now stale and recomputable.
        var changed = service.Evaluate(
            session, context.Setup, context.Bike, context.Source, new TelemetryProcessingOptions(100));
        Assert.IsType<SessionStaleness.DependencyHashChanged>(changed);
        Assert.True(changed.IsStale);
        Assert.True(changed.CanRecompute);

        // Reverting to the option the BLOB was produced with re-evaluates as current,
        // proving the option is the only difference above.
        var unchanged = service.Evaluate(
            session, context.Setup, context.Bike, context.Source, new TelemetryProcessingOptions(25));
        Assert.IsType<SessionStaleness.Current>(unchanged);
    }

    [Fact]
    public void Evaluate_ReturnsCurrent_WhenPersistedFingerprintMatchesDerivationWindow()
    {
        var context = CreateContext();
        var window = new RecordedSessionDerivationWindow(context.Source.SessionId, 1, 2);
        var current = service.CreateCurrent(context.Session, context.Setup, context.Bike, context.Source, window: window);
        var session = context.Session with
        {
            HasProcessedData = true,
            ProcessingFingerprintJson = AppJson.Serialize(current)
        };

        var staleness = service.Evaluate(session, context.Setup, context.Bike, context.Source, window: window);

        Assert.IsType<SessionStaleness.Current>(staleness);
    }

    [Fact]
    public void Evaluate_ReturnsSourceWindowChanged_WhenOnlyDerivationWindowDiffers()
    {
        var context = CreateContext();
        var persistedWindow = new RecordedSessionDerivationWindow(context.Source.SessionId, 1, 5);
        var currentWindow = persistedWindow with { EndSeconds = 4 };
        var persisted = service.CreateCurrent(context.Session, context.Setup, context.Bike, context.Source, window: persistedWindow);
        var session = context.Session with
        {
            HasProcessedData = true,
            ProcessingFingerprintJson = AppJson.Serialize(persisted)
        };

        var staleness = service.Evaluate(session, context.Setup, context.Bike, context.Source, window: currentWindow);

        Assert.IsType<SessionStaleness.SourceWindowChanged>(staleness);
        Assert.True(staleness.IsStale);
        Assert.True(staleness.CanRecompute);
    }

    [Fact]
    public void Evaluate_ReturnsDependencyHashChanged_WhenWindowAndDependencyDiffer()
    {
        var context = CreateContext();
        var persistedWindow = new RecordedSessionDerivationWindow(context.Source.SessionId, 1, 5);
        var currentWindow = persistedWindow with { EndSeconds = 4 };
        var persisted = service.CreateCurrent(context.Session, context.Setup, context.Bike, context.Source, window: persistedWindow);
        var session = context.Session with
        {
            HasProcessedData = true,
            ProcessingFingerprintJson = AppJson.Serialize(persisted)
        };
        var changedBike = context.Bike with { HeadAngle = context.Bike.HeadAngle + 1 };

        var staleness = service.Evaluate(session, context.Setup, changedBike, context.Source, window: currentWindow);

        Assert.IsType<SessionStaleness.DependencyHashChanged>(staleness);
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
    public void Evaluate_ReturnsStaleMissingRawSource_WhenSourceIsMissingAndDerivationWindowDiffers()
    {
        var context = CreateContext();
        var persisted = service.CreateCurrent(context.Session, context.Setup, context.Bike, context.Source);
        var session = context.Session with { ProcessingFingerprintJson = AppJson.Serialize(persisted) };
        var window = new RecordedSessionDerivationWindow(context.Source.SessionId, 1, 2);

        var staleness = service.Evaluate(session, context.Setup, context.Bike, null, window: window);

        Assert.IsType<SessionStaleness.MissingRawSource>(staleness);
        Assert.True(staleness.IsStale);
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
    public void Evaluate_ReturnsUnknownLegacyFingerprint_WhenPersistedSchemaDiffers()
    {
        var context = CreateContext();
        var oldSchemaFingerprint = service.CreateCurrent(context.Session, context.Setup, context.Bike, context.Source) with
        {
            SchemaVersion = 1
        };
        var session = context.Session with { ProcessingFingerprintJson = AppJson.Serialize(oldSchemaFingerprint) };

        var staleness = service.Evaluate(session, context.Setup, context.Bike, context.Source);

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
    public void Evaluate_ReturnsDependencyHashChanged_WhenTrackProjectionVersionDiffers()
    {
        var context = CreateContext();
        var persisted = service.CreateCurrent(context.Session, context.Setup, context.Bike, context.Source) with
        {
            TrackProjectionVersion = 0
        };
        var session = context.Session with { ProcessingFingerprintJson = AppJson.Serialize(persisted) };

        var staleness = service.Evaluate(session, context.Setup, context.Bike, context.Source);

        Assert.IsType<SessionStaleness.DependencyHashChanged>(staleness);
        Assert.True(staleness.CanRecompute);
    }

    [Fact]
    public void Evaluate_ReturnsStaleMissingRawSource_WhenTrackProjectionVersionDiffers()
    {
        var context = CreateContext();
        var persisted = service.CreateCurrent(context.Session, context.Setup, context.Bike, context.Source) with
        {
            TrackProjectionVersion = 0
        };
        var session = context.Session with { ProcessingFingerprintJson = AppJson.Serialize(persisted) };

        var staleness = service.Evaluate(session, context.Setup, context.Bike, null);

        Assert.IsType<SessionStaleness.MissingRawSource>(staleness);
        Assert.True(staleness.IsStale);
        Assert.False(staleness.CanRecompute);
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
            FrontCompressionDampingCutoffMmPerSecond = 120,
            FrontReboundDampingCutoffMmPerSecond = 130,
            RearCompressionDampingCutoffMmPerSecond = 240,
            RearReboundDampingCutoffMmPerSecond = 250,
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

    [Fact]
    public void ProcessingDependencyHash_PreservesRearSuspensionPayloadShape()
    {
        var linkage = TestSnapshots.FullSuspensionLinkageSpec();
        var leverageRatio = TestSnapshots.LeverageRatioCurve((0, 0), (10, 25));
        var cases = new[]
        {
            HashCase(
                "hardtail",
                new RearSuspensionSpec.Hardtail(),
                shockStroke: null,
                rearSuspensionKind: "none",
                linkagePayload: null,
                leverageRatioPayload: null),
            HashCase(
                "linkage draft",
                new RearSuspensionSpec.LinkageDraft(),
                shockStroke: null,
                rearSuspensionKind: "linkage",
                linkagePayload: null,
                leverageRatioPayload: null),
            HashCase(
                "leverage-ratio draft",
                new RearSuspensionSpec.LeverageRatioDraft(),
                shockStroke: null,
                rearSuspensionKind: "leverage_ratio",
                linkagePayload: null,
                leverageRatioPayload: null),
            HashCase(
                "linkage",
                new RearSuspensionSpec.Linkage(linkage.WithShockStroke(0.6)),
                shockStroke: 0.6,
                rearSuspensionKind: "linkage",
                linkagePayload: linkage.WithShockStroke(0.6),
                leverageRatioPayload: null),
            HashCase(
                "leverage ratio",
                new RearSuspensionSpec.LeverageRatio(leverageRatio),
                shockStroke: 10,
                rearSuspensionKind: "leverage_ratio",
                linkagePayload: null,
                leverageRatioPayload: leverageRatio),
            HashCase(
                "legacy linkage null shock stroke",
                new RearSuspensionSpec.Linkage(linkage),
                shockStroke: linkage.ShockStroke,
                rearSuspensionKind: "linkage",
                linkagePayload: linkage,
                leverageRatioPayload: null),
            HashCase(
                "legacy linkage divergent shock stroke",
                new RearSuspensionSpec.Linkage(linkage.WithShockStroke(0.75)),
                shockStroke: 0.75,
                rearSuspensionKind: "linkage",
                linkagePayload: linkage.WithShockStroke(0.75),
                leverageRatioPayload: null),
        };

        foreach (var testCase in cases)
        {
            var setup = DependencyHashSetup();
            var bike = DependencyHashBike(testCase.RearSuspension, testCase.ShockStroke);

            Assert.Equal(Hash(testCase.CurrentPayload), ProcessingDependencyHash.Compute(setup, bike));
        }
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

    private static SetupSnapshot DependencyHashSetup() =>
        TestSnapshots.Setup(id: DependencyHashSetupId, bikeId: DependencyHashBikeId);

    private static BikeSnapshot DependencyHashBike(RearSuspensionSpec rearSuspension, double? shockStroke) =>
        TestSnapshots.Bike(id: DependencyHashBikeId) with
        {
            HeadAngle = 65,
            ForkStroke = 160,
            ShockStroke = shockStroke,
            RearSuspension = rearSuspension
        };

    private static DependencyHashCase HashCase(
        string name,
        RearSuspensionSpec rearSuspension,
        double? shockStroke,
        string rearSuspensionKind,
        LinkageSpec? linkagePayload,
        LeverageRatioSpec? leverageRatioPayload)
    {
        var currentLinkage = linkagePayload is null ? "null" : CurrentLinkagePayload(linkagePayload);
        var currentLeverageRatio = leverageRatioPayload is null ? "null" : CurrentLeverageRatioPayload(leverageRatioPayload);

        return new DependencyHashCase(
            name,
            rearSuspension,
            shockStroke,
            $$$"""{"Setup":{"Id":"{{{DependencyHashSetupId}}}","BikeId":"{{{DependencyHashBikeId}}}","FrontSensorConfiguration":null,"RearSensorConfiguration":null},"Bike":{"Id":"{{{DependencyHashBikeId}}}","HeadAngle":65,"ForkStroke":160,"ShockStroke":{{{JsonNumber(shockStroke)}}},"RearSuspensionKind":"{{{rearSuspensionKind}}}","Linkage":{{{currentLinkage}}},"LeverageRatio":{{{currentLeverageRatio}}}}}""");
    }

    private static string CurrentLinkagePayload(LinkageSpec linkage)
    {
        var joints = string.Join(
            ",",
            linkage.Joints
                .OrderBy(joint => joint.Name, StringComparer.Ordinal)
                .Select(joint => $$"""{"Name":{{JsonString(joint.Name)}},"Type":{{JsonValue(joint.Type)}},"X":{{JsonNumber(joint.X)}},"Y":{{JsonNumber(joint.Y)}}}"""));
        var links = string.Join(
            ",",
            linkage.Links
                .OrderBy(link => link.A, StringComparer.Ordinal)
                .ThenBy(link => link.B, StringComparer.Ordinal)
                .Select(link => $$"""{"AName":{{JsonString(link.A)}},"BName":{{JsonString(link.B)}}}"""));

        return $$"""{"ShockStroke":{{JsonNumber(linkage.ShockStroke)}},"ShockAName":{{JsonString(linkage.Shock.A)}},"ShockBName":{{JsonString(linkage.Shock.B)}},"Joints":[{{joints}}],"Links":[{{links}}]}""";
    }

    private static string CurrentLeverageRatioPayload(LeverageRatioSpec leverageRatio)
    {
        var points = string.Join(
            ",",
            leverageRatio.Points.Select(point =>
                $$"""{"ShockTravelMm":{{JsonNumber(point.ShockTravelMm)}},"WheelTravelMm":{{JsonNumber(point.WheelTravelMm)}}}"""));
        return $$"""{"Points":[{{points}}]}""";
    }

    private static string JsonString(string value) => JsonSerializer.Serialize(value);

    private static string JsonValue<T>(T value) => JsonSerializer.Serialize(value, AppJson.Options);

    private static string JsonNumber(double? value) =>
        value.HasValue
            ? JsonNumber(value.Value)
            : "null";

    private static string JsonNumber(double value) =>
        JsonSerializer.Serialize(value, AppJson.Options);

    private static string Hash(string payload) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    private static RecordedSessionSourceSnapshot CreateSource(Guid sessionId)
    {
        byte[] payload = [4, 3, 2, 1];
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

    private sealed record DependencyHashCase(
        string Name,
        RearSuspensionSpec RearSuspension,
        double? ShockStroke,
        string CurrentPayload);
}
