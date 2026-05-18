using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Sufni.App.Theming;

namespace Sufni.App.Views.Controls;

internal sealed class TelemetryBaseRowDivider : Control
{
    private IBrush dividerBrush = SufniThemes.Fallback.GraphRow.DividerBetweenRoots.ToBrush();
    private IDisposable? themeVariantSubscription;
    private bool isDragging;
    private bool canResizeTargetRow;
    private double dragStartY;
    private double dragStartHeight;

    internal TelemetryPlotRow? TargetRow { get; set; }
    internal bool CanResizeTargetRow
    {
        get => canResizeTargetRow;
        set
        {
            canResizeTargetRow = value;
            IsHitTestVisible = value;
        }
    }

    public TelemetryBaseRowDivider()
    {
        Height = TelemetryPlotRowsPanel.BaseRowDividerHeight;
        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);

        AddHandler(
            DoubleTappedEvent,
            (_, args) =>
            {
                ResetTargetRowToPreferredHeight();
                args.Handled = true;
            },
            handledEventsToo: true);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        themeVariantSubscription = this.GetObservable(ThemeVariantScope.ActualThemeVariantProperty)
            .Subscribe(_ =>
            {
                dividerBrush = SufniThemes.FromVariant(ActualThemeVariant).GraphRow.DividerBetweenRoots.ToBrush();
                InvalidateVisual();
            });
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        themeVariantSubscription?.Dispose();
        themeVariantSubscription = null;

        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var lineY = Math.Max(0, Bounds.Height - 1);
        context.FillRectangle(dividerBrush, new Rect(0, lineY, Bounds.Width, 1));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!CanResizeTargetRow || TargetRow is null)
        {
            return;
        }

        isDragging = true;
        dragStartY = e.GetPosition(this).Y;
        dragStartHeight = TargetRow.ManualGroupHeight ?? TargetRow.AllocatedGroupHeight;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!isDragging || !CanResizeTargetRow || TargetRow is null)
        {
            return;
        }

        var deltaY = e.GetPosition(this).Y - dragStartY;
        TargetRow.ManualGroupHeight = double.Max(TargetRow.GetMinimumGroupHeight(), dragStartHeight + deltaY);
        InvalidateOwnerMeasure();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        EndDrag(e.Pointer);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        isDragging = false;
    }

    private void EndDrag(IPointer pointer)
    {
        isDragging = false;
        pointer.Capture(null);
    }

    private void InvalidateOwnerMeasure()
    {
        this.FindAncestorOfType<TelemetryPlotRowsPanel>()?.InvalidateMeasure();
    }

    internal void ResetTargetRowToPreferredHeight()
    {
        if (!CanResizeTargetRow || TargetRow is null)
        {
            return;
        }

        TargetRow.ManualGroupHeight = TargetRow.GetPreferredGroupHeight();
        InvalidateOwnerMeasure();
    }
}
