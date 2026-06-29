# Extension Host

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers the public extension host: build-time module registration, capability registration, host services, view resolution, database hooks, sync envelopes, app toolbar actions, and recorded-session contribution slots.

## Overview

`Sufni.App.ExtensionHost/` is the public SDK project that contains the contracts that let a build add extension modules without hard-coding feature-specific dependencies in the shared app. `Sufni.App/Sufni.App/ExtensionHosting/` contains the in-app host implementations for those contracts: capability registration, view lookup, extension database sessions, cascades, sync routing, recorded-session managers, and notification/dialog bridges. The public app owns only neutral host surfaces. Extension modules own their own services, view models, views, database tables, sync payloads, and user-facing workflow semantics.

The SDK is split into two top-level namespaces inside the one assembly:

- `Sufni.App.ExtensionHost.Contracts.*` — interfaces, records, and enums: the compatibility surface. Modules and the app code against these.
- `Sufni.App.ExtensionHost.Runtime.*` — behavioral machinery that ships with the SDK (`RecordedSessionExtensionSlots`, the slot publisher and its batching collection, the mutable `TelemetryPlotRowAction`). **Behavioral changes under `Runtime` are API changes** — extensions observe this machinery's semantics, not just its signatures.

One deliberate cross-reference exists: the `Contracts` scope interface exposes `RecordedSessionExtensionSlots` (a `Runtime` type) — slots *are* part of the scope contract, and the single-assembly split keeps that legal.

Accepted-for-now contract dependencies (removing them is a redesign of the extension model, out of scope): `IServiceCollection` in module registration, `Func<Control>` view factories, `AsyncTableQuery<T>`, `IStorageFile`, and `Sufni.Telemetry` types.

There is no assembly scanning. Modules are added explicitly by build-time code through the two-argument partial method `App.RegisterBuildTimeExtensions(App.Extensions, isDesktop)`. Public builds have no implementation of that partial method, so the call is removed by the compiler and `App.Extensions.Modules` remains empty.

## Module Startup

`IAppExtensionModule` is the module boundary:

- `Id` identifies the module and must be unique.
- `RegisterServices(IServiceCollection, AppExtensionServiceRegistrationContext)` runs before core shared services are registered.
- `RegisterCapabilities(IAppExtensionCapabilityRegistry)` runs after module services are registered and before `BuildServiceProvider()`.

`AppExtensionCollection` owns the ordered module list and rejects duplicate ids with ordinal comparison. `AppExtensionCapabilityRegistry` records capabilities that the app consumes after service registration, including eager extension service types and app toolbar action contributions. After `BuildServiceProvider()`, `App.OnFrameworkInitializationCompleted` resolves the existing eager coordinators and then resolves each registered eager extension service type so constructor-time subscriptions can attach before runtime work starts.

Desktop/mobile mode is computed before module service registration. The registration context (`AppExtensionServiceRegistrationContext`) exposes only `IsDesktop`; the service collection is passed to `RegisterServices` as its own parameter. Together they let modules keep platform-specific registrations outside the shared app source.

Modules that expose one concrete singleton through one or more neutral service
interfaces use `AddExtensionSingletonAlias<TService, TImplementation>()` so
the concrete type and every alias resolve to the same instance. Modules use the
generic `RegisterView<TViewModel, TSharedView>()` /
`RegisterView<TViewModel, TSharedView, TDesktopView>()` overloads for normal
parameterless extension view registrations.

## Build Imports

`Directory.Build.props` defines an overridable `SufniPrivateExtensionsRoot`, defaulting to the sibling `../Sufni.PrivateExtensions/` folder, and imports `Directory.Private.props` from that root when present. `Directory.Build.targets` imports `Directory.Private.targets` from the same root when present. Setting `SufniEnablePrivateExtensions=true` without both import files fails before build preparation with a clear error.

Public project files do not reference extension projects, implementation folders, platform hooks, views, or assets directly. Non-public builds add concrete references and linked partials only through the external import files under `SufniPrivateExtensionsRoot`.

`Sufni.App.ExtensionHost.TestSupport/` is the shared test-support project next to the SDK. It references `Sufni.App` so its `TestExtensionHostHarness` and `TestExtensionCapabilityRegistry` exercise the real host machinery (migration runner, table-catalog-validated database sessions, cascade service, capability/view registries) instead of re-implementations, and it carries the fixtures both repos' test projects reuse (`TempDatabase`, `TempDirectory`, inline dispatcher/runner fakes, the recorded-session host-context builder, `TestRecordedSessionTimeline`). Extension repos reference it through an overridable msbuild property defaulted in their own build props.

Desktop platform heads expose a neutral `Program.RegisterPlatformExtensions(IServiceCollection)` partial hook. Each head calls it after its built-in platform services and desktop sync registration, before returning the configured Avalonia builder. Private build imports can compile platform-specific partial implementations into the head assemblies without adding extension references to public project files.

## View Resolution

`ExtensionViewRegistry` stores shared and desktop-specific factories keyed by view-model type. `ViewLocator` checks the extension registry before its built-in desktop/shared dictionaries:

1. Desktop extension factory, when running on desktop and registered.
2. Shared extension factory.
3. Built-in desktop factory.
4. Built-in shared factory.
5. Fallback text block.

This keeps public `ViewLocator` dictionaries free of extension view-model types while still letting extension views render anywhere Avalonia data templates are used.

## Host Services

`IFilePickerService` is the neutral file-open picker seam available through DI. Callers pass a `FilePickerRequest` with `FilePickerFilter` descriptors and receive Avalonia `IStorageFile` results. `FilesService` implements this interface alongside the workflow-specific `IFilesService`, so extension modules that need user-selected files can depend on the generic picker surface without depending on app-specific import, GPX, image, bike/setup, or DAQ CONFIG workflows.

`IExtensionDialogService` is the neutral dialog-hosting seam for extension-owned view models. Extensions pass an `ExtensionDialogRequest<TResult>` with a title, layout, and an `IExtensionDialogResultSource<TResult>` view model. `DialogService` hosts the view model through a `ContentControl`, so extension view templates still resolve through `ViewLocator`; desktop uses an owned modal window and mobile/single-view uses the existing overlay host. Completing the result source returns the supplied result, while closing the host without completion returns `default`.

## Database Hooks

`ExtensionDatabaseConnection` is registered as the concrete singleton behind `IExtensionDatabaseConnection`. Extensions call `OpenSessionAsync()` to wait for normal SQLite initialization and receive an `IExtensionDatabaseSession` scoped to declared extension table types. The session supports table queries plus find/insert/insert-or-replace/update/delete operations and rejects table types that are not owned by a registered extension migrator.

Extension schema state lives in `extension_schema_version`:

- `extension_id` is the primary key.
- `version` stores the last successful migration version.

Each `IExtensionDatabaseMigrator` declares an `ExtensionId`, `TargetVersion`, owned `TableTypes`, and ordered `ExtensionDatabaseMigrationStep` entries. During database initialization the runner:

1. Creates/migrates core tables.
2. Creates `extension_schema_version`.
3. Creates extension-owned tables from migrator table declarations.
4. Runs missing migration steps in ascending target version.
5. Updates `extension_schema_version` after each successful step.
6. Runs core cleanup.
7. Runs extension orphan repair.

Extension-owned tables are not part of core models, stores, or snapshots.
Migrator validation rejects blank or duplicate extension ids, invalid target
versions, duplicate migration step versions, reserved core table names, and
duplicate extension table ownership before creating tables or running steps.

## Cascade Rules

Extensions declare references to core rows through `IExtensionCascadeRuleProvider`. `ExtensionCascadeService` validates that each rule targets a table declared by an extension migrator and then applies the requested action when core delete workflows run:

- `SoftDelete` marks the extension row deleted and updates its timestamp.
- `HardDelete` removes the extension row.

The service is invoked after successful bike, setup, session, and track delete work. Startup orphan repair applies the same declared rules after core cleanup. `IExtensionStateRefreshParticipant` lets extension state refresh after cascade work without public coordinators knowing extension store types. The main page startup database load also invokes these participants after the core stores refresh, so extension-owned read stores are hydrated before list, toolbar, and recorded-session contributions need persisted extension state.

## Sync Envelopes

`SynchronizationData.ExtensionBatches` carries opaque extension sync envelopes. Each envelope has an extension id, payload version, and serialized payload bytes. Core sync code does not inspect payload fields.

`ExtensionSyncService` asks registered participants for outgoing batches during push/pull response creation. Incoming batches are routed to the participant with the matching extension id; unknown ids are ignored so public and extended builds can coexist. Known participant failures propagate so the sync operation fails before the last-sync timestamp can advance.

Extension sync is ordered after core entity/app-preference sync during apply, so extension payloads can rely on the core rows from the same sync response already being present locally.

## App Toolbar Contributions

App toolbar contributions are split into simple command descriptors and
arbitrary view contributions. Modules register
`IAppToolbarContributionProvider` implementations through DI during
`RegisterServices(...)`; each provider declares the owning
`ExtensionId` and returns command contributions from
`CreateCommandContributions()` plus custom view contributions from
`CreateViewContributions()`. `MainPagesViewModel` resolves the
providers, validates that every contribution id is present, every
contribution extension id matches its provider, and no extension
reuses a contribution id across either app-toolbar family, then sorts
by `Order` and exposes `ExtensionToolbarCommands` and
`ExtensionToolbarViews`. The desktop nav rail and mobile side panel
render those contributions through `AppToolbarContributionsView`,
after the built-in import/GPX actions and before the paired-device /
theme area.

`AppToolbarCommandContribution` carries an extension id, contribution
id, order, label, optional `ToolbarIconDescriptor`, `ICommand`, and
optional command parameter. The host renders it as a `CommandBarButton`
and maps only those descriptor fields. This path is for simple
icon/text commands; it does not give an extension an arbitrary control
tree or extension-specific button template. Extensions that need custom
interactive UI contribute `AppToolbarViewContribution` instead. View
contributions carry an `IAppToolbarContributionViewModel` and render in
`CommandBar.Content`; the host wraps non-control view models in
`ContentControl`, allowing the extension view registry to resolve a
matching view template. Providers are DI-created, so toolbar commands
and view models can depend on normal extension and host services.

## Recorded-Session Scope

`SessionDetailViewModel` owns one `RecordedSessionExtensionManager` per open recorded session. The manager creates scopes from registered `IRecordedSessionExtensionFactory` instances on `Loaded`, updates them with `RecordedSessionHostState`, and disposes them on `Unloaded` / final close. Factory extension ids are required and unique.

`RecordedSessionHostState` is faceted so extensions receive only the host facts needed by each workflow. `Identity` carries the session id, display name, timestamp, duration, and loaded/active flags. `Selection` carries the current analysis range. `Timeline` carries the track timeline context, telemetry duration, an `IRecordedSessionTimeline` cursor/range interface, and neutral timeline-alignment state. Timeline alignment is represented as an optional pending mark with a target (`GpsTrack`, `ExternalMedia`, or `None`), an optional subject id, and the marked seconds; the host owns this state so only one start/end alignment flow can be active at a time across host features and extensions. The timeline interface also carries neutral timeline playback requests: the host raises `PlaybackToggleRequested` when Space is pressed while the pointer is over a recorded time-series plot with a published cursor, and `PlaybackStopRequested` on a primary click inside a plot. Extensions that own a playback source may respond by driving `SetCursorPosition`; with no subscriber the requests are no-ops. An extension that starts driving the cursor must mark the timeline through `SetPlaybackActive(true)` and clear it when playback ends — while `IsPlaybackActive` is set, host plot views suppress pointer-driven cursor updates so the mouse does not fight the playback source, and the host timeline pans the visible range (keeping the zoom span) whenever a driven cursor lands outside it, so the cursor stays visible on every linked surface. `Statistics` carries current damper percentages, damping speed cutoffs, velocity averaging mode, and travel histogram mode. The state does not expose the app's session snapshot, recorded-session domain snapshot, database internals, or concrete editor timeline view model.

`RecordedSessionHostContext` is constructed from the grouped
`RecordedSessionHostServices` record and an `IRecordedSessionHostOperations`
interface implementation so service access and host callbacks stay explicit
without a long positional constructor. It exposes constrained host operations:

- set or clear the analysis range
- set timeline visible range
- begin, resolve, or cancel a pending timeline alignment mark
- post errors or notifications
- request contributed page selection
- run a cancellable operation through `RecordedSessionOperationCoordinator`
- read processed telemetry and track points through `IRecordedSessionDataReader`

`IRecordedSessionDataReader.GetTrackAsync` returns the session-window track
projection used by the recorded-session view: cached points when the cache is
current, or a read-only projection from the linked full track and processed
telemetry when the cache is missing or aligned to an older GPS offset. The
read path does not persist regenerated points.

Operation leases reject stale progress and cancel superseded work, so extension tasks share the existing editor busy surface without controlling the editor lifecycle. Extension work reports percent values on a `0..100` scale. The recorded-session host projects those reports through `SessionOperationPresentationState` and renders the standard nonblocking busy overlay above the current session content. Extension operation progress does not set the session detail `ScreenState`; that state remains reserved for loading and error state of the session detail itself.

## Recorded-Session Slots

`RecordedSessionExtensionSlots` is the shared contribution surface exposed by recorded graph, media, and statistics workspaces. Scopes add contributions to their own slot collection; the manager mirrors them into the host collection and rebuilds when scope collections change.
Slot mirroring is coalesced and published through batched collection resets so
one extension update does not fan out as repeated intermediate empty/add UI
states.
Before mirroring, the manager validates that each contribution's extension id
matches the owning factory id and that hosted graph row targets are well formed.
Hosted graph row contributions must identify themselves through
`RecordedSessionGraphRowTarget.Extension(extensionId, contributionId)` using
their own extension and contribution ids; plot-row actions and time-range
overlays may target built-in rows or hosted rows published by the same
extension. Contribution ids are unique globally per extension across every
recorded-session slot family in the mirrored scope.
Scopes that rebuild multiple slot families use
`RecordedSessionExtensionSlotPublisher` with a
`RecordedSessionExtensionSlotBuilder` to publish a complete neutral slot
snapshot through the same batched reset path. Off-UI rebuild requests can be
coalesced so only the latest pending snapshot reaches the UI thread.
The builder can copy an existing slot snapshot, and the slot collection exposes
a generic change subscription so host mirroring is not manually repeated per
slot family.

The 15 current public slot families are:

- graph toolbar commands
- graph toolbar views
- contributed pages
- media panes
- map overlays
- statistics banners, statistics tabs, statistics plot overlays, and statistics metric annotations
- session-list indicators and actions
- plot context-menu actions
- plot-row header actions
- hosted graph rows
- recorded time-range overlays

View-model-backed slot families use marker interfaces instead of `object`:
graph toolbar views, page, media pane, statistics banner/tab/overlay,
session-list indicator, session-list action, and hosted graph row
contributions each require the matching
`IRecordedSession...ContributionViewModel` marker. Descriptor-only
families such as graph toolbar commands, map overlays, statistics
metrics, plot context actions, row header actions, and time-range
overlays carry neutral records or command descriptors instead.

Recorded-session graph toolbar command and view contributions both carry
a `RecordedSessionToolbarZone` value. The host renders `Leading`
contributions at the start of the graph toolbar and `Trailing`
contributions at the end, with each family and zone sorted by `Order`,
then extension id, then contribution id. Command contributions render as
`CommandBarButton` instances with label, optional SVG icon, command, and
optional command parameter only. Custom toolbar UI renders through
`RecordedSessionToolbarViewContribution` in `CommandBar.Content`.
Toolbar controls own their own transient UI, such as flyouts. The host
does not provide a generic page-root overlay slot for extension-owned
transient controls.

Session-list indicators and actions are created by registered `IRecordedSessionListContributionProvider` implementations. Each provider declares its owning `ExtensionId`; duplicate providers for the same extension are rejected. `RecordedSessionListExtensionService` aggregates every provider, validates that contribution ids are present, validates that each contribution extension id matches its provider, rejects duplicate contribution ids globally per extension across indicators and actions for the row, and sorts each contribution family by `Order`, so separate modules can contribute to the same recorded-session row without replacing each other. Public builds with no providers return empty contribution lists.

Providers whose contribution availability can change without a core recorded-session summary change also implement `IRecordedSessionListContributionChangeSource`. The aggregate list service exposes those invalidations as a neutral `ContributionsChanged` event. Session list rows respond by asking the list service to recreate their indicator and action descriptors from the latest summary, without the public app learning which extension-owned state changed.

Views render these through generic host controls or bindable descriptor properties. Public plot and map models receive neutral descriptors only; they do not depend on extension workflow semantics.

Statistics-tab contributions add neutral view-model-backed content to the
recorded-session statistics area. Each contribution declares a zero-based
`RequestedIndex` relative to the built-in statistics tab order. Desktop renders
contributions before the built-in tab with the same requested index and sorts
multiple contributions by `Order`, extension id, and contribution id; requested
indexes after the built-in range render after the built-in statistics tabs.
Mobile projects the same contributions into the recorded-session pages
collection at the corresponding statistics-page position, after the graph page.

Time-series graph targets are typed at the extension boundary. Built-in graph rows are referenced with `RecordedSessionBuiltInGraphRow`; extension-owned rows are referenced with `RecordedSessionGraphRowTarget.Extension(extensionId, contributionId)`. The target's `StableKey` is the only string used internally for row lookup and persisted expansion state. Host XAML may keep legacy `TelemetryGraphRowIds` for built-in rows behind conversion helpers, but extension-facing contribution records do not expose those row id strings.

Statistics plot overlays are generic descriptors. A descriptor can contain lines, bands, and labels. Lines may use explicit plot coordinates or the host plot's full current horizontal span for statistic reference lines. Labels specify text, text/background color, font size, anchor, and either explicit plot coordinates or the host plot's current right edge for statistic value labels. Plot controls render those primitives without knowing why an extension contributed them.

Statistics plot overlays target `RecordedSessionStatisticsPlotTarget` values, which combine a plot family with the relevant suspension side, balance type, or IMU location. Statistics metric annotations target `RecordedSessionStatisticsMetricTarget` enum values for the front and rear HSC, HSR, LSC, and LSR percentage slots. Extensions contribute display text, an optional delta text, a tone, and an order. `VelocityStatisticsHost` renders annotations beside the matching host metric and sorts multiple annotations by `Order`, extension id, and contribution id.

Recorded time-range overlays use neutral `RecordedTimeRangeOverlayColor` ARGB
records and line/fill style descriptors in `Sufni.App.ExtensionHost`; the app
plot layer converts those descriptors to ScottPlot primitives at render time.

## Neutrality Rules

Public code may name the host, app toolbar actions, slots, descriptors, migrations, cascades, sync envelopes, and operation leases. Public code must not name extension-specific entities, database columns, payload fields, platform services, view models, views, assets, or workflows.

`PublicNeutralityTests` (`Sufni.App.Tests/ExtensionHost/`) enforces this: it scans the repository's source, markup, project, and documentation files for known private capability vocabulary. The deny list in that file is the single allowed location for those tokens and must be refreshed when private modules add new vocabulary.
