"""Independent struct/zlib Region fixture and bounded candidate inspector."""
import base64
import gzip
import hashlib
import json
import struct
import sys
import zlib
from pathlib import Path
sys.dont_write_bytecode = True
from compression_oracle import decode_frames


def inspect(data):
    if len(data) > 67108864 or len(data) < 8192 or len(data) % 4096:
        raise ValueError("region size")
    rows = []
    ranges = []
    for index in range(1024):
        location, = struct.unpack_from(">I", data, 4 * index)
        sector, count = location >> 8, location & 255
        timestamp, = struct.unpack_from(">I", data, 4096 + 4 * index)
        if not sector and not count:
            continue
        if sector < 2 or not count or (sector + count) * 4096 > len(data):
            raise ValueError("sector allocation")
        length, = struct.unpack_from(">I", data, sector * 4096)
        if not 1 <= length <= count * 4096 - 4:
            raise ValueError("chunk length")
        flag = data[sector * 4096 + 4]
        codec, external = flag & 127, bool(flag & 128)
        if external and length != 1:
            raise ValueError("external marker")
        payload = data[sector * 4096 + 5:sector * 4096 + 4 + length]
        row = dict(index=index, sector=sector, count=count, timestamp=timestamp,
                   codec=codec, external=external, payloadSha256=hashlib.sha256(payload).hexdigest())
        if not external and codec in (1, 2, 3):
            raw, members = decode_frames(payload, codec)
            row.update(decodedSha256=hashlib.sha256(raw).hexdigest(), members=members)
        rows.append(row)
        ranges.append((sector, sector + count))
    ranges.sort()
    if any(first[1] > second[0] for first, second in zip(ranges, ranges[1:])):
        raise ValueError("overlapping sectors")
    return rows


def main():
    if len(sys.argv) == 2:
        print(json.dumps(inspect(Path(sys.argv[1]).read_bytes()), separators=(",", ":")))
        return
    root = Path(__file__).resolve().parents[2]
    raw = base64.b64decode(json.loads((root / "fixtures/codec/java-oracle.json").read_text())["nbt"]["base64"])
    data = bytearray(5 * 4096)
    for index, sector, codec, timestamp, payload in [(0, 2, 2, 0xffffffff, zlib.compress(raw)),
                                                    (31, 3, 1, 1234, gzip.compress(raw, mtime=0)),
                                                    (1023, 4, 99, 0, b"opaque\0payload")]:
        struct.pack_into(">I", data, index * 4, (sector << 8) | 1)
        struct.pack_into(">I", data, 4096 + index * 4, timestamp)
        struct.pack_into(">I", data, sector * 4096, len(payload) + 1)
        data[sector * 4096 + 4] = codec
        data[sector * 4096 + 5:sector * 4096 + 5 + len(payload)] = payload
        data[(sector + 1) * 4096 - 1] = 0x55
    print(json.dumps(dict(oracle="CPython struct/zlib/gzip", sourceBase64=base64.b64encode(data).decode("ascii"),
                         chunks=inspect(data)), separators=(",", ":")))


if __name__ == "__main__":
    main()
