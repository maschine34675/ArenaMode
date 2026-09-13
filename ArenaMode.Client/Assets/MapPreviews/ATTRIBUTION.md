# Customs map preview artwork

Customs map: **Shebuka / Tarkov.dev**. Ground-layer distribution and adaptation:
**acidphantasm / DynamicMaps**. ArenaMode grayscale adaptation: **maschine**.
No endorsement by these authors is implied.

- Original project and material: [Tarkov.dev SVG maps](https://github.com/the-hideout/tarkov-dev-svg-maps),
  [Customs.svg](https://github.com/the-hideout/tarkov-dev-svg-maps/blob/main/Customs.svg).
- Layer distributor: [acidphantasm / SPT-DynamicMaps](https://github.com/acidphantasm/SPT-DynamicMaps).
- License: **[Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International](https://creativecommons.org/licenses/by-nc-sa/4.0/)**.
  The original supplied license text is preserved unchanged in
  [Source/Shebuka-LICENSE-MODIFIED BY ACIDPHANTASM.md](<Source/Shebuka-LICENSE-MODIFIED BY ACIDPHANTASM.md>).

`customs-ground.png` and the original SVG are under **CC BY-NC-SA 4.0**. This is
not a permissive commercial asset grant and not the DynamicMaps code license.
Preserve attribution, source/license links, notices and change history when
redistributing the map. Distribute adaptations under CC BY-NC-SA 4.0, without
additional restrictions or technical measures that deny recipients those rights.
The supplied license includes its original warranty disclaimer.

The upstream README also expressly prohibits use in cheat software, live radars,
ESP overlays and pixel-bots. ArenaMode uses this artwork only as a static
pre-raid area selection floorplan, with no live player or entity tracking.

## Changes and source identity

The ground layer and its supplied license were copied byte-for-byte from the
installed `Customs_TarkovDev/Layers` directory on 2026-09-13. Those exact bytes,
not an assumed current upstream revision, are identified in `MAP-SOURCES.json`.

The PNG uses an opaque dark background and an explicit grayscale palette.
Buildings, interior floors, roads, fences and terrain remain distinguishable.
The source's red `Sniper` hazard annotation is omitted from the PNG. Original
SVG geometry is otherwise preserved, with no rotation or crop. No labels,
objective/spawn markers or Arena boundaries are baked into the image. The
original SVG and original license stay unchanged. ArenaMode's changes to the
artwork are likewise offered under CC BY-NC-SA 4.0.

The 1062.48 x 535.17 viewBox is rasterized at approximately 2x (2125 x 1070
integer pixels). Each axis fills the PNG: there is no hidden padding to upset
the documented coordinate mapping. The tiny aspect-ratio difference is only
the rounding to integer pixel dimensions.

## Redistribution checklist

Ship this attribution, `MAP-SOURCES.json`, and the unchanged license with any
binary/package containing the embedded PNG. Retain the unchanged SVG in the
source distribution so the adaptation is reproducible. The map asset terms
apply to this artwork; do not relabel it under the mod code's license.

See `tools/AreaMapAssets/README.md` for offline conversion and verification.
