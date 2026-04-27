using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using ScottPlot;
using ScottPlot.Avalonia;

namespace Sufni.App.Views.Plots;

public abstract class SufniPlotView : TemplatedControl
{
    private AvaPlot? avaPlot;
    private AxisLimits? pinchStartLimits;
    private Coordinates? pinchOrigin;

    protected AvaPlot PlotControl => avaPlot!;
    protected bool HasPlotControl => avaPlot is not null;

    public void RefreshPlot() => avaPlot?.Refresh();

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        avaPlot = e.NameScope.Find<AvaPlot>("Plot");
        Debug.Assert(avaPlot != null, nameof(avaPlot) + " != null");

        // Stop ancestor gesture recognizers (e.g. the mobile session shell's
        // horizontal tab-swipe ScrollViewer) from stealing pointer input aimed
        // at the plot, so pan/zoom works inside the plot area.
        avaPlot.PointerPressed += OnPlotPointer;
        avaPlot.PointerMoved += OnPlotPointer;

        // Two-finger pinch zoom on touch.
        avaPlot.GestureRecognizers.Add(new PinchGestureRecognizer());
        avaPlot.AddHandler(Gestures.PinchEvent, OnPlotPinch);
        avaPlot.AddHandler(Gestures.PinchEndedEvent, OnPlotPinchEnded);

        CreatePlot();
    }

    protected abstract void CreatePlot();

    protected virtual void OnViewportChanged()
    {
    }

    private static void OnPlotPointer(object? sender, PointerEventArgs e)
        => e.PreventGestureRecognition();

    private void OnPlotPinch(object? sender, PinchEventArgs e)
    {
        if (avaPlot is null)
        {
            return;
        }

        if (pinchStartLimits is null)
        {
            pinchStartLimits = avaPlot.Plot.Axes.GetLimits();
            pinchOrigin = avaPlot.Plot.GetCoordinates((float)e.ScaleOrigin.X, (float)e.ScaleOrigin.Y);
        }

        var scale = Math.Max(e.Scale, 0.0001);
        var start = pinchStartLimits.Value;
        var origin = pinchOrigin!.Value;

        var left = origin.X - (origin.X - start.Left) / scale;
        var right = origin.X + (start.Right - origin.X) / scale;
        var bottom = origin.Y - (origin.Y - start.Bottom) / scale;
        var top = origin.Y + (start.Top - origin.Y) / scale;

        avaPlot.Plot.Axes.SetLimits(left, right, bottom, top);
        avaPlot.Refresh();
    }

    private void OnPlotPinchEnded(object? sender, PinchEndedEventArgs e)
    {
        pinchStartLimits = null;
        pinchOrigin = null;
        OnViewportChanged();
    }
}