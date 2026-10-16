# Four game-icons.net replacements — 2026-10-16

User-selected headshot, melee, grenade and barbed-wire images replace the four
project-original geometric symbols. The other fourteen PNGs, including every
generated weapon picture, retain their exact bytes. Runtime layout, sizing,
tint, cause selection, headshot/kill rules and caching are unchanged.

| Arena resource | Upstream title / author | Original page |
|---|---|---|
| headshot | Skull crack / Lorc | https://game-icons.net/1x1/lorc/skull-crack.html |
| melee | Bowie knife / Skoll | https://game-icons.net/1x1/skoll/bowie-knife.html |
| grenade | Grenade / Lorc | https://game-icons.net/1x1/lorc/grenade.html |
| barbed-wire | Barbed wire / Lorc | https://game-icons.net/1x1/lorc/barbed-wire.html |

All four primary pages identify CC BY 3.0. The raw white/transparent SVGs are
stored unchanged in `tools/ArenaUiArtwork/sources/`, using Arena's existing
resource names. The offline WPF packer scales the whole 512x512 viewBox to 64x64;
it does not crop or alter paths. Alpha and neutral white pixels preserve the
existing orange/red/white runtime tints. Headshot is shared with the scoreboard;
the general kill-confirmation skull stays unchanged.

`NOTICE.md` credits title/author/source and conversion, explicitly excludes
these four works from the project-original MIT grant, and links CC BY 3.0.
The full official license is shipped and embedded as `CC-BY-3.0.txt` alongside
the existing three notices. `SOURCES.json` records exact download/page URLs,
title/author/license, raw SVG hashes and packed PNG hashes. Offline rebuilding
does not make network requests. No other asset license or Git history is changed.

## Verification and delivery

- Eighteen PNGs independently decoded and checked for dimensions, alpha, neutral
  symbol RGB, source/output hashes and exclusion of the retired EFT artwork.
- Exactly four PNGs changed; the other fourteen are byte-identical to `5992ca5`.
  All four SVGs are byte-identical to their downloaded originals. A second
  offline packing reproduces all eighteen PNGs and the manifest byte-for-byte.
- Actual-size contact sheet inspected by the implementer and independent
  reviewer, including the smallest 18px symbol swatches and blue/red tints:
  `artifacts/four-icons-20261016/artwork/contact-sheet.png` (mockup, not a screenshot).
- Production harness: 368 checks; final staged DLL: **513 checks**, including
  all 22 embedded UI resources and all four matching readable sidecars.
  **16 compiled fault controls and 14 invalid-art/provenance controls** rejected.
- Independent adversarial review: no blocking findings. Two obsolete comments
  were updated to acknowledge mixed sources/licenses. Runtime C# changed only
  in a documentation comment; layout, ownership and event behavior are untouched.

Final isolated build succeeded with zero errors and four pre-existing Shared
nullable warnings. Its manifest is
`artifacts/double-jump-build/957652f5877542b4ab74773177b7986b/manifest.json`.
Only this client DLL and its four UI notices were subsequently copied to the
local SPT installation and checked against the staged bytes. Server/Core remain
unchanged; no push/publication or Git-history rewrite.

GPU2 was still idle in the earlier game's MainMenu. This change does not claim
a new native raid: the earlier Factory acceptance predates these four icons.
The new client requires a game restart; no active game was interrupted or test
profile/config changed for this asset-only update.
