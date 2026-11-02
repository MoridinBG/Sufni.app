# UI Workflows, Composition, and Navigation

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers the presentation workflow layer: coordinators, dependency injection, and shell navigation. Presentation invariants live in [UI Architecture](ui.md), while read-state ownership lives in [UI State, Read Graphs, and Queries](ui-state.md).

## Coordinators

Coordinators own feature workflows. They are the only layer view models call
for persisted write workflows and the only layer that subscribes to
synchronization events. Successful editors request `IShellCoordinator.CompleteEdit`;
the shell alone applies post-save back navigation in the compact profile.
Coordinators and their command services persist through repositories and prepare
snapshots for publish-only store writers. Cross-aggregate writes are owned by
workflow transaction runners or command services and publish affected stores only
after the SQLite transaction commits. Coordinators never mutate store caches directly. They live
in each slice's `Coordinators/` folder
(e.g. `Bikes/Coordinators/`, `SyncAndPairing/Coordinators/`, `Shell/Coordinators/`;
the session workflow owners live in `Sessions/Coordination/`)
and are registered as singletons.

| Coordinator                                                               | Lifetime     | Owns                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| ------------------------------------------------------------------------- | ------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `IShellCoordinator` (`ShellWorkspaceCoordinator`)                         | shared       | `OpenEditor` / `CloseEditorAsync` / `CompleteEdit` / `SwitchLayoutProfileAsync` / `Open` / `Close` / `GoBack` — the only navigation surface; delegates to `ShellWorkspaceViewModel` in both layout profiles                                                                                                                                                                                                                                                                              |
| `BikeCoordinator`                                                         | shared       | Own repository writes and prepared-snapshot publication for save, cutoff updates and delete (gated by `IBikeDependencyQuery`); save checks conflicts after analysis, and route bike-editor analysis/import/export/image requests through `IBikeEditorService`. Both interfaces expose neutral result records; image loading returns encoded bytes and a file name, leaving Avalonia bitmap decoding to the editor presentation edge                                                                                                                                                                              |
| `SetupCoordinator`                                                        | shared       | Same as above + the `Board` row association (clears the previous board on save / delete); setup creation accepts a suggested board id from the selected import source; transactions return affected board IDs and the coordinator publishes authoritative board snapshots with related setup/bike changes in one batch                                                                                                                                                                                                                                                                                                                             |
| `SessionCoordinator` | shared | Owns metadata save, processing-option changes, GPS alignment and deletion. It persists through repositories and publishes prepared snapshots; it owns no event subscriptions and is not eagerly resolved. |
| `ISessionEditingCoordinator` (`SessionEditingCoordinator`)                | shared       | Trim and split commands of recorded sessions: one transaction per command, then session publication, recompute, and commit progress carrying the split's new session id. See [Trim and split](#trim-and-split) |
| `ISegmentCoordinator` (`SegmentCoordinator`)                              | shared       | Saved-segment create, rename, and delete with the segment name rule; segment matching and comparison pages; Strava connect, reset, and segment fetch. The per-editor segments picker, comparison, and Strava connect dialog call it. See [segments.md](segments.md#store-and-coordinator) |
| `PairedDeviceCoordinator` | shared | Persists local unpair and publishes the removal. No server-event subscriptions or eager resolution. |
| `ImportSessionsCoordinator`                                               | shared       | Owns one active import task and replayed `SessionImportState` across tab lifetimes, runs the full per-file import / trash workflow off thread, reads original SST bytes, persists raw source + processed session + optional generated track atomically, reports per-file progress, and publishes prepared session/source/track snapshots through store writer publish-only paths                                                                                                                                                                |
| `SyncCoordinator`                                                         | shared       | replayed `SyncState`, explicit confirm-and-sync, client phases, cursor writes and source publication; row merges use the shared `SynchronizationApplier`                                                                                                                                                                                                                                                                                    |
| `IPairingClientCoordinator` (`PairingClientCoordinator`)                  | mobile only  | `DeviceId` / `DisplayName` / `ServerUrl`, derived `PairingClientState`, explicit initialization, mDNS browse lifetime and captured peer resolution; request/confirm/unpair commands                                                                                                                                                                                                                                                                                                                     |
| `IInboundSyncCoordinator` (`InboundSyncCoordinator`) | desktop only | Endpoints await pairing, row push/pull and BLOB/source upload operations. The coordinator owns PIN challenges and expiry, credential rotation, and paired-device publication. Push awaits the shared `SynchronizationApplier` for merge and publication of all seven synced row families before acknowledgement. Source uploads validate and publish committed captured content; BLOB uploads use the session applier. Owns the declared peer-run activity and silence deadline; no server-event subscriptions or direct eager resolution. |
| `IMapLayerCoordinator` (`MapLayerCoordinator`) | shared | Persists custom-layer additions and selection off-thread; typed outcomes distinguish add/selection failure. `TileLayerService` derives its catalog from custom-layer store facts and selection from device-local preferences. |
| `TrackCoordinator` | shared | Owns key-driven GPS metric recomputation, conditional persistence and cancellation/drainage. GPX import publishes track metadata; read-only session-track loading selects the greatest overlapping track from `TrackStore`, reads exact full-track points and derives the session window through `ISessionTrackReader`. GPS alignment belongs to `SessionCoordinator.ShiftGpsOffsetAsync` and uses the same selection rule. |
| `LiveDaqCoordinator`                                                      | shared       | Owns `LiveDaqStore` writes, browse lease lifecycle (activate/deactivate), discovery-to-known-board reconciliation. The shell routes diagnostics and live-session editor keys; `EditorFactory` creates their shared-stream and capture dependencies only for a new tab. Activates lazily when the Live primary page is selected — no constructor event subscriptions, so no eager resolution needed. See [Live DAQ Streaming](live-streaming.md); capture preparation, reprocessing, persistence, coherent publication and post-save reset through `SaveCaptureAsync`; reserved management operations and CONFIG selection, with retained tasks canceled/drained on async disposal. |

The coordinator's metadata save checks the store snapshot for conflicts before
persisting and publishing; it never writes the session timestamp or GPS offset.
`ShiftGpsOffsetAsync` owns alignment, deriving a track window from persisted
inputs and publishing the aligned session after its repository write. Live save derives and persists the session, raw source
and optional generated track, then publishes the resulting snapshots. Delete
uses the transaction runner for session, source and track removal, then publishes
the returned source/track removals with the session removal. The runner
selects the current track and checks other active sessions through the same overlap rule inside the delete
transaction. The coordinator clears recorded preferences, then publishes session
and source removals together in one dispatcher-owned `StorePublication` batch.
Import and live-save publication use the same batch boundary for their session
and source snapshots.

Recorded editors call `ISessionLoader` directly for staged telemetry and track
reads. Session lists and the staleness reconciler call `ISessionRecomputeEngine`
directly; that owner retains cancellation, supersession and commit guards.
`SessionCoordinator` has no forwarding-only methods or command-service wrapper.

Session readers and other workflow owners live beside the coordinator (all in `Sufni.App/Sufni.App/Sessions/Coordination/`, registered as
singletons):

| Use case            | Owns                                                                                                                                                       |
| ------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ISessionLoader` (`SessionLoader`) | Local-only staged detail reads. It depends on read-only `ISessionStore` (never `ISessionStoreWriter`) and reads the session timing and selects the expected track from `ITrackStore`. `LoadDetailAsync` decodes and validates telemetry for first-content publication, while `LoadTrackAsync` resolves the optional map/track payload later. Missing processed telemetry returns an incomplete-local-data result. Available telemetry opens even when its source is missing or its fingerprint is stale; the domain projection drives the source warning or recompute prompt. Track failures remain a typed failed settling result |
| `SessionRecomputeEngine` | Serialized, per-session **cancel-and-replace** recompute engine and the single owner of recompute liveness. `RequestRecomputeAsync` rebuilds processed telemetry from the raw source against the current setup/bike inputs and the session row's velocity-filter option; the reprocessor returns a pure `ProcessedGeneration` with telemetry, serialized bytes, fingerprint JSON, projected GPS points. The engine creates/reuses the content track, prepares the session and validates the result fingerprint against a captured database input bundle, then calls `ISessionRepository.UpdateProcessedDerivedDataAsync` with those expected scalar values. After publication, the engine conditionally deletes the previously selected track in a transaction that verifies no active session still selects it. A newer request for the same session cancels and replaces the in-flight one (which resolves to `Superseded`), and a run whose DB inputs change underneath it re-enqueues itself until it converges. `IsActive` reports an in-flight run. Cancellation bounds correctness (no stale commit), not CPU — an in-flight reprocess is not interrupted, only prevented from committing. `RequestRecomputeAllAsync` enumerates active ids through the session-id projection (`ISessionRepository.GetActiveSessionIdsAsync()`), excludes sessions currently classified `Current`, and fans the remainder through `RequestRecomputeAsync` with a degree of parallelism scaled to `Environment.ProcessorCount`, returning a `SessionRecomputeAllResult` tally and reporting per-session progress through an optional `IProgress<SessionRecomputeAllProgress>` so the caller can drive a determinate progress dialog; sessions that disappear or cannot be recomputed are counted and skipped, not surfaced as failures. Completed fingerprints are the restart checkpoint; bulk runs are not resumed automatically. The staleness prompt (`SessionStalenessReconciler`) offers "Recompute all" alongside "Recompute" — it builds those buttons through the dialog service's generic `ShowChoiceAsync` prompt (the service stays recompute-agnostic; the reconciler owns the choice ids and what they mean) and runs the bulk recompute behind a modal, equally generic `ShowProgressAsync` loading dialog |
| `ISessionSyncApplier` (`SessionSyncApplier`) | Owns incoming processed generations on hub and client: validates bytes, normalizes duration, applies guarded writes and publishes committed sessions. The inbound coordinator awaits upload application; client transfers call it directly. It distinguishes applied/ignored generations from pending transfers; terminal ignores clear only a matching hub request. It has no source or server-event responsibilities. |

### Startup and database recovery

`AppStartup` owns the initial database gate, the single-flight store refresh,
tile-layer initialization, and server startup. `App` builds one provider and a
shell root before asking startup to activate the primary pages. Schema work runs
off-thread; initialization or refresh failure prevents page creation. After the
refresh, the composition callback resolves subscription owners and pages before
the server can accept arrivals. `ShellRootViewModel` observes the shell startup
state: `Starting`, `Ready`, `Failed(reason, details)`, or `RelaunchRequired`.
Both layout profiles block their normal surfaces until `Ready`.

Database failure offers the log location and reset. Reset closes the existing
connection, archives remaining database files, creates the replacement through
normal migrations, and requires relaunch. It never rebuilds stores, coordinators
or pages in-process. Reset failure stays blocked and permits retry; successful
reset exposes only Quit. Startup logs database initialization failure once.

After readiness, `AppStartup` retains the source-retention cleanup and processing
option normalization tasks. Its stop operation cancels and awaits those jobs and
waits for structural migrations or a reset to settle. Normalization passes the
cancellation token into recompute and leaves its marker unwritten on interruption.

`TrackCoordinator`, `PairingClientCoordinator` and `SyncCoordinator` are
resolved at page activation, before server startup. `SessionSyncApplier` is
registered on every platform and is called directly by transfer owners.

Recompute requests and bulk recompute accept cancellation. The engine retains
all outstanding tasks, including displaced per-session runs, so `StopAsync`
waits for work that cannot yet be interrupted before the provider is disposed.
Shutdown also cancels and drains the track coordinator’s input evaluation and GPS
metric tasks before finalizing deletes and disposing services.
Canceled processing cannot install a stale result. Bulk cancellation stops
scheduling further sessions; fingerprints remain the checkpoint for later work.

### Shutdown and mobile background

`AppShutdown` closes background tabs first and the focused tab last, using each
tab's `TryPrepareCloseAsync`. Dirty editors and unsaved captures retain their
save/discard/cancel policy; Cancel stops the shutdown before work is canceled.
After the tabs close, the shell cancels and awaits startup jobs, import, sync and
recompute, finalizes pending deletes, stops/disposes the sync server, disposes
the application provider, and flushes/closes logging. Concurrent quit requests
join the same shutdown. The normal shell surface is disabled during the request.

`App` intercepts both desktop `ShutdownRequested` and main-window `Closing`;
the latter keeps the window present while close prompts run. Native shutdown is
allowed only after the shell drain completes. Android pause and iOS background
instead finalize pending deletes and flush logs without closing tabs or stopping
other work. The iOS head requests a background execution window for that drain.

### Processing option changes

`SessionCoordinator.SetProcessingOptionAsync` persists the session's filter option
through `ISessionRepository`, publishes the resulting snapshot and starts
recompute before queued projection work can prompt. Failure restores the previous
option only when no newer option has replaced it; supersession leaves the newer
request in charge. `ProcessingPreferenceWorkflow` forwards the slider intent and
projects the read-only store and typed result. Preferences no longer persist this
field. Live-save metadata and split children carry the session option.

### Pending deletes

`PendingDeletes` is the shell's application-lifetime owner of five-second undo
windows for bikes, setups, sessions and paired devices. Lists schedule typed
keys and project the owner's pending entries; undo removes the intent. Expiry,
dismissal and explicit finalization invoke the owning feature coordinator off
thread. The shell closes an editor only after successful deletion. A failed or
blocked delete removes the pending intent, reveals the row, and reports its
outcome to the owning list, including while that list is detached.

The owner retains commits in flight so finalization waits for them without
issuing the same delete twice. Pending intents are in-memory state and end with
the process; desktop shutdown and mobile background explicitly finalize them.
`PendingDeleteEntryViewModel` forwards undo/dismiss intent and has no timer or
commit callback. `ItemListViewModelBase` owns only the undo-bar projection and
list feedback; hiding rows derives from the shell owner's current keys.

### Trim and split

`ISessionEditingCoordinator` (`SessionEditingCoordinator`, singleton) runs the
recorded-session window edits from the signal-row context menu of the session
editor: "Trim from beginning", "Trim until end", "Split here", and "Undo trim".
`SessionWindowEditPolicy` owns the pure sample-count gate and window/origin
arithmetic. The editor combines that gate with its screen state. Inside each
`SessionPersistenceTransactionRunner` transaction, the runner checks the expected
window, asks the policy for immutable patches, and applies the planned session changes. Source-owned segments and clips remain unchanged:

- **Trim until end** ends the window at the click.
- **Trim from beginning** moves the window start and the session origin to the
  click. The timestamp takes the whole seconds; timeline reads derive the fractional
  origin from the source window. Authored GPS alignment is unchanged.
- **Undo trim** restores the previous window and origin. A window equal to the session's own full range is stored as no
  window.
- **Split** ends the window at the click, renames the session "(1)", and
  inserts a "(2)" session over the rest of the window. Both windows derive their
  segments and clips from the shared source.

The transaction rejects the edit when the stored window differs from the
snapshot the command started from, so concurrent edits of one session do not
need a lock. After the commit the coordinator publishes the changed sessions,
requests their recompute before any
other await (an open editor prompts for a recompute of a stale session unless
one is already running), reports a `SessionEditProgress` with
the created id, and waits for the recomputes. An applied edit retains a typed recompute failure
when rebuilding cannot finish; the editor presents its reason and separate
diagnostics without treating the committed edit as failed. The editor routes that progress to
`shell.OpenEditor(new EditorKey.Session(id), background: true)` before the
coordinator completes. Until a recompute
completes, the session shows as stale and then reloads, as after a built-in
recalculate; edits do not use the operation overlay. The editor allows one edit
at a time and enables a command only when every remaining part keeps at least
five samples and a trim removes at least one.

## Dependency Injection

Each platform head constructs a `PlatformRoot` containing capabilities, input
capabilities, `LaunchPreferences`, `IAppPaths`, and its platform-service
registration delegate. It passes that root to `App` through the Avalonia
application factory. `AppComposition.AddSharedServices` installs the platform
and shared registrations into a new collection; `App` builds the provider with
`ValidateOnBuild = true`. There is no static service collection.

`AppPreferences.ReadLaunchValues` reads the preferences document once. The launch
snapshot supplies the first-frame theme and layout profile, and seeds runtime
`AppPreferences` without another read. It also captures legacy custom layers for the
SQLite migration; startup then rewrites the normalized device-local document
before activating pages. Each head chooses the default layout and
app-data directory. Paths reach persistence, logging and certificate storage
through the root. `TimeProvider.System` is registered once for live cadence, pairing/sync, persistence timestamps, video,
Strava and notification/undo expiry. `IUiTimerScheduler` supplies UI cadence: view
models receive it through constructors, while main/dialog roots provide the
inherited `UiTimers.Scheduler` property for XAML-created controls.

Shared registrations in `AppComposition`:

- **Shell**: `ShellWorkspaceViewModel`, `ShellRootViewModel`, and the single
  `IShellCoordinator` implementation, `ShellWorkspaceCoordinator`, are shared
  registrations. Single-view lifetimes host the shell in `MainView` and wire
  native back requests to the shared shell root.
- **Services**: `IBackgroundTaskRunner`,
  `IUiThreadDispatcher`, `IUiTimerScheduler`, `IDaqDriveProbe`,
  `IDaqManagementService`, `ITelemetryDataStoreService`,
  SQLite repository interfaces, `ISyncDataStore`,
  `IFilesService`, `ITileLayerService`,
  `IMapViewModelFactory`, `ISessionTrackReader`, `IFullTrackPointReader`,
  plus `IAppPreferences` and its `ISessionPreferences` and `IUiPreferences` facets —
  registered as singletons via factory delegates that resolve the
  same `IAppPreferences` instance. The concrete `DialogService`
  singleton is re-registered behind `IDialogService` (the `Show*`
  prompt contract for view models and coordinators) and `IDialogHost`
  (the owner-window/overlay-host/presentation-mode wiring contract
  used only by `App`) via factory delegates that resolve the same
  instance. Recorded-session derivation
  services (`IProcessingFingerprintService`,
  `IRecordedSessionReprocessor`, `IRecordedSessionAnalysisComputer`) are
  also singleton services. `IRecordedSessionAnalysisResultStateFactory` is
  a singleton factory; each result state it creates belongs to one open editor
  with its own cache and cancellation scope. `IRecordedSessionSourceSyncQuery` is the
  source-sync read helper that combines the repository's missing recording
  sources with the sources referenced by derived sessions. The recorded-source
  factory is static and stays in `RecordedSessionProjection/` beside the
  reprocessor.
- **Data refresh**: `AppDataRefresher` is registered behind
  `IAppDataRefresher`. It refreshes
  bikes, setups, sessions, recorded-session sources, paired devices, session
  videos, session segments, tracks, boards and custom tile layers off-thread. It prepares every snapshot before
  publishing one UI-thread batch. Concurrent requests share the active refresh;
  failure or cancellation before publication preserves all existing stores.
- **Stores**: each concrete store registered as a singleton, then
  re-registered behind both its read and writer interfaces via
  factory delegates that resolve the same instance. This includes
  `RecordedSessionSourceStore`, registered behind
  `IRecordedSessionSourceStore` and `IRecordedSessionSourceStoreWriter`.
  Persisted writer interfaces expose replacement and publication only; `LiveDaqStore` remains runtime-only.
- **Coordinators**: every shared entity coordinator plus `SyncCoordinator` and
  `ImportSessionsCoordinator`. `ISynchronizationApplier` is shared by client pulls
  and hub pushes to merge rows and publish affected IDs together. None depends on an editor factory or shell.
  `ShellWorkspaceCoordinator` holds `IEditorFactory`; `CreateEditor(key, shell)`
  receives its shell at call time. No `Func` cycle breakers remain.
- **Queries, indexes, and read graphs**: `IBikeDependencyQuery`,
  `ISetupBoardQuery`, `ILiveDaqKnownBoardsQuery`, `IRecordedSessionDomainQuery`,
  `IProcessingDependencyHashIndex`, and `IRecordedSessionProjection`.
- **Live DAQ**: `LiveDaqStore` (singleton behind both
  `ILiveDaqStore` and `ILiveDaqStoreWriter`),
  `IDaqBrowseOwner`, `ILiveDaqBoardIdInspector`,
  `ILiveDaqCatalogService`, `ILiveDaqClientFactory`,
  `ILiveDaqSharedStreamRegistry`, `ILiveSessionServiceFactory`,
  `LiveDaqCoordinator`, `LiveDaqListViewModel`. All registered
  unconditionally; `MainPagesViewModel` receives
  `LiveDaqListViewModel` as a required dependency on both shells.
- **Global feedback/actions**: `ShellFeedbackViewModel` is a singleton shared
  by the root and primary-page composition. `IShellActions` exposes its sync
  command and import availability to lists without exposing another feature's
  view model. `ShellDispatcherExceptionHandler` is resolved once by `App` and
  releases its dispatcher subscription when the provider is disposed.
- **View models**: list view models,
  `ShellWorkspaceViewModel`, `ShellRootViewModel`, and
  `MainPagesViewModel` as singletons. `MainPagesViewModel` receives
  platform-optional page view models such as pairing client/server surfaces
  based on the registered capabilities and services.

Concrete datastore construction, management-protocol ownership,
file-picker lifetime (including loaded `SelectedDeviceConfigFile` results
for device CONFIG replacement),
UI-thread dispatching, and background execution stay
behind these service registrations rather than being created ad hoc in
view models.

Platform entry points add (a strict subset depending on the
platform): `IAppEnvironment` with default layout profile, `AppCapabilities`,
and `InputCapabilities`; `ISecureStorage`; `IServiceDiscovery` (registered as
keyed singletons under `"daq"` and optionally `"sync"`); `IHapticFeedback`;
`IFriendlyNameProvider`; `IVideoPlayerFactory` on heads with
`AppCapabilities.CanPlayVideo`; `ISynchronizationServerService` +
`ISynchronizationTokenService` + `IInboundSyncCoordinator` +
`PairingServerViewModel` on server-capable desktop heads; or
`IPairingClientCoordinator` +
`PairingClientViewModel` and `IHttpApiService` on client-capable mobile heads.
Presentation selection flows through `IAppEnvironment.LayoutProfile`, and UI
actions are gated by capabilities. The native Avalonia lifetime selects
`MainWindow` or `MainView` at the composition edge.

After `BuildServiceProvider()`, the lifetime wiring resolves
`IDialogHost` and configures dialog presentation for the shell:
desktop sets `MainWindow` as both owner and overlay host with
`DialogPresentationMode.Window`; mobile/single-view sets `MainView`
as overlay host with `DialogPresentationMode.Overlay`. View models
never see this contract — they consume `IDialogService` only. Close confirmation
honors the configured mode: save/discard/cancel and unsavable-page confirmation
render as overlays on single-view hosts. Every overlay uses the explicitly
configured host; there is no application-lifetime lookup fallback.

`App`'s page-activation callback eagerly resolves `TrackCoordinator` and
`SyncCoordinator` for their store and inbound-activity subscriptions. `AppStartup`
awaits the mobile pairing coordinator’s explicit initialization before activating
pages; construction no longer starts credential/identity I/O. Inbound sync is reached through its state consumer and server; paired-device
commands and the session applier need no direct eager resolution.
`AppStartup` calls `ISynchronizationServerService.StartAsync` directly.

## Navigation

The sync coordinator reports the successful pairing milestone before awaiting
the initial sync. Its caller requests `GoBack` at that milestone, leaving rejected
PINs and failures on the pairing surface. Navigation stays in the view model.

Navigation is owned exclusively by `IShellCoordinator`. Features use
`OpenEditor(EditorKey, background)` and `CloseEditorAsync(EditorKey)`; the shell
also exposes `Open`, `Close` and `GoBack` for its existing non-editor surfaces.
Successful editors pass the key carried by their saved outcome to `CompleteEdit`
after accepting the saved baseline. It returns to the previous surface in compact
layout only while that editor is still current, and leaves workspace focus unchanged. Feature coordinators neither open nor close tabs.
`GoBack()` returns `true` only when the shared workspace consumed the back
request.

App-defined keyboard shortcuts are listed in
`KeyboardShortcutRegistry`, grouped by source and shortcut ID in
`GesturesBySource`. `App` creates the registry from `InputCapabilities` and
exposes it as an application resource. Shell and feature views resolve
`KeyBinding.Gesture` values through `ShortcutGestureExtension` instead
of hard-coding gesture strings. Command-style shortcuts use the
head-supplied `InputCapabilities.CommandModifier` (`Meta` / Cmd on macOS and
iOS, `Control` on Windows, Linux, and Android); shared code does not inspect the OS. Native text editing, focus traversal,
and control-internal keys stay local to their controls.

`ShellWorkspaceCoordinator` matches editor keys against open tabs before calling
`IEditorFactory.CreateEditor(key, shell)`. Reopening a key focuses its existing
tab; a background request preserves current focus. The factory reads the current
store snapshot (or joined setup/board details) and returns no editor for missing entities. It creates live
stream/capture dependencies only after deduplication, so a second request does
not create an unused capture service.

Persisted bike, setup and session keys have closed-tab restore entries. Restore
constructs a fresh editor from the current snapshot. New drafts, imports and
live tabs have no restore entry. Successful deletion uses `CloseEditorAsync`,
which performs tab cleanup and forgets its history, with no extra back step.
Normal close runs `TryPrepareCloseAsync` before removing the tab.

Import tabs are factory-created per open lifetime and dispose their run
observation and datastore collection handler on final close. The coordinator
retains the run and progress across those tab lifetimes. `GoBack` selects the
previously focused tab or returns to the primary surface.

`SwitchLayoutProfileAsync` persists the local preference and applies it on the UI
dispatcher without closing background tabs. Recorded-session loads and subscriptions
survive view replacement and end only on tab close; inactive-tab prompt deferral
applies in both profiles.

`ShellRootViewModel` is the data context for both platform lifetimes. The
layout profile controls presentation only:

- **Compact profile** — `CompactShellView` presents the shared workspace as the
  primary page plus the focused detail surface. Single-view mobile lifetimes
  host it through `MainView` so safe areas,
  plot overlays, and hardware back requests are wired to the native host. Back
  closes transient surfaces first, then delegates to `shell.GoBack()`.
- **Workspace profile** — `WorkspaceShellView` presents the same tab collection
  with a persistent rail and tab strip. Its middle-click close class handler is
  registered once, so recreating the shell cannot multiply close prompts. Reordering previews by fading the
  dragged tab and showing an insertion indicator, then commits through
  `ShellWorkspaceViewModel.MoveTab`.

The first screen is the primary navigation with no detail tab selected. The
workspace profile shows a neutral empty workspace state until a tab opens.
Profile-specific views are resolved by `ViewLocator` from
`IAppEnvironment.LayoutProfile`, not from platform.

Setup writes and transaction work run through the background runner; the
coordinator supplies prepared snapshots to the setup, bike and board stores after
commit. Desktop inbound sync reads and maps bike/setup/board rows off-thread
before publishing those snapshots in one batch.
