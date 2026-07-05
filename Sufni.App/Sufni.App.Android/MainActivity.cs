using Android.App;
using Android.Content.PM;
using Avalonia.Android;
using Android.Views;

namespace Sufni.App.Android
{
    [Activity(
        Label = "Sufni Telemetry",
        Theme = "@style/MyTheme.NoActionBar",
        Icon = "@mipmap/ic_launcher",
        MainLauncher = true,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
    public class MainActivity : AvaloniaMainActivity
    {
        internal static Window? CurrentWindow { get; private set; }

        protected override void OnResume()
        {
            base.OnResume();
            CurrentWindow = Window;
        }

        protected override void OnPause()
        {
            if (CurrentWindow == Window)
            {
                CurrentWindow = null;
            }

            base.OnPause();
        }
    }
}
