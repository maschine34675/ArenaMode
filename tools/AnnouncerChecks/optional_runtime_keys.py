"""Promoted runtime cues; original intake labels/cuts and audio remain unchanged."""

OPTIONAL = frozenset('prepare_to_fight three two one taken_the_lead lost_the_lead '
    'tied_for_the_lead one_minute_remaining thirty_seconds_remaining ten_seconds_remaining '
    'five_kills_remaining one_kill_remaining match_point final_round final_weapon ladder_complete '
    'draw shutdown clutch'.split())


def runtime_key(relative):
    prefix, suffix = 'sounds/', '.ogg'
    if relative.startswith(prefix) and relative.endswith(suffix):
        key = relative[len(prefix):-len(suffix)]
        if key in OPTIONAL:
            return key
    return None
