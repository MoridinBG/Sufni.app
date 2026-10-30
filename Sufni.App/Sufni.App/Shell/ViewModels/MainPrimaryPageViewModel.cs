
using Sufni.App.Shared.Base;
namespace Sufni.App.Shell.ViewModels;

public sealed class MainPrimaryPageViewModel
{
    public MainPrimaryPageViewModel(
        MainPrimaryPageRole role,
        string header,
        string iconPath,
        ViewModelBase content)
    {
        Role = role;
        Header = header;
        IconPath = iconPath;
        Content = content;
    }

    public MainPrimaryPageRole Role { get; }
    public string Header { get; }
    public string IconPath { get; }
    public ViewModelBase Content { get; }

}
