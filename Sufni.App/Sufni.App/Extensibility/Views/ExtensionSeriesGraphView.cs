using System.ComponentModel;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;

using Sufni.App.Shared.Plots;
using Sufni.App.Shared.Views.Plots;
namespace Sufni.App.Extensibility.Views;

// App plot view for a neutral extension-contributed series graph. Derives from
// SufniTimeSeriesPlotView so theme reactivity, cursor readout, timeline link,
// and the airtime overlay are all inherited. GraphWorkspace is intentionally
// never bound, which leaves analysis-range/preview/context-menu interactions
// inert (they early-return when GraphWorkspace is null).
public sealed class ExtensionSeriesGraphView : SufniTimeSeriesPlotView
{
    private RecordedSessionSeriesGraphViewModel? vm;

    public ExtensionSeriesGraphView()
    {
        DataContextChanged += (_, _) => BindViewModel(DataContext as RecordedSessionSeriesGraphViewModel);
    }

    protected override double? TimelineDurationSeconds => vm?.DurationSeconds;
    protected override bool CanLoadPlotData => vm is { Series.Count: > 0 };

    protected override void CreatePlot()
    {
        SetPlotModel(new ExtensionSeriesGraphPlot(PlotControl.Plot, CurrentTheme));
        InitializeCursorReadoutInteractions();
    }

    protected override void LoadPlotData(TelemetryPlot plotModel)
    {
        if (plotModel is ExtensionSeriesGraphPlot p && vm is not null) p.LoadSeries(vm);
    }

    private void BindViewModel(RecordedSessionSeriesGraphViewModel? next)
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
            case nameof(RecordedSessionSeriesGraphViewModel.Timeline):
                Timeline = vm?.Timeline; break;
            case nameof(RecordedSessionSeriesGraphViewModel.ShowAirtime):
                ShowAirtime = vm?.ShowAirtime ?? false; break;
        }
    }
}
