"""Offline validation of packaged assets, conversion records and licenses."""
import array
import hashlib
import json
from pathlib import Path
import sys
import wave

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / 'ArenaMode.Client/Assets/ArenaMode/docs/licenses/audio'


def verify():
    manifest = json.loads((DEST / 'HITMARKER-SOURCES.json').read_text(encoding='utf-8'))
    assert manifest['author'] == 'User391915396' and manifest['license'] == 'CC0-1.0'
    assert manifest['source'] == 'https://freesound.org/people/User391915396/sounds/570335/'
    assert manifest['sourceSha256'] == 'B85C21E42BA30FF161BC4E3F877DAF77057B7B64A33272459E853557B9EC305C'
    assert {row['cue'] for row in manifest['files']} == {'hit', 'headshot', 'kill'}
    assert len(manifest['files']) == 3
    raw = (DEST / manifest['licenseFile']).read_bytes()
    assert hashlib.sha256(raw).hexdigest().upper() == manifest['licenseSha256']
    assert b'CC0 1.0 Universal' in raw
    assert len({row['sha256'] for row in manifest['files']}) == 1, 'All three cues must use the same sound'
    for row in manifest['files']:
        path = DEST / row['file']
        assert hashlib.sha256(path.read_bytes()).hexdigest().upper() == row['sha256']
        with wave.open(str(path)) as wav:
            assert wav.getnchannels() == row['channels'] == 1
            assert wav.getframerate() == row['sampleRate'] == 44100
            assert wav.getnframes() == row['frames']
            assert wav.getsampwidth() == 2 and wav.getcomptype() == 'NONE'
            values = array.array('h', wav.readframes(wav.getnframes()))
        if sys.byteorder != 'little': values.byteswap()
        assert abs(len(values) / 44.1 - manifest['sourceFormat']['durationMs']) < 0.1
        peak = max(abs(v) for v in values) / 32768
        assert 0.00001 < peak < 1.0
        assert len(set(values)) > 10
        print(f"{row['file']}: {len(values)/44.1:.3f} ms, peak {peak:.5f}, SHA256 matched")
    print('HITMARKER_ASSETS_VERIFIED')


if __name__ == '__main__': verify()
