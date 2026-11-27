using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Models;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.SensorFindings.ViewModels;

/// <summary>
/// The findings of one session that no decision covers, one row per sensor and class. Rows follow the
/// decisions that the session store publishes; Drop and Keep go to the session coordinator.
/// </summary>
public sealed partial class SensorFindingsViewModel : ObservableObject, IDisposable
{
    private readonly Guid sessionId;
    private readonly ImmutableArray<SensorFinding> findings;
    private readonly ISessionCoordinator sessionCoordinator;
    private readonly Action<FailurePresentation> reportError;
    private readonly Action<string> notify;
    private readonly IDisposable subscription;
    private ImmutableArray<SensorFinding> shown = [];
    private double shownOffset;

    public SensorFindingsViewModel(Guid sessionId, ImmutableArray<SensorFinding> findings, ISessionStore sessionStore,
        ISessionCoordinator sessionCoordinator, IUiThreadDispatcher dispatcher, Action<FailurePresentation> reportError,
        Action<string> notify)
    {
        this.sessionId = sessionId;
        this.findings = findings;
        this.sessionCoordinator = sessionCoordinator;
        this.reportError = reportError;
        this.notify = notify;
        subscription = sessionStore.Watch(sessionId).ObserveOnUiThread(dispatcher).Subscribe(Apply);
    }

    public ObservableCollection<SensorFindingRowViewModel> Rows { get; } = [];

    [ObservableProperty] public partial string SessionName { get; private set; } = string.Empty;
    [ObservableProperty] public partial bool HasRows { get; private set; }

    public void Dispose() => subscription.Dispose();

    private void Apply(SessionSnapshot snapshot)
    {
        SessionName = snapshot.Name;
        var undecided = snapshot.SensorDecisions.Undecided(findings);
        if (undecided.SequenceEqual(shown) && snapshot.WindowStartSeconds == shownOffset) return;
        shown = undecided;
        shownOffset = snapshot.WindowStartSeconds;

        Rows.Clear();
        foreach (var group in undecided.GroupBy(finding => (finding.Sensor, finding.IsNotice))
                     .OrderBy(group => group.Key.Sensor.Kind).ThenBy(group => group.Key.Sensor.Location)
                     .ThenBy(group => group.Key.IsNotice))
        {
            Rows.Add(new SensorFindingRowViewModel(group.Key.Sensor, SensorName(group.Key.Sensor),
                string.Join("; ", group.Select(finding => $"{Reason(finding.Reason)} {Ranges(finding.Ranges, shownOffset)}")),
                [.. group.SelectMany(finding => finding.Ranges)], !group.Key.IsNotice, DecideAsync));
        }
        HasRows = Rows.Count > 0;
    }

    private async Task DecideAsync(SensorFindingRowViewModel row, SensorDecision decision)
    {
        switch (await sessionCoordinator.SetSensorDecisionAsync(sessionId, row.Sensor, decision, row.Ranges))
        {
            case SensorDecisionResult.Failed failed:
                reportError(FailurePresentation.From(failed.Reason, failed.Details));
                break;
            case SensorDecisionResult.Missing:
            case SensorDecisionResult.Dropped { Recompute: SessionRecomputeResult.Missing }:
                notify("The session is no longer available.");
                break;
            case SensorDecisionResult.Dropped { Recompute: SessionRecomputeResult.ProcessingFailed failed }:
                reportError(FailurePresentation.From(failed.Reason, failed.Details));
                break;
            case SensorDecisionResult.Dropped { Recompute: SessionRecomputeResult.Failed failed }:
                reportError(FailurePresentation.From(failed.Reason, failed.Details));
                break;
            case SensorDecisionResult.Dropped { Recompute: SessionRecomputeResult.NotRecomputable }:
                reportError(new FailurePresentation("The readings were dropped, but the session cannot be recomputed until the source recording is restored."));
                break;
        }
    }

    private static string SensorName(SensorIdentity sensor) => sensor.Kind switch
    {
        SensorKind.ForkTravel => "Fork travel",
        SensorKind.ShockTravel => "Shock travel",
        SensorKind.Imu => $"{LocationName(sensor.Location)} IMU",
        SensorKind.Gps => "GPS",
        SensorKind.Temperature => $"{LocationName(sensor.Location)} temperature",
        _ => throw new ArgumentOutOfRangeException(nameof(sensor)),
    };

    private static string LocationName(byte? location) => location switch
    {
        0 => "Frame",
        1 => "Fork",
        2 => "Rear",
        _ => $"Sensor {location}",
    };

    private static string Reason(SensorFaultReason reason) => reason switch
    {
        SensorFaultReason.OutOfRange => "out of range",
        SensorFaultReason.Frozen => "frozen",
        SensorFaultReason.Zero => "reads zero",
        SensorFaultReason.Glitch => "glitch",
        SensorFaultReason.JumpAndStuck => "jumped and stuck",
        SensorFaultReason.FixLost => "no fix",
        SensorFaultReason.NoValue => "no readings",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    // Ranges are on the recording source timeline; the session starts at its window start.
    private static string Ranges(IEnumerable<SourceTimeRange> ranges, double offset) =>
        string.Join(", ", ranges.Select(range => $"{Time(range.StartSeconds - offset)}–{Time(range.EndSeconds - offset)}"));

    private static string Time(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss");
}
