using Sufni.App.Sessions.Signals.Views.Controls;
using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Segments.Models;

namespace Sufni.App.Segments.ViewModels;

/// <summary>
/// One signal of a compared match, shown as a child row of the matching built-in
/// signal row. Only the travel row of a match with airtimes has an airtime toggle.
/// </summary>
public sealed partial class SegmentComparisonRowViewModel : ObservableObject
{
    public const string MatchErrorToolTip = "Average GPS path distance after alignment. Lower is closer.";

    private const string AirtimeIconPathData =
        "M12 4L17.7 6.3L20 12L17.7 17.7L12 20L6.3 17.7L4 12L6.3 6.3Z";
    private const string CompareAnalysisIconPathData = "M4 6H20V8H4V6ZM4 11H17V13H4V11ZM4 16H13V18H4V16Z";
    private const string RemoveIconPathData =
        "M6.4 5L12 10.6L17.6 5L19 6.4L13.4 12L19 17.6L17.6 19L12 13.4L6.4 19L5 17.6L10.6 12L5 6.4Z";

    private readonly SignalRowAction? compareAnalysisAction;

    [ObservableProperty] public partial bool ShowAirtime { get; private set; }

    public SegmentComparisonRowViewModel(
        SegmentComparison comparison,
        SegmentComparisonSignal signal,
        Action<SegmentComparison> toggleAnalysisComparison,
        Action<SegmentComparisonRowViewModel> remove)
    {
        Comparison = comparison;
        Signal = signal;
        Series = comparison.GetSeries(signal);
        AirtimeSpans = signal == SegmentComparisonSignal.Travel ? comparison.Airtimes : [];
        ShowAirtime = AirtimeSpans.Count > 0;

        var actions = new List<SignalRowAction>();
        if (AirtimeSpans.Count > 0)
        {
            var airtimeAction = new SignalRowAction
            {
                Id = "airtime",
                Kind = SignalRowActionKind.Toggle,
                IconPathData = AirtimeIconPathData,
            };
            airtimeAction.Command = new RelayCommand(() =>
            {
                ShowAirtime = !ShowAirtime;
                UpdateAirtimeAction(airtimeAction);
            });
            UpdateAirtimeAction(airtimeAction);
            actions.Add(airtimeAction);
        }

        if (comparison.CanCompareAnalysis)
        {
            compareAnalysisAction = new SignalRowAction
            {
                Id = "compare-analysis",
                Kind = SignalRowActionKind.Toggle,
                IconPathData = CompareAnalysisIconPathData,
                Command = new RelayCommand(() => toggleAnalysisComparison(comparison)),
            };
            SetAnalysisCompared(false);
            actions.Add(compareAnalysisAction);
        }

        actions.Add(new SignalRowAction
        {
            Id = "remove",
            Kind = SignalRowActionKind.Execute,
            IconPathData = RemoveIconPathData,
            ToolTip = "Remove matched segment",
            Command = new RelayCommand(() => remove(this)),
            Tone = SignalRowActionTone.Danger,
        });
        HeaderActions = actions;
    }

    public SegmentComparison Comparison { get; }
    public SegmentComparisonSignal Signal { get; }
    public IReadOnlyList<SegmentComparisonSeries> Series { get; }
    public IReadOnlyList<SegmentMatchedAirtime> AirtimeSpans { get; }
    public IReadOnlyList<SignalRowAction> HeaderActions { get; }
    public bool InvertValueAxis => Signal == SegmentComparisonSignal.Travel;

    public string Title => $"{Comparison.Match.SessionName} {SignalName}  match error {Comparison.Match.NormalizedCostMeters:0.0}m";

    public string EmptyMessage => $"No matched {SignalName} data";

    private string SignalName => Signal switch
    {
        SegmentComparisonSignal.Travel => "travel",
        SegmentComparisonSignal.Velocity => "velocity",
        SegmentComparisonSignal.Imu => "IMU",
        _ => "speed",
    };

    internal void SetAnalysisCompared(bool isCompared)
    {
        if (compareAnalysisAction is null)
        {
            return;
        }

        compareAnalysisAction.IsChecked = isCompared;
        compareAnalysisAction.ToolTip = isCompared ? "Stop comparing analysis" : "Compare analysis";
    }

    private void UpdateAirtimeAction(SignalRowAction action)
    {
        action.IsChecked = ShowAirtime;
        action.ToolTip = ShowAirtime ? "Hide airtime" : "Show airtime";
    }
}
