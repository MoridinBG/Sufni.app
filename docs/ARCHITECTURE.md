# Architecture

Sufni.App is a cross-platform application for analyzing mountain bike suspension telemetry. It acquires raw sensor data from a Pico-based DAQ device (via USB or WiFi), processes it through a signal analysis pipeline, and presents interactive plots for tuning suspension spring rates and damping settings. It also models bike linkage kinematics to compute leverage ratios and related characteristics. The app runs on Windows, macOS, Linux, Android, and iOS using Avalonia UI, and supports desktop <-> mobile synchronization.

This document is the catalog. Each subsystem is summarized here and the deep details live in `architecture/`.

## Adoption status

The [Architecture Adoption Plan](../ARCHITECTURE-ADOPTION.md) is complete as of
2026-11-03, including library immutability, ownership cleanup, typed failure
boundaries, parallel test isolation and platform validation. The manifest
exceptions explicitly accepted in the plan's section 5 remain; the subsystem
documents describe the implemented state.

---

## Table of Contents

- [Project Structure](#project-structure)
- [Platform Abstractions](#platform-abstractions)
- [Platform Capabilities And Layout Profiles](#platform-capabilities-and-layout-profiles)
- [Data Acquisition & File Format](#data-acquisition--file-format)
- [DAQ Management](#daq-management)
- [Signal Processing & Suspension Kinematics](#signal-processing--suspension-kinematics)
- [UI Architecture](#ui-architecture)
- [Plot Rendering](#plot-rendering)
- [Maps & GPS Tracks](#maps--gps-tracks)
- [Session Video](#session-video)
- [Saved Segments](#saved-segments)
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
| **Sufni.App**             | `Sufni.App/Sufni.App/`         | Neutral shared application layer, organized **domain-slice-first** (see below): each slice keeps its own views, view models, coordinators, stores, queries, services, models, and plots |
| **Sufni.App.Desktop**     | `Sufni.App/Sufni.App.Desktop/` | Desktop-only layer: sync server, ASP.NET Core hosting, inbound desktop sync infrastructure                   |
| **Sufni.App.Video.LibVlc** | `Sufni.App/Sufni.App.Video.LibVlc/` | LibVLC video player for the Windows and Linux heads |
| **Sufni.App.Windows**     | `Sufni.App/Sufni.App.Windows/` | Windows entry point (`Program.cs`)                                                                           |
| **Sufni.App.macOS**       | `Sufni.App/Sufni.App.macOS/`   | macOS entry point (`Program.cs`)                                                                             |
| **Sufni.App.Linux**       | `Sufni.App/Sufni.App.Linux/`   | Linux entry point (`Program.cs`)                                                                             |
| **Sufni.App.Android**     | `Sufni.App/Sufni.App.Android/` | Android entry point (`MainApplication.cs` / `MainActivity.cs`)                                                |
| **Sufni.App.iOS**         | `Sufni.App/Sufni.App.iOS/`     | iOS entry point (`Main.cs`) and application delegate (`AppDelegate.cs`)                                      |

Scenario-specific solutions live at the repository root:

- `Sufni.App.sln` — full matrix / repo-wide solution
- `Sufni.Desktop.sln` — desktop workflow solution
- `Sufni.Android.sln` — Android workflow solution
- `Sufni.iOS.sln` — iOS workflow solution

### Domain-slice layout (`Sufni.App/Sufni.App/`)

The shared app is organized **domain-slice-first**: top-level folders are feature/domain
slices, and the technical layers (`ViewModels/`, `Views/`, `DesktopViews/`, `Coordinators/`,
`Stores/`, `Queries/`, `Services/`, `Models/`, `Plots/`) live *inside* each slice. The
layering rules apply across slices in the same way.

| Slice | Folder | Covers |
| --- | --- | --- |
| **Bikes** | `Bikes/` | Bike entity: list/editor VMs+views, store, coordinator, linkage/image editing, telemetry-bike factory |
| **Setups** | `Setups/` | Setup entity + `Models/SensorConfigurations/` calibration strategies, editor workflow |
| **Sessions** | `Sessions/` | Recorded sessions: store, coordinators, processing (`Processing/RecordedSessionProjection`, `Processing/SessionDetails`), Telemetry Analysis, detail/signals/media pages, lists, plots |
| **LiveDaq** | `LiveDaq/` | Live DAQ streaming: `Services/LiveStreaming`, `Services/Imu`, stores, editor/page VMs+views, live plots |
| **Acquisition** | `Acquisition/` | Telemetry import + DAQ management (`Services/Management`), data-store models, import UI |
| **SyncAndPairing** | `SyncAndPairing/` | Client sync + pairing coordinators/services/VMs (desktop sync server stays in `Sufni.App.Desktop`) |
| **MapsAndTracks** | `MapsAndTracks/` | Map view + interaction/viewport controllers, track geometry, track signal plot |
| **Video** | `Video/` | Session video: clip links, store, import/alignment coordinator, video pane and playback, player contracts |
| **Segments** | `Segments/` | Saved segments of recorded sessions: model, repository, store, coordinator, segments picker, Strava segment efforts |
| **Shell** | `Shell/` | App shell: `ShellRootViewModel`, `ShellWorkspaceViewModel`, `MainPagesViewModel`, shell coordinator, profile shell views, navigation behaviors, keyboard shortcuts, installation preferences and shell-dependent list/editor bases |
| **Shared** | `Shared/` | View/store bases (`Base/`), framing (`IO/`), common helpers, formatting, shared plot bases, shared `Views/` + `DesktopViews/` controls/dialogs/overlays/converters |
| **Infrastructure** | `Infrastructure/` | Infra services, theme model and bridge (`Theming/`), and platform-service abstractions (`IServiceDiscovery`, `ISecureStorage`, `IHapticFeedback`, `IFriendlyNameProvider`) |

`App.axaml(.cs)`, `AppComposition.cs`, the schema composition `DatabaseMigrationRunner.cs`,
the application serialization catalog `AppJson.cs`,
`ViewLocator.cs`, and `MobileAppBootstrapper.cs` stay at the project root.

---

## Platform Abstractions

| Interface               | File                                                    | Purpose                                                                 | Implementations                                                                                                |
| ----------------------- | ------------------------------------------------------- | ----------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------- |
| `IServiceDiscovery`     | `Sufni.App/Sufni.App/Infrastructure/IServiceDiscovery.cs`     | mDNS browse for `_sufni._tcp` DAQ announcements and `_sstsync._tcp` sync | `SocketServiceDiscovery` (shared / Win / Linux), `AndroidServiceDiscovery` (Android), `BonjourServiceDiscovery` (macOS / iOS) |
| `ISecureStorage`        | `Sufni.App/Sufni.App/Infrastructure/ISecureStorage.cs`        | Encrypted key-value store for JWT secrets, certificates, refresh tokens | `WindowsSecureStorage`, `LinuxSecureStorage`, `MacOsSecureStorage`, `AndroidSecureStorage`, `IosSecureStorage` |
| `IHapticFeedback`       | `Sufni.App/Sufni.App/Infrastructure/IHapticFeedback.cs`       | Tactile feedback: `LongPress()`                              | `AndroidHapticFeedback`, `IosHapticFeedback`                                                                   |
| `IFriendlyNameProvider` | `Sufni.App/Sufni.App/Infrastructure/IFriendlyNameProvider.cs` | Human-readable device name for sync identification                      | `AndroidFriendlyNameProvider`, `IosFriendlyNameProvider`                                                       |
| `IVideoPlayerFactory`   | `Sufni.App/Sufni.App/Video/Models/VideoPlayback.cs`           | Native video player for the session video pane                          | `LibVlcVideoPlayerFactory` (Win / Linux), `MacOsVideoPlayerFactory`, `IosVideoPlayerFactory`                     |

DAQ browsing resolves DNS address candidates without opening TCP; connections
try those candidates only when a workflow acquires the device port. Sync-peer
browsing retains its reachability selection. See
[live discovery](architecture/live-streaming.md#discovery--catalog).

Each platform entry point passes its registrations, capabilities, launch
preferences and `IAppPaths` through `PlatformRoot`. `AppComposition` constructs
the shared graph; `App` builds it with validation enabled. `AppPaths` derives the
database, preferences, logs and certificate paths from the head-selected app-data
directory.

---

## Platform Capabilities And Layout Profiles

A cross-cutting reference for the divergence points between platform heads and layout profiles: shared `Compact` / `Workspace` shell presentation, profile-based `ViewLocator` selection, platform capabilities, desktop-capable sync server / inbound coordinators, mobile-capable pairing client and haptic / friendly-name services, DI composition order, solution scoping, and behavioral differences across feature workflows.

Topics in [architecture/desktop-vs-mobile.md](architecture/desktop-vs-mobile.md):

- [Project Layout](architecture/desktop-vs-mobile.md#project-layout) — shared `Sufni.App` and desktop-only `Sufni.App.Desktop`
- [Layout Profiles And View Selection](architecture/desktop-vs-mobile.md#layout-profiles-and-view-selection) — `Compact` / `Workspace` and profile-based `ViewLocator`
- [Platform Capabilities](architecture/desktop-vs-mobile.md#platform-capabilities) — `IAppEnvironment`, `AppCapabilities`, and `InputCapabilities`
- [Shared Navigation Shell](architecture/desktop-vs-mobile.md#shared-navigation-shell) — `ShellWorkspaceCoordinator` and profile-specific shell views
- [Desktop-Capable Surface](architecture/desktop-vs-mobile.md#desktop-capable-surface) — sync server, inbound sync, pairing server
- [Mobile-Capable Surface](architecture/desktop-vs-mobile.md#mobile-capable-surface) — pairing client, haptics, friendly-name
- [DI Composition](architecture/desktop-vs-mobile.md#di-composition) — typed platform roots, launch preferences and validated registrations
- [Solution Scoping](architecture/desktop-vs-mobile.md#solution-scoping) — `Sufni.Desktop.sln` / `Sufni.Android.sln` / `Sufni.iOS.sln`
- [Testing](architecture/desktop-vs-mobile.md#testing) — explicit profile/capability setup
- [Behavioral Differences](architecture/desktop-vs-mobile.md#behavioral-differences) — workflow-level divergences

---

## Data Acquisition & File Format

Telemetry reaches the app through three `ITelemetryDataStore` implementations behind a single interface, and SST files come in three binary versions (V3 fixed-record, V4 TLV with IMU/GPS/markers/temperature, V5 chunked/segment-aware recordings). Import captures the original SST bytes as a recorded-session source, then derives processed telemetry and a processing fingerprint from that source plus the selected setup/bike calibration.

`ImportSessionsCoordinator` owns browsing, listing, folder selection and the import batch. Immutable jobs capture row edits; repository checkpoints prevent duplicate imports and recover publication before acknowledgement. Network sources observe the shared live catalog and reserve the DAQ port for listing or import. Local adapters share `DaqDriveLayout` and retain malformed recordings as non-importable rows.

Topics in [architecture/acquisition.md](architecture/acquisition.md):

- [Data Acquisition](architecture/acquisition.md#data-acquisition) — overview and source-flow diagram
  - [Interfaces](architecture/acquisition.md#interfaces) — `ITelemetryDataStore`, `ITelemetryFile`, `ITelemetryDataStoreService`
  - [Import Screen Boundaries](architecture/acquisition.md#import-screen-boundaries) — canonical worked example of the boundary rules
  - [Mass Storage](architecture/acquisition.md#mass-storage) — `BOARDID` marker, drive probing, background lifecycle
  - [Network (WiFi DAQ)](architecture/acquisition.md#network-wifi-daq) — mDNS discovery and the SST TCP wire protocol
  - [Storage Provider](architecture/acquisition.md#storage-provider) — Avalonia picker integration and duplicate detection
- [File Format & Parsing](architecture/acquisition.md#file-format--parsing) — version dispatch
  - [SST V3 Format](architecture/acquisition.md#sst-v3-format) — legacy fixed-record layout
  - [SST V4 TLV Format](architecture/acquisition.md#sst-v4-tlv-format) — TLV chunks (Rates, Telemetry, Marker, IMU, IMU Meta, GPS, Temperature)
  - [SST V5 Chunked Format](architecture/acquisition.md#sst-v5-chunked-format) — stream/source descriptors, fixed-rate segments, gaps, and final status/backlog metadata
  - [Spike Elimination](architecture/acquisition.md#spike-elimination) — four-stage anomaly correction
  - [Parsed Telemetry Data Structures](architecture/acquisition.md#parsed-telemetry-data-structures) — `GpsRecord`, `ImuRecord`, `ImuMetaEntry`, `RawImuData`, `RawStreamGap`, `SstFinalStatus`, `MarkerData`, `TemperatureSample`, `TemperatureAverage`

---

## DAQ Management

A side-channel framed TCP protocol for non-streaming DAQ operations: list and retrieve files, mark uploaded, trash remotely, set time, edit and replace CONFIG. Network imports use this protocol for file transfer. It is distinct from live preview but shares the DAQ's single-client TCP port. The network telemetry store, the import workflow and the `LiveDaqCoordinator` diagnostics workflow use it.

Topics in [architecture/daq-management.md](architecture/daq-management.md):

- [Service Surface](architecture/daq-management.md#service-surface) — `IDaqManagementService`, `IDaqManagementSession`, `DaqManagementException`
- [Wire Protocol](architecture/daq-management.md#wire-protocol) — frame header, frame types, result codes, per-operation choreography
- [Callers](architecture/daq-management.md#callers) — `NetworkTelemetryDataStore`, `NetworkTelemetryFile`, `LiveDaqCoordinator`
- [CONFIG Document](architecture/daq-management.md#config-document) — `DaqConfigDocument`, `DaqConfigFields`, `DaqConfigValidator`, `SelectedDeviceConfigFile`

---

## Signal Processing & Suspension Kinematics

Processing classifies absent channels from calibration and whole-channel sentinel
content, preserves the usable side, and raises typed `TelemetryProcessingException`
failures for unprocessable data. Channel presence is independent of stroke activity.
Recorded derivation takes one captured input and stamps fingerprint schema v4,
including the independent app calibration version, from the inputs it processes.
The velocity-filter option is a nullable session column, copied once from legacy
preferences at startup and changed through the session coordinator.

`TelemetryData.FromRecording()` orchestrates the full pipeline: measurement preprocessing → travel calibration → configurable Savitzky-Golay velocity → stroke detection → categorization → airtime detection → histogram bin definitions. The `Sufni.Kinematics` library independently solves bike linkage geometry to derive leverage ratios. Sensor calibration maps between raw ADC counts and millimeters of travel. Recorded sessions persist the raw source separately from the derived `TelemetryData` BLOB so stale processed data can be recomputed when the source and dependencies are available; an optional derivation window on the session row lets a trimmed or split session derive from a source-absolute slice of another session's raw source.

Topics in [architecture/processing.md](architecture/processing.md):

- [Signal Processing Pipeline](architecture/processing.md#signal-processing-pipeline) — pipeline overview
  - [Travel Calculation](architecture/processing.md#travel-calculation) — sensor-driven mm conversion
  - [Velocity Calculation](architecture/processing.md#velocity-calculation) — Savitzky-Golay smoothed first derivative
  - [Stroke Detection](architecture/processing.md#stroke-detection) — sign changes with top-out concatenation
  - [Stroke Categorization](architecture/processing.md#stroke-categorization) — compression / rebound / idling thresholds
  - [Airtime Detection](architecture/processing.md#airtime-detection) — front/rear overlap heuristics
  - [Processing Parameters](architecture/processing.md#processing-parameters) — stroke/histogram defaults and the owners of other tunables
  - [Serialized Structure](architecture/processing.md#serialized-structure) — MessagePack `TelemetryData` shape
  - [Recorded Session Derivation](architecture/processing.md#recorded-session-derivation) — raw source, optional derivation window, processed BLOB, processing fingerprint, and staleness rules
  - [Shared Caching](architecture/processing.md#shared-caching) — `SingleFlightLruCache` for bounded reusable computations
- [Suspension Kinematics](architecture/processing.md#suspension-kinematics)
  - [Linkage Model](architecture/processing.md#linkage-model) — immutable specs, resolver validation, runtime linkage state
  - [Kinematic Solver](architecture/processing.md#kinematic-solver) — Gauss-Seidel constraint relaxation across shock travel
  - [Bike Characteristics](architecture/processing.md#bike-characteristics) — derived travel limits and leverage ratio
  - [Utilities](architecture/processing.md#utilities) — `CoordinateRotation`, `GroundCalculator`, `EtrtoRimSize`, `GeometryUtils`
- [Sensor Calibration](architecture/processing.md#sensor-calibration) — `ISensorConfiguration` strategy pattern, polymorphic JSON, four implementations

---

## UI Architecture

`ShellFeedbackViewModel` owns global sync, GPX and video feedback independently
of the selected page, including bulk recompute outcomes from `SessionCoordinator`. Both layout profiles render the same feedback bar, and
one shell dispatcher handler sends unexpected UI failures there. List view models
own their menus and session import intent; feature views do not reach into a
parent shell to invoke import.

`AppStartup` gates page creation on schema initialization and the first store
refresh, then starts the capable head's sync server.
Both shells render structural failures as a blocking screen with a log location
and database reset. Reset archives the closed database and creates a new
incarnation through normal migrations; continuing requires relaunch. Startup
owns and cancels its post-readiness retention work.
`AppShutdown` owns close prompts, work cancellation/drain, pending-delete
finalization, server stop and provider disposal. Android pause and iOS
background finalize pending deletes and flush logs while preserving tabs.

The presentation layer is layered `Views → ViewModels → Coordinators / Stores / Queries → Services → Platform`. Classes hold the five roles of the [manifest](ARCHITECTURE-MANIFEST.md), whatever their names ([role holders](architecture/ui.md#architectural-invariants)):

- Stores own shared read state and cache mutation, with no repository dependencies.
- Coordinators own workflows and long-lived event subscriptions. They persist data, and only they hold store writers. Besides the `*Coordinator` classes, the role covers `SessionRecomputeEngine`, `SessionSyncApplier`, `SynchronizationApplier`, `AppDataRefresher` and `RecordedSessionSourceRetentionCleanup`.
- Queries answer business questions and publish joined projections, such as recorded-session staleness and session-list summaries.
- View models depend on read-only store interfaces, project state to bindings and route commands. The shell owns navigation and editor creation.
- Services reach the world. Transaction runners are persistence services: they commit cross-aggregate SQL and do not publish.

ScottPlot rendering helpers live alongside the rest of the UI. App preferences store per-session signal row hierarchy and expanded state, signal root-row height ratios, and desktop session-detail pane ratios.

Recorded-session editors reduce each accepted input into one coherent state.
Workspaces read that state, and the coordinator owns staleness choices, preference
persistence and processing-option writes. Metadata saves compare authored draft
fields to their saved base. Readiness, highlight ranges and plot statistics run
as keyed background analysis; the UI only renders accepted results.

Bike and setup editors also retain one authored draft base; their coordinators
validate, build rows and enforce board/delete rules transactionally. File imports
are list actions that open new tabs. Bike plot analysis is keyed by the suspension
spec and debounced, while shared bike snapshots retain only image presence.
Editors load image-bearing drafts on demand, decode in background work and dispose
bitmaps on close. Pointer capability gates the awaited suspension-mode command.

Presentation-layer topics:

- [Architectural Invariants](architecture/ui.md#architectural-invariants) — what each role owns
- [Layered Architecture](architecture/ui.md#layered-architecture) — diagram and dependency rules
- [Threading & Lifecycle](architecture/ui.md#threading--lifecycle) — UI vs background ownership, `Loaded`/`Unloaded` discipline
  - [Cancellation & Result Coherence](architecture/ui.md#cancellation--result-coherence) — cancellation as neutral exit, stale-result guards
- [Result Shapes](architecture/ui.md#result-shapes) — sealed `Saved`/`Conflict`/`Failed` records and similar service outcomes
- [Testing Boundaries](architecture/ui.md#testing-boundaries) — what each layer's tests assert
- [Stores](architecture/ui-state.md#stores) — `BikeStore`, `SetupStore`, `SessionStore`, `RecordedSessionSourceStore`, `PairedDeviceStore`; snapshot model and conflict baseline
- [Recorded Session Projection](architecture/ui-state.md#recorded-session-projection) — `IRecordedSessionProjection`, summaries, domain snapshots, staleness, and recompute inputs
- [Queries](architecture/ui-state.md#queries) — dependency, known-board, and recorded-session domain query patterns
- [Coordinators](architecture/ui-workflows.md#coordinators) — entity, shell, sync, pairing, import, inbound-sync coordinators; eager-resolution rules
- [Trim and split](architecture/ui-workflows.md#trim-and-split) — recorded-session window edits, their transactions, and the recompute that follows
- [Dependency Injection](architecture/ui-workflows.md#dependency-injection) — `PlatformRoot`, `AppComposition`, shared vs platform registrations, eager resolution
- [Navigation](architecture/ui-workflows.md#navigation) — `IShellCoordinator`, shared workspace tabs, compact/workspace shell presentation
- [View Models](architecture/ui-view-models.md#view-models) — shell / page / list / row / editor categories, `TabPageViewModelBase`, `ViewModelBase`
  - [Session Sub-Pages](architecture/ui-view-models.md#session-sub-pages) — recorded/live session tab composition and signal layout preferences
- [Controls Library](architecture/controls.md#controls-library) — reusable controls in `Shared/Views/Controls/` and `Shared/DesktopViews/Controls/`
- [Theming](architecture/theming.md#theming) — theme snapshots, resource bridge, runtime theme service, and theme ownership
- [Plot Rendering](architecture/plot-rendering.md) — `SufniPlot` / `TelemetryPlot`, IMU display, shared plot hosting and input gestures

---

## Plot Rendering

ScottPlot-based plot classes (shared bases in `Shared/Plots/`, concrete plots in each slice's `Plots/` folder), wrapped by Avalonia plot views in the slices' `Views/Plots/` folders (e.g. `Shared/Views/Plots/`, `Sessions/Plots/Views/Plots/`). Recorded telemetry plots inherit from `TelemetryPlot`, recorded time-series rows add `RecordedTimeSeriesPlot`, live plots derive through `LiveStreamingPlotBase` and apply incremental batches via ScottPlot's `DataStreamer`, and GPS speed/elevation rows use `TrackSignalPlot` over `TrackPoint` data. Segment comparison series can carry segmented runs for real data gaps; every run contributes to value limits, and unsupported value forms fail loudly. The position/velocity analysis plot owns deterministic disposal of its native stroke paths when reloaded or detached. `TelemetryDisplaySmoothing` and `TelemetryDisplayDownsampling` shape the displayed signal at load time. Signal row titles, drag/drop hierarchy changes, and root/child row plot backgrounds are owned by the Avalonia row controls, while ScottPlot keeps axes, legends, data rendering, readouts, and overlays.

Topics in [architecture/plot-rendering.md](architecture/plot-rendering.md):

- [Layering](architecture/plot-rendering.md#layering) — plot classes as adapters over ScottPlot, view ownership
- [Shared Hosting And Input](architecture/plot-rendering.md#shared-hosting-and-input) — shared plot controls with capability-driven gestures
- [Class Hierarchy](architecture/plot-rendering.md#class-hierarchy) — `SufniPlot` / `TelemetryPlot` / `LiveStreamingPlotBase`
- [Concrete Plots](architecture/plot-rendering.md#concrete-plots) — Travel / Velocity / Strokes / Balance / IMU / Leverage / Live families
- [Display-Time Pipeline](architecture/plot-rendering.md#display-time-pipeline) — downsampling, smoothing windows, mobile `MaximumDisplayHz`
- [Cross-Cutting Patterns](architecture/plot-rendering.md#cross-cutting-patterns) — axis rules, cursors, recorded vs live differences

---

## Maps & GPS Tracks

GPS records from V4 SST files or live captures are projected into a `Track` row on session save. A Mapsui-backed map renders them beside the recorded session view and the live-session media workspace.

- Generated tracks and imported GPX tracks share content-derived IDs. Generated tracks are persisted or reused atomically with the processed session and recorded source.
- `TrackCoordinator` derives windowed GPS metrics for the grouped session list. These values are device-local, keyed by content/window inputs, and recomputed from committed session/track changes.
- `TrackStore` holds content IDs, fractional bounds and points revisions without point payloads. Startup and local writing workflows populate it.
- Sessions have no stored full-track link. Their domain projection, map and comparison reads select the greatest overlapping track (tie lowest ID) and derive the session window from exact full-track points. The cache key includes all window inputs and the points revision; exact reads fail closed instead of advancing to newer content.
- `MapLayerCoordinator` owns map-layer writes. `TileLayerService` derives a catalog/selection snapshot from `CustomTileLayerStore` and device-local selection for `MapViewModel`. Custom definitions sync as ordinary rows; preferences stay local.
- Attached map views own native map/layer disposal and cancellable background geometry preparation. Timeline interactions consume the prepared geometry.

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

## Session Video

A local video file is linked once per recording source. Each session over that source derives visible clips from its window, with one shared source-relative alignment per file; trim, split and session deletion leave links intact. New links wait for source identity to arrive. The recorded-session editor plays the clip in a video pane beside the map, in sync with the telemetry cursor, and a context-menu flow aligns the clip to the telemetry. Heads with `AppCapabilities.CanPlayVideo` register a native player. Shared loading owns cancellable file-access leases and typed playback failures; iOS persists and restores security-scoped bookmarks. File presence updates independently of editor loads.

Topics in [architecture/video.md](architecture/video.md):

- [Persistence](architecture/video.md#persistence) — `session_video` table and clip liveness
- [Store And Coordinator](architecture/video.md#store-and-coordinator) — `SessionVideoStore`, `VideoCoordinator`
- [Import](architecture/video.md#import) — "Open video" action and overlap matching
- [Clip Selection](architecture/video.md#clip-selection) — which clip shows at a telemetry time
- [Video Pane](architecture/video.md#video-pane) — editor-lifetime pane, view-owned native player
- [Playback And Timeline](architecture/video.md#playback-and-timeline) — sync and timeline playback
- [Alignment](architecture/video.md#alignment) — mark video event, mark telemetry event
- [Session List Indicator](architecture/video.md#session-list-indicator) — present/missing row icon
- [Platform Players](architecture/video.md#platform-players) — LibVLC and AVFoundation players

---

## Saved Segments

A saved segment is a synchronized named range on a recording source, keyed by its content hash. Source-relative anchors stay fixed through trim and split; each session derives visible segments from its window. Session deletion retains attachments, and new creation waits until source identity is available. The segments picker of the signals page lists, selects, renames, and deletes them, and shows Strava segment efforts when the user connected their own Strava API app.

Topics in [architecture/segments.md](architecture/segments.md):

- [Persistence](architecture/segments.md#persistence) — `session_segment` table and startup cleanup
- [Store And Coordinator](architecture/segments.md#store-and-coordinator) — `SessionSegmentStore`, `SegmentCoordinator`, name rule
- [Synchronization](architecture/segments.md#synchronization) — segment list in the sync payload
- [Trim And Split](architecture/segments.md#trim-and-split) — how windows select source-owned segments
- [Segments Picker](architecture/segments.md#segments-picker) — per-editor picker, "Save current selection", name prompt
- [Strava](architecture/segments.md#strava) — `IStravaSegmentService` behind `ISegmentCoordinator`, connect dialog, cancellation on close

---

## Live DAQ Streaming

The live preview feature streams real-time telemetry from a connected DAQ over framed TCP protocols. It owns the transport: discovery catalog, browse ownership, runtime-only store, per-identity shared stream, protocol-specific v2/v3 clients behind `ILiveDaqClientFactory`, and the diagnostics tab.

- The shared stream owns one convergent lifecycle operation per identity. Caller cancellation stops only that caller's wait. Client generations reject stale events and completions from replaced transports.
- Stop cleanup rechecks the desired state, so a concurrent restart continues on a fresh binding rather than publishing a terminal disconnect.
- V2 start results publish the ACK's accepted stream-family mask as the canonical mask after matching the session header id. They retain transport session identity after direct waiter cancellation, and stop acknowledgment completes a pending start before clearing it.
- V3 connections validate the server hello against the discovered board ID when one is available. They then use descriptor-based SST v5 stream metadata for telemetry and final-status reporting.

The feature activates only when the user selects the Live primary page. Diagnostics and live-session tabs for the same DAQ attach to the shared stream through leases. The recording / capture / save side lives in [Live Session Recording](#live-session-recording).

```
mDNS announcement
  -> LiveDaqCatalogService (requires TXT live_proto=2|3, reads TXT bid when present)
    -> LiveDaqCoordinator.Reconcile (merge with known boards)
      -> LiveDaqStore.ReplaceAll
        -> DynamicData -> LiveDaqListViewModel -> UI

User selects row
  -> shell.OpenEditor(EditorKey.LiveDaq)
    -> deduplicate open tabs
      -> EditorFactory.CreateEditor(key, shell)
        -> shared stream lookup and diagnostics editor creation

Diagnostics tab attaches
  -> shared stream uses ILiveDaqClientFactory for LiveDaqV2Client or LiveDaqV3Client
  -> client ConnectAsync + StartPreviewAsync
  -> receive loop handles control frames and queues telemetry
    -> parse loop -> publish loop -> shared stream fan-out
      -> LiveDaqSessionState.ApplyFrame
      -> IUiTimerScheduler tick -> CreateSnapshot -> UI binding

Last lease released
  -> shared stream disconnects and registry evicts it
```

Topics in [architecture/live-streaming.md](architecture/live-streaming.md):

- [Overview](architecture/live-streaming.md#overview) — feature scope, architecture diagram
- [Data Flow](architecture/live-streaming.md#data-flow) — discovery → list → diagnostics tab attach
- [Live Wire Protocol](architecture/live-streaming.md#live-wire-protocol) — v2 and v3 wire shapes mapped into canonical live frames
- [Transport Layer](architecture/live-streaming.md#transport-layer) — protocol readers, client factory, client lifecycle, session state accumulator
- [Discovery & Catalog](architecture/live-streaming.md#discovery--catalog) — browse ownership, `_sufni._tcp`, TXT protocol and board identity, catalog service
- [Known-Board Query](architecture/live-streaming.md#known-board-query) — board + setup + bike enrichment
- [Runtime Store](architecture/live-streaming.md#runtime-store) — in-memory `LiveDaqStore`, no persistence
- [Coordinator](architecture/live-streaming.md#coordinator) — activate/deactivate and reconcile; shell tab routing
- [View Models](architecture/live-streaming.md#view-models) — list, row, diagnostics tab
- [Views](architecture/live-streaming.md#views) — desktop and shared/mobile axaml pairs
- [Design Decisions](architecture/live-streaming.md#design-decisions) — separation from import, per-identity shared stream, throttled UI, lease-based browse

---

## Live Session Recording

The recording / capture / save side of the Live DAQ feature. Once the user opens a live-session tab, `LiveSessionService` attaches to the shared transport (acquiring the configuration lock), accumulates fixed-rate travel/IMU samples into segment builders plus GPS into an append-only buffer, feeds duration-bounded display context through `LiveSignalPipeline`, and computes mode-specific analysis results off the UI thread. The pipeline carries those immutable results with display batches; live plots render them through the common analysis result-source contract. On save, `LiveDaqCoordinator.SaveCaptureAsync` creates a segment-aware live-capture recorded source, then delegates telemetry, generated-track, and processing-fingerprint derivation to `IRecordedSessionReprocessor` before persisting the processed `Session` row atomically with the source and optional generated `Track`.

Topics in [architecture/live-session.md](architecture/live-session.md):

- [Overview](architecture/live-session.md#overview) — recording slice, relationship to live preview
- [Data Flow](architecture/live-session.md#data-flow) — attach → capture → save sequence
- [Configuration Lock](architecture/live-session.md#configuration-lock) — exclusive control of stream parameters
- [Capture Service](architecture/live-session.md#capture-service) — `LiveSessionService` lifecycle and frame handlers
- [Capture Stores](architecture/live-session.md#capture-stores) — fixed-rate travel/IMU segments, GPS append buffer, and duration-bounded recent display context
- [Live Signal Pipeline](architecture/live-session.md#live-signal-pipeline) — `ILiveSignalPipeline`, `LiveSignalPipelineFactory`, per-row batches
- [Stream Configuration](architecture/live-session.md#stream-configuration) — `LiveDaqStreamConfiguration` knobs
- [Presentation Records](architecture/live-session.md#presentation-records) — `LiveSessionPresentationSnapshot`, `LiveSessionControlState`
- [Live Session Detail View Model](architecture/live-session.md#live-session-detail-view-model) — tab lifecycle and preferences forwarding
- [GPS Preview State](architecture/live-session.md#gps-preview-state) — diagnostics fix-mode interpretation; live maps use projected track points
- [Save Flow](architecture/live-session.md#save-flow) — `LiveDaqCoordinator.SaveCaptureAsync` integration
- [Design Decisions](architecture/live-session.md#design-decisions) — separation from streaming, lock model, throttling

---

## Persistence & Serialization

`Sufni.Telemetry.TelemetryCodec` owns processed MessagePack and live-capture
source JSON formats, with permanent legacy readers and frozen byte fixtures.

SQLite via the sqlite-net API (`sqlite-net-e` package) with WAL mode.

- All sync-enabled entities inherit from `Synchronizable`, carrying authored version/timestamp/writer metadata, a deletion marker and transport sequences. Hub and client apply one merge rule, and durable deletion ledger entries survive payload purge.
- Separate local-only revisions identify processed telemetry and full-track point payloads. Replaceable SQLite triggers advance them only on effective content changes. Revision-constrained readers return no value when the requested generation is gone.
- Metadata-only session saves do not name the processed BLOB. Session-window tracks are derived on read and have no persisted payload or revision.
- Session metadata includes derived `duration_seconds`. Device-local `session_gps_metrics` holds distance/ascent/descent and their complete input key.
- Processed telemetry, processing fingerprints, generated tracks and raw recorded sources have distinct persistence paths. Processed-session writes that derive data from a source use one transaction for the session, optional generated track and source row.

Topics in [architecture/persistence.md](architecture/persistence.md):

- [Schema](architecture/persistence.md#schema) — ER diagram for `session`, `bike`, `setup`, `board`, `track`, `session_recording_source`, `sync`, `paired_device`
- [SQLite Persistence Repositories](architecture/persistence.md#sqlite-persistence-repositories) — generic `Synchronizable` operations, processed-session transactions, session blob ops, and recorded-source ops
- [Soft Delete](architecture/persistence.md#soft-delete) — `Deleted` timestamp, 1-day purge window, expired-pair cleanup
- [Conflict Resolution](architecture/persistence.md#conflict-resolution) — one version/deletion/timestamp/writer ordering on both devices

---

## Cross-Device Synchronization

A desktop instance can host an embedded ASP.NET Core / Kestrel server over TLS with JWT auth, advertised via mDNS. Mobile clients pair with a 6-digit PIN, then push and pull entity changes, deletion ledger entries, processed telemetry blobs, and recorded-source payloads. `SyncCoordinator` owns explicit confirm-and-sync and publishes one replayed `SyncState` for shell and pairing surfaces; desktop row endpoints await `IInboundSyncCoordinator` for persistence and coherent publication across all synced stores. `SynchronizationPeer` owns JWT auto-refresh. Runs verify the hub incarnation and announce their phases; desktop progress follows the inbound coordinator’s run state and silence deadline.

Topics in [architecture/sync.md](architecture/sync.md):

- [Pairing Flow](architecture/sync.md#pairing-flow) — request → confirm → refresh sequence
- [Server](architecture/sync.md#server) — `SynchronizationServerService`, TLS / JWT / mDNS setup, full endpoint table
- [Client](architecture/sync.md#client) — `SyncCoordinator` metadata/blob/source phases and cursor writes, shared `SynchronizationApplier`, per-peer HTTP transport and token refresh, `SynchronizationData` payload
