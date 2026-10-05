"""Score clock models against the true age of the samples, on the estimation logs of two PCs.

The estimation log records, for each prediction, the age the receiver's clock model gave (predAge).
With both PCs' logs on one time line (analyze_estimation.py) the true age is known too, so any
clock model that can be computed from the logged samples (stamp, arrival, round trip) can be scored
the same way: the error is model age minus true age, in ms.

  python compare_clocks.py JoinFS-test-logs-A.zip JoinFS-test-logs-B.zip [--from 2026-10-04] [--to 2026-10-05]

Models: 'logged' (what ran), and 'MinOffset' - the same arithmetic as Estimation/MinOffsetClock.cs
(1 s buckets over 10 s for the offset, 5 s buckets over a minute for the round trip), without its
restart rule. Give it one day at a time (--from/--to): two sessions of the same pair of PCs share
one time line. The alignment of the two PCs assumes the fastest way each direction takes the same
time, so neither model can be scored on an asymmetric path.
"""
import argparse, datetime, math, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import analyze_estimation as ae


class Buckets:
    def __init__(self, n, width):
        self.n, self.width, self.v = n, width, {}

    def add(self, t, x):
        k = math.floor(t / self.width)
        self.v[k] = min(self.v.get(k, math.inf), x)
        for old in [o for o in self.v if o <= k - self.n]:
            del self.v[old]

    def min(self, t):
        k = math.floor(t / self.width)
        vals = [x for o, x in self.v.items() if k - self.n < o <= k]
        return min(vals) if vals else None


def pct(v, q):
    v = sorted(v)
    return v[min(len(v) - 1, int(q * len(v)))]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('logs', nargs='+')
    ap.add_argument('--from', dest='lo', default='2000-01-01')
    ap.add_argument('--to', dest='hi', default='2100-01-01')
    args = ap.parse_args()
    lo = ae.unix(datetime.datetime.fromisoformat(args.lo))
    hi = ae.unix(datetime.datetime.fromisoformat(args.hi))
    pcs = [ae.PcLog(p) for p in args.logs]
    sessions = [s for pc in pcs for s in pc.sessions if s.clock and lo <= s.start < hi]
    directions = []
    for ses in sessions:
        for callsign, rows in ses.objects.items():
            if len(rows) >= 1000 and any(not math.isnan(s.predLocal) for s in rows):
                d = ae.Direction(ses, callsign)
                ae.find_owner(d, sessions)
                directions.append(d)
    ae.calibrate(directions)

    for d in directions:
        if not d.calibrated:
            continue
        r, o = d.receiver, d.owner
        S = sorted((s for seg in d.owner_segments for s in seg), key=lambda s: s.netTime)

        def now_sender(local):
            sys_r = r.clock.to_utc(local)
            shared = sys_r + r.pc.correction(sys_r)
            sys_o = shared - o.pc.correction(shared - o.pc.correction(shared))
            return o.clock.to_local(sys_o)

        offsets, rtts = Buckets(10, 1.0), Buckets(12, 5.0)
        errs = {'logged': [], 'MinOffset': []}
        prev = None
        for s in S:
            p, prev = prev, s
            if p is not None:
                offsets.add(p.receivedAt, p.receivedAt - p.netTime)
                if 0 < p.rtt < 5:
                    rtts.add(p.receivedAt, p.rtt)
            if p is None or math.isnan(s.predLocal) or s.paused or abs(s.predFrom - p.netTime) > 1e-6:
                continue
            t = s.predLocal
            fastest, rtt = offsets.min(t), rtts.min(t)
            if fastest is None:
                continue
            true_age = now_sender(t) - s.predFrom
            age = (t - p.receivedAt) + ((p.receivedAt - p.netTime) - fastest) + (rtt or 0.0) / 2
            errs['logged'].append((s.predAge - true_age) * 1000)
            errs['MinOffset'].append((age - true_age) * 1000)
        print(f"\n{r.pc.computer} watching {d.callsign}: {len(errs['logged'])} predictions")
        print(f"{'model':10} {'p5':>7} {'p50':>7} {'p95':>7} {'|e| p95':>8} {'|e| p99':>8}   (ms, model age - true age)")
        for name, v in errs.items():
            a = [abs(x) for x in v]
            print(f"{name:10} {pct(v, .05):7.2f} {pct(v, .5):7.2f} {pct(v, .95):7.2f} {pct(a, .95):8.2f} {pct(a, .99):8.2f}")


if __name__ == '__main__':
    main()
