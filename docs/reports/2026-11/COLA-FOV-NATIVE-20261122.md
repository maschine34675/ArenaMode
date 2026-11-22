# Tarkova Cola 4.0.0 / FOV Fix 5.0.1 native verification

Date: 2026-11-22. SPT 4.1 / EFT 0.16.9.5, reserved SPT-GPU-2.
Development candidate only; no release or push. General body replacement belongs
to Redeploy.Core **1.0.28**, including FOV Fix's new Realism Common Lib **0.1.0**
dependency. Arena supplies its separate profile, reward and refund protection.
The shared implementation is committed in Redeploy as `ecf464d`.

## Candidate and upstream identity

The official [Cola 4.0.0 release](https://github.com/Yendaxddd/TarkovaCola/releases/tag/4.0.0)
and [FOV Fix 5.0.1 release](https://github.com/space-commits/SPT-FOV-Fix/releases/tag/v5.0.1)
were staged on the reserved host with Common Lib 0.1.0. The BepInEx load log names
all three versions. No foreign DLL is shipped in Arena or Redeploy.

The frozen candidate is under `artifacts/cola-fov-20261122/candidate-a/`.
`files.json` records all ten deployment files; `sources.json` hashes 1,945 source
and project files. Production sources remained unchanged through native testing.
The later correction to ColaReleaseChecks changes only its fixture lookup.

| Loaded component | Build | SHA256 |
| --- | --- | --- |
| Redeploy.Core | 1.0.28+abd2537 | `1bebdb16f58b899f89164f20044ed22957bad72ed575f69984007dfbc7ae792c` |
| Redeploy.Client | 1.1.3+abd2537 | `27744dbeb1e47865e780d0bef9e6246aa47ad52da0a88e4a93073a44adf4d8ab` |
| ArenaMode.Client | 0.6.0+9d0c14de | `8c2f234d818677324f717e7f40cb682b4a027970949b08b294c9740e847375ff` |

These stamps identify the base commit plus the then-uncommitted changes, not a
released tag. Loaded-process identity is recorded in `native/standalone-loaded.json`
and `native/arena-loaded.json`; the native observers run against those processes.
Standalone omitted ArenaMode.Client entirely. The second process loaded it and
successfully entered a protected Factory Deathmatch with two participants.

## Controlled observation

Core 1.0.27, with the same upstream mods, supplied the baseline: after a real live
body replacement, all nine old Common Lib controllers remained enabled and old
GearState delegates remained subscribed beside the replacement's delegates.
This establishes a lifetime defect; it does not claim to reproduce a visible freeze.

The temporary `tools/ColaFovNativeProbe` reads identities and transient state. Its
live swap calls the actual Core pipeline and retains the old physical player.
It never invokes a compatibility repair directly or writes foreign state fields.
The test profile is disposable; ordinary profiles were not used for the trials.

| Candidate scenario | Result |
| --- | --- |
| Standalone native death, then normal PMC-near-corpse prompt | Nine old controllers disabled; nine current controllers enabled; no old delegates; FOV owner is the new player. |
| Standalone G28 after respawn | ADS observed, a real shot recorded; short navigation walk arrived. |
| Standalone Quick Revive downed state, then retained-live-body swap | Native bottle use, equipped augmentation and a non-Cola stim allowed lethal chest damage to enter `Downed=true`. Replacement cleared old physical state while preserving `_reviveUsed=true`. |
| Quick Revive deadline | Replacement still alive 35.7 seconds after downing/swap, beyond the ten-second deadline; three M4 shots recorded. A further walk moved the player but stopped at a local obstacle. |
| Arena automatic death/respawn | Nine old controllers disabled, nine current controllers enabled, no old delegates; ADS and a real shot worked. |
| Arena retained-live-body swap after drinking Quick Revive | Same ownership checks passed; a real shot and a short walk to its destination succeeded. |
| Ordinary Cola offering control | Native request queued the fixture; normal raid exit produced the expected one-rouble mail. |
| Protected Arena offering | Native request left the queue empty; the authenticated server route returned `{"json":"no"}`. No offering mail or item persisted. |
| Arena end | Server logged full snapshot restoration and protected post-raid completion; Cola sidecar matched entry exactly; session XP was captured separately for the reward journal. |

Full assertions: `native/native-ownership-assertions.json`; per-transition snapshots
and logs are in the same ignored evidence directory. The full exit-profile fingerprint
is `f06d98df67a55d3b4ae7c648c90ad9adc11ac03db53fee6f2ddaaad451acbecc`,
exactly matching the actual arm checkpoint, observed restoration and persisted proof.
The raw exit file also matches the recorded persisted revision. An intermediate
on-disk entry copy differed in three `PointsEarnedDuringSession` counters because
SPT's native local-start handler had already reset them to zero after the arm
snapshot. The installed server's IL confirms that reset. No counter was excluded
from the accepted comparison. See `native/profile-checkpoint-comparison.json`.

## Offline checks and review

* Redeploy suite: 637 passed, zero skipped. Additional focused FOV/API/profile guards:
  174 passed, zero skipped.
* Shared replacement coordinator: 23 cases, 1,100 assertions; 23 source mutants.
* Common Lib: normal/partial-init/rollback checks, actual published-DLL contract and
  nine compiled mutants. Cola body lifecycle: 68 assertions, 88 published-DLL checks
  and eight compiled mutants.
* Arena suite: 4,427 passed, three explicitly reported unavailable-fixture skips.
  Affected runner: 58 successful steps and one initial Cola fixture-path failure;
  the corrected Cola harness then passed in a worker (213 assertions, eleven mutants).
  Its scoped runner completed with two successful steps, zero failures, including
  a second full unit-suite pass with the same three known skips.
* Cola reward/persistence focus: 190 tests; published v4 harness 213 assertions and
  eleven mutants, retained v3 harness 172 assertions and eight mutants.

Independent review corrected per-attempt Common Lib enable-state capture and
ensured a missing Cola hook does not remove the remaining protection hooks during
an owned raid. Failure rollback is checked offline, not claimed as a native trial.
No new per-frame scene searches, global Harmony enumeration or reflection scans
were added to the shared update loop.

## Scope and installation

This verifies the listed body transitions, ordinary/Arena isolation and v4 admission.
It is not a playthrough of all fifteen perks, all rewards, Scav continuation or every
mod combination. Arena's existing denials remain unchanged. Whitelist versions are
reviewed identities, not maximum-version gates.

The test host's pre-existing Arena installation contains separate, unmerged map
work (`fb2b3af6` client and `026d4c44` server). Therefore the candidate is retained
as a reproducible test artifact; normal installations are not silently replaced
with a main-branch build that would remove those map changes. Cleanup restores
the prior host files, removes the temporary upstream packages/probe/profile and
restores settings.

## Final preservation and cleanup

The audit covered 3,626 baseline files on each of VM, GPU1 and GPU2. VM/GPU1
remained byte-identical before synchronization resumed. GPU2 retained all original
profiles and all existing PFT data byte-for-byte; five changed configs were restored.
Its only remaining baseline differences were three APBS diagnostic generation logs.
The 99 historical backups rotated by SPT were restored additively; 99 new ordinary
profile backups were retained. No original profile was overwritten during cleanup.

Cleanup removed the 325 staged package files, temporary probe/weapon fixture,
probe whitelist override and disposable profile sidecars, including eight separate
reward-checkpoint/run/snapshot files. Every changed/deleted byte was archived locally.
All ten original own binaries/PDBs/policy files again match VM and GPU1. Game and
server are stopped; the host's original synchronization state (`paused=false`) was
restored and read back. Evidence: `native/preservation-audit.json`, both cleanup
receipts, `native/original-own-file-comparison.json` and `native/sync-restored.json`.

The candidate Arena client log has no errors. Standalone exit logged the same
inactive `MenuScreen` coroutine error as the old-Core control run. Native missing
asset-script warnings and optional absent-mod type lookups were also present;
no new Cola/FOV/CommonLib exception or failed protected restoration was observed.
