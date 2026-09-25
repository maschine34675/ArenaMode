# Absorbed Arena info-panel checks

Run `pwsh -NoProfile -File tools/RaidInfoPanelChecks/Verify.ps1` from this repo.
The full success marker is `ARENA_INFO_PANEL_CHECKS_PASSED`. The optional
`-ProductionOnly` mode explicitly reports that negative controls did not run.

The net10 executable links these production files unchanged:

- `ArenaInfoPanels.cs`: ownership, refresh scheduling, independent toggles,
  per-module failures, stale-content invalidation, theme subscription and disposal.
- `RaidPlayerContext.cs`: ordinary-raid exclusion, exact game/world identity,
  loading/stopping, death, unready hands/body/inventory, alive roster, respawn,
  first-person view and spectator exclusion.
- `InfoPanelSettings.cs`: all 17 original setting mappings, existing/unbound and
  already-bound Arena values taking precedence, missing/invalid legacy entries,
  and read-only legacy binding. Test fixture bytes stay unchanged after imports.
- `MagazineInventoryCounter.cs` (rewritten 2026-09-25, "HUD minimal reserve"):
  the candidate set is exactly what the reload button offers — the native
  `GetReachableItemsOfTypeNonAlloc<Magazine>` query itself (rig/pockets direct
  children, called, never patched), widened by the backpack's own direct
  children exactly when `ArenaBackpackReload.AppendsBackpackFor` says the
  reload path would append them. Full/partial classification applies equally
  to every candidate, backpack included. Nested pouches, in the rig or the
  backpack, are never counted (not loadable by anything). `BackpackLoadedCount`
  is gone; the summary is `FullCount`/`PartialCount` only. Exception cleanup
  releases both scratch buffers and the following refresh recovers.
- `GrenadeInventoryCounter.cs` (rewritten 2026-09-25): the displayed number per
  kind is "usable" — every carried grenade (rig, pockets, backpack, nested
  pouches included) for which `IsAtReachablePlace` says so, which already
  folds in Arena's reach widening, its setting gate and `Examined`; a grenade
  on the native tap-G list is always usable too (checked first, cheaper than
  the reachability call). A separate "on G" count per kind, from the native
  tap-G list alone, drives icon dimming without changing the displayed number.
  `Ready`/`Stored` are gone. The stand-in `InventoryController` keeps the
  one-level `GetAcceptableItemsNonAlloc` query (slots before grids, appending
  without clearing, the owner override that skips an unsearched container —
  now shared by both the native reachable query and Arena's backpack query)
  and adds `IsAtReachablePlace`: native rig/pockets recursion (nested pouches
  included, unlike the one-level query) unaffected by search state, widened by
  a settable `ArenaWidenReach` flag standing in for
  `ArenaReachPatches.ReachableAnywherePatch`. Cases cover on-G and usable
  agreeing for direct children, a nested-rig grenade usable without widening
  while a backpack one is not, ArmorVest/secure exclusion, unexamined
  exclusion, an unsearched rig dropping out of G's list without affecting
  usability, the widening flag adding and removing the backpack grenade,
  effect-based classification for unlisted types, a classification fault fired
  with both scratch lists filled, an examine fault on the very first call, the
  summary accessors the UI reads, and native-faithful missing-controller
  handling (`Player.Inventory` reads through the controller, as natively).
- `ArenaHudVisibility.cs`: BattleUI draws the Arena canvas; native screens and a
  missing screen manager stop drawing without deactivating the canvas object.
- `ReserveRowLayout.cs` (rewritten 2026-09-25): geometry of the shared
  magazine/grenade row. Left and right packing skip hidden slots even with stale
  widths, frag stays outermost beside flashes, and a zero, non-finite,
  implausibly small or absurd text measurement falls back to the estimate or is
  capped. The row never wraps any more: `FitScale` returns a single scale
  factor for both groups — 1 when they fit beside each other (including the
  group gap), `ContentWidth / needed` when they do not, clamped to
  `MinFitScale` — checked at the exact fit boundary, an extreme width that
  hits the clamp, a case where the gap alone forces a shrink, and the case
  where one empty group never needs to shrink. A sweep over all visibility
  combinations checks packing spacing and reported extents. The views' TMP
  measurement, `ApplyScale` and RectTransform placement are not linked here.
- `ArmorDurabilityCounter.cs` (rewritten 2026-09-25, "HUD minimal reserve",
  linked here for the first time — the stub it replaces only counted `Reset()`
  calls): two aggregates, Head (Headwear/FaceCover/Eyewear) and Body
  (TacticalVest/ArmorVest), each the sum of every distinct `ArmorComponent`
  found recursively under that group's slots (`GetItemComponentsInChildrenNonAlloc`,
  which also walks plates and helmet mods), added ONCE per component
  regardless of how many body regions it covers — replacing the old 19-zone
  model, which credited a component to every zone it covered and so would
  double- or quadruple-count it into any aggregate built by summing zones.
  Cases cover Head/Body separation, all three head slots (including FaceCover
  and Eyewear, not just Headwear), a plate carrier's front/back/side plates
  each counted once (the back plate used to cover two zones), a helmet's
  visor mod landing in Head, ArmorVest and an armored TacticalVest both
  landing in Body, components without `Repairable` or with zero max
  durability ignored, no armor leaving `HasArmor` false, and a collection
  fault propagating to the caller while still clearing the scratch list
  (checked directly, like the magazine/grenade scratch buffers, since this
  production class has no call counter of its own).

The suite includes fast repeated ticks, immediate hide/recovery before the next
refresh deadline, replacement physical bodies, independent weapon/status faults,
invalid intervals, bounded warnings, partial construction and cleanup after a
hide failure. Disposal also exercises the real magazine scratch reset (both
buffers).

Thirty-four controlled source faults must compile and fail at their intended
behavioral assertion: omitted inventory-open flag, omitted native-screen check,
a canvas kept drawing on native screens, panels built under an already hidden
root (before TMP's Awake), omitted early inactive-session guard, removed
exact-world check, stale weapon content, leaked theme handler, legacy config
writes; for magazines: scratch retention, the backpack hook ignored, a nested
backpack pouch counted, backpack magazines all forced to full regardless of
fill; for grenades: scratch retention, a leak on the exception path only, the
native reach list replaced by a hand-written walk that skips Examined, stun no
longer grouped with flash, secure-container grenades counted, the
`IsAtReachablePlace` filter dropped entirely (everything carried becomes
usable), and G's own dimming count taken from the carried/usable set instead of
its real candidate list; for the reserve row: hidden slots reserving room,
grenade spacing omitted, the measurement fallback removed, the grenade order
reversed, implausibly small measurements trusted, an empty group no longer
skipping the shrink, `FitScale` never actually shrinking, its clamp removed,
and its group gap left out of the fit; for armor: a component counted twice
instead of once, FaceCover missing from the head slot list, ArmorVest missing
from the body slot list, Pockets (not a native armor slot) added to the body
slot list, and the scratch list left unreset on the exception path.
Each run saves copied mutants and output under this
project's ignored `obj/negative/<unique-id>` directory; production source is never
modified. The verifier reruns the unmodified production sources afterward.

These are **offline contract checks**, not Unity GPU or in-raid acceptance.
Stand-ins model the external Unity lifecycle, EFT data access, BepInEx Bind
contract and UI modules; actual text, textures, rendering, visibility hierarchy,
native inventory converters and other mods' behavior require the separate real
build/resource checks and GPU test. The migration fixtures use simplified
vector/color text syntax: they verify mapping and ownership, not BepInEx's real
parser. Item ancestry follows the inspected native contract (ordinary ancestry
for IsChildOf, merged ancestors for GetRootItem) but does not replace a live
inventory check with actual database-created armored rigs.

This verifier does not deploy, modify legacy configs, retire another mod, alter
the source-import record, or launch the game. `Build-Verify.ps1` and
`SOURCE-IMPORT.json` are independently owned build/import evidence.
`Build-Verify.ps1` also checks icons authored inside ArenaMode against their own
record, `ARENA-ICONS.json`, so the import provenance of the imported icons
stays exactly as strict. Never rerun the four imported generators: that would
re-encode imported icons. New icons get their own generator (for example
`tools/RaidInfoPanelIcons/gen_grenades.ps1`) and an `ARENA-ICONS.json` entry.

Of the original 26 imported icons, 9 are still embedded: `Build-Verify.ps1`
builds its expected set only from `SOURCE-IMPORT.json` entries that are both
`unchanged` and not `retired`, and asserts that count is exactly 9. The other
17 — `armor_base.png`, all 15 `armor_zone_*.png` and `mag_backpack.png` — were
retired 2026-09-25 ("HUD minimal reserve": the 19-zone paper doll and the
separate backpack-magazine bucket are both gone) and deleted from
`ArenaMode.Client/Hud/InfoPanels/Icons`; their `SOURCE-IMPORT.json` entries keep
their original source path and hash as provenance with an added `"retired"`
reason, and `Build-Verify.ps1` additionally asserts none of them is embedded in
the built DLL. `ARENA-ICONS.json` now records 5 Arena-authored icons: the three
grenade glyphs plus `armor_body.png`/`armor_helmet.png`
(`tools/RaidInfoPanelIcons/gen_armor_icons.ps1`), the two new armor-row icons
that replace the retired paper doll. `rounds_backpack.png` stays — loose
cartridges for shell-by-shell weapons keep their own backpack bucket, only
spare magazines and grenades were folded into the regular counts.
