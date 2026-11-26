import sys, os; sys.path.insert(0, os.path.dirname(__file__))
from collections import Counter, defaultdict
import datetime as dt
from scipy.spatial import cKDTree
from common import *

R = 6378137.0
MATCH_M = float(os.environ.get("MATCH_M", 6))
HEAD_DEG = 45
CS = computed_sessions()


def merc_to_ll(x, y):
    return np.degrees(x / R), np.degrees(2 * np.arctan(np.exp(y / R)) - np.pi / 2)


# One global local frame for every ride.
lat0 = lon0 = None
for sid, s, e in CS:
    tid = e["key"]["Content"].get("FullTrackId")
    if tid in D["tracks"] and len(D["tracks"][tid]):
        lon0, lat0 = merc_to_ll(D["tracks"][tid][0, 1], D["tracks"][tid][0, 2])
        break
K = math.cos(math.radians(lat0))


def track_fn(e):
    c = e["key"]["Content"]
    arr = D["tracks"][c["FullTrackId"]]
    origin = c["Timestamp"] + (c.get("WindowOriginFraction") or 0) + (c.get("GpsOffsetSeconds") or 0)
    o = np.argsort(arr[:, 0])
    t = arr[o, 0] - origin
    lon, lat = merc_to_ll(arr[o, 1], arr[o, 2])
    ex = R * K * np.radians(lon - lon0)
    ny = R * np.radians(lat - lat0)
    z = arr[o, 3]
    good = np.isfinite(z)
    return lambda q: (np.interp(q, t, ex), np.interp(q, t, ny),
                      np.interp(q, t[good], z[good]) if good.sum() > 1 else np.full(np.shape(q), np.nan))


rides = []
for idx, (sid, s, e) in enumerate(CS):
    ws = e["profile"]["Windows"]
    f = track_fn(e)
    st = np.array([w["StartSeconds"] for w in ws]); en = np.array([w["EndSeconds"] for w in ws])
    cx, cy, _ = f((st + en) / 2)
    sx, sy, sz = f(st); ex_, ey, ez = f(en)
    head = np.arctan2(ey - sy, ex_ - sx)
    # acceleration from neighbour window speeds (contiguous only)
    n = len(ws)
    acc = np.full(n, np.nan)
    mid = (st + en) / 2
    for i in range(1, n - 1):
        if adjacent(ws[i - 1], ws[i]) and adjacent(ws[i], ws[i + 1]) and ws[i - 1]["Moving"] and ws[i + 1]["Moving"]:
            acc[i] = (ws[i + 1]["MeanSpeed"] - ws[i - 1]["MeanSpeed"]) / (mid[i + 1] - mid[i - 1])
    # near a stop: within 15 s of a non-moving window of the same ride
    stops = [(w["StartSeconds"], w["EndSeconds"]) for w in ws if not w["Moving"]]
    near = np.array([any(a - 15 <= w["EndSeconds"] and w["StartSeconds"] <= b + 15 for a, b in stops) for w in ws])
    rd = lambda w: w.get("Grade") if w["Moving"] else None
    bl = [baseline(ws, i, rd) for i in range(n)]
    rides.append(dict(idx=idx, sid=sid, date=dt.datetime.utcfromtimestamp(s["timestamp"]).strftime("%Y-%m-%d"),
                      ws=ws, cx=cx, cy=cy, head=head, acc=acc, near=near, bl=bl, sz=sz, ez=ez,
                      grade=e["profile"]["GradeState"] == 0, tune=s["tune"]))


def ok(w):
    return is_descent(w)


# ---------- C: matching ----------
pairs = []  # (ra, rb, i, j)
pair_km = {}
for a in range(len(rides)):
    A = rides[a]
    ia = [i for i, w in enumerate(A["ws"]) if ok(w)]
    if not ia: continue
    for b in range(a + 1, len(rides)):
        B = rides[b]
        ib = np.array([j for j, w in enumerate(B["ws"]) if ok(w)])
        if not len(ib): continue
        tree = cKDTree(np.c_[B["cx"][ib], B["cy"][ib]])
        d, k = tree.query(np.c_[A["cx"][ia], A["cy"][ia]], distance_upper_bound=MATCH_M)
        m = []
        used = set()
        for i, dd, kk in sorted(zip(ia, d, k), key=lambda t: t[1]):
            if not np.isfinite(dd): continue
            j = int(ib[kk])
            if j in used: continue
            dh = abs((A["head"][i] - B["head"][j] + np.pi) % (2 * np.pi) - np.pi)
            if math.degrees(dh) > HEAD_DEG: continue
            used.add(j)
            m.append((i, j))
        if len(m) >= 20:  # at least 200 m in common
            pairs += [(a, b, i, j) for i, j in m]
            pair_km[(a, b)] = len(m) * 10 / 1000
print(f"=== C: matching (centre within {MATCH_M} m, heading within {HEAD_DEG} deg, one-to-one, >=200 m per pair) ===")
print("ride pairs", len(pair_km), "matched km", round(sum(pair_km.values()), 2), "window pairs", len(pairs))
rides_in = Counter()
for (a, b), km in pair_km.items(): rides_in[a] += 1; rides_in[b] += 1
print("rides that take part", len(rides_in), "pair km distribution", sorted(round(v, 2) for v in pair_km.values()))
# trail groups: connected components of the pair graph
parent = list(range(len(rides)))
def find(x):
    while parent[x] != x: parent[x] = parent[parent[x]]; x = parent[x]
    return x
for a, b in pair_km: parent[find(a)] = find(b)
groups = defaultdict(list)
for r in rides_in: groups[find(r)].append(r)
print("groups (ride indices):", sorted(sorted(g) for g in groups.values()))
same_day = sum(1 for (a, b) in pair_km if rides[a]["date"] == rides[b]["date"])
print("pairs on the same day", same_day, "same tune", sum(1 for (a, b) in pair_km if rides[a]["tune"] == rides[b]["tune"]))


# ---------- D helpers ----------
def stats(xa, xb, label, unit=1.0):
    xa = np.asarray(xa, float); xb = np.asarray(xb, float)
    d = xa - xb
    noise = d.std(ddof=1) / math.sqrt(2)
    sig = ((xa + xb) / 2).std(ddof=1)
    sig_c = math.sqrt(max(0.0, sig ** 2 - noise ** 2 / 2))
    print(f"  {label}: n={len(d)} noise/run={noise / unit:.3f} signal(pair mean)={sig / unit:.3f} "
          f"signal(noise-corrected)={sig_c / unit:.3f} ratio={sig / noise:.2f} corr-ratio={sig_c / noise:.2f} mean d={d.mean() / unit:.3f}")
    return noise, sig


def agg(r, i, sensor, half):
    ws = r["ws"]
    if i - half < 0 or i + half >= len(ws): return None
    vals = []
    for k in range(i - half, i + half + 1):
        w = ws[k]
        if k > i - half and not adjacent(ws[k - 1], w): return None
        rr = w.get(sensor)
        if not ok(w) or not usable(rr): return None
        vals.append(rr["SpeedRemoved"] ** 2)
    return math.log(math.sqrt(sum(vals) / len(vals)))


print("\n=== D: repeatability (moving, non-climb in both runs) ===")
L15 = math.log(1.5)
for sensor in ("Fork", "Frame"):
    print(sensor, "ln(SpeedRemoved); unit = class (ln 1.5)")
    for half, lab in ((0, "10 m"), (1, "30 m"), (2, "50 m")):
        xa, xb = [], []
        for a, b, i, j in pairs:
            va = agg(rides[a], i, sensor, half); vb = agg(rides[b], j, sensor, half)
            if va is not None and vb is not None: xa.append(va); xb.append(vb)
        if len(xa) > 10:
            n, sg = stats(xa, xb, lab + " (class units)", L15)
    # speed exponent: d ln Level on d ln speed
    y, x, grp = [], [], []
    for a, b, i, j in pairs:
        wa, wb = rides[a]["ws"][i], rides[b]["ws"][j]
        if usable(wa.get(sensor)) and usable(wb.get(sensor)):
            y.append(math.log(wa[sensor]["Level"]) - math.log(wb[sensor]["Level"]))
            x.append(math.log(wa["MeanSpeed"]) - math.log(wb["MeanSpeed"]))
            grp.append((a, b))
    y = np.array(y); x = np.array(x)
    X = np.c_[np.ones_like(x), x]
    beta, res, *_ = np.linalg.lstsq(X, y, rcond=None)
    resid = y - X @ beta
    s2 = resid @ resid / (len(y) - 2)
    se = math.sqrt(s2 * np.linalg.inv(X.T @ X)[1, 1])
    # cluster bootstrap over ride pairs
    rng = np.random.default_rng(1)
    keys = sorted(set(grp)); gi = defaultdict(list)
    for k, g in enumerate(grp): gi[g].append(k)
    boots = []
    for _ in range(1000):
        sel = np.concatenate([gi[keys[q]] for q in rng.integers(0, len(keys), len(keys))])
        bb, *_ = np.linalg.lstsq(X[sel], y[sel], rcond=None); boots.append(bb[1])
    r2 = 1 - resid @ resid / ((y - y.mean()) @ (y - y.mean()))
    # through origin too
    b0 = (x @ y) / (x @ x)
    print(f"  speed exponent {sensor}: slope={beta[1]:.3f} +- {se:.3f} (OLS SE), +- {np.std(boots):.3f} (pair bootstrap), "
          f"no-intercept slope={b0:.3f}, R2={r2:.3f}, n={len(y)}, SD dln(speed)={x.std():.3f}")

# grade
print("grade (degrees), grade rides only")
gp = [(a, b, i, j) for a, b, i, j in pairs if rides[a]["grade"] and rides[b]["grade"]
      and rides[a]["ws"][i].get("Grade") is not None and rides[b]["ws"][j].get("Grade") is not None]
ga = np.array([deg(rides[a]["ws"][i]["Grade"]) for a, b, i, j in gp]); gb = np.array([deg(rides[b]["ws"][j]["Grade"]) for a, b, i, j in gp])
stats(ga, gb, "window grade")
bp = [(a, b, i, j) for a, b, i, j in gp if rides[a]["bl"][i] is not None and rides[b]["bl"][j] is not None]
stats([deg(rides[a]["bl"][i]) for a, b, i, j in bp], [deg(rides[b]["bl"][j]) for a, b, i, j in bp], "baseline grade")
# decomposition of grade difference into per-pair offset and within-pair scatter
dd = defaultdict(list)
for (a, b, i, j), x1, x2 in zip(gp, ga, gb): dd[(a, b)].append(x1 - x2)
means = np.array([np.mean(v) for v in dd.values()])
within = np.sqrt(np.mean(np.concatenate([np.array(v) - np.mean(v) for v in dd.values()]) ** 2))
print(f"  window grade d: SD of per-pair mean d = {means.std(ddof=1):.3f} deg over {len(means)} pairs; "
      f"within-pair RMS = {within:.3f} deg (per run /sqrt2: {within / math.sqrt(2):.3f})")
# drivers
rows = []
for (a, b, i, j), x1, x2 in zip(gp, ga, gb):
    A, B = rides[a], rides[b]
    wa, wb = A["ws"][i], B["ws"][j]
    if not (np.isfinite(A["acc"][i]) and np.isfinite(B["acc"][j])): continue
    tr = None
    if wa.get("Front") and wa.get("Rear") and wb.get("Front") and wb.get("Rear"):
        tr = (wa["Front"]["MeanTravel"] - wa["Rear"]["MeanTravel"]) - (wb["Front"]["MeanTravel"] - wb["Rear"]["MeanTravel"])
    rows.append((x1 - x2, A["acc"][i] - B["acc"][j], wa["MeanSpeed"] - wb["MeanSpeed"], tr,
                 A["near"][i] or B["near"][j], (a, b)))
y = np.array([r[0] for r in rows]); da = np.array([r[1] for r in rows]); dv = np.array([r[2] for r in rows])


def ols(cols, y, names):
    X = np.c_[np.ones(len(y)), *cols]
    beta, *_ = np.linalg.lstsq(X, y, rcond=None)
    res = y - X @ beta
    s2 = res @ res / (len(y) - X.shape[1])
    se = np.sqrt(np.diag(s2 * np.linalg.inv(X.T @ X)))
    r2 = 1 - res @ res / ((y - y.mean()) @ (y - y.mean()))
    print("   ", ", ".join(f"{n}={b:.3f}+-{s:.3f}" for n, b, s in zip(["icpt"] + names, beta, se)), f"R2={r2:.3f} n={len(y)}")


print(" grade-difference drivers (window grade, deg):")
ols([da], y, ["d accel (deg per m/s2)"])
ols([dv], y, ["d speed (deg per m/s)"])
ols([da, dv], y, ["d accel", "d speed"])
sel = np.array([r[3] is not None for r in rows])
dt_ = np.array([r[3] for r in rows if r[3] is not None], float)
ols([dt_], y[sel], ["d(front-rear travel) (deg per mm)"])
ols([da[sel], dv[sel], dt_], y[sel], ["d accel", "d speed", "d(front-rear) mm"])
print(f"   SD d accel={da.std():.3f} m/s2, SD d speed={dv.std():.3f} m/s, SD d(front-rear)={dt_.std():.2f} mm")
near = np.array([r[4] for r in rows])
print(f" stops: noise/run near stop (<=15 s) = {y[near].std(ddof=1) / math.sqrt(2):.3f} deg (n={near.sum()}), "
      f"others = {y[~near].std(ddof=1) / math.sqrt(2):.3f} deg (n={(~near).sum()})")
# same with all gp (not only with accel)
nr = np.array([rides[a]["near"][i] or rides[b]["near"][j] for a, b, i, j in gp]); dg = ga - gb
print(f" stops (all grade pairs): near={dg[nr].std(ddof=1) / math.sqrt(2):.3f} (n={nr.sum()}), others={dg[~nr].std(ddof=1) / math.sqrt(2):.3f} (n={(~nr).sum()})")

# ---------- G: IMU grade vs GPS elevation ----------
print("\n=== G: IMU grade vs GPS elevation, non-overlapping 300 m spans of contiguous moving windows ===")
xs, ys, per = [], [], []
for r in rides:
    if not r["grade"]: continue
    ws = r["ws"]; run = []
    def flush(run):
        for k in range(0, len(run) - 29, 30):
            seg = run[k:k + 30]
            if any(ws[q].get("Grade") is None for q in seg): continue
            h0, h1 = r["sz"][seg[0]], r["ez"][seg[-1]]
            if not (np.isfinite(h0) and np.isfinite(h1)): continue
            xs.append(deg(np.mean([ws[q]["Grade"] for q in seg]))); ys.append(deg((h0 - h1) / 300.0)); per.append(r["idx"])
    for i, w in enumerate(ws):
        if w["Moving"] and (not run or adjacent(ws[run[-1]], w)): run.append(i)
        else:
            flush(run); run = [i] if w["Moving"] else []
    flush(run)
xs = np.array(xs); ys = np.array(ys)
X = np.c_[np.ones_like(xs), xs]; beta, *_ = np.linalg.lstsq(X, ys, rcond=None); res = ys - X @ beta
print(f"spans={len(xs)} rides={len(set(per))} gain={beta[1]:.3f} intercept={beta[0]:.3f} deg R2={1 - res @ res / ((ys - ys.mean()) @ (ys - ys.mean())):.3f} "
      f"residual SD={res.std(ddof=2):.3f} deg; RMS(elev - imu)={np.sqrt(np.mean((ys - xs) ** 2)):.3f} deg; IMU grade SD={xs.std():.2f}")

# ---------- H: rear RMS velocity response fit ----------
print("\n=== H: ln(rear RMS velocity) ~ 1 + ln(low, mid, high bands) + ln(speed), pooled, moving non-climb ===")
def fit(sensor, require_both):
    Y, Xr = [], []
    for r in rides:
        for w in r["ws"]:
            if not w["Moving"] or (w.get("Band") is not None and w["Band"] in CLIMB_BANDS): continue
            if not w.get("Rear") or w["Rear"]["RmsVelocity"] <= 0: continue
            rr = w.get(sensor)
            if rr is None: continue
            if require_both and not (w.get("Fork") and w.get("Frame")): continue
            v = [rr["LowBand"], rr["MiddleBand"], rr["HighBand"], w["MeanSpeed"]]
            if min(v) <= 0: continue
            Y.append(math.log(w["Rear"]["RmsVelocity"])); Xr.append([1] + [math.log(q) for q in v])
    Y = np.array(Y); X = np.array(Xr)
    beta, *_ = np.linalg.lstsq(X, Y, rcond=None); res = Y - X @ beta
    r2 = 1 - res @ res / ((Y - Y.mean()) @ (Y - Y.mean()))
    print(f"  {sensor} bands{' (common windows)' if require_both else ' (all windows)'}: n={len(Y)} R2={r2:.3f} "
          f"residual SD={res.std(ddof=X.shape[1]):.3f} (ln units) coef={np.round(beta, 3).tolist()}")
for both in (True, False):
    fit("Frame", both); fit("Fork", both)

# ---------- I: stretch of N consecutive readings (the anchor estimator) ----------
print("\n=== I: repeatability of N consecutive 10 m readings of one place, non-overlapping stretches ===")
by_pair = defaultdict(list)
for a, b, i, j in pairs: by_pair[(a, b)].append((i, j))


def stretches(sensor, n):
    out = []
    for (a, b), m in by_pair.items():
        A, B = rides[a]["ws"], rides[b]["ws"]
        run = []
        def flush(run):
            for k in range(0, len(run) - n + 1, n): out.append(run[k:k + n])
        for i, j in sorted(m):
            wa, wb = A[i], B[j]
            good = ok(wa) and ok(wb) and usable(wa.get(sensor)) and usable(wb.get(sensor))
            if run and (not good or i != run[-1][0] + 1 or not adjacent(A[i - 1], A[i])):
                flush(run); run = []
            if good: run.append((i, math.log(wa[sensor]["SpeedRemoved"]), math.log(wb[sensor]["SpeedRemoved"])))
        flush(run)
    return out


for sensor in ("Fork", "Frame"):
    print(sensor, "ln(SpeedRemoved); unit = class (ln 1.5)")
    for n in (1, 3, 5, 10, 20):
        st = stretches(sensor, n)
        if len(st) < 10: continue
        xa = np.array([[q[1] for q in s] for s in st]); xb = np.array([[q[2] for q in s] for s in st])
        dmean = xa.mean(1) - xb.mean(1); dmed = np.median(xa, 1) - np.median(xb, 1)
        print(f"  {n * 10} m: stretches={len(st)} noise/run of the mean={dmean.std(ddof=1) / math.sqrt(2) / L15:.3f} "
              f"of the median={dmed.std(ddof=1) / math.sqrt(2) / L15:.3f}")
