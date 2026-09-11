# ArenaMode audio

## Shared hitmarker sound (2026-09-11)

All three effects in `hitmarkers/` use **Hitmarker Sound Effect** by
**User391915396**, published on Freesound on 2021-05-04.

- Source: <https://freesound.org/people/User391915396/sounds/570335/>.
- License on the source page: **CC0 1.0 Universal**, checked 2026-09-11.
  Redistribution and adaptation are permitted under CC0; credit is optional.
  No endorsement or warranty is implied. The uploader describes processing
  recording noise into this effect; this is not an attribution to Call of Duty.
- License: <https://creativecommons.org/publicdomain/zero/1.0/>.
- Unchanged legal text: [CC0-1.0.txt](hitmarkers/CC0-1.0.txt), downloaded from
  <https://creativecommons.org/publicdomain/zero/1.0/legalcode.txt>.
- User-supplied original: `570335__user391915396__hitmarker-sound-effect.wav`,
  48 kHz stereo PCM24, 9,000 frames (187.5 ms).
- [SOURCES.json](hitmarkers/SOURCES.json) records the source, source/output/license
  hashes, original format, encoder version and exact modifications.

| Cue | Bundled WAV | Duration |
| --- | --- | --- |
| Hit | `hitmarkers/hit.wav` | 187.506 ms |
| Headshot | `hitmarkers/headshot.wav` | 187.506 ms |
| Kill | `hitmarkers/kill.wav` | 187.506 ms |

The three files are byte-identical, as requested after the user's audition.
Conversion averages the stereo channels (0.5 L + 0.5 R), resamples to 44.1 kHz
and encodes signed PCM16 WAV for the existing loader. No trimming, normalization,
fades, pitch changes, splicing or added effects. Duration differs only by rounding
to a whole output frame. Cue priorities and the independent volume control remain.

Reproduce with `python tools/HitmarkerAudioChecks/import_assets.py <original.wav>`
(explicit local intake plus FFmpeg, never run by builds). Validate offline with
`python tools/HitmarkerAudioChecks/verify_assets.py`. Keep the WAVs, legal text and
source record together; these assets are independent of the announcer and code
licenses. The previous Kenney examples have been replaced; Git retains history.

## TripleSnail kill callouts

The six TripleSnail recordings in `sounds/` are from **Announcer kill sounds**:

- Voice recording: **Antti Saari**.
- Original audio editing and pack publication: **TripleSnail**.
- Source: <https://opengameart.org/content/announcer-kill-sounds>.
- Original attribution: "Recorded by Antti Saari Edited by TripleSnail".
- License: **Creative Commons Attribution 3.0 Unported (CC BY 3.0)**.
- License URL: <https://creativecommons.org/licenses/by/3.0/>.
- Full license text: [CC-BY-3.0.txt](CC-BY-3.0.txt).
- Source/license checked: 2026-08-31.

These audio assets retain their CC BY 3.0 license independently of the mod's
code. Keep this attribution and the license with redistributed copies. The
license permits redistribution and adaptations subject to its terms, including
attribution, retention of notices, identification of adaptations, and no
additional restrictions on the licensed audio. No endorsement by the original
creators is implied. The assets are supplied without warranties; see the license.

### Files and modifications

For ArenaMode, six of the user-supplied original WAV files were converted to
Ogg Vorbis and renamed to match the existing callout keys. Encoding used
FFmpeg 8.1.2, `libvorbis`, quality `5`. Original sample rates, channel counts,
duration and volume were retained; no trimming, normalization, pitch shifting,
splicing or additional effects were applied. The lossy format conversion changes
the encoded signal. Attribution/source/license metadata was added to each OGG.

| Original file | Bundled file | Existing callout |
| --- | --- | --- |
| DoubleKill.wav | sounds/double_kill.ogg | DOUBLE KILL |
| TripleKill.wav | sounds/triple_kill.ogg | TRIPLE KILL |
| KillingSpree.wav | sounds/killing_spree.ogg | KILLING SPREE |
| Rampage.wav | sounds/rampage.ogg | RAMPAGE |
| Dominating.wav | sounds/dominating.ogg | DOMINATING |
| Godlike.wav | sounds/godlike.ogg | GODLIKE |

The original pack does **not** contain MULTI KILL, MONSTER KILL or UNSTOPPABLE.
MULTI KILL and MONSTER KILL still have no bundled sound; UNSTOPPABLE now uses
the separately licensed WARLORD recording below. Other
recordings in the original pack are not included because the current mod does
not emit those labels. Existing scoring and streak thresholds are unchanged.

Original WAV SHA-256 checksums, for identifying the conversion inputs:

| Original file | SHA-256 |
| --- | --- |
| DoubleKill.wav | 98B8102E7A63C2D4B03DC96EC89F358AE06799CCF7C0835889DA25BC1EF2CF7E |
| TripleKill.wav | 7A1C2A11B9122A5BAE31B80BD7F5ADA6281E2A1868B71A3F1D43B383725BBE82 |
| KillingSpree.wav | 14DC8580BFF409D90CE2FD030E5A656BF78827DA9633777A3BB5A1B2D99B5FA6 |
| Rampage.wav | 51E9DA4F671186B458DD49321DC2A18AFA2FAD7D958ECE533308520A76ED9C46 |
| Dominating.wav | 3B07A0FC72000E5357218970CED7E4BBB71EB2353C0E8D4371E341CB37B73D43 |
| Godlike.wav | FC6CA171E20BF84DEE0DE82A7BDA1F91C8F1005F7DB12C3D9D84ACB232751B09 |

## VoiceBosch WARLORD cues

The ten recordings named below are from **WARLORD - Video
Game Announcer**:

- Creator, voice recording and production: **VoiceBosch**.
- Preferred attribution: **VoiceBosch**.
- Source: <https://opengameart.org/content/warlord-video-game-announcer>.
- License: **Creative Commons Attribution-ShareAlike 4.0 International
  (CC BY-SA 4.0)**.
- License URL: <https://creativecommons.org/licenses/by-sa/4.0/>.
- Full license text: [CC-BY-SA-4.0.txt](CC-BY-SA-4.0.txt).
- Source/license checked: 2026-09-03.

These ten audio assets and the converted OGG files remain under CC BY-SA
4.0 independently of the mod's code and the separately licensed TripleSnail
recordings. Keep this attribution and license with redistributed copies. No
endorsement by VoiceBosch is implied.

The user-supplied original WAV files were converted to Ogg Vorbis and renamed
to stable ArenaMode cue keys. Encoding used FFmpeg 8.1.2, `libvorbis`, quality
`5`, 44.1 kHz stereo. The recordings were loudness-normalized toward -16 LUFS
with a -1.7 dBTP encoder target. The peak limit keeps the final Vorbis files
below -1.5 dBTP, so the two shortest, peak-heavy lines stop below the loudness
target. No trimming, pitch shifting,
splicing or other content edits were applied. Attribution, source, license and
conversion metadata were added to each OGG. These converted files are shared
under CC BY-SA 4.0.

| Original file | Bundled file | Tactical event |
| --- | --- | --- |
| 01._start.wav | sounds/round_start.ogg | A round becomes live |
| 19._round_winner.wav | sounds/round_won.ogg | The player's side wins a round |
| 20._mission_failed.wav | sounds/round_lost.ogg | The player's side loses a round |
| 08._first_blood.wav | sounds/first_blood.ogg | Local player earns the first eligible kill |
| 09._headshot.wav | sounds/headshot.ogg | Local direct one-contact headshot kill |
| 06._unstoppable.wav | sounds/unstoppable.ogg | Existing 20-kill streak milestone |
| 15._last_man_standing.wav | sounds/last_man_standing.ogg | Local PMC is the last living 5v5 teammate |
| 22._mission_accomplished.wav | sounds/match_won.ogg | Protected 5v5 match victory |
| 29._game_over.wav | sounds/match_lost.ogg | Protected 5v5 match defeat |
| 25._payback.wav | sounds/payback.ogg | Kill the same enemy body responsible for the previous death |

The seven additions from 2026-09-05 use the same format/quality but a -2 dBTP
encoder target. Final decoded peaks were measured below -1.5 dBTP. The exact
source/output SHA-256 hashes, encoder version, durations and measured levels
are in [WARLORD-ADDITIONS.json](WARLORD-ADDITIONS.json). Reproduce with
`tools/Convert-WarlordAdditions.ps1` from the repository. No trimming, pitch
shifting or splicing; attribution/license/conversion metadata is embedded.
The three original round cues were not re-encoded.

Original WAV SHA-256 checksums, for identifying the conversion inputs:

| Original file | SHA-256 |
| --- | --- |
| 01._start.wav | E01B662FB2AB2EA1675B7F76E9F7DA5651B9CF770F12C2C90BD6F8E3A0E8DC08 |
| 19._round_winner.wav | 0963F72BE983A7AD798EDB9320A7105E9D0CC3615DE4D68DFCD46936473B5D94 |
| 20._mission_failed.wav | C8DA20D983E1099E5068805E1E0D00F2EF084216B824F8DD24137F7446D83724 |

## Installation and playback

Build and publish output includes the `ArenaMode` directory beside the client
DLL. The local build also copies this directory to `BepInEx/plugins/ArenaMode/`.
Release packages must include the whole directory with these license files,
not just the client DLL or the OGG files alone.

Restart the game after installing or replacing audio. In the F12 configuration,
`Audio / Announcer` enables playback and `Audio / AnnouncerVolume` controls its
volume independently of Tarkov's Interface slider by default. Overall game
volume and listener mute/pause still apply. `Audio / AnnouncerUseUiVolume`
defaults to false; set it to true only to restore the legacy Interface route
for compatibility. The independent output never changes other game sounds or
silently falls back to that route. Only arena matches
subscribe to the callouts and Tactical cues; normal raids do not use this
announcer. A Tactical cue plays when combat actually becomes live and when a
round is decided. The final protected 5v5 result uses Mission Accomplished or
Game Over instead of repeating an intermediate round result. Technical
no-contest exits are silent.

Clips begin loading asynchronously when Arena is selected in the native start
screen, with round lifecycle cues first. A callout that still occurs before its
clip has finished loading is skipped, without queueing a stale announcement.
When a round ends immediately after a kill callout, its win/loss cue waits for
the callout's actual duration plus a short gap so it does not cut the kill
phrase off. Rapid awards retain only one pending phrase, selected by priority,
with a four-second age limit. Round-start cues remain immediate; if a custom
callout outlasts the complete round aftermath, the stale result is skipped and
reported instead of leaking into the next round.
Once loaded, clips remain available for subsequent arena matches in that
process. Each match rechecks the cache and retries missing or unusable
recordings. Invalid clips
encountered during a match can also recover, with at least ten seconds between
retry attempts. A recovery does not replay the missed announcement.

Client logs include match attachment, loading results, and a summary of received,
requested and skipped announcements. Each skip reason is logged once per clip and match.
The first three playback requests, and later changes in output settings, include
the Interface/master mixer levels and listener state. A logged playback request
names the selected route (`Arena.Master` by default; `GUISounds.SingleUI.Primary`
for legacy compatibility). It means audio was submitted for playback, not that it was
audible on the player's device. These diagnostics do not change mixer settings.

The headless lifecycle regression is runnable from the repository root:

```powershell
dotnet run --project tools/AnnouncerChecks -c Release
```

It uses the real AnnouncerService with controlled game/audio stand-ins. It does
not replace an in-game listening test. Playback, mixer balance and subjective
voice quality still require a GPU-capable game client.
