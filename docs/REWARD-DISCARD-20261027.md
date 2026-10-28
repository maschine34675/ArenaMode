# Automatic reward discard

The user replaced the manual recovery workflow on 2026-10-27: an uncertain small
reward should be dropped, not become a player-facing profile repair task.

For lost disposable private-session data, the later
[startup recovery](OFFLINE-STARTUP-RECOVERY-20261028.md) adds a separate durable
session discard after verified process exit. It never restores an original profile.

- Normal server startup automatically discards old unproven snapshot/run rewards
  and a committed RUB/XP claim missing from the current profile's payment list.
- Valid restoration proofs and healthy rewards retain their normal processing.
- No Resolve Rewards, Reward Help, confirmation dialog, or recovery HTTP routes.
  Optional reward failures use short neutral text and Debug diagnostics.
- The existing native full-profile restore/readback safeguards remain. Discarding
  a reward never substitutes for restoring inventory or completing a real
  offline-session/Tarkova Cola transaction.

The implementation reuses the durable case-specific closure store. It removes
the affected claims from active processing and remembers their identity to stop
late duplicate payments. Original records are retained internally; journal
history is not blindly deleted and no profile is reset. Interrupted decisions
resume at startup. A stale plan is retired against the latest profile, then a
fresh decision is made automatically. Rewards OFF remains OFF.

Malformed or foreign records whose ownership/meaning cannot be established are
not automatically treated as a valid disposable claim. They produce a neutral
optional-rewards-unavailable state, not a claim of profile damage. The two known
old cases are explicitly supported by the automatic path.

## Verification

Automated verification: 43/43 recovery cases passed, including both isolated
original baselines, all six interrupted-write boundaries, lost markers, OFF,
unchanged native progress, and later valid payouts. The full regression passed
3,300 tests with one explicit environment-dependent APBS skip. The manual-route
test now verifies those routes are absent. Independent review found no remaining
blocking issue. The original fixtures were updated to assert the new discard
policy while preserving actual restoration and foreign-transaction checks.

Release builds of the client, server and BigBrain companion succeeded. Only those
three DLLs from commit `4478b6c` were deployed locally and to GPU-2, with matching
SHA-256 hashes. In both normal and private Arena processes, source build, installed
files and loaded assembly identities matched. Assets and configuration were not
deployed.

Native acceptance on GPU-2 completed a normal startup, Arena menu entry, return
to SPT and a second normal startup. Both known original problems were closed
automatically: two closure records contain three exact case entries. Neither
discarded claim was paid. Two separate valid claims were processed normally,
totalling 10,000 RUB and 1,000 XP; the second startup paid nothing again. The old
warning messages were absent. Arena menus expose no recovery controls, and the
normal main menu has no Arena strip or reward UI.

Read-only audits of both synchronized ordinary installations verified the exact
closures, retained original evidence/history, unchanged consent, and unchanged
PMC/Scav inventories, quests, skills and foreign mod data. The XP delta matches
the valid payments; native timestamp/level/trader recalculation is excluded from
that comparison. All 228 Arena configuration values and 185 reported inventory
items also match the prior native acceptance capture. This pass exercised menu
entry/return and startup, not another combat raid.

Private backup, deployment, audit and UI evidence is retained under
`artifacts/reward-discard-20261027/` and must not be published. Game and servers
were stopped again after verification; no second instance remained.
