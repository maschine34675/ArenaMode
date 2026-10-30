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

## Deployment and package evidence

Runtime source: `b67b2dd27dec1fdbcb8a30e740d700f1bbeeb8bc`. Both client
assemblies were rebuilt without automatic deployment, then deployed together to
the local installation and GPU2. Before/after inventories verified all 116 local
and 134 GPU2 records, permitting only the documented owned changes/migrations.
Core, Server and other installed DLLs were unchanged. Retired documents remain
recoverable in `BepInEx/backups/maschine-ArenaMode-layout`, outside plugin discovery.

| Installed assembly | SHA256 |
|---|---|
| Client | `FCA91E5FEAB8FB004AE48A7096ACE706E946AEA3689C0FF8D2344FE7455B87A5` |
| BigBrainCompat | `9905563A43712071F4F938CF21A8AC9DE0EF21F2BD8E65204DFA5BBE68F3F521` |

Final embedded-resource/readable-notice check: **517 assertions passed** against
that Client. Both installations pass the 41-cue hash/provenance comparison.
The installed-audio verifier also accepts preserved custom/reserve extras while
rejecting a missing owned cue; exact fresh-package contents remain a separate check.

The isolated ZIP `ArenaMode-0.1.0-test-b67b2dd.zip` passed the package verifier:
71 files, four DLLs, 41 OGGs, four WAVs, 32 resolvable local documentation links,
no root files and no reserve audio. SHA256:
`e5828a962ba89e9a32de8bc15fd6fb62f857764b302ebbf5f0c889bc41654f3c`.
All 63 shared asset/document files match the installation; two MIT legal texts
differ only in Git checkout line endings. The ZIP builds matching Core from the
current Redeploy source, without replacing the installed Core. It was not uploaded.

Ignored evidence: `artifacts/audio-layout-20261018/`,
`artifacts/ui-asset-checks/660f12ec2f6542b2a7494421577df057/`, and
`artifacts/test-releases/613c66684db64a13a35b023ebc1e219c/`.

## Bounded GPU2 check

Two short Factory Deathmatch entries used the test profile. UiBridge confirmed
VM, host-disk and loaded identities for the deployed pair. Announcer loading
reported **41 loaded, zero failed**; the following match reused all 41. Hitmarker
attachment reported **3/3** clips. `prepare_to_fight` created real 44.1 kHz stereo
PCM and was submitted to the independent output. No audio-load error was observed.
This proves native loading/submission, not audibility on a listener's speakers.
No real hitmarker playback or collected-pickup cue was confirmed in these short
entries; those event/path checks remain offline evidence. In particular, do not
describe `collect.wav` as natively played.

A separate placement finding occurred on the first entry at approximately
`(12.611, 1.182, 60.104)`: all six Factory pickups were skipped after the
15-second/15-attempt window (21:01:46 local time). Each final slot attempt had
149 incomplete-path refusals, 25 missing-navigation-floor refusals and 90
obstructed-pedestal refusals. `ArenaPickupGeometry` still requires a complete
native path from the local actor; that gameplay code was not changed here.
This does not establish the exact obstacle responsible. The second entry at
approximately `(2.411, -2.640, 49.400)` placed all six on its first attempt
(21:05:23). Follow up the first spawn/navigation case separately; do not loosen
pickup geometry to make this audio test pass.

Both raids were left normally. Native readbacks and filtered audio/placement logs
are retained in `artifacts/audio-layout-20261018/native/runtime-evidence.json`.
The server recorded protected snapshot restoration for both exits; the test
inventory remained unchanged. EFT and the SPT server were then stopped, and the
original Arena configuration and test-profile bytes were restored and hash-checked.
GPU2 is free again. No personal profile was used and no GitHub update was made.
