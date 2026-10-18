# ArenaMode UI artwork: sources and licenses

This directory contains eighteen PNGs with two distinct licenses. The four
game-icons.net symbols below are **not** covered by the project-original MIT
grant. Include this notice, `SOURCES.json` and the applicable license texts
when redistributing the artwork.

## Four game-icons.net symbols — CC BY 3.0

| File / use | Original title | Author | Source |
|---|---|---|---|
| `headshot.png` / headshot | Skull crack | Lorc | <https://game-icons.net/1x1/lorc/skull-crack.html> |
| `melee.png` / melee | Bowie knife | Skoll | <https://game-icons.net/1x1/skoll/bowie-knife.html> |
| `grenade.png` / grenade | Grenade | Lorc | <https://game-icons.net/1x1/lorc/grenade.html> |
| `barbed-wire.png` / barbed wire | Barbed wire | Lorc | <https://game-icons.net/1x1/lorc/barbed-wire.html> |

Copyright remains with the named authors. These works are licensed under
**Creative Commons Attribution 3.0 Unported (CC BY 3.0)**:
<https://creativecommons.org/licenses/by/3.0/>. Full terms are supplied in
[CC-BY-3.0.txt](../ArenaMode/docs/licenses/CC-BY-3.0.txt). Lorc: <https://lorcblog.blogspot.com/>; Skoll is credited on
the linked source page. No endorsement by the authors or game-icons.net is implied.

Changes by ArenaMode: the entire 512x512 white-on-transparent SVG canvas is
scaled to 64x64 and rasterized with WPF; straight RGB is normalized to white
while retaining antialiasing alpha. Existing HUD colors tint the icons at
runtime. No source paths were edited or cropped. Exact upstream SVGs are stored
in `tools/ArenaUiArtwork/sources/` under the corresponding Arena filenames.
Source/download URLs, author, title, license and source/output SHA256 hashes are
recorded per image in `SOURCES.json`. Sources and license checked 2026-10-16.

## Fourteen project-original assets — MIT

Eight weapon-category illustrations generated with OpenAI's built-in image
tool, five geometric symbols (`kill`, `death`, `assist`, `bleeding`, `fire`) and
the original team banner are made available under the **MIT License** by
maschine / ArenaMode contributors, to the extent that copyright or related
rights apply to the contributors' work. The full permission and copyright
notice is in `MIT-LICENSE.txt`; preserve it with copies or substantial portions.
License reference: <https://opensource.org/license/mit>.

Weapon masters and exact generation/refinement prompts are stored under
`tools/ArenaUiArtwork/renders/`; the six project-original SVGs are under
`tools/ArenaUiArtwork/sources/`. Their manifest URNs identify project-original
work, not external downloads. Weapons represent categories, not exact inventory
models. No extracted EFT/Arena pixels, game fonts or traced game art are used in
this replacement set. These notices do not clear older repository assets or
other separately credited assets elsewhere in the mod.

## Offline packing and runtime use

`tools/ArenaUiArtwork/Generate-Artwork.ps1` rasterizes stored SVGs and downsizes
stored renders without a network call or game installation. Weapons use 128x64
RGBA canvases, symbols 64x64 and the team banner 688x107. Symbols remain white
and transparent; the banner uses a neutral alpha gradient. Weapon colors and
alpha survive aspect-fit resampling. There are no per-kill inventory renders
or extra runtime texture creation. Filenames, gameplay meanings, fallback text,
team colors and layout remain unchanged by this four-symbol replacement.

## Equipment HUD icons — CC BY 3.0

The packaged [CC BY 3.0 legal text](../ArenaMode/docs/licenses/CC-BY-3.0.txt) applies to both icons below.

### Kevlar vest

- Title: **Kevlar vest**
- Creator: **Skoll**
- Source: https://game-icons.net/1x1/skoll/kevlar-vest.html
- Original SVG: https://game-icons.net/icons/ffffff/transparent/1x1/skoll/kevlar-vest.svg
- License: **Creative Commons Attribution 3.0 Unported (CC BY 3.0)**
- License terms: https://creativecommons.org/licenses/by/3.0/
- Full legal text: https://creativecommons.org/licenses/by/3.0/legalcode
- Source and license verified: 2026-10-11.

ArenaMode uses this artwork for the body-armor status icon
(`ArenaMode.Client/Hud/InfoPanels/Icons/armor_body.png`, embedded in the client).
Changes: the original SVG is rasterized to a transparent 64 x 64 PNG, with the
white silhouette's maximum alpha reduced to 232/255 to match the existing HUD.
The HUD applies its existing status/warning tint when displaying the icon.
The artwork's vector shape is unchanged. No endorsement by Skoll is implied.

The unmodified source SVG is retained at
`tools/RaidInfoPanelIcons/kevlar-vest.svg`. The generator and recorded source/output
hashes are at `tools/RaidInfoPanelIcons/gen_armor_icons.ps1` and
`tools/RaidInfoPanelChecks/ARENA-ICONS.json` in the source repository.

CC BY 3.0 permits sharing and adapting the artwork, including commercially,
provided the required attribution and license notice are retained. The artwork
remains subject to that license independently of ArenaMode's code or other assets.
Keep this attribution with any distribution containing the icon.

### Stahlhelm

- Title: **Stahlhelm**
- Creator: **Skoll**
- Source: https://game-icons.net/1x1/skoll/stahlhelm.html
- Original SVG: https://game-icons.net/icons/ffffff/transparent/1x1/skoll/stahlhelm.svg
- License: **Creative Commons Attribution 3.0 Unported (CC BY 3.0)**
- License terms: https://creativecommons.org/licenses/by/3.0/
- Full legal text: https://creativecommons.org/licenses/by/3.0/legalcode
- Source and license verified: 2026-10-11.

ArenaMode uses this artwork for the helmet status icon
(`ArenaMode.Client/Hud/InfoPanels/Icons/armor_helmet.png`, embedded in the client).
Changes: the original SVG is rasterized to a transparent 64 x 64 PNG, with the
white silhouette's maximum alpha reduced to 232/255 to match the existing HUD.
The HUD applies its existing status/warning tint when displaying the icon.
The artwork's vector shape is unchanged. No endorsement by Skoll is implied.

The unmodified source SVG is retained at
`tools/RaidInfoPanelIcons/stahlhelm.svg`. The generator and recorded source/output
hashes are at `tools/RaidInfoPanelIcons/gen_armor_icons.ps1` and
`tools/RaidInfoPanelChecks/ARENA-ICONS.json` in the source repository.

The same CC BY 3.0 terms above apply to this artwork independently of ArenaMode's
code or other assets. Keep this attribution with any distribution containing it.
