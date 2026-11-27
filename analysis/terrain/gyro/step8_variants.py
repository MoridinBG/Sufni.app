"""Grade variants for the cutoff question: 0.08, 0.15 and 0.3 Hz, suspension pitch, no braking correction, gyroscope.

Output: gyro/variants8.pkl (format of step2_variants).
"""
import json, os, pickle, sqlite3, sys; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gcommon import *
con = sqlite3.connect(f"file:{os.path.join(os.path.dirname(GYRO), 'snap.db')}?mode=ro", uri=True)
bike = con.execute("SELECT b.rear_suspension FROM setup s JOIN bike b ON b.id = s.bike_id WHERE s.deleted IS NULL").fetchone()[0]
joints = {j["type"]: j for j in json.loads(bike)["linkage"]["joints"]}
WB = math.hypot(joints["rear_wheel"]["x"] - joints["front_wheel"]["x"], joints["rear_wheel"]["y"] - joints["front_wheel"]["y"])
print(f"wheelbase from linkage joints: {WB:.1f} mm")
out = {}
for idx in range(len(CS)):
    r = Ride(idx)
    if not r.has_imu or r.entry["profile"]["GradeState"] != 0:
        continue
    rate = r.rate
    has_susp = bool(np.isfinite(r.front).any() and np.isfinite(r.rear).any()) and 1 not in r.entry["profile"]["SilentSensors"]
    delta = np.arctan(np.nan_to_num(r.front - r.rear) / WB) if has_susp else np.zeros(len(r.acc))
    LP = lambda v, fc: per_segment(r, lambda s: lowpass(v[s], rate, fc))
    xc = r.acc[:, 0] - r.lacc / G0
    pitch = {}
    for fc in (0.08, 0.15, 0.3):
        p = -np.arctan2(LP(xc, fc), LP(r.acc[:, 2], fc))
        pitch[f"A{fc}"] = p
        pitch[f"A{fc}+S"] = p - LP(delta, fc)
        pitch[f"A{fc}_nobrake"] = -np.arctan2(LP(r.acc[:, 0], fc), LP(r.acc[:, 2], fc))
    th_a = -np.arctan2(xc, r.acc[:, 2])
    roll = np.arctan2(r.att["repoUy"], r.att["repoUz"])
    rates = {"G": r.gyr[:, 1],
             "Gp": r.gyr[:, 1] * np.cos(roll) + r.gyr[:, 2] * np.sin(roll),
             "Gm": r.gyr[:, 1] * np.cos(roll) - r.gyr[:, 2] * np.sin(roll)}
    for gname, q in rates.items():
        th_g = per_segment(r, lambda s: np.cumsum(q[s]) / rate)
        for fc in (0.04, 0.08):
            p = per_segment(r, lambda s: lowpass(th_a[s], rate, fc) + th_g[s] - lowpass(th_g[s], rate, fc))
            pitch[f"{gname}{fc}"] = p
            pitch[f"{gname}{fc}+S"] = p - delta
    grades = {}
    for name, p in pitch.items():
        g, tg, tw = r.window_grades(np.sin(p))
        z1 = r.o1_offset(g, tg, tw)
        grades[name] = {"O1": [None if v is None else v - z1 for v in g], "z1": z1}
    yaw = np.abs(r.gyr[:, 0] * r.att["repoUx"] + r.gyr[:, 1] * r.att["repoUy"] + r.gyr[:, 2] * r.att["repoUz"])
    wyaw = [None if q is None else float(np.degrees(yaw[q[1]:q[2]].mean())) for q in r.window_ranges()]
    out[idx] = dict(grades=grades, yaw=wyaw, has_susp=has_susp)
    print(f"ride {idx:2d} done, suspension {'yes' if has_susp else 'no'}", flush=True)
pickle.dump(dict(rides=out, WB=WB, names=list(next(iter(out.values()))["grades"])), open(os.path.join(GYRO, "variants8.pkl"), "wb"))
