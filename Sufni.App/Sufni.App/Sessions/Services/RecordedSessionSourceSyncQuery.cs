using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

namespace Sufni.App.Sessions.Services;

public interface IRecordedSessionSourceSyncQuery
{
    Task<IReadOnlyList<Guid>> GetSourceSyncTargetIdsAsync();
}

internal sealed class RecordedSessionSourceSyncQuery(
    IRecordedSessionSourceRepository recordedSessionSourceRepository,
    IRecordedSessionDerivationWindowProvider windowProvider) : IRecordedSessionSourceSyncQuery
{
    public async Task<IReadOnlyList<Guid>> GetSourceSyncTargetIdsAsync()
    {
        var missingSessionSourceIds = await recordedSessionSourceRepository.GetSessionIdsMissingRecordedSourceAsync();
        var windows = await windowProvider.GetWindowsAsync();
        var targetIds = missingSessionSourceIds
            .Select(sessionId =>
                windows.TryGetValue(sessionId, out var window)
                    ? window.SourceSessionId
                    : sessionId)
            .ToHashSet();

        var existingSourceIds = (await recordedSessionSourceRepository.GetSourceBackedSessionIdsAsync()).ToHashSet();
        foreach (var sourceId in await windowProvider.GetReferencedSourceSessionIdsAsync())
        {
            if (!existingSourceIds.Contains(sourceId))
            {
                targetIds.Add(sourceId);
            }
        }

        return targetIds.ToArray();
    }
}
