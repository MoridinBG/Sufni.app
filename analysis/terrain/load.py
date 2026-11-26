"""Decode terrain profiles, session metadata, IMU metadata and GPS tracks from the snapshot into a pickle.

Read-only use of snap.db (a copy of the app database). Output: data.pkl beside this script.
"""
import json, math, os, pickle, sqlite3, sys
import msgpack
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
DB = os.path.join(HERE, "snap.db")
con = sqlite3.connect(f"file:{DB}?mode=ro", uri=True)

def ext_hook(code, data):
    return msgpack.ExtType(code, data)

def unpack(b):
    return msgpack.unpackb(b, raw=False, strict_map_key=False, ext_hook=ext_hook)

sessions = {}
for row in con.execute("""SELECT id, setup_id, timestamp, front_springrate, rear_springrate, front_hsc, rear_hsc,
        front_lsc, rear_lsc, front_lsr, rear_lsr, front_hsr, rear_hsr, duration_seconds, deleted, data IS NOT NULL
        FROM session"""):
    sid = row[0]
    sessions[sid] = dict(id=sid, setup=row[1], timestamp=row[2], tune=tuple(row[3:13]), duration=row[13],
                         deleted=row[14], has_data=bool(row[15]))

setups = {r[0]: r[1] for r in con.execute("SELECT id, bike_id FROM setup WHERE deleted IS NULL")}
bikes = [r[0] for r in con.execute("SELECT id FROM bike WHERE deleted IS NULL")]

# IMU metadata from processed telemetry (top-level msgpack map; ImuData is a compact array).
for sid, s in sessions.items():
    if not s["has_data"]:
        continue
    blob = con.execute("SELECT data FROM session WHERE id=?", (sid,)).fetchone()[0]
    td = unpack(blob)
    imu = td.get("ImuData") if isinstance(td, dict) else None
    s["td_keys"] = list(td.keys()) if isinstance(td, dict) else type(td).__name__
    if isinstance(imu, list) and len(imu) >= 3:
        s["imu_rate"] = imu[1]
        s["imu_meta"] = [tuple(m) for m in imu[2]]
        s["imu_active"] = list(imu[3]) if isinstance(imu[3], (bytes, list)) else imu[3]
    elif isinstance(imu, dict):
        s["imu_rate"] = imu.get("SampleRate")
        s["imu_meta"] = imu.get("Meta")
    else:
        s["imu_rate"] = None
        s["imu_meta"] = None
    md = td.get("Metadata") if isinstance(td, dict) else None
    s["susp_rate"] = md.get("SampleRate") if isinstance(md, dict) else None
    del td, blob

profiles = {}
for sid, key, outcome, summary, prof, ckey, classes in con.execute(
        "SELECT session_id, terrain_profile_key, terrain_outcome, terrain_summary, profile, terrain_classes_key, terrain_classes "
        "FROM session_terrain_profile"):
    k = json.loads(key)
    entry = dict(key=k, outcome=outcome, summary=json.loads(summary) if summary else None,
                 class_key=json.loads(ckey) if ckey else None, classes=json.loads(classes) if classes else None)
    if prof is not None:
        entry["profile"] = unpack(prof)
    profiles[sid] = entry

tracks = {}
for tid, pts in con.execute("SELECT id, points FROM track WHERE deleted IS NULL"):
    p = json.loads(pts) if pts else []
    arr = np.array([[q["time"], q["x"], q["y"], q.get("ele") if q.get("ele") is not None else np.nan] for q in p], float)
    tracks[tid] = arr

with open(os.path.join(HERE, "data.pkl"), "wb") as f:
    pickle.dump(dict(sessions=sessions, setups=setups, bikes=bikes, profiles=profiles, tracks=tracks), f)

# quick structure print
one = next(e for e in profiles.values() if "profile" in e)
p = one["profile"]
print("profile keys:", list(p.keys()))
print("window keys:", list(p["Windows"][0].keys()))
w = next((w for e in profiles.values() if "profile" in e for w in e["profile"]["Windows"] if w.get("Fork")), p["Windows"][0])
print("sample window:", {k: v for k, v in w.items()})
print("summary keys:", one["summary"].keys())
for s in list(sessions.values())[:3]:
    print(s.get("td_keys"), s.get("imu_rate"), s.get("imu_meta"), s.get("imu_active"), s.get("susp_rate"))
