"""Decode the shipped RGBA PNGs independently of Unity test doubles (stdlib only)."""
import argparse
import binascii
import hashlib
import json
from pathlib import Path
import struct
import zlib

GUNS = {"assault-rifle", "carbine", "sniper", "marksman", "machinegun", "shotgun", "pistol", "smg"}
SYMBOLS = {"kill", "death", "assist", "headshot", "grenade", "melee", "bleeding", "fire", "barbed-wire"}
LICENSED = {
    "headshot": ("Skull crack", "Lorc", "lorc/skull-crack"),
    "melee": ("Bowie knife", "Skoll", "skoll/bowie-knife"),
    "grenade": ("Grenade", "Lorc", "lorc/grenade"),
    "barbed-wire": ("Barbed wire", "Lorc", "lorc/barbed-wire"),
}
EXPECTED = {**{name + ".png": (128, 64) for name in GUNS},
            **{name + ".png": (64, 64) for name in SYMBOLS}, "team-panel.png": (688, 107)}


def need(condition, reason):
    if not condition:
        raise ValueError(reason)


def decode_rgba(data):
    need(data[:8] == b"\x89PNG\r\n\x1a\n", "PNG signature")
    offset, chunks, compressed = 8, [], bytearray()
    width = height = 0
    while offset < len(data):
        need(offset + 12 <= len(data), "truncated PNG chunk")
        length = struct.unpack_from(">I", data, offset)[0]
        kind = data[offset + 4:offset + 8]
        end = offset + 12 + length
        need(end <= len(data), "truncated PNG body")
        body = data[offset + 8:offset + 8 + length]
        need(binascii.crc32(kind + body) & 0xffffffff == struct.unpack_from(">I", data, end - 4)[0], "PNG CRC")
        if kind == b"IHDR":
            need(not chunks and length == 13, "PNG header placement")
            width, height, depth, color, compression, filtering, interlace = struct.unpack(">IIBBBBB", body)
            need((depth, color, compression, filtering, interlace) == (8, 6, 0, 0, 0), "PNG must be noninterlaced RGBA8")
            need(0 < width <= 688 and 0 < height <= 107, "PNG dimensions bounded")
        elif kind == b"IDAT":
            compressed.extend(body)
        elif kind == b"IEND":
            need(length == 0 and end == len(data), "PNG exact IEND")
        chunks.append(kind)
        offset = end
    need(chunks and chunks[0] == b"IHDR" and chunks[-1] == b"IEND" and compressed, "complete PNG")
    stride = width * 4
    expected = height * (stride + 1)
    decompressor = zlib.decompressobj()
    raw = decompressor.decompress(compressed, expected + 1)
    need(len(raw) == expected and decompressor.eof and not decompressor.unused_data and not decompressor.unconsumed_tail, "bounded PNG decompression")
    pixels, previous = bytearray(), bytearray(stride)
    for row in range(height):
        start = row * (stride + 1)
        method = raw[start]
        need(method <= 4, "PNG filter")
        current = bytearray(raw[start + 1:start + 1 + stride])
        for i in range(stride):
            a, b, c = (current[i - 4] if i >= 4 else 0), previous[i], (previous[i - 4] if i >= 4 else 0)
            if method == 1:
                predictor = a
            elif method == 2:
                predictor = b
            elif method == 3:
                predictor = (a + b) // 2
            elif method == 4:
                p = a + b - c
                distances = (abs(p - a), abs(p - b), abs(p - c))
                predictor = (a, b, c)[distances.index(min(distances))]
            else:
                predictor = 0
            current[i] = (current[i] + predictor) & 255
        pixels.extend(current)
        previous = current
    return (width, height), pixels


def validate(repo, asset_dir):
    manifest = json.loads((asset_dir / "SOURCES.json").read_text(encoding="utf-8-sig"))
    need(manifest["schemaVersion"] == 2, "manifest schema 2")
    entries = manifest["assets"]
    need(len(entries) == 18 and {entry["file"] for entry in entries} == set(EXPECTED), "exact manifest asset set")
    need({path.name for path in asset_dir.glob("*.png")} == set(EXPECTED), "exact PNG file set")
    retired = json.loads((repo / "tools/ArenaUiAssetChecks/RetiredArtwork.json").read_text(encoding="utf-8-sig"))
    old_hashes = {entry["sha256"].lower() for entry in retired["assets"]}
    need(len(old_hashes) == 18, "eighteen original rejection hashes")
    notice = (asset_dir / "NOTICE.md").read_text(encoding="utf-8")
    license_text = (asset_dir / "MIT-LICENSE.txt").read_text(encoding="utf-8")
    need("MIT" in notice and "SOURCES.json" in notice, "readable attribution/provenance notice")
    for fragment in ("Permission is hereby granted, free of charge", "The above copyright notice and this permission notice", 'THE SOFTWARE IS PROVIDED "AS IS"', "Copyright"):
        need(fragment in license_text, "complete MIT license")
    cc_text = (asset_dir / "CC-BY-3.0.txt").read_text(encoding="utf-8")
    for fragment in ("Attribution 3.0 Unported", "3. License Grant", "4. Restrictions", "8. Miscellaneous"):
        need(fragment in cc_text, "complete CC BY 3.0 license")
    need("https://creativecommons.org/licenses/by/3.0/" in notice and "CC-BY-3.0.txt" in notice,
         "readable CC license notice")
    report = []
    for entry in entries:
        name = entry["file"]
        data = (asset_dir / name).read_bytes()
        digest = hashlib.sha256(data).hexdigest()
        need(33 <= len(data) < 256 * 1024, f"{name}: bounded byte size")
        need(digest not in old_hashes, f"{name}: retired artwork is forbidden")
        need(digest == entry["sha256"].lower(), f"{name}: manifest hash")
        size, pixels = decode_rgba(data)
        need(size == EXPECTED[name] == tuple(entry["pixelSize"]), f"{name}: fixed size")
        stem = Path(name).stem
        licensed = LICENSED.get(stem)
        if licensed:
            title, author, path = licensed
            source_url = f"https://game-icons.net/1x1/{path}.html"
            need(entry["license"] == "CC-BY-3.0" and entry["author"] == author,
                 f"{name}: exact licensed author and license")
            need(entry.get("title") == title and entry["sourceURL"] == source_url
                 and entry.get("downloadURL") == f"https://game-icons.net/icons/ffffff/transparent/1x1/{path}.svg"
                 and entry.get("licenseURL") == "https://creativecommons.org/licenses/by/3.0/",
                 f"{name}: exact licensed source identity")
            need(all(value in notice for value in (name, title, author, source_url)),
                 f"{name}: readable named attribution")
        else:
            need(entry["license"] == "MIT" and bool(entry["author"].strip()), f"{name}: author and MIT license")
            need(entry.get("sourceURL") == f"urn:arenamode:original-ui-artwork:20261016:{stem}", f"{name}: original source identity")
        source = (repo / entry["sourceFile"]).resolve()
        is_gun = Path(name).stem in GUNS
        source_kind = "generated-weapon-render" if is_gun else "licensed-white-symbol" if licensed else "original-white-symbol"
        need(entry.get("artworkKind") == source_kind, f"{name}: explicit artwork kind")
        expected_source = repo / "tools/ArenaUiArtwork" / ("renders" if is_gun else "sources") / (Path(name).stem + (".png" if is_gun else ".svg"))
        need(source == expected_source.resolve() and source.is_file(), f"{name}: present repository source")
        need(bool(entry["modifications"]), f"{name}: transformation recorded")
        need(hashlib.sha256(source.read_bytes()).hexdigest() == entry.get("sourceSha256", "").lower(), f"{name}: source hash")
        visible, transparent, fractional, shaded = 0, 0, 0, 0
        for index in range(0, len(pixels), 4):
            r, g, b, a = pixels[index:index + 4]
            if a:
                visible += 1
                if is_gun:
                    shaded += a >= 128 and min(r, g, b) < 240 and max(r, g, b) > 24
                else:
                    need(r == g == b == 255, f"{name}: visible pixels are tint-neutral white")
                fractional += a < 255
            else:
                transparent += 1
        need(visible > 20 and transparent > 20, f"{name}: visible art and transparent surroundings")
        need(fractional > 0, f"{name}: antialiasing or alpha gradient")
        if is_gun:
            need(shaded > 20, f"{name}: shaded weapon render rather than white silhouette")
            width, height = size
            border = [pixels[(y * width + x) * 4 + 3] for y in range(height) for x in range(width)
                      if x in (0, width - 1) or y in (0, height - 1)]
            need(not any(border), f"{name}: transparent border without clipping")
        report.append({"file": name, "size": list(size), "sha256": digest, "visiblePixels": visible, "transparentPixels": transparent, "fractionalAlphaPixels": fractional})
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--assets", type=Path)
    parser.add_argument("--report", type=Path)
    arguments = parser.parse_args()
    result = validate(arguments.repo.resolve(), arguments.assets or arguments.repo / "ArenaMode.Client/Assets/Ui")
    if arguments.report:
        arguments.report.parent.mkdir(parents=True, exist_ok=True)
        arguments.report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"REAL UI ARTWORK VERIFIED ({len(result)} decoded RGBA PNGs; no Unity doubles)")
