# Local announcer trial - elevenlabs.io

2026-09-11: the user supplied `D:\SPT41\Development\ansagen2.wav`, generated
with the free ElevenLabs plan, to audition five announcements in game.
This is a local non-commercial trial, not a replacement for release assets.
No open-content license is asserted for these recordings.

## Cue mapping

| Spoken line | Installed file in `BepInEx/plugins/ArenaMode/sounds/` | Existing event |
| --- | --- | --- |
| DOUBLE KILL | `double_kill.ogg` | Two kills in the current chain |
| ULTRA KILL | `multi_kill.ogg` | Four kills; HUD continues to say MULTI KILL |
| MONSTER KILL | `monster_kill.ogg` | Five or more kills in the chain |
| UNSTOPPABLE | `unstoppable.ogg` | 20-kill streak |
| GODLIKE | `godlike.ogg` | 25-kill streak, then 35, 45, etc. |

There is no separate ULTRA KILL event. Using its recording for MULTI KILL lets
all five recordings play without changing scoring, HUD labels or the DLL.
Existing announcement priority and suppression still apply: a streak milestone
can supersede the chain callout, so the fifth kill of a fresh life can announce
KILLING SPREE instead of MONSTER KILL. Other sounds remain unchanged.

## Files and conversion

- Original SHA256:
  `B41440907BA52AFAAE4FA3D5434182FDCB8C9D1226987922A93629327225849A`.
- Original format: 44.1 kHz stereo PCM16, 8.495420 seconds.
- Order follows the user's five-line list; four inter-phrase gaps were measured
  with FFmpeg `silencedetect=noise=-26dB:d=0.10` and the waveform inspected.
  This is not a speech-recognition or in-game listening validation.
- Cuts in source seconds: 0-1.46, 1.57-3.15, 3.31-5.06, 5.20-6.92,
  7.02-end. Small pause portions are omitted; word onsets and trailing ambience
  are retained. Five-millisecond fade-in and ten-millisecond fade-out soften
  the cut boundaries.
- Converted to Ogg Vorbis quality 5, retaining stereo, sample rate and gain.
  No loudness normalization, pitch changes or other effects. Each OGG decodes
  with the expected duration, finite audible signal and peak below full scale.
- Metadata titles include `elevenlabs.io` and mark the files as local trials.

Regenerate locally (Python 3 plus FFmpeg; verifies the original hash):

```powershell
python tools/AnnouncerChecks/split_local_trial.py 'D:\SPT41\Development\ansagen2.wav'
```

Outputs and source/output hashes: `artifacts/local-announcer-trial/ansagen2/`.
This directory is Git-ignored and is outside the project's packaged assets glob.
Neither the original WAV nor generated OGGs are committed or shipped in releases.
The intake script never installs files or modifies release assets.

## Installation and rollback

Local delivery copies only these five OGGs into the installed `ArenaMode/sounds/`
directory. `LOCAL-ANNOUNCER-TRIAL.md` and `LOCAL-ANNOUNCER-SOURCES.json` beside
that directory identify these overrides; the standard `AUDIO-LICENSES.md` still
documents the release set and does not license the five local overrides.
Receipt and backups: `artifacts/local-announcer-trial/ansagen2/delivery/`.

**Restart the game** after installing or reverting. Unlike hitmarker WAVs, healthy
announcer PCM stays cached across matches. No server restart or DLL swap needed.

A normal client build copies the release assets and can restore the original
double_kill, unstoppable and godlike files. It does not remove the extra
multi_kill and monster_kill files. Reapply all five trial OGGs after a deploying
build if continuing this audition. To return fully to the release set, restore
the backed-up original files, remove trial-only files listed as absent-before in
the receipt, and remove the two local-trial notices. Do not delete unrelated
audio files or copy all files from a backup indiscriminately.

## Licensing boundary

ElevenLabs' [official publishing guidance](https://help.elevenlabs.io/hc/en-us/articles/13313564601361-Can-I-publish-the-content-I-generate-on-the-platform),
checked 2026-09-11, limits free-plan generations to non-commercial use and
requires attribution when publishing; its current guidance asks for
`elevenlabs.io` or `11.ai` in the title. A later paid subscription does not
retroactively grant commercial rights to these free-plan generations.
Any later release needs a separate licensing/voice-rights decision. These
local overrides must not be silently imported into the redistributable packs
or described as covered by TripleSnail's/Warlord's Creative Commons licenses.
