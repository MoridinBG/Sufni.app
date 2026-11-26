"""Check T: does the frame IMU roughness reading depend on the suspension tune? Stored profiles only.

Per ride: median over moving, non-climb windows with usable fork and frame readings of ln(frame / fork), for Level and the
three bands. Tune = (front spring, rear spring, front/rear HSC, LSC, LSR, HSR) from the session row.
Matched windows (6 m, as cdgh): per ride pair, mean of d ln(frame Level) and of d ln(fork Level); SD across pairs.
"""
import os, sys; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from collections import defaultdict
from gcommon import *
from common import usable
from gmatch import rides, match

KEYS = ("Level", "LowBand", "MiddleBand", "HighBand")
FIELDS = ("front spring", "rear spring", "f HSC", "r HSC", "f LSC", "r LSC", "f LSR", "r LSR", "f HSR", "r HSR")
per, combos = {}, {}
for k, r in enumerate(rides):
    if k == 1: continue
    vals = {q: [] for q in KEYS}
    for w in r["ws"]:
        if not is_descent(w) or not usable(w.get("Frame")) or not usable(w.get("Fork")): continue
        for q in KEYS:
            if w["Frame"][q] > 0 and w["Fork"][q] > 0:
                vals[q].append(math.log(w["Frame"][q] / w["Fork"][q]))
    if len(vals["Level"]) < 20: continue
    combos.setdefault(r["tune"], len(combos))
    per[k] = ({q: float(np.median(v)) for q, v in vals.items()}, len(vals["Level"]))
print("=== T: tune combinations (values as stored) ===")
for t, c in sorted(combos.items(), key=lambda x: x[1]):
    print(f"  T{c}: " + ", ".join(f"{n}={v}" for n, v in zip(FIELDS, t)))
print("\nride  tune  windows  median ln(frame/fork): Level  Low  Middle  High")
for k, (m, n) in per.items():
    print(f"  {k:2d}   T{combos[rides[k]['tune']]}   {n:5d}   " + "  ".join(f"{m[q]:+.3f}" for q in KEYS))
print("\nbetween-combination SD (of combination means) vs within-combination SD (pooled, rides sharing a tune)")
for q in KEYS:
    groups = defaultdict(list)
    for k, (m, n) in per.items(): groups[combos[rides[k]["tune"]]].append(m[q])
    means = [np.mean(v) for v in groups.values()]
    within = [x - np.mean(v) for v in groups.values() for x in v]
    dof = sum(len(v) - 1 for v in groups.values())
    print(f"  {q:10s} between SD {np.std(means, ddof=1):.3f} over {len(means)} combinations; within SD "
          f"{math.sqrt(sum(x * x for x in within) / dof) if dof else float('nan'):.3f} (dof {dof}); group sizes {sorted(len(v) for v in groups.values())}")

pairs, pair_km = match(6.0)
acc = defaultdict(lambda: {"Frame": [], "Fork": []})
for a, b, i, j in pairs:
    if a == 1 or b == 1: continue
    for s in ("Frame", "Fork"):
        wa, wb = rides[a]["ws"][i].get(s), rides[b]["ws"][j].get(s)
        if usable(wa) and usable(wb):
            acc[(a, b)][s].append(math.log(wa["Level"]) - math.log(wb["Level"]))
print("\n=== T: matched windows (6 m), pair-mean d ln(Level); pairs with >= 20 usable windows per sensor ===")
for same in (True, False):
    for s in ("Frame", "Fork"):
        m = [np.mean(v[s]) for (a, b), v in acc.items() if len(v[s]) >= 20 and (rides[a]["tune"] == rides[b]["tune"]) == same]
        print(f"  {'same tune' if same else 'different tune'} {s:5s}: pairs {len(m):2d}, SD of pair means {np.std(m, ddof=1):.3f}, mean |pair mean| {np.mean(np.abs(m)):.3f}")
# frame relative to fork on the same matched windows (removes the shared terrain and speed)
print("\n  d ln(frame/fork) on windows with both sensors usable in both runs:")
rel = defaultdict(list)
for a, b, i, j in pairs:
    if a == 1 or b == 1: continue
    A, B = rides[a]["ws"][i], rides[b]["ws"][j]
    if all(usable(x.get(s)) for x in (A, B) for s in ("Frame", "Fork")):
        rel[(a, b)].append(math.log(A["Frame"]["Level"] / A["Fork"]["Level"]) - math.log(B["Frame"]["Level"] / B["Fork"]["Level"]))
for same in (True, False):
    m = [(k, np.mean(v)) for k, v in rel.items() if len(v) >= 20 and (rides[k[0]]["tune"] == rides[k[1]]["tune"]) == same]
    print(f"  {'same tune' if same else 'different tune'}: pairs {len(m)}, SD of pair means {np.std([x for _, x in m], ddof=1):.3f}; "
          + ", ".join(f"{a}-{b}:{x:+.2f}" for (a, b), x in m))
