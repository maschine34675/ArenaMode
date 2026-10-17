"""Offline ZIP integrity/layout check; does not load any game assemblies."""
import hashlib
import json
from pathlib import PurePosixPath
import sys
import zipfile

DLLS = {
    'BepInEx/plugins/maschine-ArenaMode.Client.dll',
    'BepInEx/plugins/maschine-Redeploy.Core.dll',
    'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll',
    'Optional/BigBrain-1.5.0/BepInEx/plugins/maschine-ArenaMode.BigBrainCompat.dll',
}
REQUIRED = DLLS | {
    'INSTALL.txt', 'README.md', 'LICENSE', 'REDEPLOY-CORE-LICENSE.txt',
    'THIRD-PARTY-NOTICES.md', 'PACKAGE-MANIFEST.json',
    'SPT_Runtime/user/mods/ArenaMode/config/kits.json',
    'SPT_Runtime/user/mods/ArenaMode/config/ladder.json',
    'BepInEx/plugins/ArenaMode/AUDIO-LICENSES.md',
    'BepInEx/plugins/ArenaMode/HUD-ICON-LICENSES.md',
    'BepInEx/plugins/ArenaMode/Ui/NOTICE.md',
    'BepInEx/plugins/ArenaMode/Ui/MIT-LICENSE.txt',
    'BepInEx/plugins/ArenaMode/Ui/CC-BY-3.0.txt',
    'BepInEx/plugins/ArenaMode/MapPreviews/ATTRIBUTION.md',
}


def verify(path):
    if not __debug__:
        raise RuntimeError('Run without PYTHONOPTIMIZE or -O: validation must not be disabled.')
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        names = [entry.filename for entry in entries]
        assert len(names) == len(set(name.casefold() for name in names)), 'Duplicate ZIP entry'
        for entry in entries:
            name = entry.filename
            parts = PurePosixPath(name).parts
            assert not entry.is_dir(), f'Unexpected directory entry: {name}'
            assert '\\' not in name and ':' not in name and not name.startswith('/'), f'Unsafe entry: {name}'
            assert '..' not in parts and '.' not in parts, f'Unsafe entry: {name}'
            assert name in REQUIRED or name.startswith('BepInEx/plugins/ArenaMode/'), f'Unexpected payload: {name}'
            assert PurePosixPath(name).suffix.lower() not in {'.exe', '.pdb', '.cfg', '.log', '.zip'}, f'Forbidden payload: {name}'
        assert REQUIRED <= set(names), f'Missing files: {REQUIRED - set(names)}'
        assert {name for name in names if name.lower().endswith('.dll')} == DLLS, 'Unexpected DLL set'
        manifest = json.loads(archive.read('PACKAGE-MANIFEST.json'))
        assert manifest['schemaVersion'] == 1 and manifest['kind'] == 'local-test'
        assert not manifest['deployed'] and not manifest['uploaded']
        for key in ('arenaCommit', 'redeployCoreCommit'):
            assert len(manifest[key]) == 40 and all(c in '0123456789abcdef' for c in manifest[key]), key
        records = manifest['files']
        assert len(records) == len({item['path'] for item in records}), 'Duplicate manifest record'
        assert {item['path'] for item in records} == set(names) - {'PACKAGE-MANIFEST.json'}, 'Manifest/file mismatch'
        for item in records:
            data = archive.read(item['path'])
            assert len(data) == item['bytes'], f"Size mismatch: {item['path']}"
            assert hashlib.sha256(data).hexdigest() == item['sha256'], f"Hash mismatch: {item['path']}"
            if item['path'] in DLLS:
                assert data[:2] == b'MZ', f"Not a DLL: {item['path']}"
        for name in ('kits.json', 'ladder.json'):
            data = json.loads(archive.read('SPT_Runtime/user/mods/ArenaMode/config/' + name))
            assert data, f'Empty defaults: {name}'
        assert archive.testzip() is None, 'ZIP CRC failure'
        print(f'TEST_RELEASE_VERIFIED: {len(names)} files; exact DLL set, paths, hashes, defaults and notices')


if __name__ == '__main__':
    verify(sys.argv[1])
