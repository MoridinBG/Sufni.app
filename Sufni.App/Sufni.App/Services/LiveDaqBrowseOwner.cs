using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Sufni.App.Services;

// Shares one keyed browse session across multiple feature consumers by using
// disposable leases rather than global start/stop ownership.
internal sealed class LiveDaqBrowseOwner([FromKeyedServices("gosst")] IServiceDiscovery serviceDiscovery) : ILiveDaqBrowseOwner
{
    private const string ServiceType = "_gosst._tcp";
    private static readonly ILogger logger = Log.ForContext<LiveDaqBrowseOwner>();

    private readonly object gate = new();
    private int leaseCount;
    private bool isBrowseStarted;

    public IDisposable AcquireBrowse()
    {
        lock (gate)
        {
            if (!isBrowseStarted)
            {
                logger.Verbose("Starting live DAQ browse");
                try
                {
                    serviceDiscovery.StartBrowse(ServiceType);
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Live DAQ browse start failed; attempting compensating stop");
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
                logger.Verbose("Stopping live DAQ browse");
                isBrowseStarted = false;
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
