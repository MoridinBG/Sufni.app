using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Sufni.App.LiveDaq.Services.LiveStreaming;

namespace Sufni.App.Acquisition.Models;

internal class TelemetryDataStoreComparer : IEqualityComparer<ITelemetryDataStore>
{
    public bool Equals(ITelemetryDataStore? ds1, ITelemetryDataStore? ds2)
    {
        if (ReferenceEquals(ds1, ds2))
            return true;

        if (ds1 is null || ds2 is null)
            return false;

        return ds1.Name == ds2.Name;
    }

    public int GetHashCode(ITelemetryDataStore ds) => ds.Name.GetHashCode();
}

// A single browseable acquisition source. The UI treats all providers through
// this contract, whether they are physical DAQ storage or app-managed folders.
public interface ITelemetryDataStore
{
    public string Name { get; }
    public Guid? BoardId { get; }
    // Local sources are always idle; network sources observe the shared DAQ port owner.
    public IObservable<DaqTransportHolder?> ObserveTransport();
    public Task<TelemetryFileListing> GetFiles(CancellationToken cancellationToken = default);
    public Task<TelemetryImportBatchAcquisition> OpenImportBatchAsync(Guid batchId, CancellationToken cancellationToken = default);
}
