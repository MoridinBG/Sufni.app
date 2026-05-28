using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Sufni.App.SessionDetails;

namespace Sufni.App.Views.Plots;

public class VelocityBandView : TemplatedControl
{
    private static readonly GridLength highSpeedZoneLength = new(
        SessionDampingSettings.VelocityHistogramLimitMmPerSecond -
        SessionDampingSettings.HighSpeedThresholdMmPerSecond,
        GridUnitType.Star);

    private static readonly GridLength lowSpeedZoneLength = new(
        SessionDampingSettings.HighSpeedThresholdMmPerSecond,
        GridUnitType.Star);

    public GridLength HighSpeedZoneLength => highSpeedZoneLength;

    public GridLength LowSpeedZoneLength => lowSpeedZoneLength;

    public static readonly StyledProperty<double?> HsrPercentageProperty = AvaloniaProperty.Register<VelocityBandView, double?>(
        "HsrPercentage");

    public double? HsrPercentage
    {
        get => GetValue(HsrPercentageProperty);
        set => SetValue(HsrPercentageProperty, value);
    }

    public static readonly StyledProperty<double?> LsrPercentageProperty = AvaloniaProperty.Register<VelocityBandView, double?>(
        "LsrPercentage");

    public double? LsrPercentage
    {
        get => GetValue(LsrPercentageProperty);
        set => SetValue(LsrPercentageProperty, value);
    }

    public static readonly StyledProperty<double?> LscPercentageProperty = AvaloniaProperty.Register<VelocityBandView, double?>(
        "LscPercentage");

    public double? LscPercentage
    {
        get => GetValue(LscPercentageProperty);
        set => SetValue(LscPercentageProperty, value);
    }

    public static readonly StyledProperty<double?> HscPercentageProperty = AvaloniaProperty.Register<VelocityBandView, double?>(
        "HscPercentage");

    public double? HscPercentage
    {
        get => GetValue(HscPercentageProperty);
        set => SetValue(HscPercentageProperty, value);
    }
}
