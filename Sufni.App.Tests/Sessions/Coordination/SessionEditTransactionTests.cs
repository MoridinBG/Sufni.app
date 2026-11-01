using SQLite;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Segments.Models;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Tests.TestSupport.Persistence;
using Sufni.App.Video.Models;

namespace Sufni.App.Tests.Sessions.Coordination;

public class SessionEditTransactionTests : IDisposable
{
    private readonly TempDatabase tempDatabase = new("session-edit-transaction.db");
    private readonly SqliteConnectionContext context;
    private readonly SessionPersistenceTransactionRunner runner;

    public SessionEditTransactionTests()
    {
        context = PersistenceTestData.CreateConnectionContext(tempDatabase.DatabasePath);
        runner = new SessionPersistenceTransactionRunner(context, PersistenceTestData.CreateTimeProvider(), PersistenceTestData.CreateDeviceIdentity());
    }

    public void Dispose() => tempDatabase.Dispose();

    [Fact]
    public async Task TrimUntilEndAsync_EndsWindowAtClick_AndKeepsUndoWindow()
    {
        var session = await InsertSessionAsync();

        var commit = await runner.TrimUntilEndAsync(session, 4);

        var committed = Assert.IsType<SessionEditCommit.Committed>(commit);
        Assert.Equal([session.Id], committed.SessionIds);
        var trimmed = await GetSnapshotAsync(session.Id);
        Assert.Equal(new RecordedSessionDerivationWindow(session.Id, 0, 4), trimmed.DerivationWindow);
        Assert.True(trimmed.CanUndoTrim);
        Assert.Equal(100, trimmed.Timestamp);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1.25)]
    public async Task TrimFromBeginningAsync_MovesWindowStartAndOrigin_WithoutChangingAuthoredAlignment(double alignment)
    {
        var session = await InsertSessionAsync(alignment);

        await runner.TrimFromBeginningAsync(session, 2.5);

        var trimmed = await GetSnapshotAsync(session.Id);
        Assert.Equal(new RecordedSessionDerivationWindow(session.Id, 2.5, null), trimmed.DerivationWindow);
        Assert.Equal(102, trimmed.Timestamp);
        Assert.Equal(alignment, trimmed.GpsOffsetSeconds, precision: 6);
    }

    [Fact]
    public async Task TrimFromBeginningAsync_LeavesSourceVideoAlignmentUnchanged()
    {
        var session = await InsertSessionAsync();
        var clip = await InsertClipAsync( alignmentOffsetSeconds: 1);

        var commit = await runner.TrimFromBeginningAsync(session, 2.5);

        Assert.IsType<SessionEditCommit.Committed>(commit);
        Assert.Equal(1, (await GetClipAsync(clip.Id)).AlignmentOffsetSeconds, precision: 6);
    }

    [Fact]
    public async Task UndoTrimAsync_AfterTrimOfFullRange_RestoresSessionWithoutWindow()
    {
        var session = await InsertSessionAsync();
        await runner.TrimFromBeginningAsync(session, 2.5);

        await runner.UndoTrimAsync(await GetSnapshotAsync(session.Id));

        var restored = await GetSnapshotAsync(session.Id);
        Assert.Null(restored.DerivationWindow);
        Assert.False(restored.CanUndoTrim);
        Assert.Equal(100, restored.Timestamp);
        Assert.Equal(0, restored.GpsOffsetSeconds, precision: 6);
    }

    [Fact]
    public async Task UndoTrimAsync_WithoutTrim_IsRejected()
    {
        var session = await InsertSessionAsync();

        var commit = await runner.UndoTrimAsync(session);

        Assert.Equal("Session has no trim to undo.", Assert.IsType<SessionEditCommit.Rejected>(commit).Message);
    }

    [Fact]
    public async Task SplitAsync_CreatesSecondPartOverRestOfWindow()
    {
        var session = await InsertSessionAsync();

        var commit = await runner.SplitAsync(session, 3);

        var committed = Assert.IsType<SessionEditCommit.Committed>(commit);
        var part2Id = Assert.IsType<Guid>(committed.CreatedSessionId);
        Assert.Equal([session.Id, part2Id], committed.SessionIds);
        var part1 = await GetSnapshotAsync(session.Id);
        var part2 = await GetSnapshotAsync(part2Id);
        Assert.Equal("ride (1)", part1.Name);
        Assert.Equal(new RecordedSessionDerivationWindow(session.Id, 0, 3), part1.DerivationWindow);
        Assert.False(part1.CanUndoTrim);
        Assert.Equal("ride (2)", part2.Name);
        Assert.Equal(new RecordedSessionDerivationWindow(session.Id, 3, null), part2.DerivationWindow);
        Assert.Equal(session.SetupId, part2.SetupId);
        Assert.Equal(103, part2.Timestamp);
    }

    [Fact]
    public async Task SplitAsync_KeepsOneSourceOwnedVideoLink()
    {
        var session = await InsertSessionAsync();
        var clip = await InsertClipAsync( alignmentOffsetSeconds: 1);

        var commit = await runner.SplitAsync(session, 3.25);

        var committed = Assert.IsType<SessionEditCommit.Committed>(commit);
        Assert.NotNull(committed.CreatedSessionId);
        Assert.Single(await (await context.GetInitializedConnectionAsync()).Table<SessionVideoClip>().ToListAsync());
        Assert.Equal(1, (await GetClipAsync(clip.Id)).AlignmentOffsetSeconds, precision: 6);
    }

    [Fact]
    public async Task TrimAndUndo_LeaveSourceSegmentAnchorsUnchanged()
    {
        var session = await InsertSessionAsync();
        var segment = await InsertSegmentAsync( 1, 4);

        var commit = await runner.TrimFromBeginningAsync(session, 2.5);
        var shifted = await GetSegmentAsync(segment.Id);
        await runner.UndoTrimAsync(await GetSnapshotAsync(session.Id));
        var restored = await GetSegmentAsync(segment.Id);

        Assert.IsType<SessionEditCommit.Committed>(commit);
        Assert.Equal((1.0, 4.0), (shifted.StartSeconds, shifted.EndSeconds));
        Assert.Equal((1.0, 4.0), (restored.StartSeconds, restored.EndSeconds));
    }

    [Fact]
    public async Task SplitAsync_LeavesSourceSegmentAnchorsAndOwnershipUnchanged()
    {
        var session = await InsertSessionAsync();
        var early = await InsertSegmentAsync( 1, 4);
        var late = await InsertSegmentAsync( 3, 6);

        var commit = await runner.SplitAsync(session, 3);

        var part2Id = Assert.IsType<SessionEditCommit.Committed>(commit).CreatedSessionId;
        var kept = await GetSegmentAsync(early.Id);
        var moved = await GetSegmentAsync(late.Id);
        Assert.Equal(("source-hash", 1.0, 4.0), (kept.OwnerKey, kept.StartSeconds, kept.EndSeconds));
        Assert.Equal(("source-hash", 3.0, 6.0), (moved.OwnerKey, moved.StartSeconds, moved.EndSeconds));
    }

    [Fact]
    public async Task SplitAsync_OutsideWindow_IsRejected()
    {
        var session = await InsertSessionAsync();
        await runner.TrimUntilEndAsync(session, 4);

        var commit = await runner.SplitAsync(await GetSnapshotAsync(session.Id), 5);

        Assert.Equal("The position is outside the session.", Assert.IsType<SessionEditCommit.Rejected>(commit).Message);
    }

    [Fact]
    public async Task Edit_WhenWindowChangedSinceSnapshot_IsRejectedWithoutChanges()
    {
        var session = await InsertSessionAsync();
        await runner.TrimUntilEndAsync(session, 4);

        var commit = await runner.TrimFromBeginningAsync(session, 1);

        Assert.Equal("Session changed while editing.", Assert.IsType<SessionEditCommit.Rejected>(commit).Message);
        Assert.Equal(
            new RecordedSessionDerivationWindow(session.Id, 0, 4),
            (await GetSnapshotAsync(session.Id)).DerivationWindow);
    }

    [Fact]
    public async Task DeleteSessionAsync_KeepsSourceReferencedByAnotherSession()
    {
        var session = await InsertSessionAsync();
        var sources = new RecordedSessionSourceRepository(context);
        await sources.PutRecordedSessionSourceAsync(PersistenceTestData.CreateRecordedSessionSource(session.Id));
        await runner.SplitAsync(session, 3);

        var sourceDeleted = await runner.DeleteSessionAsync(session.Id);

        Assert.False(sourceDeleted.SourceDeleted);
        Assert.NotNull(await sources.GetRecordedSessionSourceAsync(session.Id));
    }

    private async Task<SessionSnapshot> InsertSessionAsync(double alignment = 0)
    {
        var session = new Session(Guid.NewGuid(), "ride", "desc", Guid.NewGuid(), 100) { GpsOffsetSeconds = alignment };
        await (await context.GetInitializedConnectionAsync()).InsertAsync(session);
        return SessionSnapshot.From(session);
    }

    private async Task<SessionSnapshot> GetSnapshotAsync(Guid sessionId) =>
        SessionSnapshot.From(await (await context.GetInitializedConnectionAsync()).GetAsync<Session>(sessionId));

    private async Task<SessionSegment> GetSegmentAsync(Guid segmentId) =>
        await (await context.GetInitializedConnectionAsync()).GetAsync<SessionSegment>(segmentId);

    private async Task<SessionSegment> InsertSegmentAsync(double startSeconds, double endSeconds)
    {
        var segment = new SessionSegment
        {
            OwnerKey = "source-hash",
            Name = $"{startSeconds}-{endSeconds}",
            StartSeconds = startSeconds,
            EndSeconds = endSeconds,
        };
        await (await context.GetInitializedConnectionAsync()).InsertAsync(segment);
        return segment;
    }

    private async Task<SessionVideoClip> GetClipAsync(Guid clipId) =>
        await (await context.GetInitializedConnectionAsync()).GetAsync<SessionVideoClip>(clipId);

    private async Task<SessionVideoClip> InsertClipAsync(double alignmentOffsetSeconds)
    {
        var clip = new SessionVideoClip
        {
            Id = Guid.NewGuid(),
            OwnerKey = "source-hash",
            Path = "/videos/ride.mp4",
            StartTimestamp = 100,
            DurationSeconds = 10,
            AlignmentOffsetSeconds = alignmentOffsetSeconds,
            Added = 1,
        };
        await (await context.GetInitializedConnectionAsync()).InsertAsync(clip);
        return clip;
    }
}
