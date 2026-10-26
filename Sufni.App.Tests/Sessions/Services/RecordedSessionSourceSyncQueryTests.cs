using NSubstitute;
using Sufni.App.Sessions.Services;

namespace Sufni.App.Tests.Sessions.Services;

public class RecordedSessionSourceSyncQueryTests
{
    [Fact]
    public async Task GetSourceSyncTargetIdsAsync_ReturnsMissingRecordingSources()
    {
        var missingSourceId = Guid.NewGuid();
        var repository = Substitute.For<IRecordedSessionSourceRepository>();
        repository.GetMissingRecordingSourceIdsAsync().Returns([missingSourceId]);
        repository.GetSourceBackedSessionIdsAsync().Returns([]);
        repository.GetReferencedSourceSessionIdsAsync().Returns([]);
        var query = new RecordedSessionSourceSyncQuery(repository);

        var targetIds = await query.GetSourceSyncTargetIdsAsync();

        Assert.Equal([missingSourceId], targetIds);
    }

    [Fact]
    public async Task GetSourceSyncTargetIdsAsync_AddsReferencedSourcesThatAreNotHeld()
    {
        var heldSourceId = Guid.NewGuid();
        var absentSourceId = Guid.NewGuid();
        var repository = Substitute.For<IRecordedSessionSourceRepository>();
        repository.GetMissingRecordingSourceIdsAsync().Returns([]);
        repository.GetSourceBackedSessionIdsAsync().Returns([heldSourceId]);
        repository.GetReferencedSourceSessionIdsAsync().Returns([heldSourceId, absentSourceId]);
        var query = new RecordedSessionSourceSyncQuery(repository);

        var targetIds = await query.GetSourceSyncTargetIdsAsync();

        Assert.Equal([absentSourceId], targetIds);
    }
}
