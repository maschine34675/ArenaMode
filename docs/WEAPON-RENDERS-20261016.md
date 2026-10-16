# Realistic weapon-category pictures

2026-10-16. After rejecting the geometric weapon silhouettes, the user chose
**generated pictures matching the assault-rifle sample**, rather than native
EFT inventory pictures. This supersedes only the weapon artwork in
[the earlier replacement](UI-ARTWORK-REPLACEMENT-20261016.md).

## Delivered scope

Eight fixed category pictures: assault rifle, carbine, bolt-action sniper,
marksman rifle, machine gun, shotgun, pistol and SMG. All are generated raster
illustrations in a realistic side-view style, facing right, with transparent
backgrounds. They are illustrative representatives, not exact technical weapon
diagrams, photographs or EFT screenshots. Nine event/cause symbols and the
scoreboard banner remain byte-identical to `a9e9f30`.

The full-resolution masters are versioned under `tools/ArenaUiArtwork/renders`.
`PROMPTS.json` records every generation prompt and the two transparency
refinements; no external image or extracted game asset was used as a reference.
The built-in OpenAI image tool was used, not the API/CLI fallback. The tool does
not expose a model version, so no exact model identity is asserted.

`Generate-Artwork.ps1` reproducibly packs the stored masters into 128x64 PNGs
offline, preserving colors and straight alpha and fitting the complete image
inside a two-pixel margin. It does not regenerate pictures through a model.
The eight superseded weapon SVGs are removed; the ten still-used SVGs remain.
MIT notices explicitly identify the generated art and qualify the grant to
rights that apply to the contributors' work. This is not third-party clearance
for real weapon brands, older extracted images or unrelated mod assets.

The weapon display field increases from 44x22 to 64x32; the complete killfeed
and icon rail grow by 20px together (540/80). Text still has **436px**. The
headshot badge keeps its 18x18 size below the weapon. Other cause/fallback icons
keep their 44x22 field, reset on every pooled-row reuse. Existing category and
final-cause mappings, team-name colors, distance, scoring, timing, skull and
scoreboard behavior are unchanged. There is no new setting, runtime render,
native inventory cache or extra per-kill texture creation.

## Source alternatives checked

Primary source pages checked on 2026-10-16:

- [Styloo Guns Asset Pack](https://styloo.itch.io/guns-asset-pack): creator lists
  CC0 and supplies textured GLB/FBX gun models. Possible input for independent
  renders, but the listed set does not cover all our categories directly.
- [Quaternius Ultimate Guns](https://quaternius.com/packs/ultimategun.html):
  forty 3D models, CC0, FBX/OBJ/Blend; a stylized alternative.
- [OpenGameArt Firearm Icons](https://opengameart.org/content/cc0-firearm-icons):
  CC0 collection, chiefly small 32x32 icons; not the requested screenshot style.

No external pack was downloaded, bundled or treated as source material for the
generated pictures. Native EFT default-preset icons remain technically possible
without distributing their rendered bytes, but were not selected for this change.

## Verification and delivery

The asset checker still requires exact dimensions, resource sets, source/packed
hashes, bounded PNG decoding, transparent surroundings and rejection of all
eighteen retired extracted-image hashes. Only the eight explicit weapon entries
allow colored pixels and PNG masters; all ten remaining assets still require
white tint-neutral pixels and exact SVG paths. New controls reject a white
weapon silhouette and a clipped weapon border; old corruption controls remain.
The pooled-row checks retain text width, hazard size, cause priority and color
reset, with a compiled failure control for oversized fallback symbols.

Visual review uses `artifacts/weapon-picture-preview-20261016/artwork/contact-sheet.png`
at actual display sizes; it is a mockup, not an in-game capture. The first
single-rifle HTML proof is also ignored and is not a shipped UI.

Offline verification: 18 decoded images; 10 malformed-art controls and 16
compiled runtime/presentation controls rejected. Production harness: 368 checks;
including the actual staged DLL and its readable notices: **509 checks**.
Killfeed text/registry: **120 assertions plus six compiled fault controls**.
A second independent packing reproduces all 18 PNGs and the manifest exactly.
All PNGs total **55,951 bytes**; full-resolution masters total **8,833,824 bytes**
and remain development inputs, not embedded resources.

Staged client build succeeded with zero errors and four existing Shared nullable
warnings. Manifest: `artifacts/double-jump-build/c995a30de12d4dc6a7c1a1458cc3e170/manifest.json`.
The installed client, server and Core were unchanged by this staged build.
Independent adversarial source/pixel/layout review found no blocking issue;
one obsolete white-only loader comment was corrected. No blanket full-suite
or gameplay acceptance is implied by these scoped checks.

Native test outcome follows separately. No push, publication or history rewrite
is included.
