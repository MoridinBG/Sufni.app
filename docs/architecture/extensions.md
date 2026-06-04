# Extension Host

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers the public extension host: build-time module registration, capability registration, host services, view resolution, database hooks, sync envelopes, app toolbar actions, and recorded-session contribution slots.

## Overview

`Sufni.App/Sufni.App/ExtensionHost/` contains the public contracts that let a build add extension modules without hard-coding feature-specific dependencies in the shared app. The public app owns only neutral host surfaces. Extension modules own their own services, view models, views, database tables, sync payloads, and user-facing workflow semantics.

There is no assembly scanning. Modules are added explicitly by build-time code through `App.RegisterBuildTimeExtensions(App.Extensions)`. Public builds have no implementation of that partial method, so the call is removed by the compiler and `App.Extensions.Modules` remains empty.

## Module Startup

`IAppExtensionModule` is the module boundary:

- `Id` identifies the module and must be unique.
- `RegisterServices(IServiceCollection, AppExtensionServiceRegistrationContext)` runs before core shared services are registered.
- `RegisterCapabilities(IAppExtensionCapabilityRegistry)` runs after module services are registered and before `BuildServiceProvider()`.

`AppExtensionCollection` owns the ordered module list and rejects duplicate ids with ordinal comparison. `AppExtensionCapabilityRegistry` records capabilities that the app consumes after service registration, including eager extension service types and app toolbar action contributions. After `BuildServiceProvider()`, `App.OnFrameworkInitializationCompleted` resolves the existing eager coordinators and then resolves each registered eager extension service type so constructor-time subscriptions can attach before runtime work starts.

Desktop/mobile mode is computed before module service registration. The registration context exposes that mode plus the service collection so modules can keep platform-specific registrations outside the shared app source.

## Build Imports

`Directory.Build.props` imports `Directory.Private.props` when present, and `Directory.Build.targets` imports `Directory.Private.targets` when present. Setting `SufniEnablePrivateExtensions=true` without both import files fails before build preparation with a clear error. Public project files do not reference extension projects directly; non-public builds add those references through the imported files.

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

`SqLiteDatabaseService` is registered as the concrete singleton behind both `IDatabaseService` and `IExtensionDatabaseConnection`. `GetInitializedConnectionAsync()` awaits normal initialization before returning the raw `SQLiteAsyncConnection`.

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

## Cascade Rules

Extensions declare references to core rows through `IExtensionCascadeRuleProvider`. `ExtensionCascadeService` validates that each rule targets a table declared by an extension migrator and then applies the requested action when core delete workflows run:

- `SoftDelete` marks the extension row deleted and updates its timestamp.
- `HardDelete` removes the extension row.

The service is invoked after successful bike, setup, session, and track delete work. Startup orphan repair applies the same declared rules after core cleanup. `IExtensionStateRefreshParticipant` lets extension state refresh after cascade work without public coordinators knowing extension store types.

## Sync Envelopes

`SynchronizationData.ExtensionBatches` carries opaque extension sync envelopes. Each envelope has an extension id, payload version, and serialized payload bytes. Core sync code does not inspect payload fields.

`ExtensionSyncService` asks registered participants for outgoing batches during push/pull response creation. Incoming batches are routed to the participant with the matching extension id; unknown ids are ignored so public and extended builds can coexist. Known participant failures propagate so the sync operation fails before the last-sync timestamp can advance.

Extension sync is ordered after core entity/app-preference sync during apply, so extension payloads can rely on the core rows from the same sync response already being present locally.

## App Toolbar Actions

`AppToolbarContribution` is the app-level action slot. Modules register `IAppToolbarContributionProvider` implementations through DI during `RegisterServices(...)`; `MainPagesViewModel` resolves the providers, flattens their contributions, sorts them by `Order`, and exposes the result as `ExtensionToolbarActions`. The desktop nav rail and mobile side panel render those actions through `AppToolbarContributionsView`, after the built-in import/GPX actions and before the paired-device/theme area.

The contribution carries an extension id, contribution id, order, and view model. The rendered host wraps non-control view models in `ContentControl`, allowing the extension view registry to resolve a matching view template. Providers are DI-created, so toolbar view models can depend on normal extension and host services.

## Recorded-Session Scope

`SessionDetailViewModel` owns one `RecordedSessionExtensionManager` per open recorded session. The manager creates scopes from registered `IRecordedSessionExtensionFactory` instances on `Loaded`, updates them with `RecordedSessionHostState`, and disposes them on `Unloaded` / final close.

`RecordedSessionHostContext` exposes constrained host operations:

- set or clear the analysis range
- set timeline visible range
- post errors or notifications
- request contributed page selection
- run a cancellable operation through `RecordedSessionOperationCoordinator`

Operation leases reject stale progress and cancel superseded work, so extension tasks share the existing editor busy surface without controlling the editor lifecycle.

## Recorded-Session Slots

`RecordedSessionExtensionSlots` is the shared contribution surface exposed by recorded graph, media, and statistics workspaces. Scopes add contributions to their own slot collection; the manager mirrors them into the host collection and rebuilds when scope collections change.

The current public slot families are:

- graph toolbar actions and panels
- contributed pages
- media panes
- map overlays
- statistics banners and statistics plot overlays
- session-list indicators and actions
- plot context-menu actions
- plot-row header actions
- hosted graph rows
- recorded time-range overlays

Session-list indicators and actions are created by registered `IRecordedSessionListContributionProvider` implementations. `RecordedSessionListExtensionService` aggregates every provider and sorts each contribution family by `Order`, so separate modules can contribute to the same recorded-session row without replacing each other. Public builds with no providers return empty contribution lists.

Views render these through generic host controls or bindable descriptor properties. Public plot and map models receive neutral descriptors only; they do not depend on extension workflow semantics.

## Neutrality Rules

Public code may name the host, app toolbar actions, slots, descriptors, migrations, cascades, sync envelopes, and operation leases. Public code must not name extension-specific entities, database columns, payload fields, platform services, view models, views, assets, or workflows.
