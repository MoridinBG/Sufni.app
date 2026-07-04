using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Shared.Base;
namespace Sufni.App.Shell.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    #region Observable properties

    [ObservableProperty] public partial MainPagesViewModel MainPagesViewModel { get; set; }
    public ShellWorkspaceViewModel Workspace { get; }

    public TabPageViewModelBase? CurrentView
    {
        get => Workspace.CurrentTab;
        set => Workspace.CurrentTab = value;
    }

    public ObservableCollection<TabPageViewModelBase> Tabs => Workspace.Tabs;

    #endregion Observable properties

    #region Constructors

    public MainWindowViewModel(
        MainPagesViewModel mainPagesViewModel,
        WelcomeScreenViewModel welcomeScreenViewModel,
        IUiThreadDispatcher uiThreadDispatcher)
        : this(
            mainPagesViewModel,
            new ShellWorkspaceViewModel(uiThreadDispatcher),
            welcomeScreenViewModel,
            uiThreadDispatcher)
    {
    }

    public MainWindowViewModel(
        MainPagesViewModel mainPagesViewModel,
        ShellWorkspaceViewModel workspace,
        WelcomeScreenViewModel welcomeScreenViewModel,
        IUiThreadDispatcher uiThreadDispatcher)
        : base(uiThreadDispatcher)
    {
        MainPagesViewModel = mainPagesViewModel;
        Workspace = workspace;
        Workspace.PropertyChanged += OnWorkspacePropertyChanged;

        Workspace.OpenOrFocus(welcomeScreenViewModel);
    }

    #endregion Constructors

    #region Public methods

    public void OpenView(ViewModelBase view)
    {
        if (view is TabPageViewModelBase tabPage)
        {
            Workspace.OpenOrFocus(tabPage);
        }
    }

    public void AddView(ViewModelBase view)
    {
        if (view is TabPageViewModelBase tabPage)
        {
            Workspace.OpenInBackground(tabPage);
        }
    }

    public void CloseTabPage(TabPageViewModelBase tab, bool rememberForRestore = true)
        => Workspace.CloseTab(tab, rememberForRestore);

    public bool MoveTab(TabPageViewModelBase tab, TabPageViewModelBase targetTab, bool placeAfterTarget)
        => Workspace.MoveTab(tab, targetTab, placeAfterTarget);

    public void ForgetTabHistory<T>(Func<T, bool> match) where T : ViewModelBase
        => Workspace.ForgetTabHistory(match);

    public T? TakeTabHistory<T>(Func<T, bool> match) where T : ViewModelBase
        => Workspace.TakeTabHistory(match);

    #endregion Public methods

    #region Commands

    [RelayCommand]
    private void Restore()
    {
        Workspace.Restore();
    }

    [RelayCommand]
    private void SelectNextTab()
    {
        Workspace.SelectRelativeTab(1);
    }

    [RelayCommand]
    private void SelectPreviousTab()
    {
        Workspace.SelectRelativeTab(-1);
    }

    #endregion Commands

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShellWorkspaceViewModel.CurrentTab))
        {
            OnPropertyChanged(nameof(CurrentView));
        }
    }
}
