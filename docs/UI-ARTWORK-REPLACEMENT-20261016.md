# Original categorical UI artwork

Date: 2026-10-16. The user chose fixed category icons after the
[replacement feasibility review](UI-ASSET-REPLACEMENT-REVIEW-20261016.md).
No per-weapon native icon rendering or asynchronous inventory previews were added.

## Scope and behavior

All eighteen current PNGs under `ArenaMode.Client/Assets/Ui` are replaced by
independently authored vector artwork: eight weapon classes, nine event symbols
and the full scoreboard's team banner. Weapon silhouettes are generic category
archetypes, not traced game sprites or renders of particular EFT assets.
Editable SVGs and the offline rasterizer live under `tools/ArenaUiArtwork`.

The enum, category mapping, final-damage-cause priority, text widths, colors,
score columns and positions remain unchanged. The confirmed-kill skull remains
above the crosshair and still requires a confirmed local kill. Headshot badges
are cleared for bleeding and barbed-wire deaths exactly as before.

Gun source textures are 128 x 64, event symbols 64 x 64, and the team banner
688 x 107. HUD display sizes remain 44 x 22 for weapon icons, 18 x 18 for small
symbols and 32 x 32 for the confirmed-kill skull. White foreground with alpha
lets existing team and danger tints work without a CPU pixel rewrite.
The existing bounded cache, failed-load caching, file-size/header checks and
owned sprite/texture disposal remain in place. No runtime graphics dependency
or new player setting is introduced.

## Sources and packaging

The new artwork is provided under MIT. `SOURCES.json` schema 2 records each
author, source SVG, transformation, PNG dimensions and hashes; `NOTICE.md` and
`MIT-LICENSE.txt` describe the grant. All three notices are embedded in the
client and copied as readable files under `ArenaMode/Ui` beside the client DLL.
The existing suppressible asset deployment target remains the only copy hook.

All eighteen retired PNG hashes are rejection fingerprints in the offline
checks. They contain no retired pixels. Replacement affects the current tree
and newly built client; old Git history, backups and prior packages still hold
the former assets. This task does not rewrite history, push or publish anything,
and does not establish redistribution rights for unrelated assets.

## Verification

Independent review covered all eighteen SVG sources, actual-size dark/light
mockups, source/PNG hashes, loader ownership, failure caching, final-cause
semantics and the deployment target. Two issues were corrected before acceptance:
WPF rounded white antialias edges to RGB254 (now normalized offline without
changing alpha), and raw SVG hashes needed canonical LF checkout rules.
A second offline render reproduces all eighteen PNGs and the manifest byte for
byte on this Windows/WPF version. Current PNG total: **19,907 bytes**.

- Actual RGBA decoding and provenance checks pass for all eighteen PNGs, with
  eight deliberately malformed image/manifest/license inputs rejected.
- Production-class checks pass **349 assertions**; with the actual staged DLL
  they pass **490 assertions**, including every embedded resource's exclusion
  of retired PNG hashes and all three readable notices. All **15 compiled
  regression controls** are rejected, followed by a clean production rerun.
- Unit suite: **969 passed, one opt-in APBS integration check skipped**.
- Eight adjacent consumers pass: RaidInfoPanel 368, NativeArenaHud 44,
  KillfeedText 120, NativeMenu 654, AreaTeamMenu 66, AreaMapPreview 4,424,
  ArenaAssembly 50 and SpectatorHealth 49 assertions. KillfeedText also rejects
  all six compiled faults. Its obsolete PFT calls were repaired separately in
  `7ae0810`, preserving the original four-companion fixtures and every assertion.
- Guarded staged client build passes with zero errors and the same four existing
  Shared nullable-context warnings. Installed client/server/Core were unchanged
  by the build. No ordinary deploy-capable product build was used.

Staged manifest:
`artifacts/double-jump-build/378f6a7e08ab46f2b3a9d089f9d6f203/manifest.json`.
It records `dcd1c5c` plus the then-uncommitted replacement source list. The
later test-fixture/documentation commits do not change the production inputs.
Client SHA256:
`428C390759C2A7E5F7A683506BB2DDC726188985B3D5BBB2FCB04075F2DD7F9C`.
Raw reports, review sheets and deployment/native evidence are ignored beneath
`artifacts/ui-art-replacement-20261016/`; the independent full DLL check is under
`artifacts/ui-asset-checks/bb0738d7d0534a9f82828ddeb27acc64/`.

The same staged client and three notices were subsequently deployed, with no
server/library/audio deployment. A fresh Factory Team DM run on SPT-GPU-2
verified real killfeed categories, local headshot/skull, both scoreboard team
banners and statistic symbols; two local kills, protected exit and config
restoration completed. See [native evidence and limits](GPU-UI-ARTWORK-20261016.md),
including the two logged startup profile repairs and unrelated trace errors.
Offline mockups/doubles are not substituted for this native observation.
