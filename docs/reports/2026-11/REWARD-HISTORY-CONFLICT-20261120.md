# Reward history conflict copies on the GPU-2 test profile (2026-11-20)

Profile SPT-GPU-2 (`6ab7db1c64f04b565cf78be9`) paid no Arena reward from 2026-11-13 to 2026-11-20. Every normal server start
logged `Reward skipped for profile 6ab7db1c… (invalid-result-history)` at Debug, on GPU-1, GPU-2 and MASCHINE11 alike.
This report covers the cause, the recovery on 2026-11-20 and the fix on branch `fix/reward-history-foreign-entry`.
Raw notes of the read-only analysis: the session's notes folder (outside the repo).

## Cause

- `RewardApprovalProducer.ReadDecisions` requires every entry of `decisions/` to be `<runId>.json` of the decision it holds.
  The folder held eight Syncthing conflict copies such as
  `0848b2fa7ccb4a16a7d1ab9ce5aa22e3.sync-conflict-20261113-123043-LJEZTQP.json`. Their content was valid, only the name
  failed, and their sequences 35 to 42 doubled the canonical ones. The check fired in
  `RewardRecovery.DiscardUncertainAtStartup` → `RewardRecoveryEvidence.Review` → `ReadDecisions`, before the inbox import, so
  from then on nothing was written: the last allocation, decision and `candidate.json` date from 2026-11-13 12:15:14.
- How the copies came about (VM local time; host logs are UTC, one hour earlier):
  - 10:26:25: the normal server on **GPU-1** settled eight queued receipts of this profile as sequences 35 to 42.
  - 12:15:14: the normal server on **GPU-2** settled the same eight plus two of its own newer ones as sequences 35 to 44, in
    another order. It had not received GPU-1's files: Syncthing did not reconcile the two hosts in that window (paused or
    stalled; no longer determinable).
  - 12:30 to 12:31: Syncthing reconciled, kept GPU-2's newer files and renamed GPU-1's to `*.sync-conflict-…-LJEZTQP.json`
    (LJEZTQP is SPT-GPU-2, the device of the winning change). Evidence: `candidate.json` hashes to the canonical
    `sourceFingerprint` of run `f6251e7c…`; the conflict copy of `candidate.json` hashes to the conflict decision's.
  - First failing starts: 2026-11-13 13:50:52 UTC (GPU-2) and 13:51:28 UTC (GPU-1).
- Root cause: both GPU hosts are **one installation** for the reward code. The `installationId` lives in a mirrored file and
  both run from `C:\SPT41\SPT_Runtime`, so a normal server start on GPU-1 settles GPU-2's test profile too. Two writers
  through an asynchronous sync give a split brain. The profile file itself carries seven conflict copies from 2026-11-13 to
  2026-11-15 for the same reason (not examined further here).

The code was not wrong to refuse: the history is the anti-replay record. What it did wrong is how it refused. The refusal was
a Debug line, the card said "Rewards are temporarily unavailable." although a restart changed nothing, and any foreign
entry (a cloud-sync conflict copy, `desktop.ini`, "copy of …") would have done the same to a player. In addition, the next
sequence was the count of `*.json` files, so a foreign `.json` would have shifted a new allocation, which is never rebased.

## State before the recovery

- 24 conflict copies, all from 2026-11-13 10:26, identical on the VM and GPU-2: `decisions/` 8, `allocations/` 8,
  `imported/` 7, `candidate.json` 1.
- The canonical state was consistent: 44 decisions, allocations and import markers for the same runs, sequences 1 to 44
  without gaps, sequence and fingerprint equal per run across the three folders, `candidate.json` equal to the allocation of
  sequence 44. All 44 decisions paid nothing (`no-eligible-reward`, `run-not-completed`); `journal.json` was empty.
- All 158 runs and snapshots of the profile were terminal. 100 signed receipts waited unimported in `inbox/`
  (2026-11-13 to 2026-11-20).

## Recovery (2026-11-20, approved by the owner, payout included)

1. No server, game or helper on GPU-1 or GPU-2 (`gpu-run server status`), none on the VM, no other session running.
   Syncthing folder `m3opk-xr6am` idle, all three peers at 100 %.
2. Backup outside the synchronized tree: `D:\SPT41-Backups\reward-history-recovery-20261120\before` (the profile's reward
   folder, 317 files, and the profile file, SHA-256 `B29171F5…`; copies compared by hash).
3. The 24 copies were moved to `D:\SPT41-Backups\reward-history-recovery-20261120\conflict-copies` with the script of the
   2026-10-28 cleanup, scoped to this profile (preview, then apply; each copy needs a canonical sibling and keeps its hash;
   every canonical reward file and every profile file kept its bytes; reports `archive-preview.json`,
   `archive-applied.json` there). Syncthing removed them on GPU-1 and GPU-2 within the minute; MASCHINE11 followed later.
   GPU-2's reward folder then equalled the VM's.
4. One normal server start on GPU-2 (release 0.6.0, Pack A Punch no longer installed):
   `[Info] Arena reward: 433000 RUB delivered by mail; 43300 XP credited.` On disk afterwards: inbox, allocations, import
   markers and decisions 144 each, sequences 1 to 144 without gaps, 24 approved decisions (`validated-result`), 98
   `no-eligible-reward`, 22 `run-not-completed`; `journal.json` 24 committed entries, 433,000 RUB and 43,300 XP in total.
   Profile against the backup: top-level changes only in `characters` (experience 500 → 43,800), `dialogues` (24 new
   messages) and Arena's reward block.
5. A second start (`server restart`): `AlreadyPaid (reward-inbox-already-imported)`, the reward folder byte-identical
   before and after. The server was stopped again; it had been off before.

## Fix (branch `fix/reward-history-foreign-entry`)

- `ReadDecisions` names an entry that is not `<32 lowercase hex>.json` (or is a directory) with its own code
  `result-history-foreign-entry` (`RewardHistoryForeignEntryException`, carrying the bare file name) instead of reading it or
  skipping it. A canonical name whose content does not match stays `invalid-result-history`.
- `RewardRuntimeInbox` takes the next sequence from the validated history (`ReadDecisions(...).Count`), never from a file
  count, so no allocation is written while the history is not clean.
- The start logs one Warning per affected profile for the history codes (`result-history-foreign-entry`,
  `invalid-result-history`, `result-history-gap-or-duplicate`). For a foreign file it names the file and the remedy (stop
  SPT, move it out of the SPT folder, run a synchronized server on one PC at a time); for a damaged decision it says that
  Arena does not repair it. The reward card says "Rewards are off for this profile: a file in its reward history needs
  attention. See the server log." Other quarantine codes keep their Debug line and "temporarily".
- Tests in `ArenaMode.Tests/RewardRuntimeTests.ForeignHistory.cs`: the conflict copy (refusal by name, nothing paid, match
  stays queued, card and Warning, paid after moving it out), six foreign shapes (`desktop.ini`, `notes.txt`, `.json.bak`,
  `.JSON`, upper-case id, a directory), the allocation that must not be written, and the Warning text. Each fails when its
  part of the fix is reverted (no foreign check: 8 failures; file count for the sequence: 1; card left at "temporarily": 1).

Offline verification on `da490c56` (code) plus the docs commit, rebased on master `e57c85fe`:
`tools/Run-ArenaVerification.ps1 -ChangedSince master` 51 of 53 steps passed. ChopShopChecks and IcebergChecks failed in
PortedCoverChecks because the fresh worktree has no decompiled PitFireTeam source (`artifacts/pft-installed`, gitignored);
with `ARENA_PFT_INSTALLED_SOURCE` pointing at the main checkout's copy both passed, and xunit ran once more: 4,415 tests,
4,412 passed, 3 environment-gated skips. The live install was unchanged throughout (runner guard).

Not deployed: the hosts keep release 0.6.0 until the next deliberate deploy.
