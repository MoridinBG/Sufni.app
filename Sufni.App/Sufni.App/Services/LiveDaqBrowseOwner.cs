using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Sufni.App.Services;

// Shares one keyed browse session across multiple feature consumers by using
// disposable leases rather than global start/stop ownership.
internal sealed class LiveDaqBrowseOwner([FromKeyedServices("gosst")] IServiceDiscovery serviceDiscovery) : ILiveDaqBrowseOwner
{
    private const string ServiceType = "_gosst._tcp";

    private readonly object gate = new();
    private int leaseCount;

    public IDisposable AcquireBrowse()
    {
        lock (gate)
        {
            if (leaseCount == 0)
            {
                serviceDiscovery.StartBrowse(ServiceType);
            }

            leaseCount++;
        }

        return new BrowseLease(this);
    }

    private void Release()
    {
        lock (gate)
        {
            if (leaseCount == 0)
            {
                return;
            }

            leaseCount--;
            if (leaseCount == 0)
            {
                serviceDiscovery.StopBrowse();
            }
        }
    }

    private sealed class BrowseLease(LiveDaqBrowseOwner owner) : IDisposable
    {
        private LiveDaqBrowseOwner? owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref owner, null)?.Release();
        }
    }
}