using Sufni.App.ViewModels.Hosts;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels;

public interface ISessionSink : IItemDeletionHost
{
    void Add(SessionViewModel session);
}
