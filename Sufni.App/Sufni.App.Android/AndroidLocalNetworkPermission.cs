using System;
using Android.App;
using Android.Content.PM;

namespace Sufni.App.Android;

internal sealed class AndroidLocalNetworkPermission : IAndroidLocalNetworkPermission
{
    internal const string Name = "android.permission.ACCESS_LOCAL_NETWORK";

    public bool IsGranted => !OperatingSystem.IsAndroidVersionAtLeast(37)
        || Application.Context.CheckSelfPermission(Name) == Permission.Granted;

    public event Action? Changed;

    public void Refresh() => Changed?.Invoke();
}
