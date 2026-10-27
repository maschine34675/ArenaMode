# Killfeed round faction — 2026-10-27

Team DM and Domination now label the human player with the selected round side.
Previously, team colors followed the selection while the text read the original
profile faction. A USEC profile playing BEAR therefore still said USEC.

`RoleClassifier.Label` first uses the existing exact-body, exact-raid affiliation
from `TeamDeathmatchRelations.Faction`, then falls back to the native human side.
The captured text survives respawns and late projectiles from a retired body.
Profile faction, role buckets, scoring and ordinary-raid behavior are unchanged.
Both credited combat events and uncredited/give-up deaths use this classifier.

## Verification

- Unit suite: 3308 passed, three existing environment-dependent skips.
- Role labels: 202 assertions, including both faction directions, attacker/victim
  text, old/replacement bodies, uncredited deaths and native fallback. The new
  negative control removes the round lookup and fails the selected-side check.
- Team runtime: 529 assertions with real affiliation code in both TDM and
  Domination. Domination deliberately retains native enemy perception; its
  test checks team hostility without requiring TDM's explicit enemy search.
- Killfeed text (120 assertions) and give-up (626 assertions) checks passed.
- Release client and BigBrain builds passed with deployment disabled. Client
  reports four existing nullable warnings; companion has none. Independent
  source review found no blocker.

Reproduce offline checks with `tools/Verify-KillfeedFaction.ps1 -Stage Labels`
and `-Stage Regression`. Local evidence: `artifacts/killfeed-faction-20261027/`.

## Installed artifacts

| DLL | SHA-256 |
| --- | --- |
| Client | `9A0FAD36BAD147E658C8C6E3022FF0F58A94F122EA3D8C6474DEE2455C56A09A` |
| BigBrain companion | `9F47B9A6EFCC840C9DED27C3C70A0CB5410B2466EF13361EFA2B23D268672272` |

Both match the local installation and both GPU hosts. GPU1 loaded these builds
in a fresh process. GPU2 was already running another session with its previous
loaded assemblies and was not interrupted. Server, Core and map code are unchanged.

## Native acceptance

GPU1, fresh process `e3d80d9e`, Factory day, Domination, USEC test profile with
BEAR selected. The running process reported all 16 compared plugins and both
libraries identical to the local build and host files.

| Case | Observed killfeed |
| --- | --- |
| Local AK-12 kill | Player → SovietWalrus, **BEAR → USEC** |
| Backspace give-up | Player, **BEAR · GAVE UP** |
| AKS-74 kill after replacement | Player → The_Antman, **BEAR → USEC** |
| Enemy kills replacement body | ixcetotis → Player, **USEC → BEAR** |

Named PNGs and structured death/victim snapshots are retained under the local
evidence directory. Test hooks provided movement, aim, ammunition and protection;
shots and the credited death ran through native damage. The give-up used the
normal Backspace input. A second replacement completed after the enemy kill.
The opposite faction direction, TDM, retired-body projectiles and ordinary
fallback are covered by offline checks, not additional native rounds here.

The first entry attempt hit GPU1's existing ArenaMapPort missing-install notice;
it was dismissed for the built-in Factory test. Team assembly also exceeded the
scenario's initial 35-second wait; continuing observed successful activation.

The four-minute match ended normally. At 21:24:59 the server reported snapshot
restoration and protected-end completion; the result screens returned to MainMenu.
The complete native end backup matches the checkpoint fingerprint, and observed,
persisted and complete records all exist. PMC/scav inventory, quests and skills
and PMC experience match the pre-test profile; its faction is still USEC.
All 238 configuration values match their baseline after removing the temporary
runtime overrides. BepInEx rewrote decimal separators only in generated range
comments on exit. GPU1 game/server were stopped again; GPU2's other session was
left running. No profile was manually restored or edited.
