"""Fixed standalone DAT and authorized postimages from Python struct/gzip."""
import base64
import gzip
import json
import struct


def text(value):
    encoded = value.encode("utf-8")  # These literals contain only scalar BMP text, without NUL.
    return struct.pack(">H", len(encoded)) + encoded


def document(literal="Hello %s", typed=False):
    raw = b"\x0a" + text("world")
    raw += b"\x08" + text("text") + text(literal)
    raw += (b"\x04" if typed else b"\x03") + text("number") + (struct.pack(">q", 9223372036854775807) if typed else struct.pack(">i", 42))
    raw += b"\x0b" + text("UUID") + struct.pack(">iiiii", 4, 1, 2, 3, 4)
    raw += b"\x05" + text("keep") + bytes.fromhex("80000000") + b"\0"
    return raw


def main():
    raw = document()
    encode = lambda value: base64.b64encode(value).decode("ascii")
    print(json.dumps(dict(oracle="CPython struct/gzip", sourceBase64=encode(gzip.compress(raw, mtime=0)), rawBase64=encode(raw),
                         literalRawBase64=encode(document("你好 %s")), typedRawBase64=encode(document(typed=True))), separators=(",", ":")))


if __name__ == "__main__":
    main()
