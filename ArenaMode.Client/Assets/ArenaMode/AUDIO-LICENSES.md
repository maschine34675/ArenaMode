# ArenaMode audio

## ElevenLabs Creator announcer pack (2026-09-12)

All **46 announcer recordings** are newly supplied by the user as
`alle_ansagen.ogg`, generated with **ElevenLabs Creator**, according to the user.
They replace all prior free-plan, TripleSnail and WARLORD announcer audio.
The original intake classified 18 existing cues and 28 reserve recordings.
Since 2026-09-14, 19 of those reserve recordings have explicit runtime bindings
(37 usable cues); four alternate results and five CTF recordings remain unused.
Their binary files and source/license record are unchanged; runtimeEventKey in
the manifest records the added binding separately from original intake usage.
See [ANNOUNCER-PACK.md](ANNOUNCER-PACK.md) for the ordered mapping and
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
they live in `sounds/reserve/` outside the runtime allowlist and are never
loaded. `airborne` replaces the former TRICKSHOT award label, whose recording is
the now-reserve `trickshot`.

The original 46 recordings and [ANNOUNCER-SOURCES.json](ANNOUNCER-SOURCES.json)
are byte-identical; the supplement keeps its own record in
[ANNOUNCER-ADDITIONS-20260917.json](ANNOUNCER-ADDITIONS-20260917.json), with the
source fingerprint, exact frame cuts and a hash per clip. Processing matches the
original pack: reviewed inter-phrase cuts, 5 ms fade-in, 10 ms fade-out, uniform
-1 dB gain, Ogg Vorbis quality 7 at the original 44.1 kHz stereo, no pitch,
speed, EQ, denoising or individual normalization. Boundaries were confirmed by
local offline transcription and waveform inspection rather than ASR word
timestamps; no listening test was performed. `tools/AnnouncerAssetAdditions/`
holds the splitter and the verifier that re-checks both records.

## Superseded announcer sources

[HISTORICAL-AUDIO-LICENSES.md](HISTORICAL-AUDIO-LICENSES.md) preserves the former
pack's attribution. `LOCAL-ANNOUNCER-*` and `WARLORD-ADDITIONS.json` are historical
records, explicitly superseded; their output hashes do not describe current OGGs.
The unchanged CC license texts are retained for this history. The old
`tools/Convert-WarlordAdditions.ps1` importer would overwrite seven current
Creator cues and must not be used to refresh this pack. Use the Creator splitter
and verifier documented in ANNOUNCER-PACK.md instead.

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
