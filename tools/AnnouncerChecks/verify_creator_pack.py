"""Decode and verify the packaged Creator announcer assets; optional deployment comparison.

Requires Python 3 and FFmpeg/ffprobe. Does not install files or change game state.
"""
import argparse
import hashlib
import importlib.util
import json
import math
import re
from pathlib import Path
import struct
import subprocess
import sys
from optional_runtime_keys import OPTIONAL, runtime_key

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / 'ArenaMode.Client/Assets/ArenaMode'
ACTIVE = set('double_kill triple_kill multi_kill monster_kill killing_spree rampage '
    'dominating unstoppable godlike first_blood headshot payback last_man_standing '
    'round_start round_won round_lost match_won match_lost'.split())

# Use the supplement's reviewed cuts, exact 4+2 destinations and decode/hash
# contract, rather than accepting arbitrary files beyond the original pack.
_addition_spec = importlib.util.spec_from_file_location('announcer_additions_contract',
    ROOT / 'tools/AnnouncerAssetAdditions/verify_additions.py')
ADDITIONS = importlib.util.module_from_spec(_addition_spec)
_previous_dont_write_bytecode = sys.dont_write_bytecode
try:
    sys.dont_write_bytecode = True
    _addition_spec.loader.exec_module(ADDITIONS)
finally:
    sys.dont_write_bytecode = _previous_dont_write_bytecode


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def verify_metadata(manifest):
    rows = manifest['files']
    cuts = json.loads(Path(__file__).with_name('creator_cuts.json').read_text(encoding='utf-8'))
    require(len(rows) == len(cuts) == 46, 'Expected all 46 phrases')
    require(manifest['sourceSha256'] == '79AFBFE5ABC5BF45DC3B95FC7379C49512805846D0D252B6111551244A1625D9',
        'Original source recording changed')
    require([[r['spoken'], r['file'], r['startFrame'], r['endFrameExclusive']] for r in rows] == cuts,
        'Reviewed phrase order, destinations or cuts changed')
    require({r.get('runtimeEventKey') for r in rows if r.get('runtimeEventKey')} == OPTIONAL
        and sum(bool(r.get('runtimeEventKey')) for r in rows) == 19, 'Enabled optional cue set differs')
    for row in rows:
        require(row.get('runtimeEventKey') == runtime_key(row['file']), 'Optional runtime mapping differs')
        # usage/eventKey describe the original intake, not today's directory.
        active = row['number'] <= 18
        require(row['usage'] == ('active' if active else 'reserve'), 'Original intake classification changed')
        require(row['eventKey'] == (Path(row['file']).stem if active else None), 'Original event mapping changed')


def verify_runtime_catalog():
    catalog = (ROOT / 'ArenaMode.Client/Audio/AnnouncerCueCatalog.cs').read_text(encoding='utf-8')
    match = re.search(r'LegacyReserveKeys\s*=\s*\{([^}]+)\}', catalog)
    require(match is not None, 'Explicit optional runtime catalog missing')
    keys = re.findall(r'"([a-z_]+)"', match.group(1))
    require(len(keys) == 19 and set(keys) == OPTIONAL, 'Production optional cue set differs')


def verify_inventory(assets, manifest, supplement):
    verify_metadata(manifest)
    ADDITIONS.verify_metadata(supplement)
    # This pins the reviewed original manifest AND all 46 original file hashes;
    # rewriting a file and its manifest checksum cannot silently redefine them.
    ADDITIONS.verify_originals(assets)
    rows = manifest['files']
    paths = {r['file'] for r in rows}
    extra_paths = {r['file'] for r in supplement['files']}
    require(len(paths) == 46, 'Duplicate original destination')
    require(len(extra_paths) == 6 and paths.isdisjoint(extra_paths), 'Addition destination collision')
    require(sum(r['usage'] == 'active' for r in rows) == 18, 'Original active count differs')
    require(sum(r['usage'] == 'reserve' for r in rows) == 28, 'Original reserve count differs')
    require({p.stem for p in (assets / 'announcements').glob('*.ogg')} == ACTIVE | OPTIONAL | ADDITIONS.ACTIVE,
        'Active cue set changed')
    expected = paths | extra_paths
    actual = {p.relative_to(assets).as_posix() for p in (assets / 'announcements').rglob('*.ogg')}
    require(expected == actual, 'Missing/unrecorded OGGs')
    for row in supplement['files']:
        require(sha(assets / row['file']) == row['sha256'], 'Addition clip changed: ' + row['file'])
    return expected


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--assets', type=Path, default=ASSETS)
    parser.add_argument('--deployment', type=Path, action='append', default=[])
    args = parser.parse_args()
    manifest = json.loads((args.assets / ADDITIONS.MANIFEST_DIR / 'ANNOUNCER-SOURCES.json').read_text(encoding='utf-8'))
    supplement = json.loads((args.assets / ADDITIONS.MANIFEST_DIR / ADDITIONS.MANIFEST).read_text(encoding='utf-8'))
    paths = verify_inventory(args.assets, manifest, supplement)
    verify_runtime_catalog()
    rows = manifest['files']
    previous = 0
    peaks, durations = [], []
    for number, row in enumerate(rows, 1):
        require(row['number'] == number, 'Order changed')
        start, end = row['startFrame'], row['endFrameExclusive']
        require(start == previous and end > start, 'Gap/overlap/empty cut')
        previous = end
        path = args.assets / row['file']
        require(path.resolve().is_relative_to(args.assets.resolve()), 'Unsafe path')
        require(sha(path) == row['sha256'], f'{path}: hash mismatch')
        active = row['usage'] == 'active'
        require((path.parent == args.assets / 'announcements') == (active or bool(row.get('runtimeEventKey'))),
            f'{path}: incorrect current package location')
        require(row['eventKey'] == (path.stem if active else None), 'Event mapping differs')
        probe = json.loads(subprocess.check_output(['ffprobe', '-v', 'error', '-show_streams', '-of', 'json', str(path)]))
        require(len(probe['streams']) == 1, 'Expected one stream')
        stream = probe['streams'][0]
        require((stream['codec_name'], int(stream['sample_rate']), stream['channels']) == ('vorbis', 44100, 2), 'Unsupported audio format')
        tags = {k.lower(): v for k, v in stream.get('tags', {}).items()}
        require(tags.get('title') == row['spoken'] and 'Creator' in tags.get('comment', ''), 'Missing/wrong metadata')
        raw = subprocess.check_output(['ffmpeg', '-v', 'error', '-i', str(path), '-f', 'f32le', 'pipe:1'])
        values = [v[0] for v in struct.iter_unpack('<f', raw)]
        require(values and all(math.isfinite(v) for v in values), f'{path}: invalid PCM')
        peak = max(abs(v) for v in values)
        rms = math.sqrt(sum(v*v for v in values) / len(values))
        require(0.05 < peak < 1 and rms > 0.001, f'{path}: silent/clipping signal ({peak}, {rms})')
        duration = len(values) / 2 / 44100
        require(abs(duration - (end-start)/44100) < 0.03, f'{path}: wrong decoded length')
        require(abs(row['durationSeconds'] - (end-start)/44100) < 1e-8, 'Manifest duration differs')
        peaks.append(peak)
        durations.append(duration)
    require(previous == manifest['sourceFrames'] == 2758982, 'Source coverage differs')
    for row in supplement['files']:
        duration, peak = ADDITIONS.verify_clip(args.assets, row)
        durations.append(duration)
        peaks.append(peak)
    print(f'CREATOR_PACK_VERIFIED: {len(paths)} clips; 46 originals unchanged '
        f'(intake 18 active, 28 reserve), supplement 4 active, 2 reserve; '
        f'41 runtime cues, 11 unused; duration {min(durations):.3f}-{max(durations):.3f}s; '
        f'max decoded peak {max(peaks):.5f}')
    for installed in args.deployment:
        # Packaging filters source-only reserve audio and rewrites document links.
        # Compare shipped recordings and their per-file provenance, not source-tree
        # bytes or an impossible full match against the 52-file intake inventory.
        expected = {row['file']: row for row in rows + supplement['files']
                    if not row['file'].startswith('announcements/reserve/')}
        deployed_ogg = {p.relative_to(installed).as_posix()
                        for p in (installed / 'announcements').rglob('*.ogg')}
        require(deployed_ogg == set(expected), f'{installed}: stale/missing announcer clips')
        for relative, row in expected.items():
            require(sha(installed / relative) == row['sha256'], f'Deployment audio mismatch: {relative}')
        recorded = set()
        for name in ('ANNOUNCER-SOURCES.json', ADDITIONS.MANIFEST):
            manifest_path = installed / ADDITIONS.MANIFEST_DIR / name
            record = json.loads(manifest_path.read_text(encoding='utf-8'))
            source_record = manifest if name == 'ANNOUNCER-SOURCES.json' else supplement
            require(record['sourceSha256'] == source_record['sourceSha256'], 'Packaged source identity differs')
            for row in record['files']:
                clip = (manifest_path.parent / row['file']).resolve()
                require(clip.is_relative_to(installed.resolve()), 'Unsafe packaged audio reference')
                relative = clip.relative_to(installed.resolve()).as_posix()
                require(relative in expected and relative not in recorded, 'Unknown/duplicate packaged audio record')
                require({k: v for k, v in row.items() if k != 'file'} ==
                        {k: v for k, v in expected[relative].items() if k != 'file'},
                        'Packaged clip provenance changed: ' + relative)
                recorded.add(relative)
        require(recorded == set(expected), 'Incomplete packaged audio provenance')
        print(f'CREATOR_DEPLOYMENT_VERIFIED: {len(recorded)} byte-identical active clips and matching provenance in {installed}')


if __name__ == '__main__':
    main()
