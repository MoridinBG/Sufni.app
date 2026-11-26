"""Helpers shared with step4_eval.py (band hysteresis, switch counting, noise/signal)."""
import math
import numpy as np
from gcommon import adjacent, EDGES_DEG
EDEG = [float(e) for e in EDGES_DEG]
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


def noise_signal(xa, xb):
    xa = np.asarray(xa); xb = np.asarray(xb)
    return (xa - xb).std(ddof=1) / math.sqrt(2), ((xa + xb) / 2).std(ddof=1)


