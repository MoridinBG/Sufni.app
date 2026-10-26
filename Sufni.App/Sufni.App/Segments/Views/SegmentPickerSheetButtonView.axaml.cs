using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;

using Sufni.App.Segments.ViewModels;
namespace Sufni.App.Segments.Views;

// Shows the picker as a bottom sheet anchored to the window and sized to it.
// The sheet follows the picker's open state; light dismiss closes the picker.
public partial class SegmentPickerSheetButtonView : UserControl
{
    private SegmentPickerViewModel? picker;
    private Control? placementTarget;

    public SegmentPickerSheetButtonView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (picker is not null)
        {
            picker.PropertyChanged -= OnPickerPropertyChanged;
        }

        picker = DataContext as SegmentPickerViewModel;
        if (picker is not null)
        {
            picker.PropertyChanged += OnPickerPropertyChanged;
        }

        SegmentsSheetPopup.IsOpen = picker?.IsOpen == true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SetPlacementTarget(TopLevel.GetTopLevel(this) ?? (Control)this);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SetPlacementTarget(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void SegmentsSheetPopup_OnOpened(object? sender, EventArgs e) => ConfigureSheetBounds();

    private void SegmentsSheetPopup_OnClosed(object? sender, EventArgs e)
    {
        if (picker?.IsOpen == true)
        {
            picker.CloseCommand.Execute(null);
        }
    }

    private void OnPickerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SegmentPickerViewModel.IsOpen))
        {
            SegmentsSheetPopup.IsOpen = picker?.IsOpen == true;
        }
    }

    private void SetPlacementTarget(Control? target)
    {
        if (placementTarget is not null)
        {
            placementTarget.PropertyChanged -= OnPlacementTargetPropertyChanged;
        }

        placementTarget = target;
        SegmentsSheetPopup.PlacementTarget = target ?? this;
        if (placementTarget is not null)
        {
            placementTarget.PropertyChanged += OnPlacementTargetPropertyChanged;
            ConfigureSheetBounds();
        }
    }

    private void OnPlacementTargetPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty)
        {
            ConfigureSheetBounds();
        }
    }

    private void ConfigureSheetBounds()
    {
        var bounds = placementTarget?.Bounds ?? Bounds;
        var width = IsPositiveFinite(bounds.Width) ? bounds.Width : 360;
        var height = IsPositiveFinite(bounds.Height) ? bounds.Height : 640;

        SheetFrame.Width = Math.Min(width, 430);
        SheetFrame.MaxHeight = Math.Max(280, height * 0.78);
    }

    private static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0;
}
