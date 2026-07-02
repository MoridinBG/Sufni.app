using System;
using System.Collections.Generic;
using Sufni.App.LiveDaq.Services.LiveStreaming;

namespace Sufni.App.LiveDaq.Services;

public sealed record LiveDaqCatalogEntry(
    string IdentityKey,
    string DisplayName,
    string? BoardId,
    string Host,
    int Port,
    LiveProtocolVersion ProtocolVersion = LiveProtocolVersion.V2)
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
