# Preview fixture compatibility — 18 October 2026

Test-only follow-up to the native-stop audit's enclosing UI regression failure.
The current production preview already supports protected Tactical's PMC-only
own/issued equipment choice. No runtime source or gameplay behavior changes here.
See [the equipment contract](PFT-TACTICAL-KITS-20261018.md).

The old fixture did not define the entry coordinator's frozen
`TacticalTeamKitEntry.Selected`, so current `ArenaPreviewFlow.cs` could not
compile against it. After restoring that boundary, the old matrix would also
incorrectly demand native own equipment for every Tactical team, regardless of
the selected kit choice. The rejected-team mutation still assumed the old return
statement ended immediately after `eligibility.IsTeam`.

Corrections are confined to `tools/PreviewHostLifecycleChecks`:

- Add/reset the frozen issued-kit boundary value for each fixture.
- Tactical team preview is native for own gear and uses the retained Arena figure
  for issued gear. PFT Zombies' existing own-gear rule is unchanged; rejected
  teams still cannot borrow an own-gear exception.
- Exercise both frozen Tactical decisions against the opposite menu choice,
  subsequent F12/eligibility edits, same-screen re-show and abort/new loading host.
  Source changes retain the existing single preview owner and do not release a
  pending loader early.
- Update only the rejected-team mutation anchor, preserving its original fault.
  Add two compiled faults that ignore issued Tactical equipment or force the own
  Tactical choice to use Arena equipment. Both must fail at runtime, not merely
  fail compilation.

## Verification and limits

Commands from the repository root:

```powershell
pwsh -NoProfile -File tools/PreviewHostLifecycleChecks/Verify.ps1
pwsh -NoProfile -File tools/ArenaUiInteractionChecks/Verify.ps1
git diff --check -- tools/PreviewHostLifecycleChecks docs/PREVIEW-HARNESS-FOLLOWUP-20261018.md
```

The lifecycle baseline now passes **695 assertions**. The new issued-kit mutant
fails specifically at `frozen issued PFT kit selects Arena figure`; the forced-kit
mutant preserves the older `current frozen PFT roster selects own gear` alarm.
The corrected rejected-team mutant fails at the disallowed Tactical own-gear
matrix case. Existing lifetime/native-restoration assertions were not removed.

The **complete enclosing UI verification now exits zero** with
`ARENA UI INTERACTION CHECKS PASSED`: native exit 81 assertions/eight rejected
faults; native menu 738 assertions; preview 52 assertions/five rejected faults,
native hook compilation with zero warnings/errors and the synthetic frame
parser; host lifecycle 695 assertions/**18 rejected faults**. Production source
hashes remained unchanged throughout verification. The preceding full run's
CS0103 was a real test-fixture failure, not a passing or skipped check; this
follow-up repairs it and its stale expected policy without changing production.

Final transcript: `artifacts/preview-harness-followup/full-ui-verification.txt`.
Final enclosing evidence: `artifacts/ui-interaction-checks/3bf110e08be04c338e8e5aca3696ffba`.
Final lifecycle evidence: `artifacts/preview-host-lifecycle/245df937ed5c4815b4ff428caf4d08fc`.

Four-pass review: (1) restore the missing boundary against the documented existing
contract; (2) correct the stale expected matrix while retaining rejected-party,
Zombie, lifetime and native-state rules; (3) challenge both frozen choices and
inspect actual compiled mutation failures; (4) rerun the enclosing UI chain and
review that only test fixtures/documentation changed.

This harness links actual preview flow/lifetime code but doubles Unity/native
screen objects. It does not prove rendered equipment, animation or GPU acceptance.
No mod build, deployment, profile change or GPU action belongs to this leaf.
