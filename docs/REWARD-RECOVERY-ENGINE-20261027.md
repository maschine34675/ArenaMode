# Reward recovery engine — 2026-10-27

The player can explicitly close an uncertain old Arena operation **without another payment**. The current native profile remains authoritative. This is neither profile restoration nor a reconstruction of a lost payment. Rewards opt-in is unchanged.

## Authority and scope

The normal-server API obtains a read-only review for the authenticated profile. The request contains only its 64-character review hash and explicit confirmation. The engine accepts pending schema-1 snapshot/run guards and exact committed RUB/XP journal entries absent from the profile ledger. It validates the complete result-decision sequence and payment history before offering closure. Unknown schemas, mismatched history, linked paths, true offline ownership recovery, and unresolved Cola transactions remain blocked.

The review binds installation/profile/epoch, the Arena profile extension, prior recovery outcomes, exact affected journal entries, and hashes of original guard/run/receipt/allocation/import/decision/checkpoint files. Unrelated profile progression and autosave fields do not invalidate the displayed review. A change to affected evidence or Arena metadata does. Scans and responses are bounded; one review contains at most 64 cases and a profile retains at most 64 recovery attempts.

Original journal, receipt, decision, imported, snapshot and run bytes remain unchanged. The new profile field is an optional `recoveries` array inside `com.maschine.ArenaMode.rewards`, containing only review/request fingerprints. It does not add a paid receipt, mail, XP, money, restored flag, consent change, or new enrollment epoch.

## Durable boundary

`recovery/<review-id>/` retains immutable records:

1. `request.json`: the exact confirmed review, written by the normal-server route without a profile edit.
2. `before-profile.json`: the entire current profile at the native pre-load boundary, retained for audit only.
3. `plan.json`: request fingerprint and whole-profile before/after hashes. The after document differs only by the recovery marker.
4. `completion.json`: either `closed-without-payment` after durable profile readback, or `stale` when another review is necessary.

The startup processor first finishes actual Cola recovery, then reconciles positive restoration checkpoints, then executes queued recovery before the rewards-OFF early return. Actual offline ownership recovery precedes this callback. The route never writes a live profile.

A crash before profile replacement retries the same exact plan. A later normal save before replacement makes that plan stale; it cannot replace or rebase onto the newer profile. A crash after marker replacement can finish acknowledgement if the complete owned extension is unchanged, even if unrelated native progression has since changed. That path writes no profile. Every durable boundary has a test injection point.

A completed sidecar whose marker disappeared after a profile backup rollback is not auto-acknowledged. It requires a fresh explicit review, including when that backup happens to contain the originally missing paid ledger entry. A later confirmed decision may cover the identical old cases while preserving every prior record. Changed original source bytes fail closed rather than becoming a generic new reset opportunity.

## Permanent rejection and future rewards

The kernel, startup store, approval producer, inbox and stale candidate/approval fallback accept only validated completed decisions with a matching native marker. Missing payment exceptions require the identical committed journal entry. Late signed receipts for the closed snapshot generation or exact run/arm identity are retained and rejected without allocating a sequence, paying, or writing an imported-as-paid marker. Replays return `ClosedWithoutPayment`, never `Paid`, `AlreadyPaid` or `Restored`.

Checkpoint publication uses a separate `ReadCheckpointClosures` query because it runs before recovery commit. That query keeps requested/closed generations untouched across a marker-before-completion crash or missing-marker review. It provides **no payment authority**. The ordinary kernel/inbox always use the strict `ReadClosures` query.

New unrelated runs retain their normal validation and can pay. New unresolved generations and changed unrelated journal history remain errors. Exact historical exceptions do not waive a genuine Cola transaction or profile ownership recovery.

## Verification inventory

`RewardRecoveryTests` covers read-only preview, stable review after native progress, immutable source retention, explicit OFF preservation, future payment after legacy and missing-ledger cases, all six durable crash boundaries, post-crash normal saves, lost markers with and without returned paid ledger, stale reviews, changed original evidence/payment amounts, unrelated pending generations, late signed receipts, unknown schemas, true recovery barriers, and closed/native-loaded startup windows.

The two optional real-data cases use `SPT_REWARD_RECOVERY_BASELINE` pointing to the private baseline directory with `local/` and `gpu2/`. Each test copies only the selected profile and its Arena namespaces into an owned temporary root. Only copied enrollment/handoff/delegation root paths are relocated; signed receipt payloads, keys, allocation, result and payment evidence remain exact. Tests assert the original copied case bytes and unrelated profile fields survive closure, then verify a fresh reward and replay. Omission is an explicit skipped test, never a silent pass. The task verification runner supplies the baseline and requires zero skips for this family.

Four implementation passes: authority/schema scope; durable-write/crash transitions; kernel/inbox/producer replay exceptions; preservation and future-operation behavior. Builds, test execution, independent review, deployment and native acceptance are owned by the integrating root task and are recorded separately; source implementation alone is not native acceptance.

The root run passed all 37 engine checks, including both real isolated baselines.
See [integration acceptance](REWARD-RECOVERY-ACCEPTANCE-20261027.md) for the full
regression, deployed builds, native lifecycle and unchanged original claims.
