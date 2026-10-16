# Categorical Arena UI artwork

This directory contains eight generated realistic weapon PNG masters, six
original geometric SVGs, four game-icons.net SVGs and the offline packer used to produce
`ArenaMode.Client/Assets/Ui/*.png`. The user approved the generated assault-rifle
sample after rejecting the initial geometric weapon silhouettes. Generation
used the built-in OpenAI image tool, without reference/game screenshots; exact
prompts and the two transparency refinements are in `renders/PROMPTS.json`.
The four user-selected symbols are Skull crack (headshot), Grenade and Barbed
wire by Lorc, plus Bowie knife (melee) by Skoll, under CC BY 3.0. Their exact
white/transparent upstream SVGs are stored under the Arena filenames in
`sources/`. All other artwork and this generator retain the MIT notice.
See `../../ArenaMode.Client/Assets/Ui/NOTICE.md` for individual source links,
credits, conversion details and both included license texts.

Run on Windows with PowerShell and the built-in WPF assemblies:

```powershell
pwsh -NoProfile -File tools/ArenaUiArtwork/Generate-Artwork.ps1
```

The packer requires no installed game, new download, font for production
images, third-party runtime dependency or network call. Regeneration uses the stored
PNG masters, not another nondeterministic model request. Weapon colors and
alpha survive aspect-fit downsampling to 128x64 with two pixels minimum padding.
The WPF output is converted from premultiplied to straight alpha. SVG sources use only `path`
geometry (nonzero/evenodd fill), opacity, and one white linear alpha gradient.
The four named game-icons SVGs retain their entire 512x512 viewBox, scaled by
1/8 to the 64x64 output. No path editing, cropping or background removal is needed.
Other SVG elements/fills fail closed rather than silently producing bad art.
WPF coverage/gradient alpha is retained exactly; straight PNG RGB is normalized
to white to avoid premultiplied rounding turning edge pixels slightly gray.
The preview sheet uses the installed Segoe UI font only for its review labels.

The generator replaces the named eighteen PNGs and deterministic `SOURCES.json`;
it never removes other files or writes the license notices. `-OutputDirectory`
and `-PreviewDirectory` allow a clean second render for hash comparison. Exact
PNG hashes are tied to the Windows/WPF renderer version; source hashes preserve
the authoritative vector artwork when platform encoding changes.
The local `.gitattributes` pins SVG sources to LF so their recorded byte hashes
survive a checkout with Windows `core.autocrlf=true`. PNG masters are binary.

The generated contact sheet is review evidence under
`artifacts/ui-art-replacement-20261016/artwork/contact-sheet.png`, not a packaged
game texture. It shows each gun at the current 64x32 and previous 44x22, each event badge at 18x18, the kill skull
at 32x32 and the banner at native size. Dark and bright-scene glass mockups plus
blue/red tints help review readability, but are not native in-game acceptance.

## Category visual vocabulary

| Category | Distinguishing silhouette |
|---|---|
| Assault rifle | Angular stock, curved magazine, raised front sight |
| Carbine | Wooden full stock, compact fixed magazine, no pistol grip |
| Sniper | Olive thumbhole stock, visible bolt and large optic |
| Marksman | Scoped semiautomatic, straight magazine and separate pistol grip |
| Machine gun | Ammunition box, carry handle and bipod |
| Shotgun | Full sporting stock, pump and parallel tubes |
| Pistol | Grey semiautomatic slide and black polymer grip |
| SMG | Short barrel, retractable stock and narrow curved magazine |

Kill, death and assist use a skull, gravestone and two contributors respectively.
Headshot uses Skull crack; grenade, blade, drop, flame and wire retain
the current method/cause semantics. The banner is a neutral original gradient
with a quiet diagonal edge, with no text or team colors baked into it.
