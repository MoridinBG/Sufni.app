using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Sufni.App.ViewModels;

public partial class WelcomeScreenViewModel : TabPageViewModelBase
{
    public WelcomeScreenViewModel()
    {
        Name = "Welcome";
    }

    [RelayCommand]
    private static void AddBike()
    {
        var mainPagesViewModel = App.Current?.Services?.GetService<MainPagesViewModel>();
        Debug.Assert(mainPagesViewModel is not null, "mainPagesViewModel is not null");

        mainPagesViewModel.BikesPage.AddCommand.Execute(null);
    }

    [RelayCommand]
    private static void AddSetup()
    {
        var mainPagesViewModel = App.Current?.Services?.GetService<MainPagesViewModel>();
        Debug.Assert(mainPagesViewModel is not null, "mainPagesViewModel is not null");

        mainPagesViewModel.SetupsPage.AddCommand.Execute(null);
    }

    [RelayCommand]
    private static void ImportSession()
    {
        var mainPagesViewModel = App.Current?.Services?.GetService<MainPagesViewModel>();
        Debug.Assert(mainPagesViewModel is not null, "mainPagesViewModel is not null");

        mainPagesViewModel.OpenPageCommand.Execute(mainPagesViewModel.ImportSessionsPage);
    }
}
