"""Baseline variants on the 0.15 Hz grade: how close each is to the grade other runs measure at the same place.

Reference of a window = mean 10 m grade of the matched windows of the other runs (independent noise).
Edge windows: the reference differs by more than 2 deg from the mean reference of the two windows on each side.
Needs gyro/variants8.pkl from step8_variants.py.
Usage: MATCH_M=6 MINREF=2 VAR=A0.15 step9_baseline.py. MINREF is the least number of other runs at a place.
"""
import os, pickle, sys; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from collections import defaultdict
from gcommon import *
from gmatch import rides, match
from step8_eval import hysteresis, switches_per_km

V = pickle.load(open(os.path.join(GYRO, "variants8.pkl"), "rb"))
VR = V["rides"]; EDEG = [float(e) for e in EDGES_DEG]

def neigh(ws, i, reach):
    out = []
    for d in (-1, 1):
        for o in range(1, reach + 1):
            j = i + d * o
            if j < 0 or j >= len(ws): break
            lo = min(j, j - d)
            if not adjacent(ws[lo], ws[lo + 1]): break
            out.append(j)
    return out

def make(kind, reach, need):
    def f(ws, g, i):
        if g[i] is None: return None
        v = [g[i]] + [g[j] for j in neigh(ws, i, reach) if g[j] is not None]
        if len(v) < need: return None
        return float(np.median(v)) if kind == "median" else float(np.mean(v))
    return f

def tri(ws, g, i):
    if g[i] is None: return None
    s, n = 3 * g[i], 3
    for d in (-1, 1):
        for o, wt in ((1, 2), (2, 1)):
            j = i + d * o
            if j < 0 or j >= len(ws): break
            lo = min(j, j - d)
            if not adjacent(ws[lo], ws[lo + 1]): break
            if g[j] is not None: s += wt * g[j]; n += wt
    return s / n if n >= 5 else None

METHODS = {"10m": make("mean", 0, 1), "mean5 (before)": make("mean", 2, 3), "mean3": make("mean", 1, 2),
           "median5 (today)": make("median", 2, 3), "median3": make("median", 1, 2), "tri5 1-2-3-2-1": tri}

VAR = os.environ.get("VAR", "A0.15")
vals = {}
for k in VR:
    ws = rides[k]["ws"]; g = [None if (v is None or not w["Moving"]) else v for w, v in zip(ws, VR[k]["grades"][VAR]["O1"])]
    vals[k] = {}
    for name, f in METHODS.items():
        vals[k][name] = [None if x is None else deg(x) for x in (f(ws, g, q) for q in range(len(ws)))]
    m3 = [make("median", 1, 2)(ws, g, q) for q in range(len(ws))]
    vals[k]["median3>mean3"] = [None if x is None else deg(x) for x in (make("mean", 1, 2)(ws, m3, q) for q in range(len(ws)))]

pairs, _ = match(float(os.environ.get("MATCH_M", "6")))
other = defaultdict(list)
for a, b, i, j in pairs:
    if a in VR and b in VR:
        if vals[b]["10m"][j] is not None: other[(a, i)].append(vals[b]["10m"][j])
        if vals[a]["10m"][i] is not None: other[(b, j)].append(vals[a]["10m"][i])
MINREF = int(os.environ.get("MINREF", "2"))
ref = {key: float(np.mean(v)) for key, v in other.items() if len(v) >= MINREF}
# edge windows: reference differs from the reference mean of its +-2 neighbours by over 2 deg
edge = set()
for (a, i), r in ref.items():
    nb = [ref.get((a, j)) for j in neigh(rides[a]["ws"], i, 2)]
    nb = [x for x in nb if x is not None]
    if len(nb) >= 3 and abs(r - np.mean(nb)) > 2: edge.add((a, i))
band = lambda v: sum(v >= e for e in EDEG)
print(f"variant {VAR}; reference windows with {MINREF}+ other runs {len(ref)} (edge {len(edge)})")
print(f"{'method':16s}| RMS vs ref all  edge | mean abs edge | same band as ref | runs noise | sw/km (hyst)")
NAMES = [*METHODS, "median3>mean3"]
for name in NAMES:
    e_all, e_edge, sb = [], [], []
    for (a, i), r in ref.items():
        v = vals[a][name][i]
        if v is None: continue
        e_all.append(v - r); sb.append(band(v) == band(r))
        if (a, i) in edge: e_edge.append(v - r)
    xa, xb = [], []
    for a, b, i, j in pairs:
        if a in VR and b in VR and vals[a][name][i] is not None and vals[b][name][j] is not None:
            xa.append(vals[a][name][i]); xb.append(vals[b][name][j])
    noise = (np.array(xa) - np.array(xb)).std(ddof=1) / math.sqrt(2)
    labs = {k: hysteresis(rides[k]["ws"], vals[k][name], EDEG, 1.0) for k in VR}
    sw = switches_per_km([(rides[k]["ws"], labs[k]) for k in VR])
    print(f"{name:16s}| {np.sqrt(np.mean(np.square(e_all))):.2f}  {np.sqrt(np.mean(np.square(e_edge))):.2f} | {np.mean(np.abs(e_edge)):.2f} | {np.mean(sb):.3f} | {noise:.2f} | {sw:.2f}")
print("\nMSM (ride 25) window mid seconds, degrees")
ws = rides[25]["ws"]
print("t      " + " ".join(f"{n:>14s}" for n in NAMES) + "   ref")
for i, w in enumerate(ws):
    mid = (w["StartSeconds"] + w["EndSeconds"]) / 2
    if 98 <= mid <= 118:
        r = ref.get((25, i))
        print(f"{mid:6.1f} " + " ".join(f"{(vals[25][n][i] if vals[25][n][i] is not None else float('nan')):14.1f}" for n in NAMES) + f"   {'' if r is None else f'{r:.1f}'}")
