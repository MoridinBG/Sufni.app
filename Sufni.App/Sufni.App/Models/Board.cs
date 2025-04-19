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

    public Board(string id, Guid? setupId)
    {
        Id = id;
        SetupId = setupId;
    }

    [JsonPropertyName("id")]
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("setup_id")]
    [Column("setup_id")]
    public Guid? SetupId { get; set; }
}
