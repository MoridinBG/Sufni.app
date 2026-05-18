using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Models;

// Represents one recording exposed by any acquisition source. Implementations
// may be backed by a mounted drive, a network DAQ, or a user-selected folder.
public interface ITelemetryFile
{
    public string Name { get; set; }
    public string FileName { get; }
    // Null means the importer has not made a decision yet; true/false is the
    // user's or auto-selection state for the next import operation.
    public bool? ShouldBeImported { get; set; }
    public bool Imported { get; set; }
    public string Description { get; set; }
    public byte Version { get; }
    public DateTime StartTime { get; }
    public string Duration { get; }
    public string? MalformedMessage { get; }
    public bool CanImport { get; }
    public bool HasUnknown { get; }

    // Returns the immutable raw source bytes and metadata used for durable
    // recorded-session reprocessing.
    public Task<TelemetryFileSource> ReadSourceAsync(CancellationToken cancellationToken = default);
    public Task OnImported();
    public Task OnTrashed();
}
