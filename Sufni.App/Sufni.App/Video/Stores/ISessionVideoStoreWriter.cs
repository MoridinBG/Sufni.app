using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sufni.App.Video.Stores;

public interface ISessionVideoStoreWriter : ISessionVideoStore
{
    /// <summary>
    /// Replaces the published contents with prepared snapshots.
    /// </summary>
    Task ReplaceAllAsync(IReadOnlyList<SessionVideoSnapshot> snapshots);

    Task PublishClipsAsync(IReadOnlyCollection<SessionVideoSnapshot> snapshots);
}
