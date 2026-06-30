using SQLite;

namespace Sufni.App.Extensibility.Database;

[Table("extension_schema_version")]
internal sealed class ExtensionSchemaVersion
{
    [PrimaryKey]
    [Column("extension_id")]
    public string ExtensionId { get; set; } = "";

    [Column("version")]
    public int Version { get; set; }
}
