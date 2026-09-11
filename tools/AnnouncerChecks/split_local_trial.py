"""Split the user-supplied ansagen2.wav for a local, non-commercial ElevenLabs trial.

Usage: python tools/AnnouncerChecks/split_local_trial.py <ansagen2.wav>
Requires FFmpeg. Writes only to ignored artifacts/local-announcer-trial/ansagen2.
Never changes distributable assets or installs files. See LOCAL-ANNOUNCER-TRIAL.md.
"""
import argparse
import hashlib
import io
import json
import math
from pathlib import Path
import struct
import subprocess
import wave

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / 'artifacts/local-announcer-trial/ansagen2'
SOURCE_SHA = 'B41440907BA52AFAAE4FA3D5434182FDCB8C9D1226987922A93629327225849A'
# Labels follow the order supplied by the user. Cuts lie in the measured pauses
# (silencedetect -26 dB / 100 ms), with headroom before each word and trailing
# ambience retained. ULTRA uses the existing four-kill MULTI event, not a new rule.
CUTS = [
    ('DOUBLE KILL', 'double_kill', 0.0, 1.46),
    ('ULTRA KILL', 'multi_kill', 1.57, 3.15),
    ('MONSTER KILL', 'monster_kill', 3.31, 5.06),
    ('UNSTOPPABLE', 'unstoppable', 5.20, 6.92),
    ('GODLIKE', 'godlike', 7.02, 8.49542),
]


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    args = parser.parse_args()
    original = args.source.read_bytes()
    # Fixed cuts are valid only for this reviewed recording.
    if sha(original) != SOURCE_SHA:
        raise ValueError('Different recording: review the cut boundaries before importing')
    with wave.open(io.BytesIO(original)) as wav:
        rate, channels, width, frames = wav.getframerate(), wav.getnchannels(), wav.getsampwidth(), wav.getnframes()
        if (rate, channels, width) != (44100, 2, 2):
            raise ValueError('Expected the reviewed 44.1 kHz stereo PCM16 WAV')
        samples = wav.readframes(frames)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    manifest = {
        'title': 'ArenaMode local announcer trial - elevenlabs.io',
        'source': args.source.name, 'sourceSha256': sha(original),
        'generator': 'ElevenLabs free plan (user supplied)',
        'scope': 'Local non-commercial audition only; not release assets; no open license asserted',
        'licenseChecked': '2026-09-11',
        'licenseSource': 'https://help.elevenlabs.io/hc/en-us/articles/13313564601361-Can-I-publish-the-content-I-generate-on-the-platform',
        'encoder': subprocess.check_output(['ffmpeg', '-version'], text=True).splitlines()[0],
        'processing': 'Split in pauses; 5 ms fade-in and 10 ms fade-out at cut edges; '
            'Vorbis quality 5; original stereo/sample rate/gain retained; no normalization or pitch changes.',
        'files': []}
    for label, key, start, end in CUTS:
        first, last = round(start * rate), min(frames, round(end * rate))
        selected = samples[first * channels * width:last * channels * width]
        duration = (last - first) / rate
        if duration <= 0:
            raise ValueError('Empty segment')
        path = OUTPUT / (key + '.ogg')
        subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-y',
            '-f', 's16le', '-ar', str(rate), '-ac', str(channels), '-i', 'pipe:0',
            '-af', f'afade=t=in:d=0.005,afade=t=out:st={duration - 0.010:.9f}:d=0.010',
            '-c:a', 'libvorbis', '-q:a', '5',
            '-metadata', f'title={label} - elevenlabs.io (local trial)',
            '-metadata', 'comment=User-generated ElevenLabs free-plan audio; non-commercial local trial only.',
            str(path)], input=selected, check=True)
        decoded = subprocess.check_output(['ffmpeg', '-hide_banner', '-loglevel', 'error',
            '-i', str(path), '-f', 'f32le', 'pipe:1'])
        values = [value[0] for value in struct.iter_unpack('<f', decoded)]
        if not values or not all(math.isfinite(value) for value in values):
            raise ValueError(f'{key}: empty or non-finite decoded signal')
        peak = max(abs(v) for v in values)
        if not (0.0001 < peak < 1.0):
            raise ValueError(f'{key}: silent or clipping output ({peak})')
        if abs(len(values) / channels / rate - duration) > 0.03:
            raise ValueError(f'{key}: unexpected decoded duration')
        row = {'spoken': label, 'eventKey': key, 'file': path.name,
            'startFrame': first, 'endFrameExclusive': last, 'sampleRate': rate,
            'channels': channels, 'durationSeconds': duration,
            'decodedPeak': peak, 'sha256': sha(path.read_bytes())}
        manifest['files'].append(row)
        print(f'{label} -> {path.name}: {duration:.3f} s, decoded peak {peak:.4f}')
    (OUTPUT / 'SOURCES.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(f'LOCAL_ANNOUNCER_TRIAL_READY: {OUTPUT}')


if __name__ == '__main__':
    main()
