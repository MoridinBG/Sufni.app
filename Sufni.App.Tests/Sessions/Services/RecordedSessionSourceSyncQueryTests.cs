using NSubstitute;
using Sufni.App.Sessions.Services;

namespace Sufni.App.Tests.Sessions.Services;

public class RecordedSessionSourceSyncQueryTests
{
    [Fact]
    public async Task GetSourceSyncTargetIdsAsync_AddsReferencedSourcesThatAreNotHeld()
    {
        var heldSourceId = Guid.NewGuid();
        var absentSourceId = Guid.NewGuid();
        var missingSourceId = Guid.NewGuid();
        var repository = Substitute.For<IRecordedSessionSourceRepository>();
        repository.GetMissingRecordingSourceIdsAsync().Returns([missingSourceId, absentSourceId]);
        repository.GetSourceBackedSessionIdsAsync().Returns([heldSourceId]);
        repository.GetReferencedSourceSessionIdsAsync().Returns([heldSourceId, absentSourceId]);
        var query = new RecordedSessionSourceSyncQuery(repository);

        var targetIds = await query.GetSourceSyncTargetIdsAsync();

        Assert.Equal(new[] { absentSourceId, missingSourceId }.Order(), targetIds.Order());
    }
}
