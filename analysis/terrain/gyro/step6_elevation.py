"""Step 6: GPS elevation as a grade input.

6a raw elevation: fix rate, quantisation step, SD of the second difference along time (/sqrt6 = white-noise level).
6b elevation slope vs IMU grade over N = 5, 11, 21, 31 consecutive windows (50, 110, 210, 310 m) centred on each
   matched window: elevation drop / distance over exactly those windows (TrackMotion elevation and distance), IMU = mean
   of the same windows. Same-place noise per run and signal.
6c fused = LP_L(elevation slope) + (IMU - LP_L(IMU)), LP_L = moving mean over L m of distance (truncated at ride ends).
6d pooled elevation on the main trail group: slope over 50 m (reference windows i-2..i+2) averaged over N runs; two
   disjoint sets of N runs; noise per set = SD(set1 - set2)/sqrt2.
"""
import itertools, os, pickle, sys; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from collections import defaultdict
from scipy.spatial import cKDTree
from gcommon import *
from gmatch import rides, match, track_fn
from ghelp import hysteresis, switches_per_km, noise_signal, EDEG

VR = pickle.load(open(os.path.join(GYRO, "variants.pkl"), "rb"))["rides"]
BEST = "V2_2+S"

# ---------- 6a ----------
print("=== 6a raw GPS elevation (track points as stored) ===")
for idx, (sid, s, e) in enumerate(CS):
    if idx == 1: continue
    arr = D["tracks"][e["key"]["Content"]["FullTrackId"]]
    arr = arr[np.argsort(arr[:, 0])]
    z = arr[:, 3]; ok = np.isfinite(z)
    dt = np.median(np.diff(arr[:, 0]))
    dz = np.diff(z[ok]); nz = np.abs(dz[dz != 0])
    d2 = z[ok][2:] - 2 * z[ok][1:-1] + z[ok][:-2]
    print(f"ride {idx:2d}: fixes {len(arr):5d} rate {1 / dt:.1f} Hz elevation present {ok.mean():.2f} smallest step {nz.min() if len(nz) else np.nan:.4f} m "
          f"repeats {np.mean(dz == 0):.2f}  SD(2nd diff) {d2.std():.3f} m -> white-noise level {d2.std() / math.sqrt(6):.3f} m")

# ---------- per ride geometry ----------
R_ = {k: Ride(k) for k in range(len(CS)) if k != 1}
GEO = {}
for k, r in R_.items():
    ws = r.ws
    st = np.array([w["StartSeconds"] for w in ws]); en = np.array([w["EndSeconds"] for w in ws])
    e0, d0 = r.motion_at(st); e1, d1 = r.motion_at(en)
    g = r.grid
    good = np.isfinite(g[:, 3])
    GEO[k] = dict(e0=e0, e1=e1, d0=d0, d1=d1, dc=(d0 + d1) / 2, gd=g[good, 2], ge=g[good, 3])


def span_values(k, name, half):
    """Per window: (elevation slope deg, IMU mean deg) over windows i-half..i+half, all adjacent, moving, graded."""
    ws = rides[k]["ws"]; G = GEO[k]
    g = VR[k]["grades"][name]["O1"] if (k in VR and name) else None
    el, im = [None] * len(ws), [None] * len(ws)
    for i in range(half, len(ws) - half):
        rng = range(i - half, i + half + 1)
        if any(not adjacent(ws[q - 1], ws[q]) for q in rng if q > i - half): continue
        if any(not ws[q]["Moving"] for q in rng): continue
        a, b = i - half, i + half
        drop, dist = G["e0"][a] - G["e1"][b], G["d1"][b] - G["d0"][a]
        if np.isfinite(drop) and dist > 0:
            el[i] = deg(drop / dist)
        if g is not None and all(g[q] is not None for q in rng):
            im[i] = deg(np.mean([g[q] for q in rng]))
    return el, im


def ns(pairs, va, key=None):
    xa = [va[a][i] for a, b, i, j in pairs]; xb = [va[b][j] for a, b, i, j in pairs]
    return noise_signal(xa, xb)


for mm in (6.0, 3.0):
    pairs, _ = match(mm)
    gp = [(a, b, i, j) for a, b, i, j in pairs if a in VR and b in VR
          and VR[a]["grades"]["V0"]["O1"][i] is not None and VR[b]["grades"]["V0"]["O1"][j] is not None]
    print(f"\n=== 6b match {mm} m: elevation slope vs IMU grade over the same windows (deg; noise/run, signal; n) ===")
    for half, lab in ((2, "50 m"), (5, "110 m"), (10, "210 m"), (15, "310 m")):
        el = {k: span_values(k, "V0", half) for k in VR}
        eb = {k: span_values(k, BEST, half) for k in VR}
        sel = [p for p in gp if all(v is not None for v in (el[p[0]][0][p[2]], el[p[1]][0][p[3]], el[p[0]][1][p[2]],
                                                            el[p[1]][1][p[3]], eb[p[0]][1][p[2]], eb[p[1]][1][p[3]]))]
        n1 = ns(sel, {k: el[k][0] for k in VR}); n2 = ns(sel, {k: el[k][1] for k in VR}); n3 = ns(sel, {k: eb[k][1] for k in VR})
        print(f"  {lab:6s} n={len(sel):4d}  GPS elevation {n1[0]:.2f} / {n1[1]:.2f}   V0 IMU {n2[0]:.2f} / {n2[1]:.2f}   {BEST} {n3[0]:.2f} / {n3[1]:.2f}")

# ---------- 6c fused ----------


def fused(k, name, L):
    """Window values (sin) of LP_L(elevation slope) + (IMU - LP_L(IMU)); L=None: whole ride."""
    ws = rides[k]["ws"]; G = GEO[k]
    g = VR[k]["grades"][name]["O1"]
    dc = G["dc"]
    has = np.array([v is not None for v in g])
    gv = np.array([v if v is not None else np.nan for v in g])
    lo_d, hi_d = np.nanmin(G["d0"]), np.nanmax(G["d1"])
    out = [None] * len(ws)
    for i in range(len(ws)):
        if g[i] is None: continue
        if L is None:
            a, b = lo_d, hi_d
        else:
            a, b = max(lo_d, dc[i] - L / 2), min(hi_d, dc[i] + L / 2)
        m = has & (dc >= a) & (dc <= b)
        ea, eb_ = np.interp([a, b], G["gd"], G["ge"])
        slope = (ea - eb_) / (b - a)
        out[i] = slope + gv[i] - gv[m].mean()
    return out


def evaluate_values(vals, gp):
    """vals: k -> list of sin values. Returns dict of the 6c figures."""
    w10 = {k: [None if v is None else deg(v) for v in vals[k]] for k in vals}
    b50 = {}
    for k in vals:
        ws = rides[k]["ws"]; mp = {id(w): v for w, v in zip(ws, vals[k])}
        rd = lambda w, mp=mp: mp[id(w)] if w["Moving"] else None
        b50[k] = [None if (v := baseline(ws, i, rd)) is None else deg(v) for i in range(len(ws))]
    s10 = [p for p in gp if w10[p[0]][p[2]] is not None and w10[p[1]][p[3]] is not None]
    s50 = [p for p in s10 if b50[p[0]][p[2]] is not None and b50[p[1]][p[3]] is not None]
    res = dict(n10=len(s10), n50=len(s50))
    res["w10"] = ns(s10, w10); res["b50"] = ns(s50, b50)
    dd = defaultdict(list)
    for a, b, i, j in s10: dd[(a, b)].append(w10[a][i] - w10[b][j])
    res["pair_sd"] = np.std([np.mean(v) for v in dd.values()], ddof=1)
    labs = {k: hysteresis(rides[k]["ws"], b50[k], EDEG, 1.0) for k in vals}
    res["sw"] = switches_per_km([(rides[k]["ws"], labs[k]) for k in vals])
    res["same"] = np.mean([labs[a][i] == labs[b][j] for a, b, i, j in s50])
    return res


for mm in (6.0, 3.0):
    pairs, _ = match(mm)
    gp = [(a, b, i, j) for a, b, i, j in pairs if a in VR and b in VR]
    print(f"\n=== 6c match {mm} m: fused grade; noise/run / signal for 10 m and 50 m, SD pair-mean d, +-1 deg hysteresis on the 50 m band ===")
    for name in ("V0", BEST):
        rows = [(f"{name} O1 (reference)", {k: VR[k]["grades"][name]["O1"] for k in VR}),
                (f"{name} O2 (reference)", {k: VR[k]["grades"][name]["O2"] for k in VR})]
        rows += [(f"{name} fused L={L} m", {k: fused(k, name, L) for k in VR}) for L in (100, 200, 300, 500)]
        rows += [(f"{name} fused whole ride", {k: fused(k, name, None) for k in VR})]
        for lab, vals in rows:
            q = evaluate_values(vals, gp)
            print(f"  {lab:26s} 10 m {q['w10'][0]:.2f} / {q['w10'][1]:.2f} (n={q['n10']})  50 m {q['b50'][0]:.2f} / {q['b50'][1]:.2f} (n={q['n50']})  "
                  f"pairSD {q['pair_sd']:.2f}  sw/km {q['sw']:.2f}  same band {q['same']:.3f}")

# ---------- 6d pooled elevation ----------
print("\n=== 6d pooled GPS elevation, main trail group {2,4,7,8,9,11,12,13,14}, 50 m slope ===")
GROUP = [2, 4, 7, 8, 9, 11, 12, 13, 14]
SAMP = {}
for k in GROUP:
    r = R_[k]; e = CS[k][2]
    g = r.grid
    t = g[:, 1]
    x, y, _ = track_fn(e)(t)
    hd = np.arctan2(np.gradient(y), np.gradient(x))
    SAMP[k] = dict(x=x, y=y, z=g[:, 3], d=g[:, 2], hd=hd, tree=cKDTree(np.c_[x, y]))


def project(k, px, py, ph):
    S = SAMP[k]
    idx = S["tree"].query_ball_point([px, py], 6.0)
    best = None
    for q in idx:
        dh = abs((S["hd"][q] - ph + np.pi) % (2 * np.pi) - np.pi)
        if math.degrees(dh) > 45 or not np.isfinite(S["z"][q]): continue
        dist = math.hypot(S["x"][q] - px, S["y"][q] - py)
        if best is None or dist < best[0]: best = (dist, q)
    return None if best is None else best[1]


# Places: reference = ride 4 (longest of the group); 50 m stretches = start of window i-2 to end of window i+2.
ref = 4
wsr = rides[ref]["ws"]; f = track_fn(CS[ref][2])
st = np.array([w["StartSeconds"] for w in wsr]); en = np.array([w["EndSeconds"] for w in wsr])
sx, sy, _ = f(st); ex, ey, _ = f(en)
hd = rides[ref]["head"]
slopes = {k: {} for k in GROUP}
for i in range(2, len(wsr) - 2):
    if any(not wsr[q]["Moving"] for q in range(i - 2, i + 3)): continue
    if any(not adjacent(wsr[q - 1], wsr[q]) for q in range(i - 1, i + 3)): continue
    for k in GROUP:
        a = project(k, sx[i - 2], sy[i - 2], hd[i - 2]); b = project(k, ex[i + 2], ey[i + 2], hd[i + 2])
        if a is None or b is None or b <= a: continue
        S = SAMP[k]; dist = S["d"][b] - S["d"][a]
        if not 35 <= dist <= 65: continue
        slopes[k][i] = deg((S["z"][a] - S["z"][b]) / dist)
print("places with a slope per run:", {k: len(v) for k, v in slopes.items()})
for N in (1, 2, 3):
    diffs, combos = [], 0
    for s1 in itertools.combinations(GROUP, N):
        rest = [k for k in GROUP if k not in s1]
        for s2 in itertools.combinations(rest, N):
            if s1 > s2: continue
            common = set.intersection(*(set(slopes[k]) for k in s1 + s2))
            if len(common) < 10: continue
            combos += 1
            for i in common:
                diffs.append(np.mean([slopes[k][i] for k in s1]) - np.mean([slopes[k][i] for k in s2]))
    d = np.array(diffs)
    print(f"  N={N}: set pairs {combos}, place comparisons {len(d)}, noise per set {np.sqrt(np.mean(d ** 2)) / math.sqrt(2):.2f} deg")
