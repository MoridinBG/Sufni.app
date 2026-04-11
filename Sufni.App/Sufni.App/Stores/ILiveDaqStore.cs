using System;
using DynamicData;

namespace Sufni.App.Stores;

/// <summary>
/// Read-only runtime view of the live DAQ catalog.
/// </summary>
public interface ILiveDaqStore
{
    IObservable<IChangeSet<LiveDaqSnapshot, string>> Connect();

    LiveDaqSnapshot? Get(string identityKey);
}