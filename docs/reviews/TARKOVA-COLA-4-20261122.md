# Tarkova Cola 4.0.0: Arena persistence review

Reviewed 2026-11-22. Scope: protected Arena progression and item-return paths. Generic body lifetime repairs belong to Redeploy.Core. This document records source/offline checks and the bounded native follow-up; integration evidence is linked separately below.

## Release identity

- [Official 4.0.0 source](https://github.com/Yendaxddd/TarkovaCola/tree/7163afb46ed8732d83dc26f3415e252bb5bf6c91); previous reviewed source `8d4294d4410425bad27f1d019db4f7dc8fa17631` (3.0.1).
- Both runtime identities remain `com.tarkovacola.server`, runtime version `4.0.0`.
- Official client SHA-256: `0642739761e954b7e893d3a84cde350c8305f8b246dc0d43c16679157b12ba0d`.
- Official server SHA-256: `875b01b23cd9ec42a17d5fbdb6b3f9f0c1a0e92959b01782e231a131b25c511f`.
- Local source evidence: `artifacts/whitelist-forge-20261121/part1/cola`; official release evidence: `artifacts/cola-fov-20261122/downloads/TarkovaCola-4.0.0`. The two hash-verified DLL fixtures are also registered for verification workers at `artifacts/optional-mod-releases/cola-4.0.0`. Neither foreign code nor foreign DLLs are bundled.

## Findings and changes

### Versioned rewards

4.0.0 has 15 perks, 120 challenges and 127 achievements. It adds six perk trees and 45 achievements, removes the Synergy implementation and its 18 achievements, and changes several previous challenge goals. The surviving 82 achievement rewards are unchanged.

The adapter recognizes a paired, exact v4 research/achievement fingerprint. Missing old Synergy data, mixed old/new catalogs, unknown goals and unknown achievement identities are rejected. Server validation independently compares every current achievement identity and fixed item reward against the loaded foreign server assembly. Version numbers alone cannot bypass these checks.

The new `tarkova-cola-4.0.0` reward catalog is separate from `tarkova-cola-2.2.1` and `tarkova-cola-3.0.1`. Historical signed receipts retain their original goals, achievement IDs and fixed rewards, even when settled after an upgrade. Current v4 receipts cannot claim removed synergy rewards or arbitrary item templates. Native achievement deduplication and the existing atomic startup payment/journal paths remain unchanged.

Some lowered goals can leave legitimate old profile counters above a v4 goal. The v4 capture view validates against the maximum reviewed historical bound and clamps only its comparison view. It never reduces the saved ordinary counter. This also permits the native v4 completion clamp without mistaking it for loss of protected state. Out-of-range counters still fail. Removed legacy synergy state stays intact in the ordinary file and does not generate new v4 rewards.

### Tombstone Offering

`Tombstone.OnDeath` can enqueue the contents of rig and pockets through `PerkService.RequestOffering`; `FlushRefunds` later posts it to `/tarkovacola/offering`. Server `OfferingStore.Send` delivers a system item mail and records a cooldown. The old refund hook did not cover this new route.

During an Arena lease, request creation and queue draining are blocked. The exact pre-Arena pending-offering string is captured and restored on release, including a legitimate ordinary offering or null. Failed leases keep the barrier. Server `OfferingStore.Send` uses the existing profile-scoped refund prefix and lock, so an armed profile cannot send mail or consume the cooldown. Other profiles and ordinary raids pass through unchanged. Item returns from Arena are excluded; approved achievement prizes remain allowed through the existing reward journal.

### Remaining persistence and lifetime paths

- Perk state save/load, native achievement claim/unlock, refund queues, companion editor `pit_perks.json`, and machine editor writes keep their existing barriers.
- New fixed automatic machine placement also funnels its persistent map write through the already protected `Machines.Save`. Its own non-profile F12 regenerate toggle may still be cleared by upstream; no setting is rewritten by Arena.
- `/tarkovacola/perkload` changes an in-memory bot-generation input, not a player profile or item mail. Existing global configuration routes retain their prior behavior.
- Vulture Aid HTTP reply ownership checks remain tied to the exact request closure, lease, physical body and container. No per-frame reflection or global object scans were added.
- Existing Arena body reinitialization naturally runs upstream `FinalPerks.Reset` per physical Arena life. Generic ordinary-raid body replacement must preserve once-per-raid charges; the separate Core adapter owns that behavior. Arena lease exit explicitly resets the six new perk implementations to release transient visual/item/health references.

## Offline evidence

- Focused `RewardCola*` and `TarkovaCola*` unit tests: **190 passed, 0 failed, 0 skipped** on the final implementation-leaf source after catalog deduplication. They include v4 catalog pairing, historical goal handling, removed-reward rejection, new bottle achievement settlement/deduplication, and execution of the real scoped server refund barrier for protected/other/released profiles.
- Official v4 DLL checks: **213 assertions**, including all research entries and all 127 fixed server achievement rewards decoded independently from IL. **11 production-source mutants rejected** (write barrier, machine reinit, cleanup, stale callbacks/body/container, stale fingerprint, lost ordinary offering, missing client/server offering hook, drifted v4 server goal).
- Official 3.0.1 DLL checks: **172 assertions**, **8 mutants rejected**. Existing 2.2.1 and 3.0.1 reward data retain their unit-test coverage.
- Commands: `pwsh -NoProfile -File tools/ColaReleaseChecks/Verify.ps1`; repeat with `-ReleaseDirectory artifacts/optional-mod-releases/cola-3.0.1` for the prior DLLs. Unit command uses `--filter "FullyQualifiedName~RewardCola|FullyQualifiedName~TarkovaCola"`, `tools/AirPitChecks/NoDeploy.targets` and an unused `SPTUserMods` sentinel.
- The implementation-leaf gate re-ran the final v4 check successfully: **213 assertions / 11 mutants**, with automatic evidence in the local `leaf-1.1` ledger. The expanded affected runner passed 58 steps and initially failed this harness because its ignored download path was not a registered worker input. The harness default now uses the registered optional-mod fixture root; its isolated worker rerun passed **213 assertions / 11 mutants**. No production fix or test expectation was relaxed. The scoped runner also reran the full unit suite: **4,427 passed, 0 failed, 3 explicitly reported unavailable-fixture skips** (4,430 total); runner summary **2 passed, 0 failed**.
- The integration candidate now records Cola 4.0.0, FOV Fix 5.0.1 and Common Lib 0.1.0 in its development policy. The integration owner completed bounded native checks on SPT-GPU-2; see the next section. This implementation/documentation leaf did not deploy, restart the game, change a live profile, commit or publish.

## Bounded native follow-up

The [integration report](../reports/2026-11/COLA-FOV-NATIVE-20261122.md) records the loaded candidate identities and test limits. Standalone Redeploy and Arena both passed death/respawn and retained-live-body replacement, with aiming/firing afterward. A native Quick Revive downing/swap preserved its once-per-raid charge and did not kill the replacement after the old timer elapsed. The ordinary offering control produced its expected mail; the protected client queue and authenticated server route both rejected an Arena offering. The ordinary Cola sidecar remained byte-identical. Session XP reached the protected journal; a new v4 next-start payout was not tested.

The first profile comparison used a disk copy captured after native local-start. It showed three `PointsEarnedDuringSession` differences, while skill Progress and LastAccess were unchanged. This was a measurement-timing issue: the actual Arena RAM snapshot precedes SPT's native `ResetSkillPointsEarnedDuringRaid`, which zeros those session counters before the later disk copy. The current host server DLL was independently inspected with Cecil: `StartLocalRaidAsync` calls that reset, and the reset assigns zero to every session counter.

`artifacts/cola-fov-20261122/native/profile-checkpoint-comparison.json` and its reproducible `compare-profile-checkpoint.ps1` use the production canonical JSON algorithm without excluding or normalizing fields. The exit profile fingerprint is `f06d98df67a55d3b4ae7c648c90ad9adc11ac03db53fee6f2ddaaad451acbecc`, identical to checkpoint, observed and persisted fingerprints. Its raw SHA-256 `55b31fcae766f7d0a7deffcbdec26c8f7cc585d654b4d6368e365ba4f0663e52` equals persistedRevision. Completion state is `restored`. Arena restored the exact actual arm snapshot; no protection exception or production change was needed.

## Review passes

1. Enumerated new catalogs, changed goals, removed synergies and all changed HTTP/file writes from the pinned source diff.
2. Implemented versioned immutable data and both offering boundaries; kept legacy receipts and existing native reward atomicity.
3. Challenged upgraded old counters, removed reward injection, existing ordinary queues, failed leases, other profiles, delayed callbacks and reward replay. Verified actual released binary data independently of the source-generated catalog.
4. Removed duplicate unchanged reward data and unused helper code; rechecked patch ownership, ordinary pass-through and absence paths. Independent review and native observations are recorded by the integration owner separately.
