# Reward recovery acceptance — 2026-10-27

Implemented in `a9aed4e` (durable recovery and restoration) and `5105a08`
(compact player review). Existing profiles do not need deletion. Positively proven
restoration can finish automatically; uncertain old reward cases require an
explicit, case-specific waiver of further payment. Normal profile progress and
the original evidence remain intact. This is not a universal repair of arbitrary
corrupt profiles or a bypass of genuine offline/Cola recovery.

## Automated verification

| Check | Result |
| --- | --- |
| Full xUnit regression | 3,302 passed, 0 failed, 1 explicit environment skip |
| Closure engine, including both isolated real baselines | 37/37 passed |
| Restoration/checkpoint/lifecycle/runtime checks | 148/148 passed |
| Recovery API authorization and stale-confirmation checks | 9/9 passed |
| Production-linked recovery UI behavior | Passed; all six intentional source faults rejected |
| Related UI baselines | 11 harnesses, 5,982 assertions passed; unknown suite rejected |
| TDM menu | 2,666 assertions passed |
| Existing reward menu state | 42 assertions passed |

The one skipped test is
`BotTierScalePatchTests.InstalledApbsUsesArenaRangeThroughRealNativeCloneAndLevelPipeline`;
its explicit prerequisite is `ARENA_APBS_TEST_ROOT`, supplied by the separate APBS
compatibility runner. All recovery-specific test groups ran without skips.

The two private baseline copies each contain 296 files. Their integrity manifests
remained unchanged after testing. Copies exercised explicit closure, interrupted
replacement/acknowledgement, subsequent legitimate payments, replay rejection,
and retained unrelated profile/history data. The original profiles were never
waived. A full inbox preflight prevents a later corrupt receipt from allowing an
earlier partial payout. Native-save failure tests cover the SPT hash-cache no-op
retry independently of whether rewards are enabled.

Independent source review covered lost closure acknowledgements, native full
readback, stale generations, publication before immutable private-session closure,
archive membership, and Cola recovery lease forwarding. Review findings were
fixed before the final regression run.

## Build and deployment

Release client/companion and server builds succeeded with zero errors. Four
existing client nullable warnings and three existing server warnings remain.
Only the three DLLs below were replaced on the local installation and GPU2;
previous files were backed up and destination hashes verified. Configurations,
assets and Redeploy.Core were not replaced.

| DLL | SHA-256 |
| --- | --- |
| ArenaMode.Client | `E73651CA0C4096B6F3C2C5ED5BA55831ACBA9E2413E0C611FDC61A18E4207625` |
| ArenaMode.BigBrainCompat | `4E35F99D85F6034D356598EC4F5DD32BF6D22711C6ED111D027408C4FCFD9712` |
| ArenaMode.Server | `3F4EDFFB05E49527F9D4B27B884C860D376CF507AD20C3E80483AA0C95B08640` |

GPU2 ran new process instances: normal `4bde2543`, private `0de06c30`, returned
normal `53ea3a9f`. UiBridge verified build = host disk = loaded for every listed
maschine plugin/library (17 normal plugins, 16 private plugins). The private
server DLL also matched the deployed server hash. Native behavior and new durable
records below verify execution of the changed server path.

## Native acceptance on GPU2

- In normal SPT, the main menu, inventory and traders had no recovery control or
  Arena session strip. The bottom menu retained its normal position.
- `RESOLVE REWARDS` remained reachable below the Arena choice while foreign-mod
  admission disabled that choice. The review showed exactly one old payout,
  2,000 RUB and 200 XP, with explicit no-further-payment wording and Close/Cancel.
  It fit the native dialog at 1920x1080. Cancel and Escape submitted nothing.
- The modal blocked controls behind it. After cancellation, the ordinary Exit
  dialog again showed its native `YES (Y)` action. Inventory/trader navigation
  and keyboard dismissal worked without leftover modal input blocking.
- Private-session entry completed in 55.6 seconds. `REWARD HELP` on the side
  choice directed back to normal SPT; it offered no waiver there. Its map-menu
  counterpart fit alongside the reward toggle without overlapping other cards.
- Factory Tactical loaded in 39 seconds. The test then left via the native
  disconnect/result flow and reached the private main menu in 21 seconds.
  Backspace had advanced the Tactical round rather than ending the raid; the
  explicit disconnect was used for the restoration check.
- Generation `efc044649377436ba76b236bc69952ac` wrote checkpoint, run, accepted
  final, actual restore observation, native persistence readback and completion.
  Observation and persisted profile fingerprints matched; completion was
  `restored`. The private client's complete BepInEx log had zero error entries.
- Return to normal SPT completed in 54.3 seconds. All six checkpoint records
  were archived byte-for-byte. Offline session
  `172c1f1c1a714b24925e6f3b630bc0d2` had closed/completion records, listed run
  `b9ac5edc2eec4fc5b5ec72e878187694`, and retired its active pointer.
- After return, the review still showed only the original old payout. No new
  unresolved restoration case appeared. The original warning is expected until
  the player confirms its waiver; testing did not silently resolve it.
- All 228 reported Arena settings and 185 reported inventory entries were
  semantically unchanged. Inventory capture used `nested=false`; that comparison
  covers reported fields, not unreported nested contents. Full-profile protection
  is separately covered by native readback and the restoration tests.
- The test host ended with game/server stopped and no second server or launcher.
  The pre-existing README/review edits and installed Redeploy.Core hash remained
  unchanged. No files were published or pushed.
- A final read-only audit on both ordinary roots confirmed that both original
  conflicts and their historical records were unchanged. All three profiles per
  root had no newly queued recovery request or applied waiver marker.

The actual waiver/queued restart path was exercised against isolated copies and
the production-linked UI/API checks, not by forfeiting an original profile's
claim in the live game. This native run is a lifecycle/UX acceptance test, not
an exhaustive certification of gameplay or every external mod.

## Evidence locations

Private evidence stays ignored under `artifacts/reward-recovery-20261027/`:
`deployment.json`, `native-*-loaded.json`, `native-review.png`,
`native-arena-card.png`, normal-screen/dialog snapshots,
`native-checkpoint-archive.json`, `native-private-evidence/`,
`native-snapshot-comparison.json`, and `original-claims-audit.json`.
Baselines include private profile and signing material and must not be published.

Final test evidence: `artifacts/run-arena-verification/run-arena-verification.trx`,
`artifacts/reward-recovery-verification/{Engine,Restoration,Api}/results.trx`,
the related UI/TDM logs and the bound gate evidence under
`.unlazy/reward-recovery-20261027/`. See the implementation details in
[recovery engine](REWARD-RECOVERY-ENGINE-20261027.md),
[restoration](REWARD-RESTORATION-20261027.md), and
[compact UI](REWARD-RECOVERY-UX-20261027.md).
