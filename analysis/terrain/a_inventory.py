import sys; sys.path.insert(0, __import__("os").path.dirname(__file__))
from collections import Counter
import datetime as dt
from common import *

S = D["sessions"]
print("sessions total", len(S), "not deleted", sum(s["deleted"] is None for s in S.values()))
print("profile outcomes", Counter(e["outcome"] for e in D["profiles"].values()))
print("algorithm versions", Counter(e["key"]["AlgorithmVersion"] for e in D["profiles"].values()))
print("setups", len(D["setups"]), "bikes", len(D["bikes"]), "setup->bike", D["setups"])
print("setups used by sessions", Counter(s["setup"] for s in S.values()))
print("tune combos (all sessions)", len(set(s["tune"] for s in S.values())))
cs = computed_sessions()
print("tune combos (computed)", len(set(s["tune"] for _, s, _ in cs)))
setup_idx = {sid: i for i, sid in enumerate(sorted(D["setups"]))}
tot_mov = tot_desc = 0
print("idx date setup imu_rate meta(loc:lsb) silent gradeState moving_km descent_km fork_w frame_w front_w rear_w")
for i, (sid, s, e) in enumerate(cs):
    p = e["profile"]
    ws = p["Windows"]
    mov = sum(w["Moving"] for w in ws) * 10 / 1000
    desc = sum(is_descent(w) for w in ws) * 10 / 1000
    tot_mov += mov; tot_desc += desc
    meta = s.get("imu_meta") or []
    meta = [(m["LocationId"], m["AccelLsbPerG"]) if isinstance(m, dict) else (m[0], m[1]) for m in meta]
    nf = sum(1 for w in ws if w.get("Fork")); nr = sum(1 for w in ws if w.get("Frame"))
    nfr = sum(1 for w in ws if w.get("Front")); nre = sum(1 for w in ws if w.get("Rear"))
    d = dt.datetime.utcfromtimestamp(s["timestamp"]).strftime("%Y-%m-%d")
    print(i, d, setup_idx.get(s["setup"]), s.get("imu_rate"), meta, p.get("SilentSensors"), p["GradeState"],
          round(mov, 2), round(desc, 2), nf, nr, nfr, nre, "FrameScale", p["FrameScale"], "ForkScale", p["ForkScale"])
print("total moving km", round(tot_mov, 2), "descent km", round(tot_desc, 2))
print("non-computed sessions:")
for sid, e in D["profiles"].items():
    if "profile" not in e:
        s = S[sid]
        print(dt.datetime.utcfromtimestamp(s["timestamp"]).strftime("%Y-%m-%d"), "outcome", e["outcome"], "imu", s.get("imu_rate"))
