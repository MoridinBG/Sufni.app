using System.Collections.Generic;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Sufni.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty] private ViewModelBase currentView;
    [ObservableProperty] private MainPagesViewModel? mainPagesViewModel;

    private readonly Stack<ViewModelBase> viewHistory = new();
    private readonly WelcomeScreenViewModel welcomeScreenViewModel = new();

    public MainWindowViewModel()
    {
        mainPagesViewModel = App.Current?.Services?.GetService<MainPagesViewModel>();
        Debug.Assert(mainPagesViewModel != null, nameof(mainPagesViewModel) + " != null");

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
