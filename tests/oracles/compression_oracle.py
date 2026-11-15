"""Project literals framed/validated by CPython zlib, never product assemblies."""
import base64
import gzip
import hashlib
import json
import struct
import sys
import zlib
from pathlib import Path


def encode(value):
    return base64.b64encode(value).decode("ascii")


def decode_frames(data, codec):
    if codec == 3:
        return data, 1
    output = b""
    members = 0
    while data:
        decoder = zlib.decompressobj(31 if codec == 1 else 15)
        output += decoder.decompress(data)
        if not decoder.eof:
            raise ValueError("CorruptCompression")
        members += 1
        data = decoder.unused_data
        if data and (codec == 2 or not data.startswith(b"\x1f\x8b")):
            raise ValueError("TrailingData")
    if not members:
        raise ValueError("CorruptCompression")
    return output, members


def main():
    root = Path(__file__).resolve().parents[2]
    raw = base64.b64decode(json.loads((root / "fixtures/codec/java-oracle.json").read_text())["nbt"]["base64"])
    zl = zlib.compress(raw)
    gz = gzip.compress(raw, mtime=0)
    compressor = zlib.compressobj(wbits=-15)
    deflated = compressor.compress(raw) + compressor.flush()
    header = b"\x1f\x8b\x08\x1e\0\0\0\0\0\xff" + struct.pack("<H", 3) + b"abc" + b"name\0comment\0"
    optional = header + struct.pack("<H", zlib.crc32(header) & 0xffff) + deflated + struct.pack("<II", zlib.crc32(raw), len(raw))
    corrupt_header = bytearray(optional)
    corrupt_header[len(header)] ^= 1
    corrupt_crc = bytearray(gz)
    corrupt_crc[-8] ^= 1
    corrupt_size = bytearray(gz)
    corrupt_size[-4] ^= 1
    bad_tree = base64.b64decode("H4sIAAAAAAAAClVPwU1EMBAcLraTHLyuDN405EvMYcnxWvEaKXziUiiDD1JKoYGrwcicgmC0n9kZzeweoWYi3vIdxHlhkw+Q8YVmzmjQWM9rKUVAOPKXjBsk5LMjzet2elJQI6WzM7vYQtacCEDkgrWDijxbf8Hpzcy05a/3fK3zef3IPYSzkRsATSml1IBa2EOaKfAi6uKIbqApUPJjB8FLMFBDikxTB/mqXTJog14c6bEGAY8H9GMKzg6aDdp/rMM9hPV8uw/A39oHyPrnr1ZR9d2z+374N5eIoFM6AQAA")
    invalid_window = bytes([0x88, next(flag for flag in range(256) if ((0x88 << 8) | flag) % 31 == 0)]) + zl[2:]
    # Fixed Huffman length=3/distance=1 before any literal: no history exists.
    impossible_distance = bytes.fromhex("030200")
    distance_gzip = gz[:10] + impossible_distance + struct.pack("<II", zlib.crc32(bytes(3)), 3)
    distance_zlib = zl[:2] + impossible_distance + struct.pack(">I", zlib.adler32(bytes(3)))
    historical = next(case for case in json.loads((root / "fixtures/cases/format-regressions.json").read_text())["cases"] if case["id"] == "NBT-ZLIB-01")
    cases = [("raw", 3, raw), ("zlib", 2, zl), ("gzip", 1, gz), ("gzip-options", 1, optional),
             ("gzip-members", 1, gzip.compress(raw[:100], mtime=0) + gzip.compress(raw[100:], mtime=0)),
             ("f01", 2, base64.b64decode(historical["inputBase64"])),
             ("zlib-truncated", 2, zl[:-1]), ("zlib-checksum", 2, zl[:-1] + bytes([zl[-1] ^ 1])), ("zlib-trailing", 2, zl + b"x"),
             ("gzip-truncated", 1, gz[:-1]), ("gzip-crc", 1, bytes(corrupt_crc)), ("gzip-size", 1, bytes(corrupt_size)),
             ("gzip-header-crc", 1, bytes(corrupt_header)), ("gzip-trailing", 1, gz + b"x"), ("gzip-empty", 1, b""),
             ("gzip-invalid-huffman", 1, bad_tree), ("zlib-invalid-window", 2, invalid_window),
             ("gzip-reserved-flags", 1, gz[:3] + bytes([0xe0]) + gz[4:]),
             ("gzip-distance-before-history", 1, distance_gzip), ("zlib-distance-before-history", 2, distance_zlib),
             ("raw-empty", 3, b""), ("gzip-empty-member", 1, gzip.compress(b"", mtime=0)),
             ("zlib-empty-member", 2, zlib.compress(b"")),
             ("gzip-empty-mixed-members", 1, gzip.compress(b"", mtime=0) + gz + gzip.compress(b"", mtime=0))]
    rows = []
    for name, codec, source in cases:
        row = {"id": name, "codec": codec, "sourceBase64": encode(source)}
        try:
            decoded, members = decode_frames(source, codec)
            assert decoded == (b"" if name in ("raw-empty", "gzip-empty-member", "zlib-empty-member") else raw)
            row.update(accepted=True, decodedSha256=hashlib.sha256(decoded).hexdigest(), members=members)
            if decoded != raw:
                row.update(decodedBase64=encode(decoded))
        except (zlib.error, ValueError) as error:
            row.update(accepted=False, error=str(error) if isinstance(error, ValueError) else "CorruptCompression")
        rows.append(row)
    print(json.dumps({"oracle": "CPython zlib/gzip", "python": sys.version.split()[0], "zlib": zlib.ZLIB_RUNTIME_VERSION,
                      "rawBase64": encode(raw), "cases": rows}, separators=(",", ":")))


if __name__ == "__main__":
    main()
