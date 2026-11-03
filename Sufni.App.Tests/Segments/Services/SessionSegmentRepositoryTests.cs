using Sufni.App.Segments.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.Segments.Services;

public class SessionSegmentRepositoryTests
{
    [Fact]
    public async Task GetLiveAsync_HidesOnlyDeletedSegments_AndRetainsAttachmentsAfterSessionDeletion()
    {
        using var tempDatabase = new TempDatabase();
        await using var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var repository = database.SessionSegmentRepository;
        var session = new Session(Guid.NewGuid(), "ride", string.Empty, null);
        var source = PersistenceTestData.CreateRecordedSessionSource(session.Id);
        await database.PutSessionWithRecordedSourceAsync(session, source);
        var kept = new SessionSegment { OwnerKey = source.SourceHash, Name = "kept", StartSeconds = 0, EndSeconds = 1 };
        var deleted = new SessionSegment { OwnerKey = source.SourceHash, Name = "deleted", StartSeconds = 0, EndSeconds = 1 };
        await repository.InsertAsync(kept);
        await repository.InsertAsync(deleted);
        await repository.DeleteAsync(deleted.Id);
        Assert.Equal(source.SourceHash, Assert.Single(await repository.GetLiveAsync()).OwnerKey);

        await database.SessionTransactions.DeleteSessionAsync(session.Id, TestContext.Current.CancellationToken);

        Assert.Null(await database.GetSessionAsync(session.Id));
        var retained = Assert.Single(await repository.GetLiveAsync());
        Assert.Equal(kept.Id, retained.Id);
        Assert.Equal(source.SourceHash, retained.OwnerKey);
    }

    [Fact]
    public async Task RenameAsync_ReturnsNull_WhenSegmentIsDeleted()
    {
        using var tempDatabase = new TempDatabase();
        var repository = new TestPersistenceHarness(tempDatabase.DatabasePath).SessionSegmentRepository;
        var segment = new SessionSegment { OwnerKey = "source-hash", Name = "climb", StartSeconds = 0, EndSeconds = 1 };
        await repository.InsertAsync(segment);
        await repository.DeleteAsync(segment.Id);

        var renamed = await repository.RenameAsync(segment.Id, "descent");

        Assert.Null(renamed);
    }
}
