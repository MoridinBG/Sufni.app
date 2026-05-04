using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Presentation;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.ViewModels.SessionPages;

public partial class BalancePageViewModel : PageViewModelBase
{
    public ISessionStatisticsWorkspace? StatisticsWorkspace { get; }
    public bool HasDynamicStatistics => StatisticsWorkspace?.TelemetryData is not null;
    public SurfacePresentationState CompressionPresentationState => HasDynamicStatistics
        ? StatisticsWorkspace!.CompressionBalanceState
        : CompressionBalanceState;
    public SurfacePresentationState ReboundPresentationState => HasDynamicStatistics
        ? StatisticsWorkspace!.ReboundBalanceState
        : ReboundBalanceState;

    [ObservableProperty] private string? compressionBalance;
    [ObservableProperty] private string? reboundBalance;
    [ObservableProperty] private SurfacePresentationState compressionBalanceState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState reboundBalanceState = SurfacePresentationState.Hidden;

    public BalancePageViewModel(ISessionStatisticsWorkspace? statisticsWorkspace = null)
        : base("Balance")
    {
        StatisticsWorkspace = statisticsWorkspace;
        if (statisticsWorkspace is INotifyPropertyChanged observableWorkspace)
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
        if (args.PropertyName is nameof(ISessionStatisticsWorkspace.TelemetryData))
        {
            OnPropertyChanged(nameof(HasDynamicStatistics));
            OnPropertyChanged(nameof(CompressionPresentationState));
            OnPropertyChanged(nameof(ReboundPresentationState));
        }
        else if (args.PropertyName is nameof(ISessionStatisticsWorkspace.CompressionBalanceState))
        {
            OnPropertyChanged(nameof(CompressionPresentationState));
        }
        else if (args.PropertyName is nameof(ISessionStatisticsWorkspace.ReboundBalanceState))
        {
            OnPropertyChanged(nameof(ReboundPresentationState));
        }
    }
}
