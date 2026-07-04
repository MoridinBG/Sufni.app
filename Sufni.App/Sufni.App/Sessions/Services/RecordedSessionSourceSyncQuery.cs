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
        var missingSessionSourceIds =
            (IEnumerable<Guid>?)await recordedSessionSourceRepository.GetSessionIdsMissingRecordedSourceAsync()
            ?? Array.Empty<Guid>();
        var windows =
            await windowProvider.GetWindowsAsync()
            ?? new Dictionary<Guid, RecordedSessionDerivationWindow>();
        var targetIds = missingSessionSourceIds
            .Select(sessionId =>
                windows.TryGetValue(sessionId, out var window)
                    ? window.SourceSessionId
                    : sessionId)
            .ToHashSet();

        var existingSourceIds =
            (((IEnumerable<Guid>?)await recordedSessionSourceRepository.GetSourceBackedSessionIdsAsync())
             ?? Array.Empty<Guid>())
            .ToHashSet();
        var referencedSourceIds =
            (((IEnumerable<Guid>?)await windowProvider.GetReferencedSourceSessionIdsAsync())
             ?? Array.Empty<Guid>())
            .ToHashSet();
        var persistedDerivationSourceIds =
            (IEnumerable<Guid>?)await recordedSessionSourceRepository.GetPersistedDerivationSourceSessionIdsAsync()
            ?? Array.Empty<Guid>();
        referencedSourceIds.UnionWith(persistedDerivationSourceIds);
        foreach (var sourceId in referencedSourceIds)
        {
            if (!existingSourceIds.Contains(sourceId))
            {
                targetIds.Add(sourceId);
            }
        }

        return targetIds.ToArray();
    }
}
