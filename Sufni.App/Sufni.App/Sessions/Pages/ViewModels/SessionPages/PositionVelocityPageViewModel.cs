using Sufni.App.Sessions.Presentation;
using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Shared.Common;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Plots;

namespace Sufni.App.Sessions.Pages.ViewModels.SessionPages;

public sealed record PositionVelocitySideModeOption(
    PositionVelocitySideMode Value,
    string DisplayName,
    string Description);

public sealed record PositionVelocityDisplayModeOption(
    PositionVelocityDisplayMode Value,
    string DisplayName,
    string Description);

/// <summary>
/// The position/velocity analysis surface of a recorded session. The selected side and display
/// mode are view state; the shown side follows which suspension sides have stroke data.
/// </summary>
public sealed partial class PositionVelocityPageViewModel : PageViewModelBase
{
    public ISessionAnalysisWorkspace Workspace { get; }

    public IReadOnlyList<PositionVelocitySideModeOption> SideModeOptions { get; } =
    [
        new(PositionVelocitySideMode.Front, "Front", "Show only front suspension data."),
        new(PositionVelocitySideMode.Rear, "Rear", "Show only rear suspension data."),
        new(PositionVelocitySideMode.Both, "Both", "Show front and rear suspension data together."),
    ];

    public IReadOnlyList<PositionVelocityDisplayModeOption> DisplayModeOptions { get; } =
    [
        new(
            PositionVelocityDisplayMode.Traces,
            "Traces",
            "Draw every stroke path so stroke envelopes and occasional excursions remain visible."),
        new(
            PositionVelocityDisplayMode.Heatmap,
            "Heatmap",
            "Bin stroke samples by wheel position and velocity to show where the suspension spent time."),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SideMode))]
    public partial PositionVelocitySideMode SelectedSideMode { get; set; } = PositionVelocitySideMode.Both;

    [ObservableProperty]
    public partial PositionVelocityDisplayMode SelectedDisplayMode { get; set; } = PositionVelocityDisplayMode.Traces;

    public SurfacePresentationState PresentationState
    {
        get
        {
            var front = Workspace.FrontAnalysisState;
            var rear = Workspace.RearAnalysisState;
            if (front.IsReady || rear.IsReady)
            {
                return SurfacePresentationState.Ready;
            }

            return front.ReservesLayout ? front : rear;
        }
    }

    public bool IsSideSelectorVisible => Workspace.FrontAnalysisState.IsReady && Workspace.RearAnalysisState.IsReady;

    public PositionVelocitySideMode SideMode =>
        (Workspace.FrontAnalysisState.IsReady, Workspace.RearAnalysisState.IsReady) switch
        {
            (true, false) => PositionVelocitySideMode.Front,
            (false, true) => PositionVelocitySideMode.Rear,
            _ => SelectedSideMode,
        };

    public PositionVelocityPageViewModel(ISessionAnalysisWorkspace workspace)
        : base("Position/velocity")
    {
        Workspace = workspace;
        if (workspace is INotifyPropertyChanged observableWorkspace)
        {
            observableWorkspace.PropertyChanged += OnWorkspacePropertyChanged;
        }
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ISessionAnalysisWorkspace.FrontAnalysisState)
            or nameof(ISessionAnalysisWorkspace.RearAnalysisState))
        {
            OnPropertyChanged(nameof(PresentationState));
            OnPropertyChanged(nameof(IsSideSelectorVisible));
            OnPropertyChanged(nameof(SideMode));
        }
    }
}
