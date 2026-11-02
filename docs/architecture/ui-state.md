# UI State, Read Graphs, and Queries

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers the presentation read-state layer: stores, recorded-session projections, and command-side queries. The UI overview and invariants live in [UI Architecture](ui.md).

## Stores

Stores own shared read state. One per entity family, registered as a
singleton, exposed behind two interfaces: a read-only `IXxxStore`
injected into list/row/editor view models and queries, and a
`IXxxStoreWriter` (which extends the read interface) used by coordinators,
session use-case services behind coordinators, sync appliers, transaction
runners, and the composition root. View models depend on read-only stores. The
implementation lives in each slice's store folder (e.g. `Bikes/Stores/`,
`Sessions/Store/`).

| Store                        | Read interface                  | Writer interface                    | Snapshot type                     | Key      |
| ---------------------------- | ------------------------------- | ----------------------------------- | --------------------------------- | -------- |
| `BikeStore`                  | `IBikeStore`                    | `IBikeStoreWriter`                  | `BikeSnapshot`                    | `Guid`   |
| `SetupStore`                 | `ISetupStore`                   | `ISetupStoreWriter`                 | `SetupSnapshot`                   | `Guid`   |
| `SessionStore`               | `ISessionStore`                 | `ISessionStoreWriter`               | `SessionSnapshot`                 | `Guid`   |
| `RecordedSessionSourceStore` | `IRecordedSessionSourceStore`   | `IRecordedSessionSourceStoreWriter` | `RecordedSessionSourceSnapshot`   | `Guid`   |
| `TrackStore`                 | `ITrackStore`                   | `ITrackStoreWriter`                 | `TrackSnapshot`                   | `Guid`   |
| `SessionVideoStore`          | `ISessionVideoStore`            | `ISessionVideoStoreWriter`          | `SessionVideoSnapshot`            | `Guid`   |
| `SessionSegmentStore`        | `ISessionSegmentStore`          | `ISessionSegmentStoreWriter`        | `SessionSegmentSnapshot`          | `Guid`   |
| `CustomTileLayerStore` | `ICustomTileLayerStore` | `ICustomTileLayerStoreWriter` | `CustomTileLayerSnapshot` | `Guid` |
| `BoardStore`                 | `IBoardStore`                  | `IBoardStoreWriter`                | `BoardSnapshot`                 | `Guid` |
| `PairedDeviceStore`          | `IPairedDeviceStore`            | `IPairedDeviceStoreWriter`          | `PairedDeviceSnapshot`            | `string` |
| `LiveDaqStore`               | `ILiveDaqStore`                 | `ILiveDaqStoreWriter`               | `LiveDaqSnapshot`                 | `string` |

Persisted stores share
`SourceCacheStoreBase<TSnapshot, TKey>`
(`Sufni.App/Sufni.App/Shared/Base/SourceCacheStoreBase.cs`) for the
repeated DynamicData mechanics. The base owns the cache lifetime, `Connect()`,
`Get(key)`, watch helpers, and UI-thread-dispatched protected publication
methods (`PublishSnapshotAsync`, `PublishSnapshotsAsync`, `PublishRemoveAsync`,
`PublishRemovalsAsync`, and `ReplaceAllAsync`). `StorePublication` owns their shared UI dispatcher and batch boundary.
Each store keeps its domain-specific lookups and read/write interfaces. Cache
mutation stays inside stores; persistence, conflict checks and snapshot loading
belong to the requesting workflow. No store has a repository dependency.
Startup refresh is coordinated by `IAppDataRefresher`; sync publishes affected
rows through `SynchronizationApplier`. `LiveDaqStore` remains separate
because it is runtime-only and publishes `ReplaceAll(...)` rather than
loading persisted rows.

`StorePublication.BeginBatch()` groups already-prepared cache edits on the UI
thread. Stores defer their DynamicData notifications until the outer batch ends;
then each store notifies and one shared `Committed` event identifies the stores
that emitted changes. An individual publication opens an implicit batch, and an
unchanged refresh or missing-key removal emits no commit. A batch is a publication
boundary, not a persistence transaction: load and compute before entering it.

Single-store subscriptions keep working. Joined consumers must derive from all
current store values on the relevant commit; releasing per-store notifications
in order does not make `CombineLatest` coherent. Existing projection consumers
have not migrated to this commit signal yet. The base disposes its cache and
commit-tracking subscription when its owning graph is disposed.

Each persisted store read interface exposes:

- `Connect()` — DynamicData change stream consumed by list view models.
- `Get(key)` — synchronous lookup that returns the current snapshot or
  `null`.

Each persisted writer interface additionally exposes:

- `ReplaceAllAsync(snapshots)` — replace cache contents with snapshots prepared
  by the requesting workflow. Stores do not load full refreshes. `AppDataRefresher`
  loads all ten families through repositories and
  constructs their snapshots off-thread, then publishes them inside one shared
  UI-thread batch. Concurrent full-refresh requests share the active run; a
  cancellation or load failure leaves the previously published state intact.
  Video refresh loads its narrower snapshot set through its coordinator.
  Sync uses affected-ID publication, including for segments.
- Publication methods accept prepared snapshots or ids to remove. Segment
  publication combines changes and removals in one cache update; segments are keyed
  by source ownership and survive session deletion; video
  workflows perform file-presence checks before passing their snapshots.
  `BikeCoordinator`, `SetupCoordinator`, `SessionCoordinator`,
  `TrackCoordinator` and `PairedDeviceCoordinator` own their respective writes.
  Import, live save and recompute publish snapshots returned by persistence;
  sync prepares snapshots after applying transferred data. A committed write
  finishes publication without checking cancellation between those steps.
- Bike save compares authored draft-base fields once after validation, before writing;
  session metadata save has one check in its command service. Missing sessions
  are workflow outcomes, not store results. Stores have no mutation-result union.
- The shared sync applier prepares authoritative snapshots off-thread and
  publishes all affected row families in one batch, with removals when a
  reconciled row no longer exists. Both hub endpoints and client phases await
  this publication. Source
  publication contains only metadata; payload reads use the source repository.

`BikeSnapshot` retains `HasImage` rather than encoded image bytes. Startup/full
refresh uses `IBikeMetadataReader` to select metadata and image presence without
materializing the image payload. A bike editor requests a complete `BikeDraft`
through its coordinator only when the snapshot has an image; new imported drafts
already carry their bytes. Setup export reads the complete embedded bike on demand.

Snapshots are immutable records, not view models. Persisted entities carry
an authored `Updated` timestamp. Editors compare authored draft fields with an
immutable draft base; the timestamp is not an editor conflict baseline. Runtime-only or metadata-only snapshots such
as `LiveDaqSnapshot`, `PairedDeviceSnapshot`, and `RecordedSessionSourceSnapshot`
do not expose an editor baseline.

`TrackStore` holds each active track's content ID, fractional start/end time and
local points revision. Pure selectors read `GetAll()` for a snapshot of that
metadata. `ITrackRepository.GetTrackSnapshotsAsync` reads only those
columns; point JSON stays outside the store. Startup/full refresh loads tracks
with the other families. GPX import publishes the committed metadata; recording
import, live save and recompute publish tracks with their session/source changes.
Session deletion publishes the track removal returned by its transaction, and
recompute removes an old orphan after its deletion commits. Both incoming sync directions publish affected track snapshots in the shared row batch.

`SessionSnapshot` is metadata-only: it carries BLOB availability projected from
`data IS NOT NULL`, the processing fingerprint and duration. Session reads join
GPS distance/ascent/descent and `GpsMetricsKey` from the device-local metric table
onto the same snapshot. Metadata write models and sync payloads exclude those
GPS values. Telemetry and raw-source payloads remain in SQLite and load on demand.

`SessionStore` additionally exposes `Watch(Guid)`, a low-level per-id
observable filtered to `Add`/`Update` change reasons. Recorded-session
screens consume the higher-level `RecordedSessionProjection` instead, so
they see session metadata together with setup, bike, source, and
staleness state.

`RecordedSessionSourceStore` is the metadata cache for
`session_recording_source`: source kind, source name, schema version,
and source hash. The full payload is not kept in the store. Recompute loads it directly through
`IRecordedSessionSourceRepository`; the store exposes metadata only. Import and
live save publish prepared metadata from the persisted source, and client sync
collects the metadata of committed transfers before publishing once, including
when another transfer is canceled or fails. `InboundSyncCoordinator` validates
uploaded sources off-thread, persists them, and publishes the committed metadata
before the server acknowledges the upload. Stores never read repositories to prepare publication.

`SetupStore` holds setup metadata only. Board associations live in `BoardStore`;
`ISetupBoardQuery` joins them for setup lists, editor creation, and import selection.

`LiveDaqStore` is a runtime-only store that does not persist to the
database. It has no `RefreshAsync()`, its snapshots carry no `Updated`
timestamp, and it is populated entirely by `LiveDaqCoordinator` from
discovery and known-board query results. Its writer interface exposes
`ReplaceAll(IEnumerable<LiveDaqSnapshot>)`; `LiveDaqCoordinator.ReconcileLocked` uses
`ReplaceAll` to publish a fresh reconciled set in one update. See
[Live DAQ Streaming](live-streaming.md) for the full feature
architecture.

## Recorded Session Projection

`IRecordedSessionProjection` is the read-side projection for recorded
session screens. It tracks changes in `ISessionStore`, `ISetupStore`,
`IRecordedSessionSourceStore`, `ITrackStore`, `ISessionVideoStore` and `IProcessingDependencyHashIndex`, then schedules
work from `StorePublication.Committed` for those source stores (including bikes).
The dependency hash index owns
the processing-relevant setup/bike store edge. The projection joins
current session/setup/bike/source snapshots from stores (the derivation
window is part of the session snapshot), evaluates processing staleness, and publishes two reactive surfaces:

- `ConnectSessions()` — a DynamicData stream of `RecordedSessionSummary`
  records for the session list, including the latest source-owned clip overlapping
  the session window. `SessionListViewModel` filters and
  sorts this stream, then projects the visible rows into local-date
  groups that can be expanded and collapsed. `SessionRowViewModel`
  displays `(Stale)` when the summary is stale and `(No Raw)` when the
  processed data may exist but the raw source is unavailable; it also
  exposes current-culture title, time-of-day, and subtitle strings for the
  grouped desktop and mobile list templates.
- `WatchSession(sessionId)` — a replaying observable of
  `RecordedSessionDomainSnapshot` for the recorded detail editor. The
  snapshot carries the session, setup, bike, current fingerprint,
  persisted fingerprint, source metadata (of the session's recording
  source), selected track metadata, and staleness result. It carries facts, with no emitted-change flags.

The recorded detail editor compares each domain against its last handled domain,
so deferred updates are compared with what the editor actually consumed. Runtime
session values come from the editor state's domain/load snapshot; no mutable
session entity or second snapshot is synchronized beside it.

The two reaction axes remain independent. Processed-generation, selected-track,
window, timestamp and GPS-offset changes refresh displayed telemetry/track without
replacing a metadata draft. Changes to fields saved by the editor (name,
description, setup and suspension tuning) are absorbed when clean, or prompt to
reload when local edits exist. Declining leaves the immutable `SessionDraft`
base unchanged. `SessionCoordinator.SaveAsync(draft, draftBase)` compares those
fields against the current store snapshot; unrelated authored/derived changes do
not conflict, and a declined metadata change still conflicts after later derived
updates. Saving returns the persisted snapshot. The editor advances its draft
base and remains dirty if the user made further edits while saving.

Recompute and GPS-offset adjustment are one-way: they persist and upsert
the session store, and the refresh reaches the editor through this same
watch reaction rather than a pushed result.
`ISessionRecomputeEngine.RequestRecomputeAsync(sessionId)` is
baseline-free — the `SessionRecomputeEngine` owns serialization and liveness.
`SessionCoordinator.ReconcileStalenessAsync` returns `Current`, `Active`,
`Required`, or `Unavailable` when inspecting a domain snapshot, and executes
confirmed single-session or bulk choices. Missing raw sources are unavailable
even when existing processed data remains current; missing setup/bike inputs
have their own presentation. Neither condition blocks opening available telemetry.
The editor owns prompt visibility and remembers the prompted fingerprint pair;
unchanged inputs do not prompt again, while changed processing inputs do.
Inactive tabs defer this presentation in both layouts. Domain reactions enter
directly from the projection watch, stay retained while pending, and drain on
final close. Bulk recompute completion is also delivered to shell feedback.

Recorded-session analysis result state is scoped to an open
`SessionDetailViewModel`. The editor creates one
`IRecordedSessionAnalysisResultState` through a singleton factory and exposes it
through `SessionAnalysisWorkspaceViewModel`; the heavy work is delegated to the
singleton `IRecordedSessionAnalysisComputer`. The state is invalidated when the
telemetry generation, selected analysis range, analysis modes, damping cutoffs,
or insights target changes. Session Insights computes its damping percentages
from those inputs internally; percentages are a result, never an analysis key input. Visible analysis surfaces then
request keyed results on demand: the editor eagerly requests the shared damping
percentages and optional insights records, while selected/visible analysis tabs
and plot controls request only the result records they render. Cached records are
reused within the open editor until their semantic keys no longer match. Preview
cutoffs key percentages/insights; committed plot cutoffs key velocity and balance
results, so a drag does not invalidate the committed plots.

The result state is UI-thread-owned: input changes, cache edits and notifications
are synchronous UI operations. Workers receive captured telemetry/key values and
marshal their completion back for a single guarded cache/publication step. Caller
cancellation only stops that caller waiting; supersession or disposal cancels the
owned work. Async disposal drains all work, including displaced jobs. Calculation
failures carry a closed reason and separate diagnostics, logged at this boundary.
The editor has no analysis scheduler or suppression batches. It derives requests
from current input keys, eagerly requests damping percentages, and retains Insights
demand after a visible surface requests it. The reducer clears stale percentages
and Insights with the input change; results arrive as later accepted inputs.

Projection recomputes are coalesced through the existing
`IRecordedSessionProjectionScheduler`; its UI-background-priority callback captures
the current session/setup/bike/source snapshots, track/video catalog snapshots and dependency hashes. Fingerprint
staleness and track selection evaluation runs through `IBackgroundTaskRunner`, outside the UI
thread. A single later dispatcher publication batch edits all affected summaries
and emits their domain snapshots. Inputs are checked again before publication;
obsolete results are discarded and the current inputs are evaluated instead.
Track and video changes queue all sessions; a changed catalog rejects pending derived selections before publication.
The projection's own commit names only the projection, so it cannot trigger itself.

Consumers retain the previous result while the same session is being refreshed.
A session removal clears its summary and completes its watch synchronously in
the source commit's dispatcher turn, without waiting for pending evaluation.
Watches are subscription-scoped: the first subscriber creates a replay subject,
and the last subscriber releases it. Resubscribing replays the latest domain.
Per-session preference streams in `AppPreferences` use the same subscription
lifetime: no subject exists solely because an observable was requested or a tab
used to be open. Initial preference replay and subscription registration share
the document gate, so a committed preference update cannot fall between them.

A source change also queues every session whose `source_session_id` points
at that source. `IProcessingDependencyHashIndex` queues setup dependency changes.
Bike-store changes also queue their sessions so the projected bike, including
cutoffs and its command revision, stays current even when processing inputs have
not changed. The shared pending-id set coalesces these signals into one evaluation.
A metadata-only bike change leaves the fingerprint current. The editor reducer
accepts the projected bike and staleness together and resets cutoff preview/plot
values when the bike identity or persisted cutoffs change.

`ProcessingFingerprintService` is the pure derivation service behind
the projection. It parses the persisted fingerprint JSON from
`SessionSnapshot`, computes the current fingerprint from session,
setup, bike, recorded-source snapshots, a cached setup/bike dependency hash
from `IProcessingDependencyHashIndex`, and the session's clamped
velocity-filter processing option (derived from the session snapshot's nullable column), and the session's optional derivation window, then
classifies staleness as:

- `Current` — processed data matches the recorded source and current
  processing inputs.
- `Stale` — recomputable when setup, bike and raw source are available, whether
  the BLOB is absent or any fingerprint input differs. Currentness is complete
  fingerprint equality; the evaluation retains both fingerprints for inspection.
- `MissingDependencies` — stale but not recomputable until the setup
  or bike is restored.
- `MissingRawSource(ProcessedStateStale)` — displayed as "No Raw" and
  not recomputable because the raw source is unavailable. Existing
  processed data can still be opened. `ProcessedStateStale` preserves
  whether the processed BLOB/fingerprint is known to be stale even
  though the app cannot repair it until the source is restored.

Processing-option changes arrive through the session store and invalidate the
same projection as other session inputs. There is no preference-backed option
cache or hydration gate. Startup copies legacy preference values once into the
session row; it does not reset options or automatically recompute old results.

`IRecordedSessionDomainQuery` is the command-side companion. It reads
the current session/setup/bike/source snapshots synchronously from
stores, resolves the source row through the session's `RecordingSourceSessionId`,
and returns one `RecordedSessionDomainSnapshot` for workflows such as the
recompute engine. Coordinators use the
query for current-state decisions; they do not subscribe to the projection
stream.

## Pairing and sync state

`HttpApiService` owns the paired peer graph, including credential expiration and
retirement. `PairingClientCoordinator` derives `Unpaired | AwaitingPin | Paired`
from that peer and its pending request. `SyncCoordinator` combines this with its
operation into one replayed `SyncState`; availability and busy flags are derived.
The queries remain connected for their owners' lifetimes, so reopening a surface
does not replay stale pairing state. Shell feedback and the pairing page observe
immutable snapshots through the UI dispatcher and dispose their subscriptions
with their singleton view-model lifetime.

## Queries

Queries answer business questions across entity families without
going through view models. They are stateless singletons in
each slice's `Queries/` folder (e.g. `Bikes/Queries/`, `LiveDaq/Queries/`).

`IBikeDependencyQuery.IsBikeInUseAsync(Guid)` (backed by
`BikeDependencyQuery` over `ISynchronizableRepository<Setup>`) reports whether any
setup currently references a bike. `BikeCoordinator.DeleteAsync` uses
it to short-circuit deletes with `BikeDeleteResult.InUse`. That authoritative
answer comes from the database. For command availability the query delegates to
`ISetupStore.IsBikeReferenced` and exposes its change stream directly; it owns
no duplicate setup cache or subscription.

`ILiveDaqKnownBoardsQuery` derives known board rows from `IBoardStore`, `ISetupStore` and `IBikeStore`. It captures committed snapshots, builds names, calibration and live-session contexts off-thread, and publishes on the UI dispatcher. The latest input replaces a pending computation; disposal cancels observation and suppresses late publication. Boards without setups remain visible. It has no repository dependency or fallback through setup snapshots. See [Live DAQ Streaming](live-streaming.md).

`BoardStore` is refreshed with the startup/full-refresh batch. Setup save, delete and import transactions return affected board IDs; `SetupCoordinator` reads their authoritative snapshots and publishes them with setup/bike changes in one batch. Desktop inbound sync prepares authoritative board snapshots even for board-only arrivals and publishes removals for missing rows. `SetupSnapshot` contains no board ID. `ISetupBoardQuery` emits `SetupDetails` rows only after a relevant publication commits, so setup/board batches are observed coherently. Board-only changes update list rows and subsequent editor creation. Its exact-board lookup resolves import selection through `BoardSnapshot.SetupId`; the displayed association uses the lowest board ID if several boards reference one setup. Its change stream also notifies lookup consumers when a second board changes without changing that displayed association.

`IRecordedSessionDomainQuery` lives in `RecordedSessionProjection/` rather than
`Queries/`, but it follows the same command-side rule: it answers one
current business question without owning a collection. It joins the
current session, setup, bike, and recorded-source snapshots and
returns the same domain snapshot shape that `IRecordedSessionProjection`
publishes. The `SessionRecomputeEngine` uses it for the staleness gate
before loading the raw source and a still-current guard before
committing recomputed data.
