# Exact mod-owned paths only; shared by payload, migration and inventory checks.
# Every alias points directly to its final location (no order-dependent chains).
function Get-ArenaNoticeMoves {
    $moves = [ordered]@{}
    foreach ($name in @('ANNOUNCER-ADDITIONS-20260917.json', 'ANNOUNCER-SOURCES.json', 'AUDIO-LICENSES.md')) {
        $moves[$name] = "docs/licenses/audio/$name"
        $moves["docs/$name"] = "docs/licenses/audio/$name"
    }
    foreach ($name in @('NOTICE.md', 'SOURCES.json', 'MIT-LICENSE.txt')) { $moves["Ui/$name"] = "docs/licenses/ui/$name" }
    foreach ($old in @('CC-BY-3.0.txt', 'docs/CC-BY-3.0.txt', 'Ui/CC-BY-3.0.txt', 'docs/licenses/ui/CC-BY-3.0.txt')) {
        $moves[$old] = 'docs/licenses/CC-BY-3.0.txt'
    }
    foreach ($folder in @('hitmarkers', 'pickups')) {
        foreach ($prefix in @('', 'docs/licenses/')) {
            $moves["$prefix$folder/CC0-1.0.txt"] = 'docs/licenses/CC0-1.0.txt'
            $name = if ($folder -eq 'hitmarkers') { 'HITMARKER' } else { 'PICKUP' }
            $moves["$prefix$folder/SOURCES.json"] = "docs/licenses/audio/$name-SOURCES.json"
        }
    }
    foreach ($name in @('ATTRIBUTION.md', 'MAP-SOURCES.json', 'customs-ground.png',
        'Source/Customs-Ground_Level.svg', 'Source/Shebuka-LICENSE-MODIFIED BY ACIDPHANTASM.md')) {
        $moves["MapPreviews/$name"] = "docs/licenses/map-previews/$name"
    }
    foreach ($name in @('hit', 'headshot', 'kill')) { $moves["hitmarkers/$name.wav"] = "sounds/$name.wav" }
    $moves['pickups/collect.wav'] = 'sounds/collect.wav'
    # Exact current source inventory only. Never move/delete an unknown user sound.
    foreach ($name in @(
        'double_kill.ogg',
        'triple_kill.ogg',
        'multi_kill.ogg',
        'monster_kill.ogg',
        'killing_spree.ogg',
        'rampage.ogg',
        'dominating.ogg',
        'unstoppable.ogg',
        'godlike.ogg',
        'first_blood.ogg',
        'headshot.ogg',
        'payback.ogg',
        'last_man_standing.ogg',
        'round_start.ogg',
        'round_won.ogg',
        'round_lost.ogg',
        'match_won.ogg',
        'match_lost.ogg',
        'prepare_to_fight.ogg',
        'three.ogg',
        'two.ogg',
        'one.ogg',
        'taken_the_lead.ogg',
        'lost_the_lead.ogg',
        'tied_for_the_lead.ogg',
        'one_minute_remaining.ogg',
        'thirty_seconds_remaining.ogg',
        'ten_seconds_remaining.ogg',
        'five_kills_remaining.ogg',
        'one_kill_remaining.ogg',
        'match_point.ogg',
        'final_round.ogg',
        'final_weapon.ogg',
        'ladder_complete.ogg',
        'draw.ogg',
        'shutdown.ogg',
        'clutch.ogg',
        'reserve/round_won.ogg',
        'reserve/round_lost.ogg',
        'reserve/victory.ogg',
        'reserve/defeat.ogg',
        'reserve/your_flag_has_been_taken.ogg',
        'reserve/enemy_flag_taken.ogg',
        'reserve/flag_dropped.ogg',
        'reserve/flag_returned.ogg',
        'reserve/flag_captured.ogg',
        'reserve/trickshot.ogg',
        'no_scope.ogg',
        'airborne.ogg',
        'reserve/air_superiority.ogg',
        'no_contest.ogg',
        'embarrassing.ogg'
    )) { $moves["sounds/$name"] = "announcements/$name" }
    return $moves
}
function Get-ArenaRetiredNotices {
    # Contents were consolidated into audio/AUDIO-LICENSES.md, ui/NOTICE.md and
    # the shared project LICENSE. Deployment archives these outside plugins.
    return @('ANNOUNCER-PACK.md', 'docs/ANNOUNCER-PACK.md', 'HUD-ICON-LICENSES.md', 'docs/HUD-ICON-LICENSES.md',
        'docs/REDEPLOY-CORE-LICENSE.txt', 'pickups/README.md', 'docs/licenses/pickups/README.md',
        'hitmarkers/KENNEY-IMPACT-LICENSE.txt', 'hitmarkers/KENNEY-INTERFACE-LICENSE.txt',
        'hitmarkers/.gitattributes', 'pickups/.gitattributes')
}
