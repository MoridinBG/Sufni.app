using System;
using System.ComponentModel;
using Avalonia.Controls;

using Sufni.App.Segments.ViewModels;
namespace Sufni.App.Segments.DesktopViews;

// The flyout opens and closes the picker; the picker closing (after a
// selection) hides the flyout. The panel holds the picker as its own data
// context because the flyout drops the inherited one while it is hidden.
public partial class SegmentPickerFlyoutButtonView : UserControl
{
    private SegmentPickerViewModel? picker;

    public SegmentPickerFlyoutButtonView()
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
        PickerPanel.DataContext = picker;
        if (picker is not null)
        {
            picker.PropertyChanged += OnPickerPropertyChanged;
        }
    }

    private void SegmentsFlyout_OnOpened(object? sender, EventArgs e)
    {
        if (picker?.OpenCommand.CanExecute(null) == true)
        {
            picker.OpenCommand.Execute(null);
        }
    }

    private void SegmentsFlyout_OnClosed(object? sender, EventArgs e)
    {
        if (picker?.IsOpen == true)
        {
            picker.CloseCommand.Execute(null);
        }
    }

    private void OnPickerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SegmentPickerViewModel.IsOpen) && picker?.IsOpen == false)
        {
            SegmentsButton.Flyout?.Hide();
        }
    }
}
