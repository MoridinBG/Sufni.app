using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Sufni.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly Stack<TabPageViewModelBase> tabHistory = new();
    private TabPageViewModelBase? previousActiveTab;
    private bool isClosing;

    #region Observable properties

    [ObservableProperty] private TabPageViewModelBase? currentView;
    [ObservableProperty] private MainPagesViewModel mainPagesViewModel;
    public ObservableCollection<TabPageViewModelBase> Tabs { get; set; } = [];

    #endregion Observable properties

    #region Property change handlers

    partial void OnCurrentViewChanged(TabPageViewModelBase? oldValue, TabPageViewModelBase? newValue)
    {
        if (isClosing) return;
        previousActiveTab = oldValue;
    }

    #endregion Property change handlers

    #region Constructors

    public MainWindowViewModel(MainPagesViewModel mainPagesViewModel, WelcomeScreenViewModel welcomeScreenViewModel)
    {
        MainPagesViewModel = mainPagesViewModel;

        Tabs.Add(welcomeScreenViewModel);
        CurrentView = welcomeScreenViewModel;
    }

    #endregion Constructors

    #region Public methods

    public void OpenView(ViewModelBase view)
    {
        var tabPage = view as TabPageViewModelBase;
        Debug.Assert(tabPage is not null);

        if (!Tabs.Contains(tabPage))
        {
            Tabs.Add(tabPage);
        }
        CurrentView = tabPage;
    }

    public void CloseTabPage(TabPageViewModelBase tab)
    {
        // Guard against setting previousActiveTab to the tab we are closing.
        isClosing = true;

        // We store the tab we are closing, because Tabs.Remove(tab) will change CurrentView
        var closingTab = CurrentView;

        Tabs.Remove(tab);
        tabHistory.Push(tab);

        // We don't want to switch tabs when
        //   - closing a tab that's not currently the active one.
        //   - the previous active tab is the one we are closing.
        if (tab != previousActiveTab && tab == closingTab)
            CurrentView = previousActiveTab ?? (Tabs.Count == 0 ? null : Tabs[0]);

        isClosing = false;
    }

    #endregion Public methods
    
    #region Commands

    [RelayCommand]
    private void Restore()
    {
        tabHistory.TryPop(out var toRestore);
        if (toRestore is null) return;

        Tabs.Add(toRestore);
        CurrentView = toRestore;
    }
    
    #endregion Commands
}
