using System.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sufni.App.Shared.Stores;
using Sufni.App.Shared.Base;

namespace Sufni.App.Video.Stores;

internal sealed class SessionVideoStore(
    StorePublication publication)
    : SourceCacheStoreBase<SessionVideoSnapshot, Guid>(snapshot => snapshot.Id, publication), ISessionVideoStoreWriter
{
    public IReadOnlyList<SessionVideoSnapshot> GetAll() => Items.ToArray();

    public Task PublishClipsAsync(IReadOnlyCollection<SessionVideoSnapshot> snapshots) =>
        PublishSnapshotsAsync(snapshots);
}
