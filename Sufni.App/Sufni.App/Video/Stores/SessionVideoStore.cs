using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sufni.App.Shared.Stores;
using Sufni.App.Shared.Base;

using Sufni.App.Video.Models;
namespace Sufni.App.Video.Stores;

internal sealed class SessionVideoStore(
    StorePublication publication)
    : SourceCacheStoreBase<SessionVideoSnapshot, Guid>(snapshot => snapshot.Id, publication), ISessionVideoStoreWriter
{
    public Task PublishClipsAsync(IReadOnlyCollection<SessionVideoClip> clips) =>
        PublishSnapshotsAsync(clips.Select(SessionVideoSnapshot.From));
}
