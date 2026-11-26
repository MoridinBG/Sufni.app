"""Same-place matching, copied unchanged from cdgh_matching_repeatability.py (sections 'rides' and 'C')."""
import math
import datetime as dt
from collections import Counter
import numpy as np
from scipy.spatial import cKDTree
from gcommon import CS, D, adjacent, baseline, is_descent

R = 6378137.0
HEAD_DEG = 45


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
    stops = [(w["StartSeconds"], w["EndSeconds"]) for w in ws if not w["Moving"]]
    near = np.array([any(a - 15 <= w["EndSeconds"] and w["StartSeconds"] <= b + 15 for a, b in stops) for w in ws])
    rides.append(dict(idx=idx, sid=sid, date=dt.datetime.utcfromtimestamp(s["timestamp"]).strftime("%Y-%m-%d"),
                      ws=ws, cx=cx, cy=cy, head=head, near=near, sz=sz, ez=ez,
                      grade=e["profile"]["GradeState"] == 0, tune=s["tune"]))


def ok(w):
    return is_descent(w)


def match(match_m):
    pairs, pair_km = [], {}
    for a in range(len(rides)):
        A = rides[a]
        ia = [i for i, w in enumerate(A["ws"]) if ok(w)]
        if not ia: continue
        for b in range(a + 1, len(rides)):
            B = rides[b]
            ib = np.array([j for j, w in enumerate(B["ws"]) if ok(w)])
            if not len(ib): continue
            tree = cKDTree(np.c_[B["cx"][ib], B["cy"][ib]])
            d, k = tree.query(np.c_[A["cx"][ia], A["cy"][ia]], distance_upper_bound=match_m)
            m, used = [], set()
            for i, dd, kk in sorted(zip(ia, d, k), key=lambda t: t[1]):
                if not np.isfinite(dd): continue
                j = int(ib[kk])
                if j in used: continue
                dh = abs((A["head"][i] - B["head"][j] + np.pi) % (2 * np.pi) - np.pi)
                if math.degrees(dh) > HEAD_DEG: continue
                used.add(j)
                m.append((i, j))
            if len(m) >= 20:
                pairs += [(a, b, i, j) for i, j in m]
                pair_km[(a, b)] = len(m) * 10 / 1000
    return pairs, pair_km
