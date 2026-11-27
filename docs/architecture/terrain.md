# Terrain profiles

A profile describes moving ten-metre windows of one recorded session, and the roughness and grade pieces cut from them (see [Pieces](#pieces)). The telemetry library derives suspension figures, IMU readings, grade and matching signals from processed telemetry and original GPS fixes. The profile stores measurements only; it holds no roughness classes. Each window can hold a frame, a fork and a rear axle IMU reading: the raw 1–80 Hz level, three band levels, the speed-removed level and the clipped share. Speed removal scales a level to 7 m/s with the exponent 0.7 for the frame and 0.9 for the fork and the rear axle. A reading is clipped when more than 2% of its window samples are at full scale. See [Measured basis](#measured-basis). `TerrainProfile.HasImu` is true when the ride recorded any IMU. Grade reports its own availability. `TerrainProfileCalculator.AlgorithmVersion` versions the calculator.

A failed sensor reads zero. When a suspension or IMU sensor reads zero in more than half of its measured moving windows, the calculator drops all its readings for the ride. An IMU level below one count counts as zero, because the band-pass filter leaves a small residue next to live samples. A dropped IMU yields no matching series, scale or distribution, and a dropped frame IMU also removes grade. The rear axle IMU produces no matching series. The profile lists the dropped sensors, and the terrain source note names them.

The measurement summary holds the moving and climb distances and four level distributions: the speed-removed levels of the frame, fork and rear axle IMUs on descent windows and the raw frame level on climb windows. A distribution adds 10 m per eligible window to the `TerrainLevels.Bin` of its level. An eligible reading is moving, above zero and not clipped; `TerrainProfileCalculator.Eligible` holds the level and clipped tests, and the label summary and the known-trail average use it also. `LevelDistribution` has value equality over its bins.

## Grade

Grade is the slope of the line through the two wheel axles. It comes from the recorded attitude of the frame IMU. The frame IMU needs a sample rate of at least 200 Hz, an active sensor, an accelerometer scale above 0 and a gyroscope scale above 0.

For each frame segment, `FrameAttitudeEstimator.Recorded` gives the up direction of each sample. All three gyroscope axes turn it. The measured acceleration in g pulls it towards itself with a time constant of 10 s and is not normalised, so vibration averages out. Before that, the calculator removes the forward acceleration and the turn terms: (`Acceleration`, `ForwardSpeed` × rate about Z, −`ForwardSpeed` × rate about Y) divided by 9.80665. The result of a sample is the normalised sum of a forward and a backward pass. `TrackMotion` gives the forward speed: the receiver speed of the DAQ GPS, moved 0.2 s earlier and low-passed at 0.4 Hz on the 10 Hz grid, when each fix of the span has one. Other spans take the speed from position differences. `Acceleration` is the central difference of the forward speed.

The frame pitch is `asin(−up.X)`, positive nose down. The suspension pitch is `atan((front travel − rear travel) / 1250 mm)` (`NominalWheelbaseMillimetres`), with the travel of each side interpolated at the sample time. It is 0 when a side has no travel at that time or is absent, including time that the user dropped from a [sensor finding](processing.md#sensor-health). The grade of a sample is `sin(frame pitch − suspension pitch)`, positive downhill. The window grade is the mean over the window, weighted by the speed from position differences. A sample inside an airtime has no weight. Only moving windows have a grade. See [Attitude grade](#attitude-grade). The ride offset is the weighted mean grade of the ride, minus the elevation drop between the first and the last moving window divided by the distance between them. The calculator subtracts the offset from each window grade. Grade needs 800 m of moving distance (`MinimumGradeMetres`).

The grade pieces give the bands (see [Pieces](#pieces)). The band edges are −6°, −2°, 2°, 5°, 10° and 15°, compared as sines. Along adjacent pieces, each edge keeps its side until the piece grade passes it by 1° (`GradeHysteresisDegrees`). The first piece and a piece after a stop or a gap take the plain side of each edge. A moving window takes the band of the grade piece that holds its midpoint.

`TerrainGradeState` reports the availability: `Available`; `NoFrameSensor` for a ride without a usable frame IMU; `NoElevation` when an end point has no elevation; `TooShort` under 800 m.

## Pieces

A piece is a moving stretch cut where the ground changes, so a class or a band does not mix two kinds of ground. The calculator splits each window into five 2 m cells; a cell moves with its window. Each run of time-adjacent moving cells is cut at the partition with the least squared error from the piece means plus a penalty per piece (the PELT algorithm). A run shorter than two minimum pieces is one piece.

- **Roughness pieces** cut on the speed-removed 1–80 Hz level of every IMU of the ride together, in class steps, with a penalty of 4 per IMU. A piece holds at least five cells, one window. Each piece has the IMU readings of its whole length, measured as a window is.
- **Grade pieces** cut on the cell grade in degrees, with a penalty of 400. A piece holds at least one cell. Each piece has the speed-weighted grade of its whole length, less the ride offset, and its band. A stretch inside a longer one becomes its own piece when it differs by more than about 28° over one cell, 20° over two cells or 16° over three cells.

A cell without a value takes the median of its run; a signal without values in a run does not cut it. A ride without an IMU has no roughness pieces, and a ride without grade has no grade pieces. See [Measured basis](#pieces-1).

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

`TerrainClassifier.Classify(profile, scale, labels)` returns a `ClassifiedProfile`. Its pieces cut the profile windows at every edge of a roughness piece, a grade piece and a label, in time order. A piece reads the IMU levels of the roughness piece and the grade and band of the grade piece that hold its midpoint; without one it keeps those of its window. A stopped window is one piece without a class. Each profile window has one `ClassifiedWindow`, with the classes of the piece that holds its midpoint and its own measurements. A reading is usable when its sensor has a ready median and its level is above zero. A descent piece takes the mean value of its unclipped fork and rear axle readings; without one, the frame value; without a frame reading, the mean of the clipped fork and rear axle readings. It records its `GroundSensors` source and class value. A climb piece with a ready climb median and a frame reading is Smooth climb below the climb median and Technical climb at or above it; it has no class value or source. Labels apply last: a labelled piece takes the label class, keeps its class value and source, and has `Labelled` set. Without a ready or anchored bin only labelled pieces have a class. `TerrainClassifier.Summarize` gives the classified descent distance of the pieces and its Rough and Very rough share; a labelled piece counts with its label. Every reader of a window class takes a `ClassifiedProfile`.

## Labels

A terrain label is an authored fact: a range of a recording with a `TerrainLabelClass`, either a roughness class (`Roughness`) or a grade band (`Steepness`). The user labels the analysis selection from the plot menu ("Label roughness" or "Label steepness") once the terrain of the ride is loaded, and changes or deletes labels in the Segments panel and the setup editor. A change keeps the kind of a label. `TerrainLabelCoordinator` owns create, class change and delete; `TerrainLabelStore` holds the labels. A label belongs to the recording source: its owner key is the source hash and its seconds are owner seconds, so each session of a split recording sees the labels inside its window. A new label must not overlap a label of the same kind of the same source; a roughness and a steepness label may overlap. Labels sync as their own row family; labels that overlap after a sync resolve by order.

`TerrainLabelQuery` derives the `TerrainLabelSet` of a session and never stores it: the labels that the session window holds fully, in telemetry seconds, in order of start and then of label ID; roughness labels in `Ranges` and steepness labels in `Grades`. `TerrainLabelSet.ClassAt` labels a moving window whose midpoint is inside the first range that holds it. A climb class labels only a climb window, and a descent class only a window that is not a climb, after its steepness label. `GradeAt` gives the first steepness label that holds the midpoint of a moving window.

Grade is measured in degrees, so a steepness label sets no scale. In its ride the label band wins everywhere: `TerrainClassifier.Classify` gives each piece the band of its steepness label before the roughness class and sets `ClassifiedWindow.GradeLabelled`. A piece that a label makes a climb takes a climb class, counts in the Climbs scope and the climbing report, and leaves the response rows. The stored profile, the setup scale and the bike response fit keep the measured band.

`TerrainLabels.Summarize` gives the `TerrainLabelSummary` of a ride: one `LabelledStretch` per label with an eligible reading, in label order. For each sensor, the stretch holds the median level of the eligible readings of its labelled windows and their distance at 10 m each; the median of an even count is the geometric mean of the two middle levels. A descent label reads the speed-removed frame, fork and rear axle levels, and a climb label the raw frame level; a window is a climb after its steepness label. A label is one judgement about a whole stretch, so the stretch, not each reading, is the unit of the scale. The summary also holds one `GradeStretch` per steepness label with a grade: the mean window grade of its moving windows.

`SetupTerrainScaleQuery` collects the stretches of the current label summaries of a setup. A sensor with 100 m of labelled stretches (`MinimumLabelledMetres`) is `Anchored` in place of the median of the rides; a new ride does not move it. See [Measured basis](#measured-basis). `TerrainLabels.Anchor` reads a label as a range of levels, not as the centre of its class. It takes the median bin that puts the most stretches inside the class of their label, each stretch counting once, whatever its length. Of equal choices, it takes the bin nearest the median of the bins that each stretch alone would set, so one stretch reads at the centre of its class. The climb limit needs 100 m of each climb class. `TerrainLabels.ClimbAnchor` takes the bin that puts the most climb stretches on the side of their class, and of equal choices the bin nearest midway between the median bins of the two classes, rounded down. `TerrainLabelCoverage` holds the labelled metres of each sensor and climb class. A label change inside a ride changes its class key also when the scale key stays, because the class key holds the label set.

`TerrainClassifier.Read` gives the `LabelReading` of a stretch on a scale: the class it reads, with the source order of a window, and the class steps by which it lies outside the class of its label. The reading clashes with the label above one class step (`TerrainLabels.ClashSteps`), because the typical level of 100 m varies by about 0.26 class between runs. `SetupTerrainScaleQuery` reads each stretch on the scale set without it: the other stretches, or the rides where the other stretches do not anchor a sensor. A stretch that anchors a sensor alone reads its own class on the scale it sets, so it can only be tested against everything else. `SetupTerrainScale.Readings` holds these readings by session and label range, and `ReadingOf` looks one up. `TerrainClassifier.Read` gives the `GradeReading` of a steepness stretch: the band of its mean grade on the plain band edges, its degrees and the degrees by which it lies outside the band of its label. It clashes above 2° (`TerrainLabels.GradeClashDegrees`), a chosen limit; the grade of a piece varies by about 1.4° between runs. `TerrainLabelSummary.ReadingOf` looks one up.

`ITerrainLabelQuery.ForSetup` gives each roughness row its reading in `Labels` and each steepness row the reading of the ride's label summary in `SteepnessLabels`, and no reading while the label summary of the ride is not current. The Segments panel reads roughness rows on the scale and steepness rows from the current label summary of the open session's domain snapshot ([segments.md](segments.md)). `LabelReadingText` gives the "Reads as" text, the clash flag and the tooltip of both lists.

## Recorded editor

The editor loads on each change of the group of profile status, setup scale key and label set; a domain snapshot without a scale gives `TerrainScaleKey.None`. It reads the exact profile, classifies it on that scale with that label set and derives timed runs for both axes, segmented sensor, grade and pedal bob signals, and the distance share of each class source, on its background runner. The reducer accepts a loaded result only for the current group. A change of status immediately removes the previous plot and map data. A change of only the scale key or the label set keeps the loaded terrain and the chosen scope until the new result replaces it; a result with the same profile key keeps the baseline. Without classes, a preset scope admits every window of its preset.

The strip title names the class source with the largest distance share of the classified descent pieces, or none without classes. When that source is the frame, `TerrainSourceText.IsSprung` is true: the title takes the warning colour and the tooltip says that the frame is sprung, so it reads what the suspension passes on and not the ground itself. The tooltip lists the source shares, a clipped fork or rear axle reading left out of a class value, absent frame, fork or rear axle data in classified descent pieces, what the classes of each sensor with readings in the ride are compared with (the labelled terrain of an anchored sensor, the typical descent or climb of the setup's rides, or the rides and metres it still needs; below 100 m of labels, that labels change the comparison at 100 m and how much is labelled), and the share of the classes that labels set. There is no separate check for quiet sensors: a broken sensor is a sensor finding, and terrain reads the readings that remain after the user's drops. `TerrainProfileCalculator.AlgorithmVersion` 15 removed the earlier silent-sensor check. The class strip draws a Roughness labels lane above Roughness and a Steepness labels lane above Grade when the ride has labels of that kind, and the readout names the label classes. A grade-lane piece with a steepness label has no measured shade. The lanes, their shades and the grade trace draw the classified pieces, so a class or band changes where the ground does. The grade trace is level over each piece, with a vertical join where two pieces meet. The roughness plot draws the speed-removed frame, fork and rear axle levels of the windows; the guides of a curve are the class edges of its ready median bin.

## Analysis selection

The analysis scope is a preset narrowed by legend classes. Descents admits every non-climb window, including windows without grade. Climbs admits the two climb bands, and All admits every window. A legend class outside the preset cannot be selected, and an empty axis admits every class on that axis. Every preset uses moving windows only, so stopped time never enters the analysis. `TerrainSelectionQuery` intersects matching pieces with the analysis range, joins adjacent intervals, and excludes intervals shorter than the minimum analysis duration. A partial piece contributes its mean speed times the covered duration. Legend distances describe the corresponding class inside the analysis range. The result also reports the stopped time inside the range.

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

Each source window receives at most one measurement from each aligned run. Each run is classified on the scale of its own setup. Measured frame, fork and rear axle levels use geometric means of the speed-removed levels of at least three eligible readings; a reading at zero or clipped does not enter the mean. Grade uses an arithmetic mean and a contiguous 50 m baseline: the median of the window and up to two time-adjacent windows on each side, from three grades, with the mean of the two middle grades for an even count. A run gives each source window the classes of its piece at the window midpoint. A stopped source window stays in the average without values or classes, so the baseline reaches past it.

A descent window's class is `TerrainLevels.Classify` of the mean class value of at least three runs with a class value. A climb window takes the majority climb class of at least three runs with a climb class; a tie gives Technical climb. Averaged bands and classes merge single-window outliers: a window between two time-adjacent windows of one other class takes their class. After the band merge, a source window with a steepness label keeps its label band, before the climb classes. After the class merge, a labelled source window keeps its class. Labels of matched rides have no effect. A comparison using the average keeps windows without an averaged class in its all-terrain row only. A change of the source profile key, the source scale key, the source label set or the scale catalogue starts a new average. Average values live for the current Match in the recorded editor. See [Measured basis](#measured-basis).

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
| Grade from an attitude with the gyroscope, acceleration normalised (algorithm version 9) | 3.6° of noise over 10 m, 2.0° over 50 m. The error grows in turns. | 1.5° and 1.3° |
| Slope of the GPS elevation | 2.5° of noise over 50 m, 1.0° over 310 m | 1.3° and 0.6° |
| IMU grade fused with the GPS elevation over 100 m to 500 m | 1.4° to 2.0° of noise over 50 m | 1.3° |
| Ride offset from a fit of all GPS points | 1.59° between two runs | 0.89° |
| Guard on the two end points of the ride offset | 0.86° between two runs | 0.89° |
| Baseline grade from the window and 4 neighbours on each side | 8.04 band changes per km. The values change by 0.86° (RMS). | 7.71 with the hysteresis |
| Fork class from the mean of all runs of a trail | 23.3 class changes per km | 27.0 for one run of that trail |

### Grade cutoff

This part and the gyroscope row below describe the low-pass grade of algorithm versions up to 13. Version 14 takes the grade from the recorded attitude (see [Attitude grade](#attitude-grade)).

A second analysis ran on 2026-11-27 on the 18 rides with grade, after GPS alignment. Grades and bands were computed from the raw samples for each cutoff, with the 1° hysteresis on the 50 m mean baseline of that time and before the outlier merge. Same-place matching used 3 m and gave 677 window pairs over 7 ride pairs.

| Subject | Value |
|---|---|
| GPS clock lag before alignment | 26 rides had a reliable estimate. 23 lagged by 0.1 s or more, the largest by 3.0 s. 2 rides had too little movement. |
| Noise of the window grade (10 m) at 0.08 Hz after alignment, 6 m match | 1.33°. The earlier analysis measured 1.5° without alignment. |
| Cutoff 0.08 Hz | Noise 1.09° (10 m) and 1.00° (50 m). 7.2 band changes per km. 74% of matched windows in the same band. |
| Cutoff 0.15 Hz | Noise 1.51° (10 m) and 1.09° (50 m). 8.9 band changes per km. 73% in the same band. |
| Cutoff 0.3 Hz | Noise 2.19° (10 m) and 1.23° (50 m). 10.0 band changes per km. 73% in the same band. |
| Between-run correlation of the 10 m detail (window grade minus baseline) | 0.48 at 0.08 Hz, 0.43 at 0.15 Hz, 0.39 at 0.3 Hz |

The 0.08 Hz filter spreads a change of grade over about ±3 s, so the grade rose before the descent began. At 0.15 Hz most of the extra detail repeats between runs, and the same-band share stays the same. At 0.3 Hz the noise grows faster than the detail, and braking errors show as short false climbs.

| Method | Noise over 10 m, 6 m match | Version 13 (0.15 Hz) |
|---|---|---|
| Suspension pitch removed: `atan((front − rear) / wheelbase)`, wheelbase 1249 mm from the linkage | 1.93° | 1.91° |
| No GPS braking correction | 2.12° | 1.91° |
| Gyroscope pitch rate corrected for roll, above 0.04 Hz or 0.08 Hz | 4.4° to 8.2° | 1.91° |

### Baseline grade

A third analysis ran on 2026-11-27 on the same rides at 0.15 Hz. The reference of a window is the mean 10 m grade of the other runs at the same place (6 m match). The figures below use the 928 windows with at least two other runs. 55 of them lie at a change of grade: their reference differs by more than 2° from the mean reference of the two windows on each side.

| Baseline over the window and up to two neighbours on each side | Error against the reference: all windows, changes of grade | Same band as the reference | Noise between runs | Band changes per km with the hysteresis |
|---|---|---|---|---|
| Median (today) | 1.81°, 3.16° | 68.0% | 1.43° | 8.4 |
| Mean (before) | 1.77°, 3.36° | 69.6% | 1.27° | 8.9 |
| Weighted mean 1-2-3-2-1 | 1.78°, 3.15° | 69.7% | 1.39° | 10.1 |
| Mean of the window and one neighbour on each side | 1.85°, 3.07° | 68.1% | 1.56° | 12.6 |

The mean moved a change of grade up to two windows early: on a flat stretch before a steep drop, two steep neighbours lifted a flat window of 0.5° to 3.2°, a gentle descent. The median keeps the flat window flat. Over all windows the median is about as close to the reference as the mean, and it changes band least often. Only 55 windows lie at a change of grade, so the difference there is weak evidence. Single rides now use grade pieces; the known-trail average keeps the median baseline.

### Pieces

A fourth analysis ran on 2026-11-27 on 2 m cells of the same rides, on algorithm version 13: 28 with a frame IMU, 22 with a fork IMU and 18 with grade. Each method gives every moving cell the value of its window or piece. The reference is the value of the matched 2 m cell of another run (3 m, heading within 45°), so its own noise adds the same amount to every method. A change is a cell where the reference 10 m before and 10 m after differ by more than one class or 4°.

| Method | Error against the other run: all cells, at a change | Same class between runs | Class changes per km |
|---|---|---|---|
| Frame, 10 m windows | 1.105, 1.192 class | 60.0% | 45.1 |
| Frame, roughness pieces | 1.083, 1.157 class | 65.2% | 21.0 |
| Fork, 10 m windows | 1.244, 1.286 class | 58.5% | 44.2 |
| Fork, roughness pieces | 1.225, 1.265 class | 60.8% | 23.0 |
| Grade, 10 m windows with the 50 m median baseline | 2.05°, 3.23° | 74.3% | 8.5 |
| Grade, grade pieces | 2.25°, 3.50° | 75.0% | 7.9 |

A rolling 10 m window every 2 m was about as close as the roughness pieces, but changed class 82 to 84 times per km. A penalty of 2 per IMU gave a slightly smaller error and 32 class changes per km; a penalty of 8 per IMU, 12 changes and a larger error than the windows. The noise of one 2 m cell is 0.4 to 1.1 class between rides, so one fixed penalty serves every ride. Grade pieces are farther from the reference than the windows with the median baseline, because the 0.15 Hz filter already spreads a change over about 10 m and a piece holds one grade over its length. On the MSM ride of 2026-06-11, the flat stretch before the descending turn reads 0.2° and the descent begins at 108.2 s.

### Attitude grade

A fifth analysis ran on 2026-11-27 on four rides of the data set with a 240 Hz frame IMU and a 3 Hz receiver. Ride A is the MSM ride of 2026-06-11. Ride B is the same trail on 2026-06-14. It set the constants of algorithm version 14.

Correction time: the disagreement between the forward and the backward pass, in degrees RMS on moving samples.

| Correction time | A | B | C | D | Mean |
|---|---|---|---|---|---|
| 5 s | 2.85 | 2.99 | 2.74 | 4.31 | 3.22 |
| 7 s | 2.70 | 2.95 | 2.48 | 3.58 | 2.93 |
| 10 s | 2.68 | 3.24 | 2.30 | 3.19 | 2.85 |
| 15 s | 2.86 | 3.96 | 2.25 | 3.30 | 3.09 |
| 20 s | 3.21 | 4.70 | 2.35 | 3.76 | 3.51 |

Correction form and speed source at 10 s. The unexplained figure is the forward acceleration that the correction leaves between 0.05 Hz and 0.5 Hz, in degrees RMS.

| Variant | Pass disagreement A, B, C, D | Unexplained A, B, C, D |
|---|---|---|
| Position speed, acceleration not normalised | 2.68, 3.24, 2.30, 3.19 | 4.62, 5.19, 4.03, 12.55 |
| Position speed, acceleration normalised | 4.07, 5.98, 2.61, 4.29 | 4.68, 5.24, 4.23, 12.63 |
| Receiver speed, 0.4 Hz, 0.2 s earlier, not normalised (today) | 2.70, 3.26, 2.33, 2.42 | 4.51, 5.17, 3.91, 4.15 |
| No kinematic terms | 3.90, 4.59, 4.01, 3.29 | 5.29, 6.38, 5.17, 5.55 |

The unexplained part is least with the receiver speed moved 0.1 s to 0.3 s earlier, on all four rides.

Grade pieces with a minimum of one cell:

| Method | Pieces per km (A to D) | Band changes per km (A to D) |
|---|---|---|
| Version 13 | 16 to 21 | 11.9 to 15.5 |
| Penalty 100 | 45 to 56 | 39 to 49 |
| Penalty 200 | 23 to 31 | 20 to 28 |
| Penalty 400 (today) | 11 to 19 | 8.6 to 16.8 |

Rides A and B, each 2 m cell with the grade of its piece, cells matched within 3 m:

| Method | Noise of one run | Same band | Error against the cell grade of the other run |
|---|---|---|---|
| Version 13 | 1.55° | 85% | 6.20° |
| Penalty 200 | 3.12° | 61% | 6.08° |
| Penalty 400 (today) | 2.18° | 70% | 5.68° |
| Penalty 600 | 2.08° | 72% | 5.76° |

The largest measured suspension pitch is 4.5°. Mountain bike wheelbases lie within 8% of 1250 mm, so the constant errs by 0.35° or less. On ride A suspension pitch changed the cell noise from 4.93° to 4.79°.

The export tool and `gyro/step10_pieces.py` then compared versions 13 and 14 on all 18 rides with grade (57.4 km), after GPS alignment. Windows match within 6 m, and each window takes the grade of the piece at its midpoint.

| Subject | Version 13 | Version 14 |
|---|---|---|
| Band changes per km | 7.89 | 6.58 |
| Noise of one run, 1668 matched windows | 1.81° | 2.96° |
| Matched windows in the same band | 72% | 57% |
| MSM 2026-06-11, roll-in at 109.3 s on video: nearest piece edge, grade of the piece after | 108.49 s, 8.4° for 15.6 s | 109.13 s, 24.5° for 1.2 s (6 m) |

Version 14 puts a change of grade at its place and reads a short steep stretch at its own grade. It repeats less well between runs: a position error of 3 m to 6 m between two runs adds to every sharp change, and the level of a long stretch is uncertain by about 3°, the disagreement of the two passes. A ride whose fixes have no receiver speed uses the position speed; on ride D that left three times more unexplained acceleration. A ride with a GPS offset that is not aligned keeps the braking error of the low-pass grade.

The scripts of the analysis are in `analysis/terrain/`. Its `README.md` shows how to run them.
