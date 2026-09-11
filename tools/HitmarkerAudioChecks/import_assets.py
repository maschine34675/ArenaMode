"""Explicit asset intake, not a build step. Requires Python 3 and FFmpeg.

Downloads creator archives, preserves their licenses byte-for-byte, decodes only
three selected effects and produces bounded PCM WAVs plus provenance hashes.
"""
import array
import hashlib
import io
import json
from pathlib import Path
import subprocess
import sys
import urllib.request
import wave
import zipfile

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / 'ArenaMode.Client/Assets/ArenaMode/hitmarkers'
PACKS = {
    'interface': ('https://kenney.nl/assets/interface-sounds',
        'https://kenney.nl/media/pages/assets/interface-sounds/fa43c1dd4d-1677589452/kenney_interface-sounds.zip'),
    'impact': ('https://kenney.nl/assets/impact-sounds',
        'https://kenney.nl/media/pages/assets/impact-sounds/87b4ddecda-1677589768/kenney_impact-sounds.zip'),
}
SELECTION = {
    'hit': ('interface', 'Audio/click_003.ogg'),
    'headshot': ('impact', 'Audio/impactMetal_light_000.ogg'),
    'kill': ('impact', 'Audio/impactGeneric_light_003.ogg'),
}


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def main():
    DEST.mkdir(parents=True, exist_ok=True)
    manifest = {'author': 'Kenney', 'license': 'CC0-1.0', 'checked': '2026-09-11',
        'licenseUrl': 'https://creativecommons.org/publicdomain/zero/1.0/',
        'encoder': subprocess.check_output(['ffmpeg', '-version'], text=True).splitlines()[0],
        'conversion': 'Decode to 44100 Hz mono float PCM; trim leading/trailing samples below 0.001 '
            'with 2 ms padding; limit to 300 ms with 5 ms end fade; peak-normalize to 0.5 '
            '(-6.02 dBFS); round to signed PCM16 WAV. No pitch changes, splicing or added effects.',
        'packs': {}, 'files': []}
    archives = {}
    for name, (page, url) in PACKS.items():
        raw = urllib.request.urlopen(url, timeout=45).read()
        archive = zipfile.ZipFile(io.BytesIO(raw))
        license_names = [n for n in archive.namelist() if n.lower() == 'license.txt']
        if len(license_names) != 1:
            raise ValueError(f'Ambiguous license: {archive.namelist()}')
        license_bytes = archive.read(license_names[0])
        if b'CC0' not in license_bytes or b'Kenney' not in license_bytes:
            raise ValueError('Expected Kenney CC0 license missing')
        license_file = f'KENNEY-{name.upper()}-LICENSE.txt'
        (DEST / license_file).write_bytes(license_bytes)
        manifest['packs'][name] = {'source': page, 'archive': url, 'archiveSha256': sha(raw),
            'licenseFile': license_file, 'licenseSha256': sha(license_bytes)}
        archives[name] = archive

    for cue, (pack, original) in SELECTION.items():
        encoded = archives[pack].read(original)
        decoded = subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-i', 'pipe:0',
            '-f', 'f32le', '-ac', '1', '-ar', '44100', 'pipe:1'], input=encoded,
            stdout=subprocess.PIPE, check=True).stdout
        samples = array.array('f', decoded)
        if sys.byteorder != 'little':
            samples.byteswap()
        active = [i for i, value in enumerate(samples) if abs(value) >= 0.001]
        if not active:
            raise ValueError('Source has no signal')
        start, end = max(0, active[0] - 88), min(len(samples), active[-1] + 89)
        selected = samples[start:end][:13230]
        if not selected:
            raise ValueError('Empty output')
        fade = min(221, len(selected))
        for i in range(fade):
            selected[-fade + i] *= (fade - 1 - i) / max(1, fade - 1)
        peak = max(abs(v) for v in selected)
        output = array.array('h', (round(v * 0.5 / peak * 32767) for v in selected))
        if sys.byteorder != 'little':
            output.byteswap()
        path = DEST / f'{cue}.wav'
        with wave.open(str(path), 'wb') as wav:
            wav.setnchannels(1)
            wav.setsampwidth(2)
            wav.setframerate(44100)
            wav.writeframes(output.tobytes())
        manifest['files'].append({'cue': cue, 'pack': pack, 'original': original,
            'sourceSha256': sha(encoded), 'file': path.name, 'sha256': sha(path.read_bytes()),
            'sourceDecodedFrames': len(samples), 'trimStartFrame': start,
            'frames': len(selected), 'sampleRate': 44100, 'channels': 1,
            'durationMs': round(len(selected) / 44.1, 3)})
    (DEST / 'SOURCES.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest['files'], indent=2))


if __name__ == '__main__':
    main()
