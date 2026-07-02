using Sufni.App.ExtensionHost.Contracts.Presentation;
﻿using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.Telemetry;

using Sufni.App.Sessions.Detail.ViewModels.Editors;
namespace Sufni.App.Sessions.Pages.ViewModels.SessionPages;

public partial class BalancePageViewModel : PageViewModelBase
{
    public ISessionAnalysisWorkspace? AnalysisWorkspace { get; }
    public bool HasAnalysisData => AnalysisWorkspace?.TelemetryData is not null;
    public SurfacePresentationState CompressionPresentationState => HasAnalysisData
        ? AnalysisWorkspace!.CompressionBalanceState
        : CompressionBalanceState;
    public SurfacePresentationState ReboundPresentationState => HasAnalysisData
        ? AnalysisWorkspace!.ReboundBalanceState
        : ReboundBalanceState;

    [ObservableProperty] public partial string? CompressionBalance { get; set; }
    [ObservableProperty] public partial string? ReboundBalance { get; set; }
    [ObservableProperty] public partial SurfacePresentationState CompressionBalanceState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState ReboundBalanceState { get; set; } = SurfacePresentationState.Hidden;

    public bool ZenithModeSelected
    {
        get => AnalysisWorkspace?.SelectedBalanceDisplacementMode == BalanceDisplacementMode.Zenith;
        set
        {
            if (value)
            {
                SelectBalanceDisplacementMode(BalanceDisplacementMode.Zenith);
            }
        }
    }

    public bool TravelModeSelected
    {
        get => AnalysisWorkspace?.SelectedBalanceDisplacementMode == BalanceDisplacementMode.Travel;
        set
        {
            if (value)
            {
                SelectBalanceDisplacementMode(BalanceDisplacementMode.Travel);
            }
        }
    }

    public bool SpeedModeSelected
    {
        get => AnalysisWorkspace?.SelectedBalanceDisplacementMode == BalanceDisplacementMode.Speed;
        set
        {
            if (value)
            {
                SelectBalanceDisplacementMode(BalanceDisplacementMode.Speed);
            }
        }
    }

    public bool BothSpeedModeSelected
    {
        get => AnalysisWorkspace?.SelectedBalanceSpeedMode == BalanceSpeedMode.Both;
        set
        {
            if (value)
            {
                SelectBalanceSpeedMode(BalanceSpeedMode.Both);
            }
        }
    }

    public bool LowSpeedModeSelected
    {
        get => AnalysisWorkspace?.SelectedBalanceSpeedMode == BalanceSpeedMode.LowSpeed;
        set
        {
            if (value)
            {
                SelectBalanceSpeedMode(BalanceSpeedMode.LowSpeed);
            }
        }
    }

    public bool HighSpeedModeSelected
    {
        get => AnalysisWorkspace?.SelectedBalanceSpeedMode == BalanceSpeedMode.HighSpeed;
        set
        {
            if (value)
            {
                SelectBalanceSpeedMode(BalanceSpeedMode.HighSpeed);
            }
        }
    }

    public BalancePageViewModel(ISessionAnalysisWorkspace? analysisWorkspace = null)
        : base("Balance")
    {
        AnalysisWorkspace = analysisWorkspace;
        if (analysisWorkspace is INotifyPropertyChanged observableWorkspace)
        {
            observableWorkspace.PropertyChanged += OnWorkspacePropertyChanged;
        }
    }

    partial void OnCompressionBalanceStateChanged(SurfacePresentationState value)
    {
        OnPropertyChanged(nameof(CompressionPresentationState));
    }

    partial void OnReboundBalanceStateChanged(SurfacePresentationState value)
    {
        OnPropertyChanged(nameof(ReboundPresentationState));
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ISessionAnalysisWorkspace.TelemetryData))
        {
            OnPropertyChanged(nameof(HasAnalysisData));
            OnPropertyChanged(nameof(CompressionPresentationState));
            OnPropertyChanged(nameof(ReboundPresentationState));
        }
        else if (args.PropertyName is nameof(ISessionAnalysisWorkspace.CompressionBalanceState))
        {
            OnPropertyChanged(nameof(CompressionPresentationState));
        }
        else if (args.PropertyName is nameof(ISessionAnalysisWorkspace.ReboundBalanceState))
        {
            OnPropertyChanged(nameof(ReboundPresentationState));
        }
        else if (args.PropertyName is nameof(ISessionAnalysisWorkspace.SelectedBalanceDisplacementMode))
        {
            RefreshBalanceDisplacementModeSelection();
        }
        else if (args.PropertyName is nameof(ISessionAnalysisWorkspace.SelectedBalanceSpeedMode))
        {
            RefreshBalanceSpeedModeSelection();
        }
    }

    private void SelectBalanceDisplacementMode(BalanceDisplacementMode mode)
    {
        if (AnalysisWorkspace is null || AnalysisWorkspace.SelectedBalanceDisplacementMode == mode)
        {
            return;
        }

        AnalysisWorkspace.SelectedBalanceDisplacementMode = mode;
        RefreshBalanceDisplacementModeSelection();
    }

    private void SelectBalanceSpeedMode(BalanceSpeedMode mode)
    {
        if (AnalysisWorkspace is null || AnalysisWorkspace.SelectedBalanceSpeedMode == mode)
        {
            return;
        }

        AnalysisWorkspace.SelectedBalanceSpeedMode = mode;
        RefreshBalanceSpeedModeSelection();
    }

    private void RefreshBalanceDisplacementModeSelection()
    {
        OnPropertyChanged(nameof(ZenithModeSelected));
        OnPropertyChanged(nameof(TravelModeSelected));
        OnPropertyChanged(nameof(SpeedModeSelected));
    }

    private void RefreshBalanceSpeedModeSelection()
    {
        OnPropertyChanged(nameof(BothSpeedModeSelected));
        OnPropertyChanged(nameof(LowSpeedModeSelected));
        OnPropertyChanged(nameof(HighSpeedModeSelected));
    }
}
