using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

using Sufni.App.Theming;
using Sufni.App.Infrastructure.Theming;
namespace Sufni.App.Shared.Views.Controls;

internal sealed class SignalBaseRowDivider : Control
{
    private IBrush dividerBrush = SufniThemes.Fallback.SignalRow.DividerBetweenRoots.ToBrush();
    private IDisposable? themeVariantSubscription;
    private bool isDragging;
    private bool canResizeTargetRow;
    private double dragStartY;
    private double dragStartHeight;

    internal SignalRow? TargetRow { get; set; }
    internal bool CanResizeTargetRow
    {
        get => canResizeTargetRow;
        set
        {
            canResizeTargetRow = value;
            IsHitTestVisible = value;
        }
    }

    public SignalBaseRowDivider()
    {
        Height = SignalRowsPanel.BaseRowDividerHeight;
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
                dividerBrush = SufniThemes.FromVariant(ActualThemeVariant).SignalRow.DividerBetweenRoots.ToBrush();
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
        TargetRow.ManualGroupHeightRatio = null;
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
        this.FindAncestorOfType<SignalRowsRoot>()?.CommitManualRowSizePreferences();
    }

    private void EndDrag(IPointer pointer)
    {
        isDragging = false;
        pointer.Capture(null);
        this.FindAncestorOfType<SignalRowsRoot>()?.CommitManualRowSizePreferences();
    }

    private void InvalidateOwnerMeasure()
    {
        this.FindAncestorOfType<SignalRowsPanel>()?.InvalidateMeasure();
    }

    internal void ResetTargetRowToPreferredHeight()
    {
        if (!CanResizeTargetRow || TargetRow is null)
        {
            return;
        }

        TargetRow.ManualGroupHeight = TargetRow.GetPreferredGroupHeight();
        TargetRow.ManualGroupHeightRatio = null;
        InvalidateOwnerMeasure();
    }
}
