using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using ScottPlot;

namespace Sufni.App.Views.Plots;

public abstract class SufniPlotView : TemplatedControl
{
    private readonly HashSet<IPointer> pointersStartedInDataArea = [];
    private SufniAvaPlot? avaPlot;
    private AxisLimits? pinchStartLimits;
    private Coordinates? pinchOrigin;
    private bool viewportChangeQueued;

    protected SufniAvaPlot PlotControl => avaPlot!;
    protected bool HasPlotControl => avaPlot is not null;

    public void RefreshPlot() => avaPlot?.Refresh();

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        avaPlot = e.NameScope.Find<SufniAvaPlot>("Plot");
        Debug.Assert(avaPlot != null, nameof(avaPlot) + " != null");
        pointersStartedInDataArea.Clear();

        // Stop ancestor gesture recognizers (e.g. the mobile session shell's
        // horizontal tab-swipe ScrollViewer) only when the touch starts inside
        // ScottPlot's data area. Touches that begin on the surrounding margins
        // should continue to scroll the page normally.
        avaPlot.PointerPressed += OnPlotPointerPressed;
        avaPlot.PointerMoved += OnPlotPointerMoved;
        avaPlot.PointerReleased += OnPlotPointerReleased;
        avaPlot.PointerCaptureLost += OnPlotPointerCaptureLost;

        // Two-finger pinch zoom on touch.
        avaPlot.GestureRecognizers.Add(new PinchGestureRecognizer());
        avaPlot.AddHandler(Gestures.PinchEvent, OnPlotPinch);
        avaPlot.AddHandler(Gestures.PinchEndedEvent, OnPlotPinchEnded);

        CreatePlot();
        avaPlot.Plot.RenderManager.AxisLimitsChanged += (_, _) => NotifyViewportChanged();
    }

    protected abstract void CreatePlot();

    protected virtual void OnViewportChanged()
    {
    }

    private void OnPlotPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!DidPointerStartInDataArea(e))
        {
            return;
        }

        pointersStartedInDataArea.Add(e.Pointer);
        e.PreventGestureRecognition();
    }

    private void OnPlotPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!pointersStartedInDataArea.Contains(e.Pointer))
        {
            return;
        }

        e.PreventGestureRecognition();
    }

    private void OnPlotPointerReleased(object? sender, PointerReleasedEventArgs e)
        => pointersStartedInDataArea.Remove(e.Pointer);

    private void OnPlotPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        => pointersStartedInDataArea.Remove(e.Pointer);

    private bool DidPointerStartInDataArea(PointerEventArgs e)
    {
        return avaPlot is not null && avaPlot.IsPointInDataArea(e.GetPosition(avaPlot));
    }

    private void OnPlotPinch(object? sender, PinchEventArgs e)
    {
        if (avaPlot is null)
        {
            return;
        }

        if (pinchStartLimits is null)
        {
            if (!avaPlot.IsPointInDataArea(e.ScaleOrigin))
            {
                return;
            }

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
        NotifyViewportChanged();
    }

    private void NotifyViewportChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            OnViewportChanged();
            return;
        }

        if (viewportChangeQueued)
        {
            return;
        }

        viewportChangeQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            viewportChangeQueued = false;
            OnViewportChanged();
        }, DispatcherPriority.Background);
    }
}