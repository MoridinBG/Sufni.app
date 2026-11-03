using Sufni.App.MapsAndTracks.Stores;
using NSubstitute;
using Sufni.Telemetry;

using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Sessions.Processing.RecordedSessionProjection;

public class RecordedSessionDomainQueryTests
{
    private readonly ISessionStore sessionStore = Substitute.For<ISessionStore>();
    private readonly ISetupStore setupStore = Substitute.For<ISetupStore>();
    private readonly IBikeStore bikeStore = Substitute.For<IBikeStore>();
    private readonly IRecordedSessionSourceStore sourceStore = Substitute.For<IRecordedSessionSourceStore>();
    private readonly IProcessingDependencyHashIndex dependencyHashIndex = Substitute.For<IProcessingDependencyHashIndex>();
    private readonly ProcessingFingerprintService fingerprintService = new();

    public RecordedSessionDomainQueryTests()
    {
    }

    [Fact]
    public void Get_ReturnsNull_WhenSessionIsMissing()
    {
        var query = CreateQuery();
        var sessionId = Guid.NewGuid();
        sessionStore.Get(sessionId).Returns((SessionSnapshot?)null);

        var domain = query.Get(sessionId);

        Assert.Null(domain);
    }

    [Fact]
    public void Get_UsesDerivationWindowSource_WhenWindowTargetsForeignSource()
    {
        var bike = TestSnapshots.Bike(id: Guid.NewGuid());
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), bikeId: bike.Id);
        var session = TestSnapshots.Session(
            id: Guid.NewGuid(),
            setupId: setup.Id,
            hasProcessedData: true);
        var source = CreateSource(Guid.NewGuid());
        session = session with
        {
            SourceSessionId = source.SessionId,
            WindowStartSeconds = 1.25,
            WindowEndSeconds = 3.5
        };
        var fingerprint = fingerprintService.CreateCurrent(session, setup, bike, source);
        session = session with { ProcessingFingerprintJson = AppJson.Serialize(fingerprint) };
        sessionStore.Get(session.Id).Returns(session);
        setupStore.Get(setup.Id).Returns(setup);
        bikeStore.Get(bike.Id).Returns(bike);
        sourceStore.Get(source.SessionId).Returns(source);
        sourceStore.Get(session.Id).Returns(CreateSource(session.Id) with { SourceHash = "wrong-own-source" });
        dependencyHashIndex.GetForSetup(setup.Id).Returns(ProcessingDependencyHash.Compute(setup, bike));
        var query = CreateQuery();

        var domain = query.Get(session.Id);

        Assert.NotNull(domain);
        Assert.Equal(source, domain!.Source);
        Assert.IsType<SessionStaleness.Current>(domain.Staleness);
        Assert.Equal(fingerprint, domain.CurrentFingerprint);
        Assert.Equal(new RecordedSessionDerivationWindow(source.SessionId, 1.25, 3.5), domain.Session.DerivationWindow);
    }

    [Fact]
    public void Get_ReturnsMissingDependencies_WhenSetupIsMissing()
    {
        var context = CreateCurrentContext();
        sessionStore.Get(context.Session.Id).Returns(context.Session);
        setupStore.Get(context.Setup.Id).Returns((SetupSnapshot?)null);
        sourceStore.Get(context.Session.Id).Returns(context.Source);
        var query = CreateQuery();

        var domain = query.Get(context.Session.Id);

        Assert.NotNull(domain);
        Assert.Null(domain!.Setup);
        Assert.Null(domain.Bike);
        Assert.Null(domain.CurrentFingerprint);
        var staleness = Assert.IsType<SessionStaleness.MissingDependencies>(domain.Staleness);
        Assert.True(staleness.SetupMissing);
        Assert.True(staleness.BikeMissing);
    }

    private RecordedSessionDomainQuery CreateQuery() => new(
        sessionStore,
        setupStore,
        bikeStore,
        sourceStore,
        fingerprintService,
        dependencyHashIndex, Substitute.For<ITrackStore>());

    private TestContext CreateCurrentContext()
    {
        var bike = TestSnapshots.Bike(id: Guid.NewGuid());
        var setup = TestSnapshots.Setup(id: Guid.NewGuid(), bikeId: bike.Id);
        var session = TestSnapshots.Session(
            id: Guid.NewGuid(),
            setupId: setup.Id,
            hasProcessedData: true);
        var source = CreateSource(session.Id);
        var fingerprint = fingerprintService.CreateCurrent(session, setup, bike, source);
        session = session with { ProcessingFingerprintJson = AppJson.Serialize(fingerprint) };
        return new TestContext(session, setup, bike, source);
    }

    private static RecordedSessionSourceSnapshot CreateSource(Guid sessionId)
    {
        var payload = new byte[] { 1, 9, 2, 8 };
        var hash = RecordedSessionSourceHash.ComputeImportedSst(payload);
        return new RecordedSessionSourceSnapshot(
            sessionId,
            RecordedSessionSourceKind.ImportedSst,
            "domain.SST",
            1,
            hash);
    }

    private sealed record TestContext(
        SessionSnapshot Session,
        SetupSnapshot Setup,
        BikeSnapshot Bike,
        RecordedSessionSourceSnapshot Source);
}
