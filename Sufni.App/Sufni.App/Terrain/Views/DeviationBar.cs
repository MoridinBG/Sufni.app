using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Sufni.App.Terrain.Views;

/// <summary>
/// A signed deviation drawn from a centre line over a band that marks the repeat range. The track spans the whole
/// scale, which is the half width.
/// </summary>
public sealed class DeviationBar : Control
{
    public static readonly StyledProperty<double?> DeviationProperty = AvaloniaProperty.Register<DeviationBar, double?>(nameof(Deviation));
    public static readonly StyledProperty<double> RepeatRangeProperty = AvaloniaProperty.Register<DeviationBar, double>(nameof(RepeatRange));
    public static readonly StyledProperty<double> ScaleProperty = AvaloniaProperty.Register<DeviationBar, double>(nameof(Scale), 1);
    public static readonly StyledProperty<IBrush?> TrackBrushProperty = AvaloniaProperty.Register<DeviationBar, IBrush?>(nameof(TrackBrush));
    public static readonly StyledProperty<IBrush?> BandBrushProperty = AvaloniaProperty.Register<DeviationBar, IBrush?>(nameof(BandBrush));
    public static readonly StyledProperty<IBrush?> BarBrushProperty = AvaloniaProperty.Register<DeviationBar, IBrush?>(nameof(BarBrush));
    public static readonly StyledProperty<IBrush?> LineBrushProperty = AvaloniaProperty.Register<DeviationBar, IBrush?>(nameof(LineBrush));

    static DeviationBar() => AffectsRender<DeviationBar>(DeviationProperty, RepeatRangeProperty, ScaleProperty,
        TrackBrushProperty, BandBrushProperty, BarBrushProperty, LineBrushProperty);

    public double? Deviation { get => GetValue(DeviationProperty); set => SetValue(DeviationProperty, value); }
    public double RepeatRange { get => GetValue(RepeatRangeProperty); set => SetValue(RepeatRangeProperty, value); }
    public double Scale { get => GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? BandBrush { get => GetValue(BandBrushProperty); set => SetValue(BandBrushProperty, value); }
    public IBrush? BarBrush { get => GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
    public IBrush? LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        var (width, height) = (Bounds.Width, Bounds.Height);
        if (width <= 0 || height <= 0 || Scale <= 0) return;
        var centre = width / 2;
        double X(double value) => centre + Math.Clamp(value / Scale, -1, 1) * centre;
        if (TrackBrush is not null) context.FillRectangle(TrackBrush, new Rect(0, 0, width, height));
        if (BandBrush is not null)
            context.FillRectangle(BandBrush, new Rect(X(-RepeatRange), 0, X(RepeatRange) - X(-RepeatRange), height));
        if (Deviation is { } deviation && BarBrush is not null)
        {
            var end = X(deviation);
            context.FillRectangle(BarBrush, new Rect(Math.Min(centre, end), height * 0.2, Math.Abs(end - centre), height * 0.6));
        }
        if (LineBrush is not null) context.FillRectangle(LineBrush, new Rect(centre - 0.5, 0, 1, height));
    }
}
