using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using System;
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
        private const int LocalNetworkRequest = 0x5355;
        private bool requestedLocalNetwork;
        private AndroidLocalNetworkPermission LocalNetwork => ((MainApplication)Application!).LocalNetworkPermission;

        internal static Window? CurrentWindow { get; private set; }

        protected override void OnResume()
        {
            base.OnResume();
            CurrentWindow = Window;
            LocalNetwork.Refresh();
            if (OperatingSystem.IsAndroidVersionAtLeast(37) && !LocalNetwork.IsGranted && !requestedLocalNetwork)
            {
                requestedLocalNetwork = true;
                RequestPermissions([AndroidLocalNetworkPermission.Name], LocalNetworkRequest);
            }
        }

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            requestedLocalNetwork = savedInstanceState?.GetBoolean(nameof(requestedLocalNetwork)) ?? false;
            base.OnCreate(savedInstanceState);
        }

        protected override void OnSaveInstanceState(Bundle outState)
        {
            outState.PutBoolean(nameof(requestedLocalNetwork), requestedLocalNetwork);
            base.OnSaveInstanceState(outState);
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            if (requestCode != LocalNetworkRequest) return;
            LocalNetwork.Refresh();
            if (LocalNetwork.IsGranted || grantResults.Length == 0) return;

            new AlertDialog.Builder(this)
                .SetMessage("Nearby devices access is needed to find DAQs and sync with a desktop. You can enable it in app settings or continue offline.")
                .SetPositiveButton("App settings", (_, _) => StartActivity(new Intent(
                    Settings.ActionApplicationDetailsSettings, global::Android.Net.Uri.Parse($"package:{PackageName}"))))
                .SetNegativeButton("Continue offline", (_, _) => { })
                .Show();
        }

        protected override async void OnPause()
        {
            if (CurrentWindow == Window)
            {
                CurrentWindow = null;
            }

            base.OnPause();
            if (global::Sufni.App.App.Current is { } app) await app.OnBackgroundAsync();
        }
    }
}
