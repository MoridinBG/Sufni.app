"""Step 2: per-sample grade variants -> 10 m window grades with offsets O1 and O2. Output: gyro/variants.pkl.

Grades are sin(downhill pitch), like TerrainWindow.Grade.
V0      today's path (LP 0.08 Hz on x - a_gps/g and z, sin(-atan2)).
V1_fc   LP_fc(accelerometer pitch) + (theta_g - LP_fc(theta_g)); theta_g = integral of +gyro Y (downhill positive).
V2_tau  -Up.X of the FrameAttitudeEstimator copy with correction time constant tau (repository value: 4 s);
        V2_4nt = repository algorithm without the GPS motion compensation.
+S      suspension pitch removed: pitch - atan((front - rear) / wheelbase); front and rear are vertical wheel travel.
O1      two-point offset (today). O2: least-squares offset against the GPS elevation profile of each moving run.
"""
import json, os, pickle, sqlite3, sys; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gcommon import *

con = sqlite3.connect(f"file:{os.path.join(os.path.dirname(GYRO), 'snap.db')}?mode=ro", uri=True)
bike = con.execute("SELECT b.rear_suspension FROM setup s JOIN bike b ON b.id = s.bike_id WHERE s.deleted IS NULL").fetchone()[0]
joints = {j["type"]: j for j in json.loads(bike)["linkage"]["joints"]}
WB = math.hypot(joints["rear_wheel"]["x"] - joints["front_wheel"]["x"], joints["rear_wheel"]["y"] - joints["front_wheel"]["y"])
print(f"wheelbase from linkage joints: {WB:.1f} mm")

FCS = (0.04, 0.08, 0.16)
TAUS = (1, 2, 4, 8, 16)


def o2_offset(r, g):
    """Least-squares c so that the cumulative IMU drop sum(10 (g - c)) follows the GPS drop, per-run intercepts."""
    ws = r.ws
    st = np.array([w["StartSeconds"] for w in ws]); en = np.array([w["EndSeconds"] for w in ws])
    e0, d0 = r.motion_at(st); e1, d1 = r.motion_at(en)
    num = den = 0.0
    run = []

    def flush(run):
        nonlocal num, den
        if len(run) < 3:
            return
        L = np.array([d1[i] - d0[i] for i in run])
        x = np.cumsum(L * np.array([g[i] for i in run]))
        m = np.cumsum(L)
        y = e0[run[0]] - e1[run]
        ok = np.isfinite(y)
        if ok.sum() < 3:
            return
        res, mm = (y - x)[ok], m[ok]
        res = res - res.mean(); mm = mm - mm.mean()
        num += -(res @ mm); den += mm @ mm

    for i, w in enumerate(ws):
        if g[i] is not None and (not run or adjacent(ws[run[-1]], w)):
            run.append(i)
        else:
            flush(run); run = [i] if g[i] is not None else []
    flush(run)
    return num / den


out = {}
for idx in range(len(CS)):
    r = Ride(idx)
    if not r.has_imu or r.entry["profile"]["GradeState"] != 0:
        continue
    rate = r.rate
    has_susp = bool(np.isfinite(r.front).any() and np.isfinite(r.rear).any()) and 1 not in r.entry["profile"]["SilentSensors"]
    delta = np.arctan(np.nan_to_num(r.front - r.rear) / WB) if has_susp else np.zeros(len(r.acc))
    xc = r.acc[:, 0] - r.lacc / G0
    pitch = {}
    x = per_segment(r, lambda s: lowpass(xc[s], rate, 0.08))
    z = per_segment(r, lambda s: lowpass(r.acc[s, 2], rate, 0.08))
    pitch["V0"] = -np.arctan2(x, z)
    pitch["V0+S"] = pitch["V0"] - per_segment(r, lambda s: lowpass(delta[s], rate, 0.08))
    th_a = -np.arctan2(xc, r.acc[:, 2])
    th_g = per_segment(r, lambda s: np.cumsum(r.gyr[s, 1]) / rate)
    for fc in FCS:
        p = per_segment(r, lambda s: lowpass(th_a[s], rate, fc) + th_g[s] - lowpass(th_g[s], rate, fc))
        pitch[f"V1_{fc}"] = p
        pitch[f"V1_{fc}+S"] = p - delta
    for tau in TAUS:
        p = np.arcsin(np.clip(-r.att[f"tau{tau}Ux"], -1, 1))
        pitch[f"V2_{tau}"] = p
        pitch[f"V2_{tau}+S"] = p - delta
    pitch["V2_4nt"] = np.arcsin(np.clip(-r.att["tau4noTrackUx"], -1, 1))
    # world-vertical (yaw) rate from the repository attitude, per window mean |yaw|
    yaw = np.abs(r.gyr[:, 0] * r.att["repoUx"] + r.gyr[:, 1] * r.att["repoUy"] + r.gyr[:, 2] * r.att["repoUz"])
    rng = r.window_ranges()
    wyaw = [None if q is None else float(np.degrees(yaw[q[1]:q[2]].mean())) for q in rng]
    grades = {}
    for name, p in pitch.items():
        g, tg, tw = r.window_grades(np.sin(p))
        z1 = r.o1_offset(g, tg, tw)
        z2 = o2_offset(r, g)
        grades[name] = {"O1": [None if v is None else v - z1 for v in g],
                        "O2": [None if v is None else v - z2 for v in g], "z1": z1, "z2": z2}
    out[idx] = dict(grades=grades, yaw=wyaw, has_susp=has_susp)
    print(f"ride {idx:2d}: suspension correction {'yes' if has_susp else 'no '}; offsets deg V0 O1={math.degrees(grades['V0']['z1']):+.2f} "
          f"O2={math.degrees(grades['V0']['z2']):+.2f}; V2_4 O1={math.degrees(grades['V2_4']['z1']):+.2f} O2={math.degrees(grades['V2_4']['z2']):+.2f}")
pickle.dump(dict(rides=out, WB=WB, names=list(next(iter(out.values()))["grades"])), open(os.path.join(GYRO, "variants.pkl"), "wb"))
