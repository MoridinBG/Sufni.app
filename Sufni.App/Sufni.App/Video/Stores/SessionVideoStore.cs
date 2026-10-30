using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Shared.Stores;
using Sufni.App.Shared.Base;

using Sufni.App.Video.Models;
using Sufni.App.Video.Services;
namespace Sufni.App.Video.Stores;

internal sealed class SessionVideoStore(
    ISessionVideoRepository repository,
    StorePublication publication)
    : SourceCacheStoreBase<SessionVideoSnapshot, Guid>(snapshot => snapshot.Id, publication), ISessionVideoStoreWriter
{
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clips = await repository.GetLiveAsync();
        cancellationToken.ThrowIfCancellationRequested();
        await ReplaceWithAsync(clips.Select(SessionVideoSnapshot.From));
    }

    public Task PublishClipsAsync(IReadOnlyCollection<SessionVideoClip> clips) =>
        PublishSnapshotsAsync(clips.Select(SessionVideoSnapshot.From));
}
