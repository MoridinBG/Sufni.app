# Live Session Recording

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers the live-session recording slice: the per-tab capture service that subscribes to a shared live transport, accumulates raw samples for save, derives signal batches, computes rolling analysis state, and persists the result as a recorded `Session` row with a live-capture source and processing fingerprint. Both desktop and mobile heads expose the live-session tab. The transport, discovery, catalog, and diagnostics-tab side of the live feature lives in [Live DAQ Streaming](live-streaming.md).

## Contents

- [Overview](#overview)
- [Data Flow](#data-flow)
- [Configuration Lock](#configuration-lock)
- [Capture Service](#capture-service)
- [Capture Stores](#capture-stores)
- [Live Signal Pipeline](#live-signal-pipeline)
- [Stream Configuration](#stream-configuration)
- [Presentation Records](#presentation-records)
- [Live Session Detail View Model](#live-session-detail-view-model)
- [GPS Preview State](#gps-preview-state)
- [Save Flow](#save-flow)
- [Design Decisions](#design-decisions)

## Overview

The live-session slice runs in a dedicated tab. The Live primary page opens it through `shell.OpenEditor(new EditorKey.LiveSession(identityKey))`. The tab uses the per-identity `LiveDaqSharedStream`, the same transport as the diagnostics tab. It adds per-tab capture, Telemetry Signals display, statistics and a save path. Each open live-session tab has one `LiveSessionDetailViewModel` and one `ILiveSessionService`, both scoped to the tab lifetime.

The recording side is decoupled from the transport in three ways:

1. The shared stream owns connection, frame parsing, fan-out, and disconnect. `ILiveSessionService` owns capture, signal batches, statistics and save preparation, and never touches the socket.
2. `LiveDaqCoordinator.SaveCaptureAsync` is the persistence workflow. It prepares the capture, saves and publishes the recording, then resets the service. The shell owns navigation.
3. The live-session view model holds no transport state. It consumes `ILiveSessionService` snapshots and signal batches. It sends analysis choices to the service through `UpdateAnalysisOptions` and passes `SessionPreferences` to `SaveCaptureAsync`.

```mermaid
graph LR
    Shared["LiveDaqSharedStream<br/>(per identity)"] --> Service["ILiveSessionService<br/>(per live tab)"]
    Service --> CaptureStores["FixedRateSegmentBuilder<br/>travel / IMU<br/>+ AppendOnlyChunkBuffer GPS"]
    Service --> SignalPipe["LiveSignalPipeline<br/>(velocity SG filter)"]
    Service --> StatsLoop["Analysis loop<br/>(TelemetryData.FromLiveCapture)"]
    SignalPipe --> RecentTravel["Recent travel lists<br/>(127 ms velocity context)"]
    Service -->|"Snapshots"| DetailVM["LiveSessionDetailViewModel"]
    SignalPipe -->|"SignalBatches"| DetailVM
    DetailVM --> Coord["LiveDaqCoordinator<br/>SaveCaptureAsync"]
    Coord --> Source["RecordedSessionSourceFactory<br/>live_capture"]
    Coord --> Reprocessor["RecordedSessionReprocessor<br/>telemetry + track + fingerprint"]
    Coord --> Repo["ISessionRepository<br/>ImportProcessedSessionAsync"]
    Coord --> Prefs["ISessionPreferences<br/>UpdateRecordedAsync"]
    Coord --> Publish["StorePublication batch<br/>session / source / track<br/>store writers (publish only)"]
```

The capture and unsaved edits are in-memory state. They end with the tab or
process and have no restart checkpoint. Desktop shutdown runs the same
save/discard/cancel close policy as closing the capture tab, and waits for its
cleanup before it disposes services. Capture disposal drains its loops, releases
the configuration lock, then releases its observer lease. Management owners
cancel and drain before their registry dependency is disposed. On mobile, the
tab survives backgrounding, but the app does not persist an unfinished capture.

## Data Flow

```
Shared stream emits LiveProtocolFrame
  -> LiveSessionService.HandleFrame
    -> LiveTravelBatchFrame  -> FixedRateSegmentBuilder<ushort> (front/rear) + LiveDisplayUpdate.Travel
    -> LiveImuBatchFrame     -> FixedRateSegmentBuilder<ImuRecord> per active location + LiveDisplayUpdate.Imu (vibration RMS + frame pitch/roll)
    -> LiveTemperatureBatchFrame -> AppendOnlyChunkBuffer<TemperatureSample>
    -> LiveGpsBatchFrame     -> AppendOnlyChunkBuffer<GpsRecord> + projected TrackPoint[]
    -> LiveStatusFrame / LiveSessionStatsFrame -> latest queue/dropped counters
    -> LiveMarkerBatchFrame  -> marker list
    -> LiveSessionResultFrame -> final SST stream status

Display loop (Task.Run)
  -> Channel<LiveDisplayUpdate> reader
    -> ILiveSignalPipeline.AppendTravelSamples / AppendImuSamples
      -> recent travel lists + pending batch lists
        -> PeriodicTimer flush -> Savitzky-Golay velocity over recent 127 ms window
          -> LiveSignalBatch (Travel/Velocity/IMU/Pitch-Roll) -> Subject<LiveSignalBatch>
            -> LiveSessionDetailViewModel.QueueSignalBatchRefresh
              -> LiveSessionSignalsWorkspaceViewModel.ApplySignalDataPresence
    -> projected TrackPoint[] updates drive Speed/Elevation signal rows

Statistics loop (Task.Run)
  -> snapshot segment builders and GPS buffer under lock
    -> TelemetryData.FromLiveCapture (background runner)
      -> selected-mode damping percentages + LiveSessionAnalysis.Compute
        -> immutable keyed plot results and surface states
          -> LiveSignalPipeline.SetAnalysis -> next display batch
          -> LiveSessionPresentationSnapshot (also retained for tab reactivation)
            -> UI-thread LiveSessionAnalysisResultSource -> plots render results

User presses Save
  -> LiveSessionDetailViewModel.SaveImplementation
    -> LiveDaqCoordinator.SaveCaptureAsync(immutable draft, capture service, preferences)
      -> ILiveSessionService.PrepareCaptureForSaveAsync (capture and its revision)
      -> RecordedSessionSourceFactory.CreateLiveCapture
      -> setup and bike repository reads
      -> RecordedSessionReprocessor.ReprocessAsync
      -> ISessionRepository.ImportProcessedSessionAsync
      -> ISessionPreferences.UpdateRecordedAsync
      -> PublishSessionsChangedAsync / PublishSourcesChangedAsync / PublishTracksChangedAsync
         in one StorePublication batch on the UI dispatcher
      -> ILiveSessionService.ResetCaptureAsync
        -> LiveSessionSaveResult.Saved -> view model opens recorded editor

User presses Reset
  -> ILiveSessionService.ResetCaptureAsync
    -> clear segment builders, temperature/GPS buffers, markers, gaps, final status, bump captureRevision/displayEpoch
      -> LiveSignalPipeline.Reset (clear pending + sliding window, emit empty batch)
        -> view model clears analysis pages and timeline
```

## Configuration Lock

The live-session service holds a configuration lock on the shared stream for the tab lifetime. Recording stays consistent, and the diagnostics tab on the same DAQ stays attachable for read-only viewing.

`ILiveDaqSharedStream` exposes two lease kinds:

- `AcquireLease()` is a generic observer lease.
- `AcquireConfigurationLock()` is an observer lease that _also_ increments the lock count published in `LiveDaqSharedStreamState.IsConfigurationLocked`.

While any configuration-lock lease exists, `LiveDaqSharedStream.ApplyConfigurationAsync(...)` returns without reconfiguring. The diagnostics tab therefore cannot tear down the connection mid-capture by changing rates. A direct `StopAsync` request returns `LiveDisconnectResult.Locked` and the capture transport keeps running. `LiveSessionService.EnsureAttachedAsync` acquires both leases in one atomic block and disposes both on disposal. The diagnostics tab acquires only the observer lease and keeps it until tab close.

## Capture Service

`ILiveSessionService` (`Sufni.App/Sufni.App/LiveDaq/Services/LiveStreaming/ILiveSessionService.cs`) is the per-tab recording surface. `LiveSessionServiceFactory` (`ILiveSessionServiceFactory`) builds one instance per new live-session tab after shell deduplication, giving it a `LiveDaqSessionContext` (identity, bike data, calibration), the per-identity `ILiveDaqSharedStream`, the shared `ISessionPresentationService`, an `IBackgroundTaskRunner`, a freshly built `ILiveSignalPipeline`, and an injected `TimeProvider` for analysis cadence and pressure quiet periods. The implementation in `LiveSessionService` runs three coordinated activities behind one lock plus a separate display-queue lock.

### Lifecycle and Attachment

The service owns one capture phase:

- `Active` accepts frames.
- `AwaitingReset` keeps a prior firmware session available for save.
- `Closed` rejects new frames permanently. Reset never reopens `Closed`.
- `Disposed` rejects service operations.

Reset returns `AwaitingReset` to `Active` only when the shared stream still has an open session header. Attachment derives from the owned observer lease.

**Attach.**

- `EnsureAttachedAsync` is idempotent. Under the gate, it acquires the observer lease, configuration-lock lease, `signalPipeline.Start()`, display loop task, frame subscription and state subscription, in that order.
- Either lease acquisition can return `Closed(reason)`. The service then releases any observer it already holds and does not connect.
- After acquisition, `sharedStream.ConnectLiveAsync(...)` runs outside the gate. The service returns the typed start outcome, including `Busy(holder)` and `Unavailable(reason)`, and the editor shows availability feedback.
- A failed or unexpectedly closed connection produces safe error text with expandable diagnostics. These outcomes keep captured data.
- An unexpected exception tears down the resources that this attach acquired.
- `DisposeAsync` mirrors attach. It stops subscriptions, completes the display channel, awaits the statistics and display loops, releases both leases and disposes the signal pipeline.

**Reset.** `ResetCaptureAsync` clears the travel and IMU segment builders, temperature and GPS buffers, markers, gaps, final status, latest session statistics, analysis state and track points. It advances three counters:

- `captureRevision` — the save identity.
- The analysis input revision — for capture or mode changes.
- `displayEpoch` — the display loop uses it to discard in-flight display updates.

Reset then resets the signal pipeline, which clears its sliding window and pending batch and publishes one empty `LiveSignalBatch`. After a recoverable DAQ session rollover, reset also adopts the shared stream's current session header and rebuilds the IMU builders. It reopens streaming only if the stream is still open and has a header. Otherwise the capture stays closed.

### Snapshot Publication

Each snapshot gets a monotonically increasing `Revision` under the capture lock,
separate from `CaptureRevision`. A connection or analysis change can therefore
publish without new samples. Publication rejects an older revision and updates
`Current` atomically. A synchronous notification queue serializes concurrent and
reentrant publications. A reentrant update waits until the current notification
finishes. Disposal waits for queued notifications before it completes the
observable. Enriching the first GPS point with derived speed replaces that point
and never mutates a point that an earlier snapshot holds.

### Frame Handlers

`HandleFrame` dispatches by the data-bearing canonical frame types.

- **Travel and IMU.** Under the gate, the handler appends raw samples to fixed-rate segment builders and records invalid-validity and first-index gaps as `RawStreamGap`. It builds a `LiveDisplayUpdate.Travel` or `LiveDisplayUpdate.Imu` with calibrated values for the live plots. It pushes that update onto a bounded `Channel<LiveDisplayUpdate>` (`DisplayUpdateQueueCapacity = 8`, `BoundedChannelFullMode.DropOldest`).
- **IMU display values.** `LiveImuDisplaySignalProcessor` (`LiveDaq/Services/Imu`) derives them. Firmware already bias-corrects and rotates IMU readings into the bike frame. Per-location vibration RMS uses dynamic acceleration after low-pass gravity removal and needs no session-start rest window. Optional frame pitch/roll fuses frame accelerometer and gyro data relative to the bike-frame calibration. It accepts accelerometer correction only from gravity-like samples.
- **Saved IMU.** Raw IMU segment records are the saved source of truth. Live captures never populate the dense `RawImuData.Records` graph, also when they are aligned and gap-free.
- **Temperature.** Low-rate `TemperatureSample` values go to a separate buffer and never enter the high-rate IMU motion path.
- **GPS.** The handler appends the complete raw batch and then projects `TrackPoint`s incrementally. If a record is older than the projected tail, the service reprojects all retained GPS records. Later records from that batch stay available to the fallback projection and to save.
- **Markers and results.** `LiveMarkerBatchFrame` appends user markers. `LiveSessionResultFrame` stores the final SST stream status for v3 captures, including sink backlog batches.
- **Pressure.** Display pressure starts a quiet period for analysis recomputation. Capture accumulation continues independently.

The travel handler sets `CanSave` to `true` once either travel channel has at least 5 samples. It then publishes the first saveable-capture snapshot, which enables the tab's save command.

Capture duration uses device-monotonic time. Final status supplies the stop time
when present. Otherwise the latest valid travel end is used. Before travel data
arrives, GPS batches advance duration from their device timestamps. GPS wall-clock
fix times never drive it. Closing the stream stops frame acceptance and freezes
the duration. Reset clears its origin and latest GPS timestamp.

A new `SessionId` in `LiveDaqSharedStreamState.SessionHeader`, while the capture stores hold data, moves the capture to `AwaitingReset` with a "DAQ started a new live session." error. The old header and captured data stay available for save until the user resets. The service accepts no frames in this state. After a recoverable reset, the service accepts identity-bearing travel, IMU, temperature, GPS, battery, marker, statistics and session-result frames only for the newly adopted session ID. Late frames from the previous firmware session cannot enter the next capture.

### Statistics Loop

Once a travel batch has produced enough samples for `CanBuildAnalysisLocked`,
`QueueAnalysisRecompute` advances the queued analysis input revision and starts
the owned loop when needed. The loop snapshots capture data and immutable
`LiveAnalysisOptions` under the gate, then computes telemetry, selected-mode
damping percentages, keyed travel/frequency/velocity/stroke/balance results and
surface states off the UI thread. `LiveSessionAnalysis` carries a frozen result
map. Changed modes or cutoffs queue new analysis without changing the capture's
save revision; results for superseded options or a reset capture are rejected.

Accepted results are retained in the presentation snapshot and passed to
`LiveSignalPipeline.SetAnalysis` for the next display batch. The UI applies those
values through `LiveSessionAnalysisResultSource`, the eager implementation of
`ISessionAnalysisResultSource`. Recorded result state additionally supports
on-demand requests; shared plot views render either source's prepared results.
No live plot or live mode setter calculates telemetry statistics on the UI thread.

Two gates limit analysis. `nextAnalysisRunAt` enforces
`PlotSettings.LiveAnalysisRefreshIntervalMs`. The 500-ms
`AnalysisPressureQuietPeriod` skips queueing while client or display pressure is
recent. The service logs a calculation failure once and publishes it with a fixed
reason, safe text and separate details. Captured samples stay saveable, and later
data can queue analysis again.

### Display Loop

The display loop reads `LiveDisplayUpdate` records off the bounded channel. It drops any record whose `Epoch` differs from `displayEpoch`, which skips updates from before the latest reset. It dispatches the rest into the signal pipeline (`AppendTravelSamples` / `AppendImuSamples`). The loop never writes into the capture stores. The frame handler appends capture and display data under one gate; the display path then runs off-lock.

## Capture Stores

The capture service uses fixed-rate segment builders for travel/IMU and an append-only chunk buffer for GPS. The segment builder is shared with SST v5 parsing so live v3 capture and file import preserve gaps the same way.

### `FixedRateSegmentBuilder`

`FixedRateSegmentBuilder<T>` (`Sufni.Telemetry/FixedRateSegmentBuilder.cs`) is the raw fixed-rate sink for segment-aware save and statistics. `LiveSessionService` owns one builder for front travel, one for rear travel, and one per active IMU location. Each builder keeps contiguous valid samples in `FixedRateSegment<T>` runs and emits `RawStreamGap` records for invalid source validity or first-index discontinuities. On save, travel builders become `RawCountSegment[]`, and IMU builders become `RawImuSegment` records grouped by physical location.

This is what lets live v3 captures retain the same gap semantics as SST v5 files. V2 live streams naturally produce one dense segment per active travel side or IMU location because their batches do not carry per-source validity gaps. Segment snapshots are cheap enough to take under the service gate and convert to `LiveTelemetryCapture` later on the background runner.

### `AppendOnlyChunkBuffer`

`AppendOnlyChunkBuffer<T>` (`AppendOnlyChunkBuffer.cs`) is the raw GPS sink for save and statistics. GPS is event-like, not fixed-rate, so it is append-only:

- `gpsRecords` — `GpsRecord` GPS fixes (`GpsChunkSize = 256`)

Internally the buffer keeps a `List<T[]>` of sealed full chunks plus one growing active chunk; `Append` rolls the active chunk into the sealed list once it fills. `CreateSnapshot()` captures the sealed-chunk references plus the active chunk and active count as a `ChunkedBufferSnapshot<T>` value type — the snapshot can be taken under the service's gate and flattened into a contiguous `T[]` later (off-lock, on the background task runner during statistics or save) without copying every sample twice.

This shape keeps event-like GPS capture cheap: each `LiveGpsBatchFrame` appends in O(batch) without resizing a long-lived array, statistics recomputes can grab a snapshot in O(chunks) under the gate, and `BuildCapture` flattens to `T[]` once per save.

### Live Velocity Window

`LiveSignalPipeline` keeps recent travel times plus front/rear travel values only for live signal velocity display. The retained context is duration-based: samples older than 127 ms from the newest travel sample are trimmed while keeping at least the five samples required by the Savitzky-Golay implementation. Capture and save never read this window — the segment builders and GPS buffer are the source of truth for the saved sample stream.

## Live Signal Pipeline

`ILiveSignalPipeline` and its `LiveSignalPipeline` implementation (`Sufni.App/Sufni.App/LiveDaq/Services/LiveStreaming/LiveSignalPipeline.cs`, `ILiveSignalPipeline.cs`, `LiveSignalPipelineFactory.cs`) bridge per-frame `LiveDisplayUpdate` records into per-row `LiveSignalBatch` deltas the live Signals workspace consumes. `LiveSignalPipelineFactory.Create()` constructs one with a flush interval driven by `PlotSettings.LiveSignalRefreshIntervalMs` and the injected `TimeProvider`. Its `PeriodicTimer` uses that clock.

**Purpose.** The DataStreamer-backed live plots want batches of samples at a steady cadence, not a callback per inbound frame. The pipeline collects appended samples into a `PendingSignalBatch` and flushes once per timer tick, which gives the UI a predictable refresh rate and lets the velocity filter run over a stable window snapshot.

**Threading.** The pipeline owns one `Subject<LiveSignalBatch>`, one shared lock, and one flush loop task started by `Start()`. `AppendTravelSamples`, `AppendImuSamples`, and `AppendFramePitchRollSamples` are called from the display-loop task off the UI thread; they take the lock just long enough to push into the pending lists and update the recent travel window. `FlushPendingSignalBatch` (also off the UI thread, run from the periodic-timer loop) swaps the pending batch out under the lock, snapshots the recent travel arrays, then computes velocity and emits the batch outside the lock. A failed flush is logged and its detached batch is discarded; the loop continues with newly accumulated samples. `Reset` clears state, bumps the revision, and emits a single `LiveSignalBatch.Empty` so subscribers can drop their plot data; `DisposeAsync` cancels the flush loop and completes the subject.

**Batch shape.** `LiveSignalBatch` (defined in `LiveSessionPresentation.cs`) carries:

- `Revision` — monotonically increasing across appends and resets, so consumers can filter stale batches.
- `Analysis` — optional complete background analysis result, delivered even when no new signal samples are pending. An analysis-only batch does not reset live plots; reset emits an empty batch without analysis.
- `TravelTimes`, `FrontTravel`, `RearTravel` — exactly the samples appended since the previous flush.
- `VelocityTimes`, `FrontVelocity`, `RearVelocity` — velocity samples aligned with the same travel times for that batch only. Velocity is computed by running a Savitzky-Golay filter over the recent travel snapshot. The filter window targets 51 ms, is converted to an odd sample count from the observed sample period, and falls back to NaN if fewer than five samples are available or the snapshot contains a NaN. The pipeline computes a unit-spacing Savitzky-Golay derivative and scales each output by the local mean timestamp interval, clipped at the edges. This preserves velocity across batch gaps without a time-array filter overload in the telemetry library. The cached `SavitzkyGolay` instance is keyed on the chosen window size and lives entirely on the flush-loop thread.
- `ImuTimes`, `ImuVibrationRms` — per-`LiveImuLocation` series of times and rolling vibration RMS values, again only for samples appended since the previous flush.
- `FramePitchRollTimes`, `FramePitchDegrees`, `FrameRollDegrees` — frame-only pitch/roll samples for the same flush window. They are empty when frame IMU accelerometer or gyro data is unavailable.

`LiveSessionService` exposes `signalPipeline.SignalBatches` directly through its `SignalBatches` property, so `LiveSessionDetailViewModel` subscribes to the pipeline output without going through the service's snapshot subject.

## Stream Configuration

`LiveDaqStreamConfiguration` (`Sufni.App/Sufni.App/LiveDaq/Services/LiveStreaming/LiveDaqStreamConfiguration.cs`) is the immutable record the configuration-lock holder sets to ask the DAQ for a particular live stream. It carries:

- `RequestedSensorMask` (`LiveSensorInstanceMask`) — which individual sensor instances to start (fork travel, shock travel, frame/fork/rear IMU, GPS).
- `TravelRateMhz`, `ImuRateMhz`, `GpsRateMhz` — per-stream rate caps in millihertz; zero means "do not request this stream" and disables the corresponding bit in the resolved mask. UI-facing `TravelHz`, `ImuHz`, and `GpsFixHz` properties are rounded whole-Hz projections for the rate controls.

`Default` requests Travel + IMU at 200 Hz with no GPS. `FromRequestedRates(travelHz, imuHz, gpsFixHz)` builds a configuration whose mask matches the rates: any rate at zero turns off that bit. `ToStartRequest()` uses the explicit stream mask when supplied; otherwise it derives streams from nonzero rates and intersects the sensor mask with those streams. The app requests travel, IMU, and GPS only; batch duration, extensions, priority, and header-wait flags use protocol defaults.

The shared stream stores one `LiveDaqStreamConfiguration` and exposes it as `RequestedConfiguration`. `ApplyConfigurationAsync(configuration, ct)` tears down the connection, reconnects, and sends a start request derived from the configuration. While `IsConfigurationLocked` is set, it does nothing, because a live-session tab on the same DAQ is recording. The lock blocks changes to the configuration, not the configuration itself. V2 clients round millihertz to whole-Hz wire fields; v3 clients send millihertz directly. See [Configuration Lock](#configuration-lock) for the lease mechanics, and the [Live Wire Protocol](live-streaming.md#live-wire-protocol) for the on-wire shape.

## Presentation Records

`LiveSessionPresentation.cs` defines the records the service publishes for the view model:

- **`LiveSessionPresentationSnapshot`** — what the live-session tab projects. Carries the latest `TelemetryData` from the statistics loop, computed `SessionDampingPercentages`, the projected `TrackPoint[]` for the map, the full `LiveSessionControlState`, a `CaptureRevision` integer the view model uses as a save-already-applied marker, a separate publication `Revision`, and the latest `LiveSessionAnalysis` or typed `AnalysisFailure`.
- **`LiveSignalBatch`** — described under [Live Signal Pipeline](#live-signal-pipeline).
- **`LiveSessionCapturePackage(Context, TelemetryCapture)` with `CaptureRevision`** — the immutable result of `PrepareCaptureForSaveAsync`, prepared inside `LiveDaqCoordinator.SaveCaptureAsync`; its revision is captured under the same lock as its buffers.

**`LiveSessionControlState`** (`LiveSessionControlState.cs`) is the recording state machine in record form. It carries the connection state, last error, accepted `LiveSessionHeader`, capture start UTC, capture duration, and a `CanSave` boolean. The view model reads this directly to drive command enablement, sidebar text, and the save/reset buttons.

## Live Session Detail View Model

The editor receives `TimeProvider` for automatic local-time names and elapsed
capture duration. Automatic names refresh at save time, and authored names stay
unchanged. Capture timestamps come from the recording metadata.

`LiveSessionDetailViewModel` (`Sufni.App/Sufni.App/LiveDaq/ViewModels/Editors/LiveSessionDetailViewModel.cs`) extends `TabPageViewModelBase`, one instance per open live-session tab. It is the only writer of UI state for the tab and the only caller of `LiveDaqCoordinator.SaveCaptureAsync`.

The view model wires together five things and owns no transport state:

1. **Subscribes to `ILiveSessionService.Snapshots`** through `QueuePresentationRefresh`, which stashes the latest snapshot under a gate and lets its injected `IUiTimerScheduler` (`PlotSettings.LiveUiRefreshIntervalMs`) project it into bindings via `ApplyPresentation`. This applies the session header to signals and media workspaces, projects prepared analysis results and surface state, drives the timestamp, retains the device-derived control duration, and queues the spring/damping/balance bake.
2. **Subscribes to `ILiveSessionService.SignalBatches`** through `QueueSignalBatchRefresh`, which collapses presence flags onto the UI thread and calls `LiveSessionSignalsWorkspaceViewModel.ApplySignalDataPresence` so each row's `SurfacePresentationState` reflects whether any travel, IMU vibration RMS, or frame pitch/roll data has arrived. Plot availability is recomputed from the latest accepted `LiveSessionHeader` (travel from `AcceptedTravelHz`, IMU from `AcceptedImuHz` plus active locations, pitch/roll from accepted frame IMU with valid scales) and forwarded into `PreferencesPage.ApplySignalAvailability`.
3. **Hosts a `PreferencesPageViewModel`** alongside the notes/spring/damping pages. `BalancePage` is constructed by the view model but inserted into `Pages` only while balance presentation is available or the layout needs to reserve the page. Travel/Velocity/IMU/Pitch-Roll/Speed/Elevation edits call `ApplyRuntimeSignalPreferences`, which replaces the workspace’s `SignalDisplayPreferences` through `ApplySignalDisplayPreferences(...)` so each row's `SurfacePresentationState` reflects the user's plot selection in real time.
4. **Bakes analysis presentation** — when a fresh `TelemetryData` arrives in a snapshot, `MaybeQueueBake` runs `ISessionPresentationService.BuildCachePresentation(...)` on the background runner and posts the resulting SVGs back onto the spring/damping/balance pages, mirroring the recorded-session bake. Older bakes are cancelled on each new arrival. The editor retains their tasks, rejects stale or inactive UI callbacks, and awaits them on close; rendering failures use safe text with separate details.
5. **Drives the capture lifecycle** — the first `Loaded` calls `liveSessionService.EnsureAttachedAsync()`, `Unloaded` stops foreground updates and cancels the bake, `CloseImplementation` disposes the service and drains retained bakes. `ResetImplementation` calls `ResetCaptureAsync` and clears the analysis pages. `SaveImplementation` is the recording-side save entry point (see [Save Flow](#save-flow)).

The view model never holds the configuration lock itself — that lives on the service's lease — and never subscribes to the shared stream directly.

## GPS Preview State

`GpsPreviewState` (`Sufni.App/Sufni.App/LiveDaq/Services/LiveStreaming/GpsPreviewState.cs`) is a small record interpreting the wire-level `GpsRecord.FixMode` byte. `FromRecord(record)` returns `NoFix` for mode 0 or null, 2D-fix text for mode 1, 3D-fix text for mode 2, and the raw fix-mode number for anything else. It carries only the displayed status text.

It is consumed by the diagnostics tab, where it drives the GPS section's status text in `LiveDaqDetailViewModel`. The live-session media workspace does not consume `GpsPreviewState`; it gates map availability from the accepted session header and then renders projected `TrackPoint[]` values published by the live session service. The capture-side GPS handling that turns `GpsRecord` records into projected `TrackPoint[]` for the live map and the saved track lives in `LiveSessionService.ApplyGpsBatchLocked` (shared `GpsTrackPointProjection.TryProject` / `ProjectAll`).

## Save Flow

The save lifecycle is split between the service (snapshotting capture state under lock) and the coordinator (everything that touches the database).

```
LiveSessionDetailViewModel.SaveImplementation
  -> build immutable SessionSnapshot draft from sidebar and processing preferences
  -> LiveDaqCoordinator.SaveCaptureAsync(draft, liveSessionService, preferences)
       1. PrepareCaptureForSaveAsync: snapshot buffers and revision under lock,
          flatten capture off-thread
       2. source = RecordedSessionSourceFactory.CreateLiveCapture(...)
       3. read setup and bike; draft with content-derived id and capture timestamp
       4. RecordedSessionReprocessor.ReprocessAsync(...) -> ProcessedGeneration
       5. ImportProcessedSessionAsync(session, optional track, source)
       6. ISessionPreferences.UpdateRecordedAsync(sessionId, preferences)
       7. Publish*ChangedAsync on the session, source and track store writers
          in one StorePublication batch on the UI dispatcher
       8. ResetCaptureAsync (awaited even after persistence completes)
       -> Saved(SessionId, CaptureRevision, PublicationWarning?, CleanupFailure?, PreferenceWarning?)
            | Failed(Reason, Details) | ProcessingFailed(Reason, Details)
  -> on Saved without cleanup failure: clear presentation + open recorded editor
  -> on failure: render fixed summary and separate diagnostic details
```

`SaveCaptureAsync` (`LiveDaqCoordinator.CaptureSave.cs`) works as follows:

- It derives the recorded session id from the live source hash. It replaces the temporary draft id without mutating the draft. Live saves have no editor conflict baseline.
- It creates the live-capture source through `RecordedSessionSourceFactory` and asks `IRecordedSessionReprocessor` for the pure generation. The coordinator owns track association and persistence.
- A retry of the same capture uses the same id. It keeps the existing recording, including its authored metadata and processed generation.
- `ImportProcessedSessionAsync` applies the same source-hash/content-id classification as file import. It atomically persists a new session, optional generated full track, and source. On a duplicate, publication uses the retained source snapshot.
- The session, source and track store writers publish in one dispatcher-owned `StorePublication` batch. The stores only publish the committed values.

The recorded source payload stores capture metadata, raw front/rear segments, IMU, temperature and GPS data, markers, stream gaps, final status with sink backlog counters, and missing-final-status flags. The reader also accepts older flat front/rear measurement payloads. The payload does not store `BikeData`, so recorded recompute resolves calibration from the saved session's current setup and bike.

The view model builds `SessionPreferences` from `PreferencesPage` and the per-mode analysis pickers (`CreateCurrentSessionPreferences`). The coordinator writes them through `ISessionPreferences.UpdateRecordedAsync`, so the recorded editor restores the user's plot and analysis choices. After the coordinator resets the capture, the view model clears its presentation and opens the recorded editor through `shell.OpenEditor`. The coordinator has no navigation dependency.

Cancellation before commit propagates neutrally. Before commit, preparation or
persistence failures return `Failed`, and typed processing failures return
`ProcessingFailed`. Neither resets the capture. After commit, preference,
publication and reset failures stay `Saved` outcomes with separate diagnostic
details, and their owner logs each once. Reset runs after commit without caller
cancellation. The view model uses the saved capture revision to block a second
save of unchanged capture state after a cleanup failure. Explicit reset stays
available. The view model navigates only after reset succeeds, and shows fixed
failure summaries with expandable details.

## Design Decisions

1. **Capture under the same lock as transport reads.** Frame handlers append to segment builders or GPS buffers and build display updates in one `lock(gate)` so capture and display see the same sample order. Statistics and save snapshots are taken under the same lock and materialized off-lock, avoiding any need to copy hot data twice or to queue per-sample work.
2. **Bounded display channel with `DropOldest`.** The display loop can lag behind capture without holding the gate. Only the _display_ update for those samples is dropped, and that pressure starts the analysis quiet period. Capture is never dropped.
3. **Sliding window only inside the signal pipeline.** Velocity is a presentation concern (the saved telemetry recomputes velocity from the SG filter inside `TelemetryData.FromLiveCapture`), so the sliding window lives next to the flush loop and the pipeline owns the cached SG instance.
4. **Configuration lock instead of per-tab transport.** A live-session tab and the diagnostics tab on the same DAQ share one connection through the registry. The lock makes the diagnostics tab read-only for the duration of a recording so its rate controls cannot tear down a live capture.
5. **Create-only save path through `LiveDaqCoordinator`.** Live captures persist as plain recorded sessions; the coordinator owns preparation, publication and reset. Post-save the user is moved to the recorded editor so the live tab is free to start the next capture.
6. **`SessionPreferences` round-tripped on save.** The same `PreferencesPage` and analysis pickers used in the recorded editor are forwarded through `SaveCaptureAsync`, so reopening the saved session restores the user's plot/analysis choices.
