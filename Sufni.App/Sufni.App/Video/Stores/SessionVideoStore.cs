using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Base;

using Sufni.App.Video.Models;
using Sufni.App.Video.Services;
namespace Sufni.App.Video.Stores;

internal sealed class SessionVideoStore(
    ISessionVideoRepository repository,
    IUiThreadDispatcher uiThreadDispatcher)
    : SourceCacheStoreBase<SessionVideoSnapshot, Guid>(snapshot => snapshot.Id, uiThreadDispatcher), ISessionVideoStoreWriter
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
