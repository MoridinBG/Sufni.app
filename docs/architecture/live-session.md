# Live Session Recording

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers in-memory live capture, analysis, presentation and save. Discovery, transport and the diagnostics tab are described in [Live DAQ Streaming](live-streaming.md).

## Contents

- [Overview](#overview)
- [Data Flow](#data-flow)
- [Configuration Lock](#configuration-lock)
- [Capture Coordinator](#capture-coordinator)
- [Capture Stores](#capture-stores)
- [Live Signal Pipeline](#live-signal-pipeline)
- [Analysis Scheduling](#analysis-scheduling)
- [Stream Configuration](#stream-configuration)
- [Presentation Records](#presentation-records)
- [State Publication](#state-publication)
- [Live Session Detail View Model](#live-session-detail-view-model)
- [GPS Preview State](#gps-preview-state)
- [Save Flow](#save-flow)
- [Design Decisions](#design-decisions)

## Overview

The shell opens `EditorKey.LiveSession(identityKey)` and deduplicates it before
`EditorFactory` creates the tab. `ILiveCaptureCoordinatorFactory` creates one
`LiveCaptureCoordinator` per tab, using its `LiveDaqSessionContext`, the shared
stream, presentation service, background runner, signal pipeline, clock and UI
dispatcher. The coordinator and its parts live in `LiveDaq/Coordinators/Capture/`.

The shared stream owns connection, parsing, frame delivery and disconnect.
`LiveCaptureCoordinator` is the only writer of the capture workflow. The separate
`LiveDaqCoordinator.SaveCaptureAsync` owns recording persistence and post-save
reset. The editor requests navigation through the shell.

Capture and uncommitted edits end with the tab or process; `LiveCaptureState`
documents that lifetime. They have no durable checkpoint. Desktop shutdown uses
the tab's save/discard/cancel policy and drains its work. Mobile backgrounding
retains the tab in memory but does not persist an unfinished capture.

## Data Flow

```text
Shared stream frames and state
  -> LiveCaptureCoordinator (one gate)
       -> LiveCaptureRecorder: raw capture and accepted sample times
       -> LiveTrackProjector: immutable-by-publication GPS points
       -> LiveDisplayProjector: calibrated travel, vibration and attitude
  -> LiveDisplayFeed: bounded queue -> LiveSignalPipeline(epoch, samples)
       -> flush loop: Reset, then Signals -> live plots
  -> LiveAnalysisScheduler: immutable job -> background computation
       -> coordinator accepts a keyed result or failure
  -> LiveCaptureStatePublisher: urgent post or foreground cadence
       -> UI-thread LiveCaptureState
            -> editor controls and draft status
            -> signals and media workspaces
            -> analysis workspace and page presentation applier
```

Analysis reaches the editor only through capture state. Signal batches carry
signals and explicit reset markers, never analysis.

## Configuration Lock

The capture coordinator acquires an observer lease and a configuration-lock
lease from `ILiveDaqSharedStream`. The lock lasts for the tab lifetime. Diagnostics
can attach for read-only viewing, but cannot change stream rates or disconnect a
capture. A direct stop returns `LiveDisconnectResult.Locked`.

`AttachAsync` is idempotent. Under the gate it acquires leases, starts the display
feed, and subscribes to frames before state. It then applies the current shared
state and connects only in the active phase. Closed lease acquisition releases
any acquired observer and returns the typed closed outcome. Busy/unavailable
start outcomes return to the editor. An unexpected exception releases the
subscriptions and leases acquired by that attachment.

## Capture Coordinator

The coordinator owns phase, accepted header, capture revision, reset generation,
state revision, analysis options, the settled analysis key, accepted analysis,
analysis failure, leases and subscriptions.

| Part | Responsibility | Execution |
| --- | --- | --- |
| `LiveCaptureRecorder` | Travel/IMU segments, GPS and temperature buffers, markers, gaps, final status, capture origin, duration, save eligibility and `LiveCaptureContent`. | Coordinator gate only. |
| `LiveCaptureMaterializer` | Pure conversion of captured content and `BikeData` to `LiveTelemetryCapture`, including canonical IMU segments. | Background runner. |
| `LiveTrackProjector` | Incremental GPS projection, first-point speed enrichment, full reprojection after out-of-order input. | Coordinator gate only. |
| `LiveDisplayProjector` | Travel calibration/clamping, vibration RMS and frame pitch/roll through `LiveImuDisplaySignalProcessor`; pitch/roll presence since reset. | Coordinator gate only. |
| `LiveDisplayFeed` | Bounded display queue, queue pressure and feed-loop/pipeline lifetime. | Its own queue lock and worker. |
| `LiveAnalysisScheduler` | One in-flight immutable job, cadence, pressure quiet period, pending wake-up timer and calculation failure logging. | Its own lock and worker; callbacks run outside that lock. |
| `LiveCaptureStatePublisher` | `Current`, last published revision, one pending post, subscriber-owned cadence and completion. | UI dispatcher; scheduling fields have their own lock. |

The parts have no independent authority over capture phase or revision. The
coordinator may take a worker/pipeline lock while holding its gate. Workers never
call into the coordinator while holding their locks.

### Phase and frames

Public phases are `Active`, `AwaitingReset` and `Closed`. Disposal is an internal
terminal lifecycle phase. Only active capture accepts frames, and identity-bearing
frames must match the accepted session ID.

Travel and IMU append to raw segment builders before display projection. Invalid
source validity and missing indices remain gaps. Temperature, GPS, manual markers
and final status are retained separately. Each accepted capture change advances
both capture and state revisions, including final status. Control duration and
save identity therefore refresh independently of analysis or display pressure.

A new firmware session while raw data exists enters `AwaitingReset`, retaining
the prior header and capture for save. The coordinator stores the phase, not a
rollover message. The editor derives that message. A terminal stream closure
enters `Closed` and freezes frame acceptance. An analysis already in flight at
rollover remains eligible because the retained capture and generation did not
change.

### Reset and disposal

`ResetAsync` clears raw capture, track/display projection, analysis, failure and
the settled key. It increments capture revision, state revision and reset
generation. Under the same gate it calls `LiveSignalPipeline.BeginEpoch` before
any new-generation display update can exist. Outside the gate it resets scheduler
cadence/pressure and requests urgent publication.

After rollover, reset adopts the shared stream's current header and resumes only
if that stream is open and has a header. A terminal closure cannot be reopened by
reset. Signal presence resets even when the firmware session ID is unchanged.

Disposal rejects new work, disposes subscriptions, cancels/drains display and
analysis work, releases the configuration lock then observer lease, disposes the
pipeline, and awaits UI-thread state completion after pending publication. The
editor separately cancels and drains its page bakes.

## Capture Stores

`LiveCaptureRecorder` owns one `FixedRateSegmentBuilder<ushort>` per travel side
and one `FixedRateSegmentBuilder<ImuRecord>` per active IMU location. These are the
same segment builders used by SST v5 parsing. Captured runs retain first sample
indices and session-relative device timestamps. Invalid validity ranges and
first-index discontinuities produce `RawStreamGap` records.

GPS uses `AppendOnlyChunkBuffer<GpsRecord>` (256 records per chunk); temperature
uses its own buffer (64 samples per chunk). Snapshotting takes sealed chunk
references and the active boundary under the gate. The materializer flattens
those captured ranges off-thread. Saved IMU uses canonical per-location segments,
without a duplicate dense record graph.

Capture starts at the first accepted batch's device-monotonic timestamp. UTC is
derived relative to the session header. Duration uses final stop status first,
then the latest valid travel end, then the latest GPS batch device time. GPS fix
wall-clock timestamps do not drive duration. A no-fix GPS batch still advances
it. IMU-only duration remains zero without another duration source.

Save eligibility requires at least five captured samples on either travel side.
Display queues never determine whether raw samples survive for save.

## Live Signal Pipeline

`LiveDisplayFeed` has an eight-entry `DropOldest` channel. Dropping a display
update reports pressure to the analysis scheduler; it does not remove captured
samples or publish capture state. The feed passes each update's reset generation
to the pipeline and owns its lifetime. It holds no independent display epoch.

`LiveSignalPipeline` checks generation and appends under the same lock.
`BeginEpoch(generation)` changes that epoch, clears pending samples and the
velocity window, and marks a reset pending. It emits nothing.

Only the flush loop emits data. On each `PeriodicTimer` tick it atomically takes
the pending reset, pending samples, velocity context and revisions. Outside the
lock it emits the captured `Reset` marker before that input's `Signals` batch.
A reset during computation or delivery affects the next flush input. Old samples
may complete delivery before the next reset, but cannot follow that reset. New
samples cannot precede their generation's reset or be erased by an older flush.
`LiveSignalPlotViewBase` uses `LiveSignalBatch.Kind`, not empty-array inference.

Signal batches contain travel, velocity, per-location IMU vibration RMS and
frame pitch/roll arrays. Velocity uses recent travel context bounded to 127 ms,
retaining at least five samples. Its Savitzky-Golay window targets 51 ms, rounds to
an odd sample count, and yields NaN for insufficient or invalid context. Local
mean timestamp spacing preserves velocity across batch gaps. The cached filter
belongs only to the flush loop. A failed flush logs once and discards its detached
input; later input continues.

The pipeline cadence is `PlotSettings.LiveSignalRefreshIntervalMs` (33 ms) using
injected time. Reset delivery may therefore wait one flush interval. The editor
clears its analysis pages and timeline immediately on explicit reset.

## Analysis Scheduling

No analysis runs until the editor supplies `LiveAnalysisOptions`. The analysis
workspace supplies initial options before attachment.

A `LiveAnalysisKey` contains reset generation, capture revision and options. The
coordinator derives the current key from its facts. A settled success/failure key
prevents repeating unchanged work; a retained accepted analysis with that key is
also already satisfied. The scheduler holds the immutable job while it runs.

Capture changes, option changes, pressure reports and completion wake the worker.
Wake-ups coalesce. The worker enforces a 1500-ms cadence and a 500-ms quiet period
after client-drop increases or display pressure. A delayed demand retains one
clock-driven wake-up timer, so it runs when eligible even if no further frame
arrives. Reset clears cadence and pressure. Work cannot overlap.

The background runner materializes capture, computes `TelemetryData`, damping
percentages and the selected-mode keyed plot results. The coordinator accepts a
completion only when its reset generation and options still match and its
capture revision is not older than the settled result. This permits progressive
analysis while recording grows and rejects pre-reset or superseded-mode results.
A rejected completion settles nothing, so returning to a rejected mode can run it
again without another frame. Current failures settle their inputs rather than
looping. They preserve earlier valid analysis and carry reason/details only.

`LiveSessionAnalysis` owns its key, telemetry, analysis inputs, percentages and
frozen keyed results. Readiness and user-facing failure text are presentation
concerns. The worker logs a calculation failure once with the DAQ identity.

## Stream Configuration

`LiveDaqStreamConfiguration` carries the requested sensor mask and travel, IMU
and GPS rate caps in millihertz. Zero disables the corresponding stream. Default
requests travel and IMU at 200 Hz, without GPS. V2 rounds rates to whole-Hz wire
fields; v3 sends millihertz directly. `ApplyConfigurationAsync` reconnects with
new parameters only when no capture configuration lock exists. See the
[live protocol](live-streaming.md#live-wire-protocol).

## Presentation Records

`ILiveCaptureCoordinator` exposes `States`, UI-thread `Current`, pipeline-thread
`SignalBatches`, `AttachAsync`, `UpdateAnalysisOptions`, `ResetAsync`,
`PrepareForSaveAsync` and asynchronous disposal.

`LiveCaptureState` contains state/capture revisions, `LiveCaptureControls`, track
points, signal presence, accepted analysis and analysis failure. Controls contain
connection state, stream error, capture phase, accepted header, capture start,
duration and save eligibility. They contain no labels. Presence derives from raw
travel/IMU samples and whether the display projector produced frame pitch/roll.

`LiveSessionCapturePackage(Context, TelemetryCapture)` carries the capture
revision taken with its content under the gate. It is the save-preparation result.

## State Publication

Urgent changes are phase, connection/error, header, save eligibility, signal
presence, accepted analysis/failure and reset. They request one UI dispatcher
post. Continuous changes (capture revision, duration/start and track points)
reach the foreground at `PlotSettings.LiveUiRefreshIntervalMs` (33 ms).

Only the first `States` subscription starts that cadence; the last unsubscribe
stops it. Subscribers receive `Current` immediately. Urgent changes still update
`Current` without subscribers. Continuous changes then wait for an urgent change
or the first cadence after resubscription.

A posted publication clears the pending flag before building a small state under
the coordinator gate. It reads current facts, updates `Current` and emits outside
the gate only when the revision is newer. A subscriber-triggered urgent change
queues another action; notifications do not reenter each other. Completion waits
for any pending publication and occurs on the same dispatcher.

## Live Session Detail View Model

`LiveSessionDetailViewModel` retains the draft, save/reset outcomes, controls,
labels, feedback, timestamp, preference availability and tab lifecycle. While
loaded and active it holds one foreground `States` subscription. It has no
publication timer, pending-state lock or signal-batch presence subscription.

It projects each state in this order: controls/draft status; signals and media;
analysis workspace; page presentation. Deactivation/unload disposes the state
subscription and cancels page baking. Capture itself continues for the tab.

| Part | Presentation responsibility |
| --- | --- |
| `LiveSessionAnalysisWorkspaceViewModel` | Mode selections, insights profile, the last received immutable analysis reference, result source, derived readiness, percentages and damping cutoff intent. |
| `LivePagePresentationApplier` | Background SVG baking, cancellation and retained tasks, spring/damping/balance page projection, balance-page membership and rendering feedback. |
| `LiveSessionShellMobileWorkspaceViewModel` | Selected page/index, clamping after collection changes, page count and display name. Screen state is ready and operation state hidden. |
| `SessionSidebarWorkspaceViewModel` | Reused draft name, description, notes/settings, preferences and save/reset command bindings. |
| `LiveSessionSignalsWorkspaceViewModel` | Header, track and exact signal presence from state; preferences and source visibility. It does not derive presence from batches. |
| `LiveSessionMediaWorkspaceViewModel` | Header-gated map readiness and projected track points. |

The analysis workspace exposes keyed results and damping percentages only while
their options match current selections. It reconciles immediately before forwarding
changed options. Telemetry remains available from the latest received analysis,
independently of that match: cutoff previews and mode changes keep the interactive
surfaces and baked pages present while results refresh. Surface readiness derives
from that telemetry, the header and calibration presence. Reset clears telemetry
when capture state clears its analysis. Switching A→B→A restores retained A results
without waiting for a state emission.

Cutoffs come from the current bike in `IBikeStore`, observed for the tab lifetime.
Preview/cancel/commit use `DampingCutoffWorkflow`, shared with the recorded editor,
which now receives a current-bike lookup. A bike change elsewhere updates both
cutoff views; missing bikes disable editing. Conflicts restore current store
values and use the same feedback policy as recorded editing. Late commit
completions after tab close cannot change presentation or report errors.
`LiveDaqSessionContext` no longer copies damping cutoffs.

The page applier bakes only while the tab is active and loaded with known
dimensions. New telemetry cancels the prior bake; plot-cutoff changes invalidate
its retained bake. Posted results reject canceled, superseded and inactive work.
Rendering failures are logged once per attempt and shown with separate details.
Close cancels and drains retained tasks before releasing the tab's parts.

## GPS Preview State

Diagnostics uses `GpsPreviewState` to interpret the fix-mode byte. The recording
side retains raw GPS and uses `LiveTrackProjector` instead. Incremental projection
uses `GpsTrackPointProjection.TryProject`; an out-of-order record reprojects the
entire raw buffer, including the remainder of the incoming batch. First-point
speed enrichment replaces that point and never mutates earlier published points.
The media/signals workspaces receive the resulting array through capture state.

## Save Flow

```text
Editor -> immutable draft + session preferences
  -> LiveDaqCoordinator.SaveCaptureAsync
       -> capture coordinator.PrepareForSaveAsync
            -> snapshot content and revision under gate
            -> LiveCaptureMaterializer on background runner
       -> RecordedSessionSourceFactory.CreateLiveCapture
       -> resolve current setup and bike
       -> RecordedSessionReprocessor -> processed generation + GPS points
       -> ImportProcessedSessionAsync(session, source, optional generated track)
       -> preferences write and coherent store publication
       -> capture coordinator.ResetAsync
  -> editor handles typed outcome and opens the recorded editor
```

The source hash determines the recording ID. Retrying the same capture keeps an
existing recording and its authored metadata/generation. Session, source and
optional generated track commit atomically; store writers publish committed
values in one UI-thread `StorePublication` batch. The coordinator never navigates.

The source stores metadata, raw segments, IMU, temperature/GPS, markers, stream
gaps, final status/backlog and missing-final-status flags. It does not serialize
`BikeData`; recompute resolves current calibration.

Cancellation before commit is neutral. Preparation/persistence and processing
failures retain capture. After commit, preference, publication and reset failures
remain `Saved` with separate warning/details. Reset runs without caller
cancellation. After cleanup failure, the editor blocks saving the same capture
revision again while leaving reset available. Successful cleanup clears pages
and opens the recorded editor. Automatic names refresh at save time; authored
names remain unchanged. Analysis selections and signal preferences/layout are
saved for the recorded editor.

## Design Decisions

- One coordinator owns capture facts and revision. Workers return values; only
  the coordinator accepts them and requests publication.
- Display is lossy and bounded; raw capture is authoritative and complete for
  received frames. Reset order belongs to the pipeline's epoch and single emitter.
- UI state publication belongs to the capture owner. The editor projects one
  coherent state and owns presentation-only workspaces.
- Calibration and bike association still come from the tab-opening context.
  Mid-capture calibration/setup reassociation is outside this split, while save
  resolves current persisted dependencies. IMU-only duration is also unchanged.
