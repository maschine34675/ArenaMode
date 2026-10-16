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
`CC-BY-3.0.txt`. Lorc: <https://lorcblog.blogspot.com/>; Skoll is credited on
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
