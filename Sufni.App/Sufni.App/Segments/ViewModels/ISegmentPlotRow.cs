using System.Collections.Generic;
using Sufni.App.Segments.Models;
using Sufni.App.Sessions.Signals.Views.Controls;
using Sufni.Telemetry;

namespace Sufni.App.Segments.ViewModels;

public interface ISegmentPlotRow
{
    string Title { get; }
    string? TitleToolTip { get; }
    string EmptyMessage { get; }
    double SourceDurationSeconds { get; }
    IReadOnlyList<SegmentComparisonSeries> Series { get; }
    IReadOnlyList<SignalRowAction> HeaderActions { get; }
    IReadOnlyList<SegmentMatchedAirtime> AirtimeSpans { get; }
    bool InvertValueAxis { get; }
    bool ShowAirtime { get; }
}

public sealed record TrailAverageRow(int RunCount, double SourceDurationSeconds, IReadOnlyList<SegmentComparisonSeries> Series) : ISegmentPlotRow
{
    public string Title => $"Trail average ({RunCount} runs)";
    public string? TitleToolTip => null;
    public string EmptyMessage => $"Fewer than {TrailAverage.MinimumRuns} readings at each position.";
    public IReadOnlyList<SignalRowAction> HeaderActions => [];
    public IReadOnlyList<SegmentMatchedAirtime> AirtimeSpans => [];
    public bool InvertValueAxis => false;
    public bool ShowAirtime => false;
}
