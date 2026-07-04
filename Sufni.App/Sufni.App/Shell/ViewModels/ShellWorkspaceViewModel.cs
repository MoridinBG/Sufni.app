using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
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
    private TabPageViewModelBase? previousActiveTab;
    private bool isClosing;
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

        previousActiveTab = oldValue;
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

    public void CloseTab(TabPageViewModelBase tab, bool rememberForRestore = true)
    {
        isClosing = true;
        var closingTab = CurrentTab;

        Tabs.Remove(tab);
        if (rememberForRestore)
        {
            RemoveTabHistory<TabPageViewModelBase>(
                historyTab => ReferenceEquals(historyTab, tab),
                out _);
            tabHistory.Push(tab);
        }

        if (tab != previousActiveTab && tab == closingTab)
        {
            CurrentTab = previousActiveTab ?? (Tabs.Count == 0 ? null : Tabs[0]);
        }

        isClosing = false;
    }

    public bool GoBack()
    {
        if (CurrentTab is null)
        {
            return false;
        }

        CurrentTab = previousActiveTab is not null && Tabs.Contains(previousActiveTab)
            ? previousActiveTab
            : null;
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
}
