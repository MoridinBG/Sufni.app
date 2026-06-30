using Avalonia.Controls;

namespace Sufni.App.Shell.Views;

public interface IMobileNavigationPageHost
{
    void Attach(NavigationPage navigationPage);
    void Detach(NavigationPage navigationPage);
}
