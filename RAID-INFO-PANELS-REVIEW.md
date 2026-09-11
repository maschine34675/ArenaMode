# RaidInfoPanels absorption

2026-09-11. The user requested absorbing their RaidInfoPanels project into
ArenaMode and retiring the standalone local project so both HUDs can share a
visual design. Source: `maschine34675/RaidInfoPanels-dev`, commit
`55ee6009592431167fefbbacca9faae3ccf8f51a`. This is the author's own code and
purpose-built generated artwork. No external mod code or DLL was added.
`tools/RaidInfoPanelChecks/SOURCE-IMPORT.json` records source-file hashes;
all 26 runtime PNGs are preserved byte for byte. The four source generators
are retained under `tools/RaidInfoPanelIcons` with their output paths updated.

## GPU failure and construction correction (2026-09-11)

**r2 outcome (`2a57e7d`):** both panels, direct-rig RGD-5 counts/throws, armored-rig
reserve counts, F12 layout controls and fresh post-respawn values are observed
working at 1920x1080. Two exact success markers per module and zero duplicate
CanvasGroup errors or panel/extraction exceptions were independently counted
in the synchronized final r2 log. The native named list is hidden in two Arena
starts and restored in normal PMC. This is bounded acceptance, not a general
pass: inventory overlap failed, and input-dependent/PFT/transition cases remain
open as recorded in `GPU-PANELS-RETEST-RESULTS-2026-09-11.md`.

**r2 follow-up:** panels now additionally require native BattleUI and a closed
local inventory. The whole match-owned Arena canvas stops drawing over native
inventory/pause/settings pages; its GameObject and all expiration ticks remain
active. Panel Hide resets refresh validity, so returning to BattleUI refreshes
data immediately, regardless of the configured interval. No stored position or
opacity changes. Native readers verified in ScreenManager.CheckCurrentScreen,
InventoryScreenController.ScreenType and Player.IsInventoryOpened; context and
canvas-helper regression tests exercise both guards. Actual ArenaHud tick
continuation/wiring is independently reviewed, not simulated event-expiry proof.
The separate remaining green extraction banner is hidden through its verified
Description child, preserving the numeric Clock sibling. New GPU checks needed.

`GPU-PANELS-TEST-RESULTS-2026-09-11.md` rejected the initial delivery: both panels
were absent despite enabled settings. The user's follow-up log repeats the
construction NRE at 14:24:46.740, before the independent extraction-list failure.
The absorption had moved `_root.SetActive(false)` before the two constructors.
In the installed Unity.TextMeshPro.dll, `TextMeshProUGUI.Awake` initializes
`m_canvasRenderer`; `SetOutlineThickness` uses it directly. Constructing the
text under an inactive parent delays Awake, then the outline setter dereferences
the missing renderer. The archived original constructed both panels before
`HideAll`. Restore that sequence: active synchronous construction, hide before
return, no frame/yield in between. Normal-raid/body/spectator gates are unchanged.
One construction success and bounded full exception diagnostics aid the retest.

The panel harness now models the active-hierarchy prerequisite and rejects a
source fault restoring the early deactivation. It still substitutes the actual
view constructors: this tests the evidenced lifecycle boundary, not native TMP
or rendered output. The old suite missed this boundary. Client build and icon
verification also cannot replace the pending GPU retest.

The separate extraction service had assumed Unity permitted another CanvasGroup
on PointsPanel. The live log proves otherwise (1,826 rejected additions in the
captured user run). That service now borrows the existing group's alpha/enabled
state, restores it on release, adds no components and quarantines failed lists.
Its fake now enforces the observed single-component contract. Neither error was
caused by the grenade row or the later grenade-picker fix.

## Preserved functionality

- Weapon name, magazine/chamber count, cartridge, caliber, fire mode and
  underbarrel-launcher information.
- Spare magazines in rig/pockets and backpack, with full/partial indication;
  the existing loose-round fallback and secure-container exclusions remain.
- Total HP, carried weight, front/back armor-zone puppet, damaged-zone rows,
  and bleeding/fracture badges. No hydration/energy panel is reintroduced.
- Both panel positions, scale, opacity, colors, visibility, warning thresholds,
  refresh interval and weapon-name preference.

The modules live in `ArenaMode.Client/Hud/InfoPanels` under Arena's existing
match canvas, font resolver and text-outline style. There is no additional
BepInEx plugin, standalone assembly dependency, Harmony patch or persistent
Update component. This preserves the existing panel layout; a wider visual
redesign can now happen inside ArenaMode.

**Armored rigs are included in reserve counts.** The retired Claude context's
"Armor-Vest-Grids ignored" wording refers only to the separate `ArmorVest`
equipment slot, not to armored rigs. Native `EFT.InventoryLogic/Inventory.cs`
lines 193–197 defines quick access as `Pockets` and `TacticalVest`;
`InventoryController.cs` lines 882–902 uses these same slots. The installed
database puts AVS (`544a5caa4bdc2d1a388b4568`, 12 grids) and TV-110
(`5c0e746986f7741453628fe5`, 9 grids) under the Vest parent accepted by the
`TacticalVest` slot, despite their armor components/plate slots. Both magazine
and loose-round counters already include descendants of that slot. The imported
behavior needs no functional change for armored rigs; an offline counter
regression protects that boundary.

## Boundaries and lifecycle

Only the current Arena match can display the panels. Data additionally requires
the exact active game/world, native Started state, a usable living local player
in the world's alive list and first-person view. Loading, ordinary raids,
death, body replacement and pending/active spectating hide them. New bodies
refresh immediately. The panels read inventory/health state; they do not edit
items, profiles, weight limits or health.

Each panel handles its own update failure and stays hidden until a successful
refresh, so stale values cannot reappear on the next throttled frame. Warnings
are bounded per panel/match. Refresh intervals are clamped to 0.05–2 seconds;
NaN/infinity use 0.1 seconds. Theme events are unsubscribed on teardown; static
inventory scratch buffers are cleared after reads, including failed reads.
Match-owned sprites/textures are destroyed and caches released, while borrowed
game icons and fonts are never destroyed. Weight warnings now use the actual
player's Physical overweight state/limits, including Arena's carry allowance,
instead of the original global threshold.

## Settings and migration

F12 entries move to ArenaMode's **HUD Panels - General**, **Weapon**, **Status**
and **Appearance** groups. All 17 current keys are imported as initial defaults
from `BepInEx/config/com.maschine.RaidInfoPanels.cfg`. Existing Arena values win,
including already persisted but not yet bound entries. Old unused Armor/Health
groups are ignored. Import opens the legacy configuration with saving disabled;
the legacy file is deliberately retained at that path for the next game start.
In this installation the Status panel's custom offset `(24, 245)` is preserved.
Arena's overall HUD switch also controls these modules.

If a synchronized test host still loads the retired DLL, the Arena module
temporarily disables only `RaidInfoPanels.HudOverlayController` and its named
`RaidInfoPanelsCanvas`, restoring their prior states at teardown. It does not
destroy or disable the shared BepInEx plugin host or another mod's canvas.
Removing the standalone DLL is still the permanent installation change.

## Verification and retirement

`tools/RaidInfoPanelChecks/Verify.ps1` exercises linked production lifecycle,
context and settings code against controlled external boundaries. It does not
simulate Unity rendering or certify native BepInEx file serialization.
`Build-Verify.ps1` compiles the actual client against installed EFT/Unity APIs,
then reads PE metadata without loading game code: all 26 resource byte hashes
must match the original source, exactly one Arena plugin must be registered,
and no RaidInfoPanels assembly reference may remain. The staged build keeps
installed client/server/dependency DLLs unchanged.

Independent comparison/review found no unresolved P0–P2 issue in the original
functionality, match/body gates, migration, cleanup or legacy suppression.
Parent verification passed 180 assertions and seven controlled source faults,
including armored-rig counting. A separate copied DLL with one corrupted
embedded icon (and a recomputed outer DLL hash) was correctly rejected by the
resource-content check. Real client build: zero errors, four existing CS8632
warnings. None of these checks certifies rendering or native audio playback.

Before local retirement a complete ZIP (including ignored files and `.git`),
verified all-ref Git bundle and installed DLL/config backups were created at:

`D:\SPT41\Development\_archive\RaidInfoPanels-2026-09-11-f8c969a4dffa4335987a02ade57e66fe`

The archive contains all 394 original files. Its manifest records every hash,
and ZIP readback verified the exact set and bytes. Final delivery rechecks the
source against that manifest before deleting only the explicitly named local
`D:\SPT41\Development\RaidInfoPanels` checkout and standalone installed DLL.
The remote GitHub repository is unchanged. Keep the legacy CFG for migration.
The final delivery receipt and client build identity are recorded in HANDOFF.md.
Delivery from clean commit `1e6d350` completed on 2026-09-11: installed client
SHA256 `6C660F3B3F8B39ED6B54F3E9960605D4A437FA63CE277A29DC0D7DC7FB190B72`,
standalone local checkout/DLL absent, legacy CFG retained, protected files
unchanged. Receipt: `artifacts/raid-panel-delivery/9b26d7afe2414856bc9d2e5c62b24440/receipt.json`.

GPU acceptance still belongs to a later user test: verify both panels after game
restart, custom positions and live F12 changes, kit swaps/reload/reserve counts,
armor damage/effects, death/killcam/spectating/respawn, a second raid, and no panel
in an ordinary raid. Check at the actual screen resolution for collisions with
native or Arena HUD elements. Only a game restart is needed; no server change.

## Grenade row (added 2026-09-11, after the absorption)

The weapon panel gained a row counting the local body's hand grenades as
**frag / flash / smoke**, requested by the user for ArenaMode. Hand grenades are
`EFT.InventoryLogic.ThrowWeap`; 40/30 mm launcher rounds and rockets are
`Ammo`/`Rocket` and never match. Classification uses `ThrowWeapType`: stock
items only use frag, flash and smoke; `stun_grenade` joins flash as the bots
group them; unlisted/modded types fall back to the template fields the
explosion code reads (fragments or blast radius, blindness, emission).

**Ready** is the exact candidate list of the native tap-G, taken from the same
virtual call (`GetReachableItemsOfTypeNonAlloc` with `Examined`, which appends
and never clears). In vanilla that is the top level of rig and pockets; the
local controller also skips an unsearched rig/pockets. The installed
**UseItemsAnywhere 1.4.0** rewrites `Inventory.FastAccessSlots` at startup to
add Backpack, SecuredContainer and ArmBand (see KIT-POLISH-REVIEW.md), so loose
backpack grenades are genuinely ready there and are shown as such. **Stored**
is the carried rest (nested pouches, backpack) not in that list, shown dimmed as
`+N`; a kind that is only stored shows `0 +N` with a dimmed icon.
SecuredContainer and ArmorVest are not walked for "stored": no stock secure
container accepts ThrowWeap (its ExcludedFilter lists the ThrowWeap node) and
stock armbands have no grid. Ready is counted from the native list itself, so
a ready grenade outside the walked subtrees is still counted.

Known exception, documented at the code: tap-G prefers
`Equipment.TopPriorityGrenade`. A grenade drawn earlier through a bound number
key (Arena's reach patches allow binding one outside rig/pockets) stays that
pick, so tap-G can draw a grenade this row shows as stored. The same review found
a pre-existing, separate defect in that path (a thrown backpack grenade leaves a
stale pick that blocks tap-G until hold-G); it was fixed separately in the reach
feature, see GRENADE-PICK-REVIEW.md. That fix only moves an undrawable pick on; a
drawable backpack pick still takes precedence, so this exception stands.

The row is inventory state, so it keeps updating while a grenade, knife or meds
is in hand, exactly when the remaining count matters; only the firearm lines
clear. It is hidden when no grenade is carried. As first delivered it had its
own permanently reserved row (panel 280x160, row at y=-126); the follow-up below
moved it into the magazine row. Scratch lists are cleared after every read,
including failures, and on match teardown.

Icons: three new generated glyphs (`grenade_frag/flash/smoke.png`) from
`tools/RaidInfoPanelIcons/gen_grenades.ps1`, which writes only those files and
puts its preview in ignored `artifacts/`. Their hashes live in
`tools/RaidInfoPanelChecks/ARENA-ICONS.json`; `SOURCE-IMPORT.json` is unchanged.

Verification: an adversarial review (three lenses, each finding independently
refuted) found no P0-P2 issue. Its P3 findings were fixed before commit: ready
is now counted from the native list (UseItemsAnywhere secure-container grenades
were previously uncounted), the equipment is read through the controller as
natively, the comments describe the real install, the stand-in models the
mutable slot list and the unsearched gate, and the exception-path check is no
longer vacuous. Offline suite: 247 assertions and 14 rejected source faults
(the seven original controls unchanged, seven new grenade controls). Staged real
client build: zero errors, the four existing CS8632 warnings; Build-Verify:
26 imported plus 3 authored icons byte-exact in the DLL. None of this
certifies rendering or in-raid behavior.

GPU acceptance for the row: counts with the default kit's RGD-5s, the ready
count dropping after a tap-G throw, counts while the grenade is in hand, a
grenade in a nested pouch and in the backpack (bright on this install because of
UseItemsAnywhere, dimmed `+N` only in a nested pouch), flash/smoke if a kit
carries them, correct counts after death and respawn, no row without grenades,
and no overlap with the killfeed or other bottom-right HUD elements at the real
resolution.

## Grenades in the magazine row (2026-09-11, user follow-up)

After seeing the separate row in game (r4/r5 captures: one or two magazine
slots and a single frag count, most of the width empty), the user asked to put
the grenade counts into the magazine row. The weapon panel is back to 280x132.

- One reserve row at y=-94. Magazine or loose-round slots pack from the left;
  grenade kinds pack from the right with frag outermost, then flash, then smoke.
  Hidden slots take no room, which replaces the fixed 88 px magazine pitch
  (a missing full-magazine count now lets the partial icon start at the left
  edge); each icon still says what it counts. Counting is unchanged.
- A slot is its icon column plus the count's width as TMP measures it
  (`GetPreferredValues()`), recomputed only when a count changes. That
  measurement is valid because every panel text is created on the active
  canvas. Checked in the game's own `Unity.TextMeshPro.dll` (decompiled with
  ilspycmd 11; TMP is not part of `../_decompiled`): `TextMeshProUGUI.Awake`
  sets `m_isOrthographic`, without which `CalculatePreferredValues` scales by
  0.1; the width pass re-parses the current text, skips rich-text tags and does
  not disturb the next mesh rebuild. A zero, non-finite or implausibly small
  result (below 0.25 em per glyph) falls back to a per-glyph estimate, so
  slots can sit too far apart but never overlap. Count texts now overflow
  instead of ellipsizing.
- Within 248 px of content: 12 px between magazine slots, 10 px between grenade
  slots and at least 14 px between the groups; both groups share the 30 px
  row's midline. The HUD font's digits measure 10 px at 18 pt in the r4
  capture's ammo line, so every magazine kind with frags and flashes at
  single-digit counts needs about 241 of 248 px (about 245 at the 10.8 px one
  reviewer read from a "77"). A third grenade kind, stored `+N` grenades or
  multi-digit loose-round counts can tip it: the grenades then wrap onto a
  second row at y=-124 and the panel grows to 160. It is bottom-anchored, so
  every line above moves up one row; to keep that from flickering, a body
  keeps the wrap until it is replaced (drawing a grenade or knife hides the
  magazines, and counts cross digit boundaries). 160 is the size r4/r5 tested
  with the permanent row, and this is the only height change.
- The geometry lives in `ReserveRowLayout.cs` without Unity types, so the
  offline checks link it; the views only measure and place. The panel was
  280x132 before the grenade row, too.

Verification: independent adversarial review in three lenses (view geometry
and state, TMP/Unity runtime contract, tests/docs). No P0/P1. Fixed before
commit: the wrap flipping with the hands state or a digit boundary (now kept
per body), the grenade slots' slack growing left (content 4 px inboard, gap
10 instead of 14 px), the 1 px midline offset between 30 and 28 px rows, a
half-vacuous empty-group check, an order check that only saw frags, a
fallback bound below the real digit width, and overstated wrap comments.
Offline suite: 292 assertions and 27 rejected source faults (the eighteen
existing controls unchanged, nine for the reserve row). Staged real client
build: zero errors, the four existing CS8632 warnings. None of this certifies
rendering or in-raid behavior.

GPU acceptance for the merged row: the default kit (full/partial magazines plus
RGD-5s) on one row with frag at the right edge and clear space between the last
magazine count and the first grenade icon; grenade counts staying at the right
while a grenade, knife or meds is in hand; a shell-by-shell weapon with loose
rounds plus grenades; counts after a throw, reload and respawn; if a kit allows
it, full, partial and backpack magazines with frags and flashes (the tightest
one-row case) and a third grenade kind to see the wrap, which must then stay
until the next respawn; unchanged or larger distance to the killfeed.
