# Creator announcer pack

Imported 2026-09-12 from the user's `alle_ansagen.ogg` (62.561950 seconds).
The user states this recording was generated under ElevenLabs Creator.
All 46 phrases follow the supplied order. Full rights/source record:
[AUDIO-LICENSES.md](AUDIO-LICENSES.md), [ANNOUNCER-SOURCES.json](ANNOUNCER-SOURCES.json).

The 18 existing event keys are unchanged. In particular, ULTRA KILL uses the
existing four-kill MULTI event; the HUD still says MULTI KILL. ROUND WINNER,
MISSION FAILED, MISSION ACCOMPLISHED and GAME OVER remain the current result
phrases. ROUND WON/LOST and VICTORY/DEFEAT are alternatives kept in reserve.
The 28 reserve clips have **no new gameplay triggers**. The current loader scans
only the top level of `sounds`; normal build/publish also copies `sounds/reserve`.

Slicing uses pause midpoints cross-checked against the waveform and a local
base.en Whisper transcript. The input was not uploaded for transcription.
ASR word timestamps alone are too imprecise to cut effects/reverb; they were not
used as cut points. ROUND WON was recognized phonetically as "round one";
its label follows the user's supplied list. Fade edges are only 5/10 ms; the
original voice/effects and relative levels are retained with uniform -1 dB
encoding headroom. 44.1 kHz stereo Vorbis q7 minimizes another encoding loss.

| # | Spoken line | File (relative to this folder) | Usage |
| --- | --- | --- | --- |
| 1 | DOUBLE KILL | `sounds/double_kill.ogg` | active |
| 2 | TRIPLE KILL | `sounds/triple_kill.ogg` | active |
| 3 | ULTRA KILL | `sounds/multi_kill.ogg` | active |
| 4 | MONSTER KILL | `sounds/monster_kill.ogg` | active |
| 5 | KILLING SPREE | `sounds/killing_spree.ogg` | active |
| 6 | RAMPAGE | `sounds/rampage.ogg` | active |
| 7 | DOMINATING | `sounds/dominating.ogg` | active |
| 8 | UNSTOPPABLE | `sounds/unstoppable.ogg` | active |
| 9 | GODLIKE | `sounds/godlike.ogg` | active |
| 10 | FIRST BLOOD | `sounds/first_blood.ogg` | active |
| 11 | HEADSHOT | `sounds/headshot.ogg` | active |
| 12 | PAYBACK | `sounds/payback.ogg` | active |
| 13 | LAST MAN STANDING | `sounds/last_man_standing.ogg` | active |
| 14 | ROUND START | `sounds/round_start.ogg` | active |
| 15 | ROUND WINNER | `sounds/round_won.ogg` | active |
| 16 | MISSION FAILED | `sounds/round_lost.ogg` | active |
| 17 | MISSION ACCOMPLISHED | `sounds/match_won.ogg` | active |
| 18 | GAME OVER | `sounds/match_lost.ogg` | active |
| 19 | PREPARE TO FIGHT | `sounds/reserve/prepare_to_fight.ogg` | reserve |
| 20 | THREE | `sounds/reserve/three.ogg` | reserve |
| 21 | TWO | `sounds/reserve/two.ogg` | reserve |
| 22 | ONE | `sounds/reserve/one.ogg` | reserve |
| 23 | TAKEN THE LEAD | `sounds/reserve/taken_the_lead.ogg` | reserve |
| 24 | LOST THE LEAD | `sounds/reserve/lost_the_lead.ogg` | reserve |
| 25 | TIED FOR THE LEAD | `sounds/reserve/tied_for_the_lead.ogg` | reserve |
| 26 | ONE MINUTE REMAINING | `sounds/reserve/one_minute_remaining.ogg` | reserve |
| 27 | THIRTY SECONDS REMAINING | `sounds/reserve/thirty_seconds_remaining.ogg` | reserve |
| 28 | TEN SECONDS REMAINING | `sounds/reserve/ten_seconds_remaining.ogg` | reserve |
| 29 | FIVE KILLS REMAINING | `sounds/reserve/five_kills_remaining.ogg` | reserve |
| 30 | ONE KILL REMAINING | `sounds/reserve/one_kill_remaining.ogg` | reserve |
| 31 | MATCH POINT | `sounds/reserve/match_point.ogg` | reserve |
| 32 | FINAL ROUND | `sounds/reserve/final_round.ogg` | reserve |
| 33 | FINAL WEAPON | `sounds/reserve/final_weapon.ogg` | reserve |
| 34 | LADDER COMPLETE | `sounds/reserve/ladder_complete.ogg` | reserve |
| 35 | DRAW | `sounds/reserve/draw.ogg` | reserve |
| 36 | SHUTDOWN | `sounds/reserve/shutdown.ogg` | reserve |
| 37 | CLUTCH | `sounds/reserve/clutch.ogg` | reserve |
| 38 | ROUND WON | `sounds/reserve/round_won.ogg` | reserve |
| 39 | ROUND LOST | `sounds/reserve/round_lost.ogg` | reserve |
| 40 | VICTORY | `sounds/reserve/victory.ogg` | reserve |
| 41 | DEFEAT | `sounds/reserve/defeat.ogg` | reserve |
| 42 | YOUR FLAG HAS BEEN TAKEN | `sounds/reserve/your_flag_has_been_taken.ogg` | reserve |
| 43 | ENEMY FLAG TAKEN | `sounds/reserve/enemy_flag_taken.ogg` | reserve |
| 44 | FLAG DROPPED | `sounds/reserve/flag_dropped.ogg` | reserve |
| 45 | FLAG RETURNED | `sounds/reserve/flag_returned.ogg` | reserve |
| 46 | FLAG CAPTURED | `sounds/reserve/flag_captured.ogg` | reserve |

## Reproduction and verification

From the repository root, with Python 3 and FFmpeg/ffprobe on PATH:

```powershell
python tools/AnnouncerChecks/split_creator_pack.py 'D:\SPT41\Development\alle_ansagen.ogg'
python tools/AnnouncerChecks/verify_creator_pack.py --assets artifacts/creator-announcer/pack
python tools/AnnouncerChecks/verify_creator_pack.py
```

The splitter validates the source SHA256 and writes only to ignored
`artifacts/creator-announcer/pack/`. Its adjacent `creator_cuts.json` pins all
reviewed labels, paths and frame boundaries. Review generated clips and copy them
with ANNOUNCER-SOURCES.json into tracked assets. Vorbis/Ogg container bytes can
vary between encoder versions/runs; the generated manifest records the actual
output hashes. Do not regenerate during normal builds.

Offline verification decodes every clip and checks hashes, format, finite/nonzero
signal, peak headroom, duration, source coverage, metadata and active/reserve sets.
Optional `--deployment <asset-folder>` compares the entire packaged asset tree.
The checks do not certify subjective audio quality or native in-game playback.

**Restart the game after updating**: healthy announcer PCM is cached across
matches. No DLL swap or server restart is needed for this asset-only change.
Use F12 Audio / AnnouncerVolume for the independent voice volume.
Backups and delivery evidence for this intake: ignored
`artifacts/creator-announcer/`; the original combined OGG stays outside Git.
