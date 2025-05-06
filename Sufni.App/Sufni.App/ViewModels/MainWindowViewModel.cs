using System.Collections.Generic;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Sufni.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty] private ViewModelBase currentView;
    [ObservableProperty] private MainPagesViewModel mainPagesViewModel = new();

    private readonly Stack<ViewModelBase> viewHistory = new();
    private readonly WelcomeScreenViewModel welcomeScreenViewModel = new();

    public MainWindowViewModel()
    {
        CurrentView = welcomeScreenViewModel;
    }

    public void OpenView(ViewModelBase view)
    {
        viewHistory.Push(CurrentView);
        CurrentView = view;
    }

    public void OpenPreviousView()
    {
        CurrentView = viewHistory.Pop();
        Debug.Assert(CurrentView != null, nameof(CurrentView) + " != null");
    }
}
