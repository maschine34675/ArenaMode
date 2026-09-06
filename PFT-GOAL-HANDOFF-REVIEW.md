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

## Follow-up diagnostics

Native and SAIN failures now name their failing stage. Native registration,
identity validation and actual goal refusal are separate, and BigBrain forwards
the same concrete handoff failure. Goal snapshots show requested/current profile,
exact EnemyInfo identity, key type and real visible/shootable flags. They do not
rerun PFT policy or infer which external prefix vetoed the native setter.

Each new source/target/channel/stage combination is reported immediately. Repeated
failures share one 30-second reporting budget per source and channel, with a count
of suppressed occurrences and targets whose binding has not since validated.
That target count is diagnostic history, not a count of living enemies. One
recovery per pair/channel/round confirms binding only, not physical movement.
All bookkeeping is cleared at round/reset and stores no bot references. Busy
messages show the same medical/reload/grenade/door snapshot used by the movement
policy; no additional danger evaluation or movement intervention is introduced.
Exceptions retain their complete inner stack once per stage/type; repeated reports
stay compact. Independent follow-up review found no changed AI return paths.

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

Final offline verification on 2026-09-06: 1,129 handoff/diagnostic assertions
(including a 1,000-call suppression loop), ten deliberately broken variants
rejected, installed contracts and all regression/deployment runners passed.
Client, BigBrain companion and Redeploy.Core Release/live files matched; 31
external mod/configuration files were unchanged. Existing nullable-context build
warnings are unrelated to this change.

This host has no GPU. Restart the game before testing Factory Tactical after
death, another round and player respawn, plus an ordinary PFT raid with explicit
orders. Offline checks and matching deployed binaries do not establish live
navigation success. A server restart is not required.
