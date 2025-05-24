using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Sufni.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly WelcomeScreenViewModel welcomeScreenViewModel = new();

    #region Observable properties

    [ObservableProperty] private TabPageViewModelBase currentView;
    [ObservableProperty] private MainPagesViewModel? mainPagesViewModel;
    public ObservableCollection<TabPageViewModelBase> Tabs { get; set; } = [];

    #endregion Observable properties

    #region Constructors

    public MainWindowViewModel()
    {
        mainPagesViewModel = App.Current?.Services?.GetService<MainPagesViewModel>();
        Debug.Assert(mainPagesViewModel != null, nameof(mainPagesViewModel) + " != null");

        Tabs.Add(welcomeScreenViewModel);
        CurrentView = welcomeScreenViewModel;
    }

    #endregion Constructors

    #region Public methods

    public void OpenView(ViewModelBase view)
    {
        var tabPage = view as TabPageViewModelBase;
        Debug.Assert(tabPage is not null);

        if (!Tabs.Contains(tabPage))
        {
            Tabs.Add(tabPage);
        }
        CurrentView = tabPage;
    }

    public void CloseTabPage(TabPageViewModelBase tab)
    {
        Tabs.Remove(tab);
    }

    #endregion Public methods
}
