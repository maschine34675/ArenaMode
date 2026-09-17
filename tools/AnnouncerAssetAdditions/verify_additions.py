"""Check six supplemental clips, exact original-pack preservation and rejecting controls.

Python 3 + FFmpeg/ffprobe. Read-only except temporary negative-control fixtures
under ignored artifacts. Does not install files or invoke a build.
"""
import argparse
import copy
import hashlib
import json
import math
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / 'ArenaMode.Client/Assets/ArenaMode'
MANIFEST = 'ANNOUNCER-ADDITIONS-20260917.json'
ORIGINAL_MANIFEST_SHA = '957600A17E1F461C0BA168A85B2E05E92CCC4F733AAFC12E5E1A24ACA896A03B'
SOURCE_SHA = '4CB17E7BAC2B3B81EA55828F2E0DB9C4337591F8670B7AAAA0BBBB7104DA1414'
RATE, CHANNELS, FRAMES = 44100, 2, 357446
ACTIVE = {'no_scope', 'airborne', 'no_contest', 'embarrassing'}
RESERVE = {'trickshot', 'air_superiority'}
CUTS = json.loads(Path(__file__).with_name('cuts.json').read_text(encoding='utf-8'))


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def verify_metadata(manifest):
    rows = manifest['files']
    require(manifest['sourceSha256'] == SOURCE_SHA, 'Source hash changed')
    require((manifest['sampleRate'], manifest['channels'], manifest['sourceFrames']) == (RATE, CHANNELS, FRAMES),
        'Source format changed')
    require(abs(manifest['sourceDurationSeconds'] - FRAMES/RATE) < 1e-8, 'Source duration changed')
    require(len(rows) == len(CUTS) == 6, 'Expected six clips')
    require([[r['spoken'], r['file'], r['startFrame'], r['endFrameExclusive']] for r in rows] == CUTS,
        'Reviewed order, destinations or cuts changed')
    require({r['eventKey'] for r in rows if r['usage'] == 'active'} == ACTIVE, 'Active cue mapping changed')
    require({Path(r['file']).stem for r in rows if r['usage'] == 'reserve'} == RESERVE, 'Reserve cues changed')
    previous, paths = 0, set()
    for number, row in enumerate(rows, 1):
        require(row['number'] == number and row['startFrame'] == previous
            and row['endFrameExclusive'] > previous, 'Order, gap, overlap or empty cut')
        previous = row['endFrameExclusive']
        path = Path(row['file'])
        require(row['file'] not in paths and path.suffix == '.ogg', 'Repeated/non-OGG destination')
        paths.add(row['file'])
        active = path.stem in ACTIVE
        require(path.parent.as_posix() == ('sounds' if active else 'sounds/reserve'), 'Wrong asset directory')
        require(row['usage'] == ('active' if active else 'reserve'), 'Usage changed')
        require(row['eventKey'] == (path.stem if active else None), 'Event mapping changed')
        require(abs(row['durationSeconds'] - (row['endFrameExclusive']-row['startFrame'])/RATE) < 1e-8,
            'Clip duration metadata changed')
    require(previous == FRAMES and CUTS[0][2] == 0, 'Incomplete source coverage')


def verify_originals(assets):
    original = assets / 'ANNOUNCER-SOURCES.json'
    require(sha(original) == ORIGINAL_MANIFEST_SHA, 'Original manifest changed')
    rows = json.loads(original.read_text(encoding='utf-8'))['files']
    require(len(rows) == 46, 'Original phrase count changed')
    for row in rows:
        require(sha(assets / row['file']) == row['sha256'], 'Original clip changed: ' + row['file'])
    return len(rows)


def verify_clip(assets, row):
    path = assets / row['file']
    require(path.resolve().is_relative_to(assets.resolve()), 'Unsafe path')
    require(sha(path) == row['sha256'], 'Output hash mismatch: ' + row['file'])
    probe = json.loads(subprocess.check_output(['ffprobe', '-v', 'error', '-show_streams', '-of', 'json', str(path)]))
    require(len(probe['streams']) == 1, 'Expected one audio stream')
    stream = probe['streams'][0]
    require((stream['codec_name'], int(stream['sample_rate']), stream['channels']) == ('vorbis', RATE, CHANNELS),
        'Incorrect output format')
    tags = {k.lower():v for k,v in stream.get('tags',{}).items()}
    require(tags.get('title') == row['spoken'] and MANIFEST in tags.get('comment',''), 'Missing/wrong source metadata')
    raw = subprocess.check_output(['ffmpeg', '-v', 'error', '-i', str(path), '-f', 'f32le', 'pipe:1'])
    values = [x[0] for x in struct.iter_unpack('<f', raw)]
    require(values and len(values) % CHANNELS == 0 and all(math.isfinite(x) for x in values), 'Invalid decoded PCM')
    peak = max(abs(x) for x in values)
    rms = math.sqrt(sum(x*x for x in values) / len(values))
    require(.05 < peak < 1 and rms > .001, 'Silent/clipping audio')
    duration = len(values) / CHANNELS / RATE
    require(abs(duration-row['durationSeconds']) < .03, 'Decoded duration changed')
    return duration, peak


def negative_controls(manifest, assets, original_assets):
    # All checks first pass on the positive control before individual faults.
    rejected = 0
    for field, value, expected in [('eventKey','trickshot','mapping'),
                                  ('startFrame',1,'cuts'), ('spoken','wrong line','order')]:
        mutated = copy.deepcopy(manifest)
        mutated['files'][1][field] = value
        try:
            verify_metadata(mutated)
        except ValueError as error:
            require(expected in str(error), 'Unexpected metadata-control failure')
            rejected += 1
        else:
            raise ValueError('Metadata fault was accepted')
    evidence = ROOT / 'artifacts/announcer-additions-20260917'
    evidence.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='negative-', dir=evidence) as scratch:
        scratch = Path(scratch)
        row = manifest['files'][0]
        bad = scratch / row['file']
        bad.parent.mkdir(parents=True, exist_ok=True)
        bad.write_bytes((assets/row['file']).read_bytes() + b'corruption')
        try:
            verify_clip(scratch, row)
        except ValueError as error:
            require('Output hash mismatch' in str(error), 'Unexpected output-control failure')
            rejected += 1
        else:
            raise ValueError('Output corruption was accepted')
        original_manifest = original_assets/'ANNOUNCER-SOURCES.json'
        shutil.copy2(original_manifest, scratch/'ANNOUNCER-SOURCES.json')
        originals = json.loads(original_manifest.read_text(encoding='utf-8'))['files']
        for original in originals:
            dst = scratch/original['file']
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(original_assets/original['file'], dst)
        verify_originals(scratch)
        (scratch/originals[0]['file']).write_bytes(b'corrupted original')
        try:
            verify_originals(scratch)
        except ValueError as error:
            require('Original clip changed:' in str(error), 'Unexpected original-control failure')
            rejected += 1
        else:
            raise ValueError('Original audio corruption was accepted')
    require(rejected == 5, 'Missing negative controls')
    print(f'ANNOUNCER_ADDITIONS_FAULTS_REJECTED: {rejected}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--assets', type=Path, default=ASSETS)
    parser.add_argument('--original-assets', type=Path, default=ASSETS)
    parser.add_argument('--negative-controls', action='store_true')
    args = parser.parse_args()
    manifest = json.loads((args.assets/MANIFEST).read_text(encoding='utf-8'))
    verify_metadata(manifest)
    originals = verify_originals(args.original_assets)
    measured = [verify_clip(args.assets,row) for row in manifest['files']]
    if args.negative_controls:
        negative_controls(manifest, args.assets, args.original_assets)
    print(f'ANNOUNCER_ADDITIONS_VERIFIED: {len(measured)} clips; 4 active, 2 reserve; '
        f'{originals} original clips and original manifest unchanged; '
        f'duration {min(d for d,p in measured):.3f}-{max(d for d,p in measured):.3f}s; '
        f'max decoded peak {max(p for d,p in measured):.5f}')


if __name__ == '__main__':
    main()
