# UI artwork replacement — 16 October 2026

Recommendation: use **native EFT weapon-template icons** for bullet kills,
independently authored or redistribution-licensed small symbols for the other
events, and an original procedural scoreboard banner. This is a feasibility
review, not an implemented replacement or a native performance test.

All **18 PNGs (66,699 bytes)** in `ArenaMode.Client/Assets/Ui` have live consumers.
The complete file set, SHA256 values and dimensions were independently checked
against `SOURCES.json`; the private contact sheet was visually inspected.
`NOTICE.md` establishes no redistribution license for these extracted originals.
None was deleted or replaced during this review.

## What needs replacing

| Files | Current uses | Proposed replacement |
|---|---|---|
| `assault-rifle.png`, `carbine.png`, `sniper.png`, `marksman.png`, `machinegun.png`, `shotgun.png`, `pistol.png`, `smg.png` | Eight weapon-class silhouettes in the killfeed | Native image of the actual weapon template's default preset, with a generic original fallback |
| `kill.png` | Generic killfeed fallback, K scoreboard header, confirmed-kill skull above the crosshair | Original or licensed skull; retain existing position/timing |
| `death.png`, `assist.png` | D/A scoreboard headers | Distinct original or licensed death/contribution symbols |
| `headshot.png` | HS scoreboard header and killfeed badge | Licensed/original headshot symbol readable at 18 px |
| `grenade.png`, `melee.png` | Non-bullet kill causes | Simple original/licensed grenade and knife |
| `bleeding.png`, `fire.png`, `barbed-wire.png` | Final death causes, including delayed/environmental deaths | Original/licensed blood drops, flame and barbed wire |
| `team-panel.png` | F9 scoreboard team-section background | Original gradient and diagonal geometry, tinted by team |

The compact top-center score panel does **not** use `team-panel.png`; its
surfaces already come from `ArenaUiTheme`. The common sprite loader is
`Hud/ArenaUiAssets.cs`; consumers are `Hud/KillfeedView.cs`, `Hud/KillfeedIcon.cs`,
`Hud/ArenaScoreboardPresentation.cs` and `Hud/HitmarkerElement.cs`
(paths relative to `ArenaMode.Client`). Dynamic weapons alone replace eight
originals, leaving ten other graphics to address.

## Native per-weapon images: feasible, with bounded work

Verified against this installation's local `_decompiled` source, without copying
its implementation:

- `EFT/ItemFactory.cs:544`: `GetPresetItem(templateId)` clones a default preset
  when available, otherwise constructs the base item.
- `EFT.UI.DragAndDrop/ItemViewFactory.cs:38`: `LoadItemIcon(item)` uses the normal
  inventory resolution and the shared `ItemIconCreator` memory/disk cache.
- `ItemIconCreator.cs:78`: the returned `ItemIcon` can initially have a null
  sprite. Missing images are loaded/rendered asynchronously.
- `Diz.Binding/BindableEvent.cs:55`: `Changed.Bind` invokes its callback
  immediately and returns an unsubscribe action. Arena must own that binding.
- `IconsHash.cs:33-105`: the cache key includes the visible item configuration,
  but **not the requested image dimensions**. Never request killfeed-size images
  in the shared cache; display the normal inventory image at a smaller size.
- `IconCreatorBase.cs`: cached sprites/textures belong to EFT. Do not destroy,
  recolor, change their filtering or clear the shared cache. Dispose only Arena's
  subscriptions and any explicitly Arena-owned derived resources.

Start with **one recognizable default image per weapon template**, not a new
render of every attachment combination. Exact equipped assemblies are possible
in principle, but produce more cache entries and little benefit in the existing
44x22-pixel weapon box. A template image does not claim to reproduce the killer's
actual attachments. Missing/modded presets need a safe fallback.

`ArenaKill` currently stores weapon name/class and damage kind, not a template
ID. Capture a plain `WeaponTemplateId` string from `damageInfo.Weapon` in
`CombatPatches` for direct lethal bullets. Never retain live weapon/player objects
or infer the gun from the killer's later equipped item. Resolve icons in the HUD,
not in the combat callback; presentation failure must not affect scoring.

Preserve final-cause priority: bleeding, barbed wire, fire, grenade and melee use
their cause symbol even when native `LastDamageInfo` still references an earlier
gun. Preserve stale-headshot suppression, suicide/uncredited-death text, distance,
team colors and existing killfeed layout behavior.

Each of the six pooled killfeed rows needs a changing binding token plus a HUD/
raid generation check. A late icon completion must never overwrite a reused row.
Detach on expiry, recycling, HUD disable and teardown. Show a generic symbol
immediately while an image is pending.

Cold generation loads bundles/models and performs native camera rendering and
GPU readback; a warm cache hit is much cheaper. Prefer prewarming a bounded set
of known templates during suitable loading/ready phases, one cold request at a
time, no forced regeneration or full-catalog scan. A possible cap of 64 templates
per raid is a design starting point, **not a measured optimum**. Native generation
has no cancellation token: timing out Arena's wait does not stop native work.
Do not launch unbounded replacements for timed-out requests. Avoid the direct
`GetItemSpriteAsync` path, which bypasses the preferred cached request flow.

Native previews have color/shadow, unlike flat silhouettes. First inspect them
at the actual display size. If necessary, use an Arena-owned UI material for a
monochrome treatment, without mutating borrowed textures or assuming they are
readable/non-atlased. A slightly wider icon area is an option only after checking
long-name readability; there is no need for many new settings.

## Concrete external alternatives

Primary source/license pages checked online on 16 October 2026. No third-party
replacement was downloaded or integrated. Examples need an actual 18-32 px
visual review before selection; a license check alone does not establish fit.

| Source | Suitable examples | Redistribution conditions / fit |
|---|---|---|
| Game-icons.net | [Skoll Headshot](https://game-icons.net/1x1/skoll/headshot.html), [sbed Death skull](https://game-icons.net/1x1/sbed/death-skull.html), [Lorc Barbed wire](https://game-icons.net/1x1/lorc/barbed-wire.html), [Skoll Blood](https://game-icons.net/1x1/skoll/blood.html) | These pages specify CC BY 3.0. Keep author/source/license attribution and identify modifications. Filled shapes fit the existing HUD better than very thin outlines; complex blood/wire detail may need simplification. |
| Lucide | [Skull](https://lucide.dev/icons/skull), [Crosshair](https://lucide.dev/icons/crosshair), [Handshake](https://lucide.dev/icons/handshake), [Droplet](https://lucide.dev/icons/droplet), [Flame](https://lucide.dev/icons/flame) | [License](https://lucide.dev/license): ISC, with listed Feather-derived icons under MIT, including crosshair. Retain the applicable copyright/permission notices. Consistent, minimal outlines; less specific for headshot/barbed wire and may need thicker strokes at small sizes. |
| Kenney Game Icons | [Official pack](https://kenney.nl/assets/game-icons) | Listed as CC0; [official support](https://kenney.nl/support) explains no attribution requirement. General UI alternative, not verified as a complete weapon/death-cause replacement. |

[CC BY 3.0](https://creativecommons.org/licenses/by/3.0/) permits sharing and
adaptation, including commercial use, subject to its attribution conditions;
this is not CC0/MIT and does not impose a ShareAlike license on the entire mod.
Check the chosen asset's actual author/license rather than treating a whole
website as one undifferentiated license. The existing Skoll armor/helmet
attribution already provides a suitable pattern for additional Game-icons art.

Recommended first choice: native weapon previews plus a small coherent set of
Game-icons/original filled symbols. Use an original procedural team banner and
generic weapon fallback. Lucide is a viable alternative if the whole symbol set
is deliberately given a stronger outline style; avoid mixing styles arbitrarily.

## Packaging and acceptance boundary

Calling the installed game's icon API avoids bundling the eight extracted gun
PNGs. It does **not** create a redistribution license or legal clearance for the
resulting native renders. Do not commit/export those renders or copy decompiled
code. EFT may still write its normal private icon cache.

The current project embeds the PNGs and their notices into the Client DLL.
Replacing files is insufficient if an old DLL is shipped. Once replacements
exist, update provenance, build cleanly, and inspect the embedded-resource list
and package for all original hashes. Historical Git blobs remain a separate
publication issue; this review does not rewrite history or authorize publishing.

Implementation checks should cover pending/failed/unknown icons, recycled rows,
raid/HUD teardown, damage-cause precedence, request deduplication/cap, borrowed
resource ownership and normal-size shared-cache requests. Retain existing
killfeed/scoreboard regressions. Native acceptance should then compare cold/warm
cache paths, rapid mixed-weapon kills, inventory sharpness, 1080p/1440p scaling,
long names and bright/dark backgrounds, including frame-time/memory impact.
No in-game or performance acceptance is claimed by this source review.

Private reproducible inventory and native-source review:
`artifacts/dead-code-removal-20261016/ui/`. These audit artifacts include the
existing original art and are not a redistributable replacement pack.
