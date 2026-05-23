using System.Collections.Generic;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Services;

namespace Sufni.App.ViewModels;

public partial class MainViewModel : ViewModelBase, IMainViewShellHost
{
    private readonly Stack<ViewModelBase> viewHistory = new();

    #region Observable properties

    [ObservableProperty] private ViewModelBase currentView;
    public MainPagesViewModel MainPagesViewModel { get; }

    #endregion Observable properties

    #region Constructors

    public MainViewModel(MainPagesViewModel mainPagesViewModel, IUiThreadDispatcher uiThreadDispatcher)
        : base(uiThreadDispatcher)
    {
        MainPagesViewModel = mainPagesViewModel;
        CurrentView = mainPagesViewModel;
    }

    #endregion Constructors

    #region Public mthods

    public void OpenView(ViewModelBase view)
    {
        viewHistory.Push(CurrentView);
        CurrentView = view;
    }

    public void OpenPreviousView()
    {
        if (viewHistory.Count <= 0) return;
        CurrentView = viewHistory.Pop();
        Debug.Assert(CurrentView != null, nameof(CurrentView) + " != null");
    }

    #endregion Public mthods
}
