using Android.Content;
using Android.Net.Wifi;
using Application = Android.App.Application;

namespace Sufni.App.Android;

internal sealed class AndroidMulticastLockFactory : IAndroidMulticastLockFactory
{
    public IAndroidMulticastLock? Create()
    {
        var wifiManager = Application.Context.GetSystemService(Context.WifiService) as WifiManager;
        if (wifiManager is null)
        {
            return null;
        }

        var nativeLock = wifiManager.CreateMulticastLock("SufniServiceDiscovery");
        if (nativeLock is null)
        {
            return null;
        }

        try
        {
            nativeLock.SetReferenceCounted(false);
            return new AndroidMulticastLock(nativeLock);
        }
        catch
        {
            nativeLock.Dispose();
            throw;
        }
    }

    private sealed class AndroidMulticastLock(
        WifiManager.MulticastLock nativeLock) : IAndroidMulticastLock
    {
        public bool IsHeld => nativeLock.IsHeld;

        public void Acquire()
        {
            nativeLock.Acquire();
        }

        public void Release()
        {
            nativeLock.Release();
        }

        public void Dispose()
        {
            nativeLock.Dispose();
        }
    }
}
