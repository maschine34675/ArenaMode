"""Offline validation of packaged assets, conversion records and licenses."""
import array
import hashlib
import json
from pathlib import Path
import sys
import wave

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / 'ArenaMode.Client/Assets/ArenaMode/hitmarkers'


def verify():
    manifest = json.loads((DEST / 'SOURCES.json').read_text(encoding='utf-8'))
    assert manifest['author'] == 'Kenney' and manifest['license'] == 'CC0-1.0'
    assert {row['cue'] for row in manifest['files']} == {'hit', 'headshot', 'kill'}
    assert len(manifest['files']) == 3
    for pack in manifest['packs'].values():
        raw = (DEST / pack['licenseFile']).read_bytes()
        assert hashlib.sha256(raw).hexdigest().upper() == pack['licenseSha256']
        assert b'CC0' in raw and b'Kenney' in raw
        assert pack['source'].startswith('https://kenney.nl/assets/')
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
        assert 0 < len(values) <= 13230
        peak = max(abs(v) for v in values) / 32768
        assert 0.499 < peak <= 0.501
        assert len(set(values)) > 10
        assert len(row['sourceSha256']) == 64 and row['original'].endswith('.ogg')
        print(f"{row['file']}: {len(values)/44.1:.3f} ms, peak {peak:.5f}, SHA256 matched")
    print('HITMARKER_ASSETS_VERIFIED')


if __name__ == '__main__': verify()
