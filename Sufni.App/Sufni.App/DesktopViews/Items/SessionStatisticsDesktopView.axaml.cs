using Avalonia.Controls;

namespace Sufni.App.DesktopViews.Items;

public partial class SessionStatisticsDesktopView : UserControl
{
    public SessionStatisticsDesktopView()
    {
        InitializeComponent();
        
        // Set all pages visible at first, so that their plots are populated
        SpringRate.IsVisible = true;
        Damping.IsVisible = true;
        Balance.IsVisible = true;
        
        // When the control is loaded, set Damping and Balance pages invisible.
        TabControl.Loaded += (_, _) =>
        {
            Damping.IsVisible = false;
            Balance.IsVisible = false;
        };
        
        // Handle tab switches
        TabControl.SelectionChanged += (_, _) =>
        {
            switch (TabControl.SelectedIndex)
            {
                case 0:
                    SpringRate.IsVisible = true;
                    Damping.IsVisible = false;
                    Balance.IsVisible = false;
                    break;
                case 1:
                    SpringRate.IsVisible = false;
                    Damping.IsVisible = true;
                    Balance.IsVisible = false;
                    break;
                case 2:
                    SpringRate.IsVisible = false;
                    Damping.IsVisible = false;
                    Balance.IsVisible = true;
                    break;
            }
        };
    }
}