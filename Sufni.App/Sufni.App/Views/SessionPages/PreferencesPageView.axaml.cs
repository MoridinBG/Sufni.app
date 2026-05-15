using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Sufni.App.ViewModels.SessionPages;

namespace Sufni.App.Views.SessionPages;

public partial class PreferencesPageView : UserControl
{
    public PreferencesPageView()
    {
        InitializeComponent();
        VelocityFilterWindowSlider.AddHandler<PointerReleasedEventArgs>(
            InputElement.PointerReleasedEvent,
            VelocityFilterWindowSliderPointerReleased,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        VelocityFilterWindowSlider.PointerCaptureLost += VelocityFilterWindowSliderPointerCaptureLost;
    }

    private void VelocityFilterWindowSliderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        CommitVelocityFilterWindowChange();
    }

    private void VelocityFilterWindowSliderPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        CommitVelocityFilterWindowChange();
    }

    private void CommitVelocityFilterWindowChange()
    {
        if (DataContext is PreferencesPageViewModel viewModel)
        {
            Dispatcher.UIThread.Post(viewModel.CommitProcessingPreferenceChange);
        }
    }
}
