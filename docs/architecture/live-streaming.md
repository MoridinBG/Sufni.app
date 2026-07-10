# Live DAQ Streaming

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers the live preview transport: the framed TCP protocol, discovery and catalog services, the runtime-only store, the per-identity shared stream, and the diagnostics tab. The recording / capture / save side that turns a live stream into a persisted recorded session lives in [Live Session Recording](live-session.md).

## Contents

- [Overview](#overview)
- [Data Flow](#data-flow)
- [Live Wire Protocol](#live-wire-protocol)
- [Transport Layer](#transport-layer)
- [Discovery & Catalog](#discovery--catalog)
- [Known-Board Query](#known-board-query)
- [Runtime Store](#runtime-store)
- [Coordinator](#coordinator)
- [View Models](#view-models)
- [Views](#views)
- [Design Decisions](#design-decisions)

## Overview

The live feature lets a user inspect a connected DAQ in a diagnostics tab and optionally open a separate live-session tab on top of the same connection. This file covers the side of the feature that delivers frames to those tabs: discovery, the per-identity shared transport, and the diagnostics tab itself. The recording / capture / save side — `ILiveSessionService`, analysis computation, the live signal pipeline, and the `Session` save path — lives in [Live Session Recording](live-session.md).

It exists as a dedicated feature slice: a primary page lists known and discovered DAQs, selection opens a diagnostics tab, and `Start Session` opens a second tab that subscribes to the same underlying transport. Both desktop and mobile heads expose the Live tab and the diagnostics/live-session tabs.

The feature is intentionally separate from the import pipeline. It does not reuse `ITelemetryDataStoreService.DataStores` for list state and does not share browse stop/start behavior with the import page. The diagnostics and live-session tabs share a per-identity transport through the live-streaming service layer. The diagnostics tab also exposes v2 management actions (Set Time, Edit CONFIG, and Replace Config) that run through a separate management service and only while the live client is disconnected.

```mermaid
graph LR
    subgraph Discovery
        mDNS["IServiceDiscovery<br/>(mDNS _sufni._tcp)"]
        Txt["TXT live_proto / bid"]
        Catalog["LiveDaqCatalogService"]
    end

    subgraph Application
        Query["LiveDaqKnownBoardsQuery"]
        Coord["LiveDaqCoordinator"]
        Store["LiveDaqStore<br/>(runtime-only)"]
    end

    subgraph Transport
      Registry["LiveDaqSharedStreamRegistry"]
      Shared["LiveDaqSharedStream<br/>(per identity)"]
      Factory["ILiveDaqClientFactory"]
      Client["LiveDaqV2Client / LiveDaqV3Client"]
        Reader["LiveV2ProtocolReader / LiveV3ProtocolReader"]
      Session["LiveDaqSessionState"]
    end

    subgraph Presentation
        ListVM["LiveDaqListViewModel"]
        RowVM["LiveDaqRowViewModel"]
        DetailVM["LiveDaqDetailViewModel"]
    end

    mDNS --> Catalog
    Txt --> Catalog
    Catalog --> Coord
    Query --> Coord
    Coord --> Store
    Store --> ListVM
    ListVM --> RowVM
    Coord -->|"OpenOrFocus"| DetailVM
    Coord -. "OpenSessionAsync" .-> LiveSession["live-session.md"]
    Registry --> Shared
    Factory --> Client
    Client --> Reader
    Shared --> Client
    Shared --> DetailVM
    Shared -. "frames + states" .-> LiveSession
    Session --> DetailVM
```

## Data Flow

```
mDNS announcement
  -> LiveDaqCatalogService (requires TXT live_proto=2|3, reads TXT bid when present)
    -> LiveDaqCoordinator.Reconcile (merge with known boards from query)
      -> LiveDaqStore.ReplaceAll
        -> DynamicData -> LiveDaqListViewModel -> LiveDaqRowViewModel -> UI

User selects row
  -> LiveDaqCoordinator.SelectAsync
    -> shell.OpenOrFocus<LiveDaqDetailViewModel>

Diagnostics tab loads
  -> LiveDaqSharedStream.AcquireLease()
    -> LiveDaqSharedStream.EnsureStartedAsync
      -> ILiveDaqClientFactory.Create(snapshot.ProtocolVersion)
      -> LiveDaqV2Client or LiveDaqV3Client ConnectAsync (TCP)
        -> StartPreviewAsync (protocol-specific start handshake)
          -> receive loop handles control frames and queues telemetry frames
            -> parse loop decodes telemetry frames into canonical LiveProtocolFrame records
              -> publish loop emits LiveDaqClientEvent frames
            -> LiveDaqSharedStream state/frames
              -> LiveDaqSessionState.ApplyFrame
                -> DispatcherTimer tick -> CreateSnapshot -> UI binding

User presses Start Session
  -> LiveDaqCoordinator.OpenSessionAsync
    -> shell.OpenOrFocus<LiveSessionDetailViewModel>
      -> handed off to live-session.md (capture / analysis / save)

Tab closes
  -> lease released
    -> shared stream disconnects only when the last diagnostics/live-session observer closes

Management action on a v2 endpoint while disconnected
  -> LiveDaqDetailViewModel.SetTime / EditConfig / SelectConfigFile / UploadConfig
    -> IFilesService (CONFIG picker/load), IDaqManagementService (MGMT TCP workflow), IDialogService (CONFIG editor)
      -> ViewModelBase.Notifications / ErrorMessages
```

The capture, analysis, save, and live-session view-model side of this flow continues in [Live Session Recording](live-session.md#data-flow).

## Live Wire Protocol

The live transport uses framed TCP streams separate from the framed management protocol used by import, remote trash, Set Time, and Replace Config. LIVE and MGMT share the DAQ's single-client TCP port, so only one live or management connection can be active at a time. The app supports live v2 and live v3 in parallel; `LiveDaqCatalogService` records the protocol version from TXT `live_proto`, and `ILiveDaqClientFactory` selects the matching client. Both clients publish the same canonical `LiveProtocolFrame` records to the rest of the app.

### V2 Frame Header

Live v2 uses the legacy `"LIVE"` frame envelope. Every v2 message consists of a 16-byte header followed by a typed payload.

| Offset | Size | Field         | Description                                    |
| ------ | ---- | ------------- | ---------------------------------------------- |
| 0      | 4    | Magic         | `0x4556494C` (`"LIVE"` little-endian)          |
| 4      | 2    | Version       | Protocol version (currently `2`)               |
| 6      | 2    | FrameType     | Identifies the payload layout                  |
| 8      | 4    | PayloadLength | Byte count of the payload following the header |
| 12     | 4    | Sequence      | Monotonically increasing frame counter         |

### V2 Frame Types

| Type            | Value | Direction     | Payload size | Description                                                                |
| --------------- | ----- | ------------- | ------------ | -------------------------------------------------------------------------- |
| `StartLive`     | `1`   | client -> DAQ | 16           | Request to begin streaming with individual sensor mask and rate caps       |
| `StopLive`      | `2`   | client -> DAQ | 0            | Request to stop the active session                                         |
| `Ping`          | `3`   | client -> DAQ | 0            | Keep-alive ping                                                            |
| `Identify`      | `4`   | client -> DAQ | 0            | Request the board serial without starting a live session                   |
| `StartLiveAck`  | `16`  | DAQ -> client | 12           | Result code, session ID, accepted sensor mask                              |
| `StopLiveAck`   | `17`  | DAQ -> client | 4            | Confirms session stopped                                                   |
| `Error`         | `18`  | DAQ -> client | 4            | Error code for rejected or failed operations                               |
| `Pong`          | `19`  | DAQ -> client | 0            | Keep-alive pong                                                            |
| `SessionHeader` | `20`  | DAQ -> client | 72           | Accepted rates, calibration, IMU locations, requested and accepted sensors |
| `IdentifyAck`   | `21`  | DAQ -> client | 8            | 8-byte board serial used to derive the device GUID                         |
| `TravelBatch`   | `32`  | DAQ -> client | variable     | Suspension encoder data batch                                              |
| `ImuBatch`      | `33`  | DAQ -> client | variable     | IMU sensor data batch                                                      |
| `GpsBatch`      | `34`  | DAQ -> client | variable     | GPS fix records                                                            |
| `SessionStats`  | `48`  | DAQ -> client | 28           | Running session statistics (duration, sample counts)                       |

### V2 Start Request

`START_LIVE` carries a 16-byte payload of four `uint32` fields: `RequestedSensorMask`, `TravelHz`, `ImuHz`, `GpsFixHz`. The Hz fields cap each stream's rate; zero means use the device default. `RequestedSensorMask` is an individual sensor-instance mask: fork travel, shock travel, frame IMU, fork IMU, rear IMU, and GPS each have their own bit. The app derives the mask from the requested rate controls: nonzero travel requests both travel channels, nonzero IMU requests all IMU locations, and nonzero GPS requests GPS. The configuration the shared stream actually sends is described under [Stream Configuration](live-session.md#stream-configuration).

`LiveDaqStreamConfiguration` stores requested rates internally as millihertz. `LiveDaqV2Client` rounds those rates to whole-Hz wire values because v2 cannot express fractional hertz.

### V3 Frame Shape

Live v3 starts with a 7-byte client `LIV3` handshake. The DAQ replies before any framed message with a 24-byte server hello containing `LIV3`, protocol major/minor, feature flags, maximum payload size, unique board ID, and firmware semver. The app requires major version 3 and a compatible maximum payload, accepts any minor version, ignores unknown feature bits, and checks the hello's unique board ID against the discovered board ID when discovery supplied one.

After the server hello, live v3 uses the exact 12-byte compact header `[session_id:u8, frame_type:u8, frame_flags:u16, payload_length:u32, tx_sequence:u32]`. All integers are little-endian, flags are zero, and payloads are capped at 4096 bytes. `tx_sequence` starts at zero for each connection and is diagnostic only: gaps, repeats, and wrap do not affect receive correctness. Known frames are parsed strictly; unknown DAQ-to-client frame types are skipped by their declared payload length. Control frames include capabilities request/response, device-state request/response, start request/result, stop request/result, session header/result, status, ping/pong, and error. Data frames carry travel, IMU, temperature, GPS, battery, and marker batches using the shared SST v5 descriptor and compact-payload helpers.

Capabilities use a 16-byte header followed by ordered 24-byte stream records. The v3 start request contains a four-byte header followed by ordered 20-byte stream request records. Configuration may explicitly request travel, IMU, temperature, GPS, battery, and marker. Travel/IMU/temperature/GPS may carry supported rate overrides; travel and IMU may also carry supported batch-duration overrides. Battery and marker are never inferred. GPS diagnostics are requested as a GPS extension, and `NO_GPS_HEADER_WAIT` is emitted only when GPS is selected.

`SESSION_HEADER` begins with its own 24-byte LIVE header fields and then carries direct SST v5 stream descriptors, source descriptors, and omission records; it does not contain an SST file metadata envelope. Requested streams and sensors remain client request state. Accepted streams, sensors, rates, extensions, and omissions come from the session header. `LiveV3ProtocolReader` maps the descriptor-owned compact data, status, and final-status records into canonical frames. Status and final records follow accepted descriptor order and preserve producer state/failure, sink backlog, and missed-count/time fields.

Temperature is decoded as its own low-rate canonical stream and is carried separately through diagnostics, capture, persistence, slicing, and recorded-session reprojection. Battery and marker frames are decoded and tolerated by consumers that do not retain them; marker history is saved by live capture. Explicit battery selection never becomes implicit telemetry admission.

### Start Handshakes

A successful v2 start produces two frames in sequence: `START_LIVE_ACK` (result `Ok`, session ID, accepted stream-family mask) followed by `SESSION_HEADER` (full session parameters including accepted rates, calibration scales, IMU locations, and requested/accepted individual sensor masks). A rejected v2 start produces either a `START_LIVE_ACK` with a non-Ok result or an `ERROR` frame.

A successful v3 start is capabilities-first: the client sends the `LIV3` handshake, validates the server hello, requests capabilities once, sends record-based `START_REQ`, receives a pending `START_RESULT` with a nonzero session ID, then waits for the matching `SESSION_HEADER`. A denied `START_RESULT` maps to `LivePreviewStartResult.Rejected` with fixed-registry admission reasons and leaves the connection ready. `ERROR` surfaces code, offending frame type, and detail and closes the connection. A matching pre-header `SESSION_RESULT` fails only that start and also leaves the connection ready.

The DAQ may partially accept a start request. If at least one requested telemetry stream can run, the session header identifies the accepted stream/source subset and carries an omission record for each rejected stream, source, or extension. The app computes missing sensors as `RequestedSensorMask & ~AcceptedSensorMask` for diagnostics while preserving the requested and accepted stream masks separately. If no requested telemetry remains, the DAQ returns a denied start rather than a protocol error.

### Result Codes

| Code | Name             | Meaning                                   |
| ---- | ---------------- | ----------------------------------------- |
| 0    | Ok               | Request accepted                          |
| -1   | InvalidRequest   | Malformed or unsupported request          |
| -2   | Busy             | Another client is already streaming       |
| -5   | NoSensorsStarted | None of the requested sensors could start |

### Result Shape

`StartPreviewAsync` returns a sealed record hierarchy rather than raw error codes:

```csharp
public abstract record LivePreviewStartResult
{
    public sealed record Started(LiveSessionHeader Header) : LivePreviewStartResult;
    public sealed record Rejected(LiveStartErrorCode ErrorCode, string UserMessage) : LivePreviewStartResult;
    public sealed record Failed(string ErrorMessage) : LivePreviewStartResult;
}
```

## Transport Layer

All transport types live in `Sufni.App/Sufni.App/LiveDaq/Services/LiveStreaming/`.

### Protocol Reader

`LiveV2ProtocolReader` and `LiveV3ProtocolReader` are the protocol-specific frame readers. V2 handles the legacy `"LIVE"` frame envelope and direct batch payloads. V3 handles the `LIV3` handshake, server hello, 12-byte headers, capabilities/device-state/start/result control frames, and SST v5 descriptor/data payload decoding. Both readers translate wire payloads into canonical `LiveProtocolFrame` records before anything reaches `LiveDaqSharedStream`, diagnostics state, or live-session capture.

### Client

`ILiveDaqClientFactory` creates `LiveDaqV2Client` or `LiveDaqV3Client` from the current `LiveDaqSnapshot.ProtocolVersion`. The selected client owns the concrete TCP connection lifecycle: `ConnectAsync` -> `StartPreviewAsync` -> streaming -> `StopPreviewAsync` -> `DisconnectAsync`. The v3 client models awaiting hello, ready, start pending, awaiting session header, active, and stopping phases. A terminal result replaces the session decode context and returns the connection to ready, so denied starts, completed sessions, and session-ID wrap from 255 to 1 all work without reconnecting. Stop waits for `STOP_RESULT` and then `SESSION_RESULT` in that order.

Three background tasks run off the UI thread. The receive loop reads exact frame headers and payloads, handles ordinary control frames immediately, and pushes telemetry into a bounded raw channel. A terminal result enters the same pipeline as a non-droppable ordering barrier, so every retained earlier telemetry frame is published before session completion. Each queued telemetry frame retains the descriptor context that owned it, so the terminal reset cannot invalidate already-received data. The parse loop decodes raw telemetry into canonical frames and writes them to a second bounded channel; the publish loop emits them as `LiveDaqClientEvent` values. Telemetry pressure may drop telemetry according to the bounded-channel counters, but cannot lose lifecycle frames. A `SemaphoreSlim` gate serializes lifecycle state mutations, and no-request-ID operations retain one outstanding response slot until the matching response arrives.

The client is owned by `LiveDaqSharedStream`, which reuses one client per DAQ identity and fans out stream state and frames to both the diagnostics tab and any attached live-session tabs.

### Drop Counters

`LiveDaqClientDropCounters` is the immutable record published as part of `LiveDaqSharedStreamState` and rolled into the live-session control state. It tracks six pressure boundaries: `RawTelemetryFramesSkipped` at the receive-loop raw channel, `ParsedTelemetryFramesDropped` at the parse-to-publish channel, `SubscriberFramesDropped` at each shared-stream subscriber buffer, `SignalBatchesCoalesced` when the live signal display loop merges pending batches, `SignalSamplesDiscarded` when signal batches exceed display capacity, and `StatisticsRecomputesSkipped` when live-session statistics recompute work is skipped because a newer recompute superseded it. Recording-side counters added by the live-session display/statistics loops are merged on top by `LiveSessionService` before it publishes them to the UI.

### Shared Stream

`LiveDaqSharedStreamRegistry` owns one `LiveDaqSharedStream` per DAQ identity key. A shared stream keeps the current requested configuration, accepted session header, protocol version, connection state, and frame fan-out for that identity. Observers acquire a generic lease; live-session observers also hold a configuration-lock lease so the diagnostics tab cannot reconfigure rates while a live capture is attached. The lease mechanics on the recording side are described under [Configuration Lock](live-session.md#configuration-lock).

When the last observer releases its lease, the registry disconnects and evicts the stream. If the transport drops or discovery loses the DAQ while observers are still attached, the current stream closes immediately, publishes terminal state to those observers, and is evicted for future lookups so the next attachment creates a fresh stream instance. A catalog refresh that changes the protocol version for an existing identity also closes and evicts the stream; callers must reopen the live tab so a fresh client can be created for the new protocol.

### Session State

`LiveDaqSessionState` is a thread-safe accumulator for decoded sensor values used by the diagnostics tab. It holds latest travel, per-location IMU, GPS, and stats/status frames behind a single lock. `ApplyFrame()` updates internal state from any canonical `LiveProtocolFrame`; `CreateSnapshot()` produces an immutable `LiveDaqUiSnapshot` for UI binding. The snapshot captures connection state, accepted session parameters, requested/accepted individual sensor masks, formatted accepted rates, and latest raw protocol values at a single point in time. Travel remains raw measurement data here; calibration and `mm (percent)` formatting are applied later in the detail view model. `LiveTravelUiSnapshot` carries front/rear active flags; when only one travel channel is accepted, the inactive channel is left empty so firmware neutral values are not presented as real measurements.

The recording side does not use `LiveDaqSessionState`. It subscribes to `ILiveDaqSharedStream.Frames` directly and accumulates raw samples into its own capture stores — see [Capture Service](live-session.md#capture-service).

### GPS Preview State

`GpsPreviewState` interprets GPS fix modes for diagnostics UI display: fix mode 0 is no fix, mode 1 is 2D fix (has fix but not ready for full use), mode 2 is 3D fix (has fix and ready). `LiveDaqSessionState` publishes it in `LiveDaqUiSnapshot` for the diagnostics tab. Live-session media does not consume this record; it renders map state from accepted GPS capability and projected `TrackPoint[]` values. See [Live Session Recording § GPS Preview State](live-session.md#gps-preview-state).

## Discovery & Catalog

### Browse Ownership

`DaqBrowseOwner` implements reference-counted lease-based browse ownership for `_sufni._tcp`. `AcquireBrowse()` returns a disposable lease; the first lease starts the underlying `IServiceDiscovery` mDNS browse, and the last disposed lease stops it. This keeps live discovery decoupled from the import pipeline — both features can browse concurrently without one clearing the other's state. The lease uses `Interlocked.Exchange` for safe double-dispose.

### Board-ID Inspector

`LiveDaqBoardIdInspector` is a v2 compatibility helper used by the network import path. It opens a short-lived v2 LIVE connection, sends an `IDENTIFY` frame, and parses the matching `IDENTIFY_ACK` to recover the board GUID for legacy v2 endpoints. The live catalog no longer probes devices for identity; it reads TXT `bid` when available.

### Catalog Service

`LiveDaqCatalogService` subscribes to `IServiceDiscovery` (keyed `"daq"`) for the shared DAQ mDNS browse owned by `DaqBrowseOwner` (`_sufni._tcp`). Announcements are considered live-capable only when TXT `live_proto` is present and equal to `2` or `3`; missing or unsupported values are ignored. TXT `bid`, when present as exactly 16 hex characters, is converted into the board GUID. Without `bid`, endpoint identity falls back to a protocol-qualified key such as `v3:192.168.0.50:1557`, so v2 and v3 announcements for the same address cannot accidentally share one runtime identity.

Entries are emitted through a `BehaviorSubject<IReadOnlyList<LiveDaqCatalogEntry>>` — each entry carries an identity key, display name, host, port, optional board ID, and `LiveProtocolVersion`. Announcements are keyed internally by service instance name when available, otherwise by endpoint. When a service disappears, its entry is removed and the catalog re-emitted.

## Known-Board Query

`LiveDaqKnownBoardsQuery` merges three data sources to produce enriched board records: `Board` rows from the database, `ISetupStore` for setup names, and `IBikeStore` for bike names. For each board, it attempts two setup lookups: direct `board.SetupId` first, then fallback via `SetupStore.FindByBoardId()`. For known setup+bike pairs it resolves the shared `ITelemetryBikeProcessingContextFactory` context and builds the live travel-calibration answer from the same `BikeData` delegates that live-session capture passes into telemetry processing. The query no longer performs its own rear-calibration build. It exposes a `Changes` observable that fires when setup or bike stores change, keyed lookup by identity key, and a travel-calibration answer for a specific DAQ identity so the detail view model can format calibrated travel without depending directly on setup or bike stores.

## Runtime Store

`LiveDaqStore` is a runtime-only in-memory `SourceCache<LiveDaqSnapshot, string>` keyed by identity key (board ID when known, protocol-qualified endpoint fallback). It does not persist to the database, has no `RefreshAsync()`, and snapshots carry no `Updated` timestamp. The read-only `ILiveDaqStore` is injected into the list view model; the `ILiveDaqStoreWriter` is reserved for the coordinator.

`LiveDaqSnapshot` is an immutable sealed record carrying identity key, display name, board ID, host/port, online status, setup name, bike name, and live protocol version.

## Coordinator

`LiveDaqCoordinator` owns all store writes, browse lifecycle, and tab routing.

**Activate / Deactivate** — called by `MainPagesViewModel` when the Live page becomes selected or deselected. On activate: acquires a browse lease, subscribes to catalog changes and known-board query changes, seeds the store with offline known boards. On deactivate: disposes all subscriptions and the browse lease, clears online state from the store.

**Reconcile** — the core merge logic. Takes current catalog entries and known-board records, builds a dictionary of snapshots. Known boards always appear (offline if not discovered). Discovered DAQs with a board ID matching a known board get enriched with setup and bike names. Unknown discovered DAQs appear with the catalog's protocol-qualified endpoint identity. The store is cleared and rebuilt on each reconciliation.

**SelectAsync** — routes row selection through `shell.OpenOrFocus<LiveDaqDetailViewModel>` with an identity-key matcher. If a diagnostics tab for that DAQ already exists, it focuses it; otherwise it creates a new detail view model from the snapshot, the shared stream registry, the shared known-board query, `IDaqManagementService`, and `IFilesService`.

**OpenSessionAsync** — resolves `LiveDaqSessionContext` for a known DAQ, gets the shared stream for that identity, and routes one `LiveSessionDetailViewModel` per identity through `shell.OpenOrFocus`. The coordinator remains a routing layer only; live capture accumulation and save happen below it (see [Live Session Recording](live-session.md)).

## View Models

**`LiveDaqListViewModel`** projects the store's `Connect()` stream through DynamicData (`Filter` -> `TransformWithInlineUpdate` -> `SortAndBind`) into a `ReadOnlyObservableCollection<LiveDaqRowViewModel>` sorted online-first then by display name. Owns search filtering via `BehaviorSubject`. Delegates activate/deactivate to the coordinator.

**`LiveDaqRowViewModel`** is a lightweight observable wrapper around a `LiveDaqSnapshot`. Exposes display properties (name, online status, endpoint, setup, bike). Intentionally does not implement `IListItemRow` — live DAQs are not deletable and need a custom row surface with online/offline presentation.

**`LiveDaqDetailViewModel`** extends `TabPageViewModelBase`, one instance per open diagnostics tab. It acquires a generic observer lease on the per-identity `ILiveDaqSharedStream`, projects the shared stream frames through `LiveDaqSessionState`, and uses a `DispatcherTimer` to publish a throttled `LiveDaqUiSnapshot` for raw diagnostics UI binding. It remains the only editable surface for connect, disconnect, and requested-rate reconfiguration. Partial sensor starts add a diagnostics notification for the missing requested sensors instead of populating the error list. It also hosts the management actions, all driven by a single shared `managementOperation` `CancellableOperation` (separate from the connect operation) so any new management action implicitly cancels an in-flight one and the tab lifecycle can cancel them all on unload.

Management actions stay in the detail view model rather than the transport layer or coordinator:

- `SetTimeCommand` calls `IDaqManagementService.SetTimeAsync(...)` and reports success/failure through `Notifications` and `ErrorMessages`.
- `EditConfigCommand` downloads the current CONFIG via `IDaqManagementService.GetFileAsync(...)`, parses it into a `DaqConfigDocument`, and shows the `LiveDaqConfigEditorViewModel` dialog; saving from the dialog re-uploads through `IDaqManagementService`.
- `SelectConfigFileCommand` calls `IFilesService.OpenDeviceConfigFileAsync(...)`, validates an exact `CONFIG` filename, and stages the selected bytes.
- `UploadConfigCommand` calls `IDaqManagementService.ReplaceConfigAsync(...)`, then clears the staged CONFIG regardless of success, typed failure, or exception.

The management endpoint and protocol are resolved from the latest `ILiveDaqStore` snapshot on every evaluation of `CanManage` and at command execution time, falling back to the shared stream's current catalog snapshot while the stream is still alive. The management affordances are enabled only when the DAQ endpoint is known, the endpoint protocol is v2, and the live connection state is `Disconnected`. This avoids overlapping LIVE and MGMT connections on the shared single-client port, keeps the existing v2 management path unchanged, and disables v3 diagnostics management until MGMT v3 wire bytes are specified.

The live-session tab view model (`LiveSessionDetailViewModel`) is described in [Live Session Recording § Live Session Detail View Model](live-session.md#live-session-detail-view-model).

## Views

Both desktop and mobile heads add the Live tab and bind to the same view models. Desktop-only live views live under `Sufni.App/Sufni.App/LiveDaq/DesktopViews/`; mobile/shared live views live under `Sufni.App/Sufni.App/LiveDaq/Views/`.

- `MainPagesDesktopView.axaml` / `CompactShellView.axaml` — both add a "Live" tab to the primary page set, bound to `LiveDaqsPage`
- `LiveDaqListDesktopView.axaml` / `LiveDaqListView.axaml` — list of known and discovered DAQs with search, notifications, and error bars
- `LiveDaqListItemButton.axaml` (desktop) — custom row control showing display name, setup/bike labels, endpoint, and an online/offline badge
- `LiveDaqDetailDesktopView.axaml` / `LiveDaqDetailView.axaml` — diagnostics tab with connection controls, requested rate inputs, accepted session info, a disconnected-only Device Management card (Set Time, Edit CONFIG, Replace Config, Upload CONFIG), notifications/error bars, and travel/IMU/GPS sensor sections
- `LiveDaqConfigEditorView.axaml` — CONFIG editor dialog launched from the detail tab on either head

## Design Decisions

1. **Separate from import** — the live feature does not reuse `ITelemetryDataStoreService.DataStores` or the import browse start/stop. Discovery, catalog, and browse ownership are independent seams.
2. **Runtime-only store** — `LiveDaqStore` has no persistence, no `RefreshAsync()`, and no optimistic-concurrency surface. It exists only to project discovered and known boards into the list.
3. **Custom row type** — `LiveDaqRowViewModel` does not implement `IListItemRow` because live rows are not deletable and need online/offline presentation rather than the entity-list pattern.
4. **Per-identity shared stream** — one `LiveDaqSharedStream` owns the transport for one DAQ identity, and both the diagnostics tab and the live-session tab subscribe to it through leases.
5. **Throttled UI updates** — sensor data arrives at packet rate but UI binding updates are snapshot-based and throttled by `DispatcherTimer`, not by raw frame arrival.
6. **Lease-based browse** — browse ownership uses reference counting so import and live can browse concurrently without interfering.
7. **Coordinator activation** — the coordinator activates only when the Live page is selected and deactivates when another page is selected, avoiding always-on mDNS browse for a page that may never be visited.
8. **Protocol-specific clients behind a canonical frame model** — v2 and v3 wire parsing stays inside protocol-specific readers/clients. Everything above `ILiveDaqClientFactory` receives canonical live frames and does not branch on wire layout.
9. **Disconnected v2-only management** — the detail tab disables management actions while its live client is connected instead of trying to arbitrate concurrent LIVE and MGMT workflows on the DAQ's single-client port. It also disables management for v3 snapshots because the v3 management protocol is not specified.
10. **Recording is its own slice** — capture, analysis computation, the live signal pipeline, and the `Session` save path are owned by `ILiveSessionService` and `SessionCoordinator.SaveLiveCaptureAsync`, not by anything in this file. See [Live Session Recording](live-session.md).
