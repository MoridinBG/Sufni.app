using System;
using Android.Content;
using Android.Net.Wifi;
using Serilog;
using Sufni.App.Infrastructure;
using Application = Android.App.Application;

namespace Sufni.App.Android;

public sealed class AndroidServiceDiscovery : IServiceDiscovery
{
    private static readonly ILogger logger = Log.ForContext<AndroidServiceDiscovery>();

    private readonly SocketServiceDiscovery inner = new();
    private readonly object gate = new();
    private WifiManager.MulticastLock? multicastLock;
    private bool browseStarted;

    public event EventHandler<ServiceAnnouncementEventArgs>? ServiceAdded
    {
        add => inner.ServiceAdded += value;
        remove => inner.ServiceAdded -= value;
    }

    public event EventHandler<ServiceAnnouncementEventArgs>? ServiceRemoved
    {
        add => inner.ServiceRemoved += value;
        remove => inner.ServiceRemoved -= value;
    }

    public void StartBrowse(string type)
    {
        lock (gate)
        {
            if (browseStarted)
            {
                return;
            }

            AcquireMulticastLock();
            try
            {
                inner.StartBrowse(type);
                browseStarted = true;
            }
            catch
            {
                ReleaseMulticastLock();
                throw;
            }
        }
    }

    public void StopBrowse()
    {
        lock (gate)
        {
            if (!browseStarted)
            {
                return;
            }

            try
            {
                inner.StopBrowse();
            }
            finally
            {
                browseStarted = false;
                ReleaseMulticastLock();
            }
        }
    }

    private void AcquireMulticastLock()
    {
        try
        {
            var wifiManager = Application.Context.GetSystemService(Context.WifiService) as WifiManager;
            if (wifiManager is null)
            {
                logger.Warning("Android Wi-Fi manager unavailable; service discovery will browse without a multicast lock");
                return;
            }

            var browseLock = wifiManager.CreateMulticastLock("SufniServiceDiscovery");
            if (browseLock is null)
            {
                logger.Warning("Android Wi-Fi manager did not create a multicast lock; service discovery may not receive mDNS packets");
                return;
            }

            browseLock.SetReferenceCounted(false);
            browseLock.Acquire();
            multicastLock = browseLock;
            logger.Verbose("Acquired Android multicast lock for service discovery");
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Could not acquire Android multicast lock; service discovery may not receive mDNS packets");
            multicastLock = null;
        }
    }

    private void ReleaseMulticastLock()
    {
        try
        {
            if (multicastLock?.IsHeld == true)
            {
                multicastLock.Release();
                logger.Verbose("Released Android multicast lock for service discovery");
            }
        }
        catch (Exception ex)
        {
            logger.Verbose(ex, "Releasing Android multicast lock failed");
        }
        finally
        {
            multicastLock?.Dispose();
            multicastLock = null;
        }
    }
}
