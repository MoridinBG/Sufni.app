"""Projected class agreement of N-run averages if run noise is independent and Gaussian.

Signal: class values of every usable moving non-climb window of every ride, shrunk toward the mean so that the
noise variance sigma^2 is removed. Two N-run averages = truth + N(0, sigma^2/N) each. Read-only on ../data.pkl.
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from common import *

MB = {"Fork": 43, "Frame": 27}
SIG = {"Fork": (0.71, 0.76), "Frame": (0.64, 0.67)}  # trail-index N=1 noise at 6 m, earlier pair noise
rng = np.random.default_rng(3)
for kind in ("Fork", "Frame"):
    x = []
    for sid, s, e in computed_sessions():
        for w in e["profile"]["Windows"]:
            r = w.get(kind)
            if is_descent(w) and usable(r):
                x.append(class_value(r["SpeedRemoved"], MB[kind]))
    x = np.array(x); V = x.var(); m = x.mean()
    for sig in SIG[kind]:
        k = math.sqrt(max(0.0, (V - sig ** 2) / V))
        t = m + (x - m) * k
        line = []
        for N in (1, 2, 3, 4, 8):
            reps = 20
            tt = np.tile(t, reps)
            a = tt + rng.normal(0, sig / math.sqrt(N), tt.size)
            b = tt + rng.normal(0, sig / math.sqrt(N), tt.size)
            ca = np.digitize(a, [-1, 0, 1, 2]); cb = np.digitize(b, [-1, 0, 1, 2]); ct = np.digitize(tt, [-1, 0, 1, 2])
            line.append(f"N={N}: same={np.mean(ca == cb):.3f} adj={np.mean(abs(ca - cb) <= 1):.3f} avg=true class {np.mean(ca == ct):.3f}")
        print(f"{kind} windows={len(x)} SD single-run class value={math.sqrt(V):.3f} sigma={sig} signal SD={math.sqrt(V) * k:.3f}")
        print("   " + "; ".join(line))
