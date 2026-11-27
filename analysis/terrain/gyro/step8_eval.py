"""Compares the grade variants of step8_variants.py: noise between runs, detail, band changes, same band, elevation fit.

Usage: MATCH_M=6 step8_eval.py. Prints the MSM ride of 2026-06-11 (ride index 25 in the data set of 2026-11-27).
"""
import os, pickle, sys; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from collections import defaultdict
from gcommon import *
from gmatch import rides, match

V = pickle.load(open(os.path.join(GYRO, "variants8.pkl"), "rb"))
VR = V["rides"]
EDEG = [float(e) for e in EDGES_DEG]
GROUP_A = {2, 4, 7, 8, 9, 11, 12, 13, 14}


def hysteresis(ws, val, edges, h):
    """Copied from bef_switching_scale_clipping.py."""
    lab = [None] * len(ws)
    for i in range(len(ws)):
        v = val[i]
        if v is None: continue
        if i == 0 or not adjacent(ws[i - 1], ws[i]) or lab[i - 1] is None:
            b = 0
            while b < len(edges) and v >= edges[b]: b += 1
        else:
            b = lab[i - 1]
            while b < len(edges) and v >= edges[b] + h: b += 1
            while b > 0 and v < edges[b - 1] - h: b -= 1
        lab[i] = b
    return lab


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


def switches_per_km(labels_by_ride):
    sw = km = 0
    for ws, lab in labels_by_ride:
        for ch in chains(ws, lab):
            km += len(ch) * 10 / 1000
            sw += sum(1 for k in range(1, len(ch)) if lab[ch[k]] != lab[ch[k - 1]])
    return sw / km


def values(idx, name, off):
    """Per window degrees: 10 m, 30 m (+-1, needs 2), 50 m (+-2, needs 3). The mean baseline of the cutoff analysis."""
    ws = rides[idx]["ws"]
    g = VR[idx]["grades"][name][off]
    rd = lambda w, _g={id(w): v for w, v in zip(ws, g)}: _g[id(w)] if w["Moving"] else None
    w10 = [None if (v is None or not w["Moving"]) else v for w, v in zip(ws, g)]
    b30 = [baseline(ws, i, rd, reach=1, need=2, median=False) for i in range(len(ws))]
    b50 = [baseline(ws, i, rd, reach=2, need=3, median=False) for i in range(len(ws))]
    d = lambda L: [None if v is None else deg(v) for v in L]
    return dict(w10=d(w10), b30=d(b30), b50=d(b50), sin=w10)


def noise_signal(xa, xb):
    xa = np.asarray(xa); xb = np.asarray(xb)
    return (xa - xb).std(ddof=1) / math.sqrt(2), ((xa + xb) / 2).std(ddof=1)


def evaluate(name, off, pairs, pair_km, cache):
    if (name, off) not in cache:
        cache[(name, off)] = {i: values(i, name, off) for i in VR}
    val = cache[(name, off)]
    gp = [(a, b, i, j) for a, b, i, j in pairs if a in VR and b in VR
          and val[a]["w10"][i] is not None and val[b]["w10"][j] is not None]
    res = {"n": len(gp), "pairs": len({(a, b) for a, b, _, _ in gp})}
    for key in ("w10", "b30", "b50"):
        sel = [(a, b, i, j) for a, b, i, j in gp if val[a][key][i] is not None and val[b][key][j] is not None]
        xa = [val[a][key][i] for a, b, i, j in sel]; xb = [val[b][key][j] for a, b, i, j in sel]
        res[f"noise_{key}"], res[f"signal_{key}"] = noise_signal(xa, xb)
        res[f"n_{key}"] = len(sel)
    # detail
    sel = [(a, b, i, j) for a, b, i, j in gp if val[a]["b50"][i] is not None and val[b]["b50"][j] is not None]
    da = np.array([val[a]["w10"][i] - val[a]["b50"][i] for a, b, i, j in sel])
    db = np.array([val[b]["w10"][j] - val[b]["b50"][j] for a, b, i, j in sel])
    res["detail_sd"] = np.concatenate([da, db]).std(ddof=1)
    res["detail_r"] = np.corrcoef(da, db)[0, 1]
    res["n_detail"] = len(sel)
    for grp, f in (("A", lambda a: a in GROUP_A), ("rest", lambda a: a not in GROUP_A)):
        s = [k for k, (a, b, i, j) in enumerate(sel) if f(a)]
        res[f"detail_r_{grp}"] = np.corrcoef(da[s], db[s])[0, 1] if len(s) > 10 else np.nan
        res[f"n_detail_{grp}"] = len(s)
        g2 = [(a, b, i, j) for a, b, i, j in gp if f(a)]
        res[f"noise_w10_{grp}"] = noise_signal([val[a]["w10"][i] for a, b, i, j in g2], [val[b]["w10"][j] for a, b, i, j in g2])[0]
    # per-pair mean difference
    dd = defaultdict(list)
    for a, b, i, j in gp:
        dd[(a, b)].append(val[a]["w10"][i] - val[b]["w10"][j])
    res["pair_mean_sd"] = np.std([np.mean(v) for v in dd.values()], ddof=1)
    # suspension: d grade on d(front - rear) mean travel (stored window figures)
    y, x = [], []
    for a, b, i, j in gp:
        wa, wb = rides[a]["ws"][i], rides[b]["ws"][j]
        if wa.get("Front") and wa.get("Rear") and wb.get("Front") and wb.get("Rear") and VR[a]["has_susp"] and VR[b]["has_susp"]:
            y.append(val[a]["w10"][i] - val[b]["w10"][j])
            x.append((wa["Front"]["MeanTravel"] - wa["Rear"]["MeanTravel"]) - (wb["Front"]["MeanTravel"] - wb["Rear"]["MeanTravel"]))
    y = np.array(y); x = np.array(x)
    X = np.c_[np.ones_like(x), x]
    beta, *_ = np.linalg.lstsq(X, y, rcond=None)
    rs = y - X @ beta
    se = math.sqrt(rs @ rs / (len(y) - 2) * np.linalg.inv(X.T @ X)[1, 1])
    res["susp_slope"], res["susp_se"], res["n_susp"] = beta[1], se, len(y)
    # stops
    near = np.array([rides[a]["near"][i] or rides[b]["near"][j] for a, b, i, j in gp])
    dg = np.array([val[a]["w10"][i] - val[b]["w10"][j] for a, b, i, j in gp])
    res["noise_near"] = dg[near].std(ddof=1) / math.sqrt(2); res["noise_far"] = dg[~near].std(ddof=1) / math.sqrt(2)
    res["n_near"] = int(near.sum())
    # yaw split at the median of the larger window yaw rate of the pair
    yw = np.array([max(VR[a]["yaw"][i], VR[b]["yaw"][j]) for a, b, i, j in gp])
    med = np.median(yw)
    res["yaw_med"] = med
    res["noise_yaw_hi"] = dg[yw > med].std(ddof=1) / math.sqrt(2); res["noise_yaw_lo"] = dg[yw <= med].std(ddof=1) / math.sqrt(2)
    # bands with +-1 deg hysteresis: switches per km over all grade rides, same-band share on matched windows
    for key in ("w10", "b30", "b50"):
        labs = {k: hysteresis(rides[k]["ws"], val[k][key], EDEG, 1.0) for k in VR}
        res[f"sw_{key}"] = switches_per_km([(rides[k]["ws"], labs[k]) for k in VR])
        same = [labs[a][i] == labs[b][j] for a, b, i, j in gp if labs[a][i] is not None and labs[b][j] is not None]
        res[f"same_{key}"] = np.mean(same)
    return res, gp


def elevation_spans(name, off):
    """Copied section G of cdgh_matching_repeatability.py with the variant's grades."""
    xs, ys = [], []
    for k in VR:
        r = rides[k]; ws = r["ws"]; g = VR[k]["grades"][name][off]; run = []

        def flush(run):
            for q0 in range(0, len(run) - 29, 30):
                seg = run[q0:q0 + 30]
                if any(g[q] is None for q in seg): continue
                h0, h1 = r["sz"][seg[0]], r["ez"][seg[-1]]
                if not (np.isfinite(h0) and np.isfinite(h1)): continue
                xs.append(deg(np.mean([g[q] for q in seg]))); ys.append(deg((h0 - h1) / 300.0))
        for i, w in enumerate(ws):
            if w["Moving"] and (not run or adjacent(ws[run[-1]], w)): run.append(i)
            else:
                flush(run); run = [i] if w["Moving"] else []
        flush(run)
    xs = np.array(xs); ys = np.array(ys)
    X = np.c_[np.ones_like(xs), xs]; beta, *_ = np.linalg.lstsq(X, ys, rcond=None); rs = ys - X @ beta
    return dict(spans=len(xs), gain=beta[1], r2=1 - rs @ rs / ((ys - ys.mean()) @ (ys - ys.mean())), resid=rs.std(ddof=2))


def stored_reference(gp):
    labs = {k: [w.get("Band") for w in rides[k]["ws"]] for k in VR}
    same = [labs[a][i] == labs[b][j] for a, b, i, j in gp if labs[a][i] is not None and labs[b][j] is not None]
    return switches_per_km([(rides[k]["ws"], labs[k]) for k in VR]), np.mean(same), len(same)


def main():
    ORDER = [n for n in V["names"] if n.startswith("A")]
    cache = {}
    elev = {(n, "O1"): elevation_spans(n, "O1") for n in ORDER}
    pairs, pair_km = match(float(os.environ.get("MATCH_M", "6")))
    rows = {}
    for n in ORDER:
        rows[n], gp = evaluate(n, "O1", pairs, pair_km, cache)
    r0 = rows["A0.08"]
    print(f"matched: {r0['n']} window pairs over {r0['pairs']} ride pairs; yaw median {r0['yaw_med']:.1f} dps")
    sw, same, ns = stored_reference(gp)
    print(f"stored bands today: switches/km {sw:.2f}, same band between runs {same:.3f}")
    print(f"{'variant':16s}| noise/run deg 10m 30m 50m | detail r | turn/straight noise 10m | sw/km 10m 30m 50m | same band 10m 30m 50m | elev gain R2")
    for n in ORDER:
        q = rows[n]; e = elev[(n, "O1")]
        print(f"{n:16s}| {q['noise_w10']:.2f} {q['noise_b30']:.2f} {q['noise_b50']:.2f} | {q['detail_r']:+.2f} | {q['noise_yaw_hi']:.2f}/{q['noise_yaw_lo']:.2f} | "
              f"{q['sw_w10']:5.2f} {q['sw_b30']:5.2f} {q['sw_b50']:5.2f} | {q['same_w10']:.3f} {q['same_b30']:.3f} {q['same_b50']:.3f} | {e['gain']:.3f} {e['r2']:.3f}")
    print("\nMSM 2026-06-11 (ride 25), window mid seconds: 10 m value / 30 m average")
    ws = rides[25]["ws"]
    show = [n for n in ORDER if n in ("A0.08", "A0.15", "A0.3", "A0.3+S", "A0.3_nobrake", "G0.08", "Gp0.08", "Gm0.08", "Gp0.04", "Gm0.04")]
    print("t      " + " ".join(f"{n:>13s}" for n in show))
    for i, w in enumerate(ws):
        mid = (w["StartSeconds"] + w["EndSeconds"]) / 2
        if 100 <= mid <= 116:
            cells = []
            for n in show:
                v = cache[(n, "O1")][25]
                cells.append(f"{(v['w10'][i] if v['w10'][i] is not None else float('nan')):6.1f}/{(v['b30'][i] if v['b30'][i] is not None else float('nan')):5.1f}")
            print(f"{mid:6.1f} " + " ".join(f"{c:>13s}" for c in cells))


if __name__ == "__main__":
    main()
