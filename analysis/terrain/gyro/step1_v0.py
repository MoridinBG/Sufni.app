"""Step 1: reproduce today's window grade (V0) from raw samples and validate against stored window grades."""
import csv, os, sys; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gcommon import *

print("=== Step 1: V0 reproduction ===")
allp, alls, allc = [], [], []
for idx in range(len(CS)):
    r = Ride(idx)
    if not r.has_imu or r.entry["profile"]["GradeState"] != 0:
        continue
    x = per_segment(r, lambda s: lowpass(r.acc[s, 0] - r.lacc[s] / G0, r.rate, 0.08))
    z = per_segment(r, lambda s: lowpass(r.acc[s, 2], r.rate, 0.08))
    samp = np.sin(-np.arctan2(x, z))
    g, tg, tw = r.window_grades(samp)
    zero = r.o1_offset(g, tg, tw)
    py = np.array([np.nan if v is None else v - zero for v in g])
    st = np.array([np.nan if w.get("Grade") is None else w["Grade"] for w in r.ws])
    rows = list(csv.DictReader(open(os.path.join(GYRO, "ext", f"r{idx:02d}", "windows.csv"))))
    cs = np.array([float(q["grade"]) if q["grade"] else np.nan for q in rows])
    same_windows = len(rows) == len(r.ws) and all(
        abs(float(q["start"]) - w["StartSeconds"]) < 1e-9 for q, w in zip(rows, r.ws))
    m = np.isfinite(py) & np.isfinite(st)
    dp = np.degrees(np.arcsin(py[m])) - np.degrees(np.arcsin(st[m]))
    dc = np.degrees(np.arcsin(cs[m])) - np.degrees(np.arcsin(st[m]))
    print(f"ride {idx:2d}: windows={m.sum():4d} same window cuts={same_windows} "
          f"Python RMS={np.sqrt(np.mean(dp ** 2)):.2e} deg, C# RMS={np.sqrt(np.mean(dc ** 2)):.2e} deg, "
          f"grade-window sets equal={np.array_equal(np.isfinite(py), np.isfinite(st))}")
    allp.append(np.degrees(np.arcsin(py[m]))); alls.append(np.degrees(np.arcsin(st[m]))); allc.append(np.degrees(np.arcsin(cs[m])))
p, s, c = map(np.concatenate, (allp, alls, allc))
print(f"ALL: n={len(p)} Python vs stored: r={np.corrcoef(p, s)[0, 1]:.6f} RMS={np.sqrt(np.mean((p - s) ** 2)):.2e} deg max={np.abs(p - s).max():.2e}; "
      f"C# recompute vs stored RMS={np.sqrt(np.mean((c - s) ** 2)):.2e} deg")
