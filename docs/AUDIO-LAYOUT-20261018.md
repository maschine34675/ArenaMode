# Audio and notice layout — 2026-10-18

The installed layout selected by the author is now the source, runtime, build
and ZIP layout too. Paths below are relative to `BepInEx/plugins/maschine-ArenaMode`.

```text
announcements/                    41 active OGG announcer cues
sounds/                           collect, headshot, hit and kill WAVs
docs/
  README.md, LICENSE, THIRD-PARTY-NOTICES.md
  licenses/
    CC0-1.0.txt, CC-BY-3.0.txt
    audio/
      AUDIO-LICENSES.md
      ANNOUNCER-SOURCES.json
      ANNOUNCER-ADDITIONS-20260917.json
      HITMARKER-SOURCES.json
      PICKUP-SOURCES.json
    ui/                           combined combat/equipment icon credits
    map-previews/                 unchanged Customs source and attribution
```

The source repository additionally retains eleven unused recordings under
`announcements/reserve/`; new build/deploy/release payloads exclude them. The
explicit runtime reserve fallback for previously promoted cues stays inside
`announcements` and does not scan or play the unused recordings.

No audio was converted or amplified. All 52 source OGGs and four WAVs have the
same bytes as before the move. The two announcer records keep their distinct
intake/rights statements, frame cuts, hashes and original event classification.
Source announcer paths remain asset-root-relative; packaged records use
document-relative paths. WAV and CC0 references are document-relative in both.

`ANNOUNCER-PACK.md`, the pickup README and the earlier audio guide are combined
in `audio/AUDIO-LICENSES.md`. Equipment credits join the UI notice. Identical
CC0 and CC BY full texts each ship once. Arena and Core's identical MIT notice
also ships once as `docs/LICENSE`; the writer rejects a changed Core notice
instead of silently dropping it. The different UI MIT notice remains separate.
Customs' separately licensed source/provenance is unchanged.

Deployment uses exact owned-file aliases and archives obsolete copies outside
plugin discovery. Unknown sounds, notes, area/pickup drafts and layouts are not
deleted or relocated. Lock, directory-collision and junction checks still run
before the deployer writes any DLL. Fresh package layout is `spt-root-folders-v5`.

Offline checks: 56 baseline audio byte identities; 52 decoded OGGs and four WAV
hash/format checks; announcer 337 assertions and its full mutation suite;
hitmarker 54, pickup 436 and its fault controls; team/asset expansion 895;
deployment layout 80 and inventory/helper 388. UI source suite, fourteen artwork
invalid-input controls and sixteen compiled controls pass. Solution build passes;
unit suite has 1,167 passes and two optional environment skips. No GPU playback
acceptance is implied by these offline checks; native and final ZIP evidence is
recorded separately after deployment.
