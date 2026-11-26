import sys, os; sys.path.insert(0, os.path.dirname(__file__))
from collections import Counter
import datetime as dt
from common import *

CS = computed_sessions()


# ---------- E: scale drift ----------
def ride_dist(ws, sensor):
    bins = Counter()
    for w in ws:
        if not is_descent(w):
            continue
        r = w.get(sensor)
        if usable(r):
            bins[bin_of(r["SpeedRemoved"])] += 10
    return bins


def scale(dists):
    rides = [d for d in dists if d]
    merged = Counter()
    for d in rides:
        merged.update(d)
    total = sum(merged.values())
    if len(rides) < 3 or total < 5000:
        return None
    c = 0
    for b in sorted(merged):
        c += merged[b]
        if c >= total / 2:
            return b


print("=== E: scale drift (single setup) ===")
final = {}
for sensor in ("Fork", "Frame"):
    dists, seq = [], []
    for i, (sid, s, e) in enumerate(CS):
        dists.append(ride_dist(e["profile"]["Windows"], sensor))
        seq.append(scale(dists))
    final[sensor] = seq[-1]
    contrib = sum(1 for d in dists if d)
    print(sensor, "contributing rides", contrib, "bins after each ride:", seq)
    vals = [b for b in seq if b is not None]
    changes = [(i, seq[i - 1], seq[i]) for i in range(1, len(seq)) if seq[i - 1] is not None and seq[i] != seq[i - 1]]
    print(sensor, "changes", len(changes), "range bins", (max(vals) - min(vals)) if vals else None,
          "range class units", (max(vals) - min(vals)) / 20 if vals else None)
    if sensor == "Fork":
        for i, a, b in changes:
            # share of all earlier descent windows (fork usable, Level>0) whose fork class changes a -> b
            n = ch = 0
            for sid, s, e in CS[:i]:
                for w in e["profile"]["Windows"]:
                    r = w.get("Fork")
                    if not is_descent(w) or r is None or r["Level"] <= 0:
                        continue
                    n += 1
                    ch += classify_value(class_value(r["SpeedRemoved"], a)) != classify_value(class_value(r["SpeedRemoved"], b))
            print(f"  after ride {i}: bin {a}->{b}, earlier fork descent windows {n}, class changed share {ch / n:.3f}")
    else:
        for i, a, b in changes:
            n = ch = 0
            for sid, s, e in CS[:i]:
                for w in e["profile"]["Windows"]:
                    r = w.get("Frame")
                    if not is_descent(w) or r is None or r["Level"] <= 0:
                        continue
                    n += 1
                    ch += classify_value(class_value(r["SpeedRemoved"], a)) != classify_value(class_value(r["SpeedRemoved"], b))
            print(f"  after ride {i}: bin {a}->{b}, earlier frame descent windows {n}, class changed share {ch / n:.3f}")
print("final medians", final, "(stored key: Frame 27, Fork 43)")
FORK_MB = final["Fork"]


# ---------- B helpers ----------
def chains(ws, label):
    """Split into maximal runs of time-contiguous windows that all carry a label (not None)."""
    out, cur = [], []
    for i, w in enumerate(ws):
        if label[i] is None:
            if cur: out.append(cur); cur = []
            continue
        if cur and not adjacent(ws[cur[-1]], w):
            out.append(cur); cur = []
        cur.append(i)
    if cur: out.append(cur)
    return out


def switch_stats(rides, name):
    """rides: list of (ws, labels). Returns dict of stats."""
    sw = 0; km = 0; skip = 0; runlen = Counter(); aba = 0; interior = 0; abba = 0
    for ws, lab in rides:
        for ch in chains(ws, lab):
            km += len(ch) * 10 / 1000
            runs = []
            for k, i in enumerate(ch):
                if k and lab[i] != lab[ch[k - 1]]:
                    sw += 1
                    skip += abs(lab[i] - lab[ch[k - 1]]) > 1
                if not runs or runs[-1][0] != lab[i]:
                    runs.append([lab[i], 1])
                else:
                    runs[-1][1] += 1
            for r in runs:
                b = "1" if r[1] == 1 else "2" if r[1] == 2 else "3-5" if r[1] <= 5 else ">5"
                runlen[b] += r[1]
            for j in range(1, len(runs) - 1):
                interior += 1
                if runs[j - 1][0] == runs[j + 1][0]:
                    if runs[j][1] == 1: aba += 1
                    if runs[j][1] == 2: abba += 1
    tot = sum(runlen.values())
    return dict(name=name, km=round(km, 2), switches=sw, per_km=round(sw / km, 2) if km else None,
                run_share={k: round(runlen[k] / tot, 3) for k in ("1", "2", "3-5", ">5")},
                interior_runs=interior, ABA_share=round(aba / interior, 3) if interior else None,
                ABBA_share=round(abba / interior, 3) if interior else None,
                skip_share=round(skip / sw, 3) if sw else None)


def edge_distance(rides, values, edges):
    """For each switch, distance of the new window's value from the nearest crossed edge."""
    d = []
    for (ws, lab), val in zip(rides, values):
        for ch in chains(ws, lab):
            for k in range(1, len(ch)):
                i, j = ch[k - 1], ch[k]
                if lab[i] == lab[j] or val[j] is None: continue
                lo, hi = sorted((lab[i], lab[j]))
                crossed = edges[lo:hi]
                d.append(min(abs(val[j] - e) for e in crossed))
    d = np.array(d)
    return dict(n=len(d), within={t: round(float((d <= t).mean()), 3) for t in (0.25, 0.5, 1, 2)} if len(d) else None,
                median=round(float(np.median(d)), 3) if len(d) else None)


def hysteresis(ws, val, edges, h):
    lab = [None] * len(ws)
    prev = None
    for i in range(len(ws)):
        v = val[i]
        if v is None: prev = None; continue
        if prev is None or not adjacent(ws[i - 1], ws[i]) or lab[i - 1] is None:
            b = 0
            while b < len(edges) and v >= edges[b]: b += 1
        else:
            b = lab[i - 1]
            while b < len(edges) and v >= edges[b] + h: b += 1
            while b > 0 and v < edges[b - 1] - h: b -= 1
        lab[i] = b; prev = b
    return lab


def plain(val, edges):
    out = []
    for v in val:
        if v is None: out.append(None); continue
        b = 0
        while b < len(edges) and v >= edges[b]: b += 1
        out.append(b)
    return out


def rms(a, b):
    x = np.array([p - q for p, q in zip(a, b) if p is not None and q is not None])
    return round(float(np.sqrt((x ** 2).mean())), 3), len(x)


# ---------- B: grade ----------
print("\n=== B: grade bands ===")
EDEG = [float(e) for e in EDGES_DEG]
grade_rides = [e["profile"]["Windows"] for _, _, e in CS if e["profile"]["GradeState"] == 0]
print("rides with grade", len(grade_rides))
stored, rawb, base_deg, hyst, hyst_m, wide, wide_m, wide_val = [], [], [], [], [], [], [], []
for ws in grade_rides:
    rd = lambda w: w.get("Grade") if w["Moving"] else None
    bl = [baseline(ws, i, rd) for i in range(len(ws))]
    bd = [deg(b) if b is not None else None for b in bl]
    base_deg.append(bd)
    stored.append((ws, [w.get("Band") for w in ws]))
    pb = plain(bd, EDEG)
    rawb.append((ws, pb))
    hl = hysteresis(ws, bd, EDEG, 1.0)
    hyst.append((ws, hl)); hyst_m.append((ws, merge_outliers(ws, hl)))
    bl4 = [baseline(ws, i, rd, reach=4, need=3) for i in range(len(ws))]
    b4 = [deg(b) if b is not None else None for b in bl4]
    wide_val.append(b4)
    p4 = plain(b4, EDEG)
    wide.append((ws, p4)); wide_m.append((ws, merge_outliers(ws, p4)))
# check stored band == merge(plain(baseline))
mism = sum(sum(1 for a, b in zip(merge_outliers(ws, pb), [w.get("Band") for w in ws]) if a != b) for (ws, pb) in rawb)
print("rebuild check: windows where merge(plain(baseline)) != stored Band:", mism)
for r in (stored, rawb, hyst, hyst_m, wide, wide_m):
    pass
print(switch_stats(stored, "stored bands (today, after merge)"))
print(switch_stats(rawb, "today before merge"))
print("edge distance (deg), today stored:", edge_distance(stored, base_deg, EDEG))
print(switch_stats(hyst, "(1) hysteresis +-1 deg, no merge"))
print(switch_stats(hyst_m, "(1) hysteresis +-1 deg + merge"))
print(switch_stats(wide, "(2) mean +-4 neighbours, no merge"))
print(switch_stats(wide_m, "(2) mean +-4 neighbours + merge"))
allb = [x for r in base_deg for x in r]; allw = [x for r in wide_val for x in r]
print("(2) RMS change of baseline deg vs today:", rms(allw, allb))

# ---------- B: roughness (fork alone, final fork median) ----------
print("\n=== B: roughness classes, fork SpeedRemoved, median bin", FORK_MB, "===")
CEDGES = [-1.0, 0.0, 1.0, 2.0]
r_stored, r_raw, r_val, r_h, r_hm, r_m3, r_m3m, r_m3val = [], [], [], [], [], [], [], []
fork_rides = [e["profile"]["Windows"] for _, _, e in CS if any(w.get("Fork") for w in e["profile"]["Windows"])]
print("rides with fork IMU", len(fork_rides))
for ws in fork_rides:
    val = [class_value(w["Fork"]["SpeedRemoved"], FORK_MB) if is_descent(w) and w.get("Fork") and w["Fork"]["Level"] > 0 else None
           for w in ws]
    r_val.append(val)
    pl = plain(val, CEDGES)
    r_raw.append((ws, pl)); r_stored.append((ws, merge_outliers(ws, pl)))
    hl = hysteresis(ws, val, CEDGES, 0.25)
    r_h.append((ws, hl)); r_hm.append((ws, merge_outliers(ws, hl)))
    # 3-window rolling mean of log level (contiguous, readings present)
    lg = [math.log(w["Fork"]["SpeedRemoved"]) if v is not None else None for w, v in zip(ws, val)]
    m3 = [None] * len(ws)
    for i in range(len(ws)):
        if lg[i] is None: continue
        acc = [lg[i]]
        if i > 0 and lg[i - 1] is not None and adjacent(ws[i - 1], ws[i]): acc.append(lg[i - 1])
        if i + 1 < len(ws) and lg[i + 1] is not None and adjacent(ws[i], ws[i + 1]): acc.append(lg[i + 1])
        m3[i] = (sum(acc) / len(acc) - math.log(median_level(FORK_MB))) / math.log(STEP) + 0.5
    r_m3val.append(m3)
    p3 = plain(m3, CEDGES)
    r_m3.append((ws, p3)); r_m3m.append((ws, merge_outliers(ws, p3)))
print(switch_stats(r_stored, "today (classify + merge)"))
print(switch_stats(r_raw, "today before merge"))
print("edge distance (class units), today:", edge_distance(r_stored, r_val, CEDGES))
print(switch_stats(r_h, "(3) hysteresis +-0.25, no merge"))
print(switch_stats(r_hm, "(3) hysteresis +-0.25 + merge"))
print(switch_stats(r_m3, "(4) 3-window mean log level, no merge"))
print(switch_stats(r_m3m, "(4) 3-window mean log level + merge"))
print("(4) RMS change of class value vs today:", rms([x for r in r_m3val for x in r], [x for r in r_val for x in r]))
dist = Counter(x for _, lab in r_stored for x in lab if x is not None)
print("class distance share (today, fork alone):", {k: round(v / sum(dist.values()), 3) for k, v in sorted(dist.items())})

# ---------- F: clipping ----------
print("\n=== F: clipping (descent windows) ===")
for sensor in ("Fork", "Frame"):
    rows = []
    for _, _, e in CS:
        for w in e["profile"]["Windows"]:
            if is_descent(w) and w.get(sensor) and w[sensor]["Level"] > 0:
                rows.append((w[sensor]["ClippedShare"], w.get("Fork")["Level"] if w.get("Fork") and w["Fork"]["Level"] > 0 else None))
    cl = np.array([r[0] for r in rows])
    print(sensor, "n", len(cl), "any clipped", round(float((cl > 0).mean()), 4), ">0.02", round(float((cl > 0.02).mean()), 4),
          "max", round(float(cl.max()), 4))
    withfork = [(c, f) for c, f in rows if f is not None]
    if withfork:
        fl = np.array([f for _, f in withfork]); cc = np.array([c for c, _ in withfork])
        top = fl >= np.quantile(fl, 0.9)
        print("  within roughest 10% by fork Level (n", int(top.sum()), "): any", round(float((cc[top] > 0).mean()), 4),
              ">0.02", round(float((cc[top] > 0.02).mean()), 4))
