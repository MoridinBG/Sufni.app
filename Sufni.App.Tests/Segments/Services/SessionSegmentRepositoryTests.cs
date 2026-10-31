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
        var database = new TestPersistenceHarness(tempDatabase.DatabasePath);
        var repository = database.SessionSegmentRepository;
        var liveSession = new Session(Guid.NewGuid(), "live", string.Empty, null);
        var deletedSession = new Session(Guid.NewGuid(), "deleted", string.Empty, null);
        await database.PutSessionAsync(liveSession);
        await database.PutSessionAsync(deletedSession);
        var kept = new SessionSegment { OwnerKey = "source-hash", Name = "kept", StartSeconds = 0, EndSeconds = 1 };
        var deleted = new SessionSegment { OwnerKey = "source-hash", Name = "deleted", StartSeconds = 0, EndSeconds = 1 };
        await repository.InsertAsync(kept);
        await repository.InsertAsync(deleted);
        await repository.InsertAsync(new SessionSegment { OwnerKey = "other-source", Name = "hidden", StartSeconds = 0, EndSeconds = 1 });
        await repository.DeleteAsync(deleted.Id);
        await database.DeleteAsync(deletedSession);

        var live = await repository.GetLiveAsync();

        Assert.Equal(2, live.Count);
        Assert.Contains(live, segment => segment.Id == kept.Id);
        Assert.Contains(live, segment => segment.OwnerKey == "other-source");
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
