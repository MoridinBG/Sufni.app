"""Grade pieces of the computed profiles: band changes, agreement between runs and the piece edges at the marks.

Band changes per km: band changes between adjacent pieces over the moving distance of all rides with grade.
Agreement: each matched window pair of two runs, each window with the grade of the piece at its midpoint.
Marks: for each row of marks.csv, the piece start nearest the mark and the grade of that piece.
Needs the pieces.csv and windows.csv files of the export tool.
Usage: MATCH_M=6 step10_pieces.py gyro/ext/<run>
"""
import csv, datetime as dt, os, sys; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gcommon import CS, deg
from common import HERE
from gmatch import rides, match
from ghelp import noise_signal

EXT = sys.argv[1]
MATCH_M = float(os.environ.get("MATCH_M", 6))


def read(idx, name):
    path = os.path.join(EXT, f"r{idx:02d}", name)
    return list(csv.DictReader(open(path))) if os.path.exists(path) else []


def pieces(idx):
    return [(float(r["start"]), float(r["end"]), float(r["grade"]) if r["grade"] else None,
             int(r["band"]) if r["band"] else None) for r in read(idx, "pieces.csv")]


P = {r["idx"]: pieces(r["idx"]) for r in rides}

changes = km = 0
for idx, ps in P.items():
    if not ps:
        continue
    km += sum(r["moving"] == "1" for r in read(idx, "windows.csv")) * 10 / 1000
    changes += sum(1 for a, b in zip(ps, ps[1:]) if a[1] == b[0] and a[3] is not None and b[3] is not None and a[3] != b[3])
print(f"rides with grade {sum(1 for p in P.values() if p)}, {km:.1f} km, band changes per km {changes / km:.2f}")


def piece_at(ps, t):
    return next((p for p in ps if p[0] <= t < p[1] and p[2] is not None), None)


pairs, _ = match(MATCH_M)
xa, xb, same = [], [], 0
for a, b, i, j in pairs:
    wa, wb = rides[a]["ws"][i], rides[b]["ws"][j]
    pa = piece_at(P[rides[a]["idx"]], (wa["StartSeconds"] + wa["EndSeconds"]) / 2)
    pb = piece_at(P[rides[b]["idx"]], (wb["StartSeconds"] + wb["EndSeconds"]) / 2)
    if pa is None or pb is None:
        continue
    xa.append(deg(pa[2]))
    xb.append(deg(pb[2]))
    same += pa[3] == pb[3]
noise, _ = noise_signal(xa, xb)
print(f"matched windows {len(xa)} (MATCH_M {MATCH_M:g}), noise of one run {noise:.2f} deg, same band {same / len(xa):.0%}")

starts = {dt.datetime.fromtimestamp(s["timestamp"], dt.UTC).strftime("%Y-%m-%d %H:%M"): k for k, (_, s, _) in enumerate(CS)}
for row in csv.DictReader(open(os.path.join(HERE, "marks.csv"))):
    idx, mark = starts.get(row["ride"]), float(row["seconds"])
    ps = [p for p in P.get(idx, []) if p[2] is not None]
    if not ps:
        print(f"mark {row['ride']} {mark} s: no grade pieces")
        continue
    edge = min(ps, key=lambda p: abs(p[0] - mark))
    print(f"mark {row['ride']} {mark} s ({row['note']}): ride {idx}, nearest edge {edge[0]:.2f} s, "
          f"piece after {deg(edge[2]):.1f} deg to {edge[1]:.2f} s")
