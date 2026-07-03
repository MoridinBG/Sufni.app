# UI State, Read Graphs, and Queries

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers the presentation read-state layer: stores, recorded-session projections, and command-side queries. The UI overview and invariants live in [UI Architecture](ui.md).

## Stores

Stores own shared read state. One per entity family, registered as a
singleton, exposed behind two interfaces: a read-only `IXxxStore`
injected into list/row/editor view models and queries, and a
`IXxxStoreWriter` (which extends the read interface) reserved for
coordinators and the composition root. The implementation lives in
each slice's store folder (e.g. `Bikes/Stores/`, `Sessions/Store/`).

| Store                        | Read interface                  | Writer interface                    | Snapshot type                     | Key      |
| ---------------------------- | ------------------------------- | ----------------------------------- | --------------------------------- | -------- |
| `BikeStore`                  | `IBikeStore`                    | `IBikeStoreWriter`                  | `BikeSnapshot`                    | `Guid`   |
| `SetupStore`                 | `ISetupStore`                   | `ISetupStoreWriter`                 | `SetupSnapshot`                   | `Guid`   |
| `SessionStore`               | `ISessionStore`                 | `ISessionStoreWriter`               | `SessionSnapshot`                 | `Guid`   |
| `RecordedSessionSourceStore` | `IRecordedSessionSourceStore`   | `IRecordedSessionSourceStoreWriter` | `RecordedSessionSourceSnapshot`   | `Guid`   |
| `PairedDeviceStore`          | `IPairedDeviceStore`            | `IPairedDeviceStoreWriter`          | `PairedDeviceSnapshot`            | `string` |
| `LiveDaqStore`               | `ILiveDaqStore`                 | `ILiveDaqStoreWriter`               | `LiveDaqSnapshot`                 | `string` |

Persisted stores share the internal `SourceCacheStoreBase<TSnapshot, TKey>` for the repeated DynamicData mechanics. The base owns the cache lifetime, `Connect()`, `Get(key)`, writer `Upsert`/`Remove`, and load-and-replace refresh flow; concrete stores keep their public read/write interfaces and any domain-specific lookups. Startup refresh is coordinated by `IAppDataRefresher`, which resolves writer interfaces and refreshes stores in dependency order. `LiveDaqStore` remains separate because it is runtime-only and publishes `Clear()` / `ReplaceAll(...)` rather than database refresh.

Each persisted store read interface exposes:

- `Connect()` — DynamicData change stream consumed by list view models.
- `Get(key)` — synchronous lookup that returns the current snapshot or
  `null`.

Each writer interface additionally exposes:

- `RefreshAsync()` — load (or reload) all rows from the database via the
  store's repository interface and replace the cache contents. Called at
  startup through `IAppDataRefresher` from
  `MainPagesViewModel.LoadDatabaseContent()`, and after successful sync from
  `SyncCoordinator`.
- `Upsert(snapshot)` / `Remove(key)` — cache updates invoked by coordinators
  after a save / delete / sync arrival, after persistence has already changed.

Snapshots are immutable records, not view models. Snapshots for
editor-backed persisted entities such as bikes, setups, and sessions
carry an `Updated` timestamp that the editors keep as their
`BaselineUpdated` for optimistic conflict detection. Runtime-only or
metadata-only snapshots that are not edited through that flow, such as
`LiveDaqSnapshot`, `PairedDeviceSnapshot`, and
`RecordedSessionSourceSnapshot`, do not expose an editor baseline.

`SessionSnapshot` is metadata-only: it carries `HasProcessedData`,
`ProcessingFingerprintJson`, and the nullable list-summary metrics
(`DurationSeconds`, `DistanceMeters`, `AscentMeters`, `DescentMeters`),
but not the MessagePack telemetry BLOB and not the raw recorded source.
Those large payloads stay in SQLite and are loaded on demand.

`SessionStore` additionally exposes `Watch(Guid)`, a low-level per-id
observable filtered to `Add`/`Update` change reasons. Recorded-session
screens consume the higher-level `RecordedSessionProjection` instead, so
they see session metadata together with setup, bike, source, and
staleness state.

`RecordedSessionSourceStore` is the metadata cache for
`session_recording_source`: source kind, source name, schema version,
and source hash. The full payload is not kept in the store; callers
use `LoadAsync(sessionId)` to load a `RecordedSessionSource` from
SQLite when recompute needs the bytes. The writer surface refreshes,
upserts, or removes cache snapshots after coordinators and repositories
have already changed persistence; it does not write source rows itself.
Sync-server source arrivals are applied through `SessionCoordinator` so
this store still has one application-layer writer.

`SetupStore` exposes `FindByBoardId(Guid)` so the import flow can
look up the existing setup for the currently selected DAQ board
without scanning anything from the UI side. This remains a store
responsibility because the answer is a direct lookup over the store's
own read model, not a cross-domain business query.

`LiveDaqStore` is a runtime-only store that does not persist to the
database. It has no `RefreshAsync()`, its snapshots carry no `Updated`
timestamp, and it is populated entirely by `LiveDaqCoordinator` from
discovery and known-board query results. Its writer interface adds
`Clear()` and `ReplaceAll(IEnumerable<LiveDaqSnapshot>)` on top of
`Upsert` / `Remove`; `LiveDaqCoordinator.ReconcileLocked` uses
`ReplaceAll` to publish a fresh reconciled set in one update. See
[Live DAQ Streaming](live-streaming.md) for the full feature
architecture.

## Recorded Session Projection

`IRecordedSessionProjection` is the read-side projection for recorded
session screens. It subscribes to `ISessionStore`, `ISetupStore`,
`IBikeStore`, and `IRecordedSessionSourceStore`, joins their current
snapshots with the synchronous derivation-window cache, evaluates processing
staleness, and publishes two reactive surfaces:

- `ConnectSessions()` — a DynamicData stream of `RecordedSessionSummary`
  records for the session list. `SessionListViewModel` filters and
  sorts this stream, then projects the visible rows into local-date
  groups that can be expanded and collapsed. `SessionRowViewModel`
  displays `(Stale)` when the summary is stale and `(No Raw)` when the
  processed data may exist but the raw source is unavailable; it also
  exposes current-culture title, time-of-day, and subtitle strings for the
  grouped desktop and mobile list templates.
- `WatchSession(sessionId)` — a replaying observable of
  `RecordedSessionDomainSnapshot` for the recorded detail editor. The
  snapshot carries the session, setup, bike, current fingerprint,
  persisted fingerprint, source metadata, optional derivation window,
  staleness result, and a `DerivedChangeKind` flags value describing what
  changed since the previous domain snapshot.

The recorded detail editor consumes this stream through a single
reaction that treats two axes orthogonally. The *derived* axis
(processed-data availability, fingerprint, or the session-window track)
always refreshes the displayed telemetry/track and advances the
editor's optimistic-concurrency baseline — even while a metadata prompt
is pending, and without resetting unsaved edits. The *metadata* axis
(`SessionMetadataChanged`, now user-authored fields only: name,
description, setup, timestamp, tuning) is decided independently: an
external metadata change with no local edits is absorbed, while a change
against unsaved edits prompts to discard-and-reload and otherwise holds
the baseline back so the next save still detects the conflict. That
held-back state is pinned: once the user declines, a later derived-only
emission (e.g. a recompute result) refreshes telemetry but does **not**
advance the baseline past the unacknowledged metadata edit, so it cannot
silently clear the pending conflict; the pin lifts only when the conflict
resolves (reload, save, or absorb).
`FullTrackId`/`GpsOffsetSeconds` are classified as `DerivedTrackChanged`
rather than metadata, so a recompute or GPS-offset adjustment refreshes
the track without a discard prompt.

Recompute and GPS-offset adjustment are one-way: they persist and upsert
the session store, and the refresh reaches the editor through this same
watch reaction rather than a pushed result.
`ISessionCoordinator.RequestRecomputeAsync(sessionId, reason)` is
baseline-free — the `SessionRecomputeEngine` owns serialization and
liveness — and the staleness prompter is forward-only: on a stale,
recomputable session it confirms and requests a recompute (suppressed
while a recompute the user just triggered is in flight), surfacing only
the unrecomputable/failed outcomes.

Projection recomputes are coalesced through an injected
`IRecordedSessionProjectionScheduler`; the default
`UiThreadRecordedSessionProjectionScheduler` posts to
`Dispatcher.UIThread` at background priority rather than using the
ambient synchronization context of the thread that queued the change.
This lets a batch of session/setup/bike/source/window updates produce
coherent summaries and domain snapshots instead of a cascade of
partial UI states. Dependency changes recompute all sessions because a
setup or bike update can affect any recorded session linked through
that dependency.

`ProcessingFingerprintService` is the pure derivation service behind
the projection. It parses the persisted fingerprint JSON from
`SessionSnapshot`, computes the current fingerprint from session,
setup, bike, source, and optional derivation-window snapshots plus the
session's clamped velocity-filter processing option (read from an app-wide
cache that re-hydrates after each sync apply), and classifies staleness as:

- `Current` — processed data matches the recorded source and current
  processing inputs.
- `MissingProcessedData`, `ProcessingVersionChanged`,
  `DependencyHashChanged`, `SourceWindowChanged`,
  `UnknownLegacyFingerprint` — stale and recomputable when setup, bike, and
  raw source are available.
- `MissingDependencies` — stale but not recomputable until the setup
  or bike is restored.
- `MissingRawSource(ProcessedStateStale)` — displayed as "No Raw" and
  not recomputable because the raw source is unavailable. Existing
  processed data can still be opened. `ProcessedStateStale` preserves
  whether the processed BLOB/fingerprint is known to be stale even
  though the app cannot repair it until the source is restored.

Once the processing fingerprint began recording the velocity-filter option, every pre-existing fingerprint reads as legacy — stale and recomputable. A one-time, per-device startup pass (`ProcessingOptionsResetMigration`) resets each source-backed session's stored option to the 25 ms default and recomputes it through the recompute engine, so the stored fingerprint records the option it was produced with and the session stops being stale. The pass runs off the UI thread, is tracked in `core_migration` so it runs once, and is resumable: an interrupted run leaves the marker unwritten and retries on the next launch. Source-less sessions cannot be recomputed and are left untouched, surfacing through the not-recomputable staleness state. The projection still reports stale for new source/dependency/option/window mismatches after the marker exists.

`IRecordedSessionDomainQuery` is the command-side companion. It reads
the current session/setup/bike/source snapshots synchronously from
stores, resolves the source row through `window?.SourceSessionId ?? sessionId`,
and returns one `RecordedSessionDomainSnapshot` for workflows such as the
recompute engine. Coordinators use the
query for current-state decisions; they do not subscribe to the projection
stream.

## Queries

Queries answer business questions across entity families without
going through view models. They are stateless singletons in
each slice's `Queries/` folder (e.g. `Bikes/Queries/`, `LiveDaq/Queries/`).

`IBikeDependencyQuery.IsBikeInUseAsync(Guid)` (backed by
`BikeDependencyQuery` over `ISynchronizableRepository<Setup>`) reports whether any
setup currently references a bike. `BikeCoordinator.DeleteAsync` uses
it to short-circuit deletes with `BikeDeleteOutcome.InUse`. The
answer is sourced from the database, not from any list view model, so
it does not depend on which screens the user has visited.

`ILiveDaqKnownBoardsQuery` (backed by `LiveDaqKnownBoardsQuery`)
merges `Board` rows from the database with `ISetupStore` and
`IBikeStore` to produce enriched records carrying board identity,
setup name, and bike name. It also exposes keyed lookup and travel-
calibration answers for a specific live DAQ identity, so the live
detail view model can project calibrated travel text without pushing
setup or bike logic into the transport/session-state layer. Unlike
`BikeDependencyQuery`, it caches its projection and auto-refreshes via
store change subscriptions so consumers can re-enrich display names
and calibration context without repeated database round-trips. See
[Live DAQ Streaming](live-streaming.md).

`IRecordedSessionDomainQuery` lives in `RecordedSessionProjection/` rather than
`Queries/`, but it follows the same command-side rule: it answers one
current business question without owning a collection. It joins the
current session, setup, bike, and recorded-source snapshots and
returns the same domain snapshot shape that `IRecordedSessionProjection`
publishes. The `SessionRecomputeEngine` uses it for the staleness gate
before loading the raw source and a still-current guard before
committing recomputed data.
