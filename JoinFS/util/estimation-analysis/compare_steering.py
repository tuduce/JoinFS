"""How well the simulator drew a remote aircraft, by steering law and flight phase.

The estimation log has, in each sample row, where the simulator last reported the injected object
(sim*), the simulator's own clock at that report (simClock, logs from 2026-10-05) and the steering
law that was in force (steer; the tester package alternates the laws every two minutes). With both
PCs' logs on one time line (analyze_estimation.py) the sender's true state at that moment is known,
so the drawn error is the simulator's reported position minus the truth, signed, in metres:
along track, cross track and vertical.

  python compare_steering.py JoinFS-test-logs-A.zip JoinFS-test-logs-B.zip [--from 2026-10-05] [--to 2026-10-06]

- The time of a report. With simClock, the report is timed by the simulator's clock plus the
  smallest (handled - simClock) of the last second, like the sender's stamps (SimClockStamper), so
  that the jitter of when JoinFS handled the report drops out. Without it (older logs) the time it
  was handled is used, which adds a few ms of noise: along track, 3-5 ms x the speed.
- Phases: level (|bank| < 10), turning (30-60), steep (60-120) and inverted (> 120 degrees of bank).
- One day at a time (--from/--to): two sessions of the same pair of PCs share one time line.
"""
import argparse, bisect, collections, datetime, math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import analyze_estimation as ae

PHASES = (('level', lambda b: b < 10), ('turning', lambda b: 30 <= b < 60),
          ('steep', lambda b: 60 <= b < 120), ('inverted', lambda b: b >= 120))
MIN_SPEED = 50.0   # m/s: along track needs a direction


def pct(v, q):
    v = sorted(v)
    return v[min(len(v) - 1, int(q * len(v)))] if v else math.nan


class SimClockMap:
    """Local time of a simulator-clock reading: the reading plus the smallest (handled - reading) of the last second"""
    def __init__(self, window=1.0):
        self.window, self.q = window, collections.deque()

    def local(self, handled, sim_clock):
        x = handled - sim_clock
        while self.q and self.q[-1][1] >= x:
            self.q.pop()
        self.q.append((handled, x))
        while self.q[0][0] < handled - self.window:
            self.q.popleft()
        return sim_clock + self.q[0][1]


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
        T = [s.netTime for s in S]

        def now_sender(local):
            sys_r = r.clock.to_utc(local)
            shared = sys_r + r.pc.correction(sys_r)
            sys_o = shared - o.pc.correction(shared - o.pc.correction(shared))
            return o.clock.to_local(sys_o)

        # (law, phase) -> [(along, cross, vertical)]; and the same with the handling time, for the noise
        rows = collections.defaultdict(list)
        raw = collections.defaultdict(list)
        clock = SimClockMap()
        timed = 0
        total = 0
        for s in sorted(S, key=lambda s: s.local):
            if math.isnan(s.simTime) or s.paused or s.ground:
                continue
            total += 1
            handled = s.simTime
            if not math.isnan(s.simClock):
                local = clock.local(handled, s.simClock)
                timed += 1
            else:
                local = handled
            for t, store in ((local, rows), (handled, raw)):
                ts = ae.interp_state(S, T, now_sender(t))
                if ts is None or math.hypot(ts['vx'], ts['vz']) < MIN_SPEED:
                    continue
                err = ae.split(s.simLat, s.simLon, s.simAlt, ts)
                bank = abs(math.degrees(ts['bank']))
                law = s.steer or '(not logged)'
                for phase, sel in PHASES:
                    if sel(bank):
                        store[(law, phase)].append(err)
                        store[(law, 'all')].append(err)
                        break
        print(f"\n{r.pc.computer} sees {d.callsign}: {total} airborne reports, {timed} timed by the simulator's clock")
        for title, store in (("timed by the simulator's clock" if timed else "timed by when they were handled", rows),
                             ("timed by when they were handled (the noise reference)", raw)):
            if store is raw and not timed:
                continue
            print(f"  {title}")
            print(f"  {'law':12} {'phase':9} {'n':>7}  {'along p50':>9} {'|along| p95':>11}  {'cross |p95|':>11}  {'vert p50':>8} {'|vert| p95':>10} {'|vert| p99':>10}")
            for (law, phase) in sorted(store, key=lambda k: (k[0], ['all', 'level', 'turning', 'steep', 'inverted'].index(k[1]))):
                v = store[(law, phase)]
                if len(v) < 200:
                    continue
                along, cross, vert = zip(*v)
                print(f"  {law:12} {phase:9} {len(v):7d}  {pct(along, .5):+9.3f} {pct([abs(x) for x in along], .95):11.3f}  "
                      f"{pct([abs(x) for x in cross], .95):11.3f}  {pct(vert, .5):+8.3f} {pct([abs(x) for x in vert], .95):10.3f} {pct([abs(x) for x in vert], .99):10.3f}")


if __name__ == '__main__':
    main()
