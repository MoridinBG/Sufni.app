NEVER make assumptions about naming, paths, flows or code. Always read the code or configurations or ask the user.
If you repeatedly fail to achieve something, ask the user for direction. NEVER make more than two attempts.
NEVER queue several consecutive requests that retry user aproval. Always be ready to be denied.
NEVER add functionality on your initiative, unless the user asked - commenting, logging, failsafes. Ask the user if it might really be needed.
Don't change things not related to the current prompt.

File paths are relative to the repository root (`Sufni.App/`).

# Documentation Is Required Context

The summaries in this file are navigation aids, not the source of truth.
Before answering architecture questions, changing non-trivial code, or adding
tests for a subsystem, read `docs/ARCHITECTURE.md` and the linked
`docs/architecture/*.md` document for the area you are touching. Do this before
designing the change or explaining the current flow.

Use the architecture docs to find the intended boundaries, ownership, and
terminology; then verify the exact behavior in code/configuration before making
claims or edits. If the docs and code disagree, treat the code as current truth,
call out the documentation drift, and update the docs when the task includes
documentation work.

It is very important to keep the docs up to date as changes are introduced.

# Project Overview

Sufni.App is a cross-platform desktop and mobile application for recording,
importing and analyzing mountain-bike suspension telemetry. Data is captured by
a Pico-based DAQ device that writes binary `.SST` files. The app imports those
files (via USB mass storage or WiFi/TCP), processes them into analysis-ready
data, stores them in SQLite, and renders plots and maps for suspension tuning.
A desktop instance can additionally act as a sync hub so mobile devices can
pull session data over HTTP.

The UI is Avalonia (XAML, MVVM with compiled bindings). Plots use ScottPlot,
maps use Mapsui.

# Solution Layout

Use `Sufni.App.sln` as the full-matrix solution. For day-to-day work,
scenario-specific solutions now exist beside it:

- `Sufni.Desktop.sln`
- `Sufni.Android.sln`
- `Sufni.iOS.sln`

The projects that matter most:

- `Sufni.Telemetry/` — pure C# telemetry processing (SST parsing, filters,
  stroke detection, histograms). No platform dependencies.
- `Sufni.Kinematics/` — suspension linkage simulation (bike geometry,
  leverage ratio).
- `Sufni.App/Sufni.App/` — main Avalonia application. Most UI and business
  logic lives here, organized **domain-slice-first** (top-level folders are
  slices — `Bikes/`, `Setups/`, `Sessions/`, `LiveDaq/`, `Acquisition/`,
  `SyncAndPairing/`, `MapsAndTracks/`, `Shell/`, `Shared/`, `Infrastructure/`,
  `Extensibility/` — with the technical layers nested inside each slice).
- `Sufni.App/Sufni.App.Desktop/` — desktop-only layer for sync server,
  ASP.NET Core hosting, and other desktop-only infrastructure.
- `Sufni.App/Sufni.App.{Windows,macOS,Linux}/` — desktop heads that reference
  `Sufni.App.Desktop` and bootstrap Avalonia.
- `Sufni.App/Sufni.App.{Android,iOS}/` — mobile heads that reference
  `Sufni.App` directly and bootstrap Avalonia.
- Platform-service abstractions live in `Sufni.App/Sufni.App/Infrastructure/`
  (`IServiceDiscovery`, `IHapticFeedback`, `IFriendlyNameProvider`), except
  `ISecureStorage`, which is an extension-host contract in
  `Sufni.App.ExtensionHost/Contracts/Services/`. Their implementations
  live in the owning platform heads, with socket-based service discovery in shared
  code and Bonjour implementations in the Apple heads.

- `docs/plans/` contains design notes and planning artifacts. Do not read those, unless explicitly asked to. 
  Do not read the plan files, do not consider them.

Full details: [ARCHITECTURE.md § Project Structure](docs/ARCHITECTURE.md#project-structure)
and [§ Platform Abstractions](docs/ARCHITECTURE.md#platform-abstractions).

# Finding Things

Do not tail or head commands or files unless known that they are >800-1000 lines

When you need specifics, read the code rather than guessing. Typical
locations inside `Sufni.App/Sufni.App/`:

Top-level folders are domain slices; the technical layers below live *inside* each slice
(the slice map is in
[ARCHITECTURE.md § Domain-slice layout](docs/ARCHITECTURE.md#domain-slice-layout-sufniappsufniapp)).
The layer roles are consistent across slices:

- `<Slice>/Views/` and `<Slice>/DesktopViews/` — XAML views, grouped by feature within the
  slice (`ItemLists/`, `Editors/`, `SessionPages/`, etc.). Generic reusable controls,
  dialogs, overlays, and converters live under `Shared/Views/` and `Shared/DesktopViews/`.
- `<Slice>/ViewModels/` — view models grouped by role:
  - `ItemLists/` — one list view model per entity, projects a store
    into row view models.
  - `Rows/` — cheap, non-editable wrappers around a single store
    snapshot. Implement `IListItemRow` so list controls bind against a
    single shared `x:DataType`.
  - `Editors/` — `BikeEditorViewModel` (`Bikes/ViewModels/Editors/`),
    `SetupEditorViewModel` (`Setups/ViewModels/Editors/`), `SessionDetailViewModel`
    (`Sessions/Detail/ViewModels/Editors/`), `LiveDaqDetailViewModel`
    (`LiveDaq/ViewModels/Editors/`). Constructed by
    `IEditorFactory` from snapshots or live-session contexts, never by
    another view model. Persisted-entity editors implement
    `IEditorActions` for the shared `CommonButtonLine`.
  - The shell view models (`ShellRootViewModel`, `ShellWorkspaceViewModel`,
    `MainPagesViewModel`) live in `Shell/ViewModels/`.
    The base classes `ViewModelBase`, `ItemListViewModelBase`, and
    `TabPageViewModelBase` live in `Shared/Base/` — look at them before adding
    new view models.
- `<Slice>/Coordinators/` — feature workflow owners: `BikeCoordinator` (`Bikes/`),
  `SetupCoordinator` (`Setups/`), `SessionCoordinator` (`Sessions/Coordination/`),
  `LiveDaqCoordinator` (`LiveDaq/`), `ImportSessionsCoordinator` (`Acquisition/`),
  `PairedDeviceCoordinator` / `SyncCoordinator` plus the desktop-only
  `IInboundSyncCoordinator` / `IPairingServerCoordinator` and the mobile-only
  `IPairingClientCoordinator` (`SyncAndPairing/`), and the `IShellCoordinator`
  desktop/mobile pair (`Shell/Coordinators/`). View models depend on read-only
  stores. Persisted write workflows live in coordinators or session use-case
  services behind coordinators; sync appliers and transaction runners may use
  writer interfaces for publish-only post-persistence updates. Stores remain
  the only owners of DynamicData cache mutation. Coordinators construct and
  open editor view models only through `IEditorFactory` —
  despite the name it is the editor *gateway*: the interface exposes only
  open-or-focus (`Open*`) and close (`Close*`) operations, and view-model
  creation is an implementation detail of the concrete `EditorFactory`.
  No coordinator holds a view-model factory of its own.
- `<Slice>/Stores/` — shared read state, one per entity family (`BikeStore` in
  `Bikes/Stores/`, `SetupStore` in `Setups/Stores/`, `SessionStore` in `Sessions/`,
  etc.). Each store has an `IXxxStore` (read-only) interface for VMs/queries and an
  `IXxxStoreWriter` (read+write) interface reserved for coordinators
  and the composition root. Snapshots are immutable records carrying
  an `Updated` field for optimistic conflict detection. The shared
  `SourceCacheStoreBase` lives in
  `Sufni.App.ExtensionHost/Runtime/Stores/`.
- `<Slice>/Queries/` — cross-entity reads (`IBikeDependencyQuery` in `Bikes/`,
  `ILiveDaqKnownBoardsQuery` in `LiveDaq/`). Backed by services and read-only
  stores, never by view models.
- Services are split across slices and `Infrastructure/`:
  - SQLite persistence (`SqliteConnectionContext`, `DatabaseMigrationRunner`),
    `IDialogService` (view models' prompt contract; its `IDialogHost` wiring facet is
    used only by `App`), and `IFilesService` → `Infrastructure/`.
  - `ISessionTelemetryWriter` (pre-persistence telemetry validation and
    summary-metric/session-window derivation) → `Sessions/Processing/Services/`;
    `ISessionRepository` → `Sessions/Services/`.
  - `ITelemetryDataStoreService` → `Acquisition/Services/`.
  - `IHttpApiService`, `ISynchronizationClientService` → `SyncAndPairing/Services/`
    (the desktop-only `ISynchronizationServerService` stays in `Sufni.App.Desktop`).
  - The live preview transport layer (`LiveDaqClient`, `LiveProtocolReader`,
    `LiveDaqSessionState`, `LiveDaqUiSnapshot`, protocol models) →
    `LiveDaq/Services/LiveStreaming/`.
- `<Slice>/Models/` — domain entities: `Session` (`Sessions/Models/`), `Bike`
  (`Bikes/Models/`), `Setup` (`Setups/Models/`), `Board` (`SyncAndPairing/Models/`),
  `Track` (`MapsAndTracks/Models/`). The data-store abstractions
  (`ITelemetryDataStore` / `ITelemetryFile`) live in `Acquisition/Models/`.
- `Setups/Models/SensorConfigurations/` — sensor calibration classes,
  polymorphic via a `Type` discriminator.
- `<Slice>/Plots/` — ScottPlot-based plot classes per slice (e.g. `Sessions/Plots/`,
  `LiveDaq/Plots/`, `MapsAndTracks/Views/Plots/`). Shared plot bases (`SufniPlot`,
  `TelemetryPlot`, cursor/airtime layout helpers) live in `Shared/Plots/`.

DI container setup lives in `Sufni.App/Sufni.App/App.axaml.cs`. The
shared `App.ServiceCollection` is the static composition root —
platform entry points
(`Sufni.App/Sufni.App.{Windows,macOS,Linux,Android,iOS}/`) add their
platform-specific registrations to it before
`OnFrameworkInitializationCompleted` runs the shared registrations and
calls `BuildServiceProvider()`. There is no separate
`RegisteredServices` indirection.

Full details: [docs/architecture/ui.md](docs/architecture/ui.md),
including [Stores](docs/architecture/ui.md#stores),
[Coordinators](docs/architecture/ui.md#coordinators),
[Queries](docs/architecture/ui.md#queries),
[View Models](docs/architecture/ui.md#view-models), and
[Dependency Injection](docs/architecture/ui.md#dependency-injection).

# Telemetry Acquisition

The app imports SST files from a DAQ device via two methods, both abstracted
behind `ITelemetryDataStore` / `ITelemetryFile`:

- **Mass storage** — the DAQ exposes itself as a USB drive. Sufni.App
  identifies it by a `BOARDID` marker file at the root of the drive, scans
  the root for `*.SST` files, and moves files into `uploaded/` or `trash/`
  subdirectories after import/delete.
- **Network** — mDNS discovery (`_gosst._tcp`) locates a WiFi-connected DAQ,
  then the management protocol negotiates file listings and transfers. See
  `docs/architecture/daq-management.md` and
  `Services/Management/ManagementClient.cs` for the wire protocol.

`TelemetryDataStoreService` aggregates every available source and polls for
drive changes.

Full details: [docs/architecture/acquisition.md § Data Acquisition](docs/architecture/acquisition.md#data-acquisition),
including [Interfaces](docs/architecture/acquisition.md#interfaces),
[Mass Storage](docs/architecture/acquisition.md#mass-storage),
[Network (WiFi DAQ)](docs/architecture/acquisition.md#network-wifi-daq), and
[Storage Provider](docs/architecture/acquisition.md#storage-provider).

# SST File Format

Binary, little-endian. Parsing lives in
`Sufni.Telemetry/RawTelemetryData.cs`, which is the authoritative reference.
High level:

- SST v3 uses the legacy fixed-record header and interleaved front/rear raw
  encoder counts.
- SST v4 is TLV-based and may include telemetry, markers, IMU, GPS, and
  temperature chunks.
- SST v5 is descriptor/chunk based, preserves fixed-rate stream gaps, and can
  expose segment-aware raw travel/IMU runs.
- Parsing returns raw samples, segments, and metadata. Spike elimination runs
  later inside `TelemetryData.FromRecording()` after setup/bike calibration is
  known.

Full details: [docs/architecture/acquisition.md § File Format & Parsing](docs/architecture/acquisition.md#file-format--parsing),
including [SST V3 Format](docs/architecture/acquisition.md#sst-v3-format),
[SST V4 TLV Format](docs/architecture/acquisition.md#sst-v4-tlv-format),
[SST V5 Chunked Format](docs/architecture/acquisition.md#sst-v5-chunked-format),
[Spike Elimination](docs/architecture/acquisition.md#spike-elimination), and
[Parsed Telemetry Data Structures](docs/architecture/acquisition.md#parsed-telemetry-data-structures).

# Processing Pipeline

`TelemetryData.FromRecording()` in `Sufni.Telemetry/TelemetryData.cs`
orchestrates the pipeline. Roughly:

1. Load and despike raw samples.
2. Convert counts to millimetres of travel using the `Setup`'s
   `ISensorConfiguration` calibration function.
3. Compute velocity via a fixed-dt Savitzky-Golay filter (see `Filters.cs`).
4. Detect strokes — compression, rebound, idling (see `Strokes.cs`).
5. Detect airtimes from stroke overlap heuristics.
6. Persist travel/velocity arrays, stroke data, bin definitions, markers, gaps,
   and other parsed side-channel data.
7. Compute histogram tallies, FFT frequency histogram, balance, vibration, and
   other statistics lazily on demand.

Thresholds and tunables live in `Sufni.Telemetry/Parameters.cs`.

Front/rear processing runs in parallel when both sides are present. The result
is a `TelemetryData` object serialized with MessagePack and stored as a BLOB on
the session row.

Full details: [docs/architecture/processing.md § Signal Processing Pipeline](docs/architecture/processing.md#signal-processing-pipeline),
including [Travel Calculation](docs/architecture/processing.md#travel-calculation),
[Velocity Calculation](docs/architecture/processing.md#velocity-calculation),
[Stroke Detection](docs/architecture/processing.md#stroke-detection),
[Stroke Categorization](docs/architecture/processing.md#stroke-categorization),
[Airtime Detection](docs/architecture/processing.md#airtime-detection),
[Processing Parameters](docs/architecture/processing.md#processing-parameters), and
[Serialized Structure](docs/architecture/processing.md#serialized-structure). Suspension
geometry lives under [§ Suspension Kinematics](docs/architecture/processing.md#suspension-kinematics).

# Persistence

SQLite via `sqlite-net-pcl`, async, WAL enabled. `SqliteConnectionContext`
owns the shared connection and initialization gate; `DatabaseMigrationRunner`
owns startup schema work and cleanup; aggregate repositories expose the
persistence operations and store the values they are given.
`SessionTelemetryWriter` sits in front of `ISessionRepository` for
processed-data writes and owns the domain computation that precedes them:
telemetry validation, summary-metric derivation, and session-window track
association/generation. The database file location is platform-specific app
data (`%LOCALAPPDATA%`, `~/Library/Application Support`, `~/.local/share`,
etc.).

Sync-enabled entities inherit from `Models/Synchronizable.cs`, which adds
`Updated` / `ClientUpdated` / `Deleted` timestamps used for soft delete and
conflict resolution. Rows soft-deleted more than a day ago are purged on
startup.

Full details: [docs/architecture/persistence.md](docs/architecture/persistence.md),
including [Database Service](docs/architecture/persistence.md#database-service),
[Soft Delete](docs/architecture/persistence.md#soft-delete), and
[Conflict Resolution](docs/architecture/persistence.md#conflict-resolution).

# Cross-Device Sync

A desktop instance can host an embedded ASP.NET Core HTTP server
(`SyncAndPairing/Services/SynchronizationServerService.cs`) over TLS with JWT auth,
advertised via mDNS. Mobile clients
(`SyncAndPairing/Services/SynchronizationClientService.cs`, driving `HttpApiService`) pair
with the server, then push and pull entity changes plus processed-session and
recorded-source BLOBs through dedicated binary endpoints. Protocol version 2 is
carried by an HTTP header on every sync request. These two services are the
source of truth for the endpoints and payloads.

Full details: [docs/architecture/sync.md](docs/architecture/sync.md),
including [Pairing Flow](docs/architecture/sync.md#pairing-flow),
[Server](docs/architecture/sync.md#server), and [Client](docs/architecture/sync.md#client).

# Architecture Notes

- **Layered presentation**: `Views → ViewModels → Coordinators / Stores
/ Queries → Services → Platform`. View models do not depend on other
  feature view models for business answers, do not write to stores
  directly, and do not subscribe to synchronization events. Those
  responsibilities live on coordinators. The only carve-out is shell
  composition (`MainPagesViewModel` holding the page VMs for binding).
- **Stores** own shared read state via DynamicData
  `SourceCache<TSnapshot, TKey>`. Each store is exposed behind a
  read-only and a writer interface; only coordinators get the writer.
  Persisted-entity snapshots carry an `Updated` field used as the
  editor's `BaselineUpdated` for optimistic conflict detection at save
  time. Runtime-only stores (`LiveDaqStore`) omit `Updated` and
  `RefreshAsync()`.
- **Coordinators** own feature workflows: open/save/delete, post-save
  navigation, and synchronization-arrival handling. `SaveAsync` returns
  a sealed `Saved` / `Conflict` / `Failed` record so editors can prompt
  the user before reloading on conflict.
- **MVVM** with CommunityToolkit.Mvvm source generators
  (`[ObservableProperty]`, `[RelayCommand]`). Views are XAML with
  compiled bindings, view models contain no direct UI dependencies.
- **Navigation** flows through `IShellCoordinator`, implemented by
  `ShellWorkspaceCoordinator` for the shared compact/workspace tab stack.
  View models never poke at the shell view model directly.
- **Strategy pattern** for sensor calibrations: `ISensorConfiguration`
  with polymorphic JSON deserialization selected by a `Type`
  discriminator.
- **Data store abstraction** (`ITelemetryDataStore` / `ITelemetryFile`)
  unifies mass-storage, network and storage-provider sources.
- **Live DAQ streaming** uses a separate framed TCP protocol, its own
  discovery catalog and browse lease, a runtime-only store, and a
  per-identity shared stream that tabs attach to through leases. See
  [docs/architecture/live-streaming.md](docs/architecture/live-streaming.md).
- **Dependency injection** with
  `Microsoft.Extensions.DependencyInjection`. Services with
  constructor-time event subscriptions
  (`SessionSyncApplier`, `PairedDeviceCoordinator`, `SyncCoordinator`,
  the desktop-only `IInboundSyncCoordinator` /
  `IPairingServerCoordinator` and the mobile-only
  `IPairingClientCoordinator`) are eagerly resolved in
  `App.OnFrameworkInitializationCompleted` so the subscriptions wire
  up before any sync, pairing, or telemetry arrival happens.
  `SessionCoordinator` itself is a thin router over the session
  use-case classes (`SessionLoader` for reads, `SessionCommandService`
  for store-writing commands and recompute requests, the
  `SessionRecomputeEngine` it drives, and `SessionSyncApplier`) — see
  [ui-workflows.md](docs/architecture/ui-workflows.md#coordinators).

Full details: [docs/architecture/ui.md](docs/architecture/ui.md),
starting from [Layered Architecture](docs/architecture/ui.md#layered-architecture)
through [Stores](docs/architecture/ui.md#stores),
[Coordinators](docs/architecture/ui.md#coordinators),
[Queries](docs/architecture/ui.md#queries),
[View Models](docs/architecture/ui.md#view-models),
[Dependency Injection](docs/architecture/ui.md#dependency-injection), and
[Navigation](docs/architecture/ui.md#navigation). Sensor calibration strategy
lives at [§ Sensor Calibration](docs/architecture/processing.md#sensor-calibration), and
ScottPlot plot classes at [§ Data Visualization](docs/architecture/ui.md#data-visualization).

# Terminology

View model: owns only screen state, command flow, and binding-friendly projection.
Store: owns shared read state for an entity family and direct lookups over that state.
Query: answers a business question; it does not own the shared entity collection.
Coordinator: owns workflows with side effects and store writes.
Service or factory: owns infrastructure-facing work such as picker integration, datastore creation, and background execution.

# Testing

When writing new code, write unit tests for it. When changing code verify and update the existing tests.
When adding or changing unit tests, `docs/TESTING.md` is required reading before writing the tests.
When adding or changing view tests, `docs/VIEW-TESTING.md` is required reading before writing the view tests.

- Test one unit through its public interface.
- Aim for high coverage of meaningful behavior; trivial assignments, constants, and other obvious no-logic code do not need direct tests.
- Reuse helpers from `Sufni.App.Tests/TestSupport/` and `Sufni.App.ExtensionHost.TestSupport/` (fixtures shared with extension test projects) before adding local duplicates.
- Cover desktop/mobile branches when behavior differs; use `TestApp.SetIsDesktop(true/false)` only for `ViewLocator` or plot-gesture branches (see `docs/TESTING.md`) — other shell-specific behavior is driven by explicit service configuration. Cover `BaselineUpdated` versus `Updated` optimistic-concurrency flows where relevant.
- Prefer deterministic async control such as `TaskCompletionSource<T>` and `TestSynchronizationContextScope` over timing-based waits.

# Key Dependencies

Avalonia, CommunityToolkit.Mvvm, ScottPlot(.Avalonia), Mapsui(.Avalonia),
sqlite-net-pcl, MessagePack, MathNet.Numerics, Microsoft.AspNetCore,
Microsoft.Extensions.DependencyInjection, DynamicData, System.Reactive,
Makaretu.Dns (mDNS), System.IdentityModel.Tokens.Jwt.

For current versions and the full list, see `Directory.Packages.props` and
the individual `.csproj` files.
