using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Video.Models;
namespace Sufni.App.Video.Stores;

public interface ISessionVideoStoreWriter : ISessionVideoStore
{
    /// <summary>
    /// Loads the clips of live sessions, observes file presence again, and
    /// replaces the current contents.
    /// </summary>
    Task ReplaceAllAsync(IReadOnlyList<SessionVideoSnapshot> snapshots);

    Task PublishClipsAsync(IReadOnlyCollection<SessionVideoClip> clips);
}
