NEVER make assumptions about naming, paths, flows or code. Always read the code or configurations or ask the user.
If you repeatedly fail to achieve something, ask the user for direction. NEVER make more than two attempts.
NEVER queue several consecutive requests that retry user aproval. Always be ready to be denied.
NEVER add functionality on your initiative, unless the user asked - commenting, logging, failsafes. Ask the user if it might really be needed.
Don't change things not related to the current prompt.

File paths are relative to the repository root (`Sufni.App/`).

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

Inspect `Sufni.App.sln` for the full list of projects. The ones that matter:

- `Sufni.Telemetry/` — pure C# telemetry processing (SST parsing, filters,
  stroke detection, histograms). No platform dependencies.
- `Sufni.Kinematics/` — suspension linkage simulation (bike geometry,
  leverage ratio).
- `Sufni.App/Sufni.App/` — main Avalonia application. Most UI and business
  logic lives here.
- `Sufni.App/Sufni.App.{Windows,macOS,Linux,Android,iOS}/` — thin per-platform
  heads that reference `Sufni.App` and bootstrap Avalonia.
- `ServiceDiscovery/`, `SecureStorage/`, `HapticFeedback/`,
  `FriendlyNameProvider/` — small cross-platform support libraries. Each one
  has a `*.common.cs` interface file alongside per-platform implementations
  (`*.ios.cs`, `*.android.cs`, `*.win.cs`, etc.).

Full details: [ARCHITECTURE.md § Project Structure](ARCHITECTURE.md#project-structure)
and [§ Platform Abstractions](ARCHITECTURE.md#platform-abstractions).

# Finding Things

When you need specifics, read the code rather than guessing. Typical
locations inside `Sufni.App/Sufni.App/`:

- `Views/` and `DesktopViews/` — XAML views, grouped by feature
  (`ItemLists/`, `Editors/`, `SessionPages/`, etc.).
- `ViewModels/` — view models grouped by role:
  - `ItemLists/` — one list view model per entity, projects a store
    into row view models.
  - `Rows/` — cheap, non-editable wrappers around a single store
    snapshot. Implement `IListItemRow` so list controls bind against a
    single shared `x:DataType`.
  - `Editors/` — `BikeEditorViewModel`, `SetupEditorViewModel`,
    `SessionDetailViewModel`. Constructed by the entity coordinator
    from a snapshot, never by another view model. Implement
    `IEditorActions` for the shared `CommonButtonLine`.
  - The shell view models (`MainViewModel`, `MainWindowViewModel`,
    `MainPagesViewModel`, `WelcomeScreenViewModel`) live at the top
    level. Base classes are `ViewModelBase`, `ItemListViewModelBase`,
    and `TabPageViewModelBase` — look at them before adding new view
    models.
- `Coordinators/` — feature workflow owners (`IBikeCoordinator`,
  `ISetupCoordinator`, `ISessionCoordinator`,
  `IPairedDeviceCoordinator`, `IImportSessionsCoordinator`,
  `ISyncCoordinator`, the `IShellCoordinator` desktop/mobile pair, plus
  the desktop-only `IInboundSyncCoordinator` /
  `IPairingServerCoordinator` and the mobile-only
  `IPairingClientCoordinator`). Coordinators are the only writers to
  stores and the only owners of post-save navigation. They never depend
  on view models.
- `Stores/` — shared read state, one per entity family. Each store has
  an `IXxxStore` (read-only) interface for VMs/queries and an
  `IXxxStoreWriter` (read+write) interface reserved for coordinators
  and the composition root. Snapshots are immutable records carrying
  an `Updated` field for optimistic conflict detection.
- `Queries/` — stateless cross-entity reads (`IBikeDependencyQuery`).
  Backed by services, never by view models.
- `Services/` — infrastructure: `IDatabaseService` /
  `SQLiteDatabaseService`, `ITelemetryDataStoreService`,
  `IHttpApiService`, `ISynchronizationServerService` /
  `ISynchronizationClientService`, `IDialogService`, `IFilesService`.
- `Models/` — domain entities (`Session`, `Bike`, `Setup`, `Board`,
  `Track`…) and the data-store abstractions (`ITelemetryDataStore` /
  `ITelemetryFile`).
- `Models/SensorConfigurations/` — sensor calibration classes,
  polymorphic via a `Type` discriminator.
- `Plots/` — ScottPlot-based plot classes (travel, velocity,
  histograms, balance, leverage ratio, etc.).

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
  then a binary TCP protocol negotiates file listings and transfers. See
  `SstTcpClient.cs` for the wire protocol.

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

- 14-byte header: `"SST"` magic, version byte, `uint16` sample rate (Hz),
  `int64` Unix timestamp.
- Records are interleaved `int16` pairs (front, rear) of raw encoder counts.
- Parsing applies spike elimination (sliding-window MAD-based detection with
  interpolation) before returning samples.

Full details: [docs/architecture/acquisition.md § File Format & Parsing](docs/architecture/acquisition.md#file-format--parsing),
including [SST V3 Format](docs/architecture/acquisition.md#sst-v3-format),
[SST V4 TLV Format](docs/architecture/acquisition.md#sst-v4-tlv-format),
[Spike Elimination](docs/architecture/acquisition.md#spike-elimination), and
[V4 Data Structures](docs/architecture/acquisition.md#v4-data-structures).

# Processing Pipeline

`TelemetryData.FromRecording()` in `Sufni.Telemetry/TelemetryData.cs`
orchestrates the pipeline. Roughly:

1. Load and despike raw samples.
2. Convert counts to millimetres of travel using the `Setup`'s
   `ISensorConfiguration` calibration function.
3. Compute velocity via a Savitzky-Golay filter (see `Filters.cs`).
4. Detect strokes — compression, rebound, idling (see `Strokes.cs`).
5. Detect airtimes from stroke overlap heuristics.
6. Build travel, velocity, fine-velocity and FFT-based frequency histograms
   (MathNet.Numerics does the FFT).
7. Compute statistics (max/avg travel and velocity, bottomouts,
   velocity-band percentages, etc.).

Thresholds and tunables live in `Sufni.Telemetry/Parameters.cs`.

The result is a `TelemetryData` object serialized with MessagePack and stored
as a BLOB on the session row.

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

SQLite via `sqlite-net-pcl`, async, WAL enabled. The implementation is in
`Sufni.App/Sufni.App/Services/SQLiteDatabaseService.cs`; the interface lists
all available operations. The database file location is platform-specific
app data (`%LOCALAPPDATA%`, `~/Library/Application Support`,
`~/.local/share`, etc.).

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
(`Services/SynchronizationServerService.cs`) over TLS with JWT auth,
advertised via mDNS. Mobile clients
(`Services/SynchronizationClientService.cs`, driving `HttpApiService`) pair
with the server, then push and pull entity changes and session data blobs.
These two services are the source of truth for the endpoints and payloads.

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
  Snapshots carry an `Updated` field used as the editor's
  `BaselineUpdated` for optimistic conflict detection at save time.
- **Coordinators** own feature workflows: open/save/delete, post-save
  navigation, and synchronization-arrival handling. `SaveAsync` returns
  a sealed `Saved` / `Conflict` / `Failed` record so editors can prompt
  the user before reloading on conflict.
- **MVVM** with CommunityToolkit.Mvvm source generators
  (`[ObservableProperty]`, `[RelayCommand]`). Views are XAML with
  compiled bindings, view models contain no direct UI dependencies.
- **Navigation** flows through `IShellCoordinator`
  (`DesktopShellCoordinator` for tabs, `MobileShellCoordinator` for
  the back stack). View models never poke at the shell view model
  directly.
- **Strategy pattern** for sensor calibrations: `ISensorConfiguration`
  with polymorphic JSON deserialization selected by a `Type`
  discriminator.
- **Data store abstraction** (`ITelemetryDataStore` / `ITelemetryFile`)
  unifies mass-storage, network and storage-provider sources.
- **Dependency injection** with
  `Microsoft.Extensions.DependencyInjection`. Coordinators with
  constructor-time event subscriptions
  (`SessionCoordinator`, `PairedDeviceCoordinator`, `SyncCoordinator`,
  the desktop-only `IInboundSyncCoordinator` /
  `IPairingServerCoordinator` and the mobile-only
  `IPairingClientCoordinator`) are eagerly resolved in
  `App.OnFrameworkInitializationCompleted` so the subscriptions wire
  up before any sync, pairing, or telemetry arrival happens.

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

# Key Dependencies

Avalonia, CommunityToolkit.Mvvm, ScottPlot(.Avalonia), Mapsui(.Avalonia),
sqlite-net-pcl, MessagePack, MathNet.Numerics, Microsoft.AspNetCore,
Microsoft.Extensions.DependencyInjection, DynamicData, System.Reactive,
Makaretu.Dns (mDNS), System.IdentityModel.Tokens.Jwt.

For current versions and the full list, see `Directory.Packages.props` and
the individual `.csproj` files.
