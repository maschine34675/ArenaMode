# Protected Tactical: retain frozen opponent bodies until roster retirement

## Observed failure, 2026-10-09

GPU-2 Bay5 Tactical on `e55d6a7` admitted four PFT companions and five
opponents. The native log `LogOutput-SPT-GPU-2-2026-10-09_19-12-22.log`
records nine living actors at 19:28:13.370, one corpse trimmed at
19:28:16.866, one released at 19:28:21.870, then no-contest at
19:28:21.914: `a frozen Arena opponent became destroyed or unreadable`.

This was not a missed death callback. `TryValidateBoundTeamEnemies` checks
the five original physical identities on every live tick, including dead
opponents. It rejects an unreadable/destroyed owner before testing readable
dead health. The cleanup's `Release` destroys that body, invalidating the
mode's still-current roster. The same integration can affect any map.

## Narrow change

`CorpseCleanupService.MayRelease` now asks the current Tactical mode whether
it still retains the exact corpse's Player. `TacticalMode.Corpses.cs` requires
active Arena, protected team Tactical, the exact current match/mode, current
game/world singletons, and the exact game/world pair captured by Arena's
raid-start dispatch. A roster of exactly five BotOwners may hold only their
exact Player references. A different body with the same profile ID is not held.

The dynamic check does not set `TrimState.Protected`. Hiding, collider
deactivation and gear stripping remain unchanged. No death is fabricated,
no registry is edited, no alive/unreadable object is treated as dead, and
Tactical's identity validation is unchanged. Unexpected loss or replacement
of a live enemy still fails closed.

`EndTeamRound` clears `_squad`, but the frozen arrays remain through
Aftermath/Rebinding. Therefore the lease deliberately has neither a live-phase
restriction nor a squad-count condition. `AdoptTeamFollowers` clears the old
frozen roster before the next staging phase; `OnMatchEnd` clears it too.
Replacing or clearing that roster releases the hold immediately. Normal
cleanup still waits for its existing sweep, pending aggressor/damage-source
quiet period, ragdoll and optional DynamicMaps conditions before disposal.

At most five additional hidden physical bodies are retained by this new
rule; it cannot accumulate old rounds. Existing PFT companion protections
remain separate. Solo Tactical, Zombies, Deathmatch, Gun Game, independent
TDM and ordinary-raid cleanup retain their previous rules.

## Native retirement evidence

Read from the installed decompilation, without copying foreign mod code:

- `../_decompiled/EFT/Player.cs`, `OnDeadCoroutine` around 30238–30247:
  native death already unregisters the player after a yield.
- `../_decompiled/EFT/BotOwner.cs`, `OnDied`: native bot death calls the
  controller's `BotDied` and disposes bot logic, independently of corpse release.
- `TacticalMode.ObserveTeamEnemyRetirement` waits for active owner/player
  registrations and living/active states to end. It does not require corpse
  destruction; retaining a dead object therefore does not block that barrier.
- `CorpseCleanupService.Release` calls `DestroyInfo`, `Player.Dispose` and
  `GameWorld.DestroyLoot`. The new veto runs before any of those destructive
  steps, after the existing readable-dead-body check.

## Verification and remaining acceptance

`tools/TacticalCorpseChecks` links the real cleanup, damage-source, optional
map-marker and new Tactical retention sources. Only external boundaries and
surrounding Tactical fields are native-shaped doubles. It exercises the actual
public Tick → MayRelease → Release path, with source-binding checks for roster
freeze/clear, strict live validation and the active-registry retirement barrier.
It does not execute the complete Tactical controller or Unity lifecycle.

Coverage: five held corpses and a sixth releasable corpse, unchanged hiding
and collider trimming, same-ID different bodies, roster replacement and clear,
old-world/game/match isolation, ordinary/solo/other-mode controls, and no release
of live or unreadable-health bodies. Existing CorpseCleanupChecks and
DynamicMapCorpseChecks compile the new helper too.

Four review passes: native/source diagnosis; linked integration tests;
independent lifecycle/body-identity review; compiled fault controls and final
production rerun. The independent source review found no unresolved P0–P2.
`Verify.ps1` injects faults into copies only and requires each to compile and
fail its named assertion; it never edits production sources.

Frozen local result: 68 Tactical assertions, 16 compiled faults rejected at
their expected assertions, followed by a passing production rerun. Existing
cleanup and DynamicMaps suites passed 309 and 61 assertions respectively.
Fault artifacts: `artifacts/tactical-corpse-checks/fab674bea1c94e5799742774430b804d/`.
The faults include an unwired release call, profile-only identity, wrong
game/world/match, destroyed owners/bodies, partial/oversized rosters, permanent
protection accidentally cached at trim time, and release without dead-health
proof. Main client build, deployment and native test are parent-owned steps.

Native acceptance remains necessary: restart with the final client build,
kill an opponent early in protected Tactical, wait past several cleanup
sweeps, then complete a round and verify the old opponents retire and the next
round starts. Also exercise player-death rebinding. Offline success is not
evidence that those game runs have passed. Retaining five bodies has a bounded
memory cost; no FPS improvement is claimed.
