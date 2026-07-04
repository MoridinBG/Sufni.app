using Avalonia.Controls;

namespace Sufni.App.Shell.Views
{
    public partial class MainView : UserControl
    {
        private IMobileNavigationPageHost? navigationPageHost;
        private bool isNavigationPageAttached;

        public MainView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => SyncShellHost();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        public void SetNavigationPageHost(IMobileNavigationPageHost pageHost)
        {
            navigationPageHost = pageHost;
            AttachNavigationPageIfNeeded();
        }

        private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            SyncShellHost();
        }

        private void OnUnloaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            DetachNavigationPageIfNeeded();
        }

        private void SyncShellHost()
        {
            var useNativeNavigation = UsesNativeNavigation();
            RootNavigationPage.IsVisible = useNativeNavigation;
            WorkspaceRootHost.IsVisible = !useNativeNavigation;
            WorkspaceRootHost.Content = useNativeNavigation ? null : DataContext;

            if (useNativeNavigation)
            {
                AttachNavigationPageIfNeeded();
                return;
            }

            DetachNavigationPageIfNeeded();
        }

        private static bool UsesNativeNavigation() => false;

        private void AttachNavigationPageIfNeeded()
        {
            if (!IsLoaded || isNavigationPageAttached || !UsesNativeNavigation())
            {
                return;
            }

            navigationPageHost?.Attach(RootNavigationPage);
            isNavigationPageAttached = navigationPageHost is not null;
        }

        private void DetachNavigationPageIfNeeded()
        {
            if (!isNavigationPageAttached)
            {
                return;
            }

            navigationPageHost?.Detach(RootNavigationPage);
            isNavigationPageAttached = false;
        }
    }
}
