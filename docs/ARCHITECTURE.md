# Architecture

Sufni.App is a cross-platform application for analyzing mountain bike suspension telemetry. It acquires raw sensor data from a Pico-based DAQ device (via USB or WiFi), processes it through a signal analysis pipeline, and presents interactive plots for tuning suspension spring rates and damper settings. It also models bike linkage kinematics to compute leverage ratios and related characteristics. The app runs on Windows, macOS, Linux, Android, and iOS using Avalonia UI, and supports desktop <-> mobile synchronization.

This document is the catalog. Each subsystem is summarized here and the deep details live in `architecture/`.

---

## Table of Contents

- [Project Structure](#project-structure)
- [Platform Abstractions](#platform-abstractions)
- [Desktop vs Mobile](#desktop-vs-mobile)
- [Data Acquisition & File Format](#data-acquisition--file-format)
- [DAQ Management](#daq-management)
- [Signal Processing & Suspension Kinematics](#signal-processing--suspension-kinematics)
- [UI Architecture](#ui-architecture)
- [Plot Rendering](#plot-rendering)
- [Maps & GPS Tracks](#maps--gps-tracks)
- [Live DAQ Streaming](#live-daq-streaming)
- [Live Session Recording](#live-session-recording)
- [Persistence & Serialization](#persistence--serialization)
- [Cross-Device Synchronization](#cross-device-synchronization)

---

## Project Structure

| Project                   | Path                           | Role                                                                                                         |
| ------------------------- | ------------------------------ | ------------------------------------------------------------------------------------------------------------ |
| **Sufni.Telemetry**       | `Sufni.Telemetry/`             | Pure C# library: SST parsing, signal processing, stroke detection, histograms                                |
| **Sufni.Telemetry.Tests** | `Sufni.Telemetry.Tests/`       | Unit tests for telemetry processing                                                                          |
| **Sufni.Kinematics**      | `Sufni.Kinematics/`            | Suspension linkage simulation, leverage ratio calculation                                                    |
| **Sufni.App**             | `Sufni.App/Sufni.App/`         | Neutral shared application layer: views, view models, coordinators, stores, queries, services, models, plots |
| **Sufni.App.Desktop**     | `Sufni.App/Sufni.App.Desktop/` | Desktop-only layer: sync server, ASP.NET Core hosting, inbound desktop sync infrastructure                   |
| **Sufni.App.Windows**     | `Sufni.App/Sufni.App.Windows/` | Windows entry point (`Program.cs`)                                                                           |
| **Sufni.App.macOS**       | `Sufni.App/Sufni.App.macOS/`   | macOS entry point (`Program.cs`)                                                                             |
| **Sufni.App.Linux**       | `Sufni.App/Sufni.App.Linux/`   | Linux entry point (`Program.cs`)                                                                             |
| **Sufni.App.Android**     | `Sufni.App/Sufni.App.Android/` | Android entry point (`MainActivity.cs`)                                                                      |
| **Sufni.App.iOS**         | `Sufni.App/Sufni.App.iOS/`     | iOS entry point (`Main.cs`) and application delegate (`AppDelegate.cs`)                                      |

Scenario-specific solutions live at the repository root:

- `Sufni.App.sln` — full matrix / repo-wide solution
- `Sufni.Desktop.sln` — desktop workflow solution
- `Sufni.Android.sln` — Android workflow solution
- `Sufni.iOS.sln` — iOS workflow solution

---

## Platform Abstractions

| Interface               | File                                                    | Purpose                                                                 | Implementations                                                                                                |
| ----------------------- | ------------------------------------------------------- | ----------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------- |
| `IServiceDiscovery`     | `Sufni.App/Sufni.App/Services/IServiceDiscovery.cs`     | mDNS browse for `_gosst._tcp` and `_sstsync._tcp`                       | `SocketServiceDiscovery` (shared / Win / Linux / Android), `BonjourServiceDiscovery` (macOS / iOS)             |
| `ISecureStorage`        | `Sufni.App/Sufni.App/Services/ISecureStorage.cs`        | Encrypted key-value store for JWT secrets, certificates, refresh tokens | `WindowsSecureStorage`, `LinuxSecureStorage`, `MacOsSecureStorage`, `AndroidSecureStorage`, `IosSecureStorage` |
| `IHapticFeedback`       | `Sufni.App/Sufni.App/Services/IHapticFeedback.cs`       | Tactile feedback: `Click()`, `LongPress()`                              | `AndroidHapticFeedback`, `IosHapticFeedback`                                                                   |
| `IFriendlyNameProvider` | `Sufni.App/Sufni.App/Services/IFriendlyNameProvider.cs` | Human-readable device name for sync identification                      | `AndroidFriendlyNameProvider`, `IosFriendlyNameProvider`                                                       |

Each platform entry point registers its implementations before the shared `App.axaml.cs` initialization runs.

---

## Desktop vs Mobile

A cross-cutting reference for the divergence points between the desktop and mobile experiences: shell coordinators (tabs vs back stack), `Views/` vs `DesktopViews/` selection via `ViewLocator`, the `IsDesktop` runtime flag, desktop-only sync server / inbound coordinators, mobile-only pairing client and haptic / friendly-name services, DI composition order, solution scoping, and behavioral differences across feature workflows.

Topics in [architecture/desktop-vs-mobile.md](architecture/desktop-vs-mobile.md):

- [Project Layout](architecture/desktop-vs-mobile.md#project-layout) — shared `Sufni.App` and desktop-only `Sufni.App.Desktop`
- [View Selection](architecture/desktop-vs-mobile.md#view-selection) — `Views/` vs `DesktopViews/` and how `ViewLocator` picks
- [The IsDesktop Flag](architecture/desktop-vs-mobile.md#the-isdesktop-flag) — where the boundary lives at runtime
- [Navigation Shells](architecture/desktop-vs-mobile.md#navigation-shells) — `DesktopShellCoordinator` vs `MobileShellCoordinator`
- [Desktop-Only Surface](architecture/desktop-vs-mobile.md#desktop-only-surface) — sync server, inbound sync, pairing server
- [Mobile-Only Surface](architecture/desktop-vs-mobile.md#mobile-only-surface) — pairing client, haptics, friendly-name
- [DI Composition](architecture/desktop-vs-mobile.md#di-composition) — platform-first then shared registrations
- [Solution Scoping](architecture/desktop-vs-mobile.md#solution-scoping) — `Sufni.Desktop.sln` / `Android.sln` / `iOS.sln`
- [Testing](architecture/desktop-vs-mobile.md#testing) — `TestApp.SetIsDesktop(...)`
- [Behavioral Differences](architecture/desktop-vs-mobile.md#behavioral-differences) — workflow-level divergences

---

## Data Acquisition & File Format

Telemetry reaches the app through three `ITelemetryDataStore` implementations behind a single interface, and SST files come in two binary versions (V3 fixed-record, V4 TLV with IMU/GPS/markers). Import captures the original SST bytes as a recorded-session source, then derives processed telemetry and a processing fingerprint from that source plus the selected setup/bike calibration.

Topics in [architecture/acquisition.md](architecture/acquisition.md):

- [Data Acquisition](architecture/acquisition.md#data-acquisition) — overview and source-flow diagram
  - [Interfaces](architecture/acquisition.md#interfaces) — `ITelemetryDataStore`, `ITelemetryFile`, `ITelemetryDataStoreService`
  - [Import Screen Boundaries](architecture/acquisition.md#import-screen-boundaries) — canonical worked example of the boundary rules
  - [Mass Storage](architecture/acquisition.md#mass-storage) — `BOARDID` marker, drive probing, background lifecycle
  - [Network (WiFi DAQ)](architecture/acquisition.md#network-wifi-daq) — mDNS discovery and the SST TCP wire protocol
  - [Storage Provider](architecture/acquisition.md#storage-provider) — Avalonia picker integration and duplicate detection
- [File Format & Parsing](architecture/acquisition.md#file-format--parsing) — version dispatch
  - [SST V3 Format](architecture/acquisition.md#sst-v3-format) — legacy fixed-record layout
  - [SST V4 TLV Format](architecture/acquisition.md#sst-v4-tlv-format) — TLV chunks (Rates, Telemetry, Marker, IMU, IMU Meta, GPS)
  - [Spike Elimination](architecture/acquisition.md#spike-elimination) — four-stage anomaly correction
  - [V4 Data Structures](architecture/acquisition.md#v4-data-structures) — `GpsRecord`, `ImuRecord`, `ImuMetaEntry`, `RawImuData`, `MarkerData`

---

## DAQ Management

A side-channel framed TCP protocol for non-streaming DAQ operations: list and retrieve files, mark uploaded, trash remotely, set time, edit and replace CONFIG. Distinct from both the live preview protocol and the import file-transfer protocol, but shares the DAQ's single-client TCP port. Used by the network telemetry store, the import workflow, and the Live DAQ diagnostics tab.

Topics in [architecture/daq-management.md](architecture/daq-management.md):

- [Service Surface](architecture/daq-management.md#service-surface) — `IDaqManagementService`, `IDaqManagementSession`, `DaqManagementException`
- [Wire Protocol](architecture/daq-management.md#wire-protocol) — frame header, frame types, result codes, per-operation choreography
- [Callers](architecture/daq-management.md#callers) — `NetworkTelemetryDataStore`, `NetworkTelemetryFile`, `LiveDaqDetailViewModel`
- [CONFIG Document](architecture/daq-management.md#config-document) — `DaqConfigDocument`, `DaqConfigFields`, `DaqConfigValidator`, `SelectedDeviceConfigFile`

---

## Signal Processing & Suspension Kinematics

`TelemetryData.FromRecording()` orchestrates the full pipeline: measurement preprocessing → travel calibration → Savitzky-Golay velocity → stroke detection → categorization → airtime detection → histogram bin definitions. The `Sufni.Kinematics` library independently solves bike linkage geometry to derive leverage ratios. Sensor calibration maps between raw ADC counts and millimeters of travel. Recorded sessions persist the raw source separately from the derived `TelemetryData` BLOB so stale processed data can be recomputed when the source and dependencies are available.

Topics in [architecture/processing.md](architecture/processing.md):

- [Signal Processing Pipeline](architecture/processing.md#signal-processing-pipeline) — pipeline overview
  - [Travel Calculation](architecture/processing.md#travel-calculation) — sensor-driven mm conversion
  - [Velocity Calculation](architecture/processing.md#velocity-calculation) — Savitzky-Golay smoothed first derivative
  - [Stroke Detection](architecture/processing.md#stroke-detection) — sign changes with top-out concatenation
  - [Stroke Categorization](architecture/processing.md#stroke-categorization) — compression / rebound / idling thresholds
  - [Airtime Detection](architecture/processing.md#airtime-detection) — front/rear overlap heuristics
  - [Processing Parameters](architecture/processing.md#processing-parameters) — all tunables in `Parameters.cs`
  - [Serialized Structure](architecture/processing.md#serialized-structure) — MessagePack `TelemetryData` shape
  - [Recorded Session Derivation](architecture/processing.md#recorded-session-derivation) — raw source, processed BLOB, processing fingerprint, and staleness rules
- [Suspension Kinematics](architecture/processing.md#suspension-kinematics)
  - [Linkage Model](architecture/processing.md#linkage-model) — joint types, links, JSON deserialization
  - [Kinematic Solver](architecture/processing.md#kinematic-solver) — Gauss-Seidel constraint relaxation across shock travel
  - [Bike Characteristics](architecture/processing.md#bike-characteristics) — derived travel limits and leverage ratio
  - [Utilities](architecture/processing.md#utilities) — `CoordinateRotation`, `GroundCalculator`, `EtrtoRimSize`, `GeometryUtils`
- [Sensor Calibration](architecture/processing.md#sensor-calibration) — `ISensorConfiguration` strategy pattern, polymorphic JSON, four implementations

---

## UI Architecture

The presentation layer is layered `Views → ViewModels → Coordinators / Stores / Read Graphs / Queries → Services → Platform`. Stores own shared read state (read interface for VMs, writer interface for coordinators); read graphs publish joined reactive projections such as recorded-session staleness; coordinators own all workflows, store writes, post-save navigation, recompute, and sync arrival; queries answer command-side business questions; view models project state to bindings and route commands. ScottPlot rendering helpers live alongside the rest of the UI.

Topics in [architecture/ui.md](architecture/ui.md):

- [Architectural Invariants](architecture/ui.md#architectural-invariants) — what each role owns
- [Layered Architecture](architecture/ui.md#layered-architecture) — diagram and dependency rules
- [Threading & Lifecycle](architecture/ui.md#threading--lifecycle) — UI vs background ownership, `Loaded`/`Unloaded` discipline
  - [Cancellation & Result Coherence](architecture/ui.md#cancellation--result-coherence) — cancellation as neutral exit, stale-result guards
- [Stores](architecture/ui.md#stores) — `BikeStore`, `SetupStore`, `SessionStore`, `RecordedSessionSourceStore`, `PairedDeviceStore`; snapshot model and conflict baseline
- [Recorded Session Graph](architecture/ui.md#recorded-session-graph) — `IRecordedSessionGraph`, summaries, domain snapshots, staleness, and recompute inputs
- [Coordinators](architecture/ui.md#coordinators) — entity, shell, sync, pairing, import, inbound-sync coordinators; eager-resolution rules
- [Result Shapes](architecture/ui.md#result-shapes) — sealed `Saved`/`Conflict`/`Failed` records and similar service outcomes
- [Queries](architecture/ui.md#queries) — dependency, known-board, and recorded-session domain query patterns
- [View Models](architecture/ui.md#view-models) — shell / page / list / row / editor categories, `TabPageViewModelBase`, `ViewModelBase`
- [Testing Boundaries](architecture/ui.md#testing-boundaries) — what each layer's tests assert
- [Dependency Injection](architecture/ui.md#dependency-injection) — `App.ServiceCollection`, shared vs platform registrations, eager resolution
- [Navigation](architecture/ui.md#navigation) — `IShellCoordinator`, mobile back-stack vs desktop tab model
- [Controls Library](architecture/ui.md#controls-library) — reusable controls in `Views/Controls/` and `DesktopViews/Controls/`
- [Data Visualization](architecture/ui.md#data-visualization) — `SufniPlot` / `TelemetryPlot` base classes
  - [Plot Hierarchy](architecture/ui.md#plot-hierarchy) — table of every concrete plot
  - [IMU Plot](architecture/ui.md#imu-plot) — per-location magnitude calculation
  - [Desktop vs Mobile](architecture/ui.md#desktop-vs-mobile) — extended desktop layouts vs stacked mobile views

---

## Plot Rendering

ScottPlot-based plot classes under `Sufni.App/Sufni.App/Plots/`, wrapped by Avalonia controls in `Views/Plots/` and `DesktopViews/Plots/`. Recorded plots inherit from `TelemetryPlot` and load full sample arrays once; live plots inherit from `LiveStreamingPlotBase` and apply incremental batches via ScottPlot's `DataStreamer`. `TelemetryDisplaySmoothing` and `TelemetryDisplayDownsampling` shape the displayed signal at load time.

Topics in [architecture/plot-rendering.md](architecture/plot-rendering.md):

- [Layering](architecture/plot-rendering.md#layering) — plot classes as adapters over ScottPlot, view ownership
- [Class Hierarchy](architecture/plot-rendering.md#class-hierarchy) — `SufniPlot` / `TelemetryPlot` / `LiveStreamingPlotBase`
- [Concrete Plots](architecture/plot-rendering.md#concrete-plots) — Travel / Velocity / Strokes / Balance / IMU / Leverage / Live families
- [Display-Time Pipeline](architecture/plot-rendering.md#display-time-pipeline) — downsampling, smoothing windows, mobile `MaximumDisplayHz`
- [Cross-Cutting Patterns](architecture/plot-rendering.md#cross-cutting-patterns) — axis rules, cursors, recorded vs live differences

---

## Maps & GPS Tracks

GPS records from V4 SST files or live captures are projected into a `Track` row on session save and rendered on a Mapsui-backed map alongside the recorded session view and the live-session media workspace. Generated tracks are persisted atomically with the processed session and recorded source. `TileLayerService` provides the tile source; `MapViewModel` owns map state; `IMapPreferences` persists the user's tile choice and view options.

Topics in [architecture/maps-and-tracks.md](architecture/maps-and-tracks.md):

- [Overview](architecture/maps-and-tracks.md#overview) — data path from `GpsRecord` to `Track` to map display
- [Track Model](architecture/maps-and-tracks.md#track-model) — `Track` entity and projection
- [Track Coordinator](architecture/maps-and-tracks.md#track-coordinator) — track lifecycle alongside session save
- [Tile Layer Service](architecture/maps-and-tracks.md#tile-layer-service) — provider integration
- [Map View Model](architecture/maps-and-tracks.md#map-view-model) — bound state, projected points
- [Mapsui Integration](architecture/maps-and-tracks.md#mapsui-integration) — view-side glue
- [Map Preferences](architecture/maps-and-tracks.md#map-preferences) — `IMapPreferences` facet
- [Where Maps Are Displayed](architecture/maps-and-tracks.md#where-maps-are-displayed) — recorded session and live-session media workspace

---

## Live DAQ Streaming

The live preview feature streams real-time telemetry from a connected DAQ over a framed TCP protocol. It owns the transport: discovery catalog, browse ownership, runtime-only store, per-identity shared stream, and the diagnostics tab. The feature activates only when the user selects the Live primary page; diagnostics and live-session tabs for the same DAQ attach to the shared stream through leases. The recording / capture / save side that turns a live stream into a recorded session with source data lives in [Live Session Recording](#live-session-recording).

```
mDNS announcement
  -> LiveDaqCatalogService (probe board ID)
    -> LiveDaqCoordinator.Reconcile (merge with known boards)
      -> LiveDaqStore.ReplaceAll
        -> DynamicData -> LiveDaqListViewModel -> UI

User selects row
  -> LiveDaqCoordinator.SelectAsync
    -> shell.OpenOrFocus<LiveDaqDetailViewModel>
      -> LiveDaqSharedStreamRegistry.GetOrCreate
        -> LiveDaqSharedStream.AcquireLease

Diagnostics tab attaches
  -> shared stream ensures LiveDaqClient.ConnectAsync + StartPreviewAsync
  -> receive loop parses frames -> shared stream fan-out
    -> LiveDaqSessionState.ApplyFrame
    -> DispatcherTimer tick -> CreateSnapshot -> UI binding

Last lease released
  -> shared stream disconnects and registry evicts it
```

Topics in [architecture/live-streaming.md](architecture/live-streaming.md):

- [Overview](architecture/live-streaming.md#overview) — feature scope, architecture diagram
- [Data Flow](architecture/live-streaming.md#data-flow) — discovery → list → diagnostics tab attach
- [Live Wire Protocol](architecture/live-streaming.md#live-wire-protocol) — 16-byte frame header, frame types, start handshake, result codes
- [Transport Layer](architecture/live-streaming.md#transport-layer) — protocol reader, client lifecycle, session state accumulator
- [Discovery & Catalog](architecture/live-streaming.md#discovery--catalog) — browse ownership, board-ID inspector, catalog service
- [Known-Board Query](architecture/live-streaming.md#known-board-query) — board + setup + bike enrichment
- [Runtime Store](architecture/live-streaming.md#runtime-store) — in-memory `LiveDaqStore`, no persistence
- [Coordinator](architecture/live-streaming.md#coordinator) — activate/deactivate, reconcile, tab routing
- [View Models](architecture/live-streaming.md#view-models) — list, row, diagnostics tab
- [Views](architecture/live-streaming.md#views) — desktop and shared/mobile axaml pairs
- [Design Decisions](architecture/live-streaming.md#design-decisions) — separation from import, per-identity shared stream, throttled UI, lease-based browse

---

## Live Session Recording

The recording / capture / save side of the Live DAQ feature. Once the user opens a live-session tab, `LiveSessionService` attaches to the shared transport (acquiring the configuration lock), accumulates raw frames into `AppendOnlyChunkBuffer` for save and a `SlidingWindowBuffer` for display, fans graph batches through `LiveGraphPipeline`, and surfaces statistics. On save, `SessionCoordinator.SaveLiveCaptureAsync` materializes a processed `Session` row, a live-capture recorded source, a processing fingerprint, and an optional generated `Track` from captured GPS.

Topics in [architecture/live-session.md](architecture/live-session.md):

- [Overview](architecture/live-session.md#overview) — recording slice, relationship to live preview
- [Data Flow](architecture/live-session.md#data-flow) — attach → capture → save sequence
- [Configuration Lock](architecture/live-session.md#configuration-lock) — exclusive control of stream parameters
- [Capture Service](architecture/live-session.md#capture-service) — `LiveSessionService` lifecycle and frame handlers
- [Buffers](architecture/live-session.md#buffers) — `AppendOnlyChunkBuffer` (save) and `SlidingWindowBuffer` (display)
- [Live Graph Pipeline](architecture/live-session.md#live-graph-pipeline) — `ILiveGraphPipeline`, `LiveGraphPipelineFactory`, per-row batches
- [Stream Configuration](architecture/live-session.md#stream-configuration) — `LiveDaqStreamConfiguration` knobs
- [Presentation Records](architecture/live-session.md#presentation-records) — `LiveSessionPresentation`, `LiveSessionControlState`
- [Live Session Detail View Model](architecture/live-session.md#live-session-detail-view-model) — tab lifecycle and preferences forwarding
- [GPS Preview State](architecture/live-session.md#gps-preview-state) — fix-mode interpretation, dual consumer
- [Save Flow](architecture/live-session.md#save-flow) — `SessionCoordinator.SaveLiveCaptureAsync` integration
- [Design Decisions](architecture/live-session.md#design-decisions) — separation from streaming, lock model, throttling

---

## Persistence & Serialization

SQLite via `sqlite-net-pcl` with WAL mode. All sync-enabled entities inherit from `Synchronizable`, carrying `Updated` / `ClientUpdated` / `Deleted` timestamps for soft delete and conflict resolution. Session metadata, processed telemetry, processing fingerprints, generated tracks, and raw recorded sources have distinct persistence paths; processed-session writes that derive data from a source use one transaction for the session, optional generated track, and source row.

Topics in [architecture/persistence.md](architecture/persistence.md):

- [Schema](architecture/persistence.md#schema) — ER diagram for `session`, `bike`, `setup`, `board`, `track`, `session_recording_source`, `session_cache`, `sync`, `paired_device`
- [Database Service](architecture/persistence.md#database-service) — generic `Synchronizable` operations, processed-session transactions, session blob ops, and recorded-source ops
- [Soft Delete](architecture/persistence.md#soft-delete) — `Deleted` timestamp, 1-day purge window, expired-pair cleanup
- [Conflict Resolution](architecture/persistence.md#conflict-resolution) — `MergeAsync<T>()` rules: new / remote-delete / local-wins / remote-wins

---

## Cross-Device Synchronization

A desktop instance can host an embedded ASP.NET Core / Kestrel server over TLS with JWT auth, advertised via mDNS. Mobile clients pair with a 6-digit PIN, then push and pull entity changes, processed telemetry blobs, and recorded-source payloads. `SyncCoordinator` is the application-layer entry point; `HttpApiService` handles JWT auto-refresh.

Topics in [architecture/sync.md](architecture/sync.md):

- [Pairing Flow](architecture/sync.md#pairing-flow) — request → confirm → refresh sequence
- [Server](architecture/sync.md#server) — `SynchronizationServerService`, TLS / JWT / mDNS setup, full endpoint table
- [Client](architecture/sync.md#client) — `SynchronizationClientService` metadata/blob/source sync phases, `SyncCoordinator`, `HttpApiService` token refresh, `SynchronizationData` payload
