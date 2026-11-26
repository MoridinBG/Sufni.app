"""Step 7: guards for the two-point grade offset (O1) on V0.

G0  today: elevation and distance from TrackMotion at the first moving window start and last moving window end.
G1  elevation = median of fixes within +-T s of each end point (distance from TrackMotion at the same times).
G2  drop fixes departing from a +-15 s running median by more than k m, linear interpolation in time, then G0.
G3  median offset over all (start, end) pairs: moving window starts in the first 10 % of moving distance,
    moving window ends in the last 10 %; elevation and distance from TrackMotion.
W   the whole-ride moving-mean form of step 6c.
The mean-grade term is today's speed-weighted ride mean for G0-G3.
"""
import os, pickle, sys; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from collections import defaultdict
from gcommon import *
from gmatch import rides, match
from ghelp import noise_signal

VR = pickle.load(open(os.path.join(GYRO, "variants.pkl"), "rb"))["rides"]


def fixes(k):
    f = np.fromfile(os.path.join(GYRO, "raw", f"r{k:02d}.fix"), float).reshape(-1, 4)
    f = f[np.argsort(f[:, 0])]
    return f[:, 0], f[:, 3]


info = {}
for k in VR:
    r = Ride(k)
    raw = [None if v is None else v + VR[k]["grades"]["V0"]["z1"] for v in VR[k]["grades"]["V0"]["O1"]]
    _, tg, tw = r.window_grades(np.sin(-np.arctan2(  # recompute the speed-weighted mean exactly as step 1
        per_segment(r, lambda s: lowpass(r.acc[s, 0] - r.lacc[s] / G0, r.rate, 0.08)),
        per_segment(r, lambda s: lowpass(r.acc[s, 2], r.rate, 0.08)))))
    mov = [w for w in r.ws if w["Moving"]]
    t0, t1 = mov[0]["StartSeconds"], mov[-1]["EndSeconds"]
    (e0, e1), (d0, d1) = r.motion_at(np.array([t0, t1]))
    ft, fe = fixes(k)
    info[k] = dict(r=r, raw=raw, mean=tg / tw, t0=t0, t1=t1, e0=e0, e1=e1, d0=d0, d1=d1, ft=ft, fe=fe)

offsets = defaultdict(dict)
for k, q in info.items():
    m = q["mean"]
    offsets["G0"][k] = m - (q["e0"] - q["e1"]) / (q["d1"] - q["d0"])
    for T in (2, 5, 10):
        h = [np.median(q["fe"][np.abs(q["ft"] - t) <= T]) for t in (q["t0"], q["t1"])]
        offsets[f"G1 T={T}s"][k] = m - (h[0] - h[1]) / (q["d1"] - q["d0"])
    for kk in (3, 5):
        med = np.array([np.median(q["fe"][np.abs(q["ft"] - t) <= 15]) for t in q["ft"]])
        keep = np.abs(q["fe"] - med) <= kk
        h = np.interp([q["t0"], q["t1"]], q["ft"][keep], q["fe"][keep])
        offsets[f"G2 k={kk}m"][k] = m - (h[0] - h[1]) / (q["d1"] - q["d0"])
        q[f"dropped{kk}"] = int((~keep).sum())
    ws = q["r"].ws
    mv = [w for w in ws if w["Moving"]]
    st = np.array([w["StartSeconds"] for w in mv]); en = np.array([w["EndSeconds"] for w in mv])
    es, ds = q["r"].motion_at(st); ee, de = q["r"].motion_at(en)
    total = len(mv) * 10.0
    cum0 = np.arange(len(mv)) * 10.0; cum1 = cum0 + 10
    S = np.where(cum0 <= 0.1 * total)[0]; E = np.where(cum1 >= 0.9 * total)[0]
    vals = [m - (es[a] - ee[b]) / (de[b] - ds[a]) for a in S for b in E if np.isfinite(es[a]) and np.isfinite(ee[b])]
    offsets["G3"][k] = float(np.median(vals))
    # W: step 6c whole-ride form, expressed as an offset on the same raw window grades
    G = q["r"]; g = G.grid; good = np.isfinite(g[:, 3])
    allst = np.array([w["StartSeconds"] for w in ws]); allen = np.array([w["EndSeconds"] for w in ws])
    _, a0 = G.motion_at(allst); _, a1 = G.motion_at(allen)
    lo, hi = np.nanmin(a0), np.nanmax(a1)
    ea, eb = np.interp([lo, hi], g[good, 2], g[good, 3])
    gv = np.array([v for v in q["raw"] if v is not None])
    offsets["W"][k] = gv.mean() - (ea - eb) / (hi - lo)

print("=== G0 end points (today) ===")
for k in (23, 24):
    q = info[k]
    d2 = np.abs(q["fe"][2:] - 2 * q["fe"][1:-1] + q["fe"][:-2])
    big = q["ft"][1:-1][d2 > 5]
    print(f"ride {k}: end points t={q['t0']:.1f} s / {q['t1']:.1f} s, fixes span {q['ft'][0]:.1f}-{q['ft'][-1]:.1f} s; "
          f"fixes with |2nd diff| > 5 m at t = {np.round(big, 1).tolist()} (max {d2.max():.1f} m); "
          f"G2 drops k=3: {q['dropped3']}, k=5: {q['dropped5']}")


def span_eval(off):
    xs, ys = [], []
    for k in VR:
        r = rides[k]; ws = r["ws"]; g = [None if v is None else v - off[k] for v in info[k]["raw"]]; run = []

        def flush(run):
            for q0 in range(0, len(run) - 29, 30):
                seg = run[q0:q0 + 30]
                if any(g[i] is None for i in seg): continue
                h0, h1 = r["sz"][seg[0]], r["ez"][seg[-1]]
                if not (np.isfinite(h0) and np.isfinite(h1)): continue
                xs.append(deg(np.mean([g[i] for i in seg]))); ys.append(deg((h0 - h1) / 300.0))
        for i, w in enumerate(ws):
            if w["Moving"] and (not run or adjacent(ws[run[-1]], w)): run.append(i)
            else:
                flush(run); run = [i] if w["Moving"] else []
        flush(run)
    xs, ys = np.array(xs), np.array(ys)
    X = np.c_[np.ones_like(xs), xs]; b, *_ = np.linalg.lstsq(X, ys, rcond=None); rs = ys - X @ b
    return b[1], 1 - rs @ rs / ((ys - ys.mean()) @ (ys - ys.mean())), rs.std(ddof=2)


M = {mm: match(mm)[0] for mm in (6.0, 3.0)}
print("\n=== Guards on V0: pair-mean SD 6 m / 3 m, 10 m noise 6 m / 3 m, 300 m spans (gain, R2, resid), rides changed > 0.3 deg vs G0 ===")
for name, off in offsets.items():
    res = []
    for mm, pairs in M.items():
        gp = [(a, b, i, j) for a, b, i, j in pairs if a in VR and b in VR and info[a]["raw"][i] is not None and info[b]["raw"][j] is not None]
        xa = [deg(info[a]["raw"][i] - off[a]) for a, b, i, j in gp]; xb = [deg(info[b]["raw"][j] - off[b]) for a, b, i, j in gp]
        dd = defaultdict(list)
        for (a, b, i, j), u, v in zip(gp, xa, xb): dd[(a, b)].append(u - v)
        res.append((np.std([np.mean(v) for v in dd.values()], ddof=1), noise_signal(xa, xb)[0], len(dd)))
    gain, r2, rsd = span_eval(off)
    ch = {k: math.degrees(off[k] - offsets["G0"][k]) for k in VR}
    big = ", ".join(f"{k}:{v:+.2f}" for k, v in ch.items() if abs(v) > 0.3)
    print(f"{name:10s} pairSD {res[0][0]:.2f} / {res[1][0]:.2f}  noise10 {res[0][1]:.2f} / {res[1][1]:.2f}  "
          f"spans gain {gain:.3f} R2 {r2:.3f} resid {rsd:.2f}  | 23: {math.degrees(off[23]):+.2f} 24: {math.degrees(off[24]):+.2f} | changed: {big or 'none'}")
