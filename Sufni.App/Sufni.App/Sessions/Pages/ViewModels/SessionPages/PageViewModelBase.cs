using CommunityToolkit.Mvvm.ComponentModel;

namespace Sufni.App.Sessions.Pages.ViewModels.SessionPages;

public partial class PageViewModelBase(string displayName) : ObservableObject
{
    public string DisplayName { get; } = displayName;
}
