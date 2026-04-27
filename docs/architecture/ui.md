# UI Architecture

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers the presentation layer in depth: invariants, layering, threading, stores, coordinators, queries, view models, dependency injection, navigation, controls, and ScottPlot-based plot rendering.

The presentation code is organized in five layers with a strict
one-way dependency chain:

```
Views → ViewModels → Coordinators / Stores / Queries → Services → Platform
```

CommunityToolkit.Mvvm source generators (`[ObservableProperty]`,
`[RelayCommand]`) drive bindings; views are XAML with compiled
bindings; reactive collections use DynamicData (`SourceCache<T, TKey>`
→ `ReadOnlyObservableCollection<T>`).

## Architectural Invariants

These boundaries are the invariants worth preserving even if type names
or feature wording evolve:

- A view model owns screen state, command flow, and binding-friendly projection.
- A store owns shared read state for an entity family and direct lookups over its own read model.
- A query answers a business question that crosses domains or requires derived reasoning; it does not own the shared collection.
- A coordinator owns workflows with side effects, store writes, navigation decisions, and long-lived event subscriptions.
- A service or factory owns infrastructure-facing work such as datastore construction, file-picker lifetime, platform integration, and explicit background execution.

## Layered Architecture

```mermaid
graph TB
    subgraph Presentation["Presentation"]
        Shell["Shell VMs<br/>MainViewModel / MainWindowViewModel<br/>MainPagesViewModel"]
        Lists["List VMs<br/>BikeListViewModel<br/>SetupListViewModel<br/>SessionListViewModel<br/>PairedDeviceListViewModel<br/>LiveDaqListViewModel"]
        Rows["Row VMs<br/>BikeRowViewModel<br/>SetupRowViewModel<br/>SessionRowViewModel<br/>PairedDeviceRowViewModel<br/>LiveDaqRowViewModel"]
        Editors["Editor VMs<br/>BikeEditorViewModel<br/>SetupEditorViewModel<br/>SessionDetailViewModel<br/>LiveDaqDetailViewModel<br/>LiveSessionDetailViewModel"]
    end

    subgraph Application["Application"]
        Coords["Coordinators<br/>(per entity + shell)"]
        Stores["Stores<br/>(IXxxStore / IXxxStoreWriter)"]
        Queries["Queries<br/>(IBikeDependencyQuery)<br/>(ILiveDaqKnownBoardsQuery)"]
    end

    subgraph Services["Services"]
        DBSvc["IDatabaseService"]
        HttpSvc["IHttpApiService"]
        SyncSvc["ISynchronization*Service"]
        DSSSvc["ITelemetryDataStoreService"]
        BgSvc["IBackgroundTaskRunner"]
    end

    Shell --> Coords
    Lists --> Stores
    Lists --> Coords
    Rows --> Coords
    Editors --> Coords
    Editors --> Stores
    Coords --> Stores
    Coords --> Queries
    Coords --> Services
    Queries --> Services
    Stores --> Services
```

Rules enforced by convention:

- A view model may depend on coordinators, **read-only** stores, queries, services, and other shell composition view models. It may not depend on another feature view model or on a store writer. Any remaining direct feature-VM dependency outside shell composition is technical debt, not a pattern to copy.
- A coordinator may depend on services, **read/write** stores, other coordinators, queries, the shell coordinator, and dialogs. It may not depend on any view model.
- A service or factory may depend on platform or infrastructure APIs and may create concrete datastores, own file-picker lifetime, and own background execution. View models ask services and factories to do this work; they do not `new` concrete infrastructure types.
- A store may depend only on services. A query may depend on services or read-only stores.
- Controls in `Views/Controls/` and `DesktopViews/Controls/` resolve nothing from the DI container — parent views supply everything via bindings or attached behaviours.

## Threading & Lifecycle

Thread ownership is explicit:

- The UI thread is reserved for bound-property updates, `ObservableCollection` mutation, notifications, native picker interaction, and lightweight read-store lookups.
- Filesystem work, network work, datastore enumeration, SST parsing, PSST generation, and similar slow operations must cross an explicit background boundary (`IBackgroundTaskRunner` or a service-owned equivalent) before they run.
- Services may still use UI-thread primitives for cadence or collection ownership (for example `DispatcherTimer`), but only the UI-bound collection mutation belongs back on the UI thread.
- Singleton page view models do not imply always-on work. Browse lifetimes and store subscriptions attach in `Loaded` and tear down in `Unloaded`.
- Prefer generated async-command state such as `Command.IsRunning` as the busy-state source of truth instead of maintaining duplicate booleans.

### Cancellation & Result Coherence

Background workflows whose results can be superseded should take a
`CancellationToken` from their caller and propagate it through the
coordinator/service chain.

- The owner that started the work — typically a page view model — cancels the token in `Unloaded` or before replacing the workflow with a newer request.
- Cancellation is a neutral exit, not a failure outcome. Do not translate `OperationCanceledException` into domain results such as `Failed`, `Unavailable`, or user-facing errors.
- Replaceable read/refresh workflows should be cancellable. Once a workflow has crossed into persistence or other committed side effects, prefer explicit result shapes over best-effort cancellation.

Result application must still enforce coherence after awaited work returns:

- A canceled or superseded workflow must not apply UI state, overwrite newer data, or clear busy indicators that belong to newer work.
- The component that owns the current workflow identity should only clear or dispose its current cancellation state when the completing workflow is still the active one.
- If multiple refresh triggers arrive while only the latest result matters, coalesce them or drop stale completions rather than partially merging old and new state.

## Stores

Stores own shared read state. One per entity family, registered as a
singleton, exposed behind two interfaces: a read-only `IXxxStore`
injected into list/row/editor view models and queries, and a
`IXxxStoreWriter` (which extends the read interface) reserved for
coordinators and the composition root. The implementation lives in
`Sufni.App/Sufni.App/Stores/`.

| Store               | Read interface       | Writer interface           | Snapshot type          | Key      |
| ------------------- | -------------------- | -------------------------- | ---------------------- | -------- |
| `BikeStore`         | `IBikeStore`         | `IBikeStoreWriter`         | `BikeSnapshot`         | `Guid`   |
| `SetupStore`        | `ISetupStore`        | `ISetupStoreWriter`        | `SetupSnapshot`        | `Guid`   |
| `SessionStore`      | `ISessionStore`      | `ISessionStoreWriter`      | `SessionSnapshot`      | `Guid`   |
| `PairedDeviceStore` | `IPairedDeviceStore` | `IPairedDeviceStoreWriter` | `PairedDeviceSnapshot` | `string` |
| `LiveDaqStore`      | `ILiveDaqStore`      | `ILiveDaqStoreWriter`      | `LiveDaqSnapshot`      | `string` |

Each store wraps a `SourceCache<TSnapshot, TKey>` and exposes:

- `Connect()` — DynamicData change stream consumed by list view models.
- `Get(key)` — synchronous lookup that returns the current snapshot or
  `null`.
- `RefreshAsync()` — load (or reload) all rows from the database via
  `IDatabaseService` and replace the cache contents. Called once at
  startup by `MainPagesViewModel.LoadDatabaseContent()` and again after
  every successful `SyncCoordinator.SyncAllAsync()`.
- `Upsert(snapshot)` / `Remove(key)` (writer interface only) — invoked
  by coordinators after a save / delete / sync arrival.

Snapshots are immutable records, not view models. They carry an
`Updated` timestamp that the editors keep as their `BaselineUpdated`
for optimistic conflict detection.

`SessionStore` additionally exposes `Watch(Guid)`, a per-id observable
filtered to `Add`/`Update` change reasons. `SessionDetailViewModel`
subscribes to this in `Loaded` to react to telemetry-arrival and
recalculation events for the session it is editing, then disposes the
subscription in `Unloaded`.

`SetupStore` exposes `FindByBoardId(Guid)` so the import flow can
look up the existing setup for the currently selected DAQ board
without scanning anything from the UI side. This remains a store
responsibility because the answer is a direct lookup over the store's
own read model, not a cross-domain business query.

`LiveDaqStore` is a runtime-only store that does not persist to the
database. It has no `RefreshAsync()`, its snapshots carry no `Updated`
timestamp, and it is populated entirely by `LiveDaqCoordinator` from
discovery and known-board query results. See
[Live DAQ Streaming](live-streaming.md) for the full feature
architecture.

## Coordinators

Coordinators own feature workflows. They are the only layer that
writes to stores, the only layer that decides post-save navigation
(e.g. pop the page on mobile), and the only layer that subscribes to
synchronization events. They live in `Sufni.App/Sufni.App/Coordinators/`
and are registered as singletons.

| Coordinator                                                               | Lifetime     | Owns                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| ------------------------------------------------------------------------- | ------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `IShellCoordinator` (`DesktopShellCoordinator`, `MobileShellCoordinator`) | per shell    | `Open` / `OpenOrFocus<T>` / `Close` / `GoBack` — the only navigation surface                                                                                                                                                                                                                                                                                                                                                                                        |
| `BikeCoordinator`                                                         | shared       | Open create/edit, save with conflict detection, delete (gated by `IBikeDependencyQuery`)                                                                                                                                                                                                                                                                                                                                                                            |
| `SetupCoordinator`                                                        | shared       | Same as above + the `Board` row association (clears the previous board on save / delete) and the "create setup for detected board" flow                                                                                                                                                                                                                                                                                                                             |
| `SessionCoordinator`                                                      | shared       | Save/delete, create-only `SaveLiveCaptureAsync(...)`, plus `EnsureTelemetryDataAvailableAsync` for the mobile telemetry-fetch path; subscribes to the desktop server's `SynchronizationDataArrived` and `SessionDataArrived`                                                                                                                                                                                                                                        |
| `PairedDeviceCoordinator`                                                 | shared       | Local-only unpair; subscribes to the desktop server's `PairingConfirmed` and `Unpaired`                                                                                                                                                                                                                                                                                                                                                                             |
| `ImportSessionsCoordinator`                                               | shared       | Opens the import view, runs the full per-file import / trash workflow off thread, reports per-file progress, and upserts new sessions into `SessionStore`                                                                                                                                                                                                                                                                                                           |
| `SyncCoordinator`                                                         | shared       | `IsRunning` / `IsPaired` / `CanSync` state, drives `SynchronizationClientService.SyncAll()`, refreshes every store on success                                                                                                                                                                                                                                                                                                                                       |
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

## Result Shapes

Operations with multiple semantically distinct outcomes return a sealed
record hierarchy with a private constructor so callers must
pattern-match on known cases instead of relying on bool flags, `null`,
or magic strings. This convention applies to both coordinator and
service contracts when the caller's next step differs by outcome.

`SaveAsync` on the entity coordinators follows this pattern with
`Saved(NewBaselineUpdated)`, `Conflict(CurrentSnapshot)`, or
`Failed(ErrorMessage)`. Editors pattern-match on the result and, on
conflict, prompt the user via `IDialogService` before reloading the
snapshot. The coordinator detects conflicts by comparing the baseline
`Updated` value the editor opened on against the store's current
snapshot — so a sync arrival or another tab's save during an edit
cannot silently overwrite the user's changes. The live session path is
deliberately separate: `SaveLiveCaptureAsync(...)` is create-only and
returns `Saved(SessionId, Updated)` or `Failed(ErrorMessage)` because
there is no optimistic-concurrency baseline for an in-memory live
capture.

The same convention is used for infrastructure-facing service outcomes
such as `StorageProviderRegistrationResult` (`Added` / `AlreadyOpen`)
and for small sealed event hierarchies such as `SessionImportEvent`
(`Imported` / `Failed`) when a long-running workflow streams progress
back to the UI.

## Queries

Queries answer business questions across entity families without
going through view models. They are stateless singletons in
`Sufni.App/Sufni.App/Queries/`.

`IBikeDependencyQuery.IsBikeInUseAsync(Guid)` (backed by
`BikeDependencyQuery` over `IDatabaseService`) reports whether any
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

## View Models

```mermaid
classDiagram
    class ViewModelBase {
        +ErrorMessages
        +Notifications
        -notificationsTimer: 3s auto-hide
    }

    class TabPageViewModelBase {
        #shell: IShellCoordinator
        #dialogService: IDialogService
        +IsDirty, Name, Timestamp
        +SaveCommand / ResetCommand / ExportCommand / CloseCommand
        #SaveImplementation() / ResetImplementation() / ExportImplementation()
    }

    class ItemListViewModelBase {
        +SearchText, DateFilter, MenuItems
        #AddImplementation()
    }

    class EditorVM {
        +Id, BaselineUpdated, IsInDatabase
      inherits TabPageViewModelBase command surface
    }

    class ListVM {
        +Items: ReadOnlyObservableCollection~RowVM~
        store-backed projection
    }

    class RowVM {
        +Id, Name, Timestamp
      inherits ListItemRowViewModelBase
        +Update(snapshot)
    }

    ViewModelBase <|-- TabPageViewModelBase
    ViewModelBase <|-- ItemListViewModelBase
    TabPageViewModelBase <|-- EditorVM
    ItemListViewModelBase <|-- ListVM
```

There are five kinds of view model in the presentation layer:

- **Shell view models** — `MainViewModel` (mobile), `MainWindowViewModel`
  (desktop), and `MainPagesViewModel` compose the page view models for
  binding and forward shell-level concerns. `MainPagesViewModel` is
  the only place that holds references to multiple page view models
  at once; this is the explicit "view composition" carve-out from the
  no-VM-on-VM rule. It keeps observable mirrors of `SyncCoordinator`'s
  `IsRunning` / `IsPaired` and forwards `SyncCompleted` / `SyncFailed`
  notifications to the active page, but it owns no workflows of its
  own. The triggering of the initial store refresh
  (`LoadDatabaseContent`) also lives here so the database load happens
  exactly once after the shell is constructed.

- **Feature page view models** — non-entity top-level screens such as `ImportSessionsViewModel`, `WelcomeScreenViewModel`, and the pairing pages. They own only screen-scoped state, bind directly to controls, attach subscriptions and browse lifetime in `Loaded` / `Unloaded`, and delegate workflows to coordinators and services. `ImportSessionsViewModel` is the canonical example: it keeps datastore / file selection, notifications, and errors; resolves `SelectedSetup` from `ISetupStore.FindByBoardId`; asks `ITelemetryDataStoreService` to browse, load files, and register storage-provider folders; and delegates the actual import lifecycle to `ImportSessionsCoordinator`. For long-running screen actions they prefer the generated async-command `IsRunning` state over duplicate busy flags.

- **List view models** (`ViewModels/ItemLists/`) — `BikeListViewModel`,
  `SetupListViewModel`, `SessionListViewModel`,
  `PairedDeviceListViewModel`, `LiveDaqListViewModel`. Each takes a
  read-only store plus the matching coordinator, and projects the
  store's `Connect()` change
  stream through DynamicData into a typed `ReadOnlyObservableCollection`
  of row view models:

  ```
  store.Connect()
      .Filter(filterSubject)              // search text + date range
      .TransformWithInlineUpdate(
          snapshot => new XxxRowViewModel(snapshot, coordinator),
          (row, snapshot) => row.Update(snapshot))
      .Bind(out items)                    // .SortAndBind for sessions
      .Subscribe();
  ```

  The list owns its own `Items` collection (it `new`-shadows the
  empty default on `ItemListViewModelBase`) and pushes a fresh
  predicate to a `BehaviorSubject` whenever filter state changes.
  The base class only contributes the cross-cutting search /
  date-filter / menu-item state and the `AddCommand` plumbing —
  individual lists override `AddImplementation()` to delegate to
  their coordinator.

- **Row view models** (`ViewModels/Rows/`) — `BikeRowViewModel`,
  `SetupRowViewModel`, `SessionRowViewModel`,
  `PairedDeviceRowViewModel`, `LiveDaqRowViewModel`. Cheap,
  non-editable wrappers around a single snapshot. They expose a
  `Update(snapshot)` method that DynamicData calls when the underlying
  snapshot changes, plus an `IRelayCommand`-based open/delete surface
  inherited from `ListItemRowViewModelBase` (a single shared `x:DataType` for the
  `DeletableListItemButton` / `SwipeToDeleteButton` /
  `PairedDeviceListItemButton` controls). Open and delete commands
  route through the entity coordinator. `LiveDaqRowViewModel` is an
  exception: it does not derive from `ListItemRowViewModelBase` because live DAQ
  rows are not deletable and use a custom row control with
  online/offline presentation.

- **Editor view models** (`ViewModels/Editors/`) — `BikeEditorViewModel`,
  `SetupEditorViewModel`, `SessionDetailViewModel`,
  `LiveDaqDetailViewModel`, `LiveSessionDetailViewModel`. Constructed by the entity coordinator from
  a snapshot, never by another view model and never stored in a list.
  Persisted-entity editors keep the snapshot's `Updated` value as
  `BaselineUpdated` for optimistic conflict detection at save time,
  derive editable state from the snapshot in `ResetImplementation`,
  and call back into the coordinator's `SaveAsync` / `DeleteAsync`. On
  `SaveResult.Conflict` they prompt the user via
  `IDialogService.ShowConfirmationAsync` and rebuild from the
  conflict's current snapshot. Persisted-entity editors share the
  `TabPageViewModelBase` command surface, which is the single
  `x:DataType` used by the shared `CommonButtonLine` editor button
  strip. `LiveDaqDetailViewModel` and
  `LiveSessionDetailViewModel` are the two live-only exceptions: the
  diagnostics tab is a transport/configuration surface over the shared
  stream, while the live session tab is a create-only capture editor
  backed by `ILiveSessionService`. `LiveDaqDetailViewModel` projects a
  throttled diagnostics snapshot from `LiveDaqSessionState` and also
  owns the disconnected-only Set Time / Replace Config / Upload CONFIG
  command flow, reusing `ViewModelBase.Notifications` and
  `ErrorMessages` while keeping management busy state separate from the
  live connect/disconnect workflow. The live session editor projects
  graph/media/statistics state from the live session service and
  persists through `SessionCoordinator.SaveLiveCaptureAsync(...)`.

`TabPageViewModelBase` (`ViewModels/TabPageViewModelBase.cs`) is the
shared base for everything that opens as a top-level tab or stacked
view (editors, the import view, the welcome screen). It takes
`IShellCoordinator` and `IDialogService` via its constructor and
provides the shared `IsDirty` machinery, the
`SaveCommand`/`ResetCommand`/`ExportCommand`/`CloseCommand`
implementation, and the `OpenPreviousPageCommand` that delegates to
`shell.GoBack()`. The `CloseCommand` flow uses
`IDialogService.ShowCloseConfirmationAsync` to prompt for save / discard
/ cancel before letting the shell close the tab.

`ViewModelBase` (`ViewModels/ViewModelBase.cs`) extends
`ObservableObject` and contributes the notification / error-message
collections plus the 3-second auto-hide timer that pauses on pointer
hover. It no longer carries any navigation surface — that moved to
`IShellCoordinator`.

## Testing Boundaries

The architecture is intended to be tested in layers:

- View model tests assert screen-scoped behavior such as property changes, stale-result guards, `Loaded` / `Unloaded` lifecycle, generated command `IsRunning`, and progress-driven notification updates under a test `SynchronizationContext`.
- View model tests assert screen-scoped behavior such as property changes, stale-result guards, `Loaded` / `Unloaded` lifecycle, generated command or local busy-state transitions, and progress-driven notification updates under a test `SynchronizationContext`.
- Coordinator tests assert workflow semantics such as persistence, store writes, branching, result shapes, per-file progress emission, and background-runner usage.
- Service tests cover infrastructure ownership when the behavior is non-trivial, for example datastore registration, duplicate detection, or one-shot board detection.

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
  `IDaqManagementService`, `ITelemetryDataStoreService`,
  `IDatabaseService`, `IFilesService`,
  `IDialogService`.
- **Stores**: each concrete store registered as a singleton, then
  re-registered behind both its read and writer interfaces via
  factory delegates that resolve the same instance.
- **Coordinators**: every shared entity coordinator plus `SyncCoordinator`,
  `ImportSessionsCoordinator` (the latter takes both an
  `IBackgroundTaskRunner` and a `Func<ImportSessionsViewModel>` so it
  can open / focus the singleton import page while keeping the import
  workflow itself view-model-free).
- **Queries**: `IBikeDependencyQuery`, `ILiveDaqKnownBoardsQuery`.
- **Live DAQ**: `LiveDaqStore` (singleton behind both
  `ILiveDaqStore` and `ILiveDaqStoreWriter`),
  `ILiveDaqBrowseOwner`, `ILiveDaqBoardIdInspector`,
  `ILiveDaqCatalogService`, `Func<ILiveDaqClient>`,
  `ILiveDaqSharedStreamRegistry`, `ILiveSessionServiceFactory`,
  `LiveDaqCoordinator`, `LiveDaqListViewModel`. All registered
  unconditionally; `MainPagesViewModel` receives
  `LiveDaqListViewModel` conditionally (null on mobile).
- **View models**: list view models, the import view model and the
  welcome screen as singletons; `MainViewModel` and
  `MainWindowViewModel` as singletons; `MainPagesViewModel` via an
  explicit factory because two of its dependencies
  (`PairingClientViewModel`, `PairingServerViewModel`) are optional
  and platform-specific.

Concrete datastore construction, management-protocol ownership,
file-picker lifetime (including loaded `SelectedDeviceConfigFile`
results for device CONFIG replacement), and background execution stay
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
  tab if found, otherwise instantiates and adds a new one. `Close`
  removes the tab through `MainWindowViewModel.CloseTabPage`, which
  preserves a `tabHistory` stack so `RestoreCommand` can re-open the
  most recently closed tab. `GoBack` is a no-op on desktop.

`DesktopViews/` continues to provide extended layouts (side panels,
richer controls) that the desktop tab renders instead of the mobile
view.

## Controls Library

`Sufni.App/Sufni.App/Views/Controls/` contains reusable UI components: `SearchBar`, `SearchBarWithDateFilter`, `EditableTitle`, `SwipeToDeleteButton`, `PullableMenuScrollViewer`, `PinInput`, `SidePanel`, `NotificationsBar`, `ErrorMessagesBar`, dialog windows (`OkCancelDialogWindow`, `YesNoCancelDialogWindow`), and `CommonButtonLine`. `CommonButtonLine` binds against `TabPageViewModelBase`, while the desktop-specific row controls (`DesktopViews/Controls/DeletableListItemButton`, `PairedDeviceListItemButton`) bind against `ListItemRowViewModelBase` so the shared button surface stays consistent across entity families. `LiveDaqListItemButton` is a separate desktop control that binds against `LiveDaqRowViewModel` directly because live DAQ rows are not deletable and need online/offline presentation.

## Data Visualization

### Plot Hierarchy

`SufniPlot` (`Sufni.App/Sufni.App/Plots/SufniPlot.cs`) is the base class providing dark theme styling (background `#15191C`, data area `#20262B`, grid `#505558`, labels `#D0D0D0`). It also patches a ScottPlot horizontal line rendering issue.

`TelemetryPlot` (`Sufni.App/Sufni.App/Plots/TelemetryPlot.cs`) extends `SufniPlot` for time-series data with front (blue `#3288bd`) and rear (teal `#66c2a5`) color conventions. It defines `LockedVerticalSoftLockedHorizontalRule` — an axis rule that locks the Y range but allows X panning/zooming within the session duration.

| Plot                             | File                                    | Description                                                                         |
| -------------------------------- | --------------------------------------- | ----------------------------------------------------------------------------------- |
| **TravelPlot**                   | `Plots/TravelPlot.cs`                   | Suspension travel over time (mm). Airtime spans shown as semi-transparent red       |
| **VelocityPlot**                 | `Plots/VelocityPlot.cs`                 | Suspension velocity over time (mm/s). Positive = compression, negative = rebound    |
| **TravelHistogramPlot**          | `Plots/TravelHistogramPlot.cs`          | Time distribution across 20 travel bins, separate compression/rebound               |
| **VelocityHistogramPlot**        | `Plots/VelocityHistogramPlot.cs`        | Stacked histogram by 10 travel zones, reveals damping at different stroke positions |
| **TravelFrequencyHistogramPlot** | `Plots/TravelFrequencyHistogramPlot.cs` | FFT of travel signal (0-10 Hz), identifies suspension oscillation frequencies       |
| **BalancePlot**                  | `Plots/BalancePlot.cs`                  | Front vs rear scatter with polynomial trend lines, Mean Signed Deviation            |
| **LeverageRatioPlot**            | `Plots/LeverageRatioPlot.cs`            | Leverage ratio curve from linkage kinematics                                        |
| **ImuPlot**                      | `Plots/ImuPlot.cs`                      | Accelerometer magnitude per sensor location after removing gravity                  |

### IMU Plot

`ImuPlot` de-interleaves IMU records by sensor location (records cycle through active locations), converts raw counts to Gs using `AccelLsbPerG` calibration, subtracts 1G from the Z axis to remove gravity, then plots `sqrt(ax^2 + ay^2 + az^2)` per location. Colors: frame=orange, fork=blue, shock=teal.

### Desktop vs Mobile

Desktop builds use extended views in `Sufni.App/Sufni.App/DesktopViews/` that provide richer layouts (side-by-side panels, additional controls). Mobile uses the standard `Views/` with a simpler stacked layout. Plot views wrap ScottPlot's Avalonia control; map views use Mapsui's Avalonia control.
