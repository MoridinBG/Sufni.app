# Extension Host

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers the public extension host: build-time module registration, capability registration, host services, view resolution, database hooks, sync envelopes, app toolbar actions, and recorded-session contribution slots.

## Overview

`Sufni.App.ExtensionHost/` is the public SDK project that contains the contracts that let a build add extension modules without hard-coding feature-specific dependencies in the shared app. `Sufni.App/Sufni.App/Extensibility/` contains the in-app host implementations for those contracts: capability registration, view lookup, extension database sessions, cascades, sync routing, recorded-session managers, and notification/dialog bridges. The public app owns only neutral host surfaces. Extension modules own their own services, view models, views, database tables, sync payloads, and user-facing workflow semantics.

The SDK is split into two top-level namespaces inside the one assembly:

- `Sufni.App.ExtensionHost.Contracts.*` — interfaces, records, and enums: the compatibility surface. Modules and the app code against these.
- `Sufni.App.ExtensionHost.Runtime.*` — behavioral machinery that ships with the SDK (`RecordedSessionExtensionSlots`, the slot publisher and its batching collection, the mutable `SignalRowAction`, and runtime presentation controls such as `PlotZoomContainer`). **Behavioral changes under `Runtime` are API changes** — extensions observe this machinery's semantics, not just its signatures.

One deliberate cross-reference exists: the `Contracts` scope interface exposes `RecordedSessionExtensionSlots` (a `Runtime` type) — slots *are* part of the scope contract, and the single-assembly split keeps that legal.

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

Recorded-session page and analysis-tab contributions are projected into the
session page collection behind an app-internal wrapper page view model.
`ViewLocator` unwraps that wrapper in both `Match` and `Build`, so template
matching and view construction are decided by the wrapped contribution view
model's registered factory — without this the mobile page carousel would fall
back to the default `ToString()` presenter.

## Host Services

`IFilePickerService` is the neutral file-open picker seam available through DI. Callers pass a `FilePickerRequest` with `FilePickerFilter` descriptors and receive Avalonia `IStorageFile` results. `FilesService` implements this interface alongside the workflow-specific `IFilesService`, so extension modules that need user-selected files can depend on the generic picker surface without depending on app-specific import, GPX, image, bike/setup, or DAQ CONFIG workflows.

`IExtensionDialogService` is the neutral dialog-hosting seam for extension-owned view models. Extensions pass an `ExtensionDialogRequest<TResult>` with a title, layout, and an `IExtensionDialogResultSource<TResult>` view model. `DialogService` hosts the view model through a `ContentControl`, so extension view templates still resolve through `ViewLocator`; desktop uses an owned modal window and mobile/single-view uses the existing overlay host. Completing the result source returns the supplied result, while closing the host without completion returns `default`.

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
modal placement and close gestures.

## Database Hooks

`ExtensionDatabaseConnection` is registered as the concrete singleton behind `IExtensionDatabaseConnection`. Extensions call `OpenSessionAsync()` to wait for normal SQLite initialization and receive an `IExtensionDatabaseSession` scoped to declared extension table types. The session supports table queries plus find/insert/insert-or-replace/update/delete operations and rejects table types that are not owned by a registered extension migrator. For extension-owned multi-statement writes, `RunInTransactionAsync(Action<IExtensionDatabaseTransaction>)` runs a synchronous transaction callback with the same table validation on `Table`, `Find`, `Insert`, `InsertOrReplace`, `Update`, and `Delete`; exceptions roll the whole callback back.

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

Extensions declare references to core rows through `IExtensionCascadeRuleProvider`. `ExtensionCascadeService` validates that each rule targets a table declared by an extension migrator and then applies the requested action inside `ISynchronizableRepository<T>.DeleteAsync` when a core entity kind/id is deleted:

- `SoftDelete` marks the extension row deleted and updates its timestamp.
- `HardDelete` removes the extension row.

The core row soft-delete and matching extension cascade rules share the same transaction. Rules are applied by entity kind/id even when the core row is already tombstoned or absent, which lets retries and orphaned extension rows converge. Startup orphan repair applies the same declared rules after core cleanup. `IExtensionStateRefreshParticipant` lets extension state refresh after cascade work without public coordinators knowing extension store types; delete workflows refresh after the transaction commits when any rule matched. The main page startup database load also invokes these participants after the core stores refresh, so extension-owned read stores are hydrated before list, toolbar, and recorded-session contributions need persisted extension state.

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
optional command parameter. `AppToolbarContributionsView` renders each
command contribution to match the surface it is placed in, set through
its `Presentation` property: an icon-only embedded button with the label
as a hover tooltip in the desktop nav rail, and a labeled menu item in
the mobile side panel. Either way it maps only those descriptor fields.
This path is for simple icon/text commands; it does not give an
extension an arbitrary control tree or extension-specific button
template. Extensions that need custom interactive UI contribute
`AppToolbarViewContribution` instead. View contributions carry an
`IAppToolbarContributionViewModel` and render directly in the
contributions host; the host wraps non-control view models in
`ContentControl`, allowing the extension view registry to resolve a
matching view template. The host reserves no space when both
contribution families are empty, so public builds with no contributions
add nothing to the rail or side panel. Providers are DI-created, so
toolbar commands and view models can depend on normal extension and host
services.

## Recorded-Session Scope

`SessionDetailViewModel` owns one `RecordedSessionExtensionManager` per open recorded session. The manager creates scopes from registered `IRecordedSessionExtensionFactory` instances on `Loaded`, updates them with `RecordedSessionHostState`, and disposes them on `Unloaded` / final close. Factory extension ids are required and unique.

`RecordedSessionHostState` is faceted so extensions receive only the host facts needed by each workflow. `Identity` carries the session id, display name, timestamp, duration, and loaded/active flags. `Selection` carries the current analysis range. `Timeline` carries the track timeline context, telemetry duration, an `IRecordedSessionTimeline` cursor/range interface, and neutral timeline-alignment state. Timeline alignment is represented as an optional pending mark with a target (`GpsTrack`, `ExternalMedia`, or `None`), an optional subject id, and the marked seconds; the host owns this state so only one start/end alignment flow can be active at a time across host features and extensions. The timeline interface also carries neutral timeline playback requests: the host raises `PlaybackToggleRequested` when Space is pressed while the pointer is over a recorded time-series plot with a published cursor, and `PlaybackStopRequested` on a primary click inside a plot. Extensions that own a playback source may respond by driving `SetCursorPosition`; with no subscriber the requests are no-ops. An extension that starts driving the cursor must mark the timeline through `SetPlaybackActive(true)` and clear it when playback ends — while `IsPlaybackActive` is set, host plot views suppress pointer-driven cursor updates so the mouse does not fight the playback source, and the host timeline pans the visible range (keeping the zoom span) whenever a driven cursor lands outside it, so the cursor stays visible on every linked surface. `Analysis` carries current damping percentages, damping speed cutoffs, velocity averaging mode, and travel distribution mode. The state does not expose the app's session snapshot, recorded-session domain snapshot, database internals, or concrete editor timeline view model.

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
- create a derived recorded session, update a session's source-absolute
  origin, rename or delete a session, request recompute, or open a session in a
  background tab. These callbacks live on the recorded-session host
  operations surface rather than on a second editing service, so editing
  extensions stay scoped to the open recorded-session context.

`IRecordedSessionDataReader.GetProcessedTelemetryAsync` delegates to
`ISessionProcessedTelemetryReader`. While the recorded-session editor is loaded,
the session is retained so extension readers, plots, and mobile detail generation
share one decoded `TelemetryData` instance for the current `(sessionId, Updated,
ProcessingFingerprintJson)` processed-payload key. That instance is shared
infrastructure state and must be treated as read-only by extensions.

`IRecordedSessionDataReader.GetTrackAsync` returns the session-window track
projection used by the recorded-session view: cached points when the cache is
current, or a read-only projection from the linked full track when the cache is
missing or aligned to an older GPS offset. Alignment and regeneration use the
session row's own timestamp and duration — the values the processed-write path
generates the cache from — so the read path never deserializes the processed
telemetry blob per session (matching enumerates every session, where a per-session
blob decode dominated the scan). The read path does not persist regenerated points.

Operation leases reject stale progress and cancel superseded work, so extension tasks share the existing editor busy surface without controlling the editor lifecycle. Extension work reports percent values on a `0..100` scale. The recorded-session host projects those reports through `SessionOperationPresentationState` and renders the standard nonblocking busy overlay above the current session content. Extension operation progress does not set the session detail `ScreenState`; that state remains reserved for loading and error state of the session detail itself.

## Derivation Windows & Editing Operations

Recorded-session editing extensions can describe that a session's processed
telemetry is derived from a source-absolute window of another session's raw
recording source. The public host exposes this through a single
`IRecordedSessionDerivationWindowProvider`. Public builds register a no-op
provider; an extended build may replace it with one durable extension-owned
provider. There is no provider aggregation, duplicate-provider validation, or
generic multi-extension derivation bus.

`RecordedSessionDerivationWindow(SourceSessionId, StartSeconds, EndSeconds)`
is serialized into the processing fingerprint. The app-side
`RecordedSessionDerivationWindowCache` hydrates provider state before store
refresh, gives read graphs a synchronous lookup, and emits per-session changes
when the provider raises `WindowsChanged`. Projection, recompute, source
retention, and sync use `SourceSessionId` to find the raw source row; `Start`
and `End` are part of the fingerprint/staleness input.

The provider's retention methods answer only cross-session references. A
self-window does not retain its own raw source after deletion; a derived session
whose window points at another session does. Delete and startup cleanup consult
this same provider so the raw source survives until no live session window
references it.

The small editing callbacks on `IRecordedSessionHostOperations` are the host
side of that same model. They let an extension create an unprocessed derived
session, update a session origin before mutating the durable window, rename
without going through editor save/navigation, request a window-change recompute,
and open the derived session in the background on desktop. Mobile implements
background open as a no-op.

## Recorded-Session Slots

`RecordedSessionExtensionSlots` is the shared contribution surface exposed by recorded signals, media, and analysis workspaces. Scopes add contributions to their own slot collection; the manager mirrors them into the host collection and rebuilds when scope collections change.
Slot mirroring is coalesced and published through batched collection resets so
one extension update does not fan out as repeated intermediate empty/add UI
states.
Before mirroring, the manager validates that each contribution's extension id
matches the owning factory id and that hosted signal row targets are well formed.
Hosted signal row contributions must identify themselves through
`RecordedSessionProjectionRowTarget.Extension(extensionId, contributionId)` using
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

- signal toolbar commands
- signal toolbar views
- contributed pages
- media panes
- map overlays
- analysis banners, analysis tabs, analysis plot overlays, and analysis metric annotations
- session-list indicators and actions
- plot context-menu actions
- plot-row header actions
- hosted signal rows
- recorded time-range overlays

View-model-backed slot families use marker interfaces instead of `object`:
signal toolbar views, page, media pane, analysis banner/tab/overlay,
session-list indicator, session-list action, and hosted signal row
contributions each require the matching
`IRecordedSession...ContributionViewModel` marker. Descriptor-only
families such as signal toolbar commands, map overlays, analysis
metrics, plot context actions, row header actions, and time-range
overlays carry neutral records or command descriptors instead.

Contribution view-model lifetime is owned by the host surface that materializes
the view model. `IExtensionViewModel` remains a marker contract; when a realized
contribution view model also implements synchronous `IDisposable`, the host
disposes it when the contribution is replaced or removed, when the host control
detaches from the visual tree, or when the recorded-session page controller is
disposed on final close. Direct host controls reuse owners by contribution key
across redundant rebuilds, so disposal is tied to removal/replacement rather
than every slot refresh. Extension view-model disposal must therefore be
idempotent and must not depend on an async callback from the host.

Recorded-session analysis tab contributions carry a `CreateViewModel`
factory rather than requiring the tab view model to be created when the
scope publishes its slots. Desktop materializes an analysis tab only when
that tab is first selected, retains the created content for the open
recorded session, and drops it if the contribution is removed or
re-published. Mobile projects analysis tabs into normal recorded-session
pages and creates the view model during page projection, matching the
mobile page lifecycle. This lazy view-model creation does not change scope
ownership: the recorded-session manager still owns scope creation, host-state
updates, slot mirroring, and disposal on unload/final close.

A hosted signal row whose plot should match the app's themed time-series
rows can contribute the SDK's neutral `RecordedSessionSignalPlotViewModel`
(namespace `Sufni.App.ExtensionHost.Runtime.RecordedSessions`) as its
`IRecordedSessionHostedSignalRowContributionViewModel`. The view model carries
neutral `RecordedSessionSignalSeries` (each tagged with a
`RecordedSessionSignalSeriesRole` the app maps to a theme-invariant signal
color), a value-axis inversion flag, duration, empty message, optional airtime
spans, and observable `ShowAirtime` / `Timeline`. The host recognizes this view
model type and renders it with the app's `ExtensionSignalPlotView` (a
`SufniTimeSeriesPlotView`), so the row gets app theming, the shared cursor and
visible-range link, and the inherited airtime overlay without the extension
drawing on a raw plot. Extensions that need rendering the app cannot express
generically still supply their own view through the view registry.

Recorded-session signal toolbar command and view contributions both carry
a `RecordedSessionToolbarZone` value. The host renders `Leading`
contributions at the start of the signal toolbar and `Trailing`
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

Analysis-tab contributions add neutral view-model-backed content to the
recorded-session Telemetry Analysis area. Each contribution declares a zero-based
`RequestedIndex` relative to the built-in analysis tab order. Desktop renders
contributions before the built-in tab with the same requested index and sorts
multiple contributions by `Order`, extension id, and contribution id; requested
indexes after the built-in range render after the built-in analysis tabs.
Mobile projects the same contributions into the recorded-session pages
collection at the corresponding analysis-page position, after the Signals page.

Time-series signal targets are typed at the extension boundary. Built-in signal rows are referenced with `RecordedSessionBuiltInSignalRow`; extension-owned rows are referenced with `RecordedSessionProjectionRowTarget.Extension(extensionId, contributionId)`. The target's `StableKey` is the only string used internally for row lookup and persisted expansion state. Host XAML may keep legacy `SignalRowIds` for built-in rows behind conversion helpers, but extension-facing contribution records do not expose those row id strings.

Analysis plot overlays are generic descriptors. A descriptor can contain lines, bands, and labels. Lines may use explicit plot coordinates or the host plot's full current horizontal span for analysis reference lines. Labels specify text, text/background color, font size, anchor, and either explicit plot coordinates or the host plot's current right edge for analysis value labels. Plot controls render those primitives without knowing why an extension contributed them.

Analysis plot overlays target `RecordedSessionAnalysisPlotTarget` values, which combine a plot family with the relevant suspension side, balance type, or IMU location. Analysis metric annotations target `RecordedSessionAnalysisMetricTarget` enum values for the front and rear HSC, HSR, LSC, and LSR percentage slots. Extensions contribute display text, an optional delta text, a tone, and an order. `VelocityAnalysisHost` renders annotations beside the matching host metric and sorts multiple annotations by `Order`, extension id, and contribution id.

Recorded time-range overlays use neutral `RecordedTimeRangeOverlayColor` ARGB
records and line/fill style descriptors in `Sufni.App.ExtensionHost`; the app
plot layer converts those descriptors to ScottPlot primitives at render time.

## Neutrality Rules

Public code may name the host, app toolbar actions, slots, descriptors, migrations, cascades, sync envelopes, and operation leases. Public code must not name extension-specific entities, database columns, payload fields, platform services, view models, views, assets, or workflows.

This is now maintained by convention and code review rather than an automated test. The previous `PublicNeutralityTests` repository-scanning enforcement has been removed.
