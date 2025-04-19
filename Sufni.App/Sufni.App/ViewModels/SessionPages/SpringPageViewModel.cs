using CommunityToolkit.Mvvm.ComponentModel;

namespace Sufni.App.ViewModels.SessionPages;

public partial class SpringPageViewModel() : PageViewModelBase("Spring")
{
    [ObservableProperty] private string? frontTravelHistogram;
    [ObservableProperty] private string? rearTravelHistogram;
}
