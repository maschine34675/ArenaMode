# Bot movement checks

`pwsh -NoProfile -File tools/BotMovementChecks/Verify.ps1` compiles the actual
`ArenaBotMovement.cs` against explicit native state/IO doubles. It exercises
scope, the four modes' live-phase boundaries, horizontal-only scaling, bounded
per-step additions, native-corner damping, untouched door/air/prone states,
SAIN admission, reentrant leases and captured-controller exception restoration.
The separately linked `SainChecks` project exercises the real optional SAIN
adapter; `AllowsBoost` is only a boundary double in the main movement scenarios.

Four temporary compiled faults must fail their named behavior assertion:
accepting a non-AI body, removing the bonus-step cap, failing cap restoration,
and removing native-corner damping. Production files are never rewritten, and
the source-linked executable is rebuilt from original source at the end.

The installed binary checks inspect the exact motion/collision method and
owned fields, plus SAIN's optional reflected contract. They do not execute
Unity, SAIN pathfinding or a real Harmony detour.

`pwsh -NoProfile -File tools/BotMovementChecks/Verify-Regressions.ps1` runs the
existing local air control, firearm handling, double jump, air hip-fire, TDM
search tempo and PFT pursuit positive harnesses. Neither script builds the
native client/server projects or deploys anything. Both verify installed Arena
client, Arena server and Redeploy Core hashes remained unchanged.

Logs and fault copies stay in separate `artifacts/bot-movement-*` directories.
A live raid is still required to judge foot sliding, actual AI pace, turns,
doors, collision and navigation at low frame rates.
