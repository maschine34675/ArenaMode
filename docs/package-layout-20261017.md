# Test package layout v2

Date: 2026-10-17. This replaces the layout of the earlier
[trial archive](TEST-RELEASE-20261017.md), following the user's reorganized
example. It does not change source-asset bytes or install anything.

## Archive contract

Only `BepInEx/` and `SPT_Runtime/` occur at the ZIP root. There are no root
files and no `Optional/` directory.

```text
BepInEx/plugins/
  maschine-Redeploy.Core.dll
  ArenaMode/
    maschine-ArenaMode.Client.dll
    maschine-ArenaMode.BigBrainCompat.dll
    docs/
      INSTALL.txt
      README.md
      LICENSE
      REDEPLOY-CORE-LICENSE.txt
      THIRD-PARTY-NOTICES.md
      PACKAGE-MANIFEST.json
      AUDIO-LICENSES.md
      HUD-ICON-LICENSES.md
      CC-BY-3.0.txt
      ANNOUNCER-PACK.md
      ANNOUNCER-SOURCES.json
      ANNOUNCER-ADDITIONS-20260917.json
    sounds/
    hitmarkers/
    pickups/
    MapPreviews/
    Ui/
SPT_Runtime/user/mods/ArenaMode/
  maschine-ArenaMode.Server.dll
  config/kits.json
  config/ladder.json
```

Existing nested asset notices remain beside their assets. UI PNGs remain
embedded in the client; the readable UI notices are still in `Ui/`. Redeploy
Core stays flat to support its shared-library installation contract.

Installation instructions explicitly require removing legacy flat Arena client
and companion DLLs when updating. Keep one copy of each Arena DLL, and exactly
one Core DLL, throughout the plugin tree. Do not remove unrelated plugin folders,
overwrite customized catalogues without a backup, or downgrade a newer Core.

The companion is always included; absence of optional BigBrain must be handled
by the guarded runtime entry, not by moving its DLL out of the archive. Newer
BigBrain versions are attempted with a warning, not declared tested compatible.
The separate runtime checks and native acceptance boundaries still apply.

## Paths, provenance and build safety

- Package manifest schema is `2`, layout `spt-root-folders-v2`; paths are still
  relative to the ZIP root. Its inventory covers every file except itself.
- Copied Markdown links point to shipped files where available. Source-only
  references use the exact Arena commit. Invalid source references fail packaging.
- The announcer table explicitly makes its `sounds/...` paths relative to the
  parent ArenaMode folder. In the two packaged announcer JSON records, `file`
  becomes `../sounds/...`, relative to the relocated record. Clip fingerprints,
  cuts, rights and source manifests remain unchanged; audio is copied byte-for-byte.
- Unchanged Markdown and license texts are copied directly, preserving their
  existing recorded fingerprints. Only documents requiring link relocation are
  rewritten. The map-preview legal text is specifically checked byte-for-byte.
- Runtime assets stay in their prior `ArenaMode` subdirectories. The grouped
  client DLL requires `ArenaAssetPaths` so lookups do not append a second
  `ArenaMode/`. Six consumers cover announcer/hitmarker/pickup audio, pickup
  definitions, area drafts and pickup drafts. Flat development installs remain
  supported by that resolver; ordinary build deployment paths are not relocated.
- Before building an archived revision, the packager checks for the resolver,
  its six call sites, the soft BigBrain entry and its non-inlined typed boundary.
  Old revisions cannot be silently repackaged into this layout. These checks
  detect an incompatible baseline; they do not certify native behavior or replace
  the companion's tests. Refactoring these seams requires reviewing this guard.
- Builds still use isolated committed source snapshots and disable all deployment
  targets. The before/after installation snapshot explicitly watches both legacy
  flat and new grouped DLL paths, plus the existing recursive asset/server trees.

## Layout-only evidence

The first check used existing DLL bytes from the user's example with current
packaging code and source assets. It is deliberately named
`layout-fixture-not-a-release.zip`, under ignored `artifacts/package-layout-check-*`.
It is **not** a new DLL build, release candidate, deployment or native test.

- PowerShell and Python parser checks passed; scoped whitespace checks passed.
- Fixture: **88 files**, folder-only root, exactly four intended DLLs, complete
  hash inventory, both nonempty catalogues, **36 local document links** and
  **52 announcer provenance records** verified.
- Supplied map-preview legal text matches the prior package byte-for-byte.
- **13 archive negative controls rejected:** root README, old Optional subtree,
  flat Arena DLL, missing companion, old manifest schema, modified audio, broken
  relocated notice link, old announcer path base, wrong audio provenance hash,
  missing license, case-insensitive duplicate, path traversal and an unclassified
  root asset notice. Controls for document/provenance errors rebuilt the package
  inventory first, so those failures were not merely stale package hashes.
- Source-baseline guard accepted current code and rejected **four controls**:
  the old archived revision, a legacy audio-path caller, a hard BigBrain
  prerequisite and removal of the non-inlining boundary.

A newly committed source build and full package validation must be recorded
separately before distributing a replacement ZIP. No upload or deployment is
performed by either packaging tool.

## Runtime integration evidence

- The shared resolver covers both legacy flat and grouped DLL installations,
  preserving audio, `Pickups.json`, `AreaDrafts` and `PickupDrafts` destinations.
  Ten new unit cases exercise both layouts, case/trailing separators and invalid
  directories. Production announcer and hitmarker harnesses also load through
  the grouped location; the latter reads the actual packaged-source PCM files.
- Unit suite: **1026 passed, 2 skipped**. Announcer **335**, hitmarker **54**,
  pickups **410**, pickup objectives **545**, TeamArena expansion **895** checks.
- BigBrain remains a build-time reference, never a redistributed third-party
  DLL. Its runtime dependency is soft; the entry type has no BigBrain field,
  base type or method signature. Typed registration is behind `NoInlining`.
  Version 1.5.0 is the tested minimum; newer versions warn once, older/unloaded
  versions stay off. The Arena companion/client version match remains strict.
- The isolated-assembly harness covers missing BigBrain, versions 1.5.0,
  1.5.1 and 2.0, older/unloaded dependencies, real missing-method JIT failure,
  incorrect retained layer registrations, foreign ownership and teardown. See
  [harness procedure and limits](../tools/BigBrainCompatibilityChecks/README.md).
- Independent adversarial reviews found no blocking issue in the optional
  bootstrap, asset migration or packaging. BepInEx's installed loader was
  inspected: Cecil metadata discovery followed by loading the named entry type,
  not enumeration of all runtime types in the assembly. These source/CoreCLR
  checks do **not** establish native Mono or future BigBrain compatibility.
- Existing unrelated failure: `PolygonAreaChecks/Scope` stops at
  `inactive Arena marker cannot enter normal capture`. The same failure was
  reproduced using the unchanged `ArenaAreaCapture.cs` from baseline `fd314a8`.
  The only capture change here is its export directory; the scope/admission
  behavior was not changed. This harness is not reported as passed.
