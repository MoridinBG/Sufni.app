using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sufni.App.Models;

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

public interface ITelemetryDataStore
{
    public string Name { get; }
    public string? BoardId { get; }
    public Task<List<ITelemetryFile>> GetFiles();
}