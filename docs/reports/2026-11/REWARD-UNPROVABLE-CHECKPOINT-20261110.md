# An unprovable restoration checkpoint withheld every later reward (2026-11-10)

## Symptom

The owner's profile had been paid nothing since 2026-10-27. Every normal server start, on
MASCHINE11 and on SPT-GPU-2 alike (both run the synchronised tree from `C:\SPT41`), logged
only at Debug level:

```
[ArenaMode] Reward skipped for profile <id> (reward-checkpoint-profile-mismatch).
```

A receipt earned on 2026-10-28 (Team Deathmatch, Equator) sat unpaid in the profile's
`inbox`.

## Cause

On 2026-10-27 a direct Arena raid (Domination, Air Pit, normal server on MASCHINE11) restored
the profile snapshot in memory; the checkpoint got its `observed.json`. The native save was
then not confirmed (`Protected Arena end failed and remains retryable:
return-profile-save-not-confirmed`) and the server was stopped. The checkpoint was left with
`checkpoint.json`, `observed.json` and, from then on, `mismatch.json`: no `persisted.json`,
no run (the raid bound no reward run), guard `pending`.

At every later normal start `RewardStartupProcessor.ProcessProfileAsync` first calls
`RewardRestorationCheckpoint.RecoverAtStartup`. Its `Recover` tried the durable readback
(`Persisted`) against the profile on disk, which was not the restored snapshot and only moved
further away from it with every session. `RequireProfile` threw
`reward-checkpoint-profile-mismatch`, the whole per-profile pass ended as Quarantined, and
`RewardRecovery.DiscardUncertainAtStartup`, the automatic discard that exists for exactly
such unproven restorations (user decision 2026-10-27,
[contract](../../reviews/REWARD-DISCARD-20261027.md)), never ran. Nothing in the startup
could ever prove this restoration: startup holds no clone, so there is nothing to restore
from.

Separate from this, the profile's reward choice has been OFF since 2026-10-29: an Arena
session that did not end cleanly was discarded automatically at the next normal start,
which sets rewards to OFF by design ("Old Arena session discarded; rewards set to OFF").
That one is the player's switch in the PROFILE REWARDS card.

## Fix

`Persisted` is split: `TryPersisted` returns false on a profile mismatch (after writing the
same bounded `mismatch.json`), `Persisted` throws as before for the runtime callers.
`Recover` now leaves such a generation pending and the startup pass goes on to the next
checkpoint. The discard of the same pass then reviews the pending guard (no durable proof)
as a snapshot case, plus its run case when there is one, and closes it without payment; the
checkpoint, guard and run files stay unchanged as evidence and the second `RecoverAtStartup`
skips the closed generation. Until it is closed, the payout path still refuses it
(`reward-snapshot-restoration-unresolved`). A damaged witness, an identity or authority
conflict and a profile that fails the clean-profile check still quarantine as before; the
runtime readback (`RecoverPersisted`/`FinalizePersisted`) and the restore observation still
throw.

Accepted consequence (review finding, documented in
[REWARD-RESTORATION](../../reviews/REWARD-RESTORATION-20261027.md)): on two installations
sharing one synchronised tree, a server that starts while the restored profile file has not
arrived yet closes the generation before its own installation could prove it. The same was
already true for a missing `observed.json`; both fall under the discard policy.

## Offline verification

- New `RewardRecoveryTests.UnprovableObservedRestorationIsDiscardedInsteadOfQuarantiningTheProfile`
  (no run, run without final, run with final): not quarantined, only the recovery marker added
  to the profile, no payment, files unchanged, a second start changes nothing, the next reward
  pays. `UnprovableRestorationLeavesAProvableOneInTheSamePassToPay` (both directory orders): a
  provable restoration in the same pass is proven and paid, only the unprovable one is closed.
  `RewardRestorationCheckpointTests.MismatchedCurrentProfileRemainsByteIdenticalAndRecordsOnlyBoundedHashDiagnostics`
  now expects the pending generation instead of the throw.
- Against the old code all six new or changed cases fail; against a plausible wrong fix
  (catching the exception in `RewardStartupProcessor`) the order test fails in the case where
  the unprovable checkpoint is read first.
- Replay on a private copy of the real reward state (kept under the gitignored
  `artifacts/reward-checkpoint-fix/`, not published), with only the root-bound records
  relocated: pass 1 with the stored choice (OFF) `Deferred (rewards-not-enabled)`, the
  checkpoint closed without payment, the profile changed only by the recovery marker; pass 2
  unchanged; with rewards switched ON the 2026-10-28 receipt paid once (10,000 RUB, 1,000 XP);
  the next pass `AlreadyPaid`.
- Adversarial review (four lenses, two skeptics per finding): five findings, four refuted, the
  synchronised-tree race kept as the documented consequence above.

## Native run

Pending.
