using Sufni.App.ExtensionHost.Contracts.Presentation;
﻿using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.Telemetry;

using Sufni.App.Sessions.Detail.ViewModels.Editors;
namespace Sufni.App.Sessions.Pages.ViewModels.SessionPages;

public partial class SpringPageViewModel : PageViewModelBase
{
    public ISessionStatisticsWorkspace? StatisticsWorkspace { get; }
    public bool HasDynamicStatistics => StatisticsWorkspace?.TelemetryData is not null;
    public SurfacePresentationState FrontPresentationState => HasDynamicStatistics
        ? StatisticsWorkspace!.FrontStatisticsState
        : FrontHistogramState;
    public SurfacePresentationState RearPresentationState => HasDynamicStatistics
        ? StatisticsWorkspace!.RearStatisticsState
        : RearHistogramState;

    [ObservableProperty] public partial string? FrontTravelHistogram { get; set; }
    [ObservableProperty] public partial string? RearTravelHistogram { get; set; }
    [ObservableProperty] public partial SurfacePresentationState FrontHistogramState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState RearHistogramState { get; set; } = SurfacePresentationState.Hidden;

    public bool ActiveSuspensionModeSelected
    {
        get => StatisticsWorkspace?.SelectedTravelHistogramMode == TravelHistogramMode.ActiveSuspension;
        set
        {
            if (value)
            {
                SelectTravelHistogramMode(TravelHistogramMode.ActiveSuspension);
            }
        }
    }

    public bool DynamicSagModeSelected
    {
        get => StatisticsWorkspace?.SelectedTravelHistogramMode == TravelHistogramMode.DynamicSag;
        set
        {
            if (value)
            {
                SelectTravelHistogramMode(TravelHistogramMode.DynamicSag);
            }
        }
    }

    public SpringPageViewModel(ISessionStatisticsWorkspace? statisticsWorkspace = null)
        : base("Spring")
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
        else if (args.PropertyName is nameof(ISessionStatisticsWorkspace.SelectedTravelHistogramMode))
        {
            RefreshTravelHistogramModeSelection();
        }
    }

    private void SelectTravelHistogramMode(TravelHistogramMode mode)
    {
        if (StatisticsWorkspace is null || StatisticsWorkspace.SelectedTravelHistogramMode == mode)
        {
            return;
        }

        StatisticsWorkspace.SelectedTravelHistogramMode = mode;
        RefreshTravelHistogramModeSelection();
    }

    private void RefreshTravelHistogramModeSelection()
    {
        OnPropertyChanged(nameof(ActiveSuspensionModeSelected));
        OnPropertyChanged(nameof(DynamicSagModeSelected));
    }
}
