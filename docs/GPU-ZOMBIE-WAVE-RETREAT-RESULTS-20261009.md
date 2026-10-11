# Zombie wave ownership and retreat cost — GPU2, 2026-10-09

## Delivered behavior

`5637ce7` fixes solo and protected-team wave clearance while an infected request
is pending. The old 20-second pending expiry is removed; known pre-dispatch
failures return exactly one owed wave slot, while uncertain native activation
holds capacity and eventually ends protected as no contest. See
`ZOMBIE-WAVE-PENDING-REVIEW.md` for the 300-second preparation / 60-second native
limits and callback validation. No AI subsystem or population limit was changed.

Native retreat planning was measured, then deliberately left untouched. Its
observed cost does not justify a new production compatibility hook.

## Artifacts and environment

Host `SPT-GPU-2`, `192.168.178.109`, installation `C:\SPT41`; test profile
`SPT-GPU-2`, ID `6ab7db1c64f04b565cf78be9`. Factory day, solo Zombies.

| Artifact | ProductVersion / source | SHA256 |
|---|---|---|
| Final client | `0.1.0+5637ce7a75d30d966b6debe8ecd34b5bbd6a64ef` | `8B2E3DF3E57C76136F6642DE8445928DD05B7472E5C0719C0203569E5D65CB57` |
| Unchanged server | `0.1.0+9c67ed74ea032c239656599536ee645b53e9fc7a` | `C0871C34B4F1AFAAE990777E39827EDCC1BAF7A6C74401A1A0A47AD73DE49307` |
| Unchanged Redeploy.Core | installed reference | `673D9A106F747E5EE0A70EDE21E9DE9E5AB48E5DDE12E0B2922B2139ECDFE4DD` |
| UiBridge | 0.7.4 | `306606209ADCFF09B94BC1DC01B574C27609F39AB23234E76D915EDE9223D79E` |
| Observation client | `0.1.0+415c5aa6aadbd339b3dda646f4e44b76ef533332` | `425A517EC295F90CE8BBCE5CD36D40942D9D0FA7A655AF959848D2E04CE15240` |
| Temporary retreat probe | source `f860fc6`; built before commit, metadata `0.1.0+65dfb564203fdf01e203f1bd13a7af4979ed915b` | `6313625F8AA3361A9AC4243D50668FC0375426853DADEA5788C367E461D2B0B8` |

Only the final client DLL was deployed locally and to GPU2. Its restarted
process `cbfd6a8ecbdb441aaf128228d94c4cce` reported 21/21 matching ordinary
maschine plugins, loaded/disk/VM equal; Core matched separately. The probe was
GPU2-only and had already been removed before this final run. No server/Core
redeployment, no changed external mod, no push. Existing RespawnApiProbe remains
installed and intentionally throws a subscriber test exception during swaps;
this is disclosed below rather than counted as an Arena failure.

Staged build `artifacts/double-jump-build/238553e98bf945d59465bbac2e0818f4/`:
zero errors, four existing nullable-context warnings. Main-project deployment
was redirected; installed artifacts stayed unchanged until explicit client copy.

## Independent offline checks

- Unit suite: **913 passed, two existing skips**, zero failures. Server deployment
  redirected to an unused artifact path; installed server hash checked afterward.
- Parent reran production mode/policy **49** and whole spawn service **33**
  assertions, plus **12 compiled behavioral fault controls**. Artifacts:
  `zombie-wave-pending-checks/c5ecfab9d1f3441e91598b2cd1113e66`.
- Parent reran extracted real Burst methods: **66 assertions / 13 fault controls**.
- Parent reran solo **85** and PFT **59** assertions. Worker additionally passed
  their **13** and **11** compiled faults respectively.
- Independent source review identified a missing role/body identity validation;
  fixed before hostility/observer and covered negatively. Frozen final sources
  reviewed with no remaining P0–P2 finding. `git diff --check` clean.

Controlled desktop cases cover preparation delayed beyond 20 seconds, out-of-order
and synchronous arrivals, uncertain handoff, timeout, stale generations, duplicate
bodies, watchdog tail and terminal death. They are not native timing acceptance.

## Final-client native wave regression

Log `BepInEx/LogOutput-SPT-GPU-2-2026-10-09_15-21-44.log`, archived as
`artifacts/zombie-wave-retreat-20261009/final-native.log`.

Temporary settings: one infected per wave, zero growth, MaxAlive 4, three-second
intermission, no shooting infected, watchdog disabled. Kit loadout, fixed Spartan,
WholeCatalogue requested; optional unused kits correctly remain unpreloaded.
UiBridge enabled temporary godmode after the initial body swap, and used checked
teleports, head tracking and ordinary M4A1 trigger fire to make kills deterministic.

1. 15:23:01.088: one Spartan preload (37 templates) completed in 1.1 s. No other
   kit batch. Match starts 15:23:16.620; initial kit swap completes 15:23:17.003.
2. First infected dispatch 15:23:18.349. Before shooting, UI at elapsed 00:30
   still reads **WAVE 1 | 1 LEFT**, one live infected, zero kills/deaths. No refill
   or extra wave occurred before actual death.
3. M4A1 trigger at 15:23:53.992 kills the first infected. Exactly one refill at
   **15:23:54.242**; next dispatch 15:23:57.421. UI then **WAVE 2 | 1 LEFT**,
   one live infected.
4. Second native kill leads to exactly one refill at **15:24:31.387**; next
   dispatch 15:24:34.501. UI **WAVE 3 | 1 LEFT**, score 300, two kills, zero deaths,
   full health and 30+1 rounds. UI/player evidence archived as wave2/3-ui.json.
5. Deterministic local death at **15:24:49.985** ends survival. Protected native
   result committed `Killed` at 15:24:50.263; no player respawn or wave 4.
   ExitStatus -> menu succeeded. This also exercises the existing one-life rule.

No Error/Fatal log entries. Existing warnings: optional Fika references, Harmony
declaration guidance, deliberate RespawnApiProbe subscriber exceptions (three
swaps, other subscribers still reached), stale culling-sampler cleanup, disabled
announcer cues, scene negative-scale collider and Animator teardown warnings.
`External raid end during arena respawn — standing down` follows terminal death;
the protected exit completes. No wave timeout/no-contest or new spawn exception.

This is a short deterministic solo kit test, not a long gameplay/balance test.
Native 20+ second request delay, the safety timeouts, own-gear intermission and
PFT teardown/rebind were not forced in this final GPU run; their new boundaries
have source/controlled-harness coverage, not claimed native acceptance.

## Retreat observation, before final-client deployment

Log `BepInEx/LogOutput-SPT-GPU-2-2026-10-09_15-11-15.log`, archived as
`artifacts/zombie-wave-retreat-20261009/retreat-native.log`. Initial population
56, actual 25 Fast/31 Shooting, own gear, godmode, watchdog off. Player stayed
alive and made no kills. A checked teleport placed the player within two metres
of an actual shooter, giving positive close-range coverage; live thresholds were
3 m / 6 m. Temporary probe measured only exact live generic infected in this raid.

| 30-second window ending | Frames | Mean owner calls/frame | ManualUpdate total ms | ms/frame | Checks / successful | Check total ms |
|---|---:|---:|---:|---:|---:|---:|
| 15:14:09 | 2232 | 56 | 7.9476 | 0.003561 | 26 / 24 | 0.4936 |
| 15:14:39 | 2314 | 56 | 8.3267 | 0.003598 | 6 / 6 | 0.0952 |
| 15:15:09 | 2000 | 51.585 | 8.6971 | 0.004349 | 24 / 24 | 0.4304 |

Whole capture: **56 checks, 54 successful, 1.0192 ms total check time**, including
three successful shooter checks; **zero Fight calls and zero probe exceptions**.
Check times overlap with ManualUpdate/Fight or originate from the separate
ShallStartCauseHavePlace caller: do not add them or blindly subtract them.
Classification is outside each method timer; nested hooks and timestamp overhead
still affect measured method time. This is not whole-probe overhead or an FPS
benchmark. It establishes that this particular candidate is very small here.

Bot count eventually fell to 13; log proves corpse cleanup and zero player kills,
but not the source of their damage. Do not attribute that to crossfire, map damage
or this probe without damage evidence. Later lower-population windows are not an
optimization comparison. No Error/Fatal/runtime exceptions in observation; scene
collider and existing exit Animator warnings remain. Native retreat stays enabled.

## Preservation and final state

Both config files restored byte-for-byte after verifying that no setting differed:
ArenaMode SHA `E242E0BCFDB43FF81A195658F0E10BF60695DD91C4127156B76B7D2C6106F811`,
ModProfiler SHA `70A91C616EEE9BDCA056D0269E58196398FFD59B8F5357748537E19F0BB20982`.
Complete profile JSON comparison changed only Health.UpdateTime and
Hideout.sptUpdateLastRunTimestamp. All **12 PFT files unchanged**. No profile was
manually restored/edited. Evidence: `preservation.json`, before/after profile and
config backups in the ignored artifact directory.

Temporary probe DLL absent. EFT closed, server still running (PID 15184). The
unrelated untracked `ArenaMode.Client/Assets/ArenaMode/molotov/` is preserved.
