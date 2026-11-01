using Sufni.App.Infrastructure;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Bikes.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
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
    private readonly SessionSyncApplier applier;
    private string targetFingerprint = string.Empty;

    public SessionSyncGenerationTests()
    {
        var dispatcher = new InlineUiThreadDispatcher();
        publication = new StorePublication(dispatcher);
        sessions = new SessionStore(publication);
        applier = new SessionSyncApplier(sessions, repository.Repository, new InlineBackgroundTaskRunner(), new SessionTelemetryProcessor(), repository.SwapRequests, new ProcessingFingerprintService());
    }

    [Theory]
    [InlineData("other", true, SessionDataApplyResult.Pending)]
    [InlineData("target", false, SessionDataApplyResult.InvalidPayload)]
    [InlineData("target", true, SessionDataApplyResult.Applied)]
    public async Task ApplyUploadedAsync_KeepsRequestUntilMatchingValidBytesCommit(
        string fingerprint, bool valid, SessionDataApplyResult expectedResult)
    {
        var session = SeedSession();
        fingerprint = fingerprint == "target" ? targetFingerprint : fingerprint;
        repository.SeedSwap(new SessionBlobSwap(session.Id, targetFingerprint, new SessionProcessedGeneration(80)));
        var incoming = valid ? PersistenceTestData.CreateTelemetryBlob(80) : new byte[] { 1, 2, 3 };

        var result = await applier.ApplyUploadedAsync(session.Id, new SessionBlobPayload(fingerprint, incoming));

        Assert.Equal(expectedResult, result);
        if (result == SessionDataApplyResult.Applied)
        {
            Assert.Null(await repository.SwapRequests.GetRequestAsync(session.Id));
            Assert.Equal(incoming, await repository.Repository.GetSessionRawPsstAsync(session.Id));
            Assert.Equal(targetFingerprint, sessions.Get(session.Id)!.ProcessingFingerprintJson);
        }
        else
        {
            Assert.Equal(targetFingerprint, (await repository.SwapRequests.GetRequestAsync(session.Id))!.TargetFingerprint);
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
            new SessionBlobPayload(targetFingerprint, PersistenceTestData.CreateTelemetryBlob(90)), targetFingerprint, new SessionProcessedGeneration(90));

        Assert.Equal(SessionDataApplyResult.Pending, result);
        Assert.Equal(newer, await repository.Repository.GetSessionRawPsstAsync(session.Id));
        Assert.Null(sessions.Get(session.Id));
    }

    [Fact]
    public async Task ApplyUploadedAsync_UpdatesDuration_AndPreservesGpsMetricsWithoutTrackPoints()
    {
        var session = SeedSession(withData: false);

        var result = await applier.ApplyUploadedAsync(session.Id,
            new SessionBlobPayload(session.ProcessingFingerprintJson, PersistenceTestData.CreateTelemetryBlob(80)));

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
        var payload = new SessionBlobPayload(invalidBytes ? session.ProcessingFingerprintJson : "different",
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
            new SessionBlobPayload(targetFingerprint, data), targetFingerprint, generation);

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
    public async Task ApplyDownloadedAsync_FillCannotReplaceHeldGeneration()
    {
        var session = SeedSession();
        var data = PersistenceTestData.CreateTelemetryBlob(80);

        var result = await applier.ApplyDownloadedAsync(SessionSnapshot.From(session), new SessionBlobPayload(targetFingerprint, data), targetFingerprint);

        Assert.Equal(SessionDataApplyResult.Ignored, result);
        Assert.Equal(session.ProcessedData, await repository.Repository.GetSessionRawPsstAsync(session.Id));
        Assert.Null(sessions.Get(session.Id));
    }

    [Fact]
    public async Task ApplyDownloadedAsync_PersistenceFailure_PreservesHeldGenerationAndPublishedState()
    {
        var session = SeedSession();
        var original = SessionSnapshot.From(session);
        await sessions.ReplaceAllAsync([original]);
        repository.WriteFailure = new IOException("disk full");

        var result = await applier.ApplyDownloadedAsync(original,
            new SessionBlobPayload(targetFingerprint, PersistenceTestData.CreateTelemetryBlob(80)), targetFingerprint, new SessionProcessedGeneration(80));

        Assert.Equal(SessionDataApplyResult.Failed, result);
        Assert.Equal(original, sessions.Get(session.Id));
        Assert.Equal(session.ProcessedData, await repository.Repository.GetSessionRawPsstAsync(session.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Transfer_WaitsForSourceInputs_ThenRetriesWithoutLosingHeldData(bool upload)
    {
        var session = SeedSession();
        var inputs = await repository.Repository.GetProcessingInputBundleAsync(session.Id);
        repository.Repository.GetProcessingInputBundleAsync(session.Id).Returns((SessionProcessingInputBundle?)null);
        var generation = new SessionProcessedGeneration(80);
        if (upload) repository.SeedSwap(new SessionBlobSwap(session.Id, targetFingerprint, generation));
        var payload = new SessionBlobPayload(targetFingerprint, PersistenceTestData.CreateTelemetryBlob(80));
        Task<SessionDataApplyResult> Apply() => upload
            ? applier.ApplyUploadedAsync(session.Id, payload)
            : applier.ApplyDownloadedAsync(SessionSnapshot.From(session), payload, targetFingerprint, generation);

        Assert.Equal(SessionDataApplyResult.Pending, await Apply());
        Assert.Equal(session.ProcessedData, await repository.Repository.GetSessionRawPsstAsync(session.Id));
        Assert.Null(sessions.Get(session.Id));
        repository.Repository.GetProcessingInputBundleAsync(session.Id).Returns(inputs);

        Assert.Equal(SessionDataApplyResult.Applied, await Apply());
        Assert.Equal(payload.Data, await repository.Repository.GetSessionRawPsstAsync(session.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Transfer_KeepsCurrentLocalGeneration_AndCompletesRequest_WhenIncomingFingerprintIsEquivalent(bool upload)
    {
        var session = SeedSession();
        session.ProcessingFingerprintJson = targetFingerprint;
        repository.Seed(session);
        var payload = new SessionBlobPayload(targetFingerprint, PersistenceTestData.CreateTelemetryBlob(80));
        repository.SeedSwap(new SessionBlobSwap(session.Id, targetFingerprint, new SessionProcessedGeneration(80)));

        var result = upload ? await applier.ApplyUploadedAsync(session.Id, payload)
            : await applier.ApplyDownloadedAsync(SessionSnapshot.From(session), payload,
                targetFingerprint, new SessionProcessedGeneration(80));

        Assert.Equal(SessionDataApplyResult.Ignored, result);
        Assert.Equal(session.ProcessedData, await repository.Repository.GetSessionRawPsstAsync(session.Id));
        Assert.Null(sessions.Get(session.Id));
        if (upload) Assert.Null(await repository.SwapRequests.GetRequestAsync(session.Id));
    }

    [Fact]
    public async Task Transfer_RejectsTargetWhoseCalibrationIsNoLongerCurrent()
    {
        var session = SeedSession();
        var inputs = (await repository.Repository.GetProcessingInputBundleAsync(session.Id))!;
        repository.Repository.GetProcessingInputBundleAsync(session.Id).Returns(inputs with
        {
            Bike = inputs.Bike with { HeadAngle = inputs.Bike.HeadAngle + 1 }
        });

        var result = await applier.ApplyDownloadedAsync(SessionSnapshot.From(session),
            new SessionBlobPayload(targetFingerprint, PersistenceTestData.CreateTelemetryBlob(80)),
            targetFingerprint, new SessionProcessedGeneration(80));

        Assert.Equal(SessionDataApplyResult.Ignored, result);
        Assert.Equal(session.ProcessedData, await repository.Repository.GetSessionRawPsstAsync(session.Id));
        Assert.Null(sessions.Get(session.Id));
    }

    private Session SeedSession(bool withData = true)
    {
        var session = new Session(Guid.NewGuid(), "ride", "notes", null, 100)
        {
            ProcessedData = withData ? PersistenceTestData.CreateTelemetryBlob(65) : null,
            ProcessingFingerprintJson = "held", ProcessedTelemetryRevision = withData ? 1 : 0,
            Updated = 12, DurationSeconds = 65, DistanceMeters = 10, AscentMeters = 4, DescentMeters = 2, GpsOffsetSeconds = 7
        };
        var setup = TestSnapshots.Setup();
        var bike = TestSnapshots.Bike(id: setup.BikeId);
        session.Setup = setup.Id;
        var inputs = new SessionProcessingInputBundle(
            new SessionProcessingInput(session.Id, setup.Id),
            new SetupProcessingInput(setup.Id, setup.BikeId, null, null),
            new BikeProcessingInput(bike.Id, bike.HeadAngle, bike.ForkStroke, bike.ShockStroke,
                RearSuspensionJsonCodec.Serialize(bike.RearSuspension)),
            RecordedSessionSourceSnapshot.From(PersistenceTestData.CreateRecordedSessionSource(session.Id)));
        targetFingerprint = AppJson.Serialize(new ProcessingFingerprintService().CreateCurrentDatabaseInputs(inputs));
        if (!withData) session.ProcessingFingerprintJson = targetFingerprint;
        repository.Repository.GetProcessingInputBundleAsync(session.Id).Returns(inputs);
        repository.Seed(session);
        return session;
    }

    public void Dispose()
    {
        sessions.Dispose();
        publication.Dispose();
    }
}
