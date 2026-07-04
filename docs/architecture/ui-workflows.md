# UI Workflows, Composition, and Navigation

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers the presentation workflow layer: coordinators, dependency injection, and shell navigation. Presentation invariants live in [UI Architecture](ui.md), while read-state ownership lives in [UI State, Read Graphs, and Queries](ui-state.md).

## Coordinators

Coordinators own feature workflows. They are the only layer that
writes to stores, the only layer that decides post-save navigation
(e.g. pop the page on mobile), and the only layer that subscribes to
synchronization events. They live in each slice's `Coordinators/` folder
(e.g. `Bikes/Coordinators/`, `SyncAndPairing/Coordinators/`, `Shell/Coordinators/`;
the Sessions router and its use-case classes live in `Sessions/Coordination/`)
and are registered as singletons.

| Coordinator                                                               | Lifetime     | Owns                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| ------------------------------------------------------------------------- | ------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `IShellCoordinator` (`ShellWorkspaceCoordinator`)                         | shared       | `Open` / `OpenOrFocus<T>` / `OpenInBackground<T>` / `Close` / `CloseIfOpen<T>` / `GoBack` — the only navigation surface; delegates to `ShellWorkspaceViewModel` in both layout profiles                                                                                                                                                                                                                                                                              |
| `BikeCoordinator`                                                         | shared       | Open create/edit, save with conflict detection, delete (gated by `IBikeDependencyQuery`)                                                                                                                                                                                                                                                                                                                                                                            |
| `SetupCoordinator`                                                        | shared       | Same as above + the `Board` row association (clears the previous board on save / delete) and the "create setup for detected board" flow                                                                                                                                                                                                                                                                                                                             |
| `SessionCoordinator`                                                      | shared       | Thin router over `SessionLoader` (reads) and `SessionCommandService` (writes): `OpenEditAsync` routes through `IEditorFactory`, loads delegate to `SessionLoader`, and save / delete / recompute requests delegate to `SessionCommandService`. It owns no event subscriptions and is not eagerly resolved                                                                                                                                                                                                                                                |
| `PairedDeviceCoordinator`                                                 | shared       | Local-only unpair; subscribes to the desktop server's `PairingConfirmed` and `Unpaired`                                                                                                                                                                                                                                                                                                                                                                             |
| `ImportSessionsCoordinator`                                               | shared       | Opens the import view, runs the full per-file import / trash workflow off thread, reads original SST bytes, persists raw source + processed session + optional generated track atomically, reports per-file progress, and upserts new sessions/sources into their stores                                                                                                                                                                                              |
| `SyncCoordinator`                                                         | shared       | `IsRunning` / `IsPaired` / `CanSync` state, drives `SynchronizationClientService.SyncAll()`, refreshes every store on success, including `RecordedSessionSourceStore` after `SessionStore`                                                                                                                                                                                                                                                                          |
| `IPairingClientCoordinator` (`PairingClientCoordinator`)                  | mobile only  | `DeviceId` / `DisplayName` / `ServerUrl` / `IsPaired` source of truth, mDNS browse lifecycle, request/confirm/unpair HTTP plumbing                                                                                                                                                                                                                                                                                                                                  |
| `IPairingServerCoordinator` (`PairingServerCoordinator`)                  | desktop only | Re-exposes `ISynchronizationServerService` pairing events as plain .NET events for `PairingServerViewModel`, plus `StartServerAsync()` passthrough                                                                                                                                                                                                                                                                                                                  |
| `IInboundSyncCoordinator` (`InboundSyncCoordinator`)                      | desktop only | Marker interface; constructor subscribes to `SynchronizationDataArrived` and writes incoming bikes/setups into their stores. Sessions and paired devices have their own coordinators, so each entity family has exactly one inbound writer                                                                                                                                                                                                                          |
| `TrackCoordinator`                                                        | shared       | GPX import and **read-only** session-track loading: it resolves the linked full-track points through `IFullTrackPointReader` and the cached session-window polyline through `ISessionTrackReader`, both keyed by the row `updated` values. When the cached session-window track is missing or misaligned it regenerates points in memory for display only — track association and cached-track persistence are owned by the processed-write path. The GPS-offset adjustment is its one remaining write, persisted one-way — it upserts the session store and the editor refreshes through the session watch like a recompute, with no pushed result                                                                                                                            |
| `LiveDaqCoordinator`                                                      | shared       | Owns `LiveDaqStore` writes, browse lease lifecycle (activate/deactivate), discovery-to-known-board reconciliation, and detail tab open/focus routing. When it creates a detail tab, it threads shared `IDaqManagementService` and `IFilesService` instances into `LiveDaqDetailViewModel`. Activates lazily when the Live primary page is selected — no constructor event subscriptions, so no eager resolution needed. See [Live DAQ Streaming](live-streaming.md) |

The session workflow itself is split into four use-case classes beside the
coordinator (all in `Sufni.App/Sufni.App/Sessions/Coordination/`, registered as
singletons):

| Use case            | Owns                                                                                                                                                       |
| ------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `SessionLoader`     | Unified local-only session detail load; missing local processed telemetry or recorded-source payloads return an incomplete-local-data result                  |
| `SessionCommandService` | Every store-writing session command: `SaveAsync` (metadata save with in-memory store-snapshot optimistic-concurrency conflict detection; preserves processing fingerprints on metadata-only saves), `DeleteAsync` (clears the session's stored preferences, deletes through repositories whose soft delete applies extension cascades atomically, removes the recorded source only when the derivation-window provider says no live session still references it, and cleans up orphaned generated tracks only after the active-session `full_track_id` projection shows no other session references that track), create-only `SaveLiveCaptureAsync` (persists live captures as processed session + raw source + optional generated track), extension-host editing helpers (`CreateDerivedSessionAsync`, `UpdateSessionOriginAsync`, `RenameSessionAsync`) for trim/split workflows, and `RequestRecomputeAsync` / `RequestRecomputeAllAsync` (both delegated to `SessionRecomputeEngine`) |
| `SessionRecomputeEngine` | Serialized, per-session **cancel-and-replace** recompute engine and the single owner of recompute liveness. `RequestRecomputeAsync` rebuilds processed telemetry from the raw source against the current setup/bike inputs and the hydrated `IRecordedSessionProcessingOptionCache`; the reprocessor returns a `ProcessedTelemetryPayload` carrying the decoded telemetry, serialized bytes, and fingerprint JSON, and `SessionTelemetryWriter.UpdateProcessedDerivedDataAsync` commits that payload after the in-transaction DB-input fingerprint check. If that derived write replaces the session's previous full track, the engine deletes the previous track only after the active-session `full_track_id` projection shows it is no longer referenced. A newer request for the same session cancels and replaces the in-flight one (which resolves to `Superseded`), and a run whose DB inputs change underneath it re-enqueues itself until it converges. `IsActive` reports an in-flight run. Cancellation bounds correctness (no stale commit), not CPU — an in-flight reprocess is not interrupted, only prevented from committing. `RequestRecomputeAllAsync` hydrates the processing-option cache, enumerates active ids through the session-id projection (`ISessionRepository.GetActiveSessionIdsAsync()`), and fans them through `RequestRecomputeAsync` with a degree of parallelism scaled to `Environment.ProcessorCount`, returning a `SessionRecomputeAllResult` tally and reporting per-session progress through an optional `IProgress<SessionRecomputeAllProgress>` so the caller can drive a determinate progress dialog; sessions that cannot be recomputed are counted and skipped, not surfaced as failures. The staleness prompt (`SessionStalenessReconciler`) offers "Recompute all" alongside "Recompute" — it builds those buttons through the dialog service's generic `ShowChoiceAsync` prompt (the service stays recompute-agnostic; the reconciler owns the choice ids and what they mean) and runs the bulk recompute behind a modal, equally generic `ShowProgressAsync` loading dialog |
| `SessionSyncApplier`| Subscribes to the desktop server's `SynchronizationDataArrived`, `SessionDataArrived`, and `SessionSourceDataArrived`, applying inbound session data to the stores |

`InboundSyncCoordinator`, `SessionSyncApplier`, `PairedDeviceCoordinator`,
`PairingClientCoordinator` on client-capable mobile heads,
`PairingServerCoordinator` on server-capable desktop heads, and
`SyncCoordinator` are eagerly resolved in `App.axaml.cs` after
`BuildServiceProvider()` so their constructor event subscriptions wire up
before any sync, pairing, or telemetry arrival can happen.

## Dependency Injection

The DI container is a `ServiceCollection` exposed as a static field
on `App` (`App.axaml.cs`). Each platform entry point
(`Sufni.App.{Windows,macOS,Linux,Android,iOS}/Program.cs` or
`AppDelegate.cs` / `MainActivity.cs`) adds its platform-specific
registrations to `App.ServiceCollection` before
`OnFrameworkInitializationCompleted` runs the shared registrations
and calls `BuildServiceProvider()`. There is no separate
`RegisteredServices` indirection — the `ServiceCollection` itself is
the composition root.

Extension module startup is part of the same composition root. `App`
computes the native platform mode, calls the build-time partial
`RegisterBuildTimeExtensions(App.Extensions)`, registers the
profile-aware `ExtensionViewRegistry`, then lets modules register
services before core shared services. `AppExtensionServiceRegistrationContext.IsDesktop`
remains platform-based for service registration. After module and core
services are present, `AppExtensionCapabilityRegistry` is registered as a
singleton and modules register profile-based view capabilities. No assembly
scanning occurs; public builds have no partial implementation and therefore no
modules. See [Extension Host](extensions.md#module-startup).

Shared registrations in `App.OnFrameworkInitializationCompleted`:

- **Extension host**: `IExtensionViewRegistry` /
  `ExtensionViewRegistry`, `AppExtensionCapabilityRegistry`, and the
  services/capabilities supplied by `App.Extensions` before the
  service provider is built. Capabilities include eager service
  resolution and app toolbar action contributions.
- **Shell**: `ShellWorkspaceViewModel`, `ShellRootViewModel`, and the single
  `IShellCoordinator` implementation, `ShellWorkspaceCoordinator`, are shared
  registrations. Single-view lifetimes additionally register
  `MobileNavigationShellHost` behind `IMobileNavigationShellHost` and
  `IMobileNavigationPageHost` for native host/back integration; this host
  mirrors the shared workspace instead of owning a separate navigation model.
- **Services**: `IHttpApiService`, `IBackgroundTaskRunner`,
  `IUiThreadDispatcher`, `IDaqManagementService`, `ITelemetryDataStoreService`,
  SQLite repository interfaces, `ISyncDataStore`, `IExtensionDatabaseConnection`,
  `IFilesService`, `IFilePickerService`, `ITileLayerService`,
  `IMapViewModelFactory`, `ISessionTrackReader`, `IFullTrackPointReader`,
  plus `IAppPreferences` and the two facets
  it exposes — `IMapPreferences` and `ISessionPreferences` —
  registered as singletons via factory delegates that resolve the
  same `IAppPreferences` instance. The concrete `DialogService`
  singleton is re-registered behind `IDialogService` (the `Show*`
  prompt contract for view models and coordinators), `IDialogHost`
  (the owner-window/overlay-host/presentation-mode wiring contract
  used only by `App`), and `IExtensionDialogService` via factory
  delegates that resolve the same instance. Recorded-session derivation
  services (`IProcessingFingerprintService`,
  `IRecordedSessionReprocessor`, `IRecordedSessionAnalysisComputer`) are
  also singleton services. `IRecordedSessionAnalysisResultStateFactory` is
  transient because each open recorded-session editor owns its own result
  cache and cancellation scope. The recorded-source factory is static and
  stays in `RecordedSessionProjection/` beside the reprocessor.
  `IRecordedSessionReprocessor`, `IRecordedSessionAnalysisComputer`,
  `IRecordedSessionDerivationWindowCache`, and the public-build no-op
  `IRecordedSessionDerivationWindowProvider`) are also singleton services.
  `IRecordedSessionAnalysisResultStateFactory` is transient because each open
  recorded-session editor owns its own result cache and cancellation scope.
  `IRecordedSessionSourceSyncQuery` is the source-sync read helper that
  combines repository missing-source state with derivation-window references.
  The recorded-source factory is static and stays in
  `RecordedSessionProjection/` beside the reprocessor.
- **Stores**: each concrete store registered as a singleton, then
  re-registered behind both its read and writer interfaces via
  factory delegates that resolve the same instance. This includes
  `RecordedSessionSourceStore`, registered behind
  `IRecordedSessionSourceStore` and
  `IRecordedSessionSourceStoreWriter`.
- **Coordinators**: every shared entity coordinator plus `SyncCoordinator`,
  `ImportSessionsCoordinator` (the latter takes both an
  `IBackgroundTaskRunner` and a `Func<ImportSessionsViewModel>` so it
  can open / focus the singleton import page while keeping the import
  workflow itself view-model-free).
- **Queries, indexes, and read graphs**: `IBikeDependencyQuery`,
  `ILiveDaqKnownBoardsQuery`, `IRecordedSessionDomainQuery`,
  `IProcessingDependencyHashIndex`, and `IRecordedSessionProjection`.
- **Live DAQ**: `LiveDaqStore` (singleton behind both
  `ILiveDaqStore` and `ILiveDaqStoreWriter`),
  `IDaqBrowseOwner`, `ILiveDaqBoardIdInspector`,
  `ILiveDaqCatalogService`, `Func<ILiveDaqClient>`,
  `ILiveDaqSharedStreamRegistry`, `ILiveSessionServiceFactory`,
  `LiveDaqCoordinator`, `LiveDaqListViewModel`. All registered
  unconditionally; `MainPagesViewModel` receives
  `LiveDaqListViewModel` as a required dependency on both shells.
- **View models**: list view models, the import view model,
  `MainViewModel`, `ShellWorkspaceViewModel`, `ShellRootViewModel`, and
  `MainPagesViewModel` as singletons. `MainPagesViewModel` receives
  platform-optional page view models such as pairing client/server surfaces
  based on the registered capabilities and services.

Concrete datastore construction, management-protocol ownership,
file-picker lifetime (including the generic `IFilePickerService` seam and
loaded `SelectedDeviceConfigFile` results for device CONFIG replacement),
UI-thread dispatching, and background execution stay
behind these service registrations rather than being created ad hoc in
view models.

Platform entry points add (a strict subset depending on the
platform): `IAppEnvironment` with default layout profile, `AppCapabilities`,
and `InputCapabilities`; `ISecureStorage`; `IServiceDiscovery` (registered as
keyed singletons under `"gosst"` and optionally `"sync"`); `IHapticFeedback`;
`IFriendlyNameProvider`; `ISynchronizationServerService` +
`IPairingServerCoordinator` + `IInboundSyncCoordinator` +
`PairingServerViewModel` on server-capable desktop heads; or
`ISynchronizationClientService` + `IPairingClientCoordinator` +
`PairingClientViewModel` on client-capable mobile heads. Platform mode is
determined once from the Avalonia application lifetime and stored on
`App.IsDesktop`, but presentation selection flows through
`IAppEnvironment.LayoutProfile`, and UI actions are gated by capabilities.

After `BuildServiceProvider()`, the lifetime wiring resolves
`IDialogHost` and configures dialog presentation for the shell:
desktop sets `MainWindow` as both owner and overlay host with
`DialogPresentationMode.Window`; mobile/single-view sets `MainView`
as overlay host with `DialogPresentationMode.Overlay`. View models
never see this contract — they consume `IDialogService` only.

`App` also eagerly resolves
`IProcessingDependencyHashIndex`, `ISessionTrackReader`,
`SessionSyncApplier`, `PairedDeviceCoordinator`,
`SyncCoordinator`, plus the desktop-only
`IPairingServerCoordinator` and `IInboundSyncCoordinator` (or the
mobile-only `IPairingClientCoordinator`). This is necessary because
their constructors subscribe to synchronization-server / pairing /
service-discovery events and nothing else depends on them at
startup. The app then resolves any eager service types declared by
extension capabilities, giving extension services the same
constructor-subscription startup point without requiring core
coordinators to know their concrete types.

## Navigation

Navigation is owned exclusively by `IShellCoordinator`. View models
never poke at shell controls directly — they call `shell.Open(view)`,
`shell.OpenOrFocus<T>(match, factory)`, `shell.OpenInBackground<T>(match, factory)`,
`shell.Close(view)`, `shell.CloseIfOpen<T>(match)`, or `shell.GoBack()`.
`GoBack()` returns `true` only when the shared workspace consumed the back
request.

App-defined keyboard shortcuts are listed in
`KeyboardShortcutRegistry`, grouped by source and shortcut ID in
`GesturesBySource`. Shell and feature views resolve
`KeyBinding.Gesture` values through `ShortcutGestureExtension` instead
of hard-coding gesture strings. Command-style shortcuts use the
platform command modifier (`Meta` / Cmd on macOS and iOS, `Control`
on Windows, Linux, and Android). Native text editing, focus traversal,
and control-internal keys stay local to their controls.

`ShellWorkspaceCoordinator` translates every coordinator call into
`ShellWorkspaceViewModel` operations. `OpenOrFocus<T>(match, create)` walks the
open tab collection, reuses a matching tab when found, restores a matching
closed-history tab when available, and otherwise creates a new tab through the
caller-supplied factory. `OpenInBackground<T>(match, create)` uses the same
dedupe/restoration behavior without selecting the tab. `Close` and
`CloseIfOpen<T>` remove tabs through the tab page close path so dirty editors
continue to run their close commands and unsaved-change prompts. `GoBack()`
selects the previously focused tab when possible, otherwise clears the current
detail surface and returns to the primary shell surface.

`ShellRootViewModel` is the data context for both platform lifetimes. The
layout profile controls presentation only:

- **Compact profile** — `CompactShellView` presents the shared workspace as the
  primary page plus the focused detail surface. Single-view mobile lifetimes
  host it through `MainView` and `MobileNavigationShellHost` so safe areas,
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
