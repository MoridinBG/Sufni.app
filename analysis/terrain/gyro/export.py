"""Export per-ride processed-telemetry BLOBs and terrain GPS fixes from the read-only snapshot.

Ride index = position in common.computed_sessions() (time order), as in the earlier scripts.
Fixes follow TrackSampleAdapter.FromOriginalFixes: telemetry seconds, local metres east/north, elevation (NaN = none).
Output: gyro/raw/rNN.blob, gyro/raw/rNN.fix (float64 n x 4), gyro/raw/index.json.
"""
import json, math, os, sqlite3, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import numpy as np
from common import D, computed_sessions, HERE

OUT = os.path.join(HERE, "gyro", "raw")
os.makedirs(OUT, exist_ok=True)
con = sqlite3.connect(f"file:{os.path.join(HERE, 'snap.db')}?mode=ro", uri=True)
R = 6378137.0
index = []
for idx, (sid, s, e) in enumerate(computed_sessions()):
    c = e["key"]["Content"]
    blob, rev = con.execute("SELECT data, processed_telemetry_revision FROM session WHERE id=?", (sid,)).fetchone()
    pts_json, prev = con.execute("SELECT points, points_revision FROM track WHERE id=?", (c["FullTrackId"],)).fetchone()
    pts = json.loads(pts_json)
    gps_offset = c.get("GpsOffsetSeconds") or 0.0
    if not math.isfinite(gps_offset):
        gps_offset = 0.0
    frac = c.get("WindowOriginFraction") or 0.0
    origin = c["Timestamp"] + frac + gps_offset
    dur = c["DurationSeconds"]
    rows, x0 = [], None
    for p in pts:
        x, y, t = p.get("x"), p.get("y"), p.get("time")
        if x is None or y is None or t is None:
            continue
        sec = t - origin
        if not (math.isfinite(x) and math.isfinite(y) and math.isfinite(sec) and 0 <= sec <= dur):
            continue
        if x0 is None:
            x0, y0 = x, y
            lat = math.degrees(2 * math.atan(math.exp(y / R)) - math.pi / 2)
            k = math.cos(math.radians(lat))
        h = p.get("ele")
        rows.append((sec, (x - x0) * k, (y - y0) * k, h if h is not None and math.isfinite(h) else np.nan))
    np.asarray(rows, float).tofile(os.path.join(OUT, f"r{idx:02d}.fix"))
    open(os.path.join(OUT, f"r{idx:02d}.blob"), "wb").write(blob)
    index.append(dict(idx=idx, telemetry_revision_db=rev, telemetry_revision_key=c["ProcessedTelemetryRevision"],
                      points_revision_db=prev, points_revision_key=c["FullTrackPointsRevision"], fixes=len(rows),
                      grade_state=e["profile"]["GradeState"]))
json.dump(index, open(os.path.join(OUT, "index.json"), "w"), indent=1)
bad = [r["idx"] for r in index if r["telemetry_revision_db"] != r["telemetry_revision_key"]
       or r["points_revision_db"] != r["points_revision_key"]]
print("rides", len(index), "revision mismatches", bad)
