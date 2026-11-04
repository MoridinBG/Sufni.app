using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Shared.Base;
using Sufni.App.Shell.ViewModels.Base;

namespace Sufni.App.LiveDaq.ViewModels.Editors;

public sealed class LiveSessionShellMobileWorkspaceViewModel : ObservableObject, ISessionShellMobileWorkspace, IDisposable
{
    private int selectedPageIndex;
    public TabPageViewModelBase Editor { get; }
    public ObservableCollection<PageViewModelBase> Pages { get; }
    public int SelectedPageIndex { get => selectedPageIndex; set => SetSelectedPageIndex(value); }
    public PageViewModelBase? SelectedPage => Pages.Count == 0 ? null : Pages[SelectedPageIndex];
    public int PageCount => Pages.Count;
    public string SelectedPageDisplayName => SelectedPage?.DisplayName ?? string.Empty;
    public SessionScreenPresentationState ScreenState => SessionScreenPresentationState.Ready;
    public SessionOperationPresentationState SessionOperationState => SessionOperationPresentationState.Hidden;

    public LiveSessionShellMobileWorkspaceViewModel(TabPageViewModelBase editor, ObservableCollection<PageViewModelBase> pages)
    {
        Editor = editor;
        Pages = pages;
        Pages.CollectionChanged += OnPagesChanged;
    }

    public void Dispose() => Pages.CollectionChanged -= OnPagesChanged;

    private void OnPagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        var clampedIndex = ClampSelectedPageIndex(selectedPageIndex);
        SetProperty(ref selectedPageIndex, clampedIndex, nameof(SelectedPageIndex));
        NotifySelectedPagePropertiesChanged();
    }

    private void SetSelectedPageIndex(int value)
    {
        var clampedIndex = ClampSelectedPageIndex(value);
        if (SetProperty(ref selectedPageIndex, clampedIndex, nameof(SelectedPageIndex)))
        {
            NotifySelectedPagePropertiesChanged();
        }
    }

    private int ClampSelectedPageIndex(int value)
    {
        if (Pages.Count == 0)
        {
            return 0;
        }

        if (value < 0)
        {
            return 0;
        }

        if (value >= Pages.Count)
        {
            return Pages.Count - 1;
        }

        return value;
    }

    private void NotifySelectedPagePropertiesChanged()
    {
        OnPropertyChanged(nameof(SelectedPage));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(SelectedPageDisplayName));
    }

}
