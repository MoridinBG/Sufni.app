# UI Workflows, Composition, and Navigation

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers the presentation workflow layer: coordinators, dependency injection, and shell navigation. Presentation invariants live in [UI Architecture](ui.md), while read-state ownership lives in [UI State, Read Graphs, and Queries](ui-state.md).

## Coordinators

Coordinators own feature workflows. They are the only layer view models call
for persisted write workflows and the only layer that subscribes to
synchronization events. Successful editors request `IShellCoordinator.CompleteEdit`;
the shell alone applies post-save back navigation in the compact profile. Single-aggregate persistence and cache publication live
on store writer APIs; cross-aggregate writes are owned by workflow transaction
runners or command services and publish affected stores only after the SQLite
transaction commits. Coordinators never mutate store caches directly. They live
in each slice's `Coordinators/` folder
(e.g. `Bikes/Coordinators/`, `SyncAndPairing/Coordinators/`, `Shell/Coordinators/`;
the Sessions router and its use-case classes live in `Sessions/Coordination/`)
and are registered as singletons.

| Coordinator                                                               | Lifetime     | Owns                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| ------------------------------------------------------------------------- | ------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `IShellCoordinator` (`ShellWorkspaceCoordinator`)                         | shared       | `OpenEditor` / `CloseEditorAsync` / `CompleteEdit` / `SwitchLayoutProfileAsync` / `Open` / `Close` / `GoBack` — the only navigation surface; delegates to `ShellWorkspaceViewModel` in both layout profiles                                                                                                                                                                                                                                                                              |
| `BikeCoordinator`                                                         | shared       | Save with conflict detection, delete (gated by `IBikeDependencyQuery`), and route bike-editor analysis/import/export/image requests through `IBikeEditorService`. Both interfaces expose neutral result records; image loading returns encoded bytes and a file name, leaving Avalonia bitmap decoding to the editor presentation edge                                                                                                                                                                              |
| `SetupCoordinator`                                                        | shared       | Same as above + the `Board` row association (clears the previous board on save / delete); setup creation accepts a suggested board id from the selected import source                                                                                                                                                                                                                                                                                                                             |
| `SessionCoordinator`                                                      | shared       | Thin router over `SessionLoader` (reads) and `SessionCommandService` (writes): telemetry-first detail loading and the later typed track-settling read both delegate to `SessionLoader`; save / delete / recompute requests delegate to `SessionCommandService`. It owns no event subscriptions and is not eagerly resolved                                                                                                                                                                                                                                                |
| `ISessionEditingCoordinator` (`SessionEditingCoordinator`)                | shared       | Trim and split commands of recorded sessions: one transaction per command, then session, segment, and clip publication, recompute, and commit progress carrying the split's new session id. See [Trim and split](#trim-and-split) |
| `ISegmentCoordinator` (`SegmentCoordinator`)                              | shared       | Saved-segment create, rename, and delete with the segment name rule; segment matching and comparison pages; Strava connect, reset, and segment fetch. The per-editor segments picker, comparison, and Strava connect dialog call it. See [segments.md](segments.md#store-and-coordinator) |
| `PairedDeviceCoordinator`                                                 | shared       | Local-only unpair; subscribes to the desktop server's `PairingConfirmed` and `Unpaired`                                                                                                                                                                                                                                                                                                                                                                             |
| `ImportSessionsCoordinator`                                               | shared       | Owns one active import task and replayed `SessionImportState` across tab lifetimes, runs the full per-file import / trash workflow off thread, reads original SST bytes, persists raw source + processed session + optional generated track atomically, reports per-file progress, and publishes changed session/source ids through store writer publish-only paths                                                                                                                                                                |
| `SyncCoordinator`                                                         | shared       | `IsRunning` / `IsPaired` / `CanSync` state, drives `SynchronizationClientService.SyncAll()`, then refreshes all stores through `IAppDataRefresher`                                                                                                                                                                                                                                                                                    |
| `IPairingClientCoordinator` (`PairingClientCoordinator`)                  | mobile only  | `DeviceId` / `DisplayName` / `ServerUrl`, mirrors HTTP `PairedState` as `IsPaired`, mDNS browse lifecycle, request/confirm/unpair HTTP plumbing                                                                                                                                                                                                                                                                                                                     |
| `IPairingServerCoordinator` (`PairingServerCoordinator`)                  | desktop only | Re-exposes `ISynchronizationServerService` pairing events as plain .NET events for `PairingServerViewModel`, plus `StartServerAsync()` passthrough                                                                                                                                                                                                                                                                                                                  |
| `IInboundSyncCoordinator` (`InboundSyncCoordinator`)                      | desktop only | Marker interface; constructor subscribes to `SynchronizationDataArrived` and publishes incoming bike/setup ids through writer publish-only paths after the server merge has already persisted them, and loads authoritative segment snapshots off-thread before replacing that store when segments arrive. Sessions and paired devices have their own coordinators, so each entity family has exactly one inbound writer                                                                                                                                                  |
| `TrackCoordinator`                                                        | shared       | GPX import and **read-only** session-track loading: it resolves the linked full-track points through `IFullTrackPointReader` and the cached session-window polyline through `ISessionTrackReader`, keyed by `points_revision` and `track_projection_revision` respectively. When the cached session-window track is missing or misaligned it regenerates points in memory for display only — track association and cached-track persistence are owned by the processed-write path. The GPS-offset adjustment is its one remaining write, committed through `ISessionStoreWriter.CommitTrackPatchAsync`; the editor refreshes through the session watch like a recompute, with no pushed result |
| `LiveDaqCoordinator`                                                      | shared       | Owns `LiveDaqStore` writes, browse lease lifecycle (activate/deactivate), discovery-to-known-board reconciliation. The shell routes diagnostics and live-session editor keys; `EditorFactory` creates their shared-stream and capture dependencies only for a new tab. Activates lazily when the Live primary page is selected — no constructor event subscriptions, so no eager resolution needed. See [Live DAQ Streaming](live-streaming.md) |

The session workflow itself is split into four use-case classes beside the
coordinator (all in `Sufni.App/Sufni.App/Sessions/Coordination/`, registered as
singletons):

| Use case            | Owns                                                                                                                                                       |
| ------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `SessionLoader`     | Local-only staged detail reads. It depends on read-only `ISessionStore` (never `ISessionStoreWriter`) and uses `Get(sessionId)` only to resolve the linked full-track id. `LoadDetailAsync` decodes and validates telemetry for first-content publication, while `LoadTrackAsync` resolves the optional map/track payload later. Missing local processed telemetry or recorded-source payloads return an incomplete-local-data result; track failures remain a typed failed settling result |
| `SessionCommandService` | Session write workflows: `SaveAsync` delegates metadata commits to `ISessionStoreWriter`; `DeleteAsync` clears stored preferences, always uses `SessionPersistenceTransactionRunner` to delete the session, the generated track, and the recorded source when no other live session derives from it in one transaction, then publishes session removal (and source removal when the source was deleted) after commit; create-only `SaveLiveCaptureAsync` persists live captures as processed session + raw source + optional generated track and publishes affected stores post-commit; `RequestRecomputeAsync` / `RequestRecomputeAllAsync` delegate to `SessionRecomputeEngine` |
| `SessionRecomputeEngine` | Serialized, per-session **cancel-and-replace** recompute engine and the single owner of recompute liveness. `RequestRecomputeAsync` rebuilds processed telemetry from the raw source against the current setup/bike inputs and the hydrated `IRecordedSessionProcessingOptionCache`; the reprocessor returns a `ProcessedTelemetryPayload` carrying the decoded telemetry, serialized bytes, and fingerprint JSON, and `SessionTelemetryWriter.UpdateProcessedDerivedDataAsync` commits that payload after the in-transaction DB-input fingerprint check. If that derived write replaces the session's previous full track, the engine deletes the previous track only after the active-session `full_track_id` projection shows it is no longer referenced. A newer request for the same session cancels and replaces the in-flight one (which resolves to `Superseded`), and a run whose DB inputs change underneath it re-enqueues itself until it converges. `IsActive` reports an in-flight run. Cancellation bounds correctness (no stale commit), not CPU — an in-flight reprocess is not interrupted, only prevented from committing. `RequestRecomputeAllAsync` hydrates the processing-option cache, enumerates active ids through the session-id projection (`ISessionRepository.GetActiveSessionIdsAsync()`), and fans them through `RequestRecomputeAsync` with a degree of parallelism scaled to `Environment.ProcessorCount`, returning a `SessionRecomputeAllResult` tally and reporting per-session progress through an optional `IProgress<SessionRecomputeAllProgress>` so the caller can drive a determinate progress dialog; sessions that cannot be recomputed are counted and skipped, not surfaced as failures. The staleness prompt (`SessionStalenessReconciler`) offers "Recompute all" alongside "Recompute" — it builds those buttons through the dialog service's generic `ShowChoiceAsync` prompt (the service stays recompute-agnostic; the reconciler owns the choice ids and what they mean) and runs the bulk recompute behind a modal, equally generic `ShowProgressAsync` loading dialog |
| `SessionSyncApplier`| Subscribes to the desktop server's `SynchronizationDataArrived`, `SessionDataArrived`, and `SessionSourceDataArrived`, publishing inbound session/source ids through writer publish-only paths after the server has persisted the payloads. The desktop sync server's PSST upload endpoint persists BLOB fills and swaps through `ISessionTelemetryWriter`; the resulting `SessionDataArrived` event is the only store-publication path for that upload |

`ISessionTrackReader`, `InboundSyncCoordinator`, `SessionSyncApplier`, `PairedDeviceCoordinator`,
`PairingClientCoordinator` on client-capable mobile heads,
`PairingServerCoordinator` on server-capable desktop heads, and
`SyncCoordinator` are eagerly resolved in `App.axaml.cs` after
`BuildServiceProvider()` so their constructor event subscriptions wire up
before any sync, pairing, or telemetry arrival can happen.

### Trim and split

`ISessionEditingCoordinator` (`SessionEditingCoordinator`, singleton) runs the
recorded-session window edits from the signal-row context menu of the session
editor: "Trim from beginning", "Trim until end", "Split here", and "Undo trim".
Each command is one `SessionPersistenceTransactionRunner` transaction:

- **Trim until end** ends the window at the click.
- **Trim from beginning** moves the window start and the session origin to the
  click. The timestamp takes the whole seconds and the GPS offset the fraction.
  The session's saved segments shift by the origin change, and its video
  alignment shifts by the change of the fraction.
- **Undo trim** restores the previous window and origin, and shifts the
  segments back. A window equal to the session's own full range is stored as no
  window.
- **Split** ends the window at the click, renames the session "(1)", and
  inserts a "(2)" session over the rest of the window with copies of the video
  clips. Segments that start at or after the click move to the "(2)" session.

The transaction rejects the edit when the stored window differs from the
snapshot the command started from, so concurrent edits of one session do not
need a lock. After the commit the coordinator publishes the changed sessions,
requests their recompute with `RecomputeReason.SourceWindowChanged` before any
other await (an open editor prompts for a recompute of a stale session unless
one is already running), publishes the changed clips and segments, reports a `SessionEditProgress` with
the created id, and waits for the recomputes. The editor routes that progress to
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
`AppPreferences` without another read. Each head chooses the default layout and
app-data directory. Paths reach persistence, logging and certificate storage
through the root. `TimeProvider.System` is registered once; preferences use the
injected clock, as do live cadence, pairing/sync, persistence timestamps, video,
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
  `IAppDataRefresher`. It hydrates the processing-option cache, then refreshes
  bikes, setups, sessions, recorded-session sources, paired devices, session
  videos, and session segments off-thread. It prepares every snapshot before
  publishing one UI-thread batch. Concurrent requests share the active refresh;
  failure or cancellation before publication preserves all existing stores.
- **Stores**: each concrete store registered as a singleton, then
  re-registered behind both its read and writer interfaces via
  factory delegates that resolve the same instance. This includes
  `RecordedSessionSourceStore`, registered behind
  `IRecordedSessionSourceStore` and `IRecordedSessionSourceStoreWriter`.
  Persisted writer interfaces expose semantic commit methods plus
  publish-only changed/removed batches; `LiveDaqStore` remains runtime-only.
- **Coordinators**: every shared entity coordinator plus `SyncCoordinator` and
  `ImportSessionsCoordinator`. None depends on an editor factory or shell.
  `ShellWorkspaceCoordinator` holds `IEditorFactory`; `CreateEditor(key, shell)`
  receives its shell at call time. No `Func` cycle breakers remain.
- **Queries, indexes, and read graphs**: `IBikeDependencyQuery`,
  `ILiveDaqKnownBoardsQuery`, `IRecordedSessionDomainQuery`,
  `IProcessingDependencyHashIndex`, and `IRecordedSessionProjection`.
- **Live DAQ**: `LiveDaqStore` (singleton behind both
  `ILiveDaqStore` and `ILiveDaqStoreWriter`),
  `IDaqBrowseOwner`, `ILiveDaqBoardIdInspector`,
  `ILiveDaqCatalogService`, `ILiveDaqClientFactory`,
  `ILiveDaqSharedStreamRegistry`, `ILiveSessionServiceFactory`,
  `LiveDaqCoordinator`, `LiveDaqListViewModel`. All registered
  unconditionally; `MainPagesViewModel` receives
  `LiveDaqListViewModel` as a required dependency on both shells.
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
`IPairingServerCoordinator` + `IInboundSyncCoordinator` +
`PairingServerViewModel` on server-capable desktop heads; or
`ISynchronizationClientService` + `IPairingClientCoordinator` +
`PairingClientViewModel` and `IHttpApiService` on client-capable mobile heads.
Presentation selection flows through `IAppEnvironment.LayoutProfile`, and UI
actions are gated by capabilities. The native Avalonia lifetime selects
`MainWindow` or `MainView` at the composition edge.

After `BuildServiceProvider()`, the lifetime wiring resolves
`IDialogHost` and configures dialog presentation for the shell:
desktop sets `MainWindow` as both owner and overlay host with
`DialogPresentationMode.Window`; mobile/single-view sets `MainView`
as overlay host with `DialogPresentationMode.Overlay`. View models
never see this contract — they consume `IDialogService` only.

`App` also eagerly resolves
`ISessionTrackReader`, `PairedDeviceCoordinator`, `SyncCoordinator`, plus the
desktop-only `SessionSyncApplier`, `IPairingServerCoordinator` and `IInboundSyncCoordinator` (or the
mobile-only `IPairingClientCoordinator`). This is necessary because
their constructors subscribe to session-store, synchronization-server, pairing,
or service-discovery changes before the first refresh or arrival.

## Navigation

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
store snapshot and returns no editor for missing entities. It creates live
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
  with a persistent rail and tab strip. Reordering previews by fading the
  dragged tab and showing an insertion indicator, then commits through
  `ShellWorkspaceViewModel.MoveTab`.

The first screen is the primary navigation with no detail tab selected. The
workspace profile shows a neutral empty workspace state until a tab opens.
Profile-specific views are resolved by `ViewLocator` from
`IAppEnvironment.LayoutProfile`, not from platform.
