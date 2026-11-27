using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.SensorFindings.ViewModels;

/// <summary>The undecided findings of one sensor and one class. Only droppable findings take a decision.</summary>
public sealed partial class SensorFindingRowViewModel(
    SensorIdentity sensor,
    string sensorName,
    string details,
    string rangesText,
    IReadOnlyList<SourceTimeRange> ranges,
    bool canDecide,
    Func<SensorFindingRowViewModel, SensorDecision, Task> decide)
{
    public SensorIdentity Sensor => sensor;
    public string SensorName => sensorName;
    public string Details => details;
    public string RangesText => rangesText;
    public IReadOnlyList<SourceTimeRange> Ranges => ranges;
    public bool CanDecide => canDecide;

    [RelayCommand(CanExecute = nameof(CanDecide))]
    private Task Drop() => decide(this, SensorDecision.Drop);

    [RelayCommand(CanExecute = nameof(CanDecide))]
    private Task Keep() => decide(this, SensorDecision.Keep);
}
