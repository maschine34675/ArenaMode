# HUD/utility mutators and Stahlhelm — GPU2 results

2026-10-11. User released SPT-GPU-2; the focused menu and raid test passed.
Local evidence: `artifacts/hud-mutators-native-20261011/` (ignored).

## Build and deployment

- Helmet artwork: `5eaeda3`; runtime/menu/checks: `a51b34d`.
- Client SHA256: `915E398D468DA03C0F60287ABBEFDD28EDC5DB23A8372800B8351A8D6B13FA32`.
- Staged manifest: `artifacts/double-jump-build/5d14fbacfc9447c7855e80d385d08e40/manifest.json`.
- Real client build: zero errors, four existing nullable warnings. Nine imported
  and five generated embedded icons verified; seventeen retired icons absent.
- Client and updated icon attribution deployed locally and to GPU2; no server,
  Core or other-mod deployment. Unrelated untracked Molotov assets untouched.
- Fresh process `5791b5fb65354647bae15b039ea709ed`, started 18:05:53 local.
  All 43 plugins and five libraries matched local disk, GPU disk and loaded MVIDs.

## Native observations

Factory day, two-person continuous Deathmatch with the fixed Spartan kit:

1. At 1920x1080, the map screen showed all twenty mutators with readable titles,
   full one-line descriptions, ON/OFF/N/A colors and unobstructed native buttons.
   Actual clicks changed AUTO QUICKSLOTS, INSTANT SEARCH and ARENA HUD from ON to
   OFF; live config reads confirmed the three intended entries became false.
   Screenshots/reports: `menu-on/` and `menu-off/`.
2. The raid started with all three off. Combat panels were absent while score,
   rival and match timer remained visible. Read-only inspection confirmed
   `ArenaCombatHud.alpha=0`. `FastAccess.BoundItems` remained empty after more
   than a minute with the supplied medical kit. Evidence: `raid-off/`.
3. Enabling HUD/CombatElements live changed alpha to 1 and restored health,
   armor/helmet and weapon panels immediately, without a respawn. The new
   Stahlhelm and existing Kevlar vest were visually verified at normal HUD size.
   No layout/tint changes were apparent. Evidence: `raid-on/`.
4. Enabling AutoQuickSlots bound AI-2/Esmarch/Bandage/Splint/Analgin to 4–8.
   Five native dictionary entries and five log lines confirm this. Switching
   it off again retained all five bindings, as intended; it was then re-enabled.
5. A real M4 kill with HUD on produced a killfeed entry, score and hitmarker-audio
   request. Both hitmarker and skull timestamps became `398.635864`. A second
   real kill with HUD off increased the scoreboard to 2 kills/500 points and
   still requested kill audio, but neither timestamp advanced and no combat
   panels or killfeed entry were visible. Re-enabling did not replay that kill.
   Read-only inspection and tool responses establish the transient event gate;
   the screenshot arrived after the short skull/number animation had faded,
   so this is not a captured-frame certification of damage-number projection.
6. Inventory opening hid Arena overlays normally, while vanilla inventory and
   the five bound quickslots remained visible. Closing it restored HUD panels.
   Screenshot/report: `inventory/`.

INSTANT SEARCH was verified through the real menu/config entry and its existing
native constructor guard was checked against the installed decompilation. No
new corpse/container search was timed in this raid. Turning it off removes
Arena's forced Instant flag; native Attention Elite and automatic search chaining
are separate behaviors. Other modes and arm/live menu locks have production-
linked offline coverage, not separate native raids in this run.

Godmode and safe bot-near teleport/aim/fire hooks were used for deterministic
combat checks; there were no other human raid participants. All hooks ended off.
One unrelated bridge session polled during the run but there were no foreign
action records or foreign config/artifact changes. The initial gpu-run raid
recipe was invoked from SelectLocation, where it expected MainMenu; it timed out
without starting anything. The existing native NEXT/READY buttons then started
the raid successfully. An abbreviated plugin name in one config-set was rejected
without changing state; the exact GUID worked. Neither tool usage error was an
Arena failure. A later weak inspector handle expired and was not reused.

## Exit, logs and preservation

Protected Disconnect returned to MainMenu. Log export covers #1–#3179 with no
missing buffer coverage; one pre-existing long stored line was truncated at
2,000 characters. **Zero Error/Fatal and no Arena warnings.** Existing warnings
were three Harmony declarations, the intentional RespawnApiProbe throwing
subscriber, stale PerfectCulling sampler cleanup and Factory's native negative
BoxCollider scale at TRANSITS/Road_to_woods.

Config restore: four reset, three already at baseline, no conflicts or missing
entries. All pre-existing configuration assignments match baseline. The sole
new assignment is the intentional default `HUD/CombatElements=true`, retained
explicitly in `preservation.json` rather than treated as an old preference.

Whole-profile comparison found only `Health/UpdateTime` and the existing hideout
bookkeeping timestamp. No inventory, currency, quest or equipment changes.
All twelve PFT companion files are byte-identical. Of 115 recorded DLL paths,
only the intended local/GPU Arena client copies changed. Final status and
preservation audit are saved; GPU2 is at MainMenu with the server running.

## Offline verification and review

- TdmMatchMenuChecks: 1,960 production assertions, 32 compiled/rejected source
  faults, final unmodified production run green.
- RaidInfoPanelChecks: 368 assertions, 35 compiled/rejected source faults,
  including the new combat guard and unchanged individual panel preferences.
- AnnouncerChecks 330 and KillfeedTextChecks 120 assertions passed.
- Independent runtime/artwork review found no P0–P2 issue. The root inspected
  the 20 px preview, actual menu and raid screenshots, final source-fault output,
  installed/loaded artifacts and complete preservation result.

No push. Ordinary PMC, PFT, other resolutions and a fresh native search operation
were not separately exercised; no acceptance claim is made for those paths.
