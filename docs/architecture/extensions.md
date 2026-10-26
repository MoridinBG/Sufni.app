# Extension Host

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers the public extension host: build-time module registration, capability registration, view resolution, database hooks, and the recorded-session scope.

## Overview

`Sufni.App.ExtensionHost/` is the public SDK project that contains the contracts that let a build add extension modules without hard-coding feature-specific dependencies in the shared app. `Sufni.App/Sufni.App/Extensibility/` contains the in-app host implementations for those contracts: capability registration, view lookup, extension database sessions, cascades, recorded-session managers, and notification/dialog bridges. The public app owns only neutral host surfaces. Extension modules own their own services, view models, views, database tables, and user-facing workflow semantics.

The SDK is split into two top-level namespaces inside the one assembly:

- `Sufni.App.ExtensionHost.Contracts.*` — interfaces, records, and enums: the compatibility surface. Modules and the app code against these.
- `Sufni.App.ExtensionHost.Runtime.*` — behavioral machinery that ships with the SDK (the mutable `SignalRowAction`, runtime presentation controls such as `PlotZoomContainer`, and the `Stores.SourceCacheStoreBase` used by public and private persisted stores). **Behavioral changes under `Runtime` are API changes** — extensions observe this machinery's semantics, not just its signatures.

The theme model is **not** part of the SDK. It lives in a separate leaf project `Sufni.App.Theming` (assembly and namespace `Sufni.App.Theming`, Avalonia-only) that the app and theme-consuming extensions reference directly; the extension host's contracts and runtime use no theme type. An extension that must style a ScottPlot surface the app cannot render for it references `Sufni.App.Theming` and calls `SufniThemes.FromVariant(...)`. See [theming.md](theming.md).

Accepted-for-now contract dependencies (removing them is a redesign of the extension model, out of scope): `IServiceCollection` in module registration, `Func<Control>` view factories, `AsyncTableQuery<T>`, `IStorageFile`, and `Sufni.Telemetry` types.
The runtime presentation surface also references ScottPlot for shared plot-axis
rules that extension-owned plot controls can use without copying app logic.

There is no assembly scanning. Modules are added explicitly by build-time code through the two-argument partial method `App.RegisterBuildTimeExtensions(App.Extensions, isDesktop)`. Public builds have no implementation of that partial method, so the call is removed by the compiler and `App.Extensions.Modules` remains empty.

## Module Startup

`IAppExtensionModule` is the module boundary:

- `Id` identifies the module and must be unique.
- `RegisterServices(IServiceCollection, AppExtensionServiceRegistrationContext)` runs before core shared services are registered.
- `RegisterCapabilities(IAppExtensionCapabilityRegistry)` runs after module services are registered and before `BuildServiceProvider()`.

`AppExtensionCollection` owns the ordered module list and rejects duplicate ids with ordinal comparison. `AppExtensionCapabilityRegistry` records the eager extension service types that the app resolves after service registration. After `BuildServiceProvider()`, `App.OnFrameworkInitializationCompleted` resolves the existing eager coordinators and then resolves each registered eager extension service type so constructor-time subscriptions can attach before runtime work starts.

Desktop/mobile mode is computed before module service registration. The registration context (`AppExtensionServiceRegistrationContext`) exposes only `IsDesktop`; the service collection is passed to `RegisterServices` as its own parameter. Together they let modules keep platform-specific registrations outside the shared app source.

Modules that expose one concrete singleton through one or more neutral service
interfaces use `AddExtensionSingletonAlias<TService, TImplementation>()` so
the concrete type and every alias resolve to the same instance. Modules use the
generic `RegisterView<TViewModel, TSharedView>()` /
`RegisterView<TViewModel, TSharedView, TWorkspaceView>()` /
`RegisterView<TViewModel, TSharedView, TCompactView, TWorkspaceView>()`
overloads for normal parameterless extension view registrations.

## Build Imports

`Directory.Build.props` defines an overridable `SufniPrivateExtensionsRoot`, defaulting to the sibling `../Sufni.PrivateExtensions/` folder, and imports `Directory.Private.props` from that root when present. `Directory.Build.targets` imports `Directory.Private.targets` from the same root when present. Setting `SufniEnablePrivateExtensions=true` without both import files fails before build preparation with a clear error.

Public project files do not reference extension projects, implementation folders, platform hooks, views, or assets directly. Non-public builds add concrete references and linked partials only through the external import files under `SufniPrivateExtensionsRoot`.

`Sufni.App.ExtensionHost.TestSupport/` is the shared test-support project next to the SDK. It references `Sufni.App` so its `TestExtensionHostHarness` and `TestExtensionCapabilityRegistry` exercise the real host machinery (migration runner, table-catalog-validated database sessions, cascade service, capability/view registries) instead of re-implementations, and it carries the fixtures both repos' test projects reuse (`TempDatabase`, `TempDirectory`, inline dispatcher/runner fakes, the recorded-session host-context builder, `TestRecordedSessionTimeline`). Extension repos reference it through an overridable msbuild property defaulted in their own build props.

Desktop platform heads expose a neutral `Program.RegisterPlatformExtensions(IServiceCollection)` partial hook. Each head calls it after its built-in platform services and desktop sync registration, before returning the configured Avalonia builder. Private build imports can compile platform-specific partial implementations into the head assemblies without adding extension references to public project files.

## View Resolution

`ExtensionViewRegistry` stores shared, compact-specific, and workspace-specific factories keyed by view-model type. `ViewLocator` resolves against the active `UiLayoutProfile` and checks extension registrations before its built-in profile/common dictionaries:

1. Extension workspace factory, when the active profile is Workspace and one is registered.
2. Extension compact factory, when the active profile is Compact and one is registered.
3. Extension shared factory.
4. Built-in workspace or compact factory for the active profile.
5. Built-in common factory.
6. Fallback text block.

This keeps public `ViewLocator` dictionaries free of extension view-model types while still letting extension views render anywhere Avalonia data templates are used. Extensions that use one control in both profiles register only the shared factory; a profile-specific factory is optional, not a migration requirement.


## Runtime Presentation Controls

`Sufni.App.ExtensionHost.Runtime.Presentation` is public SDK surface. It
contains small host-compatible controls and descriptors that extension views
may use directly without referencing `Sufni.App`. `SignalRowAction` remains the
row-header action descriptor used by app and extension signal rows.
`PlotZoomContainer` is the opt-in contract for zoomable plot surfaces: the
container raises `PlotZoomRequested` on double-tap/double-click when the
gesture does not originate from an interactive descendant. The same runtime
surface also publishes `BoundedZoomRule`, `AxisRangeConstraints`, and
`PlotZoomFractions` so app-owned and extension-owned ScottPlot surfaces can use
the same pan/zoom clamping behavior.

The app's `PlotZoomOverlayHost` responds to that routed request by borrowing
the container child and moving the live control into the modal overlay. The
borrow/return contract is intentionally explicit: `BorrowChild()` detaches the
child and pins the child's effective `DataContext`; `ReturnChild(child)`
reattaches the same instance and restores either the child's previous local
`DataContext` or inherited binding. Extensions that draw their own plot surface
can wrap that surface in `PlotZoomContainer` to participate in the same modal
without the app knowing the extension's concrete view type. The pinned
`DataContext` preserves bindings that resolve through the control's data
context; inputs supplied through the original name scope, such as `#Root` or
`ElementName` bindings, are not carried with the borrowed child. While borrowed,
the extension still owns its control state and rendering; the host owns only the
modal placement and close gestures. The same ownership rule applies to custom
plot internals: an extension that creates disposable ScottPlot plottables or
native drawing resources must dispose them when replacing/clearing the plot and
when its control detaches.

## Runtime Stores

`Sufni.App.ExtensionHost.Runtime.Stores.SourceCacheStoreBase<TSnapshot, TKey>`
is SDK-visible runtime behavior for DynamicData-backed persisted read stores.
The base owns the cache lifetime, keyed connect/watch helpers, and UI-thread
dispatching for snapshot/removal publication. Public app stores and extension
stores can reuse it while keeping their own neutral read/write interfaces,
persistence services, and domain-specific lookup methods. Public app code still
does not name extension-specific store types; extensions opt into the shared
runtime base from their own projects.

## Shared Caching

`Sufni.Telemetry.Caching.SingleFlightLruCache<TKey, TValue>` is the
dependency-lower shared primitive for bounded reusable computations. It
keeps pending producers separate from retained LRU values, so count and weight
limits apply only after successful completion. Callers may use count-only,
weight-only, or combined limits and may reject successful values from retention
with a predicate while still sharing that producer result with current waiters.
Waiter cancellation is independent: producers run with their own non-cancelable
lifetime, and faulted or canceled producers remain retryable. Oversize values
are shared but not retained. `Clear()` detaches pending generations without
canceling their existing waiters, and detached late completions cannot populate
the current retained state.

## Database Hooks

`ExtensionDatabaseConnection` is registered as the concrete singleton behind `IExtensionDatabaseConnection`. Extensions call `OpenSessionAsync(extensionId)` to wait for normal SQLite initialization and receive an owner-scoped `IExtensionDatabaseSession`. Every table query and find/insert/insert-or-replace/update/delete operation must match the captured extension id, exact CLR table type, and table name registered by that extension's migrator. Undeclared tables, core-owned tables, and another extension's tables are rejected before sqlite-net runs. The ownership catalog is frozen from startup migrator registrations.

For extension-owned multi-statement writes, `RunInTransactionAsync(Action<IExtensionDatabaseTransaction>)` runs a synchronous transaction callback that retains the same owner id and repeats the same checks on `Table`, `Find`, `Insert`, `InsertOrReplace`, `Update`, and `Delete`; obtaining a transaction never broadens database access, and callback exceptions roll the whole transaction back.

Extension schema state lives in `extension_schema_version`:

- `extension_id` is the primary key.
- `version` stores the last successful migration version.

Each `IExtensionDatabaseMigrator` declares an `ExtensionId`, `TargetVersion`, owned `TableTypes`, and ordered `ExtensionDatabaseMigrationStep` entries. During database initialization the runner:

1. Creates/migrates core tables.
2. Creates `extension_schema_version`.
3. Creates extension-owned tables from migrator table declarations. Table creation is startup setup, outside the per-step transactions.
4. Runs missing migration steps in ascending target version. Each step receives an owner-scoped migration transaction, and its schema/data changes plus the `extension_schema_version` update execute in the same SQLite transaction.
5. Commits that transaction only when both the step and version update succeed; an exception rolls both back and leaves the step pending for the next startup.
6. Runs core cleanup.
7. Runs extension orphan repair.

Extension-owned tables are not part of core models, stores, or snapshots.
Migrator validation rejects blank or duplicate extension ids, invalid target
versions, duplicate migration step versions, reserved core table names, and
duplicate extension table ownership before creating tables or running steps.

## Cascade Rules

Extensions declare references to core rows through `IExtensionCascadeRuleProvider`. `ExtensionCascadeService` validates that each rule targets a table declared by an extension migrator and then applies the requested action inside `ISynchronizableRepository<T>.DeleteAsync` when a core entity kind/id is deleted:

- `SoftDelete` marks the extension row deleted and updates its timestamp.
- `HardDelete` removes the extension row.

The core row soft-delete and matching extension cascade rules share the same transaction. Rules are applied by entity kind/id even when the core row is already tombstoned or absent, which lets retries and orphaned extension rows converge. Startup orphan repair applies the same declared rules after core cleanup. `IExtensionStateRefreshParticipant` lets extension state refresh after cascade work without public coordinators knowing extension store types; delete workflows refresh after the transaction commits when any rule matched. The main page startup database load also invokes these participants after the core stores refresh, so extension-owned read stores are hydrated before recorded-session contributions need persisted extension state.

## Recorded-Session Scope

`SessionDetailViewModel` owns one `RecordedSessionExtensionManager` per open recorded session. The manager creates scopes from registered `IRecordedSessionExtensionFactory` instances on `Loaded`, updates them with `RecordedSessionHostState` derived from the recorded editor state/host facets, and disposes them on `Unloaded` / final close. Factory extension ids are required and unique.

`RecordedSessionHostState` is faceted so extensions receive only the host facts needed by each workflow. `Identity` carries the session id, display name, timestamp, duration, and loaded/active flags. `Selection` carries the current analysis range. `Timeline` carries the track timeline context, telemetry duration, and an `IRecordedSessionTimeline` cursor/range interface. The timeline interface also carries neutral timeline playback requests: the host raises `PlaybackToggleRequested` when Space is pressed while the pointer is over a recorded time-series plot with a published cursor, and `PlaybackStopRequested` on a primary click inside a plot. A playback source (the built-in video pane, see [video.md](video.md)) may respond by driving `SetCursorPosition`; with no subscriber the requests are no-ops. A playback source that starts driving the cursor must mark the timeline through `SetPlaybackActive(true)` and clear it when playback ends — while `IsPlaybackActive` is set, host plot views suppress pointer-driven cursor updates so the mouse does not fight the playback source, and the host timeline pans the visible range (keeping the zoom span) whenever a driven cursor lands outside it, so the cursor stays visible on every linked surface. `Analysis` carries current damping percentages, damping speed cutoffs, velocity averaging mode, and travel distribution mode. The state does not expose the app's session snapshot, recorded-session domain snapshot, database internals, or concrete editor timeline view model.

`RecordedSessionHostContext` is constructed from the grouped
`RecordedSessionHostServices` record and an `IRecordedSessionHostOperations`
interface implementation so service access and host callbacks stay explicit
without a long positional constructor. It exposes constrained host operations:

- set or clear the analysis range
- set timeline visible range
- post errors or notifications

Scopes publish nothing into the recorded-session views; the built-in features
render their own presentation.

## Recorded Time-Range Overlays

Recorded time-range overlays use neutral `RecordedTimeRangeOverlayColor` ARGB
records and line/fill style descriptors in `Sufni.App.ExtensionHost`; the app
plot layer converts those descriptors to ScottPlot primitives at render time.

## Neutrality Rules

Public code may name the host, descriptors, migrations, and cascades. Public code must not name extension-specific entities, database columns, payload fields, platform services, view models, views, assets, or workflows.

This is now maintained by convention and code review rather than an automated test. The previous `PublicNeutralityTests` repository-scanning enforcement has been removed.
