using System;
using SQLite;

namespace Sufni.App.Models;

[Table("session_recording_source")]
public sealed class RecordedSessionSource
{
    private RecordedSessionSourceKind sourceKind;

    [PrimaryKey]
    [Column("session_id")]
    public Guid SessionId { get; set; }

    [Column("source_kind"), NotNull]
    public string SourceKindValue
    {
        get => SourceKind.ToStorageValue();
        set => SourceKind = RecordedSessionSourceKindExtensions.FromStorageValue(value);
    }

    [Ignore]
    public RecordedSessionSourceKind SourceKind
    {
        get => sourceKind;
        set => sourceKind = value;
    }

    [Column("source_name"), NotNull]
    public string SourceName { get; set; } = null!;

    [Column("schema_version"), NotNull]
    public int SchemaVersion { get; set; }

    [Column("source_hash"), NotNull]
    public string SourceHash { get; set; } = null!;

    [Column("payload"), NotNull]
    public byte[] Payload { get; set; } = [];
}
