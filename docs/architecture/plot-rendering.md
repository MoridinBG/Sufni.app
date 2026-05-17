# Plot Rendering

> Part of the [Sufni.App architecture documentation](../ARCHITECTURE.md). This file covers the ScottPlot-backed plot classes under `Sufni.App/Sufni.App/Plots/` and the display-time pipeline that prepares samples before they reach a plot. The data side that produces those samples lives in [signal processing](processing.md); the workspace view models that compose plots into pages live in [UI](ui.md) and [live streaming](live-streaming.md).

## Table of Contents

- [Layering](#layering)
- [Class Hierarchy](#class-hierarchy)
- [Concrete Plots](#concrete-plots)
- [Display-Time Pipeline](#display-time-pipeline)
- [Cross-Cutting Patterns](#cross-cutting-patterns)

## Layering

Each concrete plot class is a thin adapter over a ScottPlot `Plot`: it owns drawing, axis rules, ticks, axis labels, legends, overlays, readouts, and the typed configuration knobs (`MaximumDisplayHz`, `SmoothingLevel`, `AnalysisRange`, plot-specific modes). It does not own the Avalonia control or the graph-row title. Avalonia plot views in `Sufni.App/Sufni.App/Views/Plots/` and `Sufni.App/Sufni.App/DesktopViews/Plots/` host a `SufniAvaPlot` (a `ScottPlot.Avalonia.AvaPlot` subclass), construct the matching plot class against `PlotControl.Plot`, and forward bindings into it.

For most recorded telemetry rows the host view passes a `TelemetryData` and calls `LoadTelemetryData(...)`. GPS speed/elevation rows are the carve-out: `TrackSignalPlotDesktopView` binds `TrackPoints` plus timeline context and calls `TrackSignalPlot.LoadTrackData(...)` for both recorded and live session surfaces. For live streaming, travel/velocity/IMU plots own ScottPlot `DataStreamer` instances and are fed `LiveGraphBatch` deltas by `LiveGraphPlotDesktopViewBase`.

## Class Hierarchy

```
SufniPlot                      (Plots/SufniPlot.cs)
├── TelemetryPlot              (Plots/TelemetryPlot.cs)
│   ├── RecordedTimeSeriesPlot (Plots/RecordedTimeSeriesPlot.cs)
│   │   └── TravelPlot, VelocityPlot, ImuPlot, TrackSignalPlot
│   ├── TravelHistogramPlot, TravelFrequencyHistogramPlot, DeepTravelHistogramPlot,
│   │   VelocityHistogramPlot, StrokeLengthHistogramPlot, StrokeSpeedHistogramPlot,
│   │   BalancePlot, VibrationThirdsPlot
│   └── LiveStreamingPlotBase  (Plots/LiveStreamingPlotBase.cs)
│       └── LiveTravelPlot, LiveVelocityPlot, LiveImuPlot
└── LeverageRatioPlot          (no telemetry — fed a CoordinateList)
```

`SufniPlot` is the root base. It holds a ScottPlot `Plot` reference, applies the dark theme (figure `#15191C`, data area `#20262B`, grid / axis `#505558`, labels `#D0D0D0`), and provides `Clear()`, `GetSvgXml(...)`, `SetBackgroundColors(...)`, `SetAxisLabels(...)`, `AddLabel(...)`, and `AddLabelWithHorizontalLine(...)`. The background hook is driven by `SufniPlotView.PlotFigureBackground` / `PlotDataBackground`, which lets `TelemetryPlotRow` give base and hosted rows slightly different figure/data-area colors without changing series colors. `AddLabelWithHorizontalLine(...)` routes through a `FixedHorizontalLine` plottable that exists solely as a workaround for ScottPlot issue 4650.

`TelemetryPlot` is the telemetry-styled base shared by recorded and live plot families. It adds the front (`#3288bd`) / rear (`#66c2a5`) color convention, the display knobs `MaximumDisplayHz` / `SmoothingLevel` / `AnalysisRange`, the protected `PrepareDisplaySignal(samples, sampleRate)` helper that runs the [display-time pipeline](#display-time-pipeline), the shared axis-rule classes `LockedVerticalSoftLockedHorizontalRule` / `BoundedZoomRule` and the `FixedAutoScaler`, configuration helpers (`ConfigureRightAxisStyle`, `ConfigureTimeTicks`, `ConfigureSymmetricValueTicks`, `ShowSourceLegend`), and `AddMarkerLines(...)` for the red vertical lines drawn at `TelemetryData.Markers`. `RecordedTimeSeriesPlot` adds the recorded-only span/analysis-range behavior for Travel, Velocity, IMU, and TrackSignal rows. `ZoomFractions.TimeSeries = 0.01` and `ZoomFractions.Statistics = 0.10` are the standardized minimum-span fractions used by the rules.

`LiveStreamingPlotBase` extends `TelemetryPlot` for the three streaming plots. It owns a render sample `Capacity`, a fixed visible-window duration, a configured Y range (`SetVerticalLimits(min, max)`), a `CursorLine`, the `SmoothingLevel` setting, and the time-coordinate translation that powers the timeline link with the recorded plots: as batches arrive, `UpdateTiming(times)` records the latest timestamp; `CoordinateToNormalizedTime(...)`, `SetCursorFromNormalized(...)`, `GetNormalizedVisibleRange()`, and `ApplyVisibleRange(...)` operate in `[0, 1]` over the rolling time window. Concrete subclasses provide one `DataStreamer` per channel via the protected `CreateStreamer(color, legendText)` helper, run a per-channel `TelemetryDisplayStreamingSmoother` over each batch, and recompute auto Y limits from a running max.

## Concrete Plots

`SessionStatisticsPlotView` (`Sufni.App/Sufni.App/DesktopViews/Plots/SessionStatisticsPlotView.cs`) constructs every histogram / balance / vibration plot from a `PlotKind` enum, so the same Avalonia control hosts the whole statistics family. The time-series plots (`TravelPlot`, `VelocityPlot`, `ImuPlot`) and `LeverageRatioPlot` have dedicated views.

| Family   | Class                          | What it draws                                                                                                        |
| -------- | ------------------------------ | -------------------------------------------------------------------------------------------------------------------- |
| Travel   | `TravelPlot`                   | Travel over time per suspension (mm). Airtime spans, marker lines, optional analysis-range and preview-range spans   |
| Travel   | `TravelHistogramPlot`          | Time-percentage distribution across travel bins, separate `ActiveSuspension` / `DynamicSag` modes, statistics labels |
| Travel   | `TravelFrequencyHistogramPlot` | FFT of the travel signal (0–10 Hz), zero-padded to at least 20 seconds for stable frequency resolution, axis ticks formatted in dB |
| Travel   | `DeepTravelHistogramPlot`      | Stroke counts across the deep-travel band (`DeepTravelThresholdRatio`)                                               |
| Velocity | `VelocityPlot`                 | Velocity over time per suspension (m/s); positive = compression, negative = rebound                                  |
| Velocity | `VelocityHistogramPlot`        | Stacked horizontal bars by 10 travel zones, supports `SampleAveraged` / `StrokePeakAveraged` modes                   |
| Track    | `TrackSignalPlot`              | Speed or elevation over time from projected `TrackPoint` data, with marker lines and cursor readout                  |
| Strokes  | `StrokeLengthHistogramPlot`    | Stroke count distribution by length, parametrized on suspension and `BalanceType` (compression/rebound)              |
| Strokes  | `StrokeSpeedHistogramPlot`     | Stroke count distribution by peak speed, same parameter set                                                          |
| Balance  | `BalancePlot`                  | Front vs rear scatter with polynomial trend lines, MSD and slope-delta labels; `Travel` / `Zenith` displacement mode |
| IMU      | `ImuPlot`                      | De-interleaved per-location accelerometer magnitude after gravity removal, plotted over time                         |
| IMU      | `VibrationThirdsPlot`          | Grouped bars (compression / rebound / overall) of lower / middle / upper third vibration percentages                 |
| Leverage | `LeverageRatioPlot`            | Travel-vs-leverage-ratio scatter from the kinematics solver; takes a `Sufni.Kinematics.CoordinateList`               |
| Live     | `LiveTravelPlot`               | Two `DataStreamer`s (front / rear), running-max Y autoscale, streaming smoother per channel                          |
| Live     | `LiveVelocityPlot`             | Same shape as travel; converts mm/s batches to m/s before streaming, symmetric Y around zero                         |
| Live     | `LiveImuPlot`                  | Three streamers (frame orange, fork blue, rear teal), running-max Y autoscale                                        |

`ImuPlot.FrameColor` is published as a static field so `LiveImuPlot` can reuse it. `TravelZonePalette` powers the `VelocityHistogramPlot` 10-zone stack, indices 1 / 4 / 8 of `VibrationThirdsPlot`, and the `TravelPercentageLegend` chip strip.

## Display-Time Pipeline

The pipeline runs for time-series plots (`TravelPlot`, `VelocityPlot`, `ImuPlot`, `TrackSignalPlot`, and the three live plots). Histograms, balance, vibration, and leverage-ratio plots already operate on summarized data and bypass it.

For recorded telemetry arrays, `TelemetryPlot.PrepareDisplaySignal(samples, sampleRate)` is the single entry point. It runs downsampling first, then smoothing, and returns the result plus the time step that ScottPlot needs to render a `Signal`:

```
TelemetryDisplayDownsampling.Prepare(samples, sampleRate, MaximumDisplayHz) → samples', step'
TelemetryDisplaySmoothing.Apply(samples', SmoothingLevel)
```

`TrackSignalPlot` uses the same smoothing duration over projected speed/elevation `TrackPoint` samples, applying it against each point's explicit timestamp. It does not use sample-rate downsampling because those points are already sparse and irregularly timed.

For live data, every batch is smoothed by a per-channel `TelemetryDisplayStreamingSmoother` before being appended to its `DataStreamer`. Live data is not downsampled at this layer — the upstream batch already arrives at the display rate and the render buffer is bounded by `Capacity`.

**Downsampling.** `TelemetryDisplayDownsampling` (`Sufni.App/Sufni.App/Plots/TelemetryDisplayDownsampling.cs`) is plain stride-based decimation. It computes `stride = ceil(sampleRate / maximumDisplayHz)` and writes every `stride`-th sample into a fresh array, scaling the time step by the same stride. It is a no-op when `MaximumDisplayHz` is null, the sample rate is at or below the cap, or the sample rate is unknown. In practice only the mobile recorded graph page sets it — `SessionGraphSettings.RecordedMobileMaximumDisplayHz = 100` — so desktop renders the full sample rate and mobile decimates to 100 Hz to stay responsive. There is no visible-range awareness; decimation runs once at load time and ScottPlot's `Signal` plottable handles further pixel-level decimation while the user pans and zooms.

**Smoothing.** `TelemetryDisplaySmoothing` (`Sufni.App/Sufni.App/Plots/TelemetryDisplaySmoothing.cs`) applies a box-filter moving average over fixed time durations: `Off` -> no-op, `Light` -> 9 ms, `Strong` -> 31 ms. Regular sampled telemetry derives an odd sample window from the displayed sample step; explicit track samples use their X timestamps directly. Recordings with fewer than three samples skip smoothing. The strategy is intentionally simpler than the upstream Savitzky-Golay derivative used inside `Sufni.Telemetry`: this layer is purely cosmetic and never alters the underlying `TelemetryData` or `TrackPoint` data. The level is per-row (Travel / Velocity / IMU / Speed / Elevation) in `SessionPlotPreferences`, persisted alongside the session so each row remembers the user's choice.

**Streaming smoother.** `TelemetryDisplayStreamingSmoother` (same file) is the live equivalent. It maintains an internal timestamped queue plus a running sum. Each `Apply(times, values, ref buffer)` call extends the queue, drops samples older than the smoothing duration, and writes the running average into the caller's scratch buffer (grown geometrically). Changing `Level` resets the internal state — the desktop view base detects that change and also calls `Plot.Reset()` so the streamer redraws from a clean slate at the new smoothing level.

**Travel zone palette.** `TravelZonePalette` (`Sufni.App/Sufni.App/Plots/TravelZonePalette.cs`) is a 10-color Spectral-style ramp (cool blue -> warm red) shared by every plot that needs ordered travel-zone coloring. It is defined as raw hex strings rather than `ScottPlot.Color` values so the Avalonia legend can use it without taking a ScottPlot dependency.

## Cross-Cutting Patterns

**Axis rules.** All time-series plots install a `LockedVerticalSoftLockedHorizontalRule` per visible suspension axis. Y is locked to the data extents (`MaxTravel` for travel, `min/max` velocity, `min/max` magnitude for IMU); X is clamped to `[0, Duration]` with a minimum span of `ZoomFractions.TimeSeries * Duration` (1 %). Histogram plots use `BoundedZoomRule` instead, which clamps both axes to the data bounds and uses `ZoomFractions.Statistics` as the floor (10 %). `FixedAutoScaler` is installed by `TravelFrequencyHistogramPlot` and `VelocityHistogramPlot` so ScottPlot's auto-scale button restores their hardcoded display windows rather than fitting tightly to the data.

**Cursor and axis linking.** Every recorded time-series plot exposes a `VerticalLine? CursorLine` and a `SetCursorPosition(double)` override. The desktop time-series views (`TravelPlotDesktopView`, `VelocityPlotDesktopView`, `ImuPlotDesktopView`, `TrackSignalPlotDesktopView`) wire pointer events to update the cursor on each plot in the row and to publish a normalized cursor position to a shared `SessionTimelineLinkViewModel`. Visible-range linking is owned by that timeline model rather than ScottPlot's `LinkXAxisWith`: recorded plot views apply `Timeline.VisibleRangeStart` / `VisibleRangeEnd` through manual `SetLimitsX(...)`, while live views call `LiveStreamingPlotBase.ApplyVisibleRange(...)`. The travel plot also owns the analysis-range UI: shift-drag draws a `selectedSpan`, plain drag updates a transient `previewSpan`, and clicking a marker line snaps the analysis-range boundary to that marker. The selected range is stored on the recorded graph workspace, forwarded back into every `TelemetryPlot.AnalysisRange`, and consumed by the histogram / balance plots through `TelemetryStatistics.HasStrokeData(...)` filtering.
On mobile recorded-session graphs, long-pressing a plot sets the first analysis-range boundary, the next long press sets the second boundary, and a normal tap clears either the pending first boundary or the completed range.

**Wheel input.** `SufniAvaPlot` owns the boundary between plot zooming and parent page scrolling. Wheel events outside ScottPlot's rendered data area pass through to the surrounding `ScrollViewer`; wheel events inside the data area use ScottPlot's normal zoom behavior. Holding Shift while wheeling over a plot also passes the event through so the page containing all graph rows scrolls vertically. Holding Alt/Option while wheeling over the data area keeps ScottPlot's normal zoom path but temporarily reduces `MouseWheelZoom.ZoomFraction` by 5x for precision zooming.

**Source legends.** Recorded Travel, Velocity, and IMU rows assign each plotted source's `RecordedTimeSeries.Label` as the ScottPlot legend text and show the legend when more than one source is rendered. Live Travel, Velocity, and IMU rows do the same when creating their `DataStreamer`s. Balance plots label only the front/rear trend lines so the legend identifies the fitted sources without duplicating the raw scatter points. Source legends are drawn in the lower-right of the graph with a dark background that is slightly below the data-area color and enough internal padding that the first entry is not clipped. Balance MSD / slope labels are anchored just above that lower-right legend.

**Titles and row backgrounds.** Telemetry time-series graph titles are not drawn by ScottPlot. `TelemetryPlot.ConfigureTimeSeriesFrame(...)` clears the ScottPlot title, and the visible title lives in the containing `TelemetryPlotRow` header (`Travel (mm)`, `Velocity (m/s)`, `IMU acceleration (g)`, `GPS speed (km/h)`, `Elevation (m)`). ScottPlot still owns axis labels, legends, data rendering, readouts, marker lines, analysis spans, and cursor overlays. Base rows use the default figure/data colors (`#15191C` / `#20262B`); hosted rows default to `#171D21` / `#222A30`.

**Statistics readouts.** Histogram and grouped-bar statistics plots register their rendered bars as readout targets while loading telemetry. Balance plots register a single trend readout that samples both front and rear fitted lines at the pointer's x-coordinate, so the tooltip shows the same stroke-travel / zenith coordinate for both sources. `SessionStatisticsPlotView` wires pointer press / move / exit events to `TelemetryPlot.SetPointerPositionWithReadout(...)`, so desktop mouse hover and touch press show the nearest bar or balance trend value. These tooltips follow the pointer, but their estimated label bounds are clamped inside ScottPlot's data rectangle, not the surrounding axes/title chrome.

**Recorded vs live.** The two paths share styling and the smoothing strategy, but diverge in everything else. Most recorded plots receive a finished `TelemetryData` and call `Plot.Add.Signal(...)` once per `LoadTelemetryData`; GPS speed/elevation rows receive projected `TrackPoint` lists through `LoadTrackData(...)`. Live travel/velocity/IMU plots register one `DataStreamer` per channel up-front and append batches to it. Recorded plots draw seconds directly on the X axis, bounded by `Duration`; live plots draw a fixed `[0, Capacity]` axis and rewrite tick labels via a `LabelFormatter` that maps coordinate -> seconds against a fixed rolling duration (2048 ms for travel/velocity, 1024 ms for IMU). Recorded plots install a Y-locked axis rule based on the loaded extents; live plots track a running max as samples arrive and call `SetVerticalLimits(...)` on every batch with a small floor and 10 % padding. Markers, airtime overlays, and analysis-range spans are recorded-only. Downsampling is recorded-only — and only when the host view sets `MaximumDisplayHz` (currently mobile alone). `LiveStreamingPlotBase.Reset()` clears every streamer (`Clear(double.NaN)`), drops timing state, and restores the initial X / Y limits; the desktop view base treats an empty batch as a reset signal and calls `Reset()` so a fresh capture starts from a clean state.

The workspace view models expose the presentation states, graph-row preferences, and data bindings used by the graph rows, while the view layer composes the reusable `TelemetryPlotsRoot` hierarchy for recorded and live editors. Row hierarchy drag/drop is handled by the Avalonia root/row controls and is persisted through `SessionPreferences.Graph`; plot models still only receive data, axis, smoothing, range, and styling inputs. See [UI Architecture](ui.md) for the row composition and [Live DAQ Streaming](live-streaming.md) for the live transport, capture, and save lifecycle.
