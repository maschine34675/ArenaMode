# Original categorical Arena UI artwork

This directory contains eighteen independently authored geometric SVGs and the
small offline renderer used to produce `ArenaMode.Client/Assets/Ui/*.png`.
All artwork was authored for ArenaMode with Codex assistance, without tracing
or deriving paths from the previous extracted game assets. Artwork and this
generator are available under the MIT notice in
`../../ArenaMode.Client/Assets/Ui/MIT-LICENSE.txt`.

Run on Windows with PowerShell and the built-in WPF assemblies:

```powershell
pwsh -NoProfile -File tools/ArenaUiArtwork/Generate-Artwork.ps1
```

The script requires no installed game, downloaded icon, font for production
images, third-party dependency or network call. Source paths use only SVG `path`
geometry (nonzero/evenodd fill), opacity, and one white linear alpha gradient.
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
survive a checkout with Windows `core.autocrlf=true`.

The generated contact sheet is review evidence under
`artifacts/ui-art-replacement-20261016/artwork/contact-sheet.png`, not a packaged
game texture. It shows each gun at 44x22, each event badge at 18x18, the kill skull
at 32x32 and the banner at native size. Dark and bright-scene glass mockups plus
blue/red tints help review readability, but are not native in-game acceptance.

## Category visual vocabulary

| Category | Distinguishing silhouette |
|---|---|
| Assault rifle | Angular stock, curved magazine, raised front sight |
| Carbine | Compact bullpup, rear magazine and carry handle |
| Sniper | Long bolt rifle, full stock and large optic |
| Marksman | Scoped semiautomatic, straight magazine and pistol grip |
| Machine gun | Ammunition box, carry handle and bipod |
| Shotgun | Full sporting stock, pump and parallel tubes |
| Pistol | Short semiautomatic slide and angled grip |
| SMG | Short barrel, wire stock and vertical magazine |

Kill, death and assist use a skull, gravestone and two contributors respectively.
Headshot retains a head/target marker; grenade, blade, drop, flame and wire retain
the current method/cause semantics. The banner is a neutral original gradient
with a quiet diagonal edge, with no text or team colors baked into it.
