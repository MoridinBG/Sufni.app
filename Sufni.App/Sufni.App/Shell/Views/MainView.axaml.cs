using Avalonia.Controls;

namespace Sufni.App.Shell.Views
{
    public partial class MainView : UserControl
    {
        private IMobileNavigationPageHost? navigationPageHost;

        public MainView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        public void SetNavigationPageHost(IMobileNavigationPageHost pageHost)
        {
            navigationPageHost = pageHost;
            if (IsLoaded)
            {
                pageHost.Attach(RootNavigationPage);
            }
        }

        private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            navigationPageHost?.Attach(RootNavigationPage);
        }

        private void OnUnloaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            navigationPageHost?.Detach(RootNavigationPage);
        }
    }
}
