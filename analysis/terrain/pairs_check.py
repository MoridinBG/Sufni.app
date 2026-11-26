import sys, os; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import runpy, io, contextlib
buf = io.StringIO()
with contextlib.redirect_stdout(buf):
    g = runpy.run_path(os.path.join(os.path.dirname(os.path.abspath(__file__)), "cdgh_matching_repeatability.py"))
rides, pairs, pair_km = g["rides"], g["pairs"], g["pair_km"]
import numpy as np, math
from collections import defaultdict
S = g["D"]["sessions"]
by = defaultdict(list)
for a, b, i, j in pairs: by[(a, b)].append((i, j))
for (a, b), m in sorted(by.items()):
    A, B = rides[a], rides[b]
    sa, sb = S[A["sid"]], S[B["sid"]]
    ta = (sa["timestamp"], sa["timestamp"] + (sa["duration"] or 0)); tb = (sb["timestamp"], sb["timestamp"] + (sb["duration"] or 0))
    ov = max(0, min(ta[1], tb[1]) - max(ta[0], tb[0]))
    df = [math.log(A["ws"][i]["Fork"]["SpeedRemoved"]) - math.log(B["ws"][j]["Fork"]["SpeedRemoved"]) for i, j in m
          if A["ws"][i].get("Fork") and B["ws"][j].get("Fork") and A["ws"][i]["Fork"]["Level"] > 0 and B["ws"][j]["Fork"]["Level"] > 0]
    dfr = [math.log(A["ws"][i]["Frame"]["SpeedRemoved"]) - math.log(B["ws"][j]["Frame"]["SpeedRemoved"]) for i, j in m
          if A["ws"][i].get("Frame") and B["ws"][j].get("Frame") and A["ws"][i]["Frame"]["Level"] > 0 and B["ws"][j]["Frame"]["Level"] > 0]
    print(a, b, A["date"], B["date"], "km", pair_km[(a, b)], "time overlap s", round(ov), "fork n", len(df),
          "SD dlnfork", round(float(np.std(df)), 3) if df else None, "frame n", len(dfr), "SD", round(float(np.std(dfr)), 3) if dfr else None,
          "sametune", A["tune"] == B["tune"])
