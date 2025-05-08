using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Sufni.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty] private TabPageViewModelBase currentView;
    [ObservableProperty] private MainPagesViewModel? mainPagesViewModel;
    public ObservableCollection<TabPageViewModelBase> Tabs { get; set; } = [];

    private readonly WelcomeScreenViewModel welcomeScreenViewModel = new();

    public MainWindowViewModel()
    {
        mainPagesViewModel = App.Current?.Services?.GetService<MainPagesViewModel>();
        Debug.Assert(mainPagesViewModel != null, nameof(mainPagesViewModel) + " != null");

        Tabs.Add(welcomeScreenViewModel);
        CurrentView = welcomeScreenViewModel;
    }

    public void OpenView(ViewModelBase view)
    {
        var tabPage = view as TabPageViewModelBase;
        Debug.Assert(tabPage is not null, "tabPage is not null");

        if (!Tabs.Contains(tabPage))
        {
            Tabs.Add(tabPage);
        }
        CurrentView = tabPage;
    }

    public void CloseTabPage(TabPageViewModelBase tab)
    {
        Tabs.Remove(tab);
    }
}
