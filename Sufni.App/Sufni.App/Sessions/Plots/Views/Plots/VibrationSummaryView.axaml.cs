using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Plots.Views.Plots;

public class VibrationSummaryView : TemplatedControl
{
    public static readonly StyledProperty<TelemetryData?> TelemetryProperty =
        AvaloniaProperty.Register<VibrationSummaryView, TelemetryData?>(nameof(Telemetry));

    public TelemetryData? Telemetry
    {
        get => GetValue(TelemetryProperty);
        set => SetValue(TelemetryProperty, value);
    }

    public static readonly StyledProperty<TelemetryTimeRange?> AnalysisRangeProperty =
        AvaloniaProperty.Register<VibrationSummaryView, TelemetryTimeRange?>(nameof(AnalysisRange));

    public TelemetryTimeRange? AnalysisRange
    {
        get => GetValue(AnalysisRangeProperty);
        set => SetValue(AnalysisRangeProperty, value);
    }

    public static readonly StyledProperty<IRecordedSessionAnalysisResultState?> AnalysisResultStateProperty =
        AvaloniaProperty.Register<VibrationSummaryView, IRecordedSessionAnalysisResultState?>(
            nameof(AnalysisResultState));

    public IRecordedSessionAnalysisResultState? AnalysisResultState
    {
        get => GetValue(AnalysisResultStateProperty);
        set => SetValue(AnalysisResultStateProperty, value);
    }

    public static readonly StyledProperty<SuspensionType> SuspensionTypeProperty =
        AvaloniaProperty.Register<VibrationSummaryView, SuspensionType>(nameof(SuspensionType));

    public SuspensionType SuspensionType
    {
        get => GetValue(SuspensionTypeProperty);
        set => SetValue(SuspensionTypeProperty, value);
    }

    public static readonly StyledProperty<ImuLocation> ImuLocationProperty =
        AvaloniaProperty.Register<VibrationSummaryView, ImuLocation>(nameof(ImuLocation));

    public ImuLocation ImuLocation
    {
        get => GetValue(ImuLocationProperty);
        set => SetValue(ImuLocationProperty, value);
    }

    public static readonly DirectProperty<VibrationSummaryView, VibrationStats?> StatsProperty =
        AvaloniaProperty.RegisterDirect<VibrationSummaryView, VibrationStats?>(nameof(Stats), o => o.Stats);

    private VibrationStats? stats;

    public VibrationStats? Stats
    {
        get => stats;
        private set => SetAndRaise(StatsProperty, ref stats, value);
    }

    public static readonly DirectProperty<VibrationSummaryView, string> TitleProperty =
        AvaloniaProperty.RegisterDirect<VibrationSummaryView, string>(nameof(Title), o => o.Title);

    private string title = string.Empty;
    private IRecordedSessionAnalysisResultState? subscribedAnalysisResultState;
    private IDisposable? analysisInputSubscription;
    private IDisposable? analysisResultSubscription;

    public string Title
    {
        get => title;
        private set => SetAndRaise(TitleProperty, ref title, value);
    }

    static VibrationSummaryView()
    {
        TelemetryProperty.Changed.AddClassHandler<VibrationSummaryView>((view, _) => view.Recompute());
        AnalysisRangeProperty.Changed.AddClassHandler<VibrationSummaryView>((view, _) => view.Recompute());
        AnalysisResultStateProperty.Changed.AddClassHandler<VibrationSummaryView>((view, _) =>
        {
            view.SubscribeToAnalysisResultState(view.AnalysisResultState);
            view.Recompute();
        });
        SuspensionTypeProperty.Changed.AddClassHandler<VibrationSummaryView>((view, _) => view.Recompute());
        ImuLocationProperty.Changed.AddClassHandler<VibrationSummaryView>((view, _) => view.Recompute());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToAnalysisResultState(AnalysisResultState);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SubscribeToAnalysisResultState(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void Recompute()
    {
        Title = $"{SuspensionType} {ImuLocation} vibration";
        var key = CreateAnalysisKey();
        if (key is not null && AnalysisResultState is { } state)
        {
            if (state.Get(key) is VibrationDistributionAnalysisResult result)
            {
                Stats = result.Stats;
                return;
            }

            Stats = null;
            _ = state.RequestAsync(key);
            return;
        }

        Stats = Telemetry is null
            ? null
            : TelemetryStatistics.CalculateVibration(Telemetry, ImuLocation, SuspensionType, AnalysisRange);
    }

    private void SubscribeToAnalysisResultState(IRecordedSessionAnalysisResultState? state)
    {
        if (ReferenceEquals(subscribedAnalysisResultState, state))
        {
            return;
        }

        analysisResultSubscription?.Dispose();
        analysisResultSubscription = null;
        analysisInputSubscription?.Dispose();
        analysisInputSubscription = null;
        subscribedAnalysisResultState = state;

        if (subscribedAnalysisResultState is not null)
        {
            analysisInputSubscription = subscribedAnalysisResultState.ConnectInputs().Subscribe(_ => Recompute());
            analysisResultSubscription = subscribedAnalysisResultState.Connect().Subscribe(OnAnalysisResultChanged);
        }
    }

    private void OnAnalysisResultChanged(RecordedSessionAnalysisResultChanged change)
    {
        var key = CreateAnalysisKey();
        if (key is null ||
            change.Key != key ||
            change.Result is not VibrationDistributionAnalysisResult result)
        {
            return;
        }

        Stats = result.Stats;
    }

    private RecordedSessionAnalysisKey? CreateAnalysisKey()
    {
        return AnalysisResultState?.CurrentInputs?.CreateKey(
            RecordedSessionAnalysisFamily.VibrationDistribution,
            SuspensionType,
            balanceType: null,
            imuLocation: ImuLocation);
    }
}
