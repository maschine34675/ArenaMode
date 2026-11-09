# Factory session artwork

The three original illustrations were generated for ArenaMode with the built-in OpenAI image generator on 2026-11-09. The first depicts an empty industrial hall. The two transparent cutouts used that hall as their lighting reference. All three are distributed with the project under its MIT license.

`hall.jpg`, `barrier.png` and `fence.png` share one 2352 × 1008 canvas. In drawing order they form the Factory scene. `splash.jpg` flattens the same layers at their resting positions. The open middle accommodates the native main menu and the side props remain visible in a centered 16:9 crop.

The [generation record](../../../tools/ArenaUiArtwork/SessionMenu/PROMPTS.json) identifies the inputs. The [exporter](../../../tools/ArenaUiArtwork/SessionMenu/Export-SessionMenuArt.ps1) rebuilds the four runtime files from the checked-in PNG masters on Windows using PowerShell 7. Pass `-PreviewDirectory artifacts/session-menu-art` to render the 21:9 and 16:9 review images.
