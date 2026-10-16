# ArenaMode original UI artwork

All eighteen PNGs in this directory are **original ArenaMode artwork**, authored
for this project with Codex assistance from geometric SVG paths. They are made
available under the **MIT License** by maschine / ArenaMode contributors.
The complete copyright and permission notice is in `MIT-LICENSE.txt`; include
that notice when redistributing the artwork or substantial portions of it.
License reference: <https://opensource.org/license/mit>.

No Escape from Tarkov / Arena artwork, extracted pixels, tracing, game fonts or
third-party icon paths are used in this replacement set. The generic weapon
silhouettes represent categories, not exact inventory models. These notices
apply to this new set only, not to game assets in older repository revisions or
to other separately credited assets elsewhere in the mod.

## Sources and modifications

Each image has its own original vector source under
`tools/ArenaUiArtwork/sources/<filename>.svg`. `SOURCES.json` records the exact
SVG and PNG SHA256, dimensions, authorship, license, stable original-artwork URN,
and conversion details for all eighteen images. A URN denotes a project-original
work; it is not a claim of an external download or licence grant from a third party.

The offline Windows/WPF generator `tools/ArenaUiArtwork/Generate-Artwork.ps1`
rasterizes these local SVGs without a network call, game installation or runtime
package. The eight weapon categories use 128x64 canvases, the nine event/method
symbols use 64x64, and the neutral team banner uses 688x107. Glyphs contain white
foreground and transparency; the banner uses a white alpha gradient so the HUD
can tint it using the existing team colors. Straight PNG RGB is normalized to
white while preserving the renderer's exact coverage/gradient alpha.

There are no per-kill inventory renders, generated native weapon caches or
embedded game artwork. Existing gameplay meanings, icon filenames, placement,
fallback text and team-relative colors are retained.
