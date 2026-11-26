"""Step 2 prerequisite: establish the pitch-rate gyro axis and sign from the data.

Reference: accelerometer pitch (downhill positive, GPS longitudinal acceleration removed), band-limited to 0.05-1 Hz,
differentiated. Each gyro axis is band-limited the same way; regression slope and correlation per axis, moving samples.
Also: per-axis gyro bias (median over the whole ride) and the copy-vs-repository check of the attitude estimator.
"""
import os, sys; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gcommon import *

print("=== Gyro axis and sign ===")
tot = {k: [] for k in range(3)}
for idx in range(len(CS)):
    r = Ride(idx)
    if not r.has_imu or r.entry["profile"]["GradeState"] != 0:
        continue
    xc = r.acc[:, 0] - r.lacc / G0
    th = -np.arctan2(xc, r.acc[:, 2])
    bl = lambda v, s: lowpass(v[s], r.rate, 1.0) - lowpass(v[s], r.rate, 0.05)
    dth = per_segment(r, lambda s: np.gradient(bl(th, s)) * r.rate)
    mv = r.valid & (r.speed > 1)
    line = []
    for k, ax in enumerate("xyz"):
        w = per_segment(r, lambda s: bl(r.gyr[:, k], s))
        c = np.corrcoef(dth[mv], w[mv])[0, 1]
        slope = (dth[mv] @ w[mv]) / (w[mv] @ w[mv])
        tot[k].append((c, slope))
        line.append(f"g{ax}: r={c:+.3f} slope={slope:+.3f} bias={np.degrees(np.median(r.gyr[:, k])):+.3f} dps")
    d = np.abs(r.att["repoUx"] - r.att["tau4Ux"]).max()
    print(f"ride {idx:2d}: " + "  ".join(line) + f"  | max |repo-copy| Up.X={d:.1e}")
for k, ax in enumerate("xyz"):
    a = np.array(tot[k])
    print(f"axis {ax}: median r={np.median(a[:, 0]):+.3f}, median slope={np.median(a[:, 1]):+.3f}")
