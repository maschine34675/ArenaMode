# Automatic reward discard

The user replaced the manual recovery workflow on 2026-10-27: an uncertain small
reward should be dropped, not become a player-facing profile repair task.

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

Build/deployment and native acceptance are recorded after the installation check.
