using Avalonia.Controls;

namespace Sufni.App.Views;

public interface IMobileNavigationPageHost
{
    void Attach(NavigationPage navigationPage);
    void Detach(NavigationPage navigationPage);
}
