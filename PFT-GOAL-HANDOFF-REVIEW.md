# Protected Tactical pursuit goal handoff

The 2026-09-06 Factory log contains native-awareness and SAIN-binding refusals
after local death, without the earlier native enemy-array/SAIN Stop error storm.
The old messages cannot distinguish failed registration from a refused goal.
This change addresses a verified integration mismatch, not every possible reason
for a bot to stand still or spin.

## Native and PFT contracts

Reviewed against the installed `pitFireTeam` 0.10.0.0 and matching reference
`SPT-PitFireTeam-beta-0.10.0` at
`109760c7a5aad3fdb181941e10ce035c3840bb4d`.
`FollowerGoalEnemyClearRetentionPatch` rejects certain unscoped memory-only goal
assignments. Arena intentionally supplies post-death opponent knowledge but used
an unscoped setter. The optional reflected `FollowerGoalEnemyTracker.Begin`
context now describes that one synchronous assignment as
`ArenaMode.TeamArenaPursuit / arenaPostDeathPursuit`.
PFT's mission commitment veto remains active; Arena does not clear commitments,
pretend to have seen/hit an enemy, or use an authoritative goal override.

The native `EFT.BotMemory.GoalEnemy` setter unsubscribes the previous goal through
the world's persistent player registry and subscribes the next through its alive
registry. A dead previous target is permitted when its exact body remains in the
persistent registry. Removed or replaced bodies are rejected before the setter.
No raw field clearing or retry follows a partially failed native call.

## Ownership and reentrancy

All three callers (initial native refresh, SAIN native rebind, BigBrain consumer)
use one helper. The exact active raid, live Tactical round/epoch, dead bound local
player, frozen four/five roster and opposite-team owner references must match.
Both bots and the native group/EnemyInfo pair must belong to current registered
bodies. The consumer additionally validates its lease generation before and after
assignment. Source, target and relationship are checked again after synchronous
goal-change callbacks. Other combat deaths do not invalidate the frozen roster.

An existing PFT context is never overwritten. The scope is disposed in `finally`,
including refusals and exceptions. Opponents use the checked native assignment
without a follower scope. Unknown PFT contracts fail closed for new follower goals
without disabling the rest of PFT integration or falling back to an unscoped write.

PFT itself uses this thread-local scope pattern. Native group reporting can cause
a nested goal assignment to another bot; the reviewed native path requires a new
enemy within 25 metres, actual visibility and `CanShoot`, and preserves mission
vetos. Arbitrary callbacks added by other mods remain outside this offline proof.

## Verification and limits

`tools/TeamArenaGoalHandoffChecks` links the actual helper, reflection adapter,
Tactical context guard and BigBrain runtime against independent behavior seams.
It covers accepted scoped goals, retained vetoes, disposal, ordinary raids,
replacement bodies, registry failures, stale epochs/leases and reentrant changes.
The regression runner deliberately removes safety properties in temporary copies;
each must fail its named behavior assertion rather than merely fail compilation.
The installed-contract checker inspects assembly metadata/IL without executing
PFT. Existing pursuit, TDM, team, spawn, PFT lifecycle and SAIN checks run before
the final client Release deployment. External mod files are hash-checked unchanged.

Independent reviewers identified the post-set source/lease/relation revalidation
requirements; all three are included. No profile, inventory or server path changes.
No third-party implementation or binaries are copied or bundled.

This host has no GPU. Restart the game before testing Factory Tactical after
death, another round and player respawn, plus an ordinary PFT raid with explicit
orders. Offline checks and matching deployed binaries do not establish live
navigation success. A server restart is not required.
