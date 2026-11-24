# Terrain profiles

A profile describes moving ten-metre windows of one recorded session. The telemetry library derives suspension figures, roughness, steepness and matching signals from processed telemetry and original GPS fixes. Roughness and steepness report their availability independently. The calculator and calibration table each have a version.

## Materialization

`TerrainProfileCoordinator` owns the device-local profile. At startup and after session or track publications, it evaluates pending sessions on the background runner and processes one recording at a time, newest first. An editor can prioritize a queued recording. A changed input supersedes the run. Shutdown cancels and drains planning and calculation.

The content token identifies the processed telemetry revision, selected full track and points revision, aligned recording window and track projection version. `TerrainProfileKey` adds both terrain versions. The exact reader and the SQLite write transaction use the same token rule as the status query. A computed row contains a summary and MessagePack profile. A terminal outcome contains only its key and reason.

## Availability

`SessionTerrainStatusQuery` derives the list and editor status from the session snapshot and track catalog. A session without processed telemetry is unavailable. A new key is pending. With matching inputs, the status exposes the current summary or the stored outcome. A recording with no selected track reaches `NoTrack` without a telemetry read. A missing exact payload yields `ContentMissing`. A stale read yields no write.

A calculation failure is recorded for its input key. If the database write itself fails, the coordinator publishes the failed status for the current app run. Reopening the database allows backfill to retry. Authored metadata and sync clocks are independent of profile materialization.

## Recorded editor

The editor reads the exact profile on its background runner and derives timed runs for both axes, segmented sensor and grade signals, and distance shares for jointly classified, frame-only and lowers-only windows. The reducer accepts a loaded result only for the current domain status. A change of status immediately removes the previous plot and map data.

The source label identifies both sensors when both contributed anywhere in the ride. The tooltip distinguishes separate contributions from joint classification and identifies clipping, missing samples or an uncalibrated scale. The grade trace uses the same five-window baseline as steepness classification.

## Analysis selection

The class filter matches moving windows on both axes. An empty axis admits every class on that axis. `TerrainSelectionQuery` intersects matching windows with the analysis range, joins adjacent intervals, and excludes intervals shorter than the minimum analysis duration. A partial window contributes its mean speed times the covered duration. Chip distances describe the corresponding class inside the analysis range.

The editor analysis demand captures one lazy selection for the profile, scope and range. Workers share that result for statistics, Insights and the availability projection. Plot overlays and banner values follow the accepted result. The filter is local to the open tab.

## Bike response baseline

The baseline identity is the ordered set of current profile keys of other rides on the same bike. The editor observes summary commits throughout the tab lifetime. A changed identity invalidates the previous model. Exact profile reads, log-linear least-squares fitting and response measurements run on the background runner. A superseded fit cannot publish. A baseline read error is local to the response panel.

Each target needs five compatible rides, 1,000 moving windows with positive observations, and independent variation in the log band levels and speed. Front targets prefer the lowers sensor and rear targets the frame. The alternative is used only if the preferred sensor is absent on the evaluated ride or throughout the baseline. Transmission uses the lowers bands to predict the frame level. Each model carries its predictor, coverage and availability.

Insights measures the whole ride and each roughness class inside the analysis time range, independently of the class filter. Boundary-window distance and log residuals are weighted by the covered fraction. Ratios use the geometric mean of observed over predicted response. Classification needs 300 m and uses the bike-baseline repeat thresholds of 30% for suspension response and 10% for transmission.

## Same-trail comparison

Ground alignment refines the GPS time map with a shared frame IMU, or a shared lowers IMU when frame data is absent. Each 150 m chunk searches 40 m to either side. At least two chunks must meet coverage and correlation requirements. Only moving windows within 75 m of an accepted centre enter the comparison.

The pair fit excludes both compared rides and requires compatible predictor scales. Rows cover all matched windows and each roughness class with at least 300 m. Pace reports the median and interquartile range. Suspension and transmission compare each ride’s log residual on jointly measured windows. The same-position RMS ratios use the observed speed ratio to the power 0.9 and remain available without a baseline. Repeat limits are 20% for RMS, compression and same-position response, 25% for rebound and 5% for transmission.

## Known-trail average

Each source window receives at most one measurement from each aligned run. Measured frame and lowers levels use geometric means of at least three readings. Grade uses an arithmetic mean and the same contiguous 50 m baseline as a single ride. A stopped source window stays in the average without values or classes, so the baseline reaches past it.

Classification requires three eligible readings for each contributing sensor. Each reading uses its own scale calibration. A ride with frame data excludes its lowers class evidence when clipping exceeds 2%. Uncalibrated and clipped readings can still contribute to the measured levels. When both sensors qualify, their mean normalized values carry equal weight. A comparison using the average keeps windows without an averaged class in its all-terrain row only. Average values live for the current Match in the recorded editor.
