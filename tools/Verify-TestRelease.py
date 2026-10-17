"""Offline ZIP integrity/layout check; does not load any game assemblies."""
import hashlib
import json
from pathlib import PurePosixPath
import posixpath
import re
import sys
from urllib.parse import unquote, urlsplit
import zipfile

ASSETS = 'BepInEx/plugins/maschine-ArenaMode/'
DOCS = ASSETS + 'docs/'
MANIFEST = DOCS + 'PACKAGE-MANIFEST.json'
DLLS = {
    ASSETS + 'maschine-ArenaMode.Client.dll',
    ASSETS + 'maschine-ArenaMode.BigBrainCompat.dll',
    'BepInEx/plugins/maschine-Redeploy.Core.dll',
    'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll',
}
REQUIRED = DLLS | {
    DOCS + 'INSTALL.txt', DOCS + 'README.md', DOCS + 'LICENSE',
    DOCS + 'REDEPLOY-CORE-LICENSE.txt', DOCS + 'THIRD-PARTY-NOTICES.md', MANIFEST,
    DOCS + 'ANNOUNCER-ADDITIONS-20260917.json', DOCS + 'ANNOUNCER-SOURCES.json',
    DOCS + 'ANNOUNCER-PACK.md', DOCS + 'CC-BY-3.0.txt',
    DOCS + 'AUDIO-LICENSES.md', DOCS + 'HUD-ICON-LICENSES.md',
    'SPT_Runtime/user/mods/ArenaMode/config/kits.json',
    'SPT_Runtime/user/mods/ArenaMode/config/ladder.json',
    ASSETS + 'Ui/NOTICE.md', ASSETS + 'Ui/SOURCES.json',
    ASSETS + 'Ui/MIT-LICENSE.txt', ASSETS + 'Ui/CC-BY-3.0.txt',
    ASSETS + 'MapPreviews/ATTRIBUTION.md',
}


def resolve_reference(document, relative):
    assert not relative.startswith(('/', '\\')) and '\\' not in relative, f'Unsafe reference: {relative}'
    path = posixpath.normpath(posixpath.join(posixpath.dirname(document), unquote(relative)))
    assert path.startswith(ASSETS), f'Reference leaves the asset tree: {document}: {relative}'
    return path


def verify(path):
    if not __debug__:
        raise RuntimeError('Run without PYTHONOPTIMIZE or -O: validation must not be disabled.')
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        names = [entry.filename for entry in entries]
        assert len(names) == len(set(name.casefold() for name in names)), 'Duplicate ZIP entry'
        for entry in entries:
            name = entry.filename
            parts = name.split('/')
            assert not entry.is_dir(), f'Unexpected directory entry: {name}'
            assert '\\' not in name and ':' not in name and not name.startswith('/'), f'Unsafe entry: {name}'
            assert all(part not in {'', '..', '.'} for part in parts), f'Unsafe entry: {name}'
            assert len(parts) > 1 and parts[0] in {'BepInEx', 'SPT_Runtime'}, f'Unexpected ZIP root payload: {name}'
            assert name in REQUIRED or any(name.startswith(ASSETS + folder + '/') for folder in
                ('sounds', 'hitmarkers', 'pickups', 'MapPreviews', 'Ui')), f'Unexpected payload: {name}'
            assert PurePosixPath(name).suffix.lower() not in {'.exe', '.pdb', '.cfg', '.log', '.zip'}, f'Forbidden payload: {name}'
        assert REQUIRED <= set(names), f'Missing files: {REQUIRED - set(names)}'
        assert {name for name in names if name.lower().endswith('.dll')} == DLLS, 'Unexpected DLL set'
        manifest = json.loads(archive.read(MANIFEST))
        assert manifest['schemaVersion'] == 2 and manifest['kind'] == 'local-test'
        assert manifest['layout'] == 'spt-root-folders-v3', 'Unexpected package layout version'
        assert not manifest['deployed'] and not manifest['uploaded']
        for key in ('arenaCommit', 'redeployCoreCommit'):
            assert len(manifest[key]) == 40 and all(c in '0123456789abcdef' for c in manifest[key]), key
        records = manifest['files']
        assert len(records) == len({item['path'] for item in records}), 'Duplicate manifest record'
        assert {item['path'] for item in records} == set(names) - {MANIFEST}, 'Manifest/file mismatch'
        for item in records:
            data = archive.read(item['path'])
            assert len(data) == item['bytes'], f"Size mismatch: {item['path']}"
            assert hashlib.sha256(data).hexdigest() == item['sha256'], f"Hash mismatch: {item['path']}"
            if item['path'] in DLLS:
                assert data[:2] == b'MZ', f"Not a DLL: {item['path']}"
        for name in ('kits.json', 'ladder.json'):
            data = json.loads(archive.read('SPT_Runtime/user/mods/ArenaMode/config/' + name))
            assert data, f'Empty defaults: {name}'
        links = 0
        for name in names:
            if not name.endswith('.md'):
                continue
            for target in re.findall(r'\]\(([^)]+)\)', archive.read(name).decode('utf-8-sig')):
                target = target.strip('<>')
                reference = urlsplit(target)
                if target.startswith('https://github.com/maschine34675/ArenaMode/blob/'):
                    assert target.startswith('https://github.com/maschine34675/ArenaMode/blob/' + manifest['arenaCommit'] + '/'), f'Unpinned source link: {name}: {target}'
                if reference.scheme or reference.netloc or not reference.path:
                    continue
                resolved = resolve_reference(name, reference.path)
                assert resolved in names, f'Broken packaged link: {name}: {target}'
                links += 1
        audio_files = set()
        for name in ('ANNOUNCER-SOURCES.json', 'ANNOUNCER-ADDITIONS-20260917.json'):
            document = DOCS + name
            for clip in json.loads(archive.read(document))['files']:
                assert clip['file'].startswith('../sounds/'), f'Unrelocated audio path: {clip["file"]}'
                resolved = resolve_reference(document, clip['file'])
                assert resolved in names and resolved not in audio_files, f'Missing/duplicate audio reference: {resolved}'
                audio_files.add(resolved)
                assert hashlib.sha256(archive.read(resolved)).hexdigest() == clip['sha256'].lower(), f'Audio provenance hash mismatch: {resolved}'
        assert audio_files == {name for name in names if name.endswith('.ogg')}, 'Unrecorded/missing announcer file'
        assert archive.testzip() is None, 'ZIP CRC failure'
        print(f'TEST_RELEASE_VERIFIED: {len(names)} files; folder-only root, exact DLL set, paths, hashes, defaults, {links} local notice links and {len(audio_files)} audio provenance records')


if __name__ == '__main__':
    verify(sys.argv[1])
