# Compact reward recovery

The player can close uncertain old Arena reward cases without deleting a profile.
The server keeps the evidence. Closing these cases authorizes **no further payout**
for them; it does not claim that a restore or payment succeeded.

## Entry and review

- **SelectRaidSide:** `RESOLVE REWARDS` below the Arena choice when old cases exist.
  This remains available when the mod whitelist prevents Arena selection.
- **Arena map menu:** the same compact action in the Profile Rewards card.
- The normal main menu, inventory and traders receive no recovery control.
- A fresh server review shows the case count, up to three short case identities,
  known RUB/XP and the number of cases with unknown amounts.
- **Close** explicitly waives further payment for the reviewed cases. **Cancel**
  submits nothing. The review says that normal profile progress stays intact.
- `RESTART PENDING` means that the normal SPT server must restart to process the
  queued request. The UI neither restarts it nor modifies profile files.
- In a private Arena session, the control directs the player back to normal SPT;
  it cannot submit approval there.

## Request and screen safety

`ArenaRewardRecoveryFlow` binds both the authenticated backend object and profile
ID. Switching either discards old responses and reviews. Closing or hiding the
screen cancels a pending review and closes its native modal. One explicit review
can submit one POST containing only its server review ID and the waiver flag.

Opening the review requires a fresh GET. A failed POST triggers read-only status
reconciliation, never an automatic retry. An in-flight operation disables the
action. The server must reject outdated review IDs independently of these UI guards.

The native message window owns input blocking. Its temporary Close/Cancel labels
are restored on the exact dialog context's closure, including silent teardown.
Server detail strings are not injected into rich-text UI; case IDs are bounded
ASCII and reward totals use decimal aggregation.

## Verification

Linked production behavior checks: `tools/RewardRecoveryUiChecks/Verify.ps1`.
They cover fresh review, cancellation, exact consent, duplicate clicks, stale IDs,
private/blocked/queued cases, login/profile changes, late replies, uncertain POST
reconciliation, clean-profile visibility and bounded text. Six deliberate source
faults must fail the corresponding assertions.

No build, test, deployment or native acceptance is claimed by this implementation
note. The root verification run records those results separately. Native checks
must confirm the side-choice/card geometry, modal buttons and keyboard input,
whitelist-blocked reachability, and absence from main/trader/inventory screens.
Do not confirm a real profile's old-case waiver merely to test this UI.

Implementation review passes: API/lifecycle, profile/consent boundaries,
asynchronous failures, and adversarial state transitions. These are source review,
not substitutes for the linked harness or native checks.
