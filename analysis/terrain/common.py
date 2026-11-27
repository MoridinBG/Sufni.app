"""Shared helpers: load the decoded snapshot and rebuild the app's window-level rules."""
import math, os, pickle
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
D = pickle.load(open(os.path.join(HERE, "data.pkl"), "rb"))

STEP = 1.5
BW = math.log(STEP) / 20
EDGES_DEG = [-6, -2, 2, 5, 10, 15]
EDGES = [math.sin(math.radians(e)) for e in EDGES_DEG]
CLIP = 0.02
EXP = {"Fork": 0.9, "Frame": 0.7}
CLIMB_BANDS = (0, 1)  # SteepClimb, Climb


def bin_of(level):
    return math.floor(math.log(level) / BW)


def median_level(b):
    return math.exp((b + 0.5) * BW)


def class_value(level, mb):
    return math.log(level / median_level(mb)) / math.log(STEP) + 0.5


def classify_value(v):
    return 0 if v < -1 else 1 if v < 0 else 2 if v < 1 else 3 if v < 2 else 4


def classify_grade(sin_baseline):
    b = 0
    while b < len(EDGES) and sin_baseline >= EDGES[b]:
        b += 1
    return b


def deg(s):
    return math.degrees(math.asin(max(-1, min(1, s))))


def adjacent(a, b):
    return a["EndSeconds"] == b["StartSeconds"]


def baseline(ws, i, read, reach=2, need=3, median=True):
    """The app takes the median from algorithm version 12. median=False gives the mean of the earlier versions."""
    g = read(ws[i])
    if g is None:
        return None
    vals = [g]
    for d in (-1, 1):
        for o in range(1, reach + 1):
            j = i + d * o
            if j < 0 or j >= len(ws):
                break
            lo = min(j, j - d)
            if not adjacent(ws[lo], ws[lo + 1]):
                break
            v = read(ws[j])
            if v is not None:
                vals.append(v)
    if len(vals) < need:
        return None
    return float(np.median(vals)) if median else sum(vals) / len(vals)


def merge_outliers(ws, classes):
    """A-B-A -> A-A-A, decisions on pre-merge classes; ws gives adjacency."""
    out = list(classes)
    for i in range(1, len(ws) - 1):
        own, before, after = classes[i], classes[i - 1], classes[i + 1]
        if own is None or before is None or after != before or own == before:
            continue
        if not adjacent(ws[i - 1], ws[i]) or not adjacent(ws[i], ws[i + 1]):
            continue
        out[i] = before
    return out


def computed_sessions():
    """(sid, session, profile dict) for sessions with a computed profile, ordered by time."""
    out = []
    for sid, e in D["profiles"].items():
        if "profile" not in e:
            continue
        s = D["sessions"][sid]
        if s["deleted"] is not None:
            continue
        out.append((sid, s, e))
    out.sort(key=lambda t: t[1]["timestamp"])
    return out


def usable(r):
    return r is not None and r["Level"] > 0 and r["ClippedShare"] <= CLIP


def is_descent(w):
    """Moving, non-climb window (the app's 'descent' population for the scale)."""
    return w["Moving"] and not (w.get("Band") is not None and w["Band"] in CLIMB_BANDS)


def track_xy_fn(e):
    """Telemetry seconds -> (x m, y m, elevation) via the full track, or None without a track."""
    c = e["key"]["Content"]
    tid = c.get("FullTrackId")
    if tid is None or tid not in D["tracks"]:
        return None
    arr = D["tracks"][tid]
    if len(arr) < 2:
        return None
    origin = c["Timestamp"] + (c.get("WindowOriginFraction") or 0) + (c.get("GpsOffsetSeconds") or 0)
    t = arr[:, 0] - origin
    order = np.argsort(t)
    t = t[order]
    # Spherical Mercator -> local metres: divide by 1/cos(lat), i.e. multiply by cos(lat).
    y0 = arr[order[0], 2]
    lat = math.degrees(2 * math.atan(math.exp(y0 / 6378137.0)) - math.pi / 2)
    k = math.cos(math.radians(lat))
    x = arr[order, 1] * k
    y = arr[order, 2] * k
    z = arr[order, 3]

    def f(sec):
        sec = np.asarray(sec, float)
        return np.interp(sec, t, x), np.interp(sec, t, y), np.interp(sec, t, z)

    return f
