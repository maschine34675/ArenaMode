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

Implementation is ready for a fresh GPU2 process. The follow-up below will record
the actual installed/loaded build, ON/OFF observations, bounded-area run and
restored profile/config state. No claim of a new performance gain is made.
