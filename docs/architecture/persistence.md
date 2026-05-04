# Persistence & Serialization

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers the SQLite schema, the database service, soft deletes, and conflict resolution semantics shared with [cross-device synchronization](sync.md).

## Schema

```mermaid
erDiagram
    session ||--o| setup : "setup_id"
    session ||--o| track : "full_track_id"
    session ||--o| session_cache : "session_id"
    session ||--o| session_recording_source : "session_id"
    setup }o--|| bike : "bike_id"
    board ||--o| setup : "setup_id"

    session {
        text id PK
        text name
        text description
        int timestamp
        text setup_id FK
        blob data
        int has_data
        text track
        text full_track_id FK
        text session_processing_fingerprint
        text front_springrate
        text rear_springrate
        int front_hsc
        int front_lsc
        int front_lsr
        int front_hsr
        int rear_hsc
        int rear_lsc
        int rear_lsr
        int rear_hsr
        int updated
        int client_updated
        int deleted
    }

    session_recording_source {
        text session_id PK
        text source_kind
        text source_name
        int schema_version
        text source_hash
        blob payload
    }

    bike {
        text id PK
        text name
        real head_angle
        real fork_stroke
        real shock_stroke
        int rear_suspension_kind
        text linkage
        text leverage_ratio
        blob image
        real pixels_to_millimeters
        real front_wheel_diameter
        real rear_wheel_diameter
        int front_wheel_rim_size
        real front_wheel_tire_width
        int rear_wheel_rim_size
        real rear_wheel_tire_width
        real image_rotation_degrees
        int updated
        int client_updated
        int deleted
    }

    setup {
        text id PK
        text name
        text bike_id FK
        text front_sensor_configuration
        text rear_sensor_configuration
        int updated
        int client_updated
        int deleted
    }

    board {
        text id PK
        text setup_id FK
        int updated
        int client_updated
        int deleted
    }

    track {
        text id PK
        text points
        int start_time
        int end_time
        int updated
        int client_updated
        int deleted
    }

    session_cache {
        text session_id PK
        text front_travel_histogram
        text rear_travel_histogram
        text front_velocity_histogram
        text rear_velocity_histogram
        text compression_balance
        text rebound_balance
        real front_hsc_percentage
        real front_lsc_percentage
        real front_lsr_percentage
        real front_hsr_percentage
        real rear_hsc_percentage
        real rear_lsc_percentage
        real rear_lsr_percentage
        real rear_hsr_percentage
    }

    sync {
        text server_url PK
        int last_sync_time
    }

    paired_device {
        text device_id PK
        text display_name
        text token
        int expires
    }
```

## Database Service

`SqLiteDatabaseService` (`Sufni.App/Sufni.App/Services/SQLiteDatabaseService.cs`) implements `IDatabaseService` using sqlite-net-pcl with WAL mode. The database path uses `Environment.SpecialFolder.LocalApplicationData` + `Sufni.App/sst.db`.

Generic operations on any `Synchronizable` subclass:

- `GetAllAsync<T>()` — returns all records where `Deleted == null`
- `GetChangedAsync<T>(long since)` — returns records where `Updated > since` OR (`Deleted != null` AND `Deleted > since`)
- `PutAsync<T>(item)` — upsert. Stamps `Updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds()` and clears `Deleted` (resurrecting any tombstoned row with the same id).
- `DeleteAsync<T>(id)` — sets `Deleted` timestamp (soft delete); idempotent — leaves the existing tombstone in place if the row is already deleted.

Session-specific operations split metadata, processed data, and recording source handling:

- `PutSessionAsync()` — updates session metadata columns, the processing fingerprint, and stamps `Updated`/`Deleted` like `PutAsync`. The `data` blob and `track` cache are written via `COALESCE(?, existing)`: if `session.ProcessedData` or `session.Track` is null, the existing derived payload is preserved.
- `PutProcessedSessionAsync(session, newFullTrack, source)` — persists a processed session in one explicit transaction. It writes a new full `Track` when supplied, stamps `session.full_track_id`, writes all session metadata plus `data` and `session_processing_fingerprint`, and optionally inserts/replaces the matching `RecordedSessionSource`. If any write fails, the session, generated track, and source write roll back together.
- `PatchSessionPsstAsync(id, bytes)` — updates only the `data` column (and sets `has_data = 1`)
- `PatchSessionTrackAsync(id, points)` — updates the cached session-window `track` JSON and stamps `updated`
- `GetSessionPsstAsync(id)` — deserializes MessagePack blob to `TelemetryData`
- `GetSessionRawPsstAsync(id)` — returns the raw MessagePack blob for sync transfer
- `GetRecordedSessionSourcesAsync()` / `GetRecordedSessionSourceAsync(id)` — load recorded-source rows or one full source payload
- `GetSessionIdsMissingRecordedSourceAsync()` — returns non-deleted session ids that do not have a source row
- `PutRecordedSessionSourceAsync(source)` / `DeleteRecordedSessionSourceAsync(sessionId)` — insert/replace or remove a recorded source outside the processed-session transaction, used by source sync and source-store writes

## Soft Delete

`Synchronizable` entities (`Sufni.App/Sufni.App/Models/Synchronizable.cs`) — `bike`, `setup`, `session`, `board`, `track` — carry `Updated` (server timestamp), `ClientUpdated` (local timestamp), and nullable `Deleted` (soft delete timestamp). `paired_device`, `session_cache`, `session_recording_source`, and `sync` are not `Synchronizable` and have their own lifecycles.

On database initialization, the `Cleanup()` pass permanently removes:

- `Synchronizable` rows with `Deleted` older than 1 day
- Orphaned `session_cache` rows whose parent session is past that 1-day grace window
- `session_recording_source` rows for purged sessions, plus any source row without a parent session
- `paired_device` rows where `Expires < DateTime.UtcNow`

## Conflict Resolution

`MergeAsync<T>()` is invoked per entity inside the `MergeAllAsync(SynchronizationData)` transaction. It compares against a derived "content version" — `existing.ClientUpdated` if set, otherwise `existing.Updated` — so locally-authored rows that have not yet round-tripped through a sync still compare correctly.

The merge cases, in evaluation order:

1. **New entity** (not in local DB): persist with `ClientUpdated = entity.Updated`, `Updated = now`. Insert.
2. **Existing already locally deleted**: keep the local tombstone; if the remote tombstone is later (`entity.Deleted > existing.Deleted`), advance `existing.Deleted` to the remote value. Always bump `existing.Updated = now`. (No content is ever revived once locally deleted.)
3. **Remote delete with `entity.Deleted > existingContentVersion`**: accept the delete — set `existing.Deleted = entity.Deleted`, `existing.Updated = now`. (Note: `Updated` is set to *now*, not to the remote's `Updated`.)
4. **Stale remote delete** (`entity.Deleted <= existingContentVersion`): ignore the delete; only bump `existing.Updated = now`.
5. **Local wins** (`existingContentVersion > entity.Updated`): keep local content; bump `existing.Updated = now`.
6. **Remote wins** (otherwise): persist remote content with `ClientUpdated = entity.Updated`, `Updated = now`. Update.

This gives local client changes precedence in conflicts while accepting remote deletes that are newer than the local content.

Session merge accepts metadata, track linkage/cache JSON, tuning fields, and `session_processing_fingerprint`, but it does not move the processed telemetry BLOB or the raw recording source through `SynchronizationData`. Those payloads are synchronized by the session-data and recorded-source endpoints described in [Cross-Device Synchronization](sync.md).
