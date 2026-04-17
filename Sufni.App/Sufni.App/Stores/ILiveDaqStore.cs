using System;
using DynamicData;

namespace Sufni.App.Stores;

/// <summary>
/// Read-only runtime view of the live DAQ catalog.
/// </summary>
public interface ILiveDaqStore
{
    // Streams runtime row changes for the live DAQ catalog.
    IObservable<IChangeSet<LiveDaqSnapshot, string>> Connect();

    // Returns null when the requested identity is not present in the runtime catalog.
    LiveDaqSnapshot? Get(string identityKey);
}