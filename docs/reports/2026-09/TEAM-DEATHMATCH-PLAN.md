# Team deathmatch contract (revision 1)

New stable mode id: `teamdeathmatch`. Continuous deathmatch, instant local
respawns and existing Arena kits, no PFT follower dependency or carried-kit
Tactical lifecycle. Equal team capacity includes the human: default five per
side, configurable 2-12 through client `TeamDeathmatchSize`. Local PMC faction
determines USEC/BEAR membership and is never modified. Reserve its slot while
dead. Only pmcUSEC/pmcBEAR bots, refill deaths with bounded request leases;
quiet server map prevents ambient population. Same-faction bots are friendly,
opposing factions hostile. Suppress same-team attributed damage without
changing unrelated raid damage. Team kills, awards, scoreboard and tracer
colors must agree; generated bot replacements remain separate personal runs,
while team totals persist for the match. No finite score target in this version.

Root owns integration, runtime mode, presentation and final tests/docs/commits.
Native friendship and spawn research preceded disjoint implementation leaves;
separate tests and cross-reviews verify the resulting contracts. Only root
deploys and commits. Executable policy tests and modeled mode/engine scenarios
do not replace the final GPU test.

| Requirement | Owner | Observation |
|---|---|---|
| New picker/F12 mode and bounded team size | UI/server leaf | mode-selection/settings tests |
| No ambient waves; faction hostility on exact server raid clone | UI/server leaf | server mutation tests, clone identity checks |
| Balanced faction refill, local slot reserved, stale requests rejected | root | production-linked spawn policy/lifecycle tests |
| Native/SAIN same-faction friendship and enemy pressure after respawn | root + native reviewer | native hooks, roster/identity tests |
| Same-team damage and score protection | root | combat tests and native armor path review |
| Team scoreboard/tracer presentation | root | roster, totals, classification tests |
| Existing PFT Tactical and ordinary raids unaffected | root + adversarial reviewer | old regressions + fail-open gates |
| Installed SAIN version checked | root | metadata hash and existing contract harness |
| Build, deploy, commit each complete change | root | Release hashes and clean Git |
| Live game behavior | user handoff | GPU unavailable here |
