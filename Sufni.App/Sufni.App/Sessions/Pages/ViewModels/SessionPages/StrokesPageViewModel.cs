using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.Telemetry;

using Sufni.App.Sessions.Detail.ViewModels.Editors;
namespace Sufni.App.Sessions.Pages.ViewModels.SessionPages;

public sealed partial class StrokesPageViewModel : PageViewModelBase
{
    public ISessionAnalysisWorkspace Workspace { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBothAnalysisSides))]
    public partial bool FrontSelectionAvailable { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBothAnalysisSides))]
    public partial bool RearSelectionAvailable { get; set; }

    [ObservableProperty] public partial SuspensionType SelectedSuspensionType { get; set; } = SuspensionType.Front;

    public bool ShowFrontStrokes => SelectedSuspensionType == SuspensionType.Front;
    public bool ShowRearStrokes => SelectedSuspensionType == SuspensionType.Rear;
    public bool HasBothAnalysisSides => FrontSelectionAvailable && RearSelectionAvailable;

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

    public StrokesPageViewModel(ISessionAnalysisWorkspace workspace)
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
        if (args.PropertyName is nameof(ISessionAnalysisWorkspace.FrontAnalysisState)
            or nameof(ISessionAnalysisWorkspace.RearAnalysisState))
        {
            RefreshAvailableSides();
        }
    }

    private void RefreshAvailableSides()
    {
        FrontSelectionAvailable = Workspace.FrontAnalysisState.ReservesLayout;
        RearSelectionAvailable = Workspace.RearAnalysisState.ReservesLayout;

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
