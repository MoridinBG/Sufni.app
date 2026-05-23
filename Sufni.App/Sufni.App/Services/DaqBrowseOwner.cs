using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Sufni.App.Services;

// Shares one keyed DAQ browse session across multiple feature consumers by using
// disposable leases rather than global start/stop ownership.
internal sealed class DaqBrowseOwner([FromKeyedServices("gosst")] IServiceDiscovery serviceDiscovery) : IDaqBrowseOwner
{
    private const string ServiceType = "_gosst._tcp";
    private static readonly ILogger logger = Log.ForContext<DaqBrowseOwner>();

    private readonly object gate = new();
    private int leaseCount;
    private bool isBrowseStarted;

    public IDisposable AcquireBrowse()
    {
        lock (gate)
        {
            if (!isBrowseStarted)
            {
                logger.Verbose("Starting DAQ browse");
                try
                {
                    serviceDiscovery.StartBrowse(ServiceType);
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "DAQ browse start failed; attempting compensating stop");
                    try
                    {
                        serviceDiscovery.StopBrowse();
                    }
                    catch (Exception stopEx)
                    {
                        logger.Verbose(stopEx, "Compensating stop after failed browse start also threw");
                    }

                    throw;
                }

                isBrowseStarted = true;
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
            if (leaseCount == 0 && isBrowseStarted)
            {
                logger.Verbose("Stopping DAQ browse");
                isBrowseStarted = false;
                serviceDiscovery.StopBrowse();
            }
        }
    }

    private sealed class BrowseLease(DaqBrowseOwner owner) : IDisposable
    {
        private DaqBrowseOwner? owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref owner, null)?.Release();
        }
    }
}
