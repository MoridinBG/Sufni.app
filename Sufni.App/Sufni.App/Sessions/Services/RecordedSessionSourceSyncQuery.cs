using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sufni.App.Sessions.Services;

public interface IRecordedSessionSourceSyncQuery
{
    Task<IReadOnlyList<Guid>> GetSourceSyncTargetIdsAsync();
}

internal sealed class RecordedSessionSourceSyncQuery(
    IRecordedSessionSourceRepository recordedSessionSourceRepository) : IRecordedSessionSourceSyncQuery
{
    public async Task<IReadOnlyList<Guid>> GetSourceSyncTargetIdsAsync()
    {
        var targetIds = (await recordedSessionSourceRepository.GetMissingRecordingSourceIdsAsync()).ToHashSet();

        var existingSourceIds = (await recordedSessionSourceRepository.GetSourceBackedSessionIdsAsync()).ToHashSet();
        foreach (var sourceId in await recordedSessionSourceRepository.GetReferencedSourceSessionIdsAsync())
        {
            if (!existingSourceIds.Contains(sourceId))
            {
                targetIds.Add(sourceId);
            }
        }

        return targetIds.ToArray();
    }
}
