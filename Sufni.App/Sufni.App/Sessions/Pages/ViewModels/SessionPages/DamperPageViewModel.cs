using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
﻿using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.Telemetry;

using Sufni.App.Sessions.Detail.ViewModels.Editors;
namespace Sufni.App.Sessions.Pages.ViewModels.SessionPages;

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

    [ObservableProperty] public partial string? FrontVelocityHistogram { get; set; }
    [ObservableProperty] public partial string? RearVelocityHistogram { get; set; }
    [ObservableProperty] public partial SurfacePresentationState FrontHistogramState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState RearHistogramState { get; set; } = SurfacePresentationState.Hidden;
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

    public void ApplyDamperPercentages(SessionDamperPercentages percentages)
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

    public void ClearDamperPercentages()
    {
        ApplyDamperPercentages(SessionDamperPercentages.Empty);
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
