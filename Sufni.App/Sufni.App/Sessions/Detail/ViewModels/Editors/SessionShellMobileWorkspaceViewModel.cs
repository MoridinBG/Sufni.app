using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.ExtensionHost.Contracts.Presentation;

using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Shared.Base;
namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal sealed class SessionShellMobileWorkspaceViewModel : ObservableObject, ISessionShellMobileWorkspace
{
    private readonly RecordedSessionContext context;

    public SessionShellMobileWorkspaceViewModel(TabPageViewModelBase editor, RecordedSessionContext context)
    {
        Editor = editor;
        this.context = context;
        context.PropertyChanged += OnContextPropertyChanged;
    }

    public TabPageViewModelBase Editor { get; }

    public ObservableCollection<PageViewModelBase> Pages => context.Pages;

    public int SelectedPageIndex
    {
        get => context.SelectedPageIndex;
        set => context.SelectedPageIndex = value;
    }

    public PageViewModelBase? SelectedPage => context.SelectedPage;

    public int PageCount => context.PageCount;

    public string SelectedPageDisplayName => context.SelectedPageDisplayName;

    public SessionScreenPresentationState ScreenState => context.ScreenState;

    public SessionOperationPresentationState SessionOperationState => context.SessionOperationState;

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(RecordedSessionContext.ScreenState))
        {
            OnPropertyChanged(nameof(ScreenState));
            return;
        }

        if (args.PropertyName is nameof(RecordedSessionContext.SessionOperationState))
        {
            OnPropertyChanged(nameof(SessionOperationState));
            return;
        }

        if (args.PropertyName is nameof(RecordedSessionContext.SelectedPageIndex))
        {
            OnPropertyChanged(nameof(SelectedPageIndex));
            return;
        }

        if (args.PropertyName is nameof(RecordedSessionContext.SelectedPage))
        {
            OnPropertyChanged(nameof(SelectedPage));
            return;
        }

        if (args.PropertyName is nameof(RecordedSessionContext.PageCount))
        {
            OnPropertyChanged(nameof(PageCount));
            return;
        }

        if (args.PropertyName is nameof(RecordedSessionContext.SelectedPageDisplayName))
        {
            OnPropertyChanged(nameof(SelectedPageDisplayName));
        }
    }
}
