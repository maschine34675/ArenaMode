# ArenaMode original UI artwork

The eighteen PNGs in this directory were created for ArenaMode: eight weapon
category illustrations generated with OpenAI's built-in image tool, nine
geometric event symbols and an original team banner. They are made available
under the **MIT License** by maschine / ArenaMode contributors, to the extent
that copyright or related rights apply to the contributors' work.
The complete copyright and permission notice is in `MIT-LICENSE.txt`; include
that notice when redistributing the artwork or substantial portions of it.
License reference: <https://opensource.org/license/mit>.

No Escape from Tarkov / Arena artwork, extracted pixels, tracing, game fonts or
third-party icon paths are used in this replacement set. The generated weapon
illustrations represent categories, not exact inventory models. These notices
apply to this new set only, not to game assets in older repository revisions or
to other separately credited assets elsewhere in the mod.

## Sources and modifications

Weapon masters and the exact generation/refinement prompts are under
`tools/ArenaUiArtwork/renders/`; the ten remaining SVG sources are under
`tools/ArenaUiArtwork/sources/`. `SOURCES.json` records source and packed PNG
SHA256, dimensions, artwork kind, provenance, license, stable original-artwork URN,
and conversion details for all eighteen images. A URN denotes a project-original
work; it is not a claim of an external download or licence grant from a third party.

The offline Windows/WPF generator `tools/ArenaUiArtwork/Generate-Artwork.ps1`
rasterizes the local SVGs and downsizes the stored render masters without a
network call, game installation or runtime package. The eight weapon categories
use 128x64 canvases with original colors and transparency, the nine event/method
symbols use 64x64, and the neutral team banner uses 688x107. Only the symbols contain white
foreground and transparency; the banner uses a white alpha gradient so the HUD
can tint it using the existing team colors. Only the SVG output's RGB is
normalized to white. Render colors and alpha are preserved through aspect-fit
resampling and conversion from premultiplied to straight alpha.

There are no per-kill inventory renders, generated native weapon caches or
embedded game artwork. Existing gameplay meanings, icon filenames, fallback
text and team-relative colors are retained. The weapon display field is wider;
text width and event-symbol sizes are unchanged.
