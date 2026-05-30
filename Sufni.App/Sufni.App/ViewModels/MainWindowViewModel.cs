using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Services;

namespace Sufni.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IMainWindowShellHost
{
    private readonly Stack<TabPageViewModelBase> tabHistory = new();
    private TabPageViewModelBase? previousActiveTab;
    private bool isClosing;
    private bool isReorderingTabs;

    #region Observable properties

    [ObservableProperty] private TabPageViewModelBase? currentView;
    [ObservableProperty] private MainPagesViewModel mainPagesViewModel;
    public ObservableCollection<TabPageViewModelBase> Tabs { get; set; } = [];

    // The shell host interface exposes Tabs as a plain enumerable to keep
    // the test surface narrow. The view model still publishes the
    // observable collection for binding.
    IEnumerable<TabPageViewModelBase> IMainWindowShellHost.Tabs => Tabs;

    #endregion Observable properties

    #region Property change handlers

    partial void OnCurrentViewChanged(TabPageViewModelBase? oldValue, TabPageViewModelBase? newValue)
    {
        if (isReorderingTabs)
        {
            return;
        }

        oldValue?.SetTabActive(false);
        newValue?.SetTabActive(true);

        if (isClosing) return;
        previousActiveTab = oldValue;
    }

    #endregion Property change handlers

    #region Constructors

    public MainWindowViewModel(
        MainPagesViewModel mainPagesViewModel,
        WelcomeScreenViewModel welcomeScreenViewModel,
        IUiThreadDispatcher uiThreadDispatcher)
        : base(uiThreadDispatcher)
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

    public void CloseTabPage(TabPageViewModelBase tab, bool rememberForRestore = true)
    {
        // Guard against setting previousActiveTab to the tab we are closing.
        isClosing = true;

        // We store the tab we are closing, because Tabs.Remove(tab) will change CurrentView
        var closingTab = CurrentView;

        Tabs.Remove(tab);
        if (rememberForRestore)
        {
            RemoveTabHistory<TabPageViewModelBase>(
                historyTab => ReferenceEquals(historyTab, tab),
                out _);
            tabHistory.Push(tab);
        }

        // We don't want to switch tabs when
        //   - closing a tab that's not currently the active one.
        //   - the previous active tab is the one we are closing.
        if (tab != previousActiveTab && tab == closingTab)
            CurrentView = previousActiveTab ?? (Tabs.Count == 0 ? null : Tabs[0]);

        isClosing = false;
    }

    public bool MoveTab(TabPageViewModelBase tab, TabPageViewModelBase targetTab, bool placeAfterTarget)
    {
        var currentIndex = Tabs.IndexOf(tab);
        var targetIndex = Tabs.IndexOf(targetTab);

        if (currentIndex < 0 || targetIndex < 0 || currentIndex == targetIndex)
        {
            return false;
        }

        var newIndex = placeAfterTarget
            ? targetIndex + (currentIndex > targetIndex ? 1 : 0)
            : targetIndex - (currentIndex < targetIndex ? 1 : 0);

        if (newIndex == currentIndex)
        {
            return false;
        }

        var selectedBeforeMove = CurrentView;
        isReorderingTabs = true;
        try
        {
            Tabs.Move(currentIndex, newIndex);
            CurrentView = selectedBeforeMove;
        }
        finally
        {
            isReorderingTabs = false;
        }

        return true;
    }

    private void SelectRelativeTab(int offset)
    {
        if (Tabs.Count <= 1 || CurrentView is null)
        {
            return;
        }

        var currentIndex = Tabs.IndexOf(CurrentView);
        if (currentIndex < 0)
        {
            return;
        }

        var nextIndex = (currentIndex + offset + Tabs.Count) % Tabs.Count;
        CurrentView = Tabs[nextIndex];
    }

    public void ForgetTabHistory<T>(Func<T, bool> match) where T : ViewModelBase
        => RemoveTabHistory(match, out _);

    public T? TakeTabHistory<T>(Func<T, bool> match) where T : ViewModelBase
    {
        RemoveTabHistory(match, out var tab);
        return tab;
    }

    private void RemoveTabHistory<T>(Func<T, bool> match, out T? mostRecentMatch)
        where T : ViewModelBase
    {
        mostRecentMatch = null;
        var retained = new List<TabPageViewModelBase>(tabHistory.Count);

        while (tabHistory.TryPop(out var tab))
        {
            if (tab is T typed && match(typed))
            {
                mostRecentMatch ??= typed;
                continue;
            }

            retained.Add(tab);
        }

        for (var i = retained.Count - 1; i >= 0; i--)
        {
            tabHistory.Push(retained[i]);
        }
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

    [RelayCommand]
    private void SelectNextTab()
    {
        SelectRelativeTab(1);
    }

    [RelayCommand]
    private void SelectPreviousTab()
    {
        SelectRelativeTab(-1);
    }

    #endregion Commands
}
