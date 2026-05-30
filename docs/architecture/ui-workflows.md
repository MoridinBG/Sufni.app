# UI Workflows, Composition, and Navigation

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers the presentation workflow layer: coordinators, dependency injection, and shell navigation. Presentation invariants live in [UI Architecture](ui.md), while read-state ownership lives in [UI State, Read Graphs, and Queries](ui-state.md).

## Coordinators

Coordinators own feature workflows. They are the only layer that
writes to stores, the only layer that decides post-save navigation
(e.g. pop the page on mobile), and the only layer that subscribes to
synchronization events. They live in `Sufni.App/Sufni.App/Coordinators/`
and are registered as singletons.

| Coordinator                                                               | Lifetime     | Owns                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| ------------------------------------------------------------------------- | ------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `IShellCoordinator` (`DesktopShellCoordinator`, `MobileShellCoordinator`) | per shell    | `Open` / `OpenOrFocus<T>` / `Close` / `CloseIfOpen<T>` / `GoBack` — the only navigation surface                                                                                                                                                                                                                                                                                                                                                                     |
| `BikeCoordinator`                                                         | shared       | Open create/edit, save with conflict detection, delete (gated by `IBikeDependencyQuery`)                                                                                                                                                                                                                                                                                                                                                                            |
| `SetupCoordinator`                                                        | shared       | Same as above + the `Board` row association (clears the previous board on save / delete) and the "create setup for detected board" flow                                                                                                                                                                                                                                                                                                                             |
| `SessionCoordinator`                                                      | shared       | Save/delete, create-only `SaveLiveCaptureAsync(...)`, `RecomputeAsync(...)`, recorded-source arrival handling, mobile `LoadMobileDetailAsync` which transparently fetches missing processed telemetry from the server before returning; preserves processing fingerprints on metadata-only saves; persists live captures as processed session + raw source + optional generated track; clears the session's stored preferences and recorded source on delete; subscribes to the desktop server's `SynchronizationDataArrived`, `SessionDataArrived`, and `SessionSourceDataArrived` |
| `PairedDeviceCoordinator`                                                 | shared       | Local-only unpair; subscribes to the desktop server's `PairingConfirmed` and `Unpaired`                                                                                                                                                                                                                                                                                                                                                                             |
| `ImportSessionsCoordinator`                                               | shared       | Opens the import view, runs the full per-file import / trash workflow off thread, reads original SST bytes, persists raw source + processed session + optional generated track atomically, reports per-file progress, and upserts new sessions/sources into their stores                                                                                                                                                                                              |
| `SyncCoordinator`                                                         | shared       | `IsRunning` / `IsPaired` / `CanSync` state, drives `SynchronizationClientService.SyncAll()`, refreshes every store on success, including `RecordedSessionSourceStore` after `SessionStore`                                                                                                                                                                                                                                                                          |
| `IPairingClientCoordinator` (`PairingClientCoordinator`)                  | mobile only  | `DeviceId` / `DisplayName` / `ServerUrl` / `IsPaired` source of truth, mDNS browse lifecycle, request/confirm/unpair HTTP plumbing                                                                                                                                                                                                                                                                                                                                  |
| `IPairingServerCoordinator` (`PairingServerCoordinator`)                  | desktop only | Re-exposes `ISynchronizationServerService` pairing events as plain .NET events for `PairingServerViewModel`, plus `StartServerAsync()` passthrough                                                                                                                                                                                                                                                                                                                  |
| `IInboundSyncCoordinator` (`InboundSyncCoordinator`)                      | desktop only | Marker interface; constructor subscribes to `SynchronizationDataArrived` and writes incoming bikes/setups into their stores. Sessions and paired devices have their own coordinators, so each entity family has exactly one inbound writer                                                                                                                                                                                                                          |
| `TrackCoordinator`                                                        | shared       | GPX import and session-track loading/association                                                                                                                                                                                                                                                                                                                                                                                                                    |
| `LiveDaqCoordinator`                                                      | shared       | Owns `LiveDaqStore` writes, browse lease lifecycle (activate/deactivate), discovery-to-known-board reconciliation, and detail tab open/focus routing. When it creates a detail tab, it threads shared `IDaqManagementService` and `IFilesService` instances into `LiveDaqDetailViewModel`. Activates lazily when the Live primary page is selected — no constructor event subscriptions, so no eager resolution needed. See [Live DAQ Streaming](live-streaming.md) |

`InboundSyncCoordinator`, `SessionCoordinator`, `PairedDeviceCoordinator`,
`PairingClientCoordinator` (mobile), `PairingServerCoordinator`
(desktop) and `SyncCoordinator` are eagerly resolved in
`App.axaml.cs` after `BuildServiceProvider()` so their constructor
event subscriptions wire up before any sync, pairing, or telemetry
arrival can happen.

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

Shared registrations in `App.OnFrameworkInitializationCompleted`:

- **Shell**: `IShellCoordinator` chosen by application lifetime —
  `DesktopShellCoordinator` for `IClassicDesktopStyleApplicationLifetime`,
  `MobileShellCoordinator` for `ISingleViewApplicationLifetime`. Both
  receive a factory for a narrow shell-host interface rather than the
  concrete shell view model, so the shell can be resolved lazily and
  tested against substitutes.
- **Services**: `IHttpApiService`, `IBackgroundTaskRunner`,
  `IUiThreadDispatcher`, `IDaqManagementService`, `ITelemetryDataStoreService`,
  `IDatabaseService`, `IFilesService`,
  `IDialogService`, plus `IAppPreferences` and the two facets
  it exposes — `IMapPreferences` and `ISessionPreferences` —
  registered as singletons via factory delegates that resolve the
  same `IAppPreferences` instance. Recorded-session derivation
  services (`IProcessingFingerprintService`,
  `IRecordedSessionReprocessor`) are also singleton services. The
  recorded-source factory is static and stays in `SessionGraph/`
  beside the reprocessor.
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
- **Queries and read graphs**: `IBikeDependencyQuery`,
  `ILiveDaqKnownBoardsQuery`, `IRecordedSessionDomainQuery`, and
  `IRecordedSessionGraph`.
- **Live DAQ**: `LiveDaqStore` (singleton behind both
  `ILiveDaqStore` and `ILiveDaqStoreWriter`),
  `IDaqBrowseOwner`, `ILiveDaqBoardIdInspector`,
  `ILiveDaqCatalogService`, `Func<ILiveDaqClient>`,
  `ILiveDaqSharedStreamRegistry`, `ILiveSessionServiceFactory`,
  `LiveDaqCoordinator`, `LiveDaqListViewModel`. All registered
  unconditionally; `MainPagesViewModel` receives
  `LiveDaqListViewModel` as a required dependency on both shells.
- **View models**: list view models, the import view model and the
  welcome screen as singletons; `MainViewModel` and
  `MainWindowViewModel` as singletons; `MainPagesViewModel` via an
  explicit factory because it takes both store refresh roots and
  platform-optional page view models. Two of its dependencies
  (`PairingClientViewModel`, `PairingServerViewModel`) are optional
  and platform-specific.

Concrete datastore construction, management-protocol ownership,
file-picker lifetime (including loaded `SelectedDeviceConfigFile`
results for device CONFIG replacement), UI-thread dispatching, and background execution stay
behind these service registrations rather than being created ad hoc in
view models.

Platform entry points add (a strict subset depending on the
platform): `ISecureStorage`, `IServiceDiscovery` (registered as
keyed singletons under `"gosst"` and optionally `"sync"`),
`IHapticFeedback`, `IFriendlyNameProvider`,
`ISynchronizationServerService` + `IPairingServerCoordinator` +
`IInboundSyncCoordinator` + `PairingServerViewModel` (desktop only),
or `ISynchronizationClientService` + `IPairingClientCoordinator` +
`PairingClientViewModel` (mobile only). Platform mode is determined
once from the Avalonia application lifetime and stored on `App.IsDesktop`,
which is the single runtime source of truth for desktop versus mobile
branches.

After `BuildServiceProvider()`, `App` eagerly resolves
`SessionCoordinator`, `PairedDeviceCoordinator`,
`SyncCoordinator`, plus the desktop-only
`IPairingServerCoordinator` and `IInboundSyncCoordinator` (or the
mobile-only `IPairingClientCoordinator`). This is necessary because
their constructors subscribe to synchronization-server / pairing /
service-discovery events and nothing else depends on them at
startup.

## Navigation

Navigation is owned exclusively by `IShellCoordinator`. View models
never poke at the shell view model directly — they call
`shell.Open(view)`, `shell.OpenOrFocus<T>(match, factory)`,
`shell.Close(view)`, `shell.CloseIfOpen<T>(match)`, or `shell.GoBack()`.

App-defined keyboard shortcuts are listed in
`KeyboardShortcutRegistry`, grouped by source and shortcut ID in
`GesturesBySource`. Shell and feature views resolve
`KeyBinding.Gesture` values through `ShortcutGestureExtension` instead
of hard-coding gesture strings. Command-style shortcuts use the
platform command modifier (`Meta` / Cmd on macOS and iOS, `Control`
on Windows, Linux, and Android). Native text editing, focus traversal,
and control-internal keys stay local to their controls.

- **Mobile** — `MobileShellCoordinator` wraps `MainViewModel`, which
  maintains a `Stack<ViewModelBase>`. `Open` pushes; `Close` pops if
  the supplied view is current; `GoBack` always pops; `OpenOrFocus`
  always pushes (mobile has no concept of focusing an existing tab).
  The Android back button is wired in `App.OnFrameworkInitializationCompleted`
  to call `MainViewModel.OpenPreviousView()`.
- **Desktop** — `DesktopShellCoordinator` wraps `MainWindowViewModel`,
  which holds an `ObservableCollection<TabPageViewModelBase> Tabs`
  and a `CurrentView`. `OpenOrFocus<T>(match, create)` walks
  `Tabs.OfType<T>().FirstOrDefault(match)` and reuses the existing
  tab if found. If no open tab matches, it removes and reuses the most
  recent matching closed tab from `tabHistory` before creating a new
  one; all older matching closed-tab entries are dropped so one logical
  tab cannot retain multiple restore-history references. `Close`
  removes the tab through `MainWindowViewModel.CloseTabPage`, which
  preserves a `tabHistory` stack so `RestoreCommand` can re-open the
  most recently closed tab. The desktop tab strip previews reordering
  by fading the dragged tab and showing an insertion indicator, then
  commits the drop through `MainWindowViewModel.MoveTab`; this changes
  the shell collection order and keeps the moved tab active.
  `GoBack` is a no-op on desktop.

`DesktopViews/` continues to provide extended layouts (side panels,
richer controls) that the desktop tab renders instead of the mobile
view.
