# ArenaMode: licenses, sources and credits

Current-tree inventory, checked **2026-10-16**. This document distinguishes
ArenaMode's code license from separately licensed assets. It does not relicense
earlier Git revisions, screenshots or game content, and is not a statement that
the entire repository history is ready for publication.

## License scope

- Original ArenaMode code and documentation: **MIT**, copyright (c) 2026
  maschine; full text in [LICENSE](LICENSE).
- Adapted third-party code: preserve the original author notices below.
- Images and audio: the component-specific terms below apply instead of the
  general project MIT grant. **The complete package is not wholly MIT.**
- Screenshots, recordings and other depictions of EFT retain the underlying
  game-content rights. No license to Battlestate Games' assets or trademarks is
  granted by this repository.

## Code

### DragonDen.Hitmarker and DragonDen.Killfeed

**MIT — Copyright (c) 2025 Drexira.**

- [DragonDen.Hitmarker, SPT-4.0](https://github.com/Drexira/DragonDen.Hitmarker/tree/SPT-4.0):
  adapted hitmarker geometry/timing/colors in
  [HitmarkerElement.cs](ArenaMode.Client/Hud/HitmarkerElement.cs), and damage-number
  projection/lifetime/overlap placement in
  [DamageNumbersElement.cs](ArenaMode.Client/Hud/DamageNumbersElement.cs).
- [DragonDen.Killfeed, SPT-4.0](https://github.com/Drexira/DragonDen.Killfeed/tree/SPT-4.0):
  adapted behavior in [KillfeedView.cs](ArenaMode.Client/Hud/KillfeedView.cs),
  with ArenaMode's own uGUI presentation.
- Source comments retain the attribution. Original upstream images and sounds
  are not part of this code adaptation. Both upstream repositories carry the
  same MIT notice, reproduced in full at the end of this document.

### Redeploy.Core

**MIT — Copyright (c) 2026 maschine.** The full, unchanged text is identical to
the project [LICENSE](LICENSE). This includes the shared Core library and the
linked `drivers/CoreVersionGate.cs` source from the separate Redeploy project.
The current Arena source references Core **1.0.8**; use the exact matching
source/build when packaging. The complete Redeploy mod is not an Arena
dependency. Public availability of matching Core source is a release requirement,
not established by this notice.

### Own integrations and behavioral references

RaidInfoPanels and CombatSlide are the author's own projects absorbed into
ArenaMode. Their original code is covered by the project MIT grant; the two
separately licensed HUD icons are listed below.

EnableLabyrinth was a behavioral reference for original Arena implementation,
not an imported mod or configuration app. Its MIT reference notice is retained
in [the access-check sources](tools/ArenaLabyrinthAccessChecks/EnableLabyrinth-LICENSE.txt),
copyright (c) 2025 acidphantasm. See the
[implementation boundary](tools/ArenaLabyrinthAccessChecks/README.md).

Smoothie (CC BY-NC-SA 4.0) was read as a technique reference for building a
server batch of bot profiles under a per-frame time budget
(`ArenaMode.Client/Performance/ProfileBuildBudget*.cs`); no code or text was
copied, the implementation follows the decompiled native method and its own
tests. Its license does not apply to ArenaMode.

SAIN, APBS, ABPS/BotPlacementSystem, PitFireTeam, BigBrain and other optional
mods retain their own licenses. Compatibility adapters do not grant permission
to bundle those mods. No foreign mod DLL is required to be redistributed as
part of ArenaMode.

## Images

### game-icons.net — CC BY 3.0

| Original work | Author | Arena use |
|---|---|---|
| [Skull crack](https://game-icons.net/1x1/lorc/skull-crack.html) | Lorc | Headshot icon |
| [Grenade](https://game-icons.net/1x1/lorc/grenade.html) | Lorc | Grenade kill icon |
| [Barbed wire](https://game-icons.net/1x1/lorc/barbed-wire.html) | Lorc | Barbed-wire kill icon |
| [Bowie knife](https://game-icons.net/1x1/skoll/bowie-knife.html) | Skoll | Melee kill icon |
| [Kevlar vest](https://game-icons.net/1x1/skoll/kevlar-vest.html) | Skoll | Body-armor HUD icon |
| [Stahlhelm](https://game-icons.net/1x1/skoll/stahlhelm.html) | Skoll | Helmet HUD icon |

Licensed under [Creative Commons Attribution 3.0 Unported](https://creativecommons.org/licenses/by/3.0/).
Copyright remains with the authors; no endorsement is implied. ArenaMode
rasterizes the SVGs to transparent 64×64 PNGs and applies HUD tinting. The four
combat icons retain the full SVG canvas; the two equipment icons use reduced
maximum alpha. Shapes are not redrawn.

Keep the exact attribution, change records and full terms:

- [Combat-icon notice](ArenaMode.Client/Assets/Ui/NOTICE.md),
  [manifest](ArenaMode.Client/Assets/Ui/SOURCES.json) and
  [CC BY 3.0 text](ArenaMode.Client/Assets/Ui/CC-BY-3.0.txt).
- [Equipment-icon notice](ArenaMode.Client/Assets/ArenaMode/HUD-ICON-LICENSES.md)
  and [CC BY 3.0 text](ArenaMode.Client/Assets/ArenaMode/CC-BY-3.0.txt).

### Customs preview — CC BY-NC-SA 4.0

Map by **Shebuka / Tarkov.dev**; ground-layer distribution/adaptation by
**acidphantasm / DynamicMaps**; grayscale adaptation by **maschine**.

- [Original map project](https://github.com/the-hideout/tarkov-dev-svg-maps),
  [DynamicMaps layer distributor](https://github.com/acidphantasm/SPT-DynamicMaps).
- [Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International](https://creativecommons.org/licenses/by-nc-sa/4.0/).
  The SVG and adapted PNG are **not MIT**. Redistribution must respect the
  noncommercial, attribution and share-alike terms applicable to this artwork.
- Arena uses a static pre-raid floorplan. The upstream project also prohibits
  cheat software, live radar, ESP and pixel-bot uses; no such use is licensed here.
- Changes: grayscale palette/dark background, rasterization, omitted red Sniper
  annotation; retained original geometry and source SVG.
- [Attribution and modifications](ArenaMode.Client/Assets/MapPreviews/ATTRIBUTION.md),
  [source manifest](ArenaMode.Client/Assets/MapPreviews/MAP-SOURCES.json),
  [unchanged supplied legal text](<ArenaMode.Client/Assets/MapPreviews/Source/Shebuka-LICENSE-MODIFIED BY ACIDPHANTASM.md>).

### Project-original artwork and effects

Eight weapon-category illustrations were generated using OpenAI's image tool.
Five geometric combat symbols and the team banner are original ArenaMode
artwork. These fourteen images are offered under MIT by maschine / ArenaMode
contributors, **to the extent copyright or related rights apply to their work**.
See the [artwork notice](ArenaMode.Client/Assets/Ui/NOTICE.md),
[full MIT text](ArenaMode.Client/Assets/Ui/MIT-LICENSE.txt) and
[generation records](tools/ArenaUiArtwork/renders/PROMPTS.json).

The project thumbnail is a generated, simplified illustration of crossed AK
and M4 rifles framed by a laurel wreath in graphite, ivory and ochre. It was
made with OpenAI's image tool by reworking the project's previous realistic
thumbnail, which used the earlier thumbnail and generated assault-rifle artwork
as references. It is not a photograph or an extracted game asset.
The [generation record](docs/THUMBNAIL-20261018.json) contains the prompts,
revision history and source/export hashes. It is offered under the project
MIT license to the extent copyright or related rights apply to the contributors' work.

The remaining original HUD graphics (excluding the two credited equipment
icons), procedural Molotov bottle, fire textures and
synthesized fire sounds are ArenaMode work, likewise offered under the project
MIT license to the extent copyright or related rights apply. Native game hand rigs,
animations and prefab references are used from the installed game, not offered
as redistributed game assets. No downloaded Molotov recording or asset from
Sage's mod is included; see the
[presentation provenance](docs/MOLOTOV-PRESENTATION-REVIEW.md).

## Audio

### CC0 effects

| Effect | Creator and source | Processing |
|---|---|---|
| Pickup | **IENBA**, [Game Pick Up](https://freesound.org/people/IENBA/sounds/762911/) | User-supplied excerpt from the collection, trimmed/converted to mono; gain increased by 12 dB. |
| Hit / headshot / kill | **User391915396**, [Hitmarker Sound Effect](https://freesound.org/people/User391915396/sounds/570335/) | Converted to mono 44.1 kHz PCM16; three identical copies for the cue slots. |

Both sources are **CC0 1.0 Universal**. The attribution is retained for provenance;
it does not imply endorsement or a warranty. These are not attributed to Call
of Duty or another commercial game.

- Pickup [manifest](ArenaMode.Client/Assets/ArenaMode/pickups/SOURCES.json) and
  [CC0 text](ArenaMode.Client/Assets/ArenaMode/pickups/CC0-1.0.txt).
- Hitmarker [manifest](ArenaMode.Client/Assets/ArenaMode/hitmarkers/SOURCES.json)
  and [CC0 text](ArenaMode.Client/Assets/ArenaMode/hitmarkers/CC0-1.0.txt).

### Announcer — separate generated-audio terms

The current set contains **52 user-supplied recordings**, generated using an
ElevenLabs **Creator** account according to the author's supplied provenance:
46 initial recordings plus six additions; 41 active cues and 11 reserve files.
Only the 41 active cues are shipped in build/deploy/release payloads; the unused
reserve recordings and their complete provenance remain in the source repository.
They were provided for distribution with ArenaMode.

These files are **not MIT, Creative Commons or public domain**. This repository
does not grant unrestricted standalone voice-pack reuse. The documented rights
position relies on the author's paid-plan statement; the specific account,
voice/model permissions and generation conditions were not independently
verified. Before release, confirm the necessary rights for the actual voice,
inputs and generation plan.

[ElevenLabs' publishing guidance](https://elevenlabs.io/docs/help-center/legal/can-i-publish-the-content-i-generate-on-the-platform),
checked 2026-10-16, permits paid-plan use subject to the necessary rights,
applicable terms and product-specific conditions, excluding Beta Services.
That guidance is not a separate open-content license for these files.

Processing includes reviewed cuts, short fades, a uniform -1 dB gain and Ogg
Vorbis encoding; the detailed manifests record exact source and output hashes.
Preserve [AUDIO-LICENSES.md](ArenaMode.Client/Assets/ArenaMode/AUDIO-LICENSES.md),
[ANNOUNCER-PACK.md](ArenaMode.Client/Assets/ArenaMode/ANNOUNCER-PACK.md),
[the initial manifest](ArenaMode.Client/Assets/ArenaMode/ANNOUNCER-SOURCES.json)
and [the additions manifest](ArenaMode.Client/Assets/ArenaMode/ANNOUNCER-ADDITIONS-20260917.json).

Older announcer packs and extracted game UI images were replaced in the current
tree. Their presence in older Git objects is not cleared by these replacements.

## Distribution checklist

- Include this overview and the project LICENSE with source and binary releases.
  The full Drexira notice below must accompany binaries containing the adaptations.
- Keep asset notices, manifests and full license texts beside their assets, also
  when images are embedded in the client DLL. Preserve the Customs source and
  attribution in source distributions.
- Include the matching Redeploy.Core copyright/license notice and make its exact
  corresponding source available. The MIT text in LICENSE carries its notice.
- Do not package game, Unity or SPT platform assemblies, optional foreign mods,
  test profiles, credentials or diagnostic tooling copied into build output.
- Recheck provenance, package contents and Git history before publication. New
  documentation does not itself resolve those release checks or publish anything.

The current build already copies the asset notices. Packaging the new root
LICENSE and this central overview is still a release-assembly step; no build or
deployment behavior is changed by adding these documents.

## Full DragonDen MIT notice

The following unchanged notice applies to both DragonDen adaptations above:

```text
MIT License

Copyright (c) 2025 Drexira

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
