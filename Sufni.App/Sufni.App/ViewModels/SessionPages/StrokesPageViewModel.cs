using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels.SessionPages;

public sealed record SuspensionSideOption(SuspensionType Value, string DisplayName);

public sealed partial class StrokesPageViewModel : PageViewModelBase
{
    public ISessionStatisticsWorkspace Workspace { get; }

    public IReadOnlyList<SuspensionSideOption> SuspensionSideOptions { get; } =
    [
        new(SuspensionType.Front, "Front"),
        new(SuspensionType.Rear, "Rear"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBothStatisticsSides))]
    private bool frontSelectionAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBothStatisticsSides))]
    private bool rearSelectionAvailable;

    [ObservableProperty] private SuspensionType selectedSuspensionType = SuspensionType.Front;

    public bool ShowFrontStrokes => SelectedSuspensionType == SuspensionType.Front;
    public bool ShowRearStrokes => SelectedSuspensionType == SuspensionType.Rear;
    public bool HasBothStatisticsSides => FrontSelectionAvailable && RearSelectionAvailable;

    public StrokesPageViewModel(ISessionStatisticsWorkspace workspace)
        : base("Strokes")
    {
        Workspace = workspace;
        RefreshAvailableSides();

        if (workspace is INotifyPropertyChanged observableWorkspace)
        {
            observableWorkspace.PropertyChanged += OnWorkspacePropertyChanged;
        }
    }

    partial void OnSelectedSuspensionTypeChanged(SuspensionType value)
    {
        OnPropertyChanged(nameof(ShowFrontStrokes));
        OnPropertyChanged(nameof(ShowRearStrokes));
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ISessionStatisticsWorkspace.FrontStatisticsState)
            or nameof(ISessionStatisticsWorkspace.RearStatisticsState))
        {
            RefreshAvailableSides();
        }
    }

    private void RefreshAvailableSides()
    {
        FrontSelectionAvailable = Workspace.FrontStatisticsState.ReservesLayout;
        RearSelectionAvailable = Workspace.RearStatisticsState.ReservesLayout;

        if (SelectedSuspensionType == SuspensionType.Front && !FrontSelectionAvailable && RearSelectionAvailable)
        {
            SelectedSuspensionType = SuspensionType.Rear;
        }
        else if (SelectedSuspensionType == SuspensionType.Rear && !RearSelectionAvailable && FrontSelectionAvailable)
        {
            SelectedSuspensionType = SuspensionType.Front;
        }
    }
}
