"""Shared loaders and the Python port of the grade path (SignalFilters.LowPass, MeasureGrade)."""
import json, math, os, sys
GYRO = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(GYRO))
import numpy as np
from scipy.signal import lfilter
from common import D, computed_sessions, adjacent, baseline, deg, EDGES_DEG, merge_outliers, is_descent

G0 = 9.80665
CS = computed_sessions()


def lowpass(x, rate, cutoff):
    """Port of SignalFilters.LowPass: 2nd-order Butterworth, odd-reflection padding, forward-backward."""
    x = np.asarray(x, float)
    n = len(x)
    if n == 0:
        return x.copy()
    k = math.tan(math.pi * cutoff / rate)
    gain = 1 / (1 + math.sqrt(2) * k + k * k)
    b0 = k * k * gain
    b = np.array([b0, 2 * b0, b0])
    a = np.array([1, 2 * (k * k - 1) * gain, (1 - math.sqrt(2) * k + k * k) * gain])
    pad = int(min(n - 1, math.ceil(rate / cutoff)))
    i = np.arange(1, pad + 1)
    data = np.concatenate([(2 * x[0] - x[i])[::-1], x, 2 * x[-1] - x[-1 - i]])

    def apply(d):
        steady = d[0] * b.sum() / a.sum()
        zi = [steady - b[0] * d[0], b[2] * d[0] - a[2] * steady]
        y, _ = lfilter(b, a, d, zi=zi)
        return y

    y = apply(data)[::-1]
    y = apply(y)[::-1]
    return y[pad:pad + n]


class Ride:
    def __init__(self, idx):
        self.idx = idx
        d = os.path.join(GYRO, "ext", f"r{idx:02d}")
        self.meta = json.load(open(os.path.join(d, "meta.json")))
        g = np.fromfile(os.path.join(d, "grid.bin"), float).reshape(-1, 6)
        self.grid = g  # span, seconds, distance, elevation, speed, acc
        sid, s, e = CS[idx]
        self.sid, self.session, self.entry = sid, s, e
        self.ws = e["profile"]["Windows"]
        self.has_imu = bool(self.meta["segments"])
        if not self.has_imu:
            return
        imu = np.fromfile(os.path.join(d, "imu.bin"), np.int16).reshape(-1, 6).astype(float)
        mot = np.fromfile(os.path.join(d, "mot.bin"), np.float32).reshape(-1, 3).astype(float)
        trav = np.fromfile(os.path.join(d, "trav.bin"), np.float32).reshape(-1, 2).astype(float)
        cols = self.meta["attColumns"].split(",")
        att = np.fromfile(os.path.join(d, "att.bin"), np.float32).reshape(-1, len(cols)).astype(float)
        self.att = {c: att[:, k] for k, c in enumerate(cols)}
        self.rate = self.meta["imuRate"]
        al, gl = self.meta["accelLsb"], self.meta["gyroLsb"]
        self.acc = imu[:, :3] / al
        self.gyr = np.radians(imu[:, 3:] / gl)
        self.valid, self.speed, self.lacc = mot[:, 0] > 0, mot[:, 1], mot[:, 2]
        self.front, self.rear = trav[:, 0], trav[:, 1]
        self.segs = [(sg["origin"], sg["count"], sg["offset"]) for sg in self.meta["segments"]]

    def motion_at(self, t):
        """TrackMotion.At for an array of times: (elevation, distance), NaN outside spans."""
        g = self.grid
        out_e = np.full(np.shape(t), np.nan); out_d = np.full(np.shape(t), np.nan)
        t = np.asarray(t, float)
        for s in np.unique(g[:, 0]):
            m = g[:, 0] == s
            st, dist, el = g[m, 1], g[m, 2], g[m, 3]
            inside = (t >= st[0]) & (t <= st[-1])
            out_d[inside] = np.interp(t[inside], st, dist)
            out_e[inside] = np.interp(t[inside], st, el)  # NaN elevations propagate as in At
        return out_e, out_d

    def window_ranges(self):
        """Per window: (segment index, a, b) sample range in global arrays, or None (port of SampleRange)."""
        out = []
        for w in self.ws:
            r = None
            if w["Moving"]:
                for k, (origin, count, off) in enumerate(self.segs):
                    a = math.ceil((w["StartSeconds"] - origin) * self.rate - 1e-8)
                    b = math.ceil((w["EndSeconds"] - origin) * self.rate - 1e-8)
                    if (w["StartSeconds"] >= origin - 1e-9 and w["EndSeconds"] <= origin + count / self.rate + 1e-9
                            and a >= 0 and b <= count and a < b):
                        r = (k, off + a, off + b)
            out.append(r)
        return out

    def window_grades(self, sample_sin):
        """Speed-weighted window means of a per-sample sin(grade) series, before the offset."""
        rng = self.window_ranges()
        g = [None] * len(self.ws)
        tg = tw = 0.0
        for i, r in enumerate(rng):
            if r is None:
                continue
            _, a, b = r
            sp = self.speed[a:b]
            wsum = sp.sum()
            if wsum <= 0:
                continue
            s = (sp * sample_sin[a:b]).sum()
            g[i] = s / wsum
            tg += s; tw += wsum
        return g, tg, tw

    def o1_offset(self, g, tg, tw):
        mov = [w for w in self.ws if w["Moving"]]
        st = self.motion_at(np.array([w["StartSeconds"] for w in mov]))
        en = self.motion_at(np.array([w["EndSeconds"] for w in mov]))
        a = next(i for i in range(len(mov)) if np.isfinite(st[0][i]) and np.isfinite(st[1][i]))
        b = next(i for i in reversed(range(len(mov))) if np.isfinite(en[0][i]) and np.isfinite(en[1][i]))
        e0, d0, e1, d1 = st[0][a], st[1][a], en[0][b], en[1][b]
        return tg / tw - (e0 - e1) / (d1 - d0)


def per_segment(r, fn):
    """Apply fn(slice) per continuous IMU segment and concatenate."""
    out = np.empty(len(r.acc))
    for origin, count, off in r.segs:
        out[off:off + count] = fn(slice(off, off + count))
    return out
