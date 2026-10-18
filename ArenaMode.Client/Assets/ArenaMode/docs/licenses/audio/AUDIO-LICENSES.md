# ArenaMode audio

## Pickup sound (2026-10-07)

`sounds/collect.wav` uses **Game Pick Up** by **IENBA**, published on
[Freesound](https://freesound.org/people/IENBA/sounds/762911/) under **CC0 1.0**
(source page checked 2026-10-07). Redistribution and adaptation are permitted;
no endorsement or warranty is implied. The user supplied an already shortened
1.399206-second excerpt of the public compilation. Arena retains its first
0.600 seconds, averages stereo to mono PCM16/44.1 kHz and fades the final 10 ms.
The removed section is trailing silence. On 2026-10-08 the packaged PCM received
a constant +12 dB gain, without clipping, compression, normalization or pitch change.
Source/output hashes and processing are in [PICKUP-SOURCES.json](PICKUP-SOURCES.json);
the unchanged [CC0 legal text](../CC0-1.0.txt) ships alongside the cue.

Peak: 26207 / 32768, about -1.94 dBFS. No clipped samples; no compression or
limiter. The existing final 10 ms fade and ending zero sample are retained.

To repeat the gain step, supply the converted WAV identified by
`preGainSha256` to `node tools/PickupAudioChecks/amplify.mjs input.wav output.wav 12`.
The tool refuses gains that would clip, before writing the output.

## ElevenLabs Creator announcer pack (2026-09-12)

All **46 announcer recordings** are newly supplied by the user as
`alle_ansagen.ogg`, generated with **ElevenLabs Creator**, according to the user.
They replace all prior free-plan, TripleSnail and WARLORD announcer audio.
The original intake classified 18 existing cues and 28 reserve recordings.
Since 2026-09-14, 19 of those reserve recordings have explicit runtime bindings
(37 usable cues); four alternate results and five CTF recordings remain unused.
The 19 promoted recordings joined the active folder on 2026-10-16; since
2026-10-18 every active announcer cue lives in `announcements/`. Audio bytes,
cuts and rights are unchanged. `usage`/`eventKey` retain the historical intake
classification, while `runtimeEventKey` identifies the added bindings and `file`
records the current source path. Together with the supplement, all 41 used
recordings are directly inside `announcements/`. The 11 unused recordings remain in the source
repository's reserve folder and are excluded from build/deploy/release payloads.
Packaged manifests list only the 41 shipped recordings; the source manifests
retain the full recording history. Existing local reserve files are not removed.
See the cue inventory below and
[ANNOUNCER-SOURCES.json](ANNOUNCER-SOURCES.json) for hashes and exact cuts.

The user supplied these paid-plan recordings for distribution with ArenaMode.
ElevenLabs' [publishing guidance](https://help.elevenlabs.io/hc/en-us/articles/13313564601361-Can-I-publish-the-content-I-generate-on-the-platform),
checked 2026-09-12, permits commercial use of output generated during a paid
subscription, including after it ends, subject to applicable terms and necessary
input/voice rights. Beta Services are excluded. This relies on the user's Creator
generation statement; the specific voice/model was not supplied or independently
verified. The [EEA/UK/Swiss terms](https://elevenlabs.io/terms-of-use-eu) and
[other-region terms](https://elevenlabs.io/terms-of-use) govern the applicable
account. No Creative Commons/public-domain grant or unrestricted standalone
asset-reuse license is asserted, and no public release is performed by this import.
The earlier free-plan recordings retain their old restrictions in Git history;
they have been replaced, not retroactively relicensed.

Processing: split in reviewed pauses; 5 ms fade-in and 10 ms fade-out; uniform
-1 dB gain to leave encoding headroom; Ogg Vorbis quality 7, original 44.1 kHz
stereo retained. No pitch/speed/EQ/denoising or individual loudness normalization.
Titles identify each spoken line and metadata points to this provenance record.

## Creator announcer supplement (2026-09-17)

Six further recordings were supplied by the user as `alle_ansagen2.ogg`, in the
same workflow and on the same stated Creator account; no separate generation
plan or voice/model statement was given for this file, and no account
verification was performed. The rights position and the region-specific terms
above therefore apply unchanged, and nothing here asserts a Creative Commons,
public-domain or standalone asset-reuse grant.

Four are wired: `no_scope`, `airborne`, `no_contest` and `embarrassing`. Two —
`trickshot` and `air_superiority` — are recorded but deliberately not wired, so
they live in the source repository's `announcements/reserve/`, are excluded from
packages, and are outside the runtime allowlist. They are never
loaded. `airborne` replaces the former TRICKSHOT award label, whose recording is
the now-reserve `trickshot`.

The original 46 recordings remain byte-identical; their manifest now reflects
the current source paths. The supplement keeps its own record in
[ANNOUNCER-ADDITIONS-20260917.json](ANNOUNCER-ADDITIONS-20260917.json), with the
source fingerprint, exact frame cuts and a hash per clip. Processing matches the
original pack: reviewed inter-phrase cuts, 5 ms fade-in, 10 ms fade-out, uniform
-1 dB gain, Ogg Vorbis quality 7 at the original 44.1 kHz stereo, no pitch,
speed, EQ, denoising or individual normalization. Boundaries were confirmed by
local offline transcription and waveform inspection rather than ASR word
timestamps; no listening test was performed. `tools/AnnouncerAssetAdditions/`
holds the splitter and the verifier that re-checks both records.

## Superseded announcer sources

No TripleSnail, WARLORD or former free-plan recording remains in this package.
Their obsolete notices and manifests are no longer shipped; the Git
history retains the original files and their own rights records, without
retroactively changing their licenses. HUD and combat-icon attribution is collected separately in the
[UI notice](../../../../Ui/NOTICE.md).
Use only the Creator splitter and verifier documented below to refresh the pack.

## Shared hitmarker sound (2026-09-11)

The three hitmarker effects in `sounds/` use **Hitmarker Sound Effect** by
**User391915396**, published on Freesound on 2021-05-04.

- Source: <https://freesound.org/people/User391915396/sounds/570335/>.
- License on the source page: **CC0 1.0 Universal**, checked 2026-09-11.
  Redistribution and adaptation are permitted under CC0; credit is optional.
  No endorsement or warranty is implied. The uploader describes processing
  recording noise into this effect; this is not an attribution to Call of Duty.
- License: <https://creativecommons.org/publicdomain/zero/1.0/>.
- Unchanged legal text: [CC0-1.0.txt](../CC0-1.0.txt), downloaded from
  <https://creativecommons.org/publicdomain/zero/1.0/legalcode.txt>.
- User-supplied original: `570335__user391915396__hitmarker-sound-effect.wav`,
  48 kHz stereo PCM24, 9,000 frames (187.5 ms).
- [HITMARKER-SOURCES.json](HITMARKER-SOURCES.json) records the source, source/output/license
  hashes, original format, encoder version and exact modifications.

| Cue | Bundled WAV | Duration |
| --- | --- | --- |
| Hit | `sounds/hit.wav` | 187.506 ms |
| Headshot | `sounds/headshot.wav` | 187.506 ms |
| Kill | `sounds/kill.wav` | 187.506 ms |

The three files are byte-identical, as requested after the user's audition.
Conversion averages the stereo channels (0.5 L + 0.5 R), resamples to 44.1 kHz
and encodes signed PCM16 WAV for the existing loader. No trimming, normalization,
fades, pitch changes, splicing or added effects. Duration differs only by rounding
to a whole output frame. Cue priorities and the independent volume control remain.

Reproduce with `python tools/HitmarkerAudioChecks/import_assets.py <original.wav>`
(explicit local intake plus FFmpeg, never run by builds). Validate offline with
`python tools/HitmarkerAudioChecks/verify_assets.py`. Keep the audio folders and the accompanying `docs/licenses` records together; these assets are independent of the announcer and code
licenses. The previous Kenney examples have been replaced; Git retains history.

## Installation and playback

Install the complete `BepInEx/plugins/maschine-ArenaMode/` folder. Build,
deploy and release ZIPs use the same layout: `announcements/*.ogg` for voices,
`sounds/{hit,headshot,kill,collect}.wav` for short effects, and this documentation
under `docs/licenses/audio/`. Paths in the cue tables below are relative to the
mod folder. WAV manifests are relative to their own document; source announcer
manifests identify the asset root, while packaged announcer manifests are
rewritten to document-relative paths. Audio bytes are unchanged by relocation.

Restart the game after installing or replacing audio. In the F12 configuration,
`Audio / Announcer` is the master switch for playback and for the on-screen kill
callouts (DOUBLE KILL, ...), and each mode's own `Announcer` entry (the ANNOUNCER
mutator in the map-screen menu) gates both as well; HUD text such as "WAVE n" is
a separate system that neither switch affects. `Audio / AnnouncerVolume` controls the
volume independently of Tarkov's Interface slider by default. Overall game
volume and listener mute/pause still apply. `Audio / AnnouncerUseUiVolume`
defaults to false; set it to true only to restore the legacy Interface route
for compatibility. The independent output never changes other game sounds or
silently falls back to that route. Only arena matches
subscribe to the callouts and Tactical cues; normal raids do not use this
announcer. A Tactical cue plays when combat actually becomes live and when a
round is decided. The final protected 5v5 result uses Mission Accomplished or
Game Over instead of repeating an intermediate round result. Technical
no-contest exits use the dedicated NO CONTEST recording.

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

Diagnostic client logs include match attachment, loading results, and a summary of received,
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

## Announcer cue inventory and reproduction

Slicing uses pause midpoints cross-checked against the waveform and a local
base.en Whisper transcript. The input was not uploaded for transcription.
ASR word timestamps alone are too imprecise to cut effects/reverb; they were not
used as cut points. ROUND WON was recognized phonetically as "round one";
its label follows the user's supplied list. Fade edges are only 5/10 ms; the
original voice/effects and relative levels are retained with uniform -1 dB
encoding headroom. 44.1 kHz stereo Vorbis q7 minimizes another encoding loss.

| # | Spoken line | File (relative to the mod folder) | Usage |
| --- | --- | --- | --- |
| 1 | DOUBLE KILL | `announcements/double_kill.ogg` | active |
| 2 | TRIPLE KILL | `announcements/triple_kill.ogg` | active |
| 3 | ULTRA KILL | `announcements/multi_kill.ogg` | active |
| 4 | MONSTER KILL | `announcements/monster_kill.ogg` | active |
| 5 | KILLING SPREE | `announcements/killing_spree.ogg` | active |
| 6 | RAMPAGE | `announcements/rampage.ogg` | active |
| 7 | DOMINATING | `announcements/dominating.ogg` | active |
| 8 | UNSTOPPABLE | `announcements/unstoppable.ogg` | active |
| 9 | GODLIKE | `announcements/godlike.ogg` | active |
| 10 | FIRST BLOOD | `announcements/first_blood.ogg` | active |
| 11 | HEADSHOT | `announcements/headshot.ogg` | active |
| 12 | PAYBACK | `announcements/payback.ogg` | active |
| 13 | LAST MAN STANDING | `announcements/last_man_standing.ogg` | active |
| 14 | ROUND START | `announcements/round_start.ogg` | active |
| 15 | ROUND WINNER | `announcements/round_won.ogg` | active |
| 16 | MISSION FAILED | `announcements/round_lost.ogg` | active |
| 17 | MISSION ACCOMPLISHED | `announcements/match_won.ogg` | active |
| 18 | GAME OVER | `announcements/match_lost.ogg` | active |
| 19 | PREPARE TO FIGHT | `announcements/prepare_to_fight.ogg` | active (promoted from reserve) |
| 20 | THREE | `announcements/three.ogg` | active (promoted from reserve) |
| 21 | TWO | `announcements/two.ogg` | active (promoted from reserve) |
| 22 | ONE | `announcements/one.ogg` | active (promoted from reserve) |
| 23 | TAKEN THE LEAD | `announcements/taken_the_lead.ogg` | active (promoted from reserve) |
| 24 | LOST THE LEAD | `announcements/lost_the_lead.ogg` | active (promoted from reserve) |
| 25 | TIED FOR THE LEAD | `announcements/tied_for_the_lead.ogg` | active (promoted from reserve) |
| 26 | ONE MINUTE REMAINING | `announcements/one_minute_remaining.ogg` | active (promoted from reserve) |
| 27 | THIRTY SECONDS REMAINING | `announcements/thirty_seconds_remaining.ogg` | active (promoted from reserve) |
| 28 | TEN SECONDS REMAINING | `announcements/ten_seconds_remaining.ogg` | active (promoted from reserve) |
| 29 | FIVE KILLS REMAINING | `announcements/five_kills_remaining.ogg` | active (promoted from reserve) |
| 30 | ONE KILL REMAINING | `announcements/one_kill_remaining.ogg` | active (promoted from reserve) |
| 31 | MATCH POINT | `announcements/match_point.ogg` | active (promoted from reserve) |
| 32 | FINAL ROUND | `announcements/final_round.ogg` | active (promoted from reserve) |
| 33 | FINAL WEAPON | `announcements/final_weapon.ogg` | active (promoted from reserve) |
| 34 | LADDER COMPLETE | `announcements/ladder_complete.ogg` | active (promoted from reserve) |
| 35 | DRAW | `announcements/draw.ogg` | active (promoted from reserve) |
| 36 | SHUTDOWN | `announcements/shutdown.ogg` | active (promoted from reserve) |
| 37 | CLUTCH | `announcements/clutch.ogg` | active (promoted from reserve) |
| 38 | ROUND WON | `announcements/reserve/round_won.ogg` | reserve |
| 39 | ROUND LOST | `announcements/reserve/round_lost.ogg` | reserve |
| 40 | VICTORY | `announcements/reserve/victory.ogg` | reserve |
| 41 | DEFEAT | `announcements/reserve/defeat.ogg` | reserve |
| 42 | YOUR FLAG HAS BEEN TAKEN | `announcements/reserve/your_flag_has_been_taken.ogg` | reserve |
| 43 | ENEMY FLAG TAKEN | `announcements/reserve/enemy_flag_taken.ogg` | reserve |
| 44 | FLAG DROPPED | `announcements/reserve/flag_dropped.ogg` | reserve |
| 45 | FLAG RETURNED | `announcements/reserve/flag_returned.ogg` | reserve |
| 46 | FLAG CAPTURED | `announcements/reserve/flag_captured.ogg` | reserve |

### Supplement cue inventory

Six further phrases from the user's `alle_ansagen2.ogg` (8.105351 seconds), kept
in their own record so the 46 above stay byte-identical. Rights and processing:
[ANNOUNCER-ADDITIONS-20260917.json](ANNOUNCER-ADDITIONS-20260917.json).

| # | Phrase | File | Usage |
|---|---|---|---|
| 1 | TRICKSHOT | `announcements/reserve/trickshot.ogg` | reserve |
| 2 | NO SCOPE | `announcements/no_scope.ogg` | active |
| 3 | AIRBORNE | `announcements/airborne.ogg` | active |
| 4 | AIR SUPERIORITY | `announcements/reserve/air_superiority.ogg` | reserve |
| 5 | NO CONTEST | `announcements/no_contest.ogg` | active |
| 6 | EMBARRASSING | `announcements/embarrassing.ogg` | active |

The four active files raise the loadable set to **41**. `AIRBORNE` is the award
label for an airborne hip-fire kill, which was previously called TRICKSHOT and
had no recording; `NO SCOPE` replaces its provisional GODLIKE stand-in.
`NO CONTEST` is a result cue for a technical end and cancels any queued victory.
`EMBARRASSING` accompanies the own-hand-grenade death text. The two reserve
phrases stay outside the runtime whitelist and are never loaded.

```powershell
python tools/AnnouncerAssetAdditions/split_additions.py 'D:\SPT41\Development\alle_ansagen2.ogg'
python tools/AnnouncerAssetAdditions/verify_additions.py --assets artifacts/announcer-additions-20260917/pack --negative-controls
python tools/AnnouncerAssetAdditions/verify_additions.py --negative-controls
```

### Reproduction and verification

From the repository root, with Python 3 and FFmpeg/ffprobe on PATH:

```powershell
python tools/AnnouncerChecks/split_creator_pack.py 'D:\SPT41\Development\alle_ansagen.ogg'
python tools/AnnouncerChecks/verify_creator_pack.py --assets artifacts/creator-announcer/pack
python tools/AnnouncerChecks/verify_creator_pack.py
```

The splitter validates the source SHA256 and writes only to ignored
`artifacts/creator-announcer/pack/`. Its adjacent `creator_cuts.json` pins all
reviewed labels, paths and frame boundaries. Review generated clips and copy them
with ANNOUNCER-SOURCES.json into tracked assets. The manifest's `file` is the
current location; `usage`/`eventKey` keep the original 18/28 intake classification,
with `runtimeEventKey` marking the 19 later-enabled recordings. Vorbis/Ogg container bytes can
vary between encoder versions/runs; the generated manifest records the actual
output hashes. Do not regenerate during normal builds.

Offline verification decodes every clip and checks hashes, format, finite/nonzero
signal, peak headroom, duration, source coverage, metadata and active/reserve sets.
Optional `--deployment <asset-folder>` checks the 41 shipped recordings and their
per-file provenance; it does not require source-only reserve recordings.
The checks do not certify subjective audio quality or native in-game playback.

**Restart the game after updating**: healthy announcer PCM is cached across
matches. Enabling the additional triggers also requires the updated client DLL; no server change is needed.
Use F12 Audio / AnnouncerVolume for the independent voice volume.
Backups and delivery evidence for this intake: ignored
`artifacts/creator-announcer/`; the original combined OGG stays outside Git.
