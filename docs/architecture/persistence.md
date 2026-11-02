# Persistence & Serialization

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers the SQLite schema, the connection context, per-aggregate repositories, soft deletes, and conflict resolution semantics shared with [cross-device synchronization](sync.md).

## Schema

```mermaid
erDiagram
    session ||--o| setup : "setup_id"
    session ||--o| session_recording_source : "session_id"
    session ||--o| session_blob_swap_request : "session_id"
    session ||--o| session_gps_metrics : "session_id"
    setup }o--|| bike : "bike_id"
    board ||--o| setup : "setup_id"

    session {
        text id PK
        text name
        text description
        int timestamp
        int velocity_filter_window_ms
        real duration_seconds
        text setup_id FK
        blob data
        real gps_offset_seconds
        text session_processing_fingerprint
        text source_session_id
        real window_start_seconds
        real window_end_seconds
        real previous_window_start_seconds
        real previous_window_end_seconds
        int processed_telemetry_revision
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
        int version
        text updated_by
        int local_seq
        int sync_seq
        int deleted
    }

    session_gps_metrics {
        text session_id PK
        text gps_metrics_key
        real distance_meters
        real ascent_meters
        real descent_meters
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
        text rear_suspension
        real front_compression_damping_cutoff_mm_per_second
        real front_rebound_damping_cutoff_mm_per_second
        real rear_compression_damping_cutoff_mm_per_second
        real rear_rebound_damping_cutoff_mm_per_second
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
        int version
        text updated_by
        int local_seq
        int sync_seq
        int deleted
    }

    setup {
        text id PK
        text name
        text bike_id FK
        text front_sensor_configuration
        text rear_sensor_configuration
        int updated
        int version
        text updated_by
        int local_seq
        int sync_seq
        int deleted
    }

    board {
        text id PK
        text setup_id FK
        int updated
        int version
        text updated_by
        int local_seq
        int sync_seq
        int deleted
    }

    track {
        text id PK
        text points
        int start_time
        int end_time
        int points_revision
        int updated
        int version
        text updated_by
        int local_seq
        int sync_seq
        int deleted
    }

    sync {
        text hub_thumbprint PK
        text db_incarnation PK
        int last_push_sequence
        int last_pull_sequence
    }

    session_blob_swap_request {
        text session_id PK
        text target_fingerprint
        text target_generation
    }

    session_segment {
        text id PK
        text owner_key
        text name
        real start_seconds
        real end_seconds
        int updated
        int version
        text updated_by
        int local_seq
        int sync_seq
        int deleted
    }

    session_video {
        text id PK
        text owner_key
        text path
        int start_timestamp
        real duration_seconds
        real alignment_offset_seconds
        int added
    }

    custom_tile_layer {
        text id PK
        text name
        text url_template
        text attribution_text
        text attribution_url
        int max_zoom
        int version
        int updated
        text updated_by
        int deleted
        int local_seq
        int sync_seq
    }

    sync_deletion {
        text entity_table PK
        text id PK
        int version
        int updated
        text updated_by
        int deleted
        int local_seq
        int sync_seq
    }

    sync_sequence {
        int id PK
        int local_seq
        int sync_seq
    }

    paired_device {
        text device_id PK
        text display_name
        text refresh_token_hash
        text previous_refresh_token_hash
        int expires
    }

    database_identity {
        int id PK
        text db_incarnation
    }

    core_migration {
        text id PK
    }
```

`database_identity` has one row (`id = 1`). The `database-incarnation`
core migration assigns its random `db_incarnation` once, including on existing
databases. Normal launches preserve it; creating a fresh database assigns a new
identity. Pairing, refresh and run-start responses carry this identity to distinguish
database resets in sync cursors. Run-start verifies it before transfers use a cursor.

`IStartupDatabase` exposes initialization and the reset file operation on the
connection context. Reset waits for initialization to settle, closes the
connection, and moves every remaining database/WAL/SHM file into a timestamped
sibling archive directory. SQLite may checkpoint and remove its sidecars on
close. The archive is retained; a separate connection creates the replacement
through the normal migration path and closes it. The existing context and its
consumers are not reinitialized; the replacement requires a new launch.

The `sync-authority` migration seeds `version = 1` and an empty unknown-writer
`updated_by` while preserving authored `updated`, allocates initial `local_seq`
and `sync_seq` values, and retains current tombstones in `sync_deletion` before
payload cleanup. `sync_sequence` holds the two transactionally allocated counters. The same migration
copies legacy custom map layers into `custom_tile_layer` before assigning their
initial metadata. Startup rewrites the preference document after migration, keeping
the selected layer and other device-local values while removing custom definitions
and the old sync clock. The migration marker prevents stale preferences from
overwriting later layer edits. The same batch hashes legacy paired-device
refresh tokens and removes the plaintext `token` column. Rotation atomically
compares the presented hash, updates expiry, and retains the previous hash for
one recovery use until the new refresh token is used.
Every local authored write advances its row version above both the payload and
ledger version, stamps the shared installation identity and advances sequences.
Derived generation writes advance sequences while preserving version, timestamp
and writer. GPS metric materializations remain local and do not advance them.
Deletes write the payload tombstone and ledger atomically. Ledger entries are
never purged; explicit restoration advances above their versions.

The remote merge uses the same authored-history rule on hub and client;
`client_updated` is removed from schema and wire. Push selection uses `local_seq`
and pull selection uses `sync_seq`, including deletion-ledger entries. The same
migration discards timestamp cursors and recreates the cursor table for a full
baseline exchange. Cursors are keyed by `(hub_thumbprint, db_incarnation)`.
Each direction updates its column atomically without overwriting the other.
An unseen hub or incarnation starts at zero, independent of its network address.

## SQLite Persistence Repositories

`SqliteConnectionContext` (`Sufni.App/Sufni.App/Infrastructure/SqliteConnectionContext.cs`) owns the single `SQLiteAsyncConnection` and the initialization gate. It receives the database path from the platform root (heads select `Environment.SpecialFolder.LocalApplicationData` + `Sufni.App/sst.db`) and starts `DatabaseMigrationRunner` through the background runner, which enables WAL mode, creates core tables, applies compatibility migrations/backfills and the derivation-window migration, performs startup cleanup, and applies the one-time track content migration.

The connection context supplies the injected clock to startup migrations.
Repositories and transaction runners receive `TimeProvider` for authored writes,
tombstones, merge timestamps and cleanup cutoffs. Transaction runners pass sampled timestamps into synchronous helpers; those
helpers do not read the wall clock.

The connection context is also the only app-owned transaction entry point for multi-statement repository writes. `RunInTransactionAsync(...)` awaits initialization, then delegates to sqlite-net's `RunInTransactionAsync(Action<SQLiteConnection>)`, so the connection/transaction lock is held for the whole callback. Transaction bodies use synchronous `SQLiteConnection` operations; raw `BEGIN TRANSACTION` / `COMMIT` / `ROLLBACK` statement sequences are not used.

Bike rows include presentation-owned damping speed cutoffs for front/rear compression and rebound. These values default to 200 mm/s, are synchronized and exported with the bike, and are backfilled on startup for legacy schemas. They are not session preferences and do not affect telemetry processing fingerprints.

Startup migration ensures bike rows use the single `rear_suspension` union JSON column. Legacy schemas with `rear_suspension_kind`, `linkage`, and/or `leverage_ratio` are backfilled into `RearSuspensionSpec` JSON, linkage shock stroke is reconciled against the bike-level `shock_stroke` column, draft rows preserve the selected mode when the payload is missing or unparseable, and the legacy columns are dropped after backfill.

The `session-domain-ownership` migration copies the launch snapshot's legacy
processing options into nullable `session.velocity_filter_window_ms`, without
changing authored timestamps or processed data. Its marker prevents later launches
from replaying old preferences over a newly authored option. Runtime preferences
and startup jobs no longer reset or recompute processing options. The same
migration drops the redundant `has_data` column and rewrites source hashes using
uncompressed SST content or live content without the setup display name. It preserves payloads, duplicate rows, session
ids and split references; stored processing fingerprints are left stale.

Startup migration does not repair or currentize stale `session_processing_fingerprint` values. Legacy, missing, malformed, or dependency-mismatched fingerprints remain classified as stale by the recorded-session projection so recompute can surface derived-data changes explicitly instead of silently rewriting fingerprint state at database initialization.

Persistence consumers inject narrow repository interfaces instead of a single database facade. `ISynchronizableRepository<T>` owns generic soft-delete CRUD for `Synchronizable` entities; `ISessionRepository`, `IRecordedSessionSourceRepository`, `ITrackRepository`, `ISessionVideoRepository`, `ISessionSegmentRepository`, and `IPairedDeviceRepository` own aggregate-specific operations and intent-specific projections; `ISyncDataStore` / `SynchronizationMergeEngine` owns sync sequences and cursors, delta projection, remote apply, and merge conflict resolution. Read paths that only need ids, source snapshots, track payload metadata, full-track reference checks, or processing fingerprint inputs use those projections instead of loading whole aggregates or BLOB columns. `DatabaseMigrationRunner` is the only schema initializer/migrator, and repositories assume `SqliteConnectionContext` has run initialization before handing out the shared connection.

The `sync-authority` migration creates `local_seq` and `sync_seq` indexes on every
synced table and the deletion ledger. Delta projection is bounded to
`(sinceExclusive, upperInclusive]`. `sync.last_push_sequence` and
`sync.last_pull_sequence` advance independently. The run's push bound is captured
before any network work; a concurrent edit beyond that bound is selected on the
next run. Legacy `updated` and `deleted` indexes are still maintained at startup.

Two local-only revision columns identify exact payload generations without using
the sync-facing `Updated` timestamp: `session.processed_telemetry_revision` changes
when the processed BLOB or its fingerprint changes, and `track.points_revision`
changes when the full-track point payload changes. Startup migration normalizes both zero and
SQL-null legacy revisions to `1` for existing non-null payloads and to `0` when
the payload is absent, then drops every owned revision trigger and recreates the
current definitions. Trigger-level `WHEN` predicates avoid nested writes for
no-op watched assignments; effective content changes advance exactly one step,
and payload-aware fallback arithmetic prevents nullable legacy state from
remaining null after a watched write. An incoming revision assignment that
accompanies a watched write is restored or replaced by the SQLite-owned value.
These values are excluded from JSON/sync contracts and are not conflict versions.
Map and comparison readers now derive session windows from the selected full
track's exact points revision plus the window inputs. The session-window payload and its revision are not persisted or synced.

Repositories do not publish UI state. Workflow owners persist, prepare any
fresh database-computed snapshot fields, then publish through store writer
interfaces. Stores do not depend on repositories, perform writes, or classify
failures. Metadata save, GPS alignment and downloaded processed-data application
are owned by their session/track/sync workflows. Cross-aggregate SQL still runs
inside one `RunInTransactionAsync` callback before publication; sync endpoints
persist first and inbound appliers prepare and publish the resulting snapshots.

`ISynchronizableRepository<T>` operations on any `Synchronizable` subclass:

- `GetAllAsync<T>()` — returns all records where `Deleted == null`
- `GetChangedAsync<T>(long since)` — returns records where `Updated > since` OR (`Deleted != null` AND `Deleted > since`)
- `PutAsync<T>(item)` — upsert. Stamps `Updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds()` and clears `Deleted` (resurrecting any tombstoned row with the same id).
- `DeleteAsync<T>(id)` — stamps an authored deletion and writes its durable ledger entry when the row exists and is not already tombstoned.

`ISessionRepository` operations split metadata and processed-data handling, and store the values they are given: reprocessing produces a pure `ProcessedGeneration`, and import/live-save/recompute owners prepare the optional content track and session before calling the repository. Incoming synchronized bytes are validated, prepared, committed and published by `SessionSyncApplier`. `session.data` is the authoritative local processed-telemetry cache; there is no separate `has_data` column. Metadata and sync-delta reads use `SessionMetadataRow`, projecting availability from `data IS NOT NULL` without selecting the BLOB. A payload-bearing `Session` derives `HasProcessedData` only from its loaded `ProcessedData`; metadata drafts carry no availability override. Persistence helpers map the declared entity schema, so projection-only fields cannot become writes. Duration remains a derived session column. GPS distance/ascent/descent and their input key live in device-local `session_gps_metrics`; metadata reads join that table without loading point JSON. The corresponding properties on `Session` are ignored by entity writes and JSON, and `SessionSnapshot` carries those joined values plus `GpsMetricsKey`. Metadata draft updates preserve `timestamp` and `gps_offset_seconds`; only creation/window editing sets the timestamp, and the explicit alignment command owns the offset. Split copies the current alignment to its new session; trim and undo preserve it. `gps_offset_seconds` is authored per-session state applied when deriving the session-window GPS track from a reusable full `Track`; it is stored on `session` rather than `track` because the same full ride track can back multiple recorded sessions or segments.

- `PutSessionAsync()` — updates user-authored session metadata columns and stamps `Updated`/`Deleted` like `PutAsync`. Existing derived summary metrics are preserved on metadata updates. Metadata SQL does not name the multi-megabyte `data` BLOB, so its fingerprint, linkage and local revision remain untouched. Soft-deleted-row reuse still restores the complete supplied aggregate through its dedicated insert/reuse path.
- `ImportProcessedSessionAsync(session, newFullTrack, source)` — classifies the recording and atomically inserts a new session, full track and source, or adds only the source to existing metadata. Duplicate source hashes preserve the existing recording. The returned session/source snapshots reflect the retained rows. Any failed write rolls back the transaction.
- `UpdateProcessedDerivedDataAsync(session, newFullTrack, expectedInputs)` — the recompute owner first validates its result fingerprint against a captured database input bundle. In one transaction the repository re-reads those scalar inputs and compares them with the expected bundle; a mismatch or missing dependency rolls back the generation and optional track together. On a match it writes only derived columns (`data`, fingerprint and duration), preserving authored metadata and `updated`. The repository has no fingerprint-service dependency.
- `UpdateSessionPsstAsync(id, data, fingerprintJson, durationSeconds, expected)` — the missing-BLOB fill write path. It atomically replaces the BLOB/fingerprint and supplied duration only while the stored fingerprint and telemetry revision match the captured guard. It preserves authored fields and never advances `updated`.
- `UpdateSessionProcessedGenerationAsync(id, data, fingerprintJson, generation, expected)` — the complete-generation swap path, with the same transaction guard. It replaces the BLOB, fingerprint and duration together while preserving authored GPS alignment. Both write methods also compare the expected hub swap target and clear a matching request inside the successful write transaction; a replaced request or changed generation returns false without modifying any rows.
- `UpdateGpsAlignmentAsync(id, gpsOffsetSeconds)` — persists only the authored GPS offset, advancing `updated`; window points remain a read-time derivation.
- `GetSessionRawPsstAsync(id)` / `GetSessionRawPsstWithFingerprintAsync(id)` — return the latest raw MessagePack blob (sync transfer, consumer-side deserialization); the second also returns the fingerprint of those bytes so the session-data push can carry both. The revision-constrained `GetSessionRawPsstAsync(id, processedTelemetryRevision)` returns bytes only while that exact local payload generation still exists.
- `GetSessionsAsync()` / `GetSessionAsync(id)` / `GetActiveSessionIdsAsync()` / `GetIncompleteSessionIdsAsync()` / `GetIncompleteSessionIdsWithFingerprintAsync()` — metadata projections, active ids for recompute-all, ids of rows without processed data (the last pairs each id with its stored fingerprint as the session-data pull's download match target).
- `GetProcessingInputBundleAsync(sessionId)` — a constrained join over session/setup/bike/source columns that builds the database-resident processing input bundle for transaction-time fingerprint checks without loading processed telemetry or source payload bytes

There is no `GetSessionPsstAsync` on the repository: consumers that need a `TelemetryData` fetch the raw blob and deserialize it themselves through `ISessionTelemetryProcessor`, so MessagePack knowledge stays out of the persistence layer.

`ISessionSyncApplier` (`Sessions/Coordination/SessionSyncApplier.cs`) owns incoming
processed generations on hub and client. `ApplyUploadedAsync` and
`ApplyDownloadedAsync` validate transferred MessagePack, submit the captured
fingerprint/revision/input guard, and publish the authoritative session after success.
The shared acceptance rule requires the incoming fingerprint to match the current
processing inputs and keeps an equivalent current local generation. Missing source
or dependency inputs defer the transfer. The transaction rechecks the captured
input bundle so a concurrent calibration or processing-option change rejects it.
Missing-BLOB fills normalize the incoming duration and preserve the current GPS
metrics. Replacing a held BLOB requires a complete target generation; a fill whose
row gained a BLOB in the meantime is superseded. Neither path reconstructs a
session-window payload.
A complete generation supplies the accepted duration. Track selection is derived locally.
Invalid or superseded transfers leave persisted and published data unchanged.

`ITrackRepository.TryCommitGpsMetricsAsync` accepts a key and prepared GPS values.
Its transaction selects the current full track using the common overlap rule and
compares the complete current key before inserting/updating `session_gps_metrics`.
A changed window, selected track or deleted session rejects the write. Successful
metric writes never change `session.updated`. The table is not synchronized.

`ITrackRepository.GetTrackSnapshotsAsync` loads active metadata without point JSON, optionally filtering IDs in chunks of 500. Full-track display uses `GetTrackPayloadMetadataAsync(trackId)` followed by `GetTrackPayloadAsync(trackId, pointsRevision)`; the exact-reader operation accepts a caller-supplied revision and returns no value when it is unavailable. `DeleteIfUnselectedAsync` checks every active session against the current track catalog using `SessionTrackSelection` and deletes only an unselected candidate in the same transaction. The session-delete transaction uses the same check after deleting its session. There is no persisted session-to-track association or containment lookup.

`IRecordedSessionSourceRepository` owns recorded-source rows:

- `GetRecordedSessionSourceAsync(id)` — load one full source payload
- `GetRecordedSessionSourceSnapshotsAsync()` / `GetRecordedSessionSourceSnapshotAsync(id)` — metadata-only source projections for stores and refreshes
- `GetSessionIdsMissingRecordedSourceAsync()` — returns non-deleted session ids whose recording source (`source_session_id`, or the session's own id) has no source row
- `GetMissingRecordingSourceIdsAsync()` — the same check, but returns the recording source ids instead of the session ids
- `GetReferencedSourceSessionIdsAsync()` — returns the distinct `source_session_id` values of live sessions that derive from another session's source
- `GetSourceBackedSessionIdsAsync()` — returns ids that currently own a recorded-source row without loading payload BLOBs; the source-sync query uses it to enumerate locally available source owners
- `DeleteOrphanedRecordedSessionSourcesAsync()` — in one transaction, removes source rows whose owner session no longer exists and that no live session references through `source_session_id`, and returns their ids
- `PutRecordedSessionSourceAsync(source)` — validates and inserts a source only when its session key is absent, returning the persisted metadata snapshot in the same transaction. An existing source retains its name, hash and payload. The processed-session transaction uses the same write-once insertion. Source deletion stays inside session transactions and orphan retention cleanup

`session_blob_swap_request` is local durable coordination state for the desktop
hub's push-direction processed-generation swaps. Each row stores the session id,
target fingerprint, and serialized `SessionProcessedGeneration`. The
`sync-authority` migration removes fingerprint-only requests; their session rows
and held BLOBs remain intact. New requests require a complete target generation.
The merge engine inserts, replaces, or clears the request inside the same
transaction that accepts the corresponding deferred session metadata. Startup
removes requests for missing or soft-deleted sessions, and recreates delete/soft-delete triggers that remove a
request with its owner session. Listing pending requests only reads the table; a
matching request is cleared after the validated generation write succeeds. An
equivalent or ineligible incoming generation is terminally ignored; its owner
clears the request only if the target fingerprint still matches. A newer request
survives completion of the old transfer.

`session_video` links a local file to an immutable recording source through `owner_key`. One source/file pair shares one owner-relative alignment offset. `ISessionVideoRepository.LinkAsync` writes distinct source links in one transaction; re-import retains an existing ID, added time and alignment. Session deletion and startup purge leave links intact. The `track-materializations` migration converts and collapses old session links by the widest-window policy, then drops `session_id`. See [video.md](video.md).

`session_segment` holds source-owned named ranges. `owner_key` is the immutable source hash and both anchors are source-relative. Reads return non-deleted segments without joining sessions; the picker derives visibility from the source identity and current session window. Session deletion and physical purge retain these rows. Only explicit segment deletion creates a tombstone. The `track-materializations` migration converts old session-relative anchors and drops `session_id` without changing segment IDs or authored clocks. See [segments.md](segments.md).

The session derivation window lives on the session row. `source_session_id`,
`window_start_seconds`, and `window_end_seconds` describe the window; a null
`source_session_id` means the session uses its own source in full. The
`previous_window_*` columns keep the window before the last trim for undo.
These columns are synchronized with the session. Older builds stored trims and
splits in `private_session_source_range`. Startup copies them once
(`core_migration` id `session-derivation-window`): live rows that are not the
session's own full range are copied, then sessions without a range row take the
window stored in their processing fingerprint. The copy drops the range table.

After the derivation-window migration, startup drops `extension_schema_version`, the table
that held the schema versions of the old tables.

## JSON Serialization

`AppJson` (`Sufni.App/Sufni.App/Infrastructure/AppJson.cs`) centralizes System.Text.Json configuration behind a source-generated `AppJsonContext` and exposes two profiles:

- **Lenient** (`AppJson.Options` / `AppJson.Context`) — all local round-trips (entity and track JSON columns; the processing dependency hash) and the entire sync client. Case-insensitive binding with a snake_case enum converter. Its output is deliberately **byte-stable**: `ProcessingDependencyHash` SHA-256s the `AppJson.Options`-serialized payload, so any change to the lenient options would silently invalidate every stored processing fingerprint. Treat the lenient profile as a wire/hash contract, not a tunable.
- **Hardened** (`AppJson.InboundOptions` / `AppJson.InboundContext`) — network-inbound deserialization on the desktop sync server only (the two `PATCH` session/source-data endpoints). Built from the .NET 10 `Strict` preset — reject duplicate keys and unmapped members, case-sensitive binding, required non-nullable members and constructor parameters — plus the same snake_case enum converter. Because the client always emits every snake_case key explicitly (including explicit nulls for optional members), well-formed first-party traffic is unaffected while malformed or truncated bodies are rejected at the trust boundary. See [Cross-Device Sync § Server](sync.md#server).

Bike persistence, sync, and export JSON carry `RearSuspensionSpec` union values, `LinkageSpec`, `LeverageRatioSpec`, and `WheelSpec`. Sync rows and schema-versioned bike export files require `rear_suspension`; missing unions, malformed unions, and the legacy `rear_suspension_kind` / `linkage` / `leverage_ratio` JSON triple are invalid outside the local SQLite startup migration. The pre-refactor mutable linkage classes are not serialization roots.

`ISessionRepository.ImportProcessedSessionAsync` performs the source-hash and
content-id classification inside the write transaction. An existing source hash
returns `AlreadyImported` without changing any rows. A live content-id session
with no source receives only the source row; its authored fields, clock and
processed data are preserved. If that retained row is soft-deleted, explicit
re-import restores the same id with the newly prepared recording, clears its
old window/deletion state and advances its authored version beyond the ledger.
Revision triggers preserve payload-generation continuity. Restoration, source
insertion and the optional generated track commit or roll back together.
A new recording inserts those same values atomically. Concurrent imports therefore converge on one session, and
pre-upgrade random ids are retained when their source hash already exists.

## Soft Delete

`Synchronizable` entities (`Sufni.App/Sufni.App/SyncAndPairing/Models/Synchronizable.cs`) — `bike`, `setup`, `session`, `board`, `track`, `session_segment` — carry authored `Version`, `Updated`, `UpdatedBy`, nullable `Deleted`, device-local `LocalSequence`, and hub `SyncSequence`. `paired_device`, `session_recording_source`, and `sync` are not `Synchronizable` and have their own lifecycles. The one-time `track-materializations` migration groups tracks by complete point
content, preserves an active row when one exists, and chooses the lowest old ID
within that state. It rekeys each survivor to its content ID without changing authored session clocks. Distinct points with
matching start/end times remain separate. Retained deleted rows with points are
rekeyed too; point-less tombstones keep their old identities. There is no per-launch track deduplication pass. The same migration drops
`session.track`, `track_projection_revision` and `full_track_id`, removes the retired revision
trigger, and rewrites queued generation JSON without the cached track or GPS metrics.
It also removes the fractional window origin formerly folded into GPS alignment,
without changing session clocks or effective track positioning. Fractional origins
are explicit read inputs derived from `window_start_seconds`.
It copies legacy distance/ascent/descent into `session_gps_metrics` with an empty
key, then drops those columns from `session`. The old values remain visible until
the coordinator replaces them. Startup removes metric rows after their session
has been physically purged.

On database initialization, the `Cleanup()` pass permanently removes:

- `Synchronizable` rows with `Deleted` older than 1 day
- `paired_device` rows whose `Expires` precedes the injected clock’s UTC time

Recorded-source orphan cleanup runs after initialization through
`RecordedSessionSourceRetentionCleanup`, not inside the schema cleanup pass. It
calls `DeleteOrphanedRecordedSessionSourcesAsync()`, which keeps every source
row that a live session references, and publishes the removed ids to the
recorded-source store. This lets a split/trimmed derived session keep using a source row whose
original owner session has been purged.

## Conflict Resolution

`SynchronizationMergeEngine.Merge` is used by both push and pull inside one
transaction. `SynchronizationMergeOrder` compares authored history in this order:

1. Higher `version` wins.
2. At equal version, deletion beats content.
3. Between two contents or two deletions, later `updated` wins, then the higher
   `updated_by` using ordinal string ordering.
4. Equal history is a no-op. Neither side re-stamps authored metadata.

Accepted writes allocate a local sequence; the hub additionally allocates its
acceptance sequence, while a client preserves the received hub sequence.
`SynchronizationData.deletions` carries durable ledger entries even after payload
purge. Content at or below a ledger version is rejected; explicitly restored
content above it can win. A historical deletion can be retained without hiding
newer active content. Soft deletion preserves the retained payload. Repeating the
same exchange changes neither rows nor sequence counters.

Session merge accepts metadata, processed-generation fields, tuning fields, and `session_processing_fingerprint`, but it does not move the processed telemetry BLOB or raw recording source through `SynchronizationData`. Those payloads use the dedicated endpoints described in [Cross-Device Synchronization](sync.md). When the local row already holds a processed BLOB, both inbound merge paths defer the **whole processed generation** — fingerprint and duration — while applying independent user metadata, including GPS alignment. Matching bytes and the accepted generation then move together through the session-data endpoint's [download-then-swap](sync.md#processed-blob-coherence-download-then-swap).
