"""Reference writer for the example file in docs/jfs2/format-spec.md Appendix A.

Writes example.jfs2 (221 bytes) next to this script and prints each block as hex.
Usage: python -I jfs2ref.py
"""
import os, struct, zlib


def varint(n):
    out = bytearray()
    while True:
        b = n & 0x7f
        n >>= 7
        if n:
            out.append(b | 0x80)
        else:
            out.append(b)
            return bytes(out)


def zig(n):
    return varint((n << 1) ^ (n >> 63))


def s(x):
    b = x.encode()
    return varint(len(b)) + b


def block(t, body):
    head = bytes([t]) + struct.pack('<I', len(body))
    return head + body + struct.pack('<I', zlib.crc32(head + body))


def record(kind, track, delta, body):
    return varint(kind) + varint(track) + zig(delta) + varint(len(body)) + body


MAGIC = bytes([0xFA, 0, 0x4A, 0x46, 0x53, 0x32, 0x0D, 0x0A])
END = b"JFS2END\n"
assert zlib.crc32(b"123456789") == 0xCBF43926  # CRC-32 check value (spec 1.3)

hdr = block(1, bytes([1, 0]) + struct.pack('<II', 0, 0) + struct.pack('<q', 1790000000000000)
            + varint(1) + s("app") + s("JoinFS"))
sch1 = block(2, varint(1) + s("track-open") + bytes([1]))
sch2 = block(2, varint(2) + s("track-close") + bytes([1]))
r1 = record(1, 1, 0, bytes([0]) + s("A") + s("") + s(""))
r2 = record(2, 1, 1000000, b"")
payload = r1 + r2
chunk_body = (struct.pack('<qq', 0, 1000000) + bytes([1, 0])
              + struct.pack('<III', 0, 2, len(payload)) + payload)
off_chunk = len(MAGIC) + len(hdr) + len(sch1) + len(sch2)
chunk = block(3, chunk_body)
idx_body = (varint(1) + struct.pack('<Qqq', off_chunk, 0, 1000000) + bytes([1])
            + varint(1) + varint(1) + struct.pack('<qq', 0, 1000000) + s("") + s(""))
off_index = off_chunk + len(chunk)
idx = block(4, idx_body)
footer = struct.pack('<Q', off_index)
footer += struct.pack('<I', zlib.crc32(footer)) + END
data = MAGIC + hdr + sch1 + sch2 + chunk + idx + footer

for label, b in (("header", hdr), ("schema 1", sch1), ("schema 2", sch2),
                 ("chunk", chunk), ("index", idx), ("footer", footer)):
    print(label, len(b), "bytes")
    print(" ".join("%02X" % x for x in b))
print("offsets: chunk", off_chunk, "index", off_index, "total", len(data))

with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "example.jfs2"), "wb") as f:
    f.write(data)
