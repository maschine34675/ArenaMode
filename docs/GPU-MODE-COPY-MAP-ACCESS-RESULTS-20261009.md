# Unified mode rules and expanded map access — GPU2 results

Date: 2026-10-09. Source commits: `1649ccf` (mode copy) and `9c67ed7`
(map admission). This report records representative native checks, not acceptance
of every map/mode, PFT or multi-round combination.

## Delivered behavior

Every mode uses a short description followed by the same **OBJECTIVE / RESPAWN /
EQUIPMENT** facts, then its own adjustable rules. The facts remain visible for
Deathmatch and Team Deathmatch and react to FFA/all-vs-player, killcam and gear
choices. Solo/PFT variants retain their actual different rules. No new settings.

| Map / area | Deathmatch | Gun Game | Tactical solo / PFT | Team DM | Zombies solo / PFT |
|---|---|---|---|---|---|
| Bay5, whole map | Yes | Yes | Yes | Yes | Yes |
| Equator, whole map | Yes | Yes | No | Yes | Yes |
| Customs, whole map | Yes | Yes | Yes | Yes | Yes |
| Customs Center / Side / Yard | No | No | No | Existing 2v2–5v5 | No |

Equator cannot reliably supply five Tactical opponents outside the unchanged
25 m spawn floor when a surviving player starts the next round near the atrium.
The three Customs contours require their existing TDM runtime owner for
containment, team reservations and respawns; another mode needs an adapter.
Neither restriction was bypassed. Optional-map installation, server allowlists,
participant caps, PFT admission and ordinary-raid guards remain intact.
See [MAP-MODE-ACCESS-REVIEW.md](MAP-MODE-ACCESS-REVIEW.md) and
[MODE-COPY-SCHEMA-REVIEW.md](MODE-COPY-SCHEMA-REVIEW.md).

## Exact deployment and loaded identity

Both installed client and server ProductVersion:
`0.1.0+9c67ed74ea032c239656599536ee645b53e9fc7a`.

| Component | SHA256, identical locally and on GPU2 |
|---|---|
| Arena client | `77F72EC9E932B05B3E2758C7EF15058125D8B0B7BEC35EB6F54CBC4B5FF8982D` |
| Arena server | `C0871C34B4F1AFAAE990777E39827EDCC1BAF7A6C74401A1A0A47AD73DE49307` |
| Core, unchanged | `673D9A106F747E5EE0A70EDE21E9DE9E5AB48E5DDE12E0B2922B2139ECDFE4DD` |
| UiBridge, unchanged | `306606209ADCFF09B94BC1DC01B574C27609F39AB23234E76D915EDE9223D79E` |

UiBridge ProductVersion `0.7.4+8218b027478e61f98b4119d179fc2a8fe41c928a`.
Native instance `0a493f4f53024ac4a645b29bf05716e2`, profile `SPT-GPU-2`
(`LOCAL_TEST_PROFILE_2`), EFT `0.16.9.5-40743-f137e819`, Unity
`2022.3.43f1`, 1920×1080. Loaded/disk/local client identity checks matched 21/21
at both ends of the run. Core matched; BigBrain, PFT and assets were not changed.

The client was built without deploying first; zero errors, four existing
warnings. Staged manifest:
`artifacts/double-jump-build/365fa4c7234049f3a5fa284e73575d35/manifest.json`.
Server build: zero errors, two existing warnings (CS8620/MUD0002).
The GPU2 server was stopped gracefully before replacement, restarted, and the
game launched against the final artifacts. No Git push.

## Offline checks

- Full unit suite: **909 passed, 2 existing skips, 0 failures**.
- Mode catalog/state: 157 assertions and 5 rejected compiled faults.
- Production rendered mode/settings views: 1324 assertions and 24 rejected
  compiled faults. Parent reran the baseline after integrating both leaves.
- Map admission and real production spawn selection against installed location
  data: 501 assertions and 6 rejected compiled faults, fully rerun by parent.
- Parent integration checks: NativeMenu 607, NativeAreaHook 189, NativeMapView 62,
  ArenaKillcam 170 assertions.
- Additional map leaf checks: AreaConfig 250, AreaTeamView 30, CustomsRefresh
  4492; native area selection 3 menu + 3 hook compiled faults; imported-map
  checks 5 compiled faults. Server matrix 24 and quiet-clone isolation 14 cases
  are included in the unit suite above.
- Independent adversarial review identified the Equator mid-map capacity
  counterexample before delivery. The resulting narrow guard was reviewed and
  exercised; no remaining source-level issue found in this change.

These are source/runtime-double checks, not substitutes for native navigation.

## Native menu checks

Actual clicks, UiBridge text/state and Windows screenshots confirmed:

- All five solo modes show the shared facts, with complete readable labels.
  Deathmatch FFA → All vs player updates the description immediately.
- Team DM killcam enabled changes RESPAWN to **After killcam**.
- Zombies own-gear selection preserves the facts; Tactical reads **1v5** and
  explains the optional PFT/four-companion alternative.
- Equator is absent in Tactical, present in Team DM. Optional-map time/Ready
  controls and native confirmation work for the accepted choices.
- Customs Team DM has four choices: Whole map, Factory Center, Factory Side,
  Customs Yard. Whole map exposes total participants (10 / 5v5 in this check).
  Switching to Center shows its polygon and 2v2/3v3/4v4/5v5 buttons, retaining
  the saved 5v5 choice. The panel and native Next/Back/Ready controls fit.

PFT menu variants and unavailable/custom-config edge cases were checked offline;
this task did not invite companions or play a new PFT map combination.

## Representative native raids

Times below are GPU client log time (UTC+2). The UiBridge sequence numbers refer
to the archived 4698-entry log export.

### Equator — Team DM 2v2

12:44:59.883: local USEC, one allied bot, two opponents. All three requested
slots admitted; final opponent at 12:45:09.278 (#3469). Assembling → Countdown
at 12:45:10.284 (#3475), Countdown → Live at 12:45:13.283 (#3482), kill limit 50,
time limit 600 seconds. A BEAR replacement admitted after a death at
12:45:18.599 (#3509), exercising population refill. Protected leave returned
to MainMenu in 18 seconds.

### Bay5 — solo Tactical 1v5

Normal map/time selection and buy screen. Purchased Wraith; balance changed
from 250000 to 29000. Five pmcUSEC requested at 12:48:00.194 (#4352), kit
respawn completed at 12:48:01.874, all five dispatched at 12:48:19.808 (#4423).
Round 1 live at 12:48:20.825 with **five active opponents, zero eliminated**
(#4439). Native inspection confirmed all five Active and multiple bots moving.
Protected leave returned to MainMenu in 19 seconds. Later rounds were not run.

### Whole Customs — solo Zombies, own gear

Temporary test settings: four initial/four maximum infected, no shooters.
Match started at 12:50:54.360 (#4605); four infected activated at 12:50:55
(#4620–4630). Native movement/targeting observed. Two initially about 180/214 m
away approached to 0.7/0.8 m, using ZombieFast behavior; after disabling the
temporary godmode, an actual melee hit reduced chest health from 85 to 75.

The other two remained roughly 301/404 m away during this short test, one still
patrolling. Full-map access works, but **whole Customs is not balanced for dense
Arena encounters**. Distant random native zones and full-map travel times need
a separate pacing pass; this change does not certify every path connection.
Protected leave returned to MainMenu in 22 seconds.

## Open native observation: melee teardown callback

One Arena damage diagnostic occurred while leaving Customs during a zombie
attack. `Stop(Left)` was committed at 12:52:23.842 (#4666); the exception followed
244 ms later at 12:52:24.086 (#4668); session end followed at 12:52:24.910.
No corresponding error appeared before leaving. This is a real open teardown
defect, not a claim of an error-free run.

Independent read-only analysis matched installed Assembly-CSharp MVID
`cc2d80b06d5b4cb1a5816d2cc901d4c7` to the stack. The reported offset `0x005e`
in `EFT.BotMemory.GoalTargetBeingHitAction` invokes
`BotMeleeWeaponData.HitCurrentEnemy` through `WeaponManager.Melee`.
Native Stop → StopGettingInfo → BotsList.Stop → BotOwner.Dispose reaches
`BotWeaponManager.Dispose`, which sets `Melee = null`. `BotMemory.Dispose`
does not unsubscribe the target's `BeingHitAction` callback. A pending melee
animation can therefore reach this callback after the AI weapon state has been
disposed while both physical bodies still exist.

The same callback error was already recorded for October 6/7 in
`ZOMBIE-MODE-REVIEW.md`; those older exact null receivers were not reverified.
The current timing/IL strongly supports the existing teardown problem, with
no evidence of a new Customs capacity blocker. No broad exception suppression
or unrelated teardown patch was included. Native exit completed and independent
profile comparison below passed.

## Preservation and remaining boundary

All 215 original Arena config values were restored, then the original file
bytes restored after EFT closed. SHA256:
`E242E0BCFDB43FF81A195658F0E10BF60695DD91C4127156B76B7D2C6106F811`.
All twelve PFT companion files remained byte-identical. Full player-profile
comparison found only `characters.pmc.Health.UpdateTime` and
`characters.pmc.Hideout.sptUpdateLastRunTimestamp` changed; inventory, progression
and companion state did not change. No profile file was manually rolled back.

EFT is closed; GPU2 SPT server remains running. Evidence/backups are local under
`artifacts/mode-copy-map-access-20261009/`: deployment and preservation JSON,
original/final config, profile comparison, companion hashes, UiBridge log and
the full `LogOutput-SPT-GPU-2-2026-10-09_12-40-51.log` copied as `gpu2-full.log`.
These ignored files contain test-profile information and are not committed.

Not certified here: every newly enabled pairing, PFT on the new maps, full-match
or multi-round endurance, whole-Customs encounter balance, and all map-specific
navigation. The external gpu-run map table still has its older restrictions;
native UI clicks were used instead of altering that separate tool's policy.
