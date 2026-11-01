using System.Text.Json.Serialization;
using SQLite;

namespace Sufni.App.SyncAndPairing.Models;

public sealed class SynchronizationDeletion : Synchronizable
{
    [Column("entity_table")]
    [JsonPropertyName("entity_table")]
    public string EntityTable { get; set; } = string.Empty;

    public static SynchronizationDeletion From(Synchronizable row, string table) => new()
    {
        EntityTable = table, Id = row.Id, Version = row.Version, Updated = row.Updated,
        UpdatedBy = row.UpdatedBy, Deleted = row.Deleted,
        LocalSequence = row.LocalSequence, SyncSequence = row.SyncSequence
    };
}
