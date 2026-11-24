# Terrain profiles

A profile describes moving ten-metre windows of one recorded session. The telemetry library derives suspension figures, roughness, grade and matching signals from processed telemetry and original GPS fixes. Roughness and grade report their availability independently. The calculator and calibration table each have a version.

A failed sensor reads zero. When a suspension or IMU sensor reads zero in more than half of its measured moving windows, the calculator drops all its readings for the ride. An IMU level below one count counts as zero, because the band-pass filter leaves a small residue next to live samples. A dropped IMU yields no matching series, scale or class evidence, and a dropped frame IMU also removes grade. The profile lists the dropped sensors, and the terrain source note names them.

## Materialization

`TerrainProfileCoordinator` owns the device-local profile. At startup and after session or track publications, it evaluates pending sessions on the background runner and processes one recording at a time, newest first. An editor can prioritize a queued recording. A changed input supersedes the run. After a written commit, the coordinator publishes the committed key, outcome and summary onto the current session snapshot. If the inputs changed meanwhile, the status query reports `Pending` for that snapshot. A publication failure is logged and the queue continues. Shutdown cancels and drains planning and calculation.

The content token identifies the processed telemetry revision, selected full track and points revision, aligned recording window and track projection version. `TerrainProfileKey` adds both terrain versions. The exact reader and the SQLite write transaction use the same token rule as the status query. A computed row contains a summary and MessagePack profile. A terminal outcome contains only its key and reason.

## Availability

`SessionTerrainStatusQuery` derives the list and editor status from the session snapshot and track catalog. A session without processed telemetry is unavailable. A new key is pending. With matching inputs, the status exposes the current summary or the stored outcome. A recording with no selected track reaches `NoTrack` without a telemetry read. Missing original track fixes yield `ContentMissing`. A stale or missing telemetry read means the run is superseded, so it writes nothing.

A calculation failure is recorded for its input key. If the database write itself fails, the coordinator logs it once and publishes `Failed` for that key without a stored row. The next publication of the session from the database, or the next app start, runs it again. Authored metadata and sync clocks are independent of profile materialization.

## Recorded editor

The editor reads the exact profile on its background runner and derives timed runs for both axes, segmented sensor, grade and pedal bob signals, and distance shares for jointly classified, frame-only and lowers-only windows. The reducer accepts a loaded result only for the current domain status. A change of status immediately removes the previous plot and map data.

The source label identifies both sensors when both contributed anywhere in the ride. The tooltip distinguishes separate contributions from joint classification and identifies clipping, missing samples or an uncalibrated scale. The grade trace uses the same five-window baseline as the grade bands.

## Analysis selection

The analysis scope is a preset narrowed by legend classes. Descents admits every non-climb window, including windows without grade. Climbs admits the two climb bands, and All admits every window. A legend class outside the preset cannot be selected, and an empty axis admits every class on that axis. Every preset uses moving windows only, so stopped time never enters the analysis. `TerrainSelectionQuery` intersects matching windows with the analysis range, joins adjacent intervals, and excludes intervals shorter than the minimum analysis duration. A partial window contributes its mean speed times the covered duration. Legend distances describe the corresponding class inside the analysis range. The result also reports the stopped time inside the range.

The editor keeps one scope choice per tab: the default, a chosen terrain scope or a segment comparison. The default is derived from the loaded profile: Descents when the profile has grade, otherwise All. No observer writes a scope when a profile arrives. A choice equal to the profile's default is stored as the default. The reducer evaluates the selection again only when the profile, the effective scope or the analysis range changes. With a loaded profile, the effective time selection is the scoped selection; without one, it is the analysis range. A segment comparison uses the analysis range. Analysis results are keyed on that selection, not on the scope, so statistics and Insights share one value. Plot overlays mark the selection of a chosen scope only.

## Bike response baseline

The baseline identity is the ordered set of current profile keys of other rides on the same bike. The editor observes summary commits throughout the tab lifetime and selects the identity synchronously on each commit. A changed identity invalidates the previous model. Exact profile reads, log-linear least-squares fitting and response measurements run on the background runner. A superseded fit cannot publish. A baseline read error is local to the response panel.

Climbing windows stay out of the fit and out of every response row, because pedalling and rider load drive the response there. Each target needs five compatible rides, 1,000 moving windows with positive observations, and independent variation in the log band levels and speed. Front targets prefer the lowers sensor and rear targets the frame. The alternative is used only if the preferred sensor is absent on the evaluated ride or throughout the baseline. Transmission uses the lowers bands to predict the frame level. Each model carries its predictor, coverage and availability.

Insights measures the whole ride and each descent level inside the analysis time range, independently of the scope. `TerrainResponse.Describe` also accepts a window filter, so a caller can limit every row to the windows of a scope. Boundary-window distance and log residuals are weighted by the covered fraction. Ratios use the geometric mean of observed over predicted response. A row reports ratios from 150 m, and the presentation marks distances under 300 m as low confidence. Classification uses the bike-baseline repeat thresholds of 30% for suspension response and 10% for transmission. `TerrainResponse.Findings` lists every cell outside its repeat range as a typed finding: target, group, signed deviation and confidence. The confidence is low under 300 m. The app turns findings into verdicts and rows into a deviation chart in `Terrain/Formatting/TerrainResponseReports`; views build no sentences.

## Comfort

`TerrainComfort` describes, per roughness level, the moving ground inside a range that a window filter admits. Each row gives the distance, the fork and shock activity (RMS velocity, mm/s) and the frame level (band-limited vibration, g). The figures are as measured, so faster riding raises them. Each figure is the quadratic mean of its windows, weighted by covered time, because the window figures are RMS values. A missing sensor leaves its figure empty.

## Climbing

`TerrainClimbing` describes the moving climb windows inside the analysis range, with covered fractions. It reports distance, time, the net height gained from the window grades, the time with detected pedalling, and the split into smooth and technical climb levels. Bob and cadence are time-weighted medians over the pedalled climb windows only, because travel at the pedal frequency on rough climbs is ground input. Mean travel per end compares climb windows with the descent bands; flat ground stays out. Below 120 s of pedalled climbing there is no summary.

## Character (experimental)

`TerrainCharacter` reports, per end, the moving descent ground of level Medium or rougher inside the analysis range that has a median compression stroke size. It splits that distance into big hits and chatter by the stroke size as a share of the end's max travel: rear big hits from 16% and chatter below 8%, front big hits from 20% and chatter below 9%. These are one bike's quartiles on rough ground and stay provisional. A share is dominant when it covers more than half of at least 300 m. Insights shows the dominant shares as an experimental hint in the response panel.

## Same-trail comparison

Ground alignment refines the GPS time map with a shared frame IMU, or a shared lowers IMU when frame data is absent. `TerrainRunAlignment` cuts the source range into 150 m chunks every 75 m. A chunk is a candidate when both runs have matching values for 80% of its metres. Each candidate searches 40 m to either side and is accepted when its best correlation is at least 0.6 below the search limit. An accepted chunk is dropped when its shift differs by more than 5 m (`NeighbourShiftToleranceMetres`) from every neighbouring accepted chunk. The pair aligns only when at least half of the candidates are accepted (`MinimumAcceptedShare`, counted before the drop) and two chunks remain. Unrelated ground correlates by chance in a few chunks, so the share rule, not the chunk count, rejects it. Only moving windows within 75 m of a remaining centre enter the comparison, at the shift interpolated between remaining centres.

The pair fit excludes both compared rides and requires compatible predictor scales. Rows cover all matched windows and each roughness class. A row reports pace and ratios from 150 m, with the same low-confidence mark under 300 m. Pace reports the median and interquartile range. Suspension and transmission compare each ride’s log residual on jointly measured windows. The same-position RMS ratios use the observed speed ratio to the power 0.9 and remain available without a baseline. Repeat limits are 20% for RMS, compression and same-position response, 25% for rebound and 5% for transmission. `TerrainRunComparison.Findings` returns the same typed findings for the response ratios.

## Known-trail average

Each source window receives at most one measurement from each aligned run. Measured frame and lowers levels use geometric means of at least three readings. Grade uses an arithmetic mean and the same contiguous 50 m baseline as a single ride. A climbing window takes its climb level from the geometric mean of at least three calibrated frame levels. A stopped source window stays in the average without values or classes, so the baseline reaches past it.

Classification requires three eligible readings for each contributing sensor. Each reading uses its own scale calibration. A ride with frame data excludes its lowers class evidence when clipping exceeds 2%. Uncalibrated and clipped readings can still contribute to the measured levels. When both sensors qualify, their mean normalized values carry equal weight. Averaged bands and levels merge single-window outliers as a single ride does. Without an average, each matched pair takes the level of the pair's mean normalised value, and outliers merge along the source ride. A comparison using the average keeps windows without an averaged class in its all-terrain row only. Average values live for the current Match in the recorded editor.
