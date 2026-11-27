# Terrain profiles

A profile describes moving ten-metre windows of one recorded session. The telemetry library derives suspension figures, IMU readings, grade and matching signals from processed telemetry and original GPS fixes. The profile stores measurements only; it holds no roughness classes. Each window can hold a frame, a fork and a rear axle IMU reading: the raw 1–80 Hz level, three band levels, the speed-removed level and the clipped share. Speed removal scales a level to 7 m/s with the exponent 0.7 for the frame and 0.9 for the fork and the rear axle. A reading is clipped when more than 2% of its window samples are at full scale. See [Measured basis](#measured-basis). `TerrainProfile.HasImu` is true when the ride recorded any IMU. Grade reports its own availability. `TerrainProfileCalculator.AlgorithmVersion` versions the calculator.

A failed sensor reads zero. When a suspension or IMU sensor reads zero in more than half of its measured moving windows, the calculator drops all its readings for the ride. An IMU level below one count counts as zero, because the band-pass filter leaves a small residue next to live samples. A dropped IMU yields no matching series, scale or distribution, and a dropped frame IMU also removes grade. The rear axle IMU produces no matching series. The profile lists the dropped sensors, and the terrain source note names them.

The measurement summary holds the moving and climb distances and four level distributions: the speed-removed levels of the frame, fork and rear axle IMUs on descent windows and the raw frame level on climb windows. A distribution adds 10 m per eligible window to the `TerrainLevels.Bin` of its level. An eligible reading is moving, not silent, above zero and not clipped; `TerrainProfileCalculator.Eligible` holds the level and clipped tests, and the label summary and the known-trail average use it also. `LevelDistribution` has value equality over its bins.

## Grade

Grade comes from the X and Z acceleration of the frame IMU in g. The frame IMU needs a sample rate of at least 200 Hz, an active sensor and a scale above 0. The calculator subtracts the longitudinal GPS acceleration, divided by 9.80665, from X. `TrackMotion` gives this acceleration: the central difference of the speed on a 10 Hz grid, low-passed at 0.4 Hz. X and Z pass a zero-phase second-order Butterworth low-pass at 0.08 Hz (`GradeCutoffHz`), for each IMU segment.

The grade of a sample is `sin(−atan2(x, z))`, positive downhill. The window grade is the mean over the window, weighted by the GPS speed. Only moving windows have one. The ride offset is the weighted mean grade of the ride, minus the elevation drop between the first and the last moving window divided by the distance between them. The calculator subtracts the offset from each window grade. Grade needs 800 m of moving distance (`MinimumGradeMetres`).

The baseline grade is the mean of the window and up to two time-adjacent windows on each side. It needs the grade of the window and at least three grades. The band edges are −6°, −2°, 2°, 5°, 10° and 15°, compared as sines. Each edge keeps its side until the baseline grade passes it by 1° (`GradeHysteresisDegrees`). The first window, a window after a time gap and a window after one without a baseline grade take the plain side of each edge. See [Measured basis](#measured-basis). Single-window outliers merge after that.

`TerrainGradeState` reports the availability: `Available`; `NoFrameSensor` for a ride without a usable frame IMU; `NoElevation` when an end point has no elevation; `TooShort` under 800 m.

## Materialization

`TerrainProfileCoordinator` owns the device-local profile, the label summary and the ride class summary. At startup and after session, track, label or recording source publications, one background evaluation gives the pending profiles, the pending label work, the scale of each setup and the pending class work. The source publication matters because sync can publish a source after the rows of its ride. The coordinator runs one item at a time, in a fixed order: profile work, then label work, then class work.

Profile work processes one recording at a time, newest first. An editor can prioritize a queued recording. A changed input supersedes the run. After a written commit, the coordinator publishes the committed key, outcome and summary onto the current session snapshot and clears its label and class keys and summaries. If the inputs changed meanwhile, the status query reports `Pending` for that snapshot. A publication failure is logged and the queue continues.

Label work starts only when no profile work is queued or running. One label run holds each session in the `Pending` label state with its label key. It reads the stored profiles, summarizes them with `TerrainLabels.Summarize` on the background runner, and writes all summaries in one transaction, with the profile key guard of class work. The written sessions publish in one step. A new evaluation with another label run cancels the run, which then writes and publishes nothing. A failed run, or a run that writes no row, passes to the class work of its plan.

Class work starts only when no profile or label work is queued or running, because profiles and labels change scales. It runs per setup, in order of the newest session of each setup. A class run holds the setup ID, its scale key and the pending sessions with their class keys. It reads their stored profiles, classifies and summarizes them on the background runner, and writes all summaries in one transaction. The write updates a row only while it holds a computed profile with the profile key of the class key; a skipped row stays `Pending` until the next evaluation. The written sessions publish in one step. A new evaluation that gives the setup another scale key, or a session of the run another class key, supersedes the run, and a superseded run writes nothing. A read or write failure is logged once. Shutdown cancels and drains planning and all three kinds of work.

The content token identifies the processed telemetry revision, selected full track and points revision, aligned recording window and track projection version. `TerrainProfileKey` adds the algorithm version. `TerrainLabelKey` holds the profile key, the label set and `TerrainLabels.Version`. `TerrainClassKey` holds the profile key, the scale key, the label set and `TerrainClassifier.Version`. The exact reader and the SQLite write transaction use the same token rule as the status query. A computed row contains a summary and MessagePack profile, and later a label key and label summary, and a class key and class summary. A terminal outcome contains only its key and reason.

## Availability

`SessionTerrainStatusQuery` derives the list and editor status from the session snapshot and track catalog. A session without processed telemetry is unavailable. A new key is pending. With matching inputs, the status exposes the current summary or the stored outcome. A recording with no selected track reaches `NoTrack` without a telemetry read. Missing original track fixes yield `ContentMissing`. A stale or missing telemetry read means the run is superseded, so it writes nothing.

A calculation failure is recorded for its input key. If the database write itself fails, the coordinator logs it once and publishes `Failed` for that key without a stored row. The next publication of the session from the database, or the next app start, runs it again. Authored metadata and sync clocks are independent of profile materialization.

`TerrainStatus.Current` describes the profile only. `SessionTerrainStatusQuery.EvaluateLabels` gives the `TerrainLabelState` of a session: `None` when the profile is not current or the label set is empty, without a read of the stored columns; `Current` with the summary when the stored label key matches; `Pending` otherwise. `SessionTerrainStatusQuery.EvaluateClasses` gives the `TerrainClassState`: `Classified` when the profile is current, the setup scale has a ready or anchored bin or the ride has labels, and the stored class key matches; `Pending` when the stored class key is missing or different; `Unavailable` otherwise, including a session without a setup. The domain snapshot and the session summary carry the class state. The session row shows the rough share only for `Classified` with at least 300 m of classified distance.

## Classes

Classes are per setup. `SetupTerrainScaleQuery.Evaluate` derives a `SetupTerrainScale` for each setup from the current measurement summaries of its sessions; it is never stored. For each sensor it merges the distributions of the contributing rides. The median bin is the first bin where the cumulative distance reaches half of the total. A sensor is `Ready` with 3 contributing rides and 5,000 m, otherwise `NotEnoughData` with its rides and metres. See [Measured basis](#measured-basis). Labels can anchor a sensor in place of this rule; see [Labels](#labels). `TerrainScaleKey` holds the ready and anchored median bins; `TerrainScaleKey.None` has none. A new ride that leaves every median in its bin leaves the key unchanged. `SetupTerrainScaleQuery.Catalog` gives the scale key of each setup from the session summaries.

`TerrainLevels` holds the level arithmetic. Twenty bins span one class step of 1.5×. The centre of Medium is the median of the bin. A reading's class value is its level in class steps from the median, plus 0.5. Below −1 is Smooth, below 0 Calm, below 1 Medium, below 2 Rough, otherwise Very rough.

`TerrainClassifier.Classify(profile, scale, labels)` returns a `ClassifiedProfile` with one `ClassifiedWindow` per profile window. A reading is usable when its sensor has a ready median and its level is above zero. A descent window takes the mean value of its unclipped fork and rear axle readings; without one, the frame value; without a frame reading, the mean of the clipped fork and rear axle readings. The window records its `GroundSensors` source and class value. A climb window with a ready climb median and a frame reading is Smooth climb below the climb median and Technical climb at or above it; it has no class value or source. Single-window outliers merge; the merge keeps the class value. Labels apply last: a labelled window takes the label class, keeps its class value and source, and has `Labelled` set. Without a ready or anchored bin only labelled windows have a class. `TerrainClassifier.Summarize` gives the classified descent distance and its Rough and Very rough share; a labelled window counts with its label. Every reader of a window class takes a `ClassifiedProfile`.

## Labels

A terrain label is an authored fact: a range of a recording with a `RoughnessClass`. The user labels the analysis selection from the plot menu once the terrain of the ride is loaded, and changes or deletes labels in the Segments panel and the setup editor. `TerrainLabelCoordinator` owns create, class change and delete; `TerrainLabelStore` holds the labels. A label belongs to the recording source: its owner key is the source hash and its seconds are owner seconds, so each session of a split recording sees the labels inside its window. A new label must not overlap a label of the same source. Labels sync as their own row family; labels that overlap after a sync resolve by order.

`TerrainLabelQuery` derives the `TerrainLabelSet` of a session and never stores it: the labels that the session window holds fully, in telemetry seconds, in order of start and then of label ID. `TerrainLabelSet.ClassAt` labels a moving window whose midpoint is inside the first range that holds it. A climb class labels only a climb window, and a descent class only a window that is not a climb.

`TerrainLabels.Summarize` gives the `TerrainLabelSummary` of a ride: one `LabelledStretch` per label with an eligible reading, in label order. For each sensor, the stretch holds the median level of the eligible readings of its labelled windows and their distance at 10 m each; the median of an even count is the geometric mean of the two middle levels. A descent label reads the speed-removed frame, fork and rear axle levels, and a climb label the raw frame level. A label is one judgement about a whole stretch, so the stretch, not each reading, is the unit of the scale.

`SetupTerrainScaleQuery` collects the stretches of the current label summaries of a setup. A sensor with 100 m of labelled stretches (`MinimumLabelledMetres`) is `Anchored` in place of the median of the rides; a new ride does not move it. See [Measured basis](#measured-basis). `TerrainLabels.Anchor` reads a label as a range of levels, not as the centre of its class. It takes the median bin that puts the most stretches inside the class of their label, each stretch counting once, whatever its length. Of equal choices, it takes the bin nearest the median of the bins that each stretch alone would set, so one stretch reads at the centre of its class. The climb limit needs 100 m of each climb class. `TerrainLabels.ClimbAnchor` takes the bin that puts the most climb stretches on the side of their class, and of equal choices the bin nearest midway between the median bins of the two classes, rounded down. `TerrainLabelCoverage` holds the labelled metres of each sensor and climb class. A label change inside a ride changes its class key also when the scale key stays, because the class key holds the label set.

`TerrainClassifier.Read` gives the `LabelReading` of a stretch on a scale: the class it reads, with the source order of a window, and the class steps by which it lies outside the class of its label. The reading clashes with the label above one class step (`TerrainLabels.ClashSteps`), because the typical level of 100 m varies by about 0.26 class between runs. `SetupTerrainScaleQuery` reads each stretch on the scale set without it: the other stretches, or the rides where the other stretches do not anchor a sensor. A stretch that anchors a sensor alone reads its own class on the scale it sets, so it can only be tested against everything else. `SetupTerrainScale.Readings` holds these readings by session and label range, and `ReadingOf` looks one up. `ITerrainLabelQuery.ForSetup` gives each label row its reading, and no reading while the label summary of its ride is not current. The Segments panel reads its label rows from the scale of the open session's domain snapshot ([segments.md](segments.md)). `TerrainClassText.ReadsAs` gives the "Reads as" text of both lists.

## Recorded editor

The editor loads on each change of the group of profile status, setup scale key and label set; a domain snapshot without a scale gives `TerrainScaleKey.None`. It reads the exact profile, classifies it on that scale with that label set and derives timed runs for both axes, segmented sensor, grade and pedal bob signals, and the distance share of each class source, on its background runner. The reducer accepts a loaded result only for the current group. A change of status immediately removes the previous plot and map data. A change of only the scale key or the label set keeps the loaded terrain and the chosen scope until the new result replaces it; a result with the same profile key keeps the baseline. Without classes, a preset scope admits every window of its preset.

The strip title names the class source with the largest share of the classified descent windows, or none without classes. When that source is the frame, `TerrainSourceText.IsSprung` is true: the title takes the warning colour and the tooltip says that the frame is sprung, so it reads what the suspension passes on and not the ground itself. The tooltip lists the source shares, a clipped fork or rear axle reading left out of a class value, absent frame, fork or rear axle data in classified descent windows, what the classes of each sensor with readings in the ride are compared with (the labelled terrain of an anchored sensor, the typical descent or climb of the setup's rides, or the rides and metres it still needs; below 100 m of labels, that labels change the comparison at 100 m and how much is labelled), the share of the classes that labels set, and the ignored sensors. With labels, the class strip draws a Labels lane above Grade and Roughness, and the readout names the label class. The roughness plot draws the speed-removed frame, fork and rear axle levels; the guides of a curve are the class edges of its ready median bin. The grade trace uses the same five-window baseline as the grade bands.

## Analysis selection

The analysis scope is a preset narrowed by legend classes. Descents admits every non-climb window, including windows without grade. Climbs admits the two climb bands, and All admits every window. A legend class outside the preset cannot be selected, and an empty axis admits every class on that axis. Every preset uses moving windows only, so stopped time never enters the analysis. `TerrainSelectionQuery` intersects matching windows with the analysis range, joins adjacent intervals, and excludes intervals shorter than the minimum analysis duration. A partial window contributes its mean speed times the covered duration. Legend distances describe the corresponding class inside the analysis range. The result also reports the stopped time inside the range.

The editor keeps one scope choice per tab: the default, a chosen terrain scope or a segment comparison. The default is derived from the loaded profile: Descents when the profile has grade, otherwise All. No observer writes a scope when a profile arrives. A choice equal to the profile's default is stored as the default. The reducer evaluates the selection again only when the profile, the effective scope or the analysis range changes. With a loaded profile, the effective time selection is the scoped selection; without one, it is the analysis range. A segment comparison uses the analysis range. Analysis results are keyed on that selection, not on the scope, so statistics and Insights share one value. Plot overlays mark the selection of a chosen scope only.

## Bike response baseline

The baseline identity is the ordered set of current profile keys of other rides on the same bike. The editor observes summary commits throughout the tab lifetime and selects the identity synchronously on each commit. A changed identity invalidates the previous model. Exact profile reads, log-linear least-squares fitting and response measurements run on the background runner. A superseded fit cannot publish. A baseline read error is local to the response panel.

Climbing windows stay out of the fit and out of every response row, because pedalling and rider load drive the response there. Each target needs five compatible rides, 1,000 moving windows with positive observations, and independent variation in the log band levels and speed. Each target has predictor sets in order of preference: fork targets `Fork`, then `Frame`; shock targets `RearAxle`, then `Frame`; `PassedToFrame` `ForkAndRearAxle`, `Fork`, then `RearAxle`. A set is a candidate when the evaluated ride and, in a comparison, the paired ride have every sensor of the set; `PassedToFrame` also needs the frame IMU. The first candidate whose fit is `Fitted` wins; a set with too little data passes to the next. Each model carries its `GroundSensors` predictors, coverage and availability.

Insights measures the whole ride and each descent level inside the analysis time range, independently of the scope. Its analysis key includes the scale key and the label set, so a change of either recomputes it. `TerrainResponse.Describe` also accepts a window filter, so a caller can limit every row to the windows of a scope. Boundary-window distance and log residuals are weighted by the covered fraction. Ratios use the geometric mean of observed over predicted response. A row reports ratios from 150 m, and the presentation marks distances under 300 m as low confidence. Classification uses the bike-baseline repeat thresholds of 30% for suspension response and 10% for vibration passed to the frame. `TerrainResponse.Findings` lists every cell outside its repeat range as a typed finding: target, group, signed deviation and confidence. The confidence is low under 300 m. The app turns findings into verdicts and rows into a deviation chart in `Terrain/Formatting/TerrainResponseReports`; views build no sentences.

## Comfort

`TerrainComfort` describes, per descent class of the classified profile, the moving ground inside a range that a window filter admits. Each row gives the distance, the fork and shock activity (RMS velocity, mm/s) and the frame level (band-limited vibration, g). The figures are as measured, so faster riding raises them. Each figure is the quadratic mean of its windows, weighted by covered time, because the window figures are RMS values. A missing sensor leaves its figure empty.

## Climbing

`TerrainClimbing` describes the moving climb windows inside the analysis range, with covered fractions. It reports distance, time, the net height gained from the window grades, the time with detected pedalling, and the split into smooth and technical climb levels. Bob and cadence are time-weighted medians over the pedalled climb windows only, because travel at the pedal frequency on rough climbs is ground input. Mean travel per end compares climb windows with the descent bands; flat ground stays out. Below 120 s of pedalled climbing there is no summary.

## Character (experimental)

`TerrainCharacter` reports, per end, the moving descent ground of class Medium or rougher inside the analysis range that has a median compression stroke size. It splits that distance into big hits and chatter by the stroke size as a share of the end's max travel: rear big hits from 16% and chatter below 8%, front big hits from 20% and chatter below 9%. These are one bike's quartiles on rough ground and stay provisional. A share is dominant when it covers more than half of at least 300 m. The Bike response page shows the dominant shares as an experimental hint in the response card. A ride without classes shows no hint.

## Same-trail comparison

Ground alignment refines the GPS time map with a shared frame IMU, or a shared fork IMU when frame data is absent. `TerrainRunAlignment` cuts the source range into 150 m chunks every 75 m. A chunk is a candidate when both runs have matching values for 80% of its metres. Each candidate searches 40 m to either side and is accepted when its best correlation is at least 0.6 below the search limit. An accepted chunk is dropped when its shift differs by more than 5 m (`NeighbourShiftToleranceMetres`) from every neighbouring accepted chunk. The pair aligns only when at least half of the candidates are accepted (`MinimumAcceptedShare`, counted before the drop) and two chunks remain. Unrelated ground correlates by chance in a few chunks, so the share rule, not the chunk count, rejects it. Only moving windows within 75 m of a remaining centre enter the comparison, at the shift interpolated between remaining centres.

Each run is classified on the scale of its own setup, taken from the scale catalogue; a matched run is classified without labels. A matched pair's class is `TerrainLevels.Classify` of the mean of the two class values; a pair with a run without a class value has no class. After the outlier merge, a pair whose source window is labelled takes the class of that window. The pair fit excludes both compared rides and requires compatible predictor scales. Rows cover all matched windows and each class. A row reports pace and ratios from 150 m, with the same low-confidence mark under 300 m. Pace reports the median and interquartile range. Suspension response and vibration passed to the frame compare each ride’s log residual on jointly measured windows. The same-position RMS ratios use the observed speed ratio to the power 0.9 and remain available without a baseline. Repeat limits are 20% for RMS, compression and same-position response, 25% for rebound and 5% for vibration passed to the frame. `TerrainRunComparison.Findings` returns the same typed findings for the response ratios.

## Known-trail average

Each source window receives at most one measurement from each aligned run. Each run is classified on the scale of its own setup. Measured frame, fork and rear axle levels use geometric means of the speed-removed levels of at least three eligible readings; a reading at zero or clipped does not enter the mean. Grade uses an arithmetic mean and the same contiguous 50 m baseline as a single ride. A stopped source window stays in the average without values or classes, so the baseline reaches past it.

A descent window's class is `TerrainLevels.Classify` of the mean class value of at least three runs with a class value. A climb window takes the majority climb class of at least three runs with a climb class; a tie gives Technical climb. Averaged bands and classes merge single-window outliers as a single ride does. After the merge, a labelled source window keeps its class; labels of matched rides have no effect. A comparison using the average keeps windows without an averaged class in its all-terrain row only. A change of the source profile key, the source scale key, the source label set or the scale catalogue starts a new average. Average values live for the current Match in the recorded editor. See [Measured basis](#measured-basis).

## Measured basis

This section records one analysis of recorded rides. The values explain the constants and the limits of the methods in this document. They do not describe the code. They apply to one data set. Measure again before you change a constant that a row supports.

**Data set.** The analysis ran on 2026-11-26 on profiles of algorithm version 9:

- 30 rides with a computed profile, 65.2 km of moving distance, one setup on one bike, recorded from 2025-12-18 to 2026-06-14.
- 28 rides have a frame IMU and 21 have a fork IMU. No ride has a rear axle IMU.
- 18 rides have grade (57.3 km).

**Method.** Two windows of different rides are the same place when their centres are within 6 m and their headings are within 45°. 37 ride pairs give 29.4 km of matched windows. The noise of a value is the standard deviation of one run at one place: the standard deviation of the difference between two runs, divided by √2. One class is a level ratio of 1.5.

### Grade

| Subject | Value |
|---|---|
| Band changes with the plain comparison | 10.95 per km. 84% of the changes occur within 1° of a band edge. |
| Band changes with the hysteresis of 1° | 7.71 per km. The grade values do not change. |
| Noise of the window grade (10 m) | 1.5° |
| Noise of the baseline grade (50 m) | 1.3° |
| Difference of the ride offset between two runs | 0.89° (standard deviation over 13 ride pairs) |
| IMU grade against the GPS elevation over 300 m | Gain 0.995, R² 0.92, 167 spans |
| Part of the grade difference between two runs that acceleration, speed and suspension pitch explain | Less than 3% |

### Roughness

| Subject | Value |
|---|---|
| Noise of one fork reading over 10 m, 30 m and 50 m | 0.76, 0.48 and 0.34 class |
| Noise of one frame reading over 10 m, 30 m and 50 m | 0.67, 0.42 and 0.30 class |
| Two runs give the same fork class at one place | 54% to 60% of the windows. 91% are within one class. |
| Mean of N runs at one place | The noise falls with √N. Measured up to N = 3. |
| Changes of the fork class | 25.9 per km |
| Speed exponent between two runs of one place | Fork 0.91 ± 0.07. Frame 0.71 ± 0.06. |
| Windows above the clipped share of 2% | None. The largest share is 1.1%. |
| Setup median of the fork, over 21 rides | 10 changes across 13 bins (0.65 class). One change gave new classes to 4% to 25% of the earlier windows. |
| Setup median of the frame, over 28 rides | 16 changes across 26 bins (1.3 class). |
| Median of 10 consecutive readings (100 m), between two runs of one place | 0.26 class of noise for the fork and for the frame (143 and 167 stretches). This is the noise of the readings only. It does not include the error of the label itself. |

### Sensors

| Subject | Value |
|---|---|
| Fork and frame deviation of one run from the mean of its place | Correlation 0.83 to 0.95 |
| Fit of the shock velocity from the band levels and the speed | R² 0.92 with the frame bands, 0.87 with the fork bands |
| Rides with a frame reading and no fork reading | 7 of 28 |
| Rear axle IMU | Not measured. No ride has one. |
| Effect of the tune on the levels | Not measurable. 22 of the 30 rides have no tune values. |

The conclusions below are a reading of these values. They are not measured causes.

- The fork and the frame move together between two runs of one place. A second ground sensor repeats most of what the first one shows, so a rear axle IMU adds little to the ground classes.
- A rear axle IMU gives a ground reading when the fork IMU has none, and it measures the input of the shock.
- The frame bands fit the shock velocity better than the fork bands.

### Methods that measured worse

| Method | Measured | Method of today |
|---|---|---|
| Grade from an attitude with the gyroscope | 3.6° of noise over 10 m, 2.0° over 50 m. The error grows in turns. | 1.5° and 1.3° |
| Slope of the GPS elevation | 2.5° of noise over 50 m, 1.0° over 310 m | 1.3° and 0.6° |
| IMU grade fused with the GPS elevation over 100 m to 500 m | 1.4° to 2.0° of noise over 50 m | 1.3° |
| Ride offset from a fit of all GPS points | 1.59° between two runs | 0.89° |
| Guard on the two end points of the ride offset | 0.86° between two runs | 0.89° |
| Baseline grade from the window and 4 neighbours on each side | 8.04 band changes per km. The values change by 0.86° (RMS). | 7.71 with the hysteresis |
| Fork class from the mean of all runs of a trail | 23.3 class changes per km | 27.0 for one run of that trail |

The scripts of the analysis are in `analysis/terrain/`. Its `README.md` shows how to run them.
