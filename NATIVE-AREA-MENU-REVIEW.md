# Native Arena area selection

Date: 2026-09-07. Branch: `feature/native-area-menu`, based on `ec11aa8`.
Scope: expose the two user-tested bounded prototypes in the existing Arena map
screen. No new area geometry, team sizes, AI, server/profile code or game modes.

## User-visible contract

| Selection | Available areas | Population |
| --- | --- | --- |
| Independent TDM, Factory daytime | Whole map / Factory South | Existing configured teams / fixed 2v2 |
| Independent TDM, Customs | Factory Center, automatically selected | Fixed 2v2 |
| Independent TDM, Factory nighttime / Labs / Labyrinth | Whole map | Existing configured teams |
| Other solo Arena modes, existing maps | Whole map | Existing mode rules |
| Protected PitFireTeam Tactical, existing maps | Whole map | Existing protected 5v5 |

The PLAY AREA panel sits below the mode sidebar. It shows the selected area,
fixed/configured team size where applicable, and mode restrictions. With no map
selected it asks the player to select a map. Whole-map Customs remains forbidden.
Customs is visible in independent TDM regardless of the saved F12 area. Selection
persists Factory Center before accepting that map. Leaving TDM hides Customs
and clears the native selection rather than changing the mode back silently.

The picker and F12 share `Team Deathmatch/ExperimentalArea`. Compatible selections
survive refresh/back; incompatible map, Factory time or mode changes resolve to
the new map's valid default during menu selection only. Factory South -> Factory
night resolves to Whole map. Switching back to day keeps Whole map until South
is selected again. Explicit server disables and edited allowlists still apply.

## Implementation and native ordering

`ArenaAreaSelectionState` is a pure proposal filtered through the existing shared
`ArenaAreaCatalog.TryResolve`. It is not a frozen or persisted raid selection.
`ArenaMenuSelection` separates discovery from exact saved-area admission. The
menu adapter must own the exact selected `RaidSettings`, write the proposed area
successfully, then pass strict availability before accepting a map. Final claim,
freeze and local-start remain strict; they do not call the repair path.

Native sources inspected under `Development/_decompiled`:

- `EFT.UI.Matchmaker/MatchMakerSelectionLocationScreen.cs`: ShowInternal creates
  native rows before the Arena postfix, then map click resolves time and level,
  assigns SelectedLocation and dereferences its ID. A failed area write must be
  rejected with a null click input before that non-null native branch. Direct
  time/group setters are guarded separately, using the incoming map ID.
- `EFT.UI/BaseDropDownBox.cs`: the new controls clone only the dropdown item
  template, with fresh click events; no popup owner or native selection event is
  borrowed. `HoverTooltipArea` callbacks retain their native lifecycle.
- `EFT.UI/LocationButton.cs`: native `Select(false)` still selects. The existing
  owned Customs row clears its toggle directly instead, then clears the map.

All configured map rows are materialized before the mode view is available.
Filtering these through strict eligibility would hide every normal row when
PFT's live policy initially rejects a stale F12 mode; Tactical repair happens
after native creation. Customs is separately hidden/shown with its owned view.

The area panel owns its rows and listeners and reuses them when option IDs stay
the same. A rejected click never changes the highlight optimistically. An old
callback cannot dispose a newer panel after Close/Show: generation mismatch
returns without cleanup. Current-owner disposal clears the updater, area/mode
views and restores native extras. No repeated native ShowInternal or global
subscriptions. Panel geometry is left 56, top 668 (704 for PFT), width 360,
height at most 188 in the existing 1080-unit layout; actual fit needs GPU capture.

## Verification

Reproduce with `tools/NativeAreaSelectionChecks/Verify.ps1 -Stage Rules`,
`-Stage Integration`, and `-Stage Build`, with `-SptRoot` for another installation.
Build automatically deploys; serialize it and coordinate the mirrored test host.
Rules link the actual policy/menu/start source and inject persistence/transport
failures. Negative controls compile copied mutants, never edit deployed source.
Native-hook checks use verified native ordering with game/UI stand-ins; they do
not prove Unity layout or live Harmony dispatch. Full server/data results and
the four final built/installed DLL hashes are recorded in local artifacts.

Final verification: all three runner stages passed in a serialized rerun.
NativeMenuChecks passed 324 assertions, NativeAreaHookChecks 63, mode selection
130, native map view 60 and Labyrinth access 50. Six copied mutants failed their
intended assertions: hidden Customs discovery, wrong settings owner, ignored
write refusal, late click rejection, PFT missing rows and stale generation
cleanup. The hook baseline passed again after mutation runs.

Release solution plus final explicit Release companion/client build succeeded;
all 330 server/data tests passed, zero skipped. Existing compiler/analyzer
warnings remain; no build errors. Independent adversarial source/native review
has no unresolved P0-P2 findings after the PFT materialization and stale-callback
fixes. These are offline and source-review results, not live UI acceptance.

Final Release outputs matched installed files under `D:\SPT41` byte-for-byte:

| DLL | SHA256 |
| --- | --- |
| ArenaMode.Client | `D4FD865FC13F2C6FC6E798F3E24B3AD4E1503D836E5B191175971E45134B8202` |
| ArenaMode.BigBrainCompat | `648D7672DC053AE5C04FD8C8D8CFAEA231ABB6641BDEF500E2CE54E24707E656` |
| ArenaMode.Server | `CAD32C23591CCB0802A5AD9A9566305A331CBE1DFF1A30FB0C60DE5DC99EC4B7` |
| Redeploy.Core | `256E4D848216565B6F921F5555A11CAA7C78422C51689EA013C417BA5590D6B5` |

Detailed local evidence: `artifacts/area-menu-checks/stage-*.log`,
`artifacts/area-checks/area-Build.trx` and `artifacts/area-checks/deployment.json`.
The normal-raid/profile safeguards, area catalog and wire contract are unchanged.

## GPU handoff

The user confirmed the GPU game was stopped before deployment. This development
host has no GPU. Tool discovery in this task exposes no connected remote native
game-control surface or accessible GPU task; the available CUA surface here is
browser-only. No autonomous raid or new GPU menu test was performed. Syncthing
reported SPT-GPU connected/unpaused during work; that is not remote process,
artifact-hash or visual verification.

After restarting the game, check in the existing GPU session (no raid needed):

1. Arena -> TDM -> Customs from saved Whole map: tile visible, Factory Center and
   fixed 2v2 shown, native Next enabled without F12 work.
2. Factory day: Whole map/South selection and persistent marker; switch to night
   and confirm Whole map, then return to day and select South again.
3. Customs -> another mode clears the map and hides Customs; returning to TDM
   restores the tile. F12 changes refresh choices without duplicate controls.
4. PFT roster with a stale non-Tactical F12 mode: Tactical repair leaves normal
   maps present; only Whole map and protected 5v5 are shown, no Customs.
5. Back, normal PMC/Scav map screen, and Arena reentry: no leaked or duplicate
   controls; native Back/Next/time controls remain accessible at the test scale.

Boundary polish and CTF remain separate future work. Existing user raid results
for containment do not establish this new menu's visual acceptance.
