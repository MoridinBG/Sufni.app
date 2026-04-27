using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Presentation;

namespace Sufni.App.ViewModels.SessionPages;

public partial class DamperPageViewModel() : PageViewModelBase("Damper")
{
    [ObservableProperty] private string? frontVelocityHistogram;
    [ObservableProperty] private string? rearVelocityHistogram;
    [ObservableProperty] private SurfacePresentationState frontHistogramState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState rearHistogramState = SurfacePresentationState.Hidden;
    [ObservableProperty] private double? frontHscPercentage;
    [ObservableProperty] private double? rearHscPercentage;
    [ObservableProperty] private double? frontLscPercentage;
    [ObservableProperty] private double? rearLscPercentage;
    [ObservableProperty] private double? frontLsrPercentage;
    [ObservableProperty] private double? rearLsrPercentage;
    [ObservableProperty] private double? frontHsrPercentage;
    [ObservableProperty] private double? rearHsrPercentage;
}
