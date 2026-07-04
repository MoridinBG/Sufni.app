using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Shared.Base;

namespace Sufni.App.Shell.ViewModels;

public interface IShellWorkspaceHost
{
    ObservableCollection<TabPageViewModelBase> Tabs { get; }
    TabPageViewModelBase? CurrentTab { get; set; }
    void OpenOrFocus(TabPageViewModelBase page);
    void OpenInBackground(TabPageViewModelBase page);
    bool CloseIfOpen(Func<TabPageViewModelBase, bool> predicate, bool rememberForRestore = true);
    Task<bool> CloseCurrentAsync();
    bool GoBack();
    bool MoveTab(int fromIndex, int toIndex);
}

public partial class ShellWorkspaceViewModel : ViewModelBase, IShellWorkspaceHost
{
    private readonly Stack<TabPageViewModelBase> tabHistory = new();
    private readonly Stack<TabPageViewModelBase> focusHistory = new();
    private bool isClosing;
    private bool isNavigatingHistory;
    private bool isReorderingTabs;

    [ObservableProperty] public partial TabPageViewModelBase? CurrentTab { get; set; }

    public ObservableCollection<TabPageViewModelBase> Tabs { get; } = [];

    public ShellWorkspaceViewModel(IUiThreadDispatcher uiThreadDispatcher)
        : base(uiThreadDispatcher)
    {
    }

    partial void OnCurrentTabChanged(TabPageViewModelBase? oldValue, TabPageViewModelBase? newValue)
    {
        if (isReorderingTabs)
        {
            return;
        }

        oldValue?.SetTabActive(false);
        newValue?.SetTabActive(true);

        if (isClosing)
        {
            return;
        }

        if (!isNavigatingHistory && oldValue is not null)
        {
            focusHistory.Push(oldValue);
        }
    }

    public void OpenOrFocus(TabPageViewModelBase page)
    {
        if (!Tabs.Contains(page))
        {
            Tabs.Add(page);
        }

        CurrentTab = page;
    }

    public void OpenInBackground(TabPageViewModelBase page)
    {
        if (!Tabs.Contains(page))
        {
            Tabs.Add(page);
        }
    }

    public bool CloseIfOpen(Func<TabPageViewModelBase, bool> predicate, bool rememberForRestore = true)
    {
        var tab = Tabs.FirstOrDefault(predicate);
        if (tab is null)
        {
            return false;
        }

        CloseTab(tab, rememberForRestore);
        return true;
    }

    public async Task<bool> CloseCurrentAsync()
    {
        if (CurrentTab is null)
        {
            return false;
        }

        await CurrentTab.CloseCommand.ExecuteAsync(null);
        return true;
    }

    public async Task<bool> CloseBackgroundTabsAsync()
    {
        var currentTab = CurrentTab;
        foreach (var tab in Tabs.ToArray())
        {
            if (ReferenceEquals(tab, currentTab))
            {
                continue;
            }

            if (!await tab.TryPrepareCloseAsync())
            {
                return false;
            }

            if (Tabs.Contains(tab))
            {
                CloseTab(tab, rememberForRestore: false);
            }
        }

        if (currentTab is not null && Tabs.Contains(currentTab))
        {
            CurrentTab = currentTab;
        }

        return true;
    }

    public void CloseTab(TabPageViewModelBase tab, bool rememberForRestore = true)
    {
        isClosing = true;
        var closingTab = CurrentTab;

        if (!Tabs.Remove(tab))
        {
            isClosing = false;
            return;
        }

        RemoveFocusHistory(tab);

        if (rememberForRestore)
        {
            RemoveTabHistory<TabPageViewModelBase>(
                historyTab => ReferenceEquals(historyTab, tab),
                out _);
            tabHistory.Push(tab);
        }

        if (tab == closingTab)
        {
            CurrentTab = TryPopPreviousOpenTab(out var previousTab)
                ? previousTab
                : Tabs.Count == 0 ? null : Tabs[0];
        }

        isClosing = false;
    }

    public bool GoBack()
    {
        if (CurrentTab is null)
        {
            return false;
        }

        isNavigatingHistory = true;
        try
        {
            CurrentTab = TryPopPreviousOpenTab(out var previousTab)
                ? previousTab
                : null;
        }
        finally
        {
            isNavigatingHistory = false;
        }

        return true;
    }

    public bool MoveTab(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 ||
            fromIndex >= Tabs.Count ||
            toIndex < 0 ||
            toIndex >= Tabs.Count ||
            fromIndex == toIndex)
        {
            return false;
        }

        var selectedBeforeMove = CurrentTab;
        isReorderingTabs = true;
        try
        {
            Tabs.Move(fromIndex, toIndex);
            CurrentTab = selectedBeforeMove;
        }
        finally
        {
            isReorderingTabs = false;
        }

        return true;
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

        return MoveTab(currentIndex, newIndex);
    }

    public void SelectRelativeTab(int offset)
    {
        if (Tabs.Count <= 1 || CurrentTab is null)
        {
            return;
        }

        var currentIndex = Tabs.IndexOf(CurrentTab);
        if (currentIndex < 0)
        {
            return;
        }

        var nextIndex = (currentIndex + offset + Tabs.Count) % Tabs.Count;
        CurrentTab = Tabs[nextIndex];
    }

    [RelayCommand]
    public void Restore()
    {
        tabHistory.TryPop(out var toRestore);
        if (toRestore is null)
        {
            return;
        }

        Tabs.Add(toRestore);
        CurrentTab = toRestore;
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

    private bool TryPopPreviousOpenTab(out TabPageViewModelBase? previousTab)
    {
        while (focusHistory.TryPop(out var candidate))
        {
            if (Tabs.Contains(candidate) && !ReferenceEquals(candidate, CurrentTab))
            {
                previousTab = candidate;
                return true;
            }
        }

        previousTab = null;
        return false;
    }

    private void RemoveFocusHistory(TabPageViewModelBase tab)
    {
        if (focusHistory.Count == 0)
        {
            return;
        }

        var retained = focusHistory
            .Where(historyTab => !ReferenceEquals(historyTab, tab) && Tabs.Contains(historyTab))
            .Reverse()
            .ToArray();
        focusHistory.Clear();
        foreach (var historyTab in retained)
        {
            focusHistory.Push(historyTab);
        }
    }
}
