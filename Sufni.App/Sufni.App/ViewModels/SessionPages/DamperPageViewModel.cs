using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Presentation;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels.SessionPages;

public partial class DamperPageViewModel : PageViewModelBase
{
    public ISessionStatisticsWorkspace? StatisticsWorkspace { get; }
    public bool HasDynamicStatistics => StatisticsWorkspace?.TelemetryData is not null;
    public SurfacePresentationState FrontPresentationState => HasDynamicStatistics
        ? StatisticsWorkspace!.FrontStatisticsState
        : FrontHistogramState;
    public SurfacePresentationState RearPresentationState => HasDynamicStatistics
        ? StatisticsWorkspace!.RearStatisticsState
        : RearHistogramState;

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

    public bool SampleAveragedModeSelected
    {
        get => StatisticsWorkspace?.SelectedVelocityAverageMode == VelocityAverageMode.SampleAveraged;
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
        get => StatisticsWorkspace?.SelectedVelocityAverageMode == VelocityAverageMode.StrokePeakAveraged;
        set
        {
            if (value)
            {
                SelectVelocityAverageMode(VelocityAverageMode.StrokePeakAveraged);
            }
        }
    }

    public DamperPageViewModel(ISessionStatisticsWorkspace? statisticsWorkspace = null)
        : base("Damper")
    {
        StatisticsWorkspace = statisticsWorkspace;
        if (statisticsWorkspace is INotifyPropertyChanged observableWorkspace)
        {
            observableWorkspace.PropertyChanged += OnWorkspacePropertyChanged;
        }
    }

    partial void OnFrontHistogramStateChanged(SurfacePresentationState value)
    {
        OnPropertyChanged(nameof(FrontPresentationState));
    }

    partial void OnRearHistogramStateChanged(SurfacePresentationState value)
    {
        OnPropertyChanged(nameof(RearPresentationState));
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ISessionStatisticsWorkspace.TelemetryData))
        {
            OnPropertyChanged(nameof(HasDynamicStatistics));
            OnPropertyChanged(nameof(FrontPresentationState));
            OnPropertyChanged(nameof(RearPresentationState));
        }
        else if (args.PropertyName is nameof(ISessionStatisticsWorkspace.FrontStatisticsState))
        {
            OnPropertyChanged(nameof(FrontPresentationState));
        }
        else if (args.PropertyName is nameof(ISessionStatisticsWorkspace.RearStatisticsState))
        {
            OnPropertyChanged(nameof(RearPresentationState));
        }
        else if (args.PropertyName is nameof(ISessionStatisticsWorkspace.SelectedVelocityAverageMode))
        {
            RefreshVelocityAverageModeSelection();
        }
    }

    private void SelectVelocityAverageMode(VelocityAverageMode mode)
    {
        if (StatisticsWorkspace is null || StatisticsWorkspace.SelectedVelocityAverageMode == mode)
        {
            return;
        }

        StatisticsWorkspace.SelectedVelocityAverageMode = mode;
        RefreshVelocityAverageModeSelection();
    }

    private void RefreshVelocityAverageModeSelection()
    {
        OnPropertyChanged(nameof(SampleAveragedModeSelected));
        OnPropertyChanged(nameof(StrokePeakAveragedModeSelected));
    }
}
