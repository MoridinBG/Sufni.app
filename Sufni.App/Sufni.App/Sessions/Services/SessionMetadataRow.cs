using System.Text.Json.Serialization;
using SQLite;
using Sufni.App.Sessions.Models;

namespace Sufni.App.Sessions.Services;

/// <summary>Metadata projection with database-derived BLOB availability and no payload read.</summary>
internal sealed class SessionMetadataRow : Session
{
    [Column("processed_data_available")]
    public bool ProcessedDataAvailable { get; set; }

    [Ignore, JsonIgnore]
    public override bool HasProcessedData => ProcessedDataAvailable;
}
