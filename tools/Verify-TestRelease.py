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
AUDIO_DOCS = DOCS + 'licenses/audio/'
WAVS = {ASSETS + 'sounds/' + name + '.wav' for name in ('collect', 'headshot', 'hit', 'kill')}
# Arena session (docs/ARENA-SESSION-20261021.md): the gate is a preloader patcher in its own folder; the
# helper is the only executable of the package and sits beside the client DLL with the shipped default list.
GATE = 'BepInEx/patchers/maschine-ArenaMode/maschine-ArenaMode.Gate.dll'
DLLS = {
    ASSETS + 'maschine-ArenaMode.Client.dll',
    ASSETS + 'maschine-ArenaMode.BigBrainCompat.dll',
    'BepInEx/plugins/maschine-Redeploy.Core.dll',
    'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll',
    GATE,
}
EXES = {ASSETS + 'maschine-ArenaMode.Session.exe'}
WHITELIST = ASSETS + 'arena-whitelist.json'
# The user's decision (2026-10-21): these mods are always on the shipped default list.
DEFAULT_WHITELIST_GUIDS = {
    'me.sol.sain', 'com.acidphantasm.progressivebotsystem', 'com.acidphantasm.botplacementsystem',
    'xyz.drakia.bigbrain', 'xyz.drakia.waypoints',
}
REQUIRED = DLLS | EXES | {WHITELIST,
    DOCS + 'INSTALL.txt', DOCS + 'README.md', DOCS + 'LICENSE',
    DOCS + 'THIRD-PARTY-NOTICES.md', MANIFEST,
    AUDIO_DOCS + 'ANNOUNCER-ADDITIONS-20260917.json', AUDIO_DOCS + 'ANNOUNCER-SOURCES.json',
    AUDIO_DOCS + 'AUDIO-LICENSES.md', AUDIO_DOCS + 'HITMARKER-SOURCES.json', AUDIO_DOCS + 'PICKUP-SOURCES.json',
    DOCS + 'licenses/CC-BY-3.0.txt', DOCS + 'licenses/CC0-1.0.txt',
    'SPT_Runtime/user/mods/ArenaMode/config/kits.json',
    'SPT_Runtime/user/mods/ArenaMode/config/ladder.json',
    DOCS + 'licenses/ui/NOTICE.md', DOCS + 'licenses/ui/SOURCES.json',
    DOCS + 'licenses/ui/MIT-LICENSE.txt',
    DOCS + 'licenses/map-previews/ATTRIBUTION.md',
} | WAVS


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
            assert not name.casefold().startswith((ASSETS + 'announcements/reserve/').casefold()), f'Unused reserve audio in package: {name}'
            assert name in REQUIRED or any(name.startswith(ASSETS + folder + '/') for folder in
                ('announcements', 'docs/licenses/map-previews')), f'Unexpected payload: {name}'
            if name.startswith(ASSETS + 'announcements/'):
                assert re.fullmatch(re.escape(ASSETS) + r'announcements/[a-z0-9_]+\.ogg', name), f'Non-runtime or nested announcement: {name}'
            assert PurePosixPath(name).suffix.lower() not in {'.exe', '.pdb', '.cfg', '.log', '.zip'} or name in EXES, f'Forbidden payload: {name}'
        assert REQUIRED <= set(names), f'Missing files: {REQUIRED - set(names)}'
        assert {name for name in names if name.lower().endswith('.dll')} == DLLS, 'Unexpected DLL set'
        assert {name for name in names if name.lower().endswith('.exe')} == EXES, 'Unexpected executable set'
        manifest = json.loads(archive.read(MANIFEST))
        assert manifest['schemaVersion'] == 2 and manifest['kind'] in {'local-test', 'release'}
        label = 'RELEASE PACKAGE' if manifest['kind'] == 'release' else 'LOCAL TEST PACKAGE'
        install = archive.read(DOCS + 'INSTALL.txt').decode('utf-8-sig')
        assert install.splitlines()[0] == f"ArenaMode {manifest['arenaVersion']} - {label}", 'Package label/manifest mismatch'
        assert manifest['layout'] == 'spt-root-folders-v6', 'Unexpected package layout version'
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
            if item['path'] in DLLS | EXES:
                assert data[:2] == b'MZ', f"Not a DLL or executable: {item['path']}"
        for name in ('kits.json', 'ladder.json'):
            data = json.loads(archive.read('SPT_Runtime/user/mods/ArenaMode/config/' + name))
            assert data, f'Empty defaults: {name}'
        # The shipped Arena session whitelist: a schema 1 policy whose entries name exactly one unit each and
        # which keeps the agreed default mods. (A broken file would make every Arena session unavailable.)
        policy = json.loads(archive.read(WHITELIST))
        assert policy.get('schema') == 1 and isinstance(policy.get('mods'), list) and policy['mods'], 'Bad default whitelist'
        for entry in policy['mods']:
            assert isinstance(entry, dict) and ('guid' in entry) != ('patcher' in entry), f'Whitelist entry needs one of guid and patcher: {entry}'
        listed = {entry['guid'].lower() for entry in policy['mods'] if 'guid' in entry}
        assert DEFAULT_WHITELIST_GUIDS <= listed, f'Default whitelist lost: {DEFAULT_WHITELIST_GUIDS - listed}'
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
            document = AUDIO_DOCS + name
            for clip in json.loads(archive.read(document))['files']:
                assert clip['file'].startswith('../../../announcements/'), f'Unrelocated audio path: {clip["file"]}'
                resolved = resolve_reference(document, clip['file'])
                assert resolved in names and resolved not in audio_files, f'Missing/duplicate audio reference: {resolved}'
                audio_files.add(resolved)
                assert hashlib.sha256(archive.read(resolved)).hexdigest() == clip['sha256'].lower(), f'Audio provenance hash mismatch: {resolved}'
        assert audio_files == {name for name in names if name.endswith('.ogg')}, 'Unrecorded/missing announcer file'
        assert len(audio_files) == 41, 'Expected all 41 active announcements and no reserve clips'
        wav_files = set()
        for category in ('HITMARKER', 'PICKUP'):
            document = AUDIO_DOCS + category + '-SOURCES.json'
            record = json.loads(archive.read(document))
            for clip in record['files'] if category == 'HITMARKER' else [record]:
                assert clip['file'].startswith('../../../sounds/'), f'Unrelocated effect path: {clip["file"]}'
                resolved = resolve_reference(document, clip['file'])
                assert resolved in WAVS and resolved not in wav_files, f'Unexpected/duplicate effect: {resolved}'
                wav_files.add(resolved)
                assert hashlib.sha256(archive.read(resolved)).hexdigest() == clip['sha256'].lower(), resolved
            assert record['licenseFile'] == '../CC0-1.0.txt', 'Effects share the complete CC0 license'
            license_path = resolve_reference(document, record['licenseFile'])
            assert hashlib.sha256(archive.read(license_path)).hexdigest() == record['licenseSha256'].lower(), license_path
        assert wav_files == WAVS, 'Unrecorded/missing effect file'
        assert archive.testzip() is None, 'ZIP CRC failure'
        print(f'TEST_RELEASE_VERIFIED: {len(names)} files; folder-only root, exact DLL and helper set, whitelist, paths, hashes, defaults, {links} local notice links and {len(audio_files)} audio provenance records')


if __name__ == '__main__':
    verify(sys.argv[1])
