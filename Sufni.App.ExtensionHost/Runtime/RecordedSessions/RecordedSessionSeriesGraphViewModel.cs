using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

namespace Sufni.App.ExtensionHost.Runtime.RecordedSessions;

// Neutral data view-model for an app-rendered hosted graph row. An extension
// fills it with neutral series + airtime spans; the app renders it with
// RecordedTimeSeriesPlot. ShowAirtime / Timeline are observable so the row
// header airtime action and the host timeline can drive the renderer.
public sealed partial class RecordedSessionSeriesGraphViewModel
    : ObservableObject, IRecordedSessionHostedGraphRowContributionViewModel
{
    public RecordedSessionSeriesGraphViewModel(
        IReadOnlyList<RecordedSessionGraphSeries> series,
        bool invertValueAxis,
        double durationSeconds,
        string emptyMessage,
        IReadOnlyList<RecordedSessionGraphSpan> airtimeSpans)
    {
        Series = series;
        InvertValueAxis = invertValueAxis;
        DurationSeconds = durationSeconds;
        EmptyMessage = emptyMessage;
        AirtimeSpans = airtimeSpans;
    }

    public IReadOnlyList<RecordedSessionGraphSeries> Series { get; }
    public bool InvertValueAxis { get; }
    public double DurationSeconds { get; }
    public string EmptyMessage { get; }
    public IReadOnlyList<RecordedSessionGraphSpan> AirtimeSpans { get; }

    [ObservableProperty] private bool showAirtime;
    [ObservableProperty] private IRecordedSessionTimeline? timeline;
}
