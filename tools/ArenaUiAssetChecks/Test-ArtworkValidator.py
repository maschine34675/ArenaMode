"""Negative controls for the real decoder/manifest audit; only writes ignored copies."""
import argparse
import binascii
import hashlib
import json
from pathlib import Path
import runpy
import shutil
import struct
import zlib

parser = argparse.ArgumentParser()
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()
repo = Path(__file__).resolve().parents[2]
output = args.output.resolve()
if not output.is_relative_to((repo / "artifacts").resolve()):
    raise ValueError("Negative controls must stay under ignored repository artifacts")
audit = runpy.run_path(str(Path(__file__).with_name("Verify-Artwork.py")))
validate, decode = audit["validate"], audit["decode_rgba"]


def chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", binascii.crc32(kind + data) & 0xffffffff)


def encode(size, pixels):
    width, height = size
    rows = b"".join(b"\0" + pixels[y * width * 4:(y + 1) * width * 4] for y in range(height))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b""))


def expect_failure(action, reason):
    try:
        action()
    except ValueError as error:
        if reason not in str(error):
            raise AssertionError(f"Wrong rejection: {error}; expected {reason}") from error
    else:
        raise AssertionError("Invalid input accepted: " + reason)
    print("Negative artwork control rejected: " + reason)


source = repo / "ArenaMode.Client/Assets/Ui"
validate(repo, source)
size, pixels = decode((source / "kill.png").read_bytes())
bad_crc = bytearray(encode(size, pixels))
bad_crc[-1] ^= 1
expect_failure(lambda: decode(bad_crc), "PNG CRC")
expect_failure(lambda: decode(encode(size, pixels) + b"extra"), "PNG exact IEND")

for name, reason in (("color", "tint-neutral white"), ("opaque", "transparent surroundings"),
                     ("hash", "manifest hash"), ("source", "present repository source"),
                     ("license", "complete MIT license"), ("duplicate", "exact manifest asset set")):
    folder = output / name
    shutil.copytree(source, folder)
    manifest_file = folder / "SOURCES.json"
    manifest = json.loads(manifest_file.read_text(encoding="utf-8-sig"))
    if name in ("color", "opaque"):
        changed = bytearray(pixels)
        if name == "color":
            index = next(i for i in range(0, len(changed), 4) if changed[i + 3])
            changed[index] = 0
        else:
            changed = bytearray(b"\xff\xff\xff\xff" * (size[0] * size[1]))
        png = encode(size, changed)
        (folder / "kill.png").write_bytes(png)
        next(entry for entry in manifest["assets"] if entry["file"] == "kill.png")["sha256"] = hashlib.sha256(png).hexdigest()
    elif name == "hash":
        manifest["assets"][0]["sha256"] = "0" * 64
    elif name == "source":
        manifest["assets"][0]["sourceFile"] = "AGENTS.md"
    elif name == "license":
        (folder / "MIT-LICENSE.txt").write_text("MIT", encoding="utf-8")
    elif name == "duplicate":
        manifest["assets"][1] = manifest["assets"][0]
    manifest_file.write_text(json.dumps(manifest), encoding="utf-8")
    expect_failure(lambda: validate(repo, folder), reason)
print("ARTWORK VALIDATOR CONTROLS PASSED (8 invalid inputs)")
