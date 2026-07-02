using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.Telemetry;

using Sufni.App.Sessions.Detail.ViewModels.Editors;
namespace Sufni.App.Sessions.Pages.ViewModels.SessionPages;

public partial class DampingPageViewModel : PageViewModelBase
{
    public ISessionAnalysisWorkspace? AnalysisWorkspace { get; }
    public bool HasAnalysisData => AnalysisWorkspace?.TelemetryData is not null;
    public SurfacePresentationState FrontPresentationState => HasAnalysisData
        ? AnalysisWorkspace!.FrontAnalysisState
        : FrontDistributionState;
    public SurfacePresentationState RearPresentationState => HasAnalysisData
        ? AnalysisWorkspace!.RearAnalysisState
        : RearDistributionState;

    [ObservableProperty] public partial string? FrontVelocityDistribution { get; set; }
    [ObservableProperty] public partial string? RearVelocityDistribution { get; set; }
    [ObservableProperty] public partial SurfacePresentationState FrontDistributionState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState RearDistributionState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial double? FrontHscPercentage { get; set; }
    [ObservableProperty] public partial double? RearHscPercentage { get; set; }
    [ObservableProperty] public partial double? FrontLscPercentage { get; set; }
    [ObservableProperty] public partial double? RearLscPercentage { get; set; }
    [ObservableProperty] public partial double? FrontLsrPercentage { get; set; }
    [ObservableProperty] public partial double? RearLsrPercentage { get; set; }
    [ObservableProperty] public partial double? FrontHsrPercentage { get; set; }
    [ObservableProperty] public partial double? RearHsrPercentage { get; set; }

    public bool SampleAveragedModeSelected
    {
        get => AnalysisWorkspace?.SelectedVelocityAverageMode == VelocityAverageMode.SampleAveraged;
        set
        {
            if (value)
            {
                SelectVelocityAverageMode(VelocityAverageMode.SampleAveraged);
            }
        }
    }

    public bool StrokePeakAveragedModeSelected
    {
        get => AnalysisWorkspace?.SelectedVelocityAverageMode == VelocityAverageMode.StrokePeakAveraged;
        set
        {
            if (value)
            {
                SelectVelocityAverageMode(VelocityAverageMode.StrokePeakAveraged);
            }
        }
    }

    public DampingPageViewModel(ISessionAnalysisWorkspace? analysisWorkspace = null)
        : base("Damping")
    {
        AnalysisWorkspace = analysisWorkspace;
        if (analysisWorkspace is INotifyPropertyChanged observableWorkspace)
        {
            observableWorkspace.PropertyChanged += OnWorkspacePropertyChanged;
        }
    }

    public void ApplyDampingPercentages(SessionDampingPercentages percentages)
    {
        var front = percentages.ForSide(SuspensionType.Front);
        var rear = percentages.ForSide(SuspensionType.Rear);

        FrontHscPercentage = front.HscPercentage;
        RearHscPercentage = rear.HscPercentage;
        FrontLscPercentage = front.LscPercentage;
        RearLscPercentage = rear.LscPercentage;
        FrontLsrPercentage = front.LsrPercentage;
        RearLsrPercentage = rear.LsrPercentage;
        FrontHsrPercentage = front.HsrPercentage;
        RearHsrPercentage = rear.HsrPercentage;
    }

    public void ClearDampingPercentages()
    {
        ApplyDampingPercentages(SessionDampingPercentages.Empty);
    }

    partial void OnFrontDistributionStateChanged(SurfacePresentationState value)
    {
        OnPropertyChanged(nameof(FrontPresentationState));
    }

    partial void OnRearDistributionStateChanged(SurfacePresentationState value)
    {
        OnPropertyChanged(nameof(RearPresentationState));
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ISessionAnalysisWorkspace.TelemetryData))
        {
            OnPropertyChanged(nameof(HasAnalysisData));
            OnPropertyChanged(nameof(FrontPresentationState));
            OnPropertyChanged(nameof(RearPresentationState));
        }
        else if (args.PropertyName is nameof(ISessionAnalysisWorkspace.FrontAnalysisState))
        {
            OnPropertyChanged(nameof(FrontPresentationState));
        }
        else if (args.PropertyName is nameof(ISessionAnalysisWorkspace.RearAnalysisState))
        {
            OnPropertyChanged(nameof(RearPresentationState));
        }
        else if (args.PropertyName is nameof(ISessionAnalysisWorkspace.SelectedVelocityAverageMode))
        {
            RefreshVelocityAverageModeSelection();
        }
    }

    private void SelectVelocityAverageMode(VelocityAverageMode mode)
    {
        if (AnalysisWorkspace is null || AnalysisWorkspace.SelectedVelocityAverageMode == mode)
        {
            return;
        }

        AnalysisWorkspace.SelectedVelocityAverageMode = mode;
        RefreshVelocityAverageModeSelection();
    }

    private void RefreshVelocityAverageModeSelection()
    {
        OnPropertyChanged(nameof(SampleAveragedModeSelected));
        OnPropertyChanged(nameof(StrokePeakAveragedModeSelected));
    }
}
