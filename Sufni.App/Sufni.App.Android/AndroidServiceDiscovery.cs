using System;
using Serilog;
using Sufni.App.Infrastructure;

namespace Sufni.App.Android;

internal interface IAndroidMulticastLock : IDisposable
{
    bool IsHeld { get; }
    void Acquire();
    void Release();
}

internal interface IAndroidMulticastLockFactory
{
    IAndroidMulticastLock? Create();
}

internal interface IAndroidLocalNetworkPermission
{
    bool IsGranted { get; }
    event Action? Changed;
}

internal sealed class AndroidServiceDiscovery(
    IServiceDiscovery inner,
    IAndroidMulticastLockFactory multicastLockFactory,
    IAndroidLocalNetworkPermission permission) : IServiceDiscovery
{
    private static readonly ILogger logger = Log.ForContext<AndroidServiceDiscovery>();

    private readonly object gate = new();
    private IAndroidMulticastLock? multicastLock;
    private string? requestedType;
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
            if (requestedType is not null) return;
            requestedType = type;
            permission.Changed += OnPermissionChanged;
            try
            {
                UpdateBrowse();
            }
            catch
            {
                permission.Changed -= OnPermissionChanged;
                requestedType = null;
                throw;
            }
        }
    }

    public void StopBrowse()
    {
        lock (gate)
        {
            permission.Changed -= OnPermissionChanged;
            requestedType = null;
            UpdateBrowse();
        }
    }

    private void OnPermissionChanged()
    {
        lock (gate)
        {
            try
            {
                UpdateBrowse();
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Could not update Android discovery for {ServiceType} after local network permission changed", requestedType);
            }
        }
    }

    private void UpdateBrowse()
    {
        if (requestedType is { } type && permission.IsGranted)
        {
            if (browseStarted) return;
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
        else if (browseStarted)
        {
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
        IAndroidMulticastLock? candidate = null;
        try
        {
            candidate = multicastLockFactory.Create();
            if (candidate is null)
            {
                logger.Warning("Android multicast lock unavailable; service discovery may not receive mDNS packets");
                return;
            }

            candidate.Acquire();
            multicastLock = candidate;
            candidate = null;
            logger.Verbose("Acquired Android multicast lock for service discovery");
        }
        catch (Exception ex)
        {
            candidate?.Dispose();
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
