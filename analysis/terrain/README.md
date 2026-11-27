# Terrain analysis scripts

These scripts measured the values in the "Measured basis" section of `docs/architecture/terrain.md`. They are not a part of the app. No build and no test uses them.

The scripts read a copy of the app database. They never write to it. The first analysis described terrain profiles of algorithm version 9. The grade cutoff and baseline grade analyses of 2026-11-27 (steps 12 to 14 of the gyroscope trial) ran on version 11, after GPS alignment.

## Input

1. Close the app.
2. Copy the app database `sst.db` from the app data folder of your platform to this folder. Name the copy `snap.db`.

For steps 12 to 15 of the gyroscope trial, first align the GPS of all rides with the align GPS command of the session list. If the copy keeps the write-ahead log mode, `sqlite3 snap.db "pragma journal_mode=delete"` lets the scripts open it read-only.

The database copy and all output files hold ride data. `.gitignore` keeps them out of the repository.

## Setup

Python 3.14 with the packages in `requirements.txt`:

```
python3 -m venv venv
venv/bin/pip install -r requirements.txt
```

The export tool in `gyro/cs/Extract` uses the .NET SDK of the repository and the `Sufni.Telemetry` project.

## Run order

Run each command from this folder. Each script prints its result. Redirect the output to a `.txt` file to keep it.

| Order | Command | Result |
|---|---|---|
| 1 | `venv/bin/python load.py` | Decodes the profiles, the session rows and the tracks to `data.pkl`. All other scripts need it. |
| 2 | `venv/bin/python a_inventory.py` | The data set: rides, distance and sensors of each ride. |
| 3 | `venv/bin/python bef_switching_scale_clipping.py` | Band and class changes per km, the hysteresis, the longer mean, the drift of the setup median, the clipped share. |
| 4 | `venv/bin/python cdgh_matching_repeatability.py` | Same-place matching, the noise of a reading, the noise of the median of a stretch of readings, the speed exponents, the IMU grade against the GPS elevation, the fit of the shock velocity. |
| 5 | `venv/bin/python pairs_check.py` | The matched ride pairs, with the time overlap of each pair. |
| 6 | `venv/bin/python trail/trail_average.py` | The mean of N runs of a place: noise, same-class share, class changes, fork against frame. |
| 7 | `venv/bin/python trail/model_agreement.py` | The projected class agreement of N-run means. |

`cdgh_matching_repeatability.py` and `trail/trail_average.py` read the match limit in metres from the environment variable `MATCH_M`. The default is 6. `trail/trail_average.py` also reads `REF0`, the ride index of the reference ride of the main trail group.

The gyroscope and elevation trial needs the raw samples:

| Order | Command | Result |
|---|---|---|
| 1 | `venv/bin/python gyro/export.py` | Writes the telemetry and the GPS fixes of each ride to `gyro/raw`. |
| 2 | `dotnet run --project gyro/cs/Extract -- gyro/raw gyro/ext` | Writes the samples, the attitude, the windows and the grade pieces of each ride to `gyro/ext`. The fixes carry the receiver speed of the telemetry. |
| 3 | `venv/bin/python gyro/step1_v0.py` | Checks that the Python grade equals the stored grade. |
| 4 | `venv/bin/python gyro/step2_axis.py` | Finds the pitch axis and the sign of the gyroscope. |
| 5 | `venv/bin/python gyro/step2_variants.py` | Calculates the grade variants to `gyro/variants.pkl`. |
| 6 | `venv/bin/python gyro/step2_turn_diag.py` | Relates the gyroscope error to turns. |
| 7 | `venv/bin/python gyro/step4_eval.py` | Compares the variants: noise, offsets, band changes. |
| 8 | `venv/bin/python gyro/step5_tune.py` | Tests the effect of the tune on the levels. |
| 9 | `venv/bin/python gyro/step6_elevation.py` | Tests the GPS elevation as a grade input, alone and fused. |
| 10 | `venv/bin/python gyro/step7_guards.py` | Tests guards on the two end points of the ride offset. |
| 11 | `venv/bin/python gyro/step7_detail.py` | Shows the detail of the rides that a guard changed. |
| 12 | `venv/bin/python gyro/step8_variants.py` | Calculates the grade at 0.08, 0.15 and 0.3 Hz, with suspension pitch, without the braking correction and with the gyroscope, to `gyro/variants8.pkl`. |
| 13 | `venv/bin/python gyro/step8_eval.py` | Compares the variants of step 12: noise between runs, detail, band changes, same band, elevation fit. |
| 14 | `venv/bin/python gyro/step9_baseline.py` | Compares baseline grades (mean, median, weighted mean) with the grade that other runs measured at the same place. |
| 15 | `venv/bin/python gyro/step10_pieces.py gyro/ext/<run>` | Reads the grade pieces that step 2 wrote to `gyro/ext/<run>`: band changes per km, the noise and the same-band share of matched windows, and the piece edge and grade at each row of `marks.csv`. |

To compare two algorithm versions, run step 2 once for each version, each into its own folder under `gyro/ext`, and run step 15 on each folder. An older version needs its own checkout of the repository, for example a `git worktree`.

`marks.csv` lists moments seen on video: the ride by its start in UTC (`YYYY-MM-DD HH:MM`), the second on the session timeline and a note.

`gyro/step8_eval.py`, `gyro/step9_baseline.py` and `gyro/step10_pieces.py` read `MATCH_M`. `gyro/step9_baseline.py` also reads `MINREF`, the least number of other runs at a place (default 2), and `VAR`, the grade variant of step 12 (default `A0.15`). Both print the windows of one ride by its ride index, set in the script.

`gyro/gcommon.py`, `gyro/ghelp.py`, `gyro/gmatch.py` and `common.py` are shared helpers.

## Limits

- The ride index in every output is the position of the ride in time order. It changes when rides are added or deleted.
- `common.py` and `gyro/gcommon.py` hold Python copies of app rules: the baseline grade, the band comparison, the outlier merge, the class limits and the grade path. Update a copy when the app rule changes. `bef_switching_scale_clipping.py` and `gyro/step1_v0.py` compare the copies with the stored values and report a difference.
- The scripts assume one setup. With more setups, group the rides by setup first.
- No ride in the measured data set has a rear axle IMU. The scripts read the frame and the fork readings only.
- The app takes the median baseline grade from algorithm version 12, and `common.py` follows it. The figures of version 9 and of the cutoff analysis used the mean, which `baseline(..., median=False)` gives. `gyro/step8_eval.py` and the longer mean of `bef_switching_scale_clipping.py` pass it. The other scripts now give median figures.
- `gyro/step1_v0.py` uses the grade cutoff of 0.15 Hz of algorithm version 11.
- The app uses a hysteresis on the grade bands from algorithm version 10. `common.py` holds the plain comparison of version 9, so the rebuild check of `bef_switching_scale_clipping.py` reports the windows that the hysteresis changed.
