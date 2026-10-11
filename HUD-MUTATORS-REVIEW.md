# HUD and utility mutators

2026-10-11. Three map-screen switches, available in every Arena mode; the
existing arm/live lock and config persistence path apply unchanged.

- **AUTO QUICKSLOTS** maps directly to `Kits/AutoQuickSlots`. Off stops further
  automatic medical bindings; it does not erase existing or manually assigned
  quickslots. Backpack eligibility remains controlled by BACKPACK ITEMS.
- **INSTANT SEARCH** maps directly to `Loot/InstantSearch`. Off stops Arena from
  forcing `ActiveSearchContentOperation.Instant`; native Attention Elite lucky
  searches and the separate automatic corpse-search chain remain possible.
- **ARENA HUD** maps to the default-on `HUD/CombatElements`. It groups killfeed,
  hitmarker/kill skull/damage numbers, and the absorbed health/weapon panels.
  Individual visibility preferences are never rewritten. Score, readiness and
  round notices, powerup status, team markers and all audio remain independent.
  The pre-existing overall `HUD/Enabled` master still overrides all of them;
  when off, the map-screen combat-HUD switch shows N/A with an explanation.

The combat visuals share a fully stretched CanvasGroup at unchanged coordinates.
Only its alpha changes; TMP objects keep their normal active lifecycle. All cue
lifetimes still tick while hidden, new combat events are not queued while off,
and info panels stop sampling/hide until enabled again. The group is owned and
destroyed by the existing Arena-only per-match canvas. No native hooks, ordinary
raid behavior, profile operations or server state were added.

The menu keeps its existing bounds and typography. Twenty controls occupy two
columns of ten, using 22 px buttons and 19.2 px descriptions without moving the
map preview or native navigation. The helmet icon now uses Skoll's Stahlhelm;
source, license, raster changes and hashes live in HUD-ICON-LICENSES.md and the
existing icon manifest. Vest pixels, HUD dimensions and live tint are unchanged.

Independent runtime/artwork review found no P0–P2 issue. Offline and native
acceptance evidence is recorded separately in the dated GPU results report.
