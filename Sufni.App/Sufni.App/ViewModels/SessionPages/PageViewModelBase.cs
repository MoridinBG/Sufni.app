using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Sufni.App.ViewModels.SessionPages;

public partial class PageViewModelBase(string displayName) : ViewModelBase
{
    public string DisplayName { get; } = displayName;
    [ObservableProperty] private bool selected;
}
