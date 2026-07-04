using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Shared.Base;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal sealed class SessionShellMobileWorkspaceViewModel : ObservableObject, ISessionShellMobileWorkspace, IDisposable
{
    private readonly RecordedSessionEditorActions actions;
    private readonly IDisposable stateSubscription;
    private int selectedPageIndex;
    private SessionScreenPresentationState screenState = SessionScreenPresentationState.Ready;
    private SessionOperationPresentationState sessionOperationState = SessionOperationPresentationState.Hidden;

    public SessionShellMobileWorkspaceViewModel(
        TabPageViewModelBase editor,
        ObservableCollection<PageViewModelBase> pages,
        IObservable<RecordedSessionEditorState> state,
        RecordedSessionEditorActions actions)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(actions);

        Editor = editor;
        Pages = pages;
        this.actions = actions;
        Pages.CollectionChanged += OnPagesChanged;
        stateSubscription = state.Subscribe(ApplyState);
    }

    public TabPageViewModelBase Editor { get; }

    public ObservableCollection<PageViewModelBase> Pages { get; }

    public int SelectedPageIndex
    {
        get => selectedPageIndex;
        set => actions.SelectPageIndex(value);
    }

    public PageViewModelBase? SelectedPage => Pages.Count == 0
        ? null
        : Pages[Math.Clamp(SelectedPageIndex, 0, Pages.Count - 1)];

    public int PageCount => Pages.Count;

    public string SelectedPageDisplayName => SelectedPage?.DisplayName ?? string.Empty;

    public SessionScreenPresentationState ScreenState => screenState;

    public SessionOperationPresentationState SessionOperationState => sessionOperationState;

    public void Dispose()
    {
        Pages.CollectionChanged -= OnPagesChanged;
        stateSubscription.Dispose();
    }

    private void ApplyState(RecordedSessionEditorState state)
    {
        if (SetProperty(ref selectedPageIndex, state.Intent.SelectedPageIndex, nameof(SelectedPageIndex)))
        {
            OnPropertyChanged(nameof(SelectedPage));
            OnPropertyChanged(nameof(SelectedPageDisplayName));
        }

        SetProperty(ref screenState, state.Presentation.ScreenState, nameof(ScreenState));
        SetProperty(ref sessionOperationState, state.Presentation.OperationState, nameof(SessionOperationState));
    }

    private void OnPagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        OnPropertyChanged(nameof(SelectedPage));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(SelectedPageDisplayName));
    }
}
