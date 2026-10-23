"""Read-only statistics for legacy .jfs recordings. Usage: python -I jfsstat.py file...  (opens files read-only; prints per-frame-type sizes)"""
import struct, sys, collections

class Fail(Exception):
    pass

class R:
    def __init__(s, d): s.d = d; s.p = 0
    def need(s, n):
        if s.p + n > len(s.d): raise Fail("eof")
    def u8(s): s.need(1); v = s.d[s.p]; s.p += 1; return v
    def i16(s): s.need(2); v = struct.unpack_from('<h', s.d, s.p)[0]; s.p += 2; return v
    def u16(s): s.need(2); v = struct.unpack_from('<H', s.d, s.p)[0]; s.p += 2; return v
    def i32(s): s.need(4); v = struct.unpack_from('<i', s.d, s.p)[0]; s.p += 4; return v
    def u32(s): s.need(4); v = struct.unpack_from('<I', s.d, s.p)[0]; s.p += 4; return v
    def f64(s): s.need(8); v = struct.unpack_from('<d', s.d, s.p)[0]; s.p += 8; return v
    def skip(s, n): s.need(n); s.p += n
    def string(s):
        n = 0; shift = 0
        while True:
            b = s.u8(); n |= (b & 0x7f) << shift
            if not b & 0x80: break
            shift += 7
            if shift > 35: raise Fail("bad len")
        if n > 10000: raise Fail("long string")
        s.need(n); v = s.d[s.p:s.p+n]; s.p += n
        return v.decode('utf-8', 'replace')

TYPES = ["ObjectPosition", "AircraftPosition", "PlaneState", "HelicopterState", "AircraftState",
         "PistonEngineState", "TurbineEngineState", "ObjectSmoke", "AircraftFuel", "AircraftPayload",
         "SimEvent", "IntegerVariables", "FloatVariables", "String8Variables"]

def parse(data, tail3, stats):
    r = R(data)
    version = r.i16()
    if version < 10022: raise Fail("old")
    objs = []
    def frames_and_tail(r, o):
        count = r.i32()
        if count < 0 or count > 50_000_000: raise Fail("count")
        prev = {}
        for _ in range(count):
            start = r.p
            t = r.u8(); tm = r.f64()
            if t >= len(TYPES): raise Fail("type %d" % t)
            name = TYPES[t]
            if name == "ObjectPosition":
                r.skip(72 + (5 if version >= 10023 else 0))
            elif name == "AircraftPosition":
                r.skip(82 + (5 if version >= 10023 else 0) + (4 if version >= 21008 else 0))
            elif name == "SimEvent":
                r.skip(8)
            elif name in ("IntegerVariables", "FloatVariables", "String8Variables"):
                n = r.u16(); cur = {}
                for _ in range(n):
                    k = r.u32()
                    if name == "String8Variables": v = r.string()
                    else: v = r.skip(4) or data[r.p-4:r.p]
                    cur[k] = v
                key = (id(o), name)
                pv = prev.get(key)
                changed = n if pv is None else sum(1 for k, v in cur.items() if pv.get(k) != v)
                stats['var_entries'][name] += n
                stats['var_changed'][name] += changed
                prev[key] = cur
            else:
                raise Fail("reserved " + name)
            stats['frames'][name] += 1
            stats['bytes'][name] += r.p - start
            o['t'] = max(o.get('t', 0), tm)
        if version >= 21004:
            if tail3:
                r.string(); r.string(); r.string()
            else:
                r.string(); r.string()
    na = r.i32()
    if na < 0 or na > 1000: raise Fail("na")
    for _ in range(na):
        o = {'kind': 'aircraft'}
        r.u8(); o['callsign'] = r.string(); r.string(); o['model'] = r.string(); r.u8()
        frames_and_tail(r, o); objs.append(o)
    if r.p < len(data):
        no = r.i32()
        if no < 0 or no > 100000: raise Fail("no")
        for _ in range(no):
            o = {'kind': 'object'}
            o['model'] = r.string(); r.u8()
            frames_and_tail(r, o); objs.append(o)
    if r.p != len(data): raise Fail("trailing %d" % (len(data) - r.p))
    return version, objs

def main(paths):
    for path in paths:
        with open(path, 'rb') as f: data = f.read()
        res = None
        for tail3 in (True, False):
            stats = {'frames': collections.Counter(), 'bytes': collections.Counter(),
                     'var_entries': collections.Counter(), 'var_changed': collections.Counter()}
            try:
                res = (tail3, stats) + parse(data, tail3, stats)
                break
            except Fail as e:
                err = str(e)
            except Exception as e:
                err = repr(e)
        print("=== %s (%.1f MB)" % (path.split('\\')[-1], len(data) / 1e6))
        if not res:
            print("  FAILED to parse both layouts:", err); continue
        tail3, stats, version, objs = res
        dur = max((o.get('t', 0) for o in objs), default=0)
        print("  version %d, tail strings: %d, objects: %d (aircraft %d), duration %.0f s" %
              (version, 3 if tail3 else 2, len(objs), sum(o['kind'] == 'aircraft' for o in objs), dur))
        tot = sum(stats['bytes'].values())
        for name, b in stats['bytes'].most_common():
            line = "  %-17s frames %9d  bytes %11d (%4.1f%%)  avg %.0f B" % (name, stats['frames'][name], b, 100 * b / tot, b / stats['frames'][name])
            if name.endswith("Variables"):
                e = stats['var_entries'][name]; c = stats['var_changed'][name]
                line += "  entries/frame %.1f, changed vs previous frame %.1f%%" % (e / stats['frames'][name], 100 * c / max(e, 1))
            print(line)
        print("  file bytes not in frames: %d" % (len(data) - tot))

main(sys.argv[1:])
