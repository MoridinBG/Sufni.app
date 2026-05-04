using NSubstitute;
using Sufni.App.Models;
using Sufni.App.SessionGraph;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.SessionGraph;

public class RecordedSessionDomainQueryTests
{
    private readonly ISessionStore sessionStore = Substitute.For<ISessionStore>();
    private readonly ISetupStore setupStore = Substitute.For<ISetupStore>();
    private readonly IBikeStore bikeStore = Substitute.For<IBikeStore>();
    private readonly IRecordedSessionSourceStore sourceStore = Substitute.For<IRecordedSessionSourceStore>();
    private readonly ProcessingFingerprintService fingerprintService = new();

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
    public void Get_ReturnsCurrentDomain_WhenSessionDependenciesAndSourceMatch()
    {
        var context = CreateCurrentContext();
        sessionStore.Get(context.Session.Id).Returns(context.Session);
        setupStore.Get(context.Setup.Id).Returns(context.Setup);
        bikeStore.Get(context.Bike.Id).Returns(context.Bike);
        sourceStore.Get(context.Session.Id).Returns(context.Source);
        var query = CreateQuery();

        var domain = query.Get(context.Session.Id);

        Assert.NotNull(domain);
        Assert.Equal(context.Session, domain!.Session);
        Assert.Equal(context.Setup, domain.Setup);
        Assert.Equal(context.Bike, domain.Bike);
        Assert.Equal(context.Source, domain.Source);
        Assert.Equal(DerivedChangeKind.None, domain.ChangeKind);
        Assert.IsType<SessionStaleness.Current>(domain.Staleness);
        Assert.NotNull(domain.CurrentFingerprint);
        Assert.Equal(domain.CurrentFingerprint, domain.PersistedFingerprint);
    }

    [Fact]
    public void Get_ReturnsMissingRawSource_WhenSourceIsMissing()
    {
        var context = CreateCurrentContext();
        var session = context.Session with
        {
            ProcessingFingerprintJson = null
        };
        sessionStore.Get(session.Id).Returns(session);
        setupStore.Get(context.Setup.Id).Returns(context.Setup);
        bikeStore.Get(context.Bike.Id).Returns(context.Bike);
        sourceStore.Get(session.Id).Returns((RecordedSessionSourceSnapshot?)null);
        var query = CreateQuery();

        var domain = query.Get(session.Id);

        Assert.NotNull(domain);
        Assert.Null(domain!.Source);
        Assert.Null(domain.CurrentFingerprint);
        Assert.IsType<SessionStaleness.MissingRawSource>(domain.Staleness);
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
        fingerprintService);

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
        var hash = RecordedSessionSourceHash.Compute(
            RecordedSessionSourceKind.ImportedSst,
            "domain.SST",
            1,
            payload);
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
