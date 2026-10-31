using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Shared.Stores;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.Sessions.Coordination;

public class SessionSyncGenerationTests : IDisposable
{
    private readonly InMemorySessionRepository repository = new(new FakeTimeProvider());
    private readonly StorePublication publication;
    private readonly SessionStore sessions;
    private readonly RecordedSessionSourceStore sources;
    private readonly SessionSyncApplier applier;

    public SessionSyncGenerationTests()
    {
        var dispatcher = new InlineUiThreadDispatcher();
        publication = new StorePublication(dispatcher);
        sessions = new SessionStore(publication);
        sources = new RecordedSessionSourceStore(publication);
        applier = new SessionSyncApplier(sessions, sources, dispatcher, Substitute.For<IRecordedSessionSourceRepository>(),
            repository.Repository, new InlineBackgroundTaskRunner(), new SessionTelemetryProcessor(), repository.SwapRequests);
    }

    [Theory]
    [InlineData("other", true, SessionDataApplyResult.Superseded)]
    [InlineData("target", false, SessionDataApplyResult.InvalidPayload)]
    [InlineData("target", true, SessionDataApplyResult.Applied)]
    public async Task ApplyUploadedAsync_KeepsRequestUntilMatchingValidBytesCommit(
        string fingerprint, bool valid, SessionDataApplyResult expectedResult)
    {
        var session = SeedSession();
        repository.SeedSwap(new SessionBlobSwap(session.Id, "target"));
        var incoming = valid ? PersistenceTestData.CreateTelemetryBlob(80) : new byte[] { 1, 2, 3 };

        var result = await applier.ApplyUploadedAsync(session.Id, new SessionBlobPayload(fingerprint, incoming));

        Assert.Equal(expectedResult, result);
        if (result == SessionDataApplyResult.Applied)
        {
            Assert.Null(await repository.SwapRequests.GetRequestAsync(session.Id));
            Assert.Equal(incoming, await repository.Repository.GetSessionRawPsstAsync(session.Id));
            Assert.Equal("target", sessions.Get(session.Id)!.ProcessingFingerprintJson);
        }
        else
        {
            Assert.Equal("target", (await repository.SwapRequests.GetRequestAsync(session.Id))!.TargetFingerprint);
            Assert.Equal(session.ProcessedData, await repository.Repository.GetSessionRawPsstAsync(session.Id));
            Assert.Null(sessions.Get(session.Id));
        }
    }

    [Fact]
    public async Task ApplyDownloadedAsync_DoesNotReplaceGenerationChangedDuringTransfer()
    {
        var session = SeedSession();
        var captured = SessionSnapshot.From(session);
        var newer = PersistenceTestData.CreateTelemetryBlob(80);
        Assert.True(await repository.Repository.UpdateSessionPsstAsync(session.Id, newer, "held",
            80, SessionProcessedWriteGuard.From(captured)));

        var result = await applier.ApplyDownloadedAsync(captured,
            new SessionBlobPayload("target", PersistenceTestData.CreateTelemetryBlob(90)), "target");

        Assert.Equal(SessionDataApplyResult.Superseded, result);
        Assert.Equal(newer, await repository.Repository.GetSessionRawPsstAsync(session.Id));
        Assert.Null(sessions.Get(session.Id));
    }

    [Fact]
    public async Task ApplyUploadedAsync_UpdatesDuration_AndPreservesGpsMetricsWithoutTrackPoints()
    {
        var session = SeedSession(withData: false);

        var result = await applier.ApplyUploadedAsync(session.Id,
            new SessionBlobPayload("held", PersistenceTestData.CreateTelemetryBlob(80)));

        Assert.Equal(SessionDataApplyResult.Applied, result);
        var saved = sessions.Get(session.Id)!;
        Assert.Equal(80, saved.DurationSeconds);
        Assert.Equal(10, saved.DistanceMeters);
        Assert.Equal(4, saved.AscentMeters);
        Assert.Equal(2, saved.DescentMeters);
        Assert.Equal(session.Updated, saved.Updated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyUploadedAsync_RejectsInvalidBytesOrMismatchedFingerprint(bool invalidBytes)
    {
        var session = SeedSession();
        var payload = new SessionBlobPayload(invalidBytes ? "held" : "different",
            invalidBytes ? [1, 2, 3] : PersistenceTestData.CreateTelemetryBlob(80));

        var result = await applier.ApplyUploadedAsync(session.Id, payload);

        Assert.Equal(SessionDataApplyResult.InvalidPayload, result);
        Assert.Equal(session.ProcessedData, await repository.Repository.GetSessionRawPsstAsync(session.Id));
        Assert.Equal("held", (await repository.Repository.GetSessionAsync(session.Id))!.ProcessingFingerprintJson);
        Assert.Null(sessions.Get(session.Id));
    }

    [Fact]
    public async Task ApplyDownloadedAsync_PublishesCompleteGeneration_WithoutChangingAuthoredFields()
    {
        var session = SeedSession();
        var data = PersistenceTestData.CreateTelemetryBlob(80);
        var generation = new SessionProcessedGeneration(80);

        var result = await applier.ApplyDownloadedAsync(SessionSnapshot.From(session),
            new SessionBlobPayload("target", data), "target", generation);

        Assert.Equal(SessionDataApplyResult.Applied, result);
        var saved = (await repository.Repository.GetSessionAsync(session.Id))!;
        Assert.Equal(session.Updated, saved.Updated);
        Assert.Equal("notes", saved.Description);
        Assert.Equal(7, saved.GpsOffsetSeconds);
        Assert.Equal(data, await repository.Repository.GetSessionRawPsstAsync(session.Id));
        Assert.Equal(generation.DurationSeconds, saved.DurationSeconds);
        Assert.Equal(session.DistanceMeters, saved.DistanceMeters);

        Assert.Equal(SessionSnapshot.From(saved), sessions.Get(session.Id));
    }

    [Fact]
    public async Task ApplyDownloadedAsync_LegacySwap_ReplacesHeldFingerprintAndBytes()
    {
        var session = SeedSession();
        var data = PersistenceTestData.CreateTelemetryBlob(80);

        var result = await applier.ApplyDownloadedAsync(SessionSnapshot.From(session), new SessionBlobPayload("target", data), "target");

        Assert.Equal(SessionDataApplyResult.Applied, result);
        Assert.Equal(data, await repository.Repository.GetSessionRawPsstAsync(session.Id));
        Assert.Equal("target", sessions.Get(session.Id)!.ProcessingFingerprintJson);
        Assert.Equal(80, sessions.Get(session.Id)!.DurationSeconds);
    }

    [Fact]
    public async Task ApplyDownloadedAsync_PersistenceFailure_PreservesHeldGenerationAndPublishedState()
    {
        var session = SeedSession();
        var original = SessionSnapshot.From(session);
        await sessions.ReplaceAllAsync([original]);
        repository.WriteFailure = new IOException("disk full");

        var result = await applier.ApplyDownloadedAsync(original,
            new SessionBlobPayload("target", PersistenceTestData.CreateTelemetryBlob(80)), "target");

        Assert.Equal(SessionDataApplyResult.Failed, result);
        Assert.Equal(original, sessions.Get(session.Id));
        Assert.Equal(session.ProcessedData, await repository.Repository.GetSessionRawPsstAsync(session.Id));
    }

    private Session SeedSession(bool withData = true)
    {
        var session = new Session(Guid.NewGuid(), "ride", "notes", null, 100)
        {
            ProcessedData = withData ? PersistenceTestData.CreateTelemetryBlob(65) : null,
            ProcessingFingerprintJson = "held", ProcessedTelemetryRevision = withData ? 1 : 0,
            Updated = 12, DurationSeconds = 65, DistanceMeters = 10, AscentMeters = 4, DescentMeters = 2, GpsOffsetSeconds = 7
        };
        repository.Seed(session);
        return session;
    }

    public void Dispose()
    {
        applier.Dispose();
        sessions.Dispose();
        sources.Dispose();
        publication.Dispose();
    }
}
