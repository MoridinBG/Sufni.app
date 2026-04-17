using System;
using System.Collections.Generic;

namespace Sufni.App.Services;

public sealed record LiveDaqCatalogEntry(
    string IdentityKey,
    string DisplayName,
    string? BoardId,
    string Host,
    int Port)
{
    public string Endpoint => $"{Host}:{Port}";
}

// Publishes the current set of discovered live DAQ endpoints independently of the
// import-oriented telemetry datastore surface.
public interface ILiveDaqCatalogService
{
    // Returns a browse lease for one consumer. Dispose it to release this
    // caller's browse interest.
    IDisposable AcquireBrowse();

    // Replays the current discovery snapshot and then pushes normalized updates.
    IObservable<IReadOnlyList<LiveDaqCatalogEntry>> Observe();
}