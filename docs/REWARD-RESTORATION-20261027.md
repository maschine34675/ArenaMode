# Durable restoration and reward evidence (2026-10-27)

Implementation for leaf 1.2 of `.unlazy/reward-recovery-20261027/PLAN.md`.
Builds, executable tests, deployment and native acceptance are root-owned and
reported separately in [integration acceptance](REWARD-RECOVERY-ACCEPTANCE-20261027.md).

## Native profile safety

After the actual in-memory restore, `ProfileSnapshotService` retains an independent
detached expected clone until native persistence is verified. Protected native end,
failed-start recovery and stale-start recovery compare the complete expected clone,
disk JSON and current RAM profile. This check applies with rewards ON or OFF.
Native save success alone is insufficient: SPT updates its hash cache before the
file write, so a failed write followed by a cached no-op must remain retryable.
The implementation never clears native hashes and never writes a profile itself.

Native writes use the existing return listener's `SaveWithoutAborting`: cancellation
is checked before entry and after completion, but the started save receives
`CancellationToken.None`. A protected native-end retry shares the running task.
Real persistence failure keeps the protected transaction and detached witness;
new Arena snapshots are refused while this witness remains pending. Optional
reward observer/publication failure cannot block a successfully verified native
end. Failed-start recovery no longer silently leaves its protected state after a
failed save. After positive full readback, the native witness idempotently retries
the restore observer, so a transient reward metadata failure at the actual swap
can recover before its RAM evidence is released.

## Durable reward witnesses

`user/arena-reward-checkpoints/<profileId>/<generation>/` contains metadata only:

| Record | Meaning |
| --- | --- |
| `checkpoint.json` | Exact binding, generation, delegation, capture root and full-profile fingerprint; bounded hashed top-level sections for diagnostics. |
| `run.json` | Exact run, arm and native-end identities. |
| `final.json` | Validated final authorization/report, persisted before acknowledging capture; no restoration assertion. |
| `observed.json` | The actual clone-restore observer matched the entire expected profile. |
| `persisted.json` | A full native-file readback matched that observed restore; binds the exact persisted serialization. |
| `complete.json` | Publication/terminal completion bound to proof and final-result hashes. |
| `mismatch.json` | Phase, expected/actual fingerprints and at most 16 hashed differing top-level paths; no profile values. |

The fingerprint sorts object properties and normalizes string escaping while
preserving every field and array position. Numeric spelling is deliberately
strict: a different representation can defer a result but cannot hide a changed
value. The live path also retains the existing full semantic comparison.
No full-profile checkpoint or rollback payload is stored.

The existing schema-1 `RewardSnapshotGuard` is unchanged. Proof and signed receipt
precede terminal snapshot/run guards. Crashes between these writes replay the
same immutable inputs. A positively restored no-final/no-run attempt can finish
without payment; an already accepted valid final result is never discarded as a
storage-error shortcut. Final Aborted/NoContest reports keep their normal no-pay
semantics. Legacy pending evidence without positive proof remains unresolved.

Before a new snapshot observer does any authority, serialization or storage work,
it detaches the preceding RAM generation. Failure during the new observer cannot
authorize a new raid using an old snapshot or run. Original durable evidence stays.

## Startup and private sessions

`RecoverAtStartup(window, id)` requires the ordinary unloaded-profile window.
Missing RAM is never proof. A missing readback can be recovered only when a durable
restore observation exists and the complete current disk fingerprint matches.
An existing positive proof remains valid after later ordinary progress; it never
replaces the current profile. Requested/confirmed case closures stop checkpoint
writes before closure processing, including incomplete acknowledgement retries.

The offline owner calls `RecoverPrivateStopped(privateRoot, id, authority)` only
after exact lease/marker validation and process exit, before freezing the receipt
list in `closed.json`. Unproven or malformed cases remain pending. A valid proven
final whose publication fails must retain the stopped session for retry; it cannot
be omitted from a frozen receipt list. Normal publication checks the offline
closure before creating inbox files, preventing an unlisted archived receipt from
poisoning the inbox.

`ArchivePrivateEvidence(privateRoot, normalRoot, id)` then copies the bounded raw
checkpoint records and profile-specific snapshot/run guards into their corresponding
ordinary namespaces. Immutable byte comparisons, flushed atomic writes and readback
preserve malformed evidence without inventing success. Canonical `.arena-reward-*.tmp`
staging names remain distinguishable from committed records after interruption.
An archive failure prevents retirement of the private tree. No private profile is
copied. `CaptureRoot` prevents an ordinary profile from substituting for a missing
private readback. Classified pending cases remain available for explicit review;
malformed or unknown records are retained for diagnosis and are not waived blindly.

## Review and executable inventory

Four self-review passes addressed implementation, full-profile/native-boundary
semantics, restart/closure/transport ordering, and bounded diagnostics/atomic staging.
Independent parent findings added native full readback independent of rewards,
publication-before-offline-closure refusal, and canonical archive staging names.

`RewardRestorationCheckpointTests` covers lost RAM, positive/no-pay restart recovery,
unchanged current profiles, complete fingerprint comparison, tampered witnesses,
failed receipt publication, interrupted terminal markers, later ordinary progress,
stale-generation isolation, exact run conflict, private authority, raw archive,
archive conflict, invalid native startup windows and private publication retries.
`RewardLifecycleTests` additionally exercises cancellation before/after native save,
failed write plus cached no-op retries with/without reward observers, independent
positive full readback, and live mutation against the detached expected clone.

Root passed 148 restoration/lifecycle/runtime tests with the no-deploy runner,
then the full regression. Native Factory exit and private-to-normal return also
produced and archived all six checkpoint records. In-game/native acceptance
remains distinct from the temporary-fixture checks; see the linked report.
