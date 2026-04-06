using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels;

public interface ISessionSink
{
    void Add(SessionViewModel session);
}
