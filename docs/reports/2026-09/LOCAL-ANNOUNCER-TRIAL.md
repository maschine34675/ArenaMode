> **Historical record — superseded 2026-09-12.** All five free-plan OGGs
> described below were replaced by new recordings generated under Creator.
> This document records the former selection and is not the current deployment
> or license manifest. See `ArenaMode.Client/Assets/ArenaMode/ANNOUNCER-PACK.md`
> in the repo, or `ANNOUNCER-PACK.md` beside this file in the installed asset pack.
> Old output hashes and licensing restrictions apply only to the old recordings.

# Development announcer recordings - elevenlabs.io

2026-09-11: the user supplied `D:\SPT41\Development\ansagen2.wav`, generated
with the free ElevenLabs plan, to audition five announcements in game.
On 2026-09-12 the user explicitly requested tracking all five OGGs in the repo
after a normal build restored the old sounds. The five recordings now live in
`ArenaMode.Client/Assets/ArenaMode/sounds/` and are copied by normal build/publish
and deployment. Their scope remains non-commercial development/testing; no
open-content license or commercial rights are asserted for these recordings.

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

(The helper was removed on 2026-10-23; the trial recording no longer exists at that path.)

The splitter still writes its intermediate outputs and hashes to the ignored
`artifacts/local-announcer-trial/ansagen2/` directory and never installs files.
The five reviewed OGGs have now been copied unchanged into the tracked asset
directory. `LOCAL-ANNOUNCER-SOURCES.json` beside `sounds/` records their source,
output hashes and attribution. The original combined WAV remains outside Git.
Future regeneration requires explicit review/copy of the new outputs and record.

## Installation and rollback

Normal builds now deploy all five recordings from the repo. The packaged
`AUDIO-LICENSES.md`, `LOCAL-ANNOUNCER-TRIAL.md` and `LOCAL-ANNOUNCER-SOURCES.json`
identify their origin and restrictions independently of TripleSnail/WARLORD.
Initial local-only receipt/backups (2026-09-11):
`artifacts/local-announcer-trial/ansagen2/delivery/`.
Repo promotion/delivery evidence (2026-09-12): `artifacts/announcer-repo-promotion/`.

**Restart the game** after installing or reverting. Unlike hitmarker WAVs, healthy
announcer PCM stays cached across matches. No server restart or DLL swap needed.

The old need to reapply recordings after each build is resolved. To restore the
earlier pack, restore the original double_kill, unstoppable and godlike OGGs
from Git history/backups in both repo and installation, remove only the added
multi_kill/monster_kill files in both locations, and update the attribution/source
documents. A normal copy-based build does not delete files removed from source.

## Licensing boundary

ElevenLabs' [official publishing guidance](https://help.elevenlabs.io/hc/en-us/articles/13313564601361-Can-I-publish-the-content-I-generate-on-the-platform),
checked 2026-09-12, limits free-plan generations to non-commercial use and
requires attribution when publishing; its current guidance asks for
`elevenlabs.io` or `11.ai` in the title. A later paid subscription does not
retroactively grant commercial rights to these free-plan generations.
Any later public release needs a separate licensing/voice-rights decision.
These recordings are now included in build/publish output at the user's request;
that does not grant commercial use or make them covered by TripleSnail's/Warlord's
Creative Commons licenses. This change does not publish the mod or audio.
