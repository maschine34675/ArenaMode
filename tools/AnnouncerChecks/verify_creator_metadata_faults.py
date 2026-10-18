"""Exercise metadata and exact original/supplement coverage using temporary copies."""
import copy
import json
from pathlib import Path
import shutil
import tempfile
from verify_creator_pack import ADDITIONS, ASSETS, sha, verify_inventory, verify_metadata, verify_runtime_catalog


def reject(name, action, expected):
    try:
        action()
    except ValueError as error:
        if expected not in str(error):
            raise AssertionError(f'{name}: wrong failure: {error}') from error
    else:
        raise AssertionError(f'{name}: mutation was accepted')


def verify_inventory_faults(manifest):
    supplement = json.loads((ASSETS / ADDITIONS.MANIFEST).read_text(encoding='utf-8'))
    paths = verify_inventory(ASSETS, manifest, supplement)
    if len(paths) != 52:
        raise AssertionError('Reviewed 46+6 positive control failed')
    rejected = 0
    with tempfile.TemporaryDirectory(prefix='arena-creator-inventory-') as temporary:
        assets = Path(temporary)
        shutil.copy2(ASSETS / 'ANNOUNCER-SOURCES.json', assets / 'ANNOUNCER-SOURCES.json')
        shutil.copy2(ASSETS / ADDITIONS.MANIFEST, assets / ADDITIONS.MANIFEST)
        for name in paths:
            target = assets / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(ASSETS / name, target)
        verify_inventory(assets, manifest, supplement)

        # Git may check out this TEXT manifest as LF or CRLF. Both must retain
        # the reviewed canonical pin; audio bytes and semantic mutations remain
        # exact. Exercise temporary copies only, never rewrite the source pack.
        manifest_path = assets / 'ANNOUNCER-SOURCES.json'
        original_bytes = manifest_path.read_bytes()
        lf_bytes = original_bytes.replace(b'\r\n', b'\n')
        for endings in (lf_bytes, lf_bytes.replace(b'\n', b'\r\n')):
            manifest_path.write_bytes(endings)
            verify_inventory(assets, manifest, supplement)
        manifest_path.write_bytes(original_bytes)

        # Unknown root, reserve and nested files must not pass a subset test.
        for name, expected in [('sounds/unknown.ogg', 'Active cue set changed'),
                               ('sounds/reserve/unknown.ogg', 'Missing/unrecorded OGGs'),
                               ('sounds/reserve/nested/unknown.ogg', 'Missing/unrecorded OGGs')]:
            target = assets / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(ASSETS / 'sounds/airborne.ogg', target)
            reject(name, lambda: verify_inventory(assets, manifest, supplement), expected)
            target.unlink()
            rejected += 1

        for name, expected in [('sounds/airborne.ogg', 'Active cue set changed'),
                               ('sounds/reserve/trickshot.ogg', 'Missing/unrecorded OGGs')]:
            (assets / name).unlink()
            reject('missing ' + name, lambda: verify_inventory(assets, manifest, supplement), expected)
            shutil.copy2(ASSETS / name, assets / name)
            rejected += 1

        for name, expected in [('sounds/double_kill.ogg', 'Original clip changed'),
                               ('sounds/airborne.ogg', 'Addition clip changed')]:
            (assets / name).write_bytes(b'changed recording')
            reject(name, lambda: verify_inventory(assets, manifest, supplement), expected)
            shutil.copy2(ASSETS / name, assets / name)
            rejected += 1

        # The original hash table is pinned, not merely trusted after alteration.
        original = copy.deepcopy(manifest)
        original_path = assets / original['files'][0]['file']
        original_path.write_bytes(b'replaced original')
        original['files'][0]['sha256'] = sha(original_path)
        (assets / 'ANNOUNCER-SOURCES.json').write_text(json.dumps(original), encoding='utf-8')
        reject('rewritten original and checksum', lambda: verify_inventory(assets, original, supplement),
            'Original manifest changed')
        rejected += 1

    for field, value, expected in [('eventKey', 'trickshot', 'Active cue mapping changed'),
                                  ('file', 'sounds/reserve/no_scope.ogg', 'cuts changed'),
                                  ('startFrame', 1, 'cuts changed')]:
        changed = copy.deepcopy(supplement)
        changed['files'][1][field] = value
        reject('supplement ' + field, lambda: verify_inventory(ASSETS, manifest, changed), expected)
        rejected += 1
    verify_inventory(ASSETS, manifest, supplement)
    return rejected


def main():
    manifest = json.loads((ASSETS / 'ANNOUNCER-SOURCES.json').read_text(encoding='utf-8'))
    verify_metadata(manifest)
    verify_runtime_catalog()
    cases = [
        ('missing optional cue', 18, 'runtimeEventKey', None, 'Enabled optional cue set differs'),
        ('CTF enabled', 41, 'runtimeEventKey', 'flag_returned', 'Enabled optional cue set differs'),
        ('alternate collision', 37, 'runtimeEventKey', 'round_won', 'Enabled optional cue set differs'),
        ('misrouted selected cue', 18, 'runtimeEventKey', 'one', 'Enabled optional cue set differs'),
        ('intake overwritten', 18, 'usage', 'active', 'Original intake classification changed'),
        ('intake event overwritten', 18, 'eventKey', 'prepare_to_fight', 'Original event mapping changed'),
        ('source cuts changed', 18, 'startFrame', 1, 'Reviewed phrase order, destinations or cuts changed'),
        ('active cue left in reserve', 18, 'file', 'sounds/reserve/prepare_to_fight.ogg',
            'Reviewed phrase order, destinations or cuts changed'),
    ]
    for name, index, field, value, expected in cases:
        mutated = copy.deepcopy(manifest)
        mutated['files'][index][field] = value
        try:
            verify_metadata(mutated)
        except ValueError as error:
            if str(error) != expected:
                raise AssertionError(f'{name}: wrong failure: {error}') from error
        else:
            raise AssertionError(f'{name}: mutation was accepted')
    verify_metadata(manifest)
    inventory_faults = verify_inventory_faults(manifest)
    print(f'CREATOR_METADATA_REGRESSION_VERIFIED: {len(cases)} metadata and '
        f'{inventory_faults} original/supplement inventory faults rejected; reviewed 52-file pack accepted')


if __name__ == '__main__':
    main()
