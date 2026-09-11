"""Explicit local asset intake, never a build step. Requires Python 3 + FFmpeg.

Pass the user-downloaded Freesound 570335 WAV. The pinned original hash prevents
accidentally assigning this author's license to an unrelated replacement.
"""
import argparse
import hashlib
import io
import json
from pathlib import Path
import subprocess
import wave

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / 'ArenaMode.Client/Assets/ArenaMode/hitmarkers'
SOURCE_SHA = 'B85C21E42BA30FF161BC4E3F877DAF77057B7B64A33272459E853557B9EC305C'


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    args = parser.parse_args()
    original = args.source.read_bytes()
    if sha(original) != SOURCE_SHA:
        raise ValueError('Not the reviewed Freesound 570335 original WAV')
    license_bytes = (DEST / 'CC0-1.0.txt').read_bytes()
    if b'CC0 1.0 Universal' not in license_bytes:
        raise ValueError('Bundled CC0 legal text missing')
    # Explicit channel average keeps source gain; no normalization, trimming,
    # fades, pitch changes or cue-specific processing. Match the existing PCM
    # asset format and retain the full 187.5 ms effect (rounded to whole frames).
    pcm = subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error',
        '-i', 'pipe:0', '-af', 'pan=mono|c0=0.5*c0+0.5*c1',
        '-ar', '44100', '-f', 's16le', 'pipe:1'], input=original,
        stdout=subprocess.PIPE, check=True).stdout
    if not pcm or len(pcm) % 2 or len(pcm) > 88200:
        raise ValueError('Expected a non-empty PCM16 effect no longer than one second')
    output = io.BytesIO()
    with wave.open(output, 'wb') as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(44100)
        wav.writeframes(pcm)
    encoded = output.getvalue()
    manifest = {
        'author': 'User391915396', 'title': 'Hitmarker Sound Effect',
        'source': 'https://freesound.org/people/User391915396/sounds/570335/',
        'original': args.source.name, 'sourceSha256': sha(original),
        'sourceFormat': {'sampleRate': 48000, 'channels': 2, 'bitsPerSample': 24,
            'frames': 9000, 'durationMs': 187.5},
        'license': 'CC0-1.0', 'checked': '2026-09-11',
        'licenseUrl': 'https://creativecommons.org/publicdomain/zero/1.0/',
        'licenseTextUrl': 'https://creativecommons.org/publicdomain/zero/1.0/legalcode.txt',
        'licenseFile': 'CC0-1.0.txt', 'licenseSha256': sha(license_bytes),
        'encoder': subprocess.check_output(['ffmpeg', '-version'], text=True).splitlines()[0],
        'conversion': 'Average stereo channels (0.5 L + 0.5 R); resample 48000 to 44100 Hz; '
            'encode signed PCM16 WAV. No trimming, normalization, fades, pitch changes '
            'or added effects. Identical bytes for hit, headshot and kill.',
        'files': []}
    for cue in ('hit', 'headshot', 'kill'):
        path = DEST / f'{cue}.wav'
        path.write_bytes(encoded)
        manifest['files'].append({'cue': cue, 'file': path.name, 'sha256': sha(encoded),
            'frames': len(pcm) // 2, 'sampleRate': 44100, 'channels': 1,
            'durationMs': round(len(pcm) / 2 / 44.1, 3)})
    (DEST / 'SOURCES.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(json.dumps(manifest['files'], indent=2))


if __name__ == '__main__':
    main()
