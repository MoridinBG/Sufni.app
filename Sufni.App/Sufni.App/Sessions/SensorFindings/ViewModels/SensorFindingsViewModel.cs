using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Models;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.SensorFindings.ViewModels;

/// <summary>
/// Dialog content with the findings of one session that no decision covers, one row per sensor and class. Rows follow
/// the decisions that the session store publishes; Drop and Keep go to the session coordinator. The dialog completes on
/// <see cref="CloseCommand"/> and when no row is left.
/// </summary>
public sealed partial class SensorFindingsViewModel : ObservableObject, IContentDialogCompletionSource, IDisposable
{
    private readonly Guid sessionId;
    private readonly ImmutableArray<SensorFinding> findings;
    private readonly ISessionCoordinator sessionCoordinator;
    private readonly Action<FailurePresentation> reportError;
    private readonly Action<string> notify;
    private readonly IDisposable subscription;
    private (ImmutableArray<SensorFinding> Findings, double Offset, double? Duration) shown = ([], 0, null);

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

    [ObservableProperty] public partial bool HasRows { get; private set; }

    public event EventHandler? Completed;

    public void Dispose() => subscription.Dispose();

    [RelayCommand]
    private void Close() => Completed?.Invoke(this, EventArgs.Empty);

    private void Apply(SessionSnapshot snapshot)
    {
        var current = (Findings: snapshot.SensorDecisions.Undecided(findings), Offset: snapshot.WindowStartSeconds,
            Duration: snapshot.DurationSeconds);
        if (current.Findings.SequenceEqual(shown.Findings) && current.Offset == shown.Offset && current.Duration == shown.Duration)
            return;
        shown = current;

        var text = new FindingText(current.Offset, current.Duration);
        Rows.Clear();
        foreach (var group in current.Findings.GroupBy(finding => (finding.Sensor, finding.IsNotice))
                     .OrderBy(group => group.Key.Sensor.Kind).ThenBy(group => group.Key.Sensor.Location)
                     .ThenBy(group => group.Key.IsNotice))
        {
            var reasons = group.GroupBy(finding => finding.Reason)
                .Select(reason => (Reason: reason.Key, Ranges: reason.SelectMany(finding => finding.Ranges).OrderBy(range => range.StartSeconds).ToArray()))
                .OrderByDescending(reason => Total(reason.Ranges))
                .ToArray();
            Rows.Add(new SensorFindingRowViewModel(group.Key.Sensor, SensorName(group.Key.Sensor),
                string.Join(", ", reasons.Select(reason => text.Summary(Reason(reason.Reason), reason.Ranges))),
                string.Join(Environment.NewLine, reasons.Select(reason => $"{Reason(reason.Reason)}: {text.Ranges(reason.Ranges)}")),
                [.. group.SelectMany(finding => finding.Ranges)], !group.Key.IsNotice, DecideAsync));
        }
        HasRows = Rows.Count > 0;
        if (!HasRows) Close();
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
        SensorFaultReason.Glitch => "glitches",
        SensorFaultReason.JumpAndStuck => "stuck after a jump",
        SensorFaultReason.FixLost => "no fix",
        SensorFaultReason.NoValue => "no readings",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    private static double Total(IEnumerable<SourceTimeRange> ranges) => ranges.Sum(range => range.EndSeconds - range.StartSeconds);

    // Ranges are on the recording source timeline; the session starts at its window start.
    private sealed record FindingText(double Offset, double? Duration)
    {
        private const double WholeRideShare = 0.95;
        private const double SpanShownFromSeconds = 60;

        // One clock format for the whole session, so times in different rows line up.
        private bool WithHours => Duration >= 3600;

        public string Summary(string reason, IReadOnlyList<SourceTimeRange> ranges)
        {
            var total = Total(ranges);
            if (Duration is > 0 and var duration && total >= WholeRideShare * duration) return $"{reason} for the whole ride";

            var start = ranges[0].StartSeconds;
            var end = ranges.Max(range => range.EndSeconds);
            var span = end - start < SpanShownFromSeconds ? Time(start) : $"{Time(start)}–{Time(end)}";
            return $"{reason} for {Length(total)} ({span})";
        }

        public string Ranges(IEnumerable<SourceTimeRange> ranges) =>
            string.Join(", ", ranges.Select(range => $"{Time(range.StartSeconds)}–{Time(range.EndSeconds)}"));

        private string Time(double sourceSeconds)
        {
            var time = TimeSpan.FromSeconds(Math.Floor(Math.Max(0, sourceSeconds - Offset)));
            return WithHours ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
        }

        private static string Length(double seconds)
        {
            var rounded = (int)Math.Round(seconds);
            if (rounded < 1) return "under 1 s";
            if (rounded < 60) return $"{rounded} s";
            if (rounded < 3600) return rounded % 60 == 0 ? $"{rounded / 60} min" : $"{rounded / 60} min {rounded % 60} s";
            var minutes = rounded / 60;
            return minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60} min";
        }
    }
}
