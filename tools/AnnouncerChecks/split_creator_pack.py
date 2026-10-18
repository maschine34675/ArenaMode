"""Reproduce the reviewed 46-line Creator pack from alle_ansagen.ogg.

Python 3 + FFmpeg/ffprobe only. Outputs to ignored artifacts/creator-announcer/pack;
never installs or overwrites tracked assets. Review before copying the results.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
from optional_runtime_keys import runtime_key

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / 'artifacts/creator-announcer/pack'
SOURCE_SHA = '79AFBFE5ABC5BF45DC3B95FC7379C49512805846D0D252B6111551244A1625D9'
RATE, CHANNELS, FRAMES = 44100, 2, 2758982
CUTS = json.loads(Path(__file__).with_name('creator_cuts.json').read_text(encoding='utf-8'))


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    args = parser.parse_args()
    if sha(args.source.read_bytes()) != SOURCE_SHA:
        raise ValueError('Different recording: these fixed cuts must not be used')
    probe = json.loads(subprocess.check_output(['ffprobe', '-v', 'error',
        '-show_streams', '-of', 'json', str(args.source)]))
    if len(probe['streams']) != 1:
        raise ValueError('Expected one audio stream')
    stream = probe['streams'][0]
    if (stream['codec_name'], int(stream['sample_rate']), stream['channels']) != ('vorbis', RATE, CHANNELS):
        raise ValueError('Expected 44.1 kHz stereo Vorbis')
    raw = subprocess.check_output(['ffmpeg', '-v', 'error', '-i', str(args.source),
        '-f', 'f32le', 'pipe:1'])
    if len(raw) != FRAMES * CHANNELS * 4:
        raise ValueError('Unexpected decoded frame count')
    if len(CUTS) != 46 or CUTS[0][2] != 0 or CUTS[-1][3] != FRAMES:
        raise ValueError('Incomplete cut plan')
    previous = 0
    for label, relative, start, end in CUTS:
        if start != previous or end <= start or end > FRAMES:
            raise ValueError('Cuts must cover the input exactly once, in order')
        if not relative.startswith('announcements/') or '..' in Path(relative).parts:
            raise ValueError('Unsafe output path')
        previous = end
    manifest = {
        'title': 'ArenaMode Creator announcer pack',
        'source': args.source.name, 'sourceSha256': SOURCE_SHA,
        'sourceCodec': 'Ogg Vorbis', 'sampleRate': RATE, 'channels': CHANNELS,
        'sourceFrames': FRAMES, 'sourceDurationSeconds': FRAMES / RATE,
        'generator': 'ElevenLabs Creator (generation under Creator stated by the user on 2026-09-12)',
        'voiceAndModel': 'Not supplied; no claim about a specific voice or model',
        'rights': 'User-supplied paid-plan output, intended for distribution with ArenaMode. '
            'Commercial use is subject to ElevenLabs terms and necessary input/voice rights; '
            'Beta Services excluded. Not a Creative Commons or public-domain dedication. '
            'No retroactive relicensing of the former free-plan recordings.',
        'licenseChecked': '2026-09-12',
        'licenseSource': 'https://help.elevenlabs.io/hc/en-us/articles/13313564601361-Can-I-publish-the-content-I-generate-on-the-platform',
        'termsSource': 'https://elevenlabs.io/terms-of-use',
        'termsSourceEeaUkSwiss': 'https://elevenlabs.io/terms-of-use-eu',
        'processing': 'One floating-point decode; cuts at reviewed inter-phrase pause midpoints; '
            '5 ms fade-in and 10 ms fade-out per clip; uniform -1 dB gain for codec headroom; '
            'Vorbis q7, original 44.1 kHz stereo retained. No pitch, speed, EQ, denoising or per-clip normalization.',
        'boundaryReview': 'User-provided 46-line order cross-checked against local faster-whisper base.en '
            'transcription and waveform/silencedetect (-23 dB, 70 ms). ASR timestamps were not used '
            'as cut points; ROUND WON was recognized phonetically as round one. Not an in-game listening test.',
        'encoder': subprocess.check_output(['ffmpeg', '-version'], text=True).splitlines()[0],
        'filePathBase': 'ArenaMode asset root (three directories above this source manifest); packaged manifests use document-relative paths.',
        'files': []}
    for number, (label, relative, start, end) in enumerate(CUTS, 1):
        out = OUTPUT / relative
        out.parent.mkdir(parents=True, exist_ok=True)
        duration = (end - start) / RATE
        selected = raw[start * CHANNELS * 4:end * CHANNELS * 4]
        subprocess.run(['ffmpeg', '-v', 'error', '-y', '-f', 'f32le', '-ar', str(RATE),
            '-ac', str(CHANNELS), '-i', 'pipe:0', '-af',
            f'volume=-1dB,afade=t=in:d=0.005,afade=t=out:st={duration-0.010:.9f}:d=0.010',
            '-c:a', 'libvorbis', '-q:a', '7',
            '-metadata', f'title={label}', '-metadata', 'artist=ArenaMode / ElevenLabs',
            '-metadata', 'comment=User-generated with ElevenLabs Creator; see ANNOUNCER-SOURCES.json and AUDIO-LICENSES.md.',
            str(out)], input=selected, check=True)
        manifest['files'].append({'number': number, 'spoken': label, 'file': relative,
            'usage': 'active' if number <= 18 else 'reserve',
            'eventKey': out.stem if number <= 18 else None,
            'startFrame': start, 'endFrameExclusive': end,
            'durationSeconds': duration, 'sha256': sha(out.read_bytes())})
        key = runtime_key(relative)
        if key is not None:
            manifest['files'][-1]['runtimeEventKey'] = key
        print(f'{number:02d} {label} -> {relative}: {duration:.3f} s')
    (OUTPUT / 'docs/licenses/audio').mkdir(parents=True, exist_ok=True)
    (OUTPUT / 'docs/licenses/audio' / 'ANNOUNCER-SOURCES.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8', newline='\n')
    print('CREATOR_PACK_SPLIT: review files before copying to the repository')


if __name__ == '__main__':
    main()
