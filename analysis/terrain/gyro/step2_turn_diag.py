"""Diagnostic: is the V2 - V0 window difference tied to turning? Window-level, 18 grade rides, moving windows."""
import os, sys, pickle; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gcommon import *
V = pickle.load(open(os.path.join(GYRO, "variants.pkl"), "rb"))["rides"]
Y, YAW, LEAN2, RIDE = [], [], [], []
for idx in sorted(V):
    r = Ride(idx)
    roll = np.arctan2(r.att["repoUy"], r.att["repoUz"])
    for i, q in enumerate(r.window_ranges()):
        a, b = V[idx]["grades"]["V0"]["O1"][i], V[idx]["grades"]["V2_4"]["O1"][i]
        if q is None or a is None or b is None: continue
        Y.append(deg(b) - deg(a)); YAW.append(V[idx]["yaw"][i]); LEAN2.append(np.degrees(np.sqrt(np.mean(roll[q[1]:q[2]] ** 2)))); RIDE.append(idx)
Y, YAW, L = map(np.array, (Y, YAW, LEAN2)); RIDE = np.array(RIDE)
Y = Y - np.array([Y[RIDE == k].mean() for k in RIDE])  # remove per-ride offset difference
for name, x in (("mean |yaw rate| dps", YAW), ("RMS roll deg", L)):
    q = np.quantile(x, [0, .25, .5, .75, 1])
    rows = []
    for lo, hi in zip(q[:-1], q[1:]):
        m = (x >= lo) & (x <= hi)
        rows.append(f"[{lo:.0f}-{hi:.0f}] mean {Y[m].mean():+.2f} SD {Y[m].std():.2f}")
    print(f"V2_4 - V0 (deg, per-ride mean removed) by quartile of {name}: " + "; ".join(rows))
print("n windows", len(Y))
