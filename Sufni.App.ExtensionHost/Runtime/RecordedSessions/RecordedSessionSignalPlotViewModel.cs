using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

namespace Sufni.App.ExtensionHost.Runtime.RecordedSessions;

// Neutral data view-model for an app-rendered hosted signal row. An extension
// fills it with neutral series + airtime spans; the app renders it with
// RecordedTimeSeriesPlot. ShowAirtime / Timeline are observable so the row
// header airtime action and the host timeline can drive the renderer.
public sealed partial class RecordedSessionSignalPlotViewModel
    : ObservableObject, IRecordedSessionHostedSignalRowContributionViewModel
{
    public RecordedSessionSignalPlotViewModel(
        IReadOnlyList<RecordedSessionSignalSeries> series,
        bool invertValueAxis,
        double durationSeconds,
        string emptyMessage,
        IReadOnlyList<RecordedSessionSignalSpan> airtimeSpans)
    {
        Series = series;
        InvertValueAxis = invertValueAxis;
        DurationSeconds = durationSeconds;
        EmptyMessage = emptyMessage;
        AirtimeSpans = airtimeSpans;
    }

    public IReadOnlyList<RecordedSessionSignalSeries> Series { get; }
    public bool InvertValueAxis { get; }
    public double DurationSeconds { get; }
    public string EmptyMessage { get; }
    public IReadOnlyList<RecordedSessionSignalSpan> AirtimeSpans { get; }

    [ObservableProperty] public partial bool ShowAirtime { get; set; }
    [ObservableProperty] public partial IRecordedSessionTimeline? Timeline { get; set; }
}
