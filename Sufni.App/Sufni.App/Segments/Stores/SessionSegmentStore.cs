using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Runtime.Stores;

using Sufni.App.Segments.Models;
using Sufni.App.Segments.Services;
namespace Sufni.App.Segments.Stores;

internal sealed class SessionSegmentStore(
    ISessionSegmentRepository repository,
    IUiThreadDispatcher uiThreadDispatcher)
    : SourceCacheStoreBase<SessionSegmentSnapshot, Guid>(snapshot => snapshot.Id, uiThreadDispatcher), ISessionSegmentStoreWriter
{
    public IReadOnlyList<SessionSegmentSnapshot> GetForSession(Guid sessionId) =>
        Items.Where(segment => segment.SessionId == sessionId).ToList();

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var segments = await repository.GetLiveAsync();
        cancellationToken.ThrowIfCancellationRequested();
        await ReplaceWithAsync(segments.Select(SessionSegmentSnapshot.From));
    }

    public async Task PublishSegmentsAsync(IReadOnlyCollection<SessionSegment> segments)
    {
        await PublishRemovalsAsync(segments.Where(segment => segment.Deleted is not null).Select(segment => segment.Id));
        await PublishSnapshotsAsync(segments.Where(segment => segment.Deleted is null).Select(SessionSegmentSnapshot.From));
    }
}
