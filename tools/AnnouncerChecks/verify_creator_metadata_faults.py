"""Exercise integration metadata validation without modifying asset bytes."""
import copy
import json
from verify_creator_pack import ASSETS, verify_metadata, verify_runtime_catalog


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
    print(f'CREATOR_METADATA_REGRESSION_VERIFIED: {len(cases)} metadata faults rejected; original accepted')


if __name__ == '__main__':
    main()
