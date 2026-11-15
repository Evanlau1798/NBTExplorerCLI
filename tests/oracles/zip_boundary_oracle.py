"""Independent ZIP records and raw Deflate completion witnesses using Python."""
import base64
import io
import json
import struct
import zipfile
import zlib


def archive(kind):
    name, raw = b"data/a", b"abc"
    payload = {"deflate": bytes.fromhex("4b4c4a0600"), "truncated": bytes.fromhex("4b4c4a06"),
               "trailing": bytes.fromhex("4b4c4a06007f7f")}.get(kind, raw)
    method = 8 if kind in ("deflate", "truncated", "trailing") else 0
    local64 = kind in ("zip64-entry", "zip64-local")
    central64 = kind == "zip64-entry"
    extra = struct.pack("<HHQQ", 1, 16, len(raw), len(payload))
    local_extra = extra if local64 else b""
    central_extra = extra if central64 or kind == "zip64-extra" else b""
    local = struct.pack("<IHHHHHIIIHH", 0x04034b50, 45 if local64 else 20, 0, method, 0, 0, zlib.crc32(raw),
                        0xffffffff if local64 else len(payload), 0xffffffff if local64 else len(raw), len(name), len(local_extra)) + name + local_extra + payload
    central = struct.pack("<IHHHHHHIIIHHHHHII", 0x02014b50, 45, 45 if central64 else 20, 0, method, 0, 0, zlib.crc32(raw),
                          0xffffffff if central64 else len(payload), 0xffffffff if central64 else len(raw), len(name), len(central_extra), 0, 0, 0, 0, 0) + name + central_extra
    end = struct.pack("<IHHHHIIH", 0x06054b50, 0, 0, 1, 1, len(central), len(local), 0)
    if kind == "zip64-eocd":
        record = struct.pack("<IQHHIIQQQQ", 0x06064b50, 44, 45, 45, 0, 0, 1, 1, len(central), len(local))
        locator = struct.pack("<IIQI", 0x07064b50, 0, len(local) + len(central), 1)
        end = record + locator + end
    return local + central + end, payload, method


def main():
    rows = []
    for kind in ("stored", "deflate", "truncated", "trailing", "zip64-entry", "zip64-local", "zip64-extra", "zip64-eocd"):
        data, payload, method = archive(kind)
        with zipfile.ZipFile(io.BytesIO(data)) as candidate:
            assert candidate.read("data/a") == b"abc"
        row = dict(id=kind, sourceBase64=base64.b64encode(data).decode("ascii"), accepted=kind in ("stored", "deflate"))
        if method == 8:
            decoder = zlib.decompressobj(-15)
            assert decoder.decompress(payload) == b"abc"
            row.update(deflateFinished=decoder.eof, trailingBytes=len(decoder.unused_data))
            assert (decoder.eof and not decoder.unused_data) == row["accepted"]
        if not row["accepted"]:
            row["error"] = "UnsupportedProfile" if kind.startswith("zip64") else "InvalidData"
        rows.append(row)
    print(json.dumps(dict(oracle="CPython struct/zipfile/zlib; profile intentionally excludes ZIP64", cases=rows), separators=(",", ":")))


if __name__ == "__main__":
    main()
