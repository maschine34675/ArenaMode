"""Split the reviewed six-line alle_ansagen2.ogg supplement, using Python + FFmpeg.

Writes only to ignored artifacts/announcer-additions-20260917/pack. Never installs,
overwrites tracked assets, or re-encodes the original 46 recordings.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / 'artifacts/announcer-additions-20260917/pack'
MANIFEST = 'ANNOUNCER-ADDITIONS-20260917.json'
SOURCE_SHA = '4CB17E7BAC2B3B81EA55828F2E0DB9C4337591F8670B7AAAA0BBBB7104DA1414'
RATE, CHANNELS, FRAMES = 44100, 2, 357446
CUTS = json.loads(Path(__file__).with_name('cuts.json').read_text(encoding='utf-8'))
ACTIVE = {'no_scope', 'airborne', 'no_contest', 'embarrassing'}


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    args = parser.parse_args()
    if sha(args.source.read_bytes()) != SOURCE_SHA:
        raise ValueError('Different recording: fixed reviewed cuts must not be reused')
    probe = json.loads(subprocess.check_output(['ffprobe', '-v', 'error',
        '-show_streams', '-of', 'json', str(args.source)]))
    if len(probe['streams']) != 1:
        raise ValueError('Expected one audio stream')
    stream = probe['streams'][0]
    if (stream['codec_name'], int(stream['sample_rate']), stream['channels']) != ('vorbis', RATE, CHANNELS):
        raise ValueError('Expected 44.1 kHz stereo Vorbis')
    raw = subprocess.check_output(['ffmpeg', '-v', 'error', '-i', str(args.source), '-f', 'f32le', 'pipe:1'])
    if len(raw) != FRAMES * CHANNELS * 4:
        raise ValueError('Unexpected decoded frame count')
    if len(CUTS) != 6 or CUTS[0][2] != 0 or CUTS[-1][3] != FRAMES:
        raise ValueError('Incomplete cut plan')
    previous, paths = 0, set()
    for label, relative, start, end in CUTS:
        if start != previous or end <= start or end > FRAMES:
            raise ValueError('Cuts must cover the source exactly once and in order')
        path = OUTPUT / relative
        if not path.resolve().is_relative_to(OUTPUT.resolve()) or not relative.startswith('sounds/') or relative in paths:
            raise ValueError('Unsafe or repeated output path')
        paths.add(relative)
        previous = end
    manifest = {
        'title': 'ArenaMode announcer additions - 2026-09-17',
        'source': args.source.name, 'sourceSha256': SOURCE_SHA,
        'sourceCodec': 'Ogg Vorbis', 'sampleRate': RATE, 'channels': CHANNELS,
        'sourceFrames': FRAMES, 'sourceDurationSeconds': FRAMES / RATE,
        'generator': 'ElevenLabs; supplied as a continuation of the user-described Creator announcer pack',
        'provenance': 'The user explicitly described the original alle_ansagen.ogg pack as generated with Creator. '
            'This supplementary recording was supplied in the same workflow; no separate generation-plan '
            'statement or voice/model details were provided for this file. No independent account verification.',
        'rights': 'Intended for use with ArenaMode. Paid-plan commercial rights apply only if generated during '
            'a qualifying paid subscription, subject to applicable ElevenLabs terms and necessary input/voice '
            'rights; Beta Services excluded. No Creative Commons/public-domain or unrestricted standalone '
            'asset-reuse license asserted. This import does not publish the recordings.',
        'licenseChecked': '2026-09-17',
        'licenseSource': 'https://help.elevenlabs.io/hc/en-us/articles/13313564601361-Can-I-publish-the-content-I-generate-on-the-platform',
        'termsSource': 'https://elevenlabs.io/terms-of-use',
        'termsSourceEeaUkSwiss': 'https://elevenlabs.io/terms-of-use-eu',
        'processing': 'One floating-point decode; contiguous cuts at reviewed inter-phrase pause midpoints; '
            '5 ms fade-in and 10 ms fade-out per clip; uniform -1 dB gain matching the original pack headroom; '
            'Vorbis q7, original 44.1 kHz stereo. No pitch, speed, EQ, denoising or individual normalization.',
        'boundaryReview': 'User-provided six-line order confirmed by local offline faster-whisper base.en '
            'and waveform inspection. Cuts use -26 dB / 40 ms inter-phrase silence midpoints, not ASR '
            'word timestamps. The complete source is covered without gaps or overlaps. No GPU listening test.',
        'originalPackManifest': 'ANNOUNCER-SOURCES.json (46 recordings remain byte-identical)',
        'encoder': subprocess.check_output(['ffmpeg', '-version'], text=True).splitlines()[0],
        'files': []}
    for number, (label, relative, start, end) in enumerate(CUTS, 1):
        out = OUTPUT / relative
        out.parent.mkdir(parents=True, exist_ok=True)
        duration = (end-start) / RATE
        selected = raw[start * CHANNELS * 4:end * CHANNELS * 4]
        subprocess.run(['ffmpeg', '-v', 'error', '-y', '-f', 'f32le', '-ar', str(RATE),
            '-ac', str(CHANNELS), '-i', 'pipe:0', '-af',
            f'volume=-1dB,afade=t=in:d=0.005,afade=t=out:st={duration-0.010:.9f}:d=0.010',
            '-c:a', 'libvorbis', '-q:a', '7', '-metadata', f'title={label}',
            '-metadata', 'artist=ArenaMode / ElevenLabs', '-metadata',
            f'comment=User-supplied continuation of the Creator announcer pack; see {MANIFEST} and AUDIO-LICENSES.md.',
            str(out)], input=selected, check=True)
        active = out.stem in ACTIVE
        manifest['files'].append({'number': number, 'spoken': label, 'file': relative,
            'usage': 'active' if active else 'reserve', 'eventKey': out.stem if active else None,
            'startFrame': start, 'endFrameExclusive': end, 'durationSeconds': duration,
            'sha256': sha(out.read_bytes())})
        print(f'{number} {label} -> {relative}: {duration:.6f}s')
    (OUTPUT / MANIFEST).write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8', newline='\n')
    print('ANNOUNCER_ADDITIONS_SPLIT: review before copying to tracked assets')


if __name__ == '__main__':
    main()
