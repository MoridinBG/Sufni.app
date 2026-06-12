using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.Presentation;
using Sufni.App.ViewModels.SessionPages;

namespace Sufni.App.ViewModels.Editors;

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
        }
    }
}
