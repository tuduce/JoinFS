#!/usr/bin/env python3
"""Analyse JoinFS estimation logs from one or more test PCs (docs/position-estimation-plan.md 6.1).

Input: the zips that "Collect test logs.bat" uploads (or folders they were extracted to). Each holds
a PC's estimation-*.csv (one per JoinFS session), clock-*.txt (the PC's offset to time.windows.com
at start and at collection) and the monitor logs.

With logs from more than one PC of the same session, everything is put on one true-UTC time line:
the sender's netTime (its ElapsedTime) maps to its system UTC through its CSV's clock rows, and the
system UTC to true UTC through its clock checks. That gives the real one-way delay per direction,
the distance between the aircraft (formation), and the sender's state at any moment.

Errors per direction (receiver PC watching a sender's aircraft), against the truth = the sender's
own samples interpolated in sender time:
  model  - at the horizon the receiver's clock chose (predFrom + predAge): the estimator alone
  total  - against the sender's state at the moment of the prediction: estimator + timing
  drawn  - where the receiver's simulator actually drew the aircraft (steering included)
Without the sender's log, 'now' assumes a one-way delay of half the minimum RTT.

Standard library only. Usage:
  python analyze_estimation.py LOG.zip [LOG2.zip ...] [--out report.md]
"""
import argparse, bisect, csv, io, math, os, re, statistics, sys, zipfile
from collections import defaultdict
from datetime import datetime, timezone

R = 6371009.0           # Earth radius as JoinFS uses it (Vector.GeodesicDistance)
GAP = 0.15              # a sample gap longer than this is a dropout; truth is not interpolated across it
EPISODE = 10.0          # metres: errors above this are listed as episodes
CLOSE = 100.0           # metres: "close formation"


# ----------------------------------------------------------------------------- loading

def unix(dt):
    return dt.replace(tzinfo=timezone.utc).timestamp()


def parse_iso_utc(text):
    m = re.match(r'(\d{4})-(\d\d)-(\d\d)T(\d\d):(\d\d):(\d\d)(\.\d+)?Z', text)
    if not m:
        return None
    y, mo, d, h, mi, s = (int(x) for x in m.groups()[:6])
    return unix(datetime(y, mo, d, h, mi, s)) + (float(m.group(7)) if m.group(7) else 0.0)


class Source:
    """A zip or a folder, as {relative path with '/': opener}"""
    def __init__(self, path):
        self.path = path
        self.files = {}
        if zipfile.is_zipfile(path):
            z = zipfile.ZipFile(path)
            for info in z.infolist():
                if not info.is_dir():
                    # Windows PowerShell 5.1's Compress-Archive writes backslashes
                    self.files[info.filename.replace('\\', '/')] = (lambda i=info: z.read(i))
        else:
            for root, _, names in os.walk(path):
                for n in names:
                    full = os.path.join(root, n)
                    rel = os.path.relpath(full, path).replace('\\', '/')
                    self.files[rel] = (lambda p=full: open(p, 'rb').read())

    def text(self, rel):
        return self.files[rel]().decode('utf-8', errors='replace')


class Sample:
    __slots__ = ('owner', 'node', 'callsign', 'local', 'netTime', 'receivedAt', 'rtt', 'lat', 'lon', 'alt',
                 'pitch', 'bank', 'hdg', 'vx', 'vy', 'vz', 'avx', 'avy', 'avz', 'ax', 'ay', 'az', 'ground', 'paused',
                 'predLocal', 'predFrom', 'predAge', 'predLat', 'predLon', 'predAlt', 'predPitch', 'predBank', 'predHdg',
                 'simTime', 'simLat', 'simLon', 'simAlt', 'simPitch', 'simBank', 'simHdg')


FIELDS = [('local', 'local'), ('netTime', 'netTime'), ('receivedAt', 'receivedAt'), ('rtt', 'rtt'),
          ('lat', 'lat'), ('lon', 'lon'), ('alt', 'alt'), ('pitch', 'pitch'), ('bank', 'bank'), ('hdg', 'heading'),
          ('vx', 'vx'), ('vy', 'vy'), ('vz', 'vz'), ('avx', 'avx'), ('avy', 'avy'), ('avz', 'avz'),
          ('ax', 'ax'), ('ay', 'ay'), ('az', 'az'), ('predLocal', 'predLocal'), ('predFrom', 'predFrom'),
          ('predAge', 'predAge'), ('predLat', 'predLat'), ('predLon', 'predLon'), ('predAlt', 'predAlt'),
          ('predPitch', 'predPitch'), ('predBank', 'predBank'), ('predHdg', 'predHeading'),
          ('simTime', 'simTime'), ('simLat', 'simLat'), ('simLon', 'simLon'), ('simAlt', 'simAlt'),
          ('simPitch', 'simPitch'), ('simBank', 'simBank'), ('simHdg', 'simHeading')]


def num(text):
    return float(text) if text != '' else math.nan


class ClockMap:
    """A JoinFS process's ElapsedTime against its PC's system UTC (the CSV's clock rows)"""
    def __init__(self, rows):
        rows.sort()
        self.local = [l for l, _ in rows]
        self.utc = [u for _, u in rows]

    def _interp(self, xs, ys, x):
        i = bisect.bisect_right(xs, x) - 1
        if i < 0:
            return ys[0] + (x - xs[0])
        if i + 1 >= len(xs):
            return ys[-1] + (x - xs[-1])
        w = (x - xs[i]) / (xs[i + 1] - xs[i])
        return ys[i] + w * (ys[i + 1] - ys[i])

    def to_utc(self, local):
        return self._interp(self.local, self.utc, local)

    def to_local(self, utc):
        return self._interp(self.utc, self.local, utc)

    def covers(self, local, margin=2.0):
        return self.local[0] - margin <= local <= self.local[-1] + margin


class Session:
    """One estimation CSV: one JoinFS run on one PC"""
    def __init__(self, pc, rel, text):
        self.pc = pc
        self.rel = rel
        m = re.search(r'estimation-(\d+)-(\d{8})-(\d{6})\.csv$', rel)
        self.port = m.group(1) if m else '?'
        clock_rows = []
        self.objects = defaultdict(list)
        for row in csv.DictReader(io.StringIO(text)):
            if row['kind'] == 'clock':
                clock_rows.append((float(row['local']), float(row['utc'])))
            elif row['kind'] == 'sample':
                s = Sample()
                s.owner, s.node, s.callsign = row['owner'], row['node'], row['callsign']
                for attr, col in FIELDS:
                    setattr(s, attr, num(row[col]))
                s.ground, s.paused = row['ground'] == '1', row['paused'] == '1'
                self.objects[s.callsign].append(s)
        self.clock = ClockMap(clock_rows) if clock_rows else None
        # the PC's time zone, from the file name (local wall time) against the first clock row
        self.tz = 0.0
        if m and clock_rows:
            wall = unix(datetime.strptime(m.group(2) + m.group(3), '%Y%m%d%H%M%S'))
            self.tz = round((wall - min(u for _, u in clock_rows)) / 900.0) * 900.0
        self.start = self.clock.utc[0] if self.clock else math.nan
        self.end = self.clock.utc[-1] if self.clock else math.nan


class PcLog:
    """Everything one PC uploaded"""
    def __init__(self, path):
        self.path = path
        src = Source(path)
        self.name = os.path.basename(path)
        self.computer = '?'
        self.package = {}
        self.checks = []        # (system utc, offset to time.windows.com in s)
        self.sessions = []
        self.shifts = []        # calibration of this PC's time line against its peer: (time, shift) (calibrate)
        self.monitor = []       # (system utc, text)
        monitor_texts = []
        for rel in sorted(src.files):
            base = rel.rsplit('/', 1)[-1]
            if base == 'package-info.txt':
                for line in src.text(rel).splitlines():
                    k, _, v = line.partition(' ')
                    self.package[k] = v
            elif base.startswith('clock-') and base.endswith('.txt'):
                self._clock_check(src.text(rel))
            elif base.startswith('estimation-') and base.endswith('.csv'):
                self.sessions.append(Session(self, rel, src.text(rel)))
            elif base.startswith('log-') and base.endswith('.txt'):
                monitor_texts.append(src.text(rel))
        self.checks.sort()
        tz = self.sessions[0].tz if self.sessions else 0.0
        for text in monitor_texts:
            for line in text.splitlines():
                m = re.match(r'(\d\d)/(\d\d)/(\d{4}) (\d\d):(\d\d):(\d\d)\.(\d{3}) - (.*)', line)
                if m:
                    d, mo, y, h, mi, s, ms = (int(x) for x in m.groups()[:7])
                    self.monitor.append((unix(datetime(y, mo, d, h, mi, s)) + ms / 1000.0 - tz, m.group(8)))
        self.monitor.sort()
        self.monitor_utc = [u for u, _ in self.monitor]

    def _clock_check(self, text):
        utc, offsets = None, []
        for line in text.splitlines():
            if line.startswith('utc '):
                utc = parse_iso_utc(line[4:].strip())
            elif line.startswith('computer '):
                self.computer = line[9:].strip()
            else:
                m = re.match(r'\d\d:\d\d:\d\d, ([+-]\d+\.\d+)s', line.strip())
                if m:
                    offsets.append(float(m.group(1)))
        if utc is not None and offsets:
            self.checks.append((utc, statistics.median(offsets)))

    def correction(self, sys_utc):
        """Shared time line minus system UTC at a moment: the w32tm offset (server minus local,
        linear between the clock checks; 0 without any) minus the calibration shift"""
        return self._w32tm(sys_utc) - self._shift(sys_utc)

    def _w32tm(self, sys_utc):
        if not self.checks:
            return 0.0
        us = [u for u, _ in self.checks]
        os_ = [o for _, o in self.checks]
        i = bisect.bisect_right(us, sys_utc) - 1
        if i < 0:
            return os_[0]
        if i + 1 >= len(us):
            return os_[-1]
        w = (sys_utc - us[i]) / (us[i + 1] - us[i])
        return os_[i] + w * (os_[i + 1] - os_[i])

    def _shift(self, sys_utc):
        """Calibration against the peer, linear between the windows (calibrate)"""
        if not self.shifts:
            return 0.0
        t = sys_utc + self._w32tm(sys_utc)
        ts = [p for p, _ in self.shifts]
        i = bisect.bisect_right(ts, t) - 1
        if i < 0:
            return self.shifts[0][1]
        if i + 1 >= len(ts):
            return self.shifts[-1][1]
        w = (t - ts[i]) / (ts[i + 1] - ts[i])
        return self.shifts[i][1] + w * (self.shifts[i + 1][1] - self.shifts[i][1])

    def events(self, sys_utc, window=5.0):
        """Monitor lines near a moment (system UTC), without the per-frame chatter"""
        i = bisect.bisect_left(self.monitor_utc, sys_utc - window)
        out = []
        while i < len(self.monitor) and self.monitor_utc[i] <= sys_utc + window:
            text = self.monitor[i][1]
            if not re.match(r'(RawPos|NETWORK:|JFP2:|SetData|DoSimEvent|DIAG:|ElevatedPlatform)', text):
                out.append((self.monitor_utc[i] - sys_utc, text))
            i += 1
        return out


# ----------------------------------------------------------------------------- geometry

def wrap(a):
    return (a + math.pi) % (2 * math.pi) - math.pi


def interp_state(series, times, t, gap=GAP):
    """Linear interpolation of a sample series at time t (in the series' time base)"""
    i = bisect.bisect_right(times, t) - 1
    if i < 0 or i + 1 >= len(series):
        return None
    a, b = series[i], series[i + 1]
    dt = times[i + 1] - times[i]
    if dt <= 0 or dt > gap or a.paused or b.paused:
        return None
    w = (t - times[i]) / dt
    out = {k: getattr(a, k) + w * (getattr(b, k) - getattr(a, k)) for k in ('lat', 'lon', 'alt', 'vx', 'vy', 'vz')}
    for k in ('pitch', 'bank', 'hdg'):
        out[k] = getattr(a, k) + w * wrap(getattr(b, k) - getattr(a, k))
    out['ground'] = a.ground
    return out


def split(lat, lon, alt, tr):
    """Error from the truth, as along-track, cross-track and vertical metres"""
    de = wrap(lon - tr['lon']) * R * math.cos(tr['lat'])
    dn = (lat - tr['lat']) * R
    du = alt - tr['alt']
    sp = math.hypot(tr['vx'], tr['vz'])
    if sp < 1.0:
        return math.hypot(de, dn), 0.0, du
    ue, un = tr['vx'] / sp, tr['vz'] / sp
    return de * ue + dn * un, -de * un + dn * ue, du


def distance(a, b):
    de = wrap(a['lon'] - b['lon']) * R * math.cos(a['lat'])
    dn = (a['lat'] - b['lat']) * R
    return math.sqrt(de * de + dn * dn + (a['alt'] - b['alt']) ** 2)


def extrapolate(s, age, acc_factor=1.0, euler_rates=False):
    """Sim.Pos.Extrapolate offline (acc_factor 1.0 is the code as it is, a t^2)"""
    age = max(-2.0, min(2.0, age))
    k = acc_factor * age * age
    e, u, n = s.vx * age + s.ax * k, s.vy * age + s.ay * k, s.vz * age + s.az * k
    lat, lon, alt = s.lat + n / R, s.lon + e / (R * math.cos(s.lat)), s.alt + u
    if euler_rates:
        rate = (s.avx * math.sin(s.bank) + s.avy * math.cos(s.bank)) / max(0.1, math.cos(s.pitch))
        return lat, lon, alt, s.hdg + rate * age
    return lat, lon, alt, s.hdg + s.avy * age


# ----------------------------------------------------------------------------- statistics

def pct(values, q):
    v = sorted(values)
    return v[min(len(v) - 1, int(q * len(v)))] if v else math.nan


def summary(values, unit_scale=1.0):
    """p50, p95, p99 and max of |value|"""
    a = [abs(x) * unit_scale for x in values if not math.isnan(x)]
    if not a:
        return None
    a.sort()
    return a[len(a) // 2], a[int(len(a) * .95)], a[int(len(a) * .99)], a[-1], len(a)


def over(values, limit):
    a = [x for x in values if not math.isnan(x)]
    return 100.0 * sum(1 for x in a if abs(x) > limit) / len(a) if a else math.nan


def fmt(x, digits=2):
    return '-' if x is None or (isinstance(x, float) and math.isnan(x)) else f'{x:.{digits}f}'


# ----------------------------------------------------------------------------- one direction

class Direction:
    """A receiver session watching one aircraft"""
    def __init__(self, receiver, callsign):
        self.receiver = receiver
        self.callsign = callsign
        rows = receiver.objects[callsign]
        # the sender's clock restarts with its JoinFS: stretches where netTime only grows
        self.segments, current = [], [rows[0]]
        for a, b in zip(rows, rows[1:]):
            if b.netTime < a.netTime or b.receivedAt - a.receivedAt > 60:
                self.segments.append(current)
                current = []
            current.append(b)
        self.segments.append(current)
        self.owner = None           # Session of the sender, when its log is here
        self.owner_segments = []
        self.calibrated = False     # both directions between the two PCs are here: one time line

    def samples(self):
        return [s for seg in self.owner_segments for s in seg]

    def latency(self, s):
        """One-way delay of a sample on the PCs' shared time line (clock checks plus calibration)"""
        o, r = self.owner, self.receiver
        send_sys = o.clock.to_utc(s.netTime)
        recv_sys = r.clock.to_utc(s.receivedAt)
        return (recv_sys + r.pc.correction(recv_sys)) - (send_sys + o.pc.correction(send_sys))


CALIBRATION_WINDOW = 20.0  # seconds


def calibrate(directions):
    """Put each pair of PCs that watched each other on one time line. Their system clocks are not
    good enough: the checks against time.windows.com are only good to tens of ms over the internet,
    and Windows Time steps a drifting clock between the checks. So the PCs are aligned from their
    own traffic, as NTP does, continuously: in each window the fastest sample each way is taken to
    have the same one-way delay. The sum of the two ways does not depend on the alignment, so it
    checks the data against the measured round trip."""
    notes = []
    by_pair = {(d.owner.pc, d.receiver.pc): d for d in directions if d.owner is not None}
    done = set()
    for (sender, receiver), d in by_pair.items():
        back = by_pair.get((receiver, sender))
        if back is None or (receiver, sender) in done:
            continue
        done.add((sender, receiver))

        def windows(direction, sender_side):
            """Fastest one-way delay per window, keyed by the window on the sender PC's line"""
            out = defaultdict(lambda: math.inf)
            o = direction.owner
            for s in direction.samples():
                if s.paused:
                    continue
                if sender_side:
                    sys_t, pc = o.clock.to_utc(s.netTime), o.pc
                else:
                    sys_t, pc = direction.receiver.clock.to_utc(s.receivedAt), direction.receiver.pc
                k = int((sys_t + pc.correction(sys_t)) // CALIBRATION_WINDOW)
                out[k] = min(out[k], direction.latency(s))
            return out

        a = windows(d, True)        # sender -> receiver, by the sender's time
        b = windows(back, False)    # receiver -> sender, by its arrival at the sender
        raw_a = statistics.median(d.latency(s) for s in d.samples()[::10])
        raw_b = statistics.median(back.latency(s) for s in back.samples()[::10])
        rtt = pct([s.rtt for s in d.samples() if s.rtt > 0], .5)
        # shift the sender PC's line in each window so both ways have the same fastest delay
        # (correction() subtracts the shift: it adds to the delay one way and takes from the other)
        points = sorted(((k + 0.5) * CALIBRATION_WINDOW, (b[k] - a[k]) / 2) for k in a if k in b)
        if len(points) < 3:
            continue
        sender.shifts = points
        d.calibrated = back.calibrated = True
        shifts = [p for _, p in points]
        fastest = statistics.median((a[k] + b[k]) / 2 for k in a if k in b)
        notes.append((d, back, raw_a, raw_b, rtt, (min(shifts), statistics.median(shifts), max(shifts)), fastest))
    return notes


def find_owner(direction, sessions):
    """The session (on another PC) whose clock the aircraft's netTime runs on"""
    best = None
    for ses in sessions:
        if ses.pc is direction.receiver.pc or ses.clock is None:
            continue
        matched = []
        for seg in direction.segments:
            inside = [s for s in seg[::50] if ses.clock.covers(s.netTime)]
            if len(inside) < 0.8 * len(seg[::50]):
                continue
            lag = statistics.median(direction.receiver.clock.to_utc(s.receivedAt) - ses.clock.to_utc(s.netTime) for s in inside)
            if -3.0 < lag < 3.0:    # the PCs' clocks may be a second or two apart
                matched.append(seg)
        n = sum(len(s) for s in matched)
        if matched and (best is None or n > best[0]):
            best = (n, ses, matched)
    if best:
        direction.owner, direction.owner_segments = best[1], best[2]


def analyse(direction, own_tracks):
    """Errors, timing and episodes for one direction"""
    r = direction.receiver
    out = {'dir': direction}
    segs = direction.owner_segments or [max(direction.segments, key=len)]
    S = [s for seg in segs for s in seg]
    S.sort(key=lambda s: s.netTime)
    T = [s.netTime for s in S]
    out['samples'] = len(S)
    out['minutes'] = sum(seg[-1].netTime - seg[0].netTime for seg in segs) / 60.0
    gaps = [(b.netTime - a.netTime, a, b) for seg in segs for a, b in zip(seg, seg[1:])]
    out['gap_p50'] = pct([g for g, _, _ in gaps], .5)
    out['dropouts'] = [(g, a, b) for g, a, b in gaps if g > GAP]
    out['rtt'] = pct([s.rtt for s in S if s.rtt > 0], .5)
    out['rtt_min'] = pct([s.rtt for s in S if s.rtt > 0], .05)
    out['queue'] = pct([s.local - s.receivedAt for s in S], .5)
    out['speed'] = pct([math.hypot(s.vx, s.vz) for s in S if not s.ground], .5)

    # timestamp honesty: distance flown between samples / speed against the stamped difference
    mism = []
    for seg in segs:
        for a, b in zip(seg, seg[1:]):
            dt = b.netTime - a.netTime
            if a.ground or a.paused or b.paused or not 0 < dt <= 0.1:
                continue
            ve, vn = (a.vx + b.vx) / 2, (a.vz + b.vz) / 2
            sp = math.hypot(ve, vn)
            if sp < 50:
                continue
            de = wrap(b.lon - a.lon) * R * math.cos(a.lat)
            dn = (b.lat - a.lat) * R
            mism.append(((de * ve + dn * vn) / sp / sp - dt) * 1000)
    out['stamp'] = (pct(mism, .05), pct(mism, .5), pct(mism, .95), over(mism, 5.0)) if mism else None

    # clock: the sender's time at a receiver local time
    if direction.calibrated:
        lat = [direction.latency(s) for s in S[::5]]
        out['oneway'] = (pct(lat, .05), pct(lat, .5), pct(lat, .95))
        out['timebase'] = 'both PCs, aligned by their two-way traffic'
        o = direction.owner

        def now_sender(local):
            sys_r = r.clock.to_utc(local)
            shared = sys_r + r.pc.correction(sys_r)
            # the owner's correction barely changes within a second: one fixed-point step
            sys_o = shared - o.pc.correction(shared - o.pc.correction(shared))
            return o.clock.to_local(sys_o)
    else:
        # lower envelope of arrival - netTime (drift follows) minus half the minimum round trip
        out['oneway'] = None
        out['timebase'] = 'assumed: one-way delay = RTT p5 / 2'
        oneway = out['rtt_min'] / 2
        d = [(s.receivedAt, s.receivedAt - s.netTime) for s in S if not s.paused]
        buckets = defaultdict(lambda: math.inf)
        t0 = d[0][0]
        for a, x in d:
            k = int((a - t0) // 10)
            buckets[k] = min(buckets[k], x)
        keys = sorted(buckets)
        env_t = [t0 + (k + 0.5) * 10 for k in keys]
        env_v = [buckets[k] for k in keys]

        def now_sender(local):
            i = bisect.bisect_right(env_t, local) - 1
            i = max(0, min(len(env_t) - 2, i)) if len(env_t) > 1 else 0
            if len(env_t) == 1:
                v = env_v[0]
            else:
                w = (local - env_t[i]) / (env_t[i + 1] - env_t[i])
                v = env_v[i] + w * (env_v[i + 1] - env_v[i])
            return local - (v - oneway)
    out['drift'] = None
    if len(S) > 1000:
        a, b = S[len(S) // 10], S[-len(S) // 10]
        # sender clock against receiver clock, ms per hour (from the arrival envelope ends)
        out['drift'] = ((b.receivedAt - b.netTime) - (a.receivedAt - a.netTime)) / (b.receivedAt - a.receivedAt) * 3.6e6

    phases = {'all': lambda tr: True, 'ground': lambda tr: tr['ground'],
              'straight': lambda tr: not tr['ground'] and abs(math.degrees(tr['bank'])) < 10,
              'turning': lambda tr: not tr['ground'] and abs(math.degrees(tr['bank'])) >= 30}
    res = {p: defaultdict(list) for p in phases}
    res['close'] = defaultdict(list)
    episodes = []
    reproduce = []
    own = own_tracks.get(r.pc)
    prev = None
    for s in S:
        p, prev = prev, s
        if math.isnan(s.predLocal) or s.paused or p is None or abs(s.predFrom - p.netTime) > 1e-6:
            continue
        i = bisect.bisect_left(T, s.predFrom)
        if i >= len(S) or abs(T[i] - s.predFrom) > 1e-6 or S[i].paused:
            continue
        base = S[i]
        c = extrapolate(base, s.predAge)
        reproduce.append(math.hypot(wrap(c[1] - s.predLon) * R * math.cos(s.predLat), (c[0] - s.predLat) * R) + abs(c[2] - s.predAlt))
        tm = interp_state(S, T, s.predFrom + s.predAge)
        t_now = now_sender(s.predLocal)
        tt = interp_state(S, T, t_now)
        if tm is None or tt is None:
            continue
        true_age = t_now - s.predFrom
        sep = None
        if own is not None and direction.calibrated:
            # the receiver's own aircraft at the same moment
            sys_o = direction.owner.clock.to_utc(t_now)
            mine = own.at(sys_o + direction.owner.pc.correction(sys_o))
            if mine is not None:
                sep = distance(tt, mine)
        groups = [g for name, sel in phases.items() if sel(tt) for g in [res[name]]]
        if sep is not None and sep < CLOSE and not tt['ground']:
            groups.append(res['close'])
        ma, mx, mu = split(s.predLat, s.predLon, s.predAlt, tm)
        ta, tx, tu = split(s.predLat, s.predLon, s.predAlt, tt)
        no = split(base.lat, base.lon, base.alt, tt)
        fixed = extrapolate(base, true_age, acc_factor=0.5, euler_rates=True)
        classic = extrapolate(base, true_age)
        drawn = None
        if not math.isnan(s.simTime):
            ts = interp_state(S, T, now_sender(s.simTime))
            if ts is not None:
                drawn = split(s.simLat, s.simLon, s.simAlt, ts)
        for g in groups:
            g['model_along'].append(ma); g['model_cross'].append(mx); g['model_vert'].append(mu)
            g['along'].append(ta); g['cross'].append(tx); g['vert'].append(tu)
            g['heading'].append(math.degrees(wrap(s.predHdg - tt['hdg'])))
            g['timing_ms'].append((s.predAge - true_age) * 1000)
            g['age_ms'].append(true_age * 1000)
            g['none_h'].append(math.hypot(no[0], no[1]))
            g['classic_h'].append(math.hypot(*split(classic[0], classic[1], classic[2], tt)[:2]))
            g['fixed_hdg'].append(math.degrees(wrap(fixed[3] - tt['hdg'])))
            if sep is not None:
                g['sep'].append(sep)
            if drawn is not None:
                g['drawn_along'].append(drawn[0]); g['drawn_cross'].append(drawn[1]); g['drawn_vert'].append(drawn[2])
        worst = max(abs(ta), abs(tu), abs(drawn[0]) if drawn else 0, abs(drawn[2]) if drawn else 0)
        if worst > EPISODE:
            episodes.append((s, tt, (ta, tx, tu), drawn, sep))
    out['res'] = res
    out['reproduce'] = pct(reproduce, .5), (max(reproduce) if reproduce else math.nan), len(reproduce)
    out['episodes'] = group_episodes(episodes)
    return out


def group_episodes(items):
    groups, current = [], []
    for it in items:
        if current and it[0].local - current[-1][0].local > 2.0:
            groups.append(current)
            current = []
        current.append(it)
    if current:
        groups.append(current)
    return groups


class Track:
    """An aircraft's own path on the shared time line, from a receiver's samples of it"""
    def __init__(self, direction):
        o = direction.owner
        pts = []
        for seg in direction.owner_segments:
            for s in seg:
                sys_o = o.clock.to_utc(s.netTime)
                pts.append((sys_o + o.pc.correction(sys_o), s))
        pts.sort(key=lambda x: x[0])
        self.t = [u for u, _ in pts]
        self.s = [s for _, s in pts]

    def at(self, utc):
        return interp_state(self.s, self.t, utc, gap=0.5)


# ----------------------------------------------------------------------------- report

def stamp_text(ts):
    return datetime.fromtimestamp(ts, timezone.utc).strftime('%H:%M:%S')


def dropout_classes(drops):
    """Gap lengths, with the 1/32 send rate (a peer whose simulator is seen as not connected,
    Sim.AircraftUpdate: intervalMask 0x1f = 32 x 50 ms) apart"""
    classes = [('0.15-0.5 s', 0.15, 0.5), ('0.5-1.5 s', 0.5, 1.5), ('1.5-1.75 s (1/32 rate)', 1.5, 1.75), ('> 1.75 s', 1.75, 1e9)]
    return [(name, sum(1 for g, _, _ in drops if lo < g <= hi), sum(g for g, _, _ in drops if lo < g <= hi)) for name, lo, hi in classes]


def report(pcs, results, notes, out):
    w = out.write
    w('# Estimation log analysis\n\n')
    w('## Inputs\n\n| PC | upload | package | sessions (UTC) | offset to time.windows.com (s) |\n|---|---|---|---|---|\n')
    for pc in pcs:
        ses = ', '.join(f"{stamp_text(s.start)}-{stamp_text(s.end)} port {s.port}" for s in pc.sessions)
        offs = ', '.join(f"{o:+.3f} @{stamp_text(u)}" for u, o in pc.checks) or '-'
        w(f"| {pc.computer} | {pc.name} | {pc.package.get('commit', '?')} | {ses} | {offs} |\n")

    w('\n## Directions (receiver PC watching an aircraft)\n\n')
    w('| receiver | aircraft | sender PC | samples | minutes | gap p50 ms | dropouts >150 ms | RTT p50 ms | one-way p5/p50/p95 ms | clock drift ms/h | time base |\n')
    w('|---|---|---|---|---|---|---|---|---|---|---|\n')
    for res in results:
        d = res['dir']
        ow = res['oneway']
        ows = f"{ow[0]*1000:.1f} / {ow[1]*1000:.1f} / {ow[2]*1000:.1f}" if ow else '-'
        w(f"| {d.receiver.pc.computer} | {d.callsign} | {d.owner.pc.computer if d.owner else '?'} | {res['samples']} | {res['minutes']:.0f} | "
          f"{res['gap_p50']*1000:.1f} | {len(res['dropouts'])} | {res['rtt']*1000:.1f} | {ows} | {fmt(res['drift'], 0)} | {res['timebase']} |\n")
    for d, back, a, b, rtt, shift, fastest in notes:
        w(f"\n- **{d.owner.pc.computer} <-> {d.receiver.pc.computer}:** by the clock checks alone, one way {a*1000:.1f} ms and back {b*1000:.1f} ms;"
          f" together {(a+b)*1000:.1f} ms against a measured round trip of {rtt*1000:.1f} ms"
          f" ({'consistent' if abs(a + b - rtt) < 0.01 else 'NOT consistent - check the logs'})."
          f" The clock checks misplace the two PCs by {shift[1]*1000:+.1f} ms (from {shift[0]*1000:+.1f} to {shift[2]*1000:+.1f} ms over the session),"
          f" so they are aligned by their traffic instead, every {CALIBRATION_WINDOW:.0f} s: fastest one-way delay {fastest*1000:.1f} ms each way."
          f" A difference between the two ways cannot be seen with clocks synchronised over the internet (Classic assumes the same: 0.52 x RTT).")
    w('\n')

    w('\n## Sender timestamps\n\nDistance flown between two samples / speed, minus the stamped time difference'
      ' (the change in stamping error; honest stamps give 0).\n\n| aircraft | p5 ms | p50 | p95 | share > 5 ms |\n|---|---|---|---|---|\n')
    for res in results:
        st_ = res['stamp']
        if st_:
            w(f"| {res['dir'].callsign} | {st_[0]:+.1f} | {st_[1]:+.1f} | {st_[2]:+.1f} | {st_[3]:.0f}% |\n")

    for res in results:
        d = res['dir']
        rp = res['reproduce']
        w(f"\n## {d.callsign} as seen by {d.receiver.pc.computer}\n\n")
        w(f"Offline Classic reproduces the logged predictions to p50 {rp[0]*1000:.2f} mm (max {rp[1]*1000:.2f} mm, {rp[2]} predictions).\n\n")
        w('| phase | n | along p50/p95/p99 m | cross p95 m | vert p95/p99 m | heading p95/p99 deg | > 3 m | drawn along p95 | drawn cross p95 | drawn vert p95/p99 | timing p95 ms | age p50 ms |\n')
        w('|---|---|---|---|---|---|---|---|---|---|---|---|\n')
        for phase in ('all', 'ground', 'straight', 'turning', 'close'):
            g = res['res'][phase]
            if not g['along']:
                continue
            a, x, v, h = summary(g['along']), summary(g['cross']), summary(g['vert']), summary(g['heading'])
            da, dx, dv = summary(g['drawn_along']), summary(g['drawn_cross']), summary(g['drawn_vert'])
            horiz = [math.hypot(p, q) for p, q in zip(g['along'], g['cross'])]
            label = {'close': f'close formation (< {CLOSE:.0f} m)', 'straight': 'air, |bank| < 10', 'turning': 'air, |bank| >= 30'}.get(phase, phase)
            w(f"| {label} | {a[4]} | {fmt(a[0])} / {fmt(a[1])} / {fmt(a[2])} | {fmt(x[1])} | {fmt(v[1])} / {fmt(v[2])} | {fmt(h[1])} / {fmt(h[2])} | "
              f"{fmt(over(horiz, 3.0))}% | {fmt(da[1]) if da else '-'} | {fmt(dx[1]) if dx else '-'} | "
              f"{(fmt(dv[1]) + ' / ' + fmt(dv[2])) if dv else '-'} | {fmt(summary(g['timing_ms'])[1], 1)} | {fmt(pct(g['age_ms'], .5), 0)} |\n")
        g = res['res']['all']
        w(f"\nWhat-ifs at the true age (all phases): horizontal p95 - no prediction {fmt(summary(g['none_h'])[1])} m, "
          f"Classic {fmt(summary(g['classic_h'])[1])} m. Heading p95/p99 - Classic {fmt(summary(g['heading'])[1])} / {fmt(summary(g['heading'])[2])} deg, "
          f"with Euler rates {fmt(summary(g['fixed_hdg'])[1])} / {fmt(summary(g['fixed_hdg'])[2])} deg.\n")
        gt = res['res']['turning']
        if gt['heading']:
            w(f"In turns: heading p95/p99 Classic {fmt(summary(gt['heading'])[1])} / {fmt(summary(gt['heading'])[2])} deg, "
              f"Euler rates {fmt(summary(gt['fixed_hdg'])[1])} / {fmt(summary(gt['fixed_hdg'])[2])} deg.\n")
        gc = res['res']['close']
        if gc['sep']:
            w(f"Close formation: {len(gc['sep'])} predictions, separation p50 {fmt(pct(gc['sep'], .5), 0)} m, min {fmt(min(gc['sep']), 1)} m.\n")

        # dropouts and what the other direction did meanwhile
        drops = sorted(res['dropouts'], key=lambda x: -x[0])
        if drops:
            w(f"\nDropouts (> {GAP*1000:.0f} ms): {len(drops)}, {sum(g for g, _, _ in drops):.0f} s in total; "
              f"{sum(1 for _, a, b in drops if a.paused or b.paused)} at a pause, {sum(1 for _, a, _ in drops if a.ground)} on the ground. By length: "
              + ', '.join(f"{name} {n} ({t:.0f} s)" for name, n, t in dropout_classes(drops) if n)
              + f". Between {stamp_text(d.receiver.clock.to_utc(min(a.receivedAt for _, a, _ in drops)))}"
              f" and {stamp_text(d.receiver.clock.to_utc(max(a.receivedAt for _, a, _ in drops)))} UTC. Longest:\n\n")
            for gap, a, b in drops[:5]:
                sys_r = d.receiver.clock.to_utc(a.receivedAt)
                w(f"- {stamp_text(sys_r)} UTC (receiver clock): {gap:.2f} s{' (paused)' if a.paused or b.paused else ''}{' on ground' if a.ground else ''}\n")
                for dtv, text in d.receiver.pc.events(sys_r + gap / 2, gap / 2 + 3)[:3]:
                    w(f"    - receiver {dtv:+.1f}s: {text[:140]}\n")
                if d.owner:
                    sys_o = d.owner.clock.to_utc(a.netTime)
                    for dtv, text in d.owner.pc.events(sys_o + gap / 2, gap / 2 + 3)[:3]:
                        w(f"    - sender {dtv:+.1f}s: {text[:140]}\n")

        eps = sorted(res['episodes'], key=lambda e: -max(max(abs(x[2][0]), abs(x[2][2]), abs(x[3][0]) if x[3] else 0, abs(x[3][2]) if x[3] else 0) for x in e))
        if eps:
            w(f"\nEpisodes with an error > {EPISODE:.0f} m: {len(eps)}. Largest:\n\n")
            for e in eps[:6]:
                worst = max(e, key=lambda x: max(abs(x[2][0]), abs(x[2][2]), abs(x[3][0]) if x[3] else 0, abs(x[3][2]) if x[3] else 0))
                s, tt, tot, drawn, sep = worst
                sys_r = d.receiver.clock.to_utc(s.local)
                wall = datetime.fromtimestamp(sys_r + d.receiver.tz, timezone.utc).strftime('%H:%M:%S')
                dr = f"drawn along {drawn[0]:+.1f} cross {drawn[1]:+.1f} vert {drawn[2]:+.1f} m" if drawn else 'drawn -'
                w(f"- {stamp_text(sys_r)} UTC ({wall} receiver local), {e[-1][0].local - e[0][0].local:.1f} s: predicted along {tot[0]:+.1f} vert {tot[2]:+.1f} m; {dr}; "
                  f"sender pitch {math.degrees(tt['pitch']):+.0f} bank {math.degrees(tt['bank']):+.0f}"
                  f"{'' if sep is None else f', separation {sep:.0f} m'}\n")
                if not math.isnan(s.simPitch):
                    w(f"    - drawn attitude pitch {math.degrees(s.simPitch):+.0f} bank {math.degrees(s.simBank):+.0f}\n")
                for dtv, text in d.receiver.pc.events(sys_r, 5.0)[:4]:
                    w(f"    - receiver {dtv:+.1f}s: {text[:140]}\n")
                if d.owner:
                    for dtv, text in d.owner.pc.events(d.owner.clock.to_utc(s.netTime), 5.0)[:4]:
                        w(f"    - sender {dtv:+.1f}s: {text[:140]}\n")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('logs', nargs='+', help='uploaded zips or extracted folders, one per PC')
    ap.add_argument('--out', help='write the report here (default: stdout)')
    ap.add_argument('--min-samples', type=int, default=1000, help='ignore aircraft with fewer samples')
    args = ap.parse_args()

    pcs = [PcLog(p) for p in args.logs]
    sessions = [s for pc in pcs for s in pc.sessions if s.clock]
    directions = []
    for ses in sessions:
        for callsign, rows in ses.objects.items():
            if len(rows) >= args.min_samples and any(not math.isnan(s.predLocal) for s in rows):
                d = Direction(ses, callsign)
                find_owner(d, sessions)
                directions.append(d)

    # one time line per pair of PCs, then each PC's own aircraft path, as another PC received it
    notes = calibrate(directions)
    own_tracks = {d.owner.pc: Track(d) for d in directions if d.calibrated}
    results = [analyse(d, own_tracks) for d in directions]

    out = open(args.out, 'w', encoding='utf-8') if args.out else sys.stdout
    report(pcs, results, notes, out)
    if args.out:
        out.close()
        print(f"report: {args.out}")


if __name__ == '__main__':
    main()
