using Avalonia.Controls;

namespace Sufni.App.DesktopViews.Items;

public partial class SessionStatisticsDesktopView : UserControl
{
    public SessionStatisticsDesktopView()
    {
        InitializeComponent();

        // Set all pages visible at first, so that their plots are populated
        SpringRate.IsVisible = true;
        Strokes.IsVisible = true;
        Damping.IsVisible = true;
        Balance.IsVisible = true;
        Vibration.IsVisible = true;
        Analysis.IsVisible = true;

        // When the control is loaded, leave only the selected Spring rate page visible.
        TabControl.Loaded += (_, _) =>
        {
            Strokes.IsVisible = false;
            Damping.IsVisible = false;
            Balance.IsVisible = false;
            Vibration.IsVisible = false;
            Analysis.IsVisible = false;
        };

        // Handle tab switches
        TabControl.SelectionChanged += (_, _) =>
        {
            switch (TabControl.SelectedIndex)
            {
                case 0:
                    SpringRate.IsVisible = true;
                    Strokes.IsVisible = false;
                    Damping.IsVisible = false;
                    Balance.IsVisible = false;
                    Vibration.IsVisible = false;
                    Analysis.IsVisible = false;
                    break;
                case 1:
                    SpringRate.IsVisible = false;
                    Strokes.IsVisible = true;
                    Damping.IsVisible = false;
                    Balance.IsVisible = false;
                    Vibration.IsVisible = false;
                    Analysis.IsVisible = false;
                    break;
                case 2:
                    SpringRate.IsVisible = false;
                    Strokes.IsVisible = false;
                    Damping.IsVisible = true;
                    Balance.IsVisible = false;
                    Vibration.IsVisible = false;
                    Analysis.IsVisible = false;
                    break;
                case 3:
                    SpringRate.IsVisible = false;
                    Strokes.IsVisible = false;
                    Damping.IsVisible = false;
                    Balance.IsVisible = true;
                    Vibration.IsVisible = false;
                    Analysis.IsVisible = false;
                    break;
                case 4:
                    SpringRate.IsVisible = false;
                    Strokes.IsVisible = false;
                    Damping.IsVisible = false;
                    Balance.IsVisible = false;
                    Vibration.IsVisible = true;
                    Analysis.IsVisible = false;
                    break;
                case 5:
                    SpringRate.IsVisible = false;
                    Strokes.IsVisible = false;
                    Damping.IsVisible = false;
                    Balance.IsVisible = false;
                    Vibration.IsVisible = false;
                    Analysis.IsVisible = true;
                    break;
            }
        };
    }
}