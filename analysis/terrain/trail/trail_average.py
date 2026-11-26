"""Trail average trial: how well does the mean of N runs describe a place on a trail?

Read-only on ../data.pkl (decoded from snap.db by ../load.py). Usage: MATCH_M=6 python trail_average.py
Ride index = time order of computed sessions (same as the earlier scripts). Output goes to stdout.
"""
import sys, os
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
from collections import Counter, defaultdict
import datetime as dt
from scipy.spatial import cKDTree
from common import *

R = 6378137.0
MATCH_M = float(os.environ.get("MATCH_M", 6))
HEAD_DEG = 45
MIN_PAIR = 20  # >= 200 m in common, as in the earlier matching
L15 = math.log(STEP)
MB = {"Fork": 43, "Frame": 27}
SIGMA_PRIOR = {"Fork": 0.76, "Frame": 0.67, "Grade": 1.48}
GROUPS = [[2, 4, 7, 8, 9, 11, 12, 13, 14], [3, 10], [18, 22, 29], [19, 20, 24, 28], [23, 25, 26, 27]]
GEDGES = [float(e) for e in EDGES_DEG]
CEDGES = [-1.0, 0.0, 1.0, 2.0]
CS = computed_sessions()
rng = np.random.default_rng(7)


def merc_to_ll(x, y):
    return np.degrees(x / R), np.degrees(2 * np.arctan(np.exp(y / R)) - np.pi / 2)


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
    return lambda q: (np.interp(q, t, ex), np.interp(q, t, ny))


rides = {}
for idx, (sid, s, e) in enumerate(CS):
    ws = e["profile"]["Windows"]
    f = track_fn(e)
    st = np.array([w["StartSeconds"] for w in ws]); en = np.array([w["EndSeconds"] for w in ws])
    cx, cy = f((st + en) / 2)
    sx, sy = f(st); ex_, ey = f(en)
    rides[idx] = dict(idx=idx, ws=ws, cx=cx, cy=cy, head=np.arctan2(ey - sy, ex_ - sx),
                      ts=s["timestamp"], date=dt.datetime.utcfromtimestamp(s["timestamp"]).strftime("%Y-%m-%d"),
                      tune=s["tune"], grade=e["profile"]["GradeState"] == 0)


def reading(r, j, kind):
    """ln SpeedRemoved (Fork/Frame) or window grade in degrees; None when not usable."""
    w = rides[r]["ws"][j]
    if not is_descent(w):
        return None
    if kind == "Grade":
        return deg(w["Grade"]) if rides[r]["grade"] and w.get("Grade") is not None else None
    rr = w.get(kind)
    return math.log(rr["SpeedRemoved"]) if usable(rr) else None


def to_class(x, kind):
    if kind == "Grade":
        b = 0
        while b < len(GEDGES) and x >= GEDGES[b]: b += 1
        return b
    return classify_value((x - math.log(median_level(MB[kind]))) / L15 + 0.5)


def unit(kind):
    return 1.0 if kind == "Grade" else L15


def match(a, b):
    """Reference windows of ride a -> windows of ride b, nearest centre, one-to-one, heading within HEAD_DEG."""
    A, B = rides[a], rides[b]
    ia = [i for i, w in enumerate(A["ws"]) if is_descent(w)]
    ib = np.array([j for j, w in enumerate(B["ws"]) if is_descent(w)])
    if not ia or not len(ib): return []
    tree = cKDTree(np.c_[B["cx"][ib], B["cy"][ib]])
    d, k = tree.query(np.c_[A["cx"][ia], A["cy"][ia]], distance_upper_bound=MATCH_M)
    m, used = [], set()
    for i, dd, kk in sorted(zip(ia, d, k), key=lambda t: t[1]):
        if not np.isfinite(dd): continue
        j = int(ib[kk])
        if j in used: continue
        dh = abs((A["head"][i] - B["head"][j] + np.pi) % (2 * np.pi) - np.pi)
        if math.degrees(dh) > HEAD_DEG: continue
        used.add(j); m.append((i, j))
    return m if len(m) >= MIN_PAIR else []


# ---------- place index ----------
print(f"######## MATCH_M = {MATCH_M} m (heading <= {HEAD_DEG} deg, one-to-one, >= {MIN_PAIR} windows per ride pair) ########")
PLACES = []  # dict(g, ref, i, runs={ride: window})
print("\n=== Place index ===")
for gi, G in enumerate(GROUPS):
    best = None
    forced = os.environ.get("REF0") if gi == 0 else None
    for ref in ([int(forced)] if forced else G):
        ms = {b: match(ref, b) for b in G if b != ref}
        cov = sum(len(m) for m in ms.values())
        if best is None or cov > best[1]: best = (ref, cov, ms)
    ref, cov, ms = best
    places = defaultdict(dict)
    for i, w in enumerate(rides[ref]["ws"]):
        if is_descent(w): places[i][ref] = i
    for b, m in ms.items():
        for i, j in m: places[i][b] = j
    for i in sorted(places):
        PLACES.append(dict(g=gi, ref=ref, i=i, runs=places[i]))
    # same physical place twice inside the reference (laps)
    ia = [i for i, w in enumerate(rides[ref]["ws"]) if is_descent(w)]
    P = np.c_[rides[ref]["cx"][ia], rides[ref]["cy"][ia]]
    pairs = cKDTree(P).query_pairs(MATCH_M)
    rep = sum(1 for p, q in pairs if abs(ia[p] - ia[q]) > 5 and
              math.degrees(abs((rides[ref]["head"][ia[p]] - rides[ref]["head"][ia[q]] + np.pi) % (2 * np.pi) - np.pi)) <= HEAD_DEG)
    print(f"group {gi} rides {G}: reference ride {ref} ({len(ia)} moving non-climb windows); matched windows per other ride "
          f"{ {b: len(m) for b, m in ms.items()} }; reference self-repeat window pairs {rep}")


def place_readings(kind):
    """list of (place, {ride: (value, speed)}) with >= 1 reading."""
    out = []
    for p in PLACES:
        rd = {}
        for r, j in p["runs"].items():
            v = reading(r, j, kind)
            if v is not None: rd[r] = (v, rides[r]["ws"][j]["MeanSpeed"])
        if rd: out.append((p, rd))
    return out


def coverage(kind, PR):
    print(f"\n--- {kind}: places by number of runs with a usable reading ---")
    for gi in range(len(GROUPS)):
        c = Counter(len(rd) for p, rd in PR if p["g"] == gi)
        if c: print(f"  group {gi}: " + ", ".join(f"{k}:{c[k]}" for k in sorted(c)))
    c = Counter(len(rd) for p, rd in PR)
    print("  all:     " + ", ".join(f"{k}:{c[k]}" for k in sorted(c)) + f"  (places >=2 runs: {sum(v for k, v in c.items() if k >= 2)})")


def block_key(p):
    return (p["g"], p["i"] // 10)  # 100 m blocks of the reference for bootstrap


def split_noise(kind, PR, Ns, min_runs=None, draws=200, label=""):
    """Item 1+2: disjoint random N-run sets per place; noise = SD(diff)/sqrt2 in class units; class agreement."""
    u = unit(kind)
    res = {}
    for N in Ns:
        need = min_runs if min_runs else 2 * N
        D_, same, adj, blocks = [], [], [], []
        np_ = 0
        for p, rd in PR:
            vals = np.array([v for v, s in rd.values()])
            if len(vals) < max(need, 2 * N): continue
            np_ += 1
            for _ in range(draws):
                perm = rng.permutation(len(vals))
                a = vals[perm[:N]].mean(); b = vals[perm[N:2 * N]].mean()
                D_.append((a - b) / u)
                ca, cb = to_class(a, kind), to_class(b, kind)
                same.append(ca == cb); adj.append(abs(ca - cb) <= 1)
                blocks.append(block_key(p))
        if np_ < 5:
            res[N] = None; continue
        D_ = np.array(D_); same = np.array(same); adj = np.array(adj)
        noise = D_.std() / math.sqrt(2)
        # block bootstrap over 100 m blocks of places
        bk = defaultdict(list)
        for k, b in enumerate(blocks): bk[b].append(k)
        keys = list(bk); bs = []
        for _ in range(200):
            sel = np.concatenate([bk[keys[q]] for q in rng.integers(0, len(keys), len(keys))])
            bs.append(D_[sel].std() / math.sqrt(2))
        res[N] = dict(places=np_, noise=noise, se=float(np.std(bs)), same=same.mean(), adj=adj.mean(), blocks=len(keys))
    s1 = res.get(1)["noise"] if res.get(1) else None
    print(f"  {label}")
    for N in Ns:
        r = res[N]
        if r is None:
            print(f"    N={N}: too few places"); continue
        print(f"    N={N}: places={r['places']:4d} (100 m blocks {r['blocks']:3d}) noise={r['noise']:.3f} +- {r['se']:.3f}  "
              f"prior/sqrtN={SIGMA_PRIOR[kind] / math.sqrt(N):.3f}  N1-here/sqrtN={(s1 / math.sqrt(N)) if s1 else float('nan'):.3f}  "
              f"same class={r['same']:.3f} same-or-adjacent={r['adj']:.3f}")
    return res


def pair_split(kind, PR):
    """N=1 noise for run pairs on the same day vs different days, same tune vs different tune."""
    u = unit(kind)
    cat = defaultdict(list)
    for p, rd in PR:
        rs = sorted(rd)
        for x in range(len(rs)):
            for y in range(x + 1, len(rs)):
                a, b = rs[x], rs[y]
                d = (rd[a][0] - rd[b][0]) / u
                cat["same day" if rides[a]["date"] == rides[b]["date"] else "different day"].append(d)
                cat["same tune" if rides[a]["tune"] == rides[b]["tune"] else "different tune"].append(d)
    print("  N=1 noise by pair type (all pairs at a place): " + "; ".join(
        f"{k}: {np.std(v) / math.sqrt(2):.3f} (n={len(v)})" for k, v in sorted(cat.items())))


def loo(kind, PR):
    """Item 3 + residual rows: run reading vs leave-one-out mean of the other runs at the place."""
    rows = []
    for p, rd in PR:
        if len(rd) < 2: continue
        for r, (v, s) in rd.items():
            ov = [x for q, x in rd.items() if q != r]
            mv = np.mean([x[0] for x in ov]); ms = np.mean([x[1] for x in ov])
            rows.append(dict(p=p, r=r, M=len(ov), v=v, m=mv, s=s, ms=ms, res=(v - mv) / unit(kind)))
    return rows


def item3(kind, rows, sigma):
    print(f"  item 3 ({kind}): run vs leave-one-out mean of the other M runs (class units / degrees)")
    for lab, sel in (("M=1", lambda m: m == 1), ("M=2", lambda m: m == 2), ("M=3", lambda m: m == 3),
                     ("M=4-5", lambda m: 4 <= m <= 5), ("M>=6", lambda m: m >= 6), ("M>=2 all", lambda m: m >= 2)):
        rr = [x for x in rows if sel(x["M"])]
        if len(rr) < 10:
            print(f"    {lab}: n={len(rr)} too few"); continue
        d = np.array([x["res"] for x in rr])
        Ms = np.array([x["M"] for x in rr])
        exp = sigma * math.sqrt(1 + np.mean(1 / Ms))
        eq = np.mean([to_class(x["v"], kind) == to_class(x["m"], kind) for x in rr])
        adj = np.mean([abs(to_class(x["v"], kind) - to_class(x["m"], kind)) <= 1 for x in rr])
        print(f"    {lab}: n={len(rr)} runs={len(set((x['p']['g'], x['r']) for x in rr))} SD={d.std():.3f} "
              f"(independent-noise expectation {exp:.3f}) mean={d.mean():+.3f} same class={eq:.3f} same-or-adjacent={adj:.3f}")


# ---------- item 4 helpers (from bef_switching_scale_clipping.py) ----------
def chains(ws, label):
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


def switch_stats(items):
    sw = 0; km = 0; runlen = Counter()
    for ws, lab in items:
        for ch in chains(ws, lab):
            km += len(ch) * 10 / 1000
            runs = []
            for k, i in enumerate(ch):
                if k and lab[i] != lab[ch[k - 1]]: sw += 1
                if not runs or runs[-1][0] != lab[i]: runs.append([lab[i], 1])
                else: runs[-1][1] += 1
            for r in runs:
                b = "1" if r[1] == 1 else "2" if r[1] == 2 else "3-5" if r[1] <= 5 else ">5"
                runlen[b] += r[1]
    tot = sum(runlen.values())
    return km, sw / km if km else float("nan"), {k: runlen[k] / tot if tot else float("nan") for k in ("1", "2", "3-5", ">5")}


def item4(kind, PR):
    print(f"  item 4 ({kind}): classes + A-B-A merge along each reference ride, places with >= 3 runs only")
    byg = defaultdict(dict)
    for p, rd in PR:
        if len(rd) >= 3: byg[p["g"]][p["i"]] = rd
    variants = {"one run (reference ride)": [], "3-run average (mean of 50 draws)": None, "all-run average": []}
    three = []
    for gi, d in byg.items():
        ref = GROUPS[gi] and next(p["ref"] for p in PLACES if p["g"] == gi)
        ws = rides[ref]["ws"]
        one = [None] * len(ws); allr = [None] * len(ws)
        for i, rd in d.items():
            vals = [v for v, s in rd.values()]
            allr[i] = to_class(np.mean(vals), kind)
            if ref in rd: one[i] = to_class(rd[ref][0], kind)
        # restrict one-run to the same windows as the averages (needs the reference reading)
        keep = [one[i] is not None for i in range(len(ws))]
        variants["one run (reference ride)"].append((ws, merge_outliers(ws, one)))
        variants["all-run average"].append((ws, merge_outliers(ws, [a if k else None for a, k in zip(allr, keep)])))
        three.append((gi, ws, d, keep))
    for name in ("one run (reference ride)", "all-run average"):
        km, pk, sh = switch_stats(variants[name])
        print(f"    {name}: km={km:.2f} switches/km={pk:.1f} distance share in runs 1:{sh['1']:.3f} 2:{sh['2']:.3f} 3-5:{sh['3-5']:.3f} >5:{sh['>5']:.3f}")
    acc = []
    for _ in range(50):
        items = []
        for gi, ws, d, keep in three:
            lab = [None] * len(ws)
            for i, rd in d.items():
                if not keep[i]: continue
                vals = [v for v, s in rd.values()]
                lab[i] = to_class(np.mean(rng.choice(vals, 3, replace=False)), kind)
            items.append((ws, merge_outliers(ws, lab)))
        acc.append(switch_stats(items))
    km = acc[0][0]; pk = np.mean([a[1] for a in acc])
    sh = {k: np.mean([a[2][k] for a in acc]) for k in ("1", "2", "3-5", ">5")}
    print(f"    3-run average (50 draws): km={km:.2f} switches/km={pk:.1f} distance share in runs 1:{sh['1']:.3f} 2:{sh['2']:.3f} 3-5:{sh['3-5']:.3f} >5:{sh['>5']:.3f}")


def tune_index():
    combos = []
    for r in sorted(rides):
        if rides[r]["tune"] not in combos: combos.append(rides[r]["tune"])
    return combos


TUNES = tune_index()


def boot_slope(x, y, cl, reps=1000):
    X = np.c_[np.ones_like(x), x]
    b, *_ = np.linalg.lstsq(X, y, rcond=None)
    keys = sorted(set(cl)); gi = defaultdict(list)
    for k, g in enumerate(cl): gi[g].append(k)
    bs = []
    for _ in range(reps):
        sel = np.concatenate([gi[keys[q]] for q in rng.integers(0, len(keys), len(keys))])
        bb, *_ = np.linalg.lstsq(X[sel], y[sel], rcond=None); bs.append(bb[1])
    return b, float(np.std(bs)), len(keys)


def item5(kind, rows):
    rr = [x for x in rows if x["M"] >= 2]
    x = np.array([math.log(q["s"] / q["ms"]) for q in rr]); y = np.array([q["res"] for q in rr])
    cl = [(q["p"]["g"], q["r"]) for q in rr]
    b, se, nc = boot_slope(x, y, cl)
    print(f"  item 5 ({kind}): residual (class units) on ln(run speed / mean speed of others), M>=2: slope={b[1]:+.3f} +- {se:.3f} "
          f"(bootstrap over {nc} runs) intercept={b[0]:+.3f} n={len(rr)} SD x={x.std():.3f}; "
          f"in ln units the slope is {b[1] * L15:+.3f} (exponent error)")
    per = defaultdict(list)
    for q in rr: per[(q["p"]["g"], q["r"])].append(q["res"])
    print(f"    per run mean residual (M>=2): group ride date tune n mean SE")
    out = {}
    for (g, r), v in sorted(per.items()):
        v = np.array(v)
        out[(g, r)] = (v.mean(), v.std(ddof=1) / math.sqrt(len(v)) if len(v) > 1 else float("nan"), len(v))
        print(f"      g{g} ride {r:2d} {rides[r]['date']} T{TUNES.index(rides[r]['tune'])} n={len(v):4d} "
              f"mean={v.mean():+.3f} SE={out[(g, r)][1]:.3f}")
    return out


def tune_test(name, per):
    """Within-group permutation test: does the between-tune spread of run mean residuals exceed chance?"""
    items = [(g, r, m, se, n) for (g, r), (m, se, n) in per.items() if n >= 20]
    def stat(lbl):
        # weighted between-combo sum of squares within each group
        tot = 0.0
        for g in set(i[0] for i in items):
            gi = [(lbl[k], items[k][2]) for k in range(len(items)) if items[k][0] == g]
            if len(set(t for t, _ in gi)) < 2: continue
            gm = np.mean([m for _, m in gi])
            for t in set(t for t, _ in gi):
                ms = [m for tt, m in gi if tt == t]
                tot += len(ms) * (np.mean(ms) - gm) ** 2
        return tot
    lbl = [TUNES.index(rides[i[1]]["tune"]) for i in items]
    obs = stat(lbl)
    cnt = 0; reps = 5000
    for _ in range(reps):
        perm = list(lbl)
        for g in set(i[0] for i in items):
            idx = [k for k in range(len(items)) if items[k][0] == g]
            vals = [perm[k] for k in idx]; rng.shuffle(vals)
            for k, v in zip(idx, vals): perm[k] = v
        cnt += stat(perm) >= obs - 1e-12
    spread = np.std([i[2] for i in items])
    print(f"    tune test {name}: runs={len(items)} (n>=20 places) SD of run means={spread:.3f} class; "
          f"between-tune SS={obs:.3f}; within-group permutation p={cnt / reps:.3f}")


def date_trend(name, per):
    items = [(g, r, m, n) for (g, r), (m, se, n) in per.items() if n >= 20]
    x = np.array([(rides[r]["ts"] - np.mean([rides[q]["ts"] for gg, q, _, _ in items if gg == g])) / 86400 / 30 for g, r, m, n in items])
    y = np.array([m for g, r, m, n in items])
    if len(set(i[0] for i in items)) < 1 or len(items) < 4:
        print(f"    date trend {name}: too few runs"); return
    w = np.array([n for g, r, m, n in items], float)
    for lab, ww in (("unweighted", np.ones_like(w)), ("weighted by places", w)):
        b = (ww * x @ y) / (ww * x @ x) if (ww * x @ x) > 0 else float("nan")
        bs = []
        for _ in range(2000):
            s = rng.integers(0, len(x), len(x))
            if ww[s] * x[s] @ x[s] > 0: bs.append((ww[s] * x[s] @ y[s]) / (ww[s] * x[s] @ x[s]))
        print(f"    date trend {name} ({lab}): slope={b:+.3f} class per 30 days +- {np.std(bs):.3f} (bootstrap over {len(x)} runs; date centred within group)")


def offsets(kind, rows):
    """Split the leave-one-out residual into a per-run constant and the scatter around it (M>=1)."""
    per = defaultdict(list)
    for q in rows: per[(q["p"]["g"], q["r"])].append(q["res"])
    per = {k: np.array(v) for k, v in per.items() if len(v) >= 20}
    means = np.array([v.mean() for v in per.values()])
    within = np.sqrt(np.mean(np.concatenate([v - v.mean() for v in per.values()]) ** 2))
    se2 = np.mean([v.var(ddof=1) / len(v) for v in per.values()])
    sd_off = math.sqrt(max(0.0, means.var(ddof=1) - se2))
    print(f"  run offsets ({kind}, M>=1, runs with >=20 places): runs={len(per)} SD of run means={means.std(ddof=1):.3f} "
          f"(minus sampling error: {sd_off:.3f}); within-run residual RMS={within:.3f}")


def item6(kind, rows, sigma):
    print(f"  item 6 ({kind}): per run SD of (run - mean of all other runs), places with M>=1; 'vs truth' removes sigma^2/M of the others' mean")
    per = defaultdict(list)
    for q in rows: per[(q["p"]["g"], q["r"])].append(q)
    allsd = []
    for (g, r), v in sorted(per.items()):
        d = np.array([q["res"] for q in v]); M = np.array([q["M"] for q in v])
        if len(d) < 20: continue
        sd = d.std(); tr = math.sqrt(max(0.0, sd ** 2 - sigma ** 2 * np.mean(1 / M)))
        allsd.append((sd, tr, len(d)))
        print(f"    g{g} ride {r:2d} n={len(d):4d} mean M={M.mean():.1f} SD={sd:.3f} vs truth={tr:.3f} mean={d.mean():+.3f}")
    a = np.array(allsd)
    if len(a):
        print(f"    median over runs: SD={np.median(a[:, 0]):.3f} vs truth={np.median(a[:, 1]):.3f}; range SD {a[:, 0].min():.3f}-{a[:, 0].max():.3f}")


# ---------- run ----------
print("\nTune combinations (front spring, rear spring, front HSC, rear HSC, front LSC, rear LSC, front LSR, rear LSR, front HSR, rear HSR):")
for k, t in enumerate(TUNES):
    print(f"  T{k}: {t}  rides {[r for r in sorted(rides) if rides[r]['tune'] == t and r != 1]}")

RES = {}
for kind in ("Fork", "Frame", "Grade"):
    print(f"\n==================== {kind} ====================")
    PR = place_readings(kind)
    coverage(kind, PR)
    print(f"  items 1+2 ({kind}): disjoint random N-run sets, 200 draws per place; noise per N-run average; same class share")
    r_all = split_noise(kind, PR, [1, 2, 3, 4], label="every place with >= 2N runs")
    for mr, Ns in ((8, [1, 2, 3, 4]), (6, [1, 2, 3]), (4, [1, 2])):
        split_noise(kind, PR, Ns, min_runs=mr, label=f"common subset: places with >= {mr} runs")
    main = [(p, rd) for p, rd in PR if p["g"] == 0]; rest = [(p, rd) for p, rd in PR if p["g"] != 0]
    split_noise(kind, main, [1, 2, 3, 4], label="main group only, every place with >= 2N runs")
    split_noise(kind, rest, [1, 2], label="other groups only, every place with >= 2N runs")
    pair_split(kind, PR)
    sigma = r_all[1]["noise"]
    rows = loo(kind, PR)
    item3(kind, rows, sigma)
    if kind != "Grade":
        item4(kind, PR)
        per = item5(kind, rows)
        tune_test(kind, per)
        date_trend(kind, per)
        RES[kind] = (per, rows)
    offsets(kind, rows)
    item6(kind, rows, sigma)

# fork vs frame residual on the same run and place
print("\n==================== Frame - Fork residual (same run, same place, M>=2 for both) ====================")
fk = {(q["p"]["g"], q["p"]["i"], q["r"]): q["res"] for q in RES["Fork"][1] if q["M"] >= 2}
fr = {(q["p"]["g"], q["p"]["i"], q["r"]): q["res"] for q in RES["Frame"][1] if q["M"] >= 2}
both = defaultdict(list)
for k in fk.keys() & fr.keys(): both[(k[0], k[2])].append((fk[k], fr[k]))
dif = {}
for (g, r), v in sorted(both.items()):
    v = np.array(v); d = v[:, 1] - v[:, 0]
    dif[(g, r)] = (d.mean(), d.std(ddof=1) / math.sqrt(len(d)), len(d))
    print(f"  g{g} ride {r:2d} T{TUNES.index(rides[r]['tune'])} n={len(d):4d} fork={v[:, 0].mean():+.3f} frame={v[:, 1].mean():+.3f} "
          f"frame-fork={d.mean():+.3f} SE={dif[(g, r)][1]:.3f} corr(fork,frame residual)={np.corrcoef(v[:, 0], v[:, 1])[0, 1]:.2f}")
tune_test("Frame-Fork", dif)
fm = [RES["Fork"][0][k][0] for k in both if k in RES["Fork"][0] and k in RES["Frame"][0] and RES["Fork"][0][k][2] >= 20]
rm = [RES["Frame"][0][k][0] for k in both if k in RES["Fork"][0] and k in RES["Frame"][0] and RES["Fork"][0][k][2] >= 20]
if len(fm) > 2:
    print(f"  correlation of run mean residuals fork vs frame over {len(fm)} runs: {np.corrcoef(fm, rm)[0, 1]:.2f}")
