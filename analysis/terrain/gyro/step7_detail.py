"""Step 7 detail: where G2 (k=3) drops fixes for rides 17 and 28, and why W differs from G0 on ride 19."""
import os, sys, pickle; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gcommon import *
for k in (17, 28):
    f = np.fromfile(os.path.join(GYRO, "raw", f"r{k:02d}.fix"), float).reshape(-1, 4); f = f[np.argsort(f[:, 0])]
    med = np.array([np.median(f[np.abs(f[:, 0] - t) <= 15, 3]) for t in f[:, 0]])
    bad = np.abs(f[:, 3] - med) > 3
    r = Ride(k); mov = [w for w in r.ws if w["Moving"]]
    print(f"ride {k}: end points {mov[0]['StartSeconds']:.1f} / {mov[-1]['EndSeconds']:.1f} s; dropped fixes at t = "
          f"{np.round(f[bad, 0], 1).tolist()} departures {np.round((f[:, 3] - med)[bad], 1).tolist()}")
r = Ride(19); ws = r.ws
mv = [i for i, w in enumerate(ws) if w["Moving"]]
print(f"ride 19: windows {len(ws)}, first/last moving index {mv[0]}/{mv[-1]}; first/last window start/end "
      f"{ws[0]['StartSeconds']:.1f}/{ws[-1]['EndSeconds']:.1f} s vs moving {ws[mv[0]]['StartSeconds']:.1f}/{ws[mv[-1]]['EndSeconds']:.1f} s")
e, d = r.motion_at(np.array([ws[0]['StartSeconds'], ws[mv[0]]['StartSeconds'], ws[mv[-1]]['EndSeconds'], ws[-1]['EndSeconds']]))
print("  elevation at window0 start / first moving / last moving / last window:", np.round(e, 1), "distance", np.round(d, 0))
