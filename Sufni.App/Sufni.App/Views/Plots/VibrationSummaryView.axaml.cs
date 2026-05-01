using Avalonia;
using Avalonia.Controls.Primitives;
using Sufni.Telemetry;

namespace Sufni.App.Views.Plots;

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

    public string Title
    {
        get => title;
        private set => SetAndRaise(TitleProperty, ref title, value);
    }

    static VibrationSummaryView()
    {
        TelemetryProperty.Changed.AddClassHandler<VibrationSummaryView>((view, _) => view.Recompute());
        AnalysisRangeProperty.Changed.AddClassHandler<VibrationSummaryView>((view, _) => view.Recompute());
        SuspensionTypeProperty.Changed.AddClassHandler<VibrationSummaryView>((view, _) => view.Recompute());
        ImuLocationProperty.Changed.AddClassHandler<VibrationSummaryView>((view, _) => view.Recompute());
    }

    private void Recompute()
    {
        Title = $"{SuspensionType} {ImuLocation} vibration";
        Stats = Telemetry is null
            ? null
            : TelemetryStatistics.CalculateVibration(Telemetry, ImuLocation, SuspensionType, AnalysisRange);
    }
}
