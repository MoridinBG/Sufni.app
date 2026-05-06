using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels.SessionPages;

public sealed partial class StrokesPageViewModel : PageViewModelBase
{
    public ISessionStatisticsWorkspace Workspace { get; }

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

    public bool FrontSideSelected
    {
        get => SelectedSuspensionType == SuspensionType.Front;
        set
        {
            if (value)
            {
                SelectedSuspensionType = SuspensionType.Front;
            }
        }
    }

    public bool RearSideSelected
    {
        get => SelectedSuspensionType == SuspensionType.Rear;
        set
        {
            if (value)
            {
                SelectedSuspensionType = SuspensionType.Rear;
            }
        }
    }

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
        OnPropertyChanged(nameof(FrontSideSelected));
        OnPropertyChanged(nameof(RearSideSelected));
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
