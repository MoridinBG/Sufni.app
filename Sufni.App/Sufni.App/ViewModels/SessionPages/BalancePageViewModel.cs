using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Presentation;

namespace Sufni.App.ViewModels.SessionPages;

public partial class BalancePageViewModel() : PageViewModelBase("Balance")
{
    [ObservableProperty] private string? compressionBalance;
    [ObservableProperty] private string? reboundBalance;
    [ObservableProperty] private SurfacePresentationState compressionBalanceState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState reboundBalanceState = SurfacePresentationState.Hidden;
}
