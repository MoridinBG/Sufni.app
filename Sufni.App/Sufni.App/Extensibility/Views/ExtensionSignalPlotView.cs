using System.ComponentModel;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;

using Sufni.App.Shared.Plots;
using Sufni.App.Shared.Views.Plots;
namespace Sufni.App.Extensibility.Views;

// App plot view for a neutral extension-contributed signal plot. Derives from
// SufniTimeSeriesPlotView so theme reactivity, cursor readout, timeline link,
// and the airtime overlay are all inherited. SignalsWorkspace is intentionally
// never bound, which leaves analysis-range/preview/context-menu interactions
// inert (they early-return when SignalsWorkspace is null).
public sealed class ExtensionSignalPlotView : SufniTimeSeriesPlotView
{
    private RecordedSessionSignalPlotViewModel? vm;

    public ExtensionSignalPlotView()
    {
        DataContextChanged += (_, _) => BindViewModel(DataContext as RecordedSessionSignalPlotViewModel);
    }

    protected override double? TimelineDurationSeconds => vm?.DurationSeconds;
    protected override bool CanLoadPlotData => vm is { Series.Count: > 0 };

    protected override void CreatePlot()
    {
        SetPlotModel(new ExtensionSignalPlot(PlotControl.Plot, CurrentTheme));
        InitializeCursorReadoutInteractions();
    }

    protected override void LoadPlotData(TelemetryPlot plotModel)
    {
        if (plotModel is ExtensionSignalPlot p && vm is not null) p.LoadSeries(vm);
    }

    private void BindViewModel(RecordedSessionSignalPlotViewModel? next)
    {
        if (ReferenceEquals(vm, next)) return;
        if (vm is not null) vm.PropertyChanged -= OnVmChanged;
        vm = next;
        if (vm is not null) vm.PropertyChanged += OnVmChanged;
        Timeline = vm?.Timeline;                 // inherited; wires cursor + range linking
        ShowAirtime = vm?.ShowAirtime ?? false;  // inherited; toggles the Airtime overlay
        RequestReload();
    }

    private void OnVmChanged(object? s, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(RecordedSessionSignalPlotViewModel.Timeline):
                Timeline = vm?.Timeline; break;
            case nameof(RecordedSessionSignalPlotViewModel.ShowAirtime):
                ShowAirtime = vm?.ShowAirtime ?? false; break;
        }
    }
}
