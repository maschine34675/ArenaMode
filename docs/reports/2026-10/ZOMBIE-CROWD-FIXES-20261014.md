# Zombie Crowd corrections — 2026-10-14

User authorized all four findings in `GPU-ZOMBIE-REVIEW-20261014.md` and asked
to retain Crowd as an optional harder variant, with pre-Crowd behavior when OFF.

## Changes

1. Removed all Crowd renderer/animator writes and their cached views. Native
   `OfflinePlayerCulling`, body rendering and pooled equipment retain ownership.
   This resolves both the stale visibility and leaked skin-setting findings
   without guessed snapshot restoration over another writer's current state.
2. Bounded recall returns its validated NavMesh sample rather than the candidate.
   Current area/body, readiness, inset, connectivity and reservations are checked;
   both bounded/full-map destinations also pass physical floor/capsule checks.
   Full contact buffers and occupied rings reject placement; no crowded fallback.
3. Eligible skipped avoidance calls clear both native offsets with `DropOffset`.
   OFF, followers, other roles and ordinary raids retain the original method.
4. `CrowdEnabled` defaults false for new configs, preserving existing values.
   `ZOMBIE CROWD` is visible in the Zombie map-screen mutators in the otherwise
   unsupported Fast Bots slot. The two underlying settings remain independent;
   hidden and armed callbacks cannot change them. Still 22 visible controls.

OFF does not undo a previous teleport. It stops additional Crowd recalls,
sight overrides and avoidance skips immediately; there is no visual state left
to undo. Population defaults, wave sizes, protected save paths and other modes
are unchanged. A fresh EFT process is required for deployment, since an older
loaded DLL could already have modified pooled assets.

## Offline validation and review

- Production runtime/area adapter: 88 assertions; five deliberately reintroduced
  defects rejected (raw point, residual offsets, ignored OFF, occupied fallback,
  omitted physical clearance). No renderer/animator APIs exist in these doubles.
- Actual menu/settings/view harness: 2,494 assertions, including fresh OFF,
  saved ON, solo/PFT selection, cross-mode hidden callbacks and armed locks.
- Full unit suite: 964 passed, two skipped, zero failed.
- Staged client build succeeds; four existing CS8632 nullable-context warnings.
  Server tests use a nonexistent deployment target; no server DLL is deployed.
- Independent reviewers checked native culling/pooling, sampled navigation,
  `BotLocalAvoidance.DropOffset`, area/raid ownership and menu callback lifetimes.
  Native physics and scene layout remain separate from the offline doubles.

## Native verification

Client implementation `ff28b75`, informational version
`0.1.0+ff28b757423a8644dbfad376138cf00757bc8d6b`, SHA256
`0878F1BC4466D4B412FDD4D90C7CAB1772E2B5F00AD1C6A11FC20C0A2D9D7D79`,
was deployed locally and loaded in a fresh GPU2 process (`9de16ba2`). Server,
Core, companion and assets were not redeployed. This is functional acceptance,
not a new performance/capacity benchmark.

Solo tests used the existing GPU2 installation at 1920x1080, a temporary cap of
12, a 100-zombie wave, fixed Crusader kit, godmode and infinite ammunition.
ModProfiler was inactive and not instrumenting. Only official UiBridge actions
and native menu inputs were used; no runtime inspector writes or injected calls.

| Check | Observed result |
|---|---|
| Map-screen switch | OFF → ON → OFF with correct color and readable description. Deathmatch shows FAST BOTS and no Crowd row; returning to Zombies retains OFF. |
| Factory OFF before ON | 17 samples spanning 33.684 s, Crowd inactive, recall and sight-skip counters zero. |
| Factory ON | 24 samples spanning 63.194 s; two complete log intervals record 17 + 8 recalls. Real gunfire and kills occurred. |
| Factory OFF after ON | 17 samples spanning 33.739 s, Crowd inactive and both counters zero again, including after a player relocation. |
| Customs Factory Center ON | Exact `customs-factory-center-polygon-v2` ready; 28 samples spanning 74.610 s, two complete intervals record 29 + 5 recalls. |
| Customs OFF | Eight samples spanning 19.441 s, Crowd inactive and both counters zero. |
| Customs containment | All 464 sampled live-player/bot positions inside the exact 40-vertex Center contour. |

Across both runs: **342 confirmed native shots and 12 kills**. Factory used three
native spawn-point teleports of the test player to separate it from the bots;
Customs observed the boundary-side starting position without moving the player.
The mode still used its normal population replacement after kills. Scripts
checked OFF inactivity; a separate evidence analyzer additionally requires
positive ON recall/sight counters, real shots/kills, complete phase coverage and
the sampled Customs containment. Counter resets every 30 s are not treated as
cumulative totals. Customs log reads include the earlier Factory raid, so the
table uses only the Customs interval timestamps.

The full process log through final cleanup contains zero Error/Fatal entries,
zero Crowd fallback warnings and no `NullReferenceException`. Warnings remain:
native scene collider/material warnings, plugin/Harmony startup warnings,
the installed RespawnApiProbe's intentionally throwing subscriber, stale native
camera sampler cleanup and disabled-announcer notices. These were not silenced.

All **223 saved Arena config keys** match the before-test snapshot, including
the user's existing Crowd ON choice and 32/50/20/own-gear preferences. The new
OFF default does not overwrite that saved choice. Both protected exits completed;
the profile differs from its post-start baseline only in the normal hideout
update timestamp. GPU2 is back in MainMenu, active test hooks off, control free;
all 43 plugin and five library identities match local/disk/loaded code. Server
SHA256 remains `F5C9539EE088C354776AD273BCD4545BA7A363E1245648332C1C23B3AD9393F7`.

### Deployment interruption and test limits

Defender initially quarantined this build as `Trojan:Win32/Bearfoos.A!ml` with
security intelligence 1.459.691.0. Updating to 1.461.20.0 / engine 1.1.26090.9
and rebuilding **identical bytes** allowed a targeted MpCmdRun scan to complete
with exit 0, "found no threats". No security exclusion, disabled protection,
quarantine restore or binary change was used. Subsequent deployment and native
tests retained that exact hash without another detection. A separate source/build
review found no newly added network/process/download execution paths; that is
not a general certification of the toolchain or dependencies.

The runner's old static map catalog refused Zombies on Customs before any raid
action. The already-supported native menu admitted the bounded map normally;
the test used its NEXT/READY controls without modifying the runner or game guards.

Offline fault injection proves the exact-sample, occupied/blocked destination and
residual-offset regressions. The native runs do **not** force every collision,
NavMesh, reservation or offset failure, and snapshots cannot prove containment
between every sample. No PFT/spectator lifecycle raid, all-map guarantee, long-run
pool stress or FPS improvement is claimed. Removing all Crowd visual writes is
the source-level fix for the two visual ownership defects.

Private raw evidence and images remain ignored under
`artifacts/zombie-crowd-fixes-20261014/`; do not publish profile/config backups or
unredacted bridge data. The retained `native-summary.json` and
`final-summary.json` are generated by independent evidence checks in that folder.
An independent reviewer reproduced shot uniqueness, kill deltas, OFF samples,
the polygon test and final restoration from the raw evidence. Gitleaks 8.30.1
found no secrets in the changed publishable source/documentation files; this
does not clear the ignored private artifacts for publication.
