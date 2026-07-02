using Sufni.App.ExtensionHost.Contracts.Presentation;
﻿using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.Telemetry;

using Sufni.App.Sessions.Detail.ViewModels.Editors;
namespace Sufni.App.Sessions.Pages.ViewModels.SessionPages;

public partial class SpringPageViewModel : PageViewModelBase
{
    public ISessionAnalysisWorkspace? AnalysisWorkspace { get; }
    public bool HasAnalysisData => AnalysisWorkspace?.TelemetryData is not null;
    public SurfacePresentationState FrontPresentationState => HasAnalysisData
        ? AnalysisWorkspace!.FrontAnalysisState
        : FrontDistributionState;
    public SurfacePresentationState RearPresentationState => HasAnalysisData
        ? AnalysisWorkspace!.RearAnalysisState
        : RearDistributionState;

    [ObservableProperty] public partial string? FrontTravelDistribution { get; set; }
    [ObservableProperty] public partial string? RearTravelDistribution { get; set; }
    [ObservableProperty] public partial SurfacePresentationState FrontDistributionState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState RearDistributionState { get; set; } = SurfacePresentationState.Hidden;

    public bool ActiveSuspensionModeSelected
    {
        get => AnalysisWorkspace?.SelectedTravelDistributionMode == TravelDistributionMode.ActiveSuspension;
        set
        {
            if (value)
            {
                SelectTravelDistributionMode(TravelDistributionMode.ActiveSuspension);
            }
        }
    }

    public bool DynamicSagModeSelected
    {
        get => AnalysisWorkspace?.SelectedTravelDistributionMode == TravelDistributionMode.DynamicSag;
        set
        {
            if (value)
            {
                SelectTravelDistributionMode(TravelDistributionMode.DynamicSag);
            }
        }
    }

    public SpringPageViewModel(ISessionAnalysisWorkspace? analysisWorkspace = null)
        : base("Spring")
    {
        AnalysisWorkspace = analysisWorkspace;
        if (analysisWorkspace is INotifyPropertyChanged observableWorkspace)
        {
            observableWorkspace.PropertyChanged += OnWorkspacePropertyChanged;
        }
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
        else if (args.PropertyName is nameof(ISessionAnalysisWorkspace.SelectedTravelDistributionMode))
        {
            RefreshTravelDistributionModeSelection();
        }
    }

    private void SelectTravelDistributionMode(TravelDistributionMode mode)
    {
        if (AnalysisWorkspace is null || AnalysisWorkspace.SelectedTravelDistributionMode == mode)
        {
            return;
        }

        AnalysisWorkspace.SelectedTravelDistributionMode = mode;
        RefreshTravelDistributionModeSelection();
    }

    private void RefreshTravelDistributionModeSelection()
    {
        OnPropertyChanged(nameof(ActiveSuspensionModeSelected));
        OnPropertyChanged(nameof(DynamicSagModeSelected));
    }
}
