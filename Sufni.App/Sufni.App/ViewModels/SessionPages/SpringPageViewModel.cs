using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Presentation;

namespace Sufni.App.ViewModels.SessionPages;

public partial class SpringPageViewModel() : PageViewModelBase("Spring")
{
    [ObservableProperty] private string? frontTravelHistogram;
    [ObservableProperty] private string? rearTravelHistogram;
    [ObservableProperty] private SurfacePresentationState frontHistogramState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState rearHistogramState = SurfacePresentationState.Hidden;
}
