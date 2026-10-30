# Announcer package maintenance — 16 October 2026

This is private repository maintenance. No public repository, release, visibility
change or game deployment is part of this work. The preceding report-only cleanup
was separately committed and pushed as `9f51e8e` to the existing private remote.

## Current licenses and provenance

Five obsolete files were removed from `ArenaMode.Client/Assets/ArenaMode`:
`HISTORICAL-AUDIO-LICENSES.md`, `LOCAL-ANNOUNCER-TRIAL.md`,
`LOCAL-ANNOUNCER-SOURCES.json`, `WARLORD-ADDITIONS.json`, `CC-BY-SA-4.0.txt`.
They described replaced announcer recordings. All twelve output fingerprints in
the old trial/WARLORD records and all eighteen OGGs immediately before Creator
import differ from the current fifty-two OGG files. Original notices remain with
their corresponding old audio in private Git history; no rights were changed.

The current Creator manifests and source/rights caveats remain. CC BY 3.0 and
HUD-ICON-LICENSES remain required for Skoll's embedded armor/helmet icons. Both
nested CC0 legal texts and provenance records remain with the pickup/hitmarker
effects. The separate Customs map license and original-game UI notices were
not changed. A private source push is not public redistribution clearance.

The audio guide now records the existing +12 dB pickup gain and dedicated
NO CONTEST cue; the pack guide's relative link to the repository review is fixed.

## Nineteen active cues promoted

The nineteen files already wired through `runtimeEventKey` now live directly
under `sounds/`, rather than `sounds/reserve/`. All fifty-two OGGs retain their
original bytes and cuts. There are forty-one used root files and eleven unused
reserve alternatives/CTF/supplement lines. The alternate `round_won` and
`round_lost` recordings remain in reserve and never replace the chosen results.

`file` in ANNOUNCER-SOURCES records the current path. Its `usage` and `eventKey`
retain the original 18-active/28-reserve intake classification; `runtimeEventKey`
still identifies the nineteen subsequently enabled cues. The cut plan and the
supplement verifier's independently pinned original-manifest hash are updated
for these path-only changes. No audio checksum or recording was redefined.

The runtime still loads root OGGs without scanning subdirectories recursively.
`LegacyReserveKeys` retains the explicit nineteen-name fallback for older
installations. Root files win once even if a copy-only upgrade leaves all old
reserve copies behind. No triggers, event keys, priorities or volume settings
changed. Existing installed files are untouched; a later packaging/deploy step
may remove only those known obsolete copies after verification.

## Verification

- Announcer lifecycle/loader checks: 333 assertions, including all nineteen root
  files winning over simultaneous legacy copies.
- Team expansion checks: 893 assertions; manifest/hash/loaded-path negative
  controls still reject missing, duplicated, misrouted and unsupported cues.
- Creator metadata regression: eight metadata and eleven inventory faults
  rejected. Supplement regression: five faults rejected.
- All fifty-two clips decoded and their hashes/format/duration checked; nineteen
  destinations changed, no audio content changed. Four current effect WAVs and
  both Skoll icon pairs retain their recorded hashes.
- Isolated Client/Server/BigBrain build at `cfdaef7`: zero errors; four existing
  Client and two Server warnings in unchanged files. All seventy-four staged
  assets match source bytes (sixty-nine audio/notice files plus five separate
  map-preview files). Installed DLL hashes remained unchanged. See also the
  [unused-code review](DEAD-CODE-REVIEW-20261016.md).

Private baseline inventory, checks and staged-build evidence live under
`artifacts/asset-maintenance-20261016/`. Offline checks do not replace native
listening tests. No live listening or GPU test was performed for this cleanup.
