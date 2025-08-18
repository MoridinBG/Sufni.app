using System;
using System.Text.Json.Serialization;
using SQLite;

namespace Sufni.App.Models;

[Table("board")]
public class Board : Synchronizable
{
    // Just to satisfy sql-net-pcl's parameterless constructor requirement
    // Uninitialized non-nullable property warnings are suppressed with null! initializer.
    public Board() { }

    public Board(Guid id, Guid? setupId)
    {
        Id = id;
        SetupId = setupId;
    }

    [JsonPropertyName("setup_id")]
    [Column("setup_id")]
    public Guid? SetupId { get; set; }
}
