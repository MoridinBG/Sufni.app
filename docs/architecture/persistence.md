# Persistence & Serialization

> Part of the [Sufni.App architecture documentation](../../ARCHITECTURE.md). This file covers the SQLite schema, the database service, soft deletes, and conflict resolution semantics shared with [cross-device synchronization](sync.md).

## Schema

```mermaid
erDiagram
    session ||--o| setup : "setup_id"
    session ||--o| track : "full_track_id"
    session ||--o| session_cache : "session_id"
    setup ||--o| bike : "bike_id"
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
        real front_spring_rate
        real rear_spring_rate
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

    bike {
        text id PK
        text name
        real head_angle
        real fork_stroke
        real shock_stroke
        text linkage
        blob image
        real pixels_to_millimeters
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
        real front_hsc_pct
        real front_lsc_pct
        real front_lsr_pct
        real front_hsr_pct
        real rear_hsc_pct
        real rear_lsc_pct
        real rear_lsr_pct
        real rear_hsr_pct
    }

    app_setting {
        text key PK
        text value
    }

    sync {
        text server_url PK
        int last_sync_time
    }

    paired_device {
        text device_id PK
        text token
        datetime expires
    }
```

## Database Service

`SqLiteDatabaseService` (`Sufni.App/Sufni.App/Services/SQLiteDatabaseService.cs`) implements `IDatabaseService` using sqlite-net-pcl with WAL mode. The database path uses `Environment.SpecialFolder.LocalApplicationData` + `Sufni.App/sst.db`.

Generic operations on any `Synchronizable` subclass:

- `GetAllAsync<T>()` — returns all records where `Deleted == null`
- `GetChangedAsync<T>(long since)` — returns records where `Updated > since` OR (`Deleted != null` AND `Deleted > since`)
- `PutAsync<T>(item)` — upsert with `Updated = DateTimeOffset.Now`
- `DeleteAsync<T>(id)` — sets `Deleted` timestamp (soft delete)

Session-specific operations split metadata from blob handling:

- `PutSessionAsync()` — updates metadata columns only, never touches the `data` blob
- `PatchSessionPsstAsync(id, bytes)` — updates only the `data` column
- `GetSessionPsstAsync(id)` — deserializes MessagePack blob to `TelemetryData`

## Soft Delete

All `Synchronizable` entities (`Sufni.App/Sufni.App/Models/Synchronizable.cs`) carry `Updated` (server timestamp), `ClientUpdated` (local timestamp), and nullable `Deleted` (soft delete timestamp). On database initialization, records with `Deleted` older than 1 day and expired paired devices are permanently removed.

## Conflict Resolution

`MergeAsync<T>()` handles incoming sync data:

1. **New entity** (not in local DB): insert, set `ClientUpdated = entity.Updated`
2. **Remote delete** (`entity.Deleted` set): accept, update local `Deleted` timestamp
3. **Local wins** (`existing.ClientUpdated > entity.Updated`): keep local content, update timestamps
4. **Remote wins** (otherwise): replace local with remote, set `ClientUpdated = entity.Updated`

This gives local client changes precedence in conflicts while accepting remote deletes.
