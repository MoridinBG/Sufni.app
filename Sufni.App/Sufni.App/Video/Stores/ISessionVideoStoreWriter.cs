using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Video.Models;
namespace Sufni.App.Video.Stores;

public interface ISessionVideoStoreWriter : ISessionVideoStore
{
    /// <summary>
    /// Loads every clip from the database, observes file presence again, and
    /// replaces the current contents.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    Task PublishClipsAsync(IReadOnlyCollection<SessionVideoClip> clips);
}
