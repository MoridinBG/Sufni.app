# UI Workflows, Composition, and Navigation

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers the presentation workflow layer: coordinators, dependency injection, and shell navigation. Presentation invariants live in [UI Architecture](ui.md), while read-state ownership lives in [UI State and Queries](ui-state.md).

## Coordinators

Coordinators own feature workflows. View models call only coordinators for
persisted write workflows. Only coordinators subscribe to synchronization events.
Coordinators persist through repositories and give prepared snapshots to
publish-only store writers. They never mutate store caches directly.

The coordinator role covers every workflow owner, whatever its name. Besides
the `*Coordinator` classes, it covers `SessionRecomputeEngine`,
`SessionSyncApplier`, `SynchronizationApplier`, `AppDataRefresher` and
`RecordedSessionSourceRetentionCleanup` (see
[role holders](ui.md#architectural-invariants)).

Three transaction runners own the multi-row SQLite transactions. They are
persistence services and do not publish:

- `IBikePersistenceTransactionRunner` deletes a bike only when no active setup
  references it. `BikeCoordinator` calls it.
- `ISetupPersistenceTransactionRunner` commits setup save, import and delete
  with the board association. `SetupCoordinator` calls it.
- `ISessionPersistenceTransactionRunner` commits session delete and the trim and
  split edits. `SessionCoordinator` and `SessionEditingCoordinator` call it.

The calling coordinator publishes the affected stores only after the
transaction commits. No separate command-service layer exists.

Coordinators live in each slice's `Coordinators/` folder (e.g.
`Bikes/Coordinators/`, `SyncAndPairing/Coordinators/`, `Shell/Coordinators/`).
The session workflow owners live in `Sessions/Coordination/`.
`RecordedSessionSourceRetentionCleanup` is a startup job in `Sessions/Services/`.
All coordinators are singletons.

| Coordinator | Lifetime | Owns |
| --- | --- | --- |
| `IShellCoordinator` (`ShellWorkspaceCoordinator`) | shared | The only navigation surface: `OpenEditor`, `CloseEditorAsync`, `CompleteEdit`, `SwitchLayoutProfileAsync`, `Open`, `Close`, `GoBack`. It delegates to `ShellWorkspaceViewModel`. |
| `BikeCoordinator` | shared | Bike save, import, cutoff updates and delete. The bike transaction runner rejects deletion of a referenced bike. |
| `SetupCoordinator` | shared | Setup save, import, delete and the board association. |
| `SessionCoordinator` | shared | Session metadata save, processing options, recorded preferences, GPS alignment, deletion, and the recompute requests from presentation. |
| `ISessionEditingCoordinator` (`SessionEditingCoordinator`) | shared | Trim and split of recorded sessions. See [Trim and split](#trim-and-split). |
| `ISegmentCoordinator` (`SegmentCoordinator`) | shared | Saved segments, segment matching, comparison and analysis comparison, and Strava. See [segments.md](segments.md#store-and-coordinator). |
| `PairedDeviceCoordinator` | shared | Local unpair and publication of the removal. |
| `ImportSessionsCoordinator` | shared | Source observations, listing, folder registration and the single active import. |
| `SyncCoordinator` | shared | Replayed `SyncState`, confirm-and-sync, client phases, cursor writes and source publication. |
| `IPairingClientCoordinator` (`PairingClientCoordinator`) | mobile only | Device identity, `PairingClientState`, mDNS browse, peer resolution and pairing commands. |
| `IInboundSyncCoordinator` (`InboundSyncCoordinator`) | desktop only | Server-side pairing, row push/pull, and BLOB and source uploads. |
| `IMapLayerCoordinator` (`MapLayerCoordinator`) | shared | Custom map-layer additions and the device's map-layer selection. |
| `TrackCoordinator` | shared | GPS metric recomputation, GPX import and read-only session-track loading. |
| `LiveDaqCoordinator` | shared | `LiveDaqStore` writes, the browse lease, live-capture save and DAQ management. See [Live DAQ Streaming](live-streaming.md). |

Details per coordinator:

- **`BikeCoordinator`** uses `IBikeEditorService` for analysis, file and image
  I/O. Image loading returns encoded bytes and a file name. The editor decodes
  the bitmap. Save returns a snapshot without analysis data.
- **`SetupCoordinator`** uses one transaction rule. The rule rejects a save
  whose bike is missing or deleted, which the coordinator returns as
  `Invalid(Bike)`. It rejects a board claim held by another setup. It clears a previous board only while this setup
  owns it. It blocks delete while active sessions reference the setup. New
  drafts keep the suggested board id without a claim. The coordinator publishes
  the returned board snapshots with the setup and bike changes in one batch.
- **`SessionCoordinator`** sends the recompute-all outcome to shell feedback
  through `RecomputeAllFinished`. It has no event subscriptions and needs no
  eager resolution.
- **`ImportSessionsCoordinator`** reference-counts source observations. It
  replays `SessionImportState` across tab lifetimes. Off the UI thread, it reads
  original SST bytes and imports or trashes each file. It persists the raw
  source, processed session and optional generated track atomically, then
  publishes the prepared snapshots.
- **`IPairingClientCoordinator`** exposes `DeviceId`, `DisplayName` and
  `ServerUrl`. It has explicit initialization and resolves the captured peer.
- **`IInboundSyncCoordinator`** serves the sync endpoints. It owns PIN
  challenges and expiry, credential rotation and paired-device publication.
  Push awaits the shared `SynchronizationApplier`, which merges and publishes
  every synced row family before the acknowledgement. Source uploads validate
  and publish committed content; BLOB uploads use the session applier. The
  coordinator owns the declared peer-run activity and its silence deadline.
  It has no server-event subscriptions and needs no direct eager resolution.
- **`IMapLayerCoordinator`** persists off-thread and returns typed outcomes for
  add and selection failure. `TileLayerService` derives its catalog from the
  custom-layer store and its selection from device-local preferences.
- **`TrackCoordinator`** recomputes GPS metrics by key, persists them
  conditionally, and cancels and drains that work. GPX import publishes track
  metadata. Session-track loading selects the greatest overlapping track from
  `TrackStore` and reads its points through `ISessionTrackReader`. GPS
  alignment belongs to `SessionCoordinator.ShiftGpsOffsetAsync`, which uses the
  same selection rule.
- **`LiveDaqCoordinator`** reconciles discovery with known boards. It activates
  when the user selects the Live primary page, so it needs no eager resolution.
  The shell routes diagnostics and live-session editor keys. `EditorFactory`
  creates their shared-stream and capture dependencies only for a new tab.
  `SaveCaptureAsync` prepares and reprocesses the capture, persists it,
  publishes in one batch and resets the capture. The coordinator also owns
  reserved management operations and CONFIG selection. Async disposal cancels
  and drains its retained tasks.

`BikeSaveValidator` checks required geometry, paired-wheel inputs, rear
suspension and solvability. It runs in the background for save and import and
logs a rejection or failure once. Save compares the immutable draft base against
the complete persisted row after validation. Import applies the same validation
and returns a new draft. The coordinator builds rows from `BikeDraft` and
publishes the saved metadata snapshot. Initial image-bearing editor loads and
conflicts return the complete current draft, including image bytes. Save preserves
separately authored cutoffs. Cutoff-only writes prepare a separate row that
keeps the persisted image. `Bike.Chainstay` is computed from the linkage geometry.

`SetupCoordinator.SaveAsync` accepts a `SetupDraft` and its optional persisted
base. It compares authored values, including the board association and typed
sensor inputs. It builds the row and sensor JSON off-thread. `SetupSaveRules`
checks required fields and sensor/bike compatibility for save and import.
Embedded bikes also pass `BikeSaveValidator`. The coordinator rejects malformed
sensor JSON as invalid input. The editor receives the saved snapshot and board
association for reset.

`BikeFileFormat` and `SetupFileFormat` own the export schemas; the persistence
models do not. Bike parsing normalizes shock stroke from the rear-suspension
payload for standalone and embedded imports. `BikeEditorService` owns bike file
I/O. `ISetupFileService` owns setup pickers, background read/parse/write and
typed file outcomes. `SetupCoordinator` consumes that result and owns
persistence and publication. The view token covers reading only. After a setup
is read, the import completes independently of that token.

`SessionCoordinator.SaveAsync(draft, draftBase)` compares the draft base against
the current store snapshot before it persists and publishes. It compares only the
metadata fields that the draft saves. It never writes the session timestamp or
GPS offset. Processing options and GPS alignment are separate commands and never
conflict with the draft. A saved result carries the persisted snapshot for the
next draft base.

`ShiftGpsOffsetAsync` derives a track window from persisted inputs, writes the
alignment, then publishes the aligned session. `DeleteAsync` uses the
transaction runner to remove the session, its unshared track and its
unreferenced source. Inside the transaction, the runner selects the current
track and checks other active sessions through the same overlap rule. The
coordinator then clears recorded preferences. It publishes the session, source
and track removals in one dispatcher-owned `StorePublication` batch. Import and
live save use the same batch boundary.

Recorded editors call `ISessionLoader` directly for staged telemetry and track
reads. Presentation reaches `ISessionRecomputeEngine` only through
`SessionCoordinator`:

- The session list's per-row recalculate command calls
  `SessionCoordinator.RecomputeAsync`. It recomputes whether or not the session
  is stale.
- The editor passes domain snapshots and the user's choice to
  `SessionCoordinator.ReconcileStalenessAsync`.

`SessionEditingCoordinator` calls the engine directly after trim and split. The
engine owns cancellation, supersession and commit guards for every caller.

Session readers and other workflow owners live beside the coordinator in
`Sufni.App/Sufni.App/Sessions/Coordination/`. All are singletons.

| Component | Role | Owns |
| --- | --- | --- |
| `ISessionLoader` (`SessionLoader`) | Query | Local-only staged detail reads from read-only `ISessionStore` and `ITrackStore`. |
| `SessionRecomputeEngine` | Coordinator | Serialized, per-session cancel-and-replace recompute. It is the single owner of recompute liveness. |
| `ISessionSyncApplier` (`SessionSyncApplier`) | Coordinator | Incoming processed generations on hub and client. |

`ISessionLoader`:

- `LoadDetailAsync` decodes and validates telemetry for first-content
  publication. It selects the expected track from session timing.
- `LoadTrackAsync` resolves the optional map and track payload later.
- Missing processed telemetry returns an incomplete-local-data result.
- Available telemetry opens even when its source is missing or its fingerprint
  is stale. The domain projection drives the source warning or recompute prompt.
- Track failures settle inside the loaded detail data. Telemetry stays; only the
  map shows as unavailable.

`SessionRecomputeEngine`:

- `RequestRecomputeAsync` rebuilds processed telemetry from the raw source. It
  uses the current setup and bike and the session row's velocity-filter option.
- The reprocessor returns a pure `ProcessedGeneration`. The engine validates its
  fingerprint against a captured database input bundle. It then calls
  `ISessionRepository.UpdateProcessedDerivedDataAsync` with those expected
  values.
- After publication, the engine deletes the previously selected track only when
  no active session still selects it.
- A newer request for the same session cancels the in-flight run, which
  resolves to `Superseded`. A run whose database inputs change re-enqueues
  itself until it converges. `IsActive` reports an in-flight run.
- Cancellation prevents a stale commit. It does not interrupt an in-flight
  reprocess.
- `RequestRecomputeAllAsync` reads active ids through
  `ISessionRepository.GetActiveSessionIdsAsync()` and skips `Current` sessions.
  It runs the rest in parallel, scaled to `Environment.ProcessorCount`. It
  returns a `SessionRecomputeAllResult` tally and reports
  `SessionRecomputeAllProgress`. It counts and skips sessions that disappear or
  cannot be recomputed.
- Completed fingerprints are the restart checkpoint. Bulk runs do not resume
  automatically.
- The editor renders the `ReconcileStalenessAsync` result. It offers
  "Recompute" and "Recompute all" through the generic dialog service. Bulk
  progress uses the modal progress dialog. The completion outcome goes to shell
  feedback, independent of the initiating tab.

`ISessionSyncApplier` validates bytes, normalizes duration, applies guarded
writes and publishes committed sessions. The inbound coordinator awaits upload
application; client transfers call the applier directly. It distinguishes
applied and ignored generations from pending transfers. A terminal ignore clears
only a matching hub request. The applier has no source or server-event
responsibilities.

### Startup and database recovery

`AppStartup` owns the initial database gate, the single-flight store refresh
and server startup. `App` builds one provider and a shell root, then asks
startup to activate the primary pages. Schema work runs off-thread. An
initialization or refresh failure prevents page creation. Tile state derives
from store and preference observations; startup does not initialize it.

`ShellRootViewModel` observes the startup state: `Starting`, `Ready`,
`Failed(reason, details)`, or `RelaunchRequired`. Both layout profiles block
their normal surfaces until `Ready`. The startup surface centers a bounded card
with a status heading, explanation, log location, expandable diagnostics and
grouped recovery actions. It scrolls when the available height is small.

Database failure offers the log location and reset. Reset closes the
connection, archives the remaining database files and creates the replacement
through normal migrations. Reset then requires relaunch. It never rebuilds
stores, coordinators or pages in-process. Reset failure stays blocked and
permits retry; successful reset exposes only Quit. Startup logs a database
initialization failure once.

After readiness, `AppStartup` runs and retains the registered `IStartupJob`
work: the recorded-source retention cleanup. Its stop operation cancels and
awaits that work. It also waits for structural migrations or a reset to settle.

Recompute requests and bulk recompute accept cancellation. The engine retains
all outstanding tasks, including displaced runs. `StopAsync` waits for work that
cannot be interrupted before the provider is disposed. Shutdown also cancels and
drains the track coordinator's input evaluation and GPS metric tasks. Canceled
processing cannot install a stale result. Bulk cancellation stops scheduling
further sessions; fingerprints remain the checkpoint.

### Shutdown and mobile background

`AppShutdown` closes background tabs first and the focused tab last, through
each tab's `TryPrepareCloseAsync`. Dirty editors and unsaved captures keep their
save/discard/cancel policy. Cancel stops the shutdown before work is canceled.
After the tabs close, the shell:

1. Cancels and awaits startup jobs, import, sync, recompute and track work.
2. Finalizes pending deletes.
3. Stops the sync server.
4. Disposes the application provider.
5. Flushes and closes logging.

Concurrent quit requests join the same shutdown. The normal shell surface is
disabled during the request.

`App` intercepts both desktop `ShutdownRequested` and main-window `Closing`.
The window stays present while close prompts run. Native shutdown continues only
after the shell drain completes. Android pause and iOS background finalize
pending deletes and flush logs. They do not close tabs or stop other work. The
iOS head requests a background execution window for that drain.

### Processing option changes

`SessionCoordinator.SetProcessingOptionAsync` persists the session's filter
option through `ISessionRepository`. It publishes the snapshot and starts
recompute before queued projection work can prompt. On failure, it restores the
previous option only when no newer option replaced it. Supersession leaves the
newer request in charge. `ProcessingPreferenceWorkflow` forwards the slider
commit and projects the typed result. The option lives on the session row, not
in device-local preferences. Live-save metadata and split children carry the
session option.

### Pending deletes

`PendingDeletes` is the shell's application-lifetime owner of five-second undo
windows for bikes, setups, sessions and paired devices. Lists schedule typed
keys and project the owner's pending entries; undo removes the intent. Expiry,
dismissal and explicit finalization invoke the owning feature coordinator off
thread. The shell closes an editor only after successful deletion. A failed or
blocked delete removes the pending intent, reveals the row, and reports its
outcome to the owning list, also while that list is detached.

The owner retains commits in flight, so finalization waits for them without
issuing the same delete twice. Pending intents are in-memory state and end with
the process. Desktop shutdown and mobile background finalize them explicitly.
`PendingDeleteEntryViewModel` forwards undo/dismiss intent and has no timer or
commit callback. `ItemListViewModelBase` owns only the undo-bar projection and
list feedback. Hidden rows derive from the shell owner's current keys.

### Trim and split

`ISessionEditingCoordinator` (`SessionEditingCoordinator`, singleton) runs the
recorded-session window edits from the signal-row context menu: "Trim from
beginning", "Trim until end", "Split here", and "Undo trim".
`SessionWindowEditPolicy` owns the pure sample-count gate and the window/origin
arithmetic. The editor combines that gate with its screen state. Inside each
`SessionPersistenceTransactionRunner` transaction, the runner checks the
expected window, asks the policy for immutable patches and applies them.
Source-owned segments and clips stay unchanged:

- **Trim until end** ends the window at the click.
- **Trim from beginning** moves the window start and the session origin to the
  click. The timestamp takes the whole seconds; timeline reads derive the
  fractional origin from the source window. Authored GPS alignment is unchanged.
- **Undo trim** restores the previous window and origin. A window equal to the
  session's full range is stored as no window.
- **Split** ends the window at the click, renames the session "(1)", and
  inserts a "(2)" session over the rest of the window. Both windows derive their
  segments and clips from the shared source.

The transaction rejects the edit when the stored window differs from the
snapshot the command started from. Concurrent edits of one session therefore
need no lock. After the commit, the coordinator:

1. Publishes removed and changed sessions in one UI-thread batch.
2. Requests their recompute before any other await, so an open editor does not
   prompt for a recompute that is already running.
3. Reports a `SessionEditProgress` with the created id.
4. Waits for the recomputes.

An applied edit retains a typed recompute failure when rebuilding cannot
finish. The editor presents the reason and separate diagnostics; the committed
edit is not a failure. The editor routes the progress to
`shell.OpenEditor(new EditorKey.Session(id), background: true)` before the
coordinator completes. Until its recompute completes, a session shows as stale
and then reloads. Edits do not use the operation overlay. The editor allows one
edit at a time. It enables a command only when every remaining part keeps at
least five samples and a trim removes at least one.

## Dependency Injection

Each platform head constructs a `PlatformRoot` containing capabilities, input
capabilities, `LaunchPreferences`, `IAppPaths`, and its platform-service
registration delegate. It passes that root to `App` through the Avalonia
application factory. `AppComposition.AddSharedServices` installs the platform
and shared registrations into a new collection. `App` builds the provider with
`ValidateOnBuild = true`. There is no static service collection.

`AppPreferences.ReadLaunchValues` reads the preferences document once. The launch
snapshot supplies the first-frame theme and layout profile, and seeds runtime
`AppPreferences` without another read. It also captures legacy custom layers for
the SQLite migration. Startup rewrites the normalized device-local document
before it activates pages. Each head chooses the default layout and app-data
directory. Paths reach persistence, logging and certificate storage through the
root. `TimeProvider.System` is registered once for live cadence, pairing/sync,
persistence timestamps, video, Strava and notification/undo expiry.
`IUiTimerScheduler` supplies UI cadence. View models receive it through
constructors. Main and dialog roots provide the inherited `UiTimers.Scheduler`
property for XAML-created controls.

Shared registrations in `AppComposition`:

- **Shell**: `ShellWorkspaceViewModel`, `ShellRootViewModel`, and the single
  `IShellCoordinator` implementation, `ShellWorkspaceCoordinator`. Single-view
  lifetimes host the shell in `MainView` and wire native back requests to the
  shared shell root.
- **Services**: `IBackgroundTaskRunner`, `IUiThreadDispatcher`,
  `IUiTimerScheduler`, `IDaqDriveProbe`, `IDaqManagementService`,
  `ITelemetryDataStoreService`, SQLite repository interfaces, `ISyncDataStore`,
  `IFilesService`, `ITileLayerService`, `IMapViewModelFactory`,
  `ISessionTrackReader`, `IFullTrackPointReader`, plus `IAppPreferences` and its
  `ISessionPreferences` and `IUiPreferences` facets. Factory delegates resolve
  the facets to the same `IAppPreferences` singleton. The concrete
  `DialogService` singleton is registered behind `IDialogService` and
  `IDialogHost`. `IDialogService` is the `Show*` prompt contract for view models
  and coordinators. `IDialogHost` is the owner-window, overlay-host and
  presentation-mode contract that only `App` uses. The recorded-session
  derivation services (`IProcessingFingerprintService`,
  `IRecordedSessionReprocessor`, `IRecordedSessionAnalysisComputer`) are
  singletons. `IRecordedSessionAnalysisResultStateFactory` is a singleton
  factory. Each result state it creates belongs to one open editor, with its own
  cache and cancellation scope. `IRecordedSessionSourceSyncQuery` combines the
  repository's missing recording sources with the sources that derived sessions
  reference. The static recorded-source factory lives in
  `RecordedSessionProjection/` beside the reprocessor.
- **Data refresh**: `AppDataRefresher` behind `IAppDataRefresher`. It refreshes
  bikes, setups, sessions, recorded-session sources, paired devices, session
  videos, session segments, tracks, boards and custom tile layers off-thread.
  It prepares every snapshot before it publishes one UI-thread batch.
  Concurrent requests share the active refresh. Failure or cancellation before
  publication preserves all existing stores.
- **Stores**: each concrete store is a singleton, registered again behind its
  read and writer interfaces through factory delegates. This includes
  `RecordedSessionSourceStore` behind `IRecordedSessionSourceStore` and
  `IRecordedSessionSourceStoreWriter`. Persisted writer interfaces expose
  replacement and publication only. `LiveDaqStore` is runtime-only.
- **Coordinators**: every shared entity coordinator plus `SyncCoordinator` and
  `ImportSessionsCoordinator`. Client pulls and hub pushes share
  `ISynchronizationApplier`, which merges rows and publishes affected IDs
  together. No coordinator depends on an editor factory or the shell.
  `ShellWorkspaceCoordinator` holds `IEditorFactory`, and
  `CreateEditor(key, shell)` receives the shell at call time.
- **Queries**: `IBikeDependencyQuery`,
  `ISetupBoardQuery`, `ILiveDaqKnownBoardsQuery`, `IRecordedSessionDomainQuery`,
  `IProcessingDependencyHashIndex`, and `IRecordedSessionProjection`.
- **Live DAQ**: `LiveDaqStore` (singleton behind both `ILiveDaqStore` and
  `ILiveDaqStoreWriter`), `IDaqBrowseOwner`, `ILiveDaqBoardIdInspector`,
  `ILiveDaqCatalogService`, `ILiveDaqClientFactory`,
  `ILiveDaqSharedStreamRegistry`, `ILiveSessionServiceFactory`,
  `LiveDaqCoordinator`, `LiveDaqListViewModel`. All are registered
  unconditionally. `MainPagesViewModel` requires `LiveDaqListViewModel` on both
  shells.
- **Global feedback/actions**: `ShellFeedbackViewModel` is a singleton that the
  root and the primary-page composition share. `IShellActions` exposes its sync
  command and import availability to lists without exposing another feature's
  view model. `App` resolves `ShellDispatcherExceptionHandler` once. The handler
  releases its dispatcher subscription when the provider is disposed.
- **View models**: list view models, `ShellWorkspaceViewModel`,
  `ShellRootViewModel`, and `MainPagesViewModel` as singletons.
  `MainPagesViewModel` receives platform-optional page view models, such as the
  pairing client and server surfaces, based on the registered capabilities and
  services.

Concrete datastore construction, management-protocol ownership, file-picker
lifetime (including loaded `SelectedDeviceConfigFile` results for device CONFIG
replacement), UI-thread dispatching, and background execution stay behind these
service registrations. View models do not create them ad hoc.

Platform entry points add a platform-specific subset of:

- `IAppEnvironment` with the default layout profile, `AppCapabilities`, and
  `InputCapabilities`.
- `ISecureStorage`, `IHapticFeedback` and `IFriendlyNameProvider`.
- `IServiceDiscovery`, as keyed singletons under `"daq"` and optionally
  `"sync"`.
- `IVideoPlayerFactory` on heads with `AppCapabilities.CanPlayVideo`.
- On server-capable desktop heads: `ISynchronizationServerService`,
  `ISynchronizationTokenService`, `IInboundSyncCoordinator` and
  `PairingServerViewModel`.
- On client-capable mobile heads: `IPairingClientCoordinator`,
  `PairingClientViewModel` and `IHttpApiService`.

`IAppEnvironment.LayoutProfile` selects the presentation, and capabilities gate
UI actions. The native Avalonia lifetime selects `MainWindow` or `MainView` at
the composition edge.

After `BuildServiceProvider()`, the lifetime wiring resolves `IDialogHost` and
configures dialog presentation for the shell. Desktop sets `MainWindow` as both
owner and overlay host with `DialogPresentationMode.Window`. Mobile and
single-view hosts set `MainView` as overlay host with
`DialogPresentationMode.Overlay`. View models never see this contract; they
consume `IDialogService` only. Close confirmation follows the configured mode:
save/discard/cancel and unsavable-page confirmation render as overlays on
single-view hosts. Every overlay uses the explicitly configured host; there is
no application-lifetime lookup fallback.

`App`'s page-activation callback eagerly resolves `TrackCoordinator` and
`SyncCoordinator` for their store and inbound-activity subscriptions. This
happens before server startup. `AppStartup` awaits the mobile pairing
coordinator's explicit `InitializeAsync` before it activates pages; the
constructor starts no credential or identity I/O. Inbound sync is reached
through its state consumer and the server. Paired-device commands and the
session applier need no eager resolution. `AppStartup` calls
`ISynchronizationServerService.StartAsync` directly.

## Navigation

`IShellCoordinator` exclusively owns navigation. Features use
`OpenEditor(EditorKey, background)` and `CloseEditorAsync(EditorKey)`. The shell
also exposes `Open`, `Close` and `GoBack` for non-editor surfaces. A successful
editor accepts the saved baseline, then passes the key from its saved outcome to
`CompleteEdit`. In compact layout, `CompleteEdit` returns to the previous
surface only while that editor is still current. In workspace layout, it leaves
focus unchanged. Feature coordinators never open or close tabs. `GoBack()`
returns `true` only when the shared workspace consumed the back request.

Coordinators can report a milestone so that the view model navigates.
`SyncCoordinator.ConfirmPairingAndSyncAsync` reports the `Paired` milestone
through its progress callback before it awaits the initial sync.
`PairingClientViewModel` calls `GoBack` at that milestone. Rejected PINs and
failures stay on the pairing surface.

`KeyboardShortcutRegistry` lists the app-defined keyboard shortcuts, grouped by
source and shortcut ID in `GesturesBySource`. `App` creates the registry from
`InputCapabilities` and exposes it as an application resource. Shell and feature
views resolve `KeyBinding.Gesture` values through `ShortcutGestureExtension`
instead of hard-coded gesture strings. Command-style shortcuts use the
head-supplied `InputCapabilities.CommandModifier`: `Meta` (Cmd) on macOS and iOS,
`Control` on Windows, Linux, and Android. Shared code does not inspect the OS.
Native text editing, focus traversal, and control-internal keys stay local to
their controls.

`ShellWorkspaceCoordinator` matches editor keys against open tabs before it
calls `IEditorFactory.CreateEditor(key, shell)`. Reopening a key focuses its
existing tab; a background request preserves the current focus. The factory
reads the current store snapshot (or joined setup/board details). It returns no
editor for missing entities. It creates live stream and capture dependencies
only after deduplication, so a second request does not create an unused capture
service.

The shell replaces a diagnostics tab whose shared stream is closed when the
user reselects it, in either layout. It detaches that tab and creates the
replacement synchronously. It retains the old tab's asynchronous cleanup task.
Shell disposal drains those tasks before dependencies are disposed. A cleanup
failure is logged once and reported through shell feedback. The registry keeps
the old port reservation until socket cleanup completes. Live-capture tabs stay
deduplicated, which preserves their unsaved capture after stream closure.

Persisted bike, setup and session keys have closed-tab restore entries. Restore
constructs a fresh editor from the current snapshot. New drafts, imports and
live tabs have no restore entry. Successful deletion uses `CloseEditorAsync`,
which cleans up the tab and forgets its history, with no extra back step. Normal
close runs `TryPrepareCloseAsync` before it removes the tab. Bike and setup file
imports are list actions. Bike import passes a validated draft to a fresh
`NewBike` tab. Setup import returns the saved setup ID and opens it from the
published store. Existing editors keep their unsaved changes.

The factory creates an import tab per open lifetime. On final close, the tab
disposes its run observation and datastore collection handler. The coordinator
retains the run and progress across those tab lifetimes. `GoBack` selects the
previously focused tab or returns to the primary surface.

`SwitchLayoutProfileAsync` persists the local preference and applies it on the
UI dispatcher. It does not close background tabs. Recorded-session loads and
subscriptions survive view replacement and end only on tab close. Inactive-tab
prompt deferral applies in both profiles.

`ShellRootViewModel` is the data context for both platform lifetimes. The
layout profile controls presentation only:

- **Compact profile** — `CompactShellView` presents the shared workspace as the
  primary page plus the focused detail surface. Single-view mobile lifetimes
  host it through `MainView`, which wires safe areas, plot overlays, and
  hardware back requests to the native host. Back closes transient surfaces
  first, then delegates to `shell.GoBack()`.
- **Workspace profile** — `WorkspaceShellView` presents the same tab collection
  with a persistent rail and tab strip. Its middle-click close class handler is
  registered once, so recreating the shell cannot multiply close prompts. A
  reorder preview fades the dragged tab and shows an insertion indicator. The
  drop commits through `ShellWorkspaceViewModel.MoveTab`.

The first screen is the primary navigation with no detail tab selected. The
workspace profile shows a neutral empty workspace state until a tab opens.
`ViewLocator` resolves profile-specific views from
`IAppEnvironment.LayoutProfile`, not from the platform.

Setup writes and transaction work run through the background runner. After the
commit, the coordinator supplies prepared snapshots to the setup, bike and
board stores. Desktop inbound sync reads and maps bike, setup and board rows
off-thread before it publishes those snapshots in one batch.
