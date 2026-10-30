# Arena friendly fire

2026-10-09. This supersedes the older unconditional independent-TDM friendly-hit
veto and the earlier PFT-Zombie note that no friendly-fire option was provided.
It does not change AI hostility, factions, target selection, native PFT mind
settings or the staging/respawn-protection rules.

## Contract

The map-screen mutator freezes one decision for the armed raid. Friendly Fire
ON means Arena adds no friendly-damage veto; native EFT/PFT rules still apply.
The defaults are ON for independent Team Deathmatch and protected PFT Tactical,
OFF for protected PFT Zombies. Solo modes have no team-damage option. Changing
F12 values during a raid changes the next selection, not the current decision.
The ordinary/unarmed value is true, and the combat decision independently
requires the exact started Arena game and world.

With the option OFF, confirmed teammates cannot damage each other's health or
armor. Independent TDM protects both authenticated factions. Protected Tactical
protects the player/four followers and the five authenticated opponents from
their own team. Protected Zombies protects only the player/four followers;
infected-to-infected behavior is native. Self-hits, enemy hits, unknown actors
and unattributed/environment damage remain native. Existing staging protection
continues to own the period before a round/wave is live.

## Physical ownership and delayed projectiles

`ArenaFriendlyFire` is the common decision at the four existing damage seams.
It checks the frozen setting, exact started game/world, a live victim and both
the current alive and ever-existed victim registrations. Team colors, faction
appearance and profile strings alone cannot grant immunity. Existing TDM
registration remains in `TeamDeathmatchRelations`; its physical affiliation
ledger is not replaced by the new wrapper.

PFT modes each own an `ArenaFriendlyFireRoster`. Immediately before their Live
transition, after the existing exact leader/follower/opponent freeze and
hostility checks, they admit the verified physical bodies. The additional
admission verifies four active distinct followers, the exact current leader,
five distinct Tactical opponents (no opponents for Zombies), world registration,
owner/body linkage and distinct nonempty authenticated follower accounts.
Admission is atomic. Every attempted new freeze advances the victim generation,
including a failed freeze, so old victim bodies cannot keep protection.

The ledger uses weak body keys and is bound to a single match/world. Historical
admitted source bodies remain attributable after death/cleanup, without needing
a current-alive attacker. A destroyed Unity source is read through the captured
weak entry rather than dereferencing its native properties. A still-existing
body whose profile identity changes is not trusted. Current victims must belong
to the latest admitted generation. No source is admitted from a matching ID
alone, and no previous match's ledger is reused.

PFT generates new runtime profile IDs at each wave/round. The already-validated
follower AccountId is therefore captured as the stable participant key at
admission. A delayed grenade from a previous follower body hitting that same
follower's replacement remains self-damage. The same applies to the local PMC
through its captured stable local profile ID. A delayed admitted teammate's
grenade hitting a different current teammate is protected. The native bridge's
physical body is authoritative: resolving the thrower's profile ID to a native
bridge only identifies a candidate for the ledger check.

One informational `friendly fire blocked` message is emitted at the first damage
hook veto from the exact local attacker per match, with mode and source/victim
profile IDs. Effect-filter candidate predicates use `reportDamage: false` and
do not count as damage-hook evidence or consume this diagnostic; bot hits do not
consume it either. Its weak match key retains no body/world; no tick or per-hit
logging is added. Logging failure cannot reverse the damage decision.

## Installed native paths reviewed

Authoritative source: `D:\SPT41\Development\_decompiled`, matching this SPT 4.1
installation; no foreign assembly or native globals were modified.

- `Player.ApplyShot`: veto precedes armor durability, hit reactions and native
  shot processing. It also covers grenade fragments, including delayed fragments
  emitted after the synchronous explosion call has returned.
- `Player.ApplyDamageInfo`: veto precedes the native wound/aggressor/stamina/hit
  mutations. The caller's `DamageInfo.Player.iPlayer` is used, never an inferred
  LastAggressor.
- `Player.ApplyExplosionDamageToArmor`: independent blast armor path veto.
- `ActiveHealthController.ApplyDamage`: final HP path veto; existing shield
  handling already honors an earlier Harmony veto through `__runOriginal`.
- `ExplosionSharedMethods.Explosion`: synchronous, nestable source context.
  `Shot.method_1` in `EFT.Ballistics/Shot.cs` resolves the native ever-existed
  shooter bridge. Grenade.GetDamageInfo constructs a Shot when it runs, so it can
  legitimately resolve a replacement body that is already current at detonation.
- `ExplosionSharedMethods.AffectPlayersShared`: native HP/armor processing is
  followed by a separate DoContusion call. The argument dictionary is copied
  without protected teammates; native cache values and shared buffers are never
  edited. This native method has one installed caller, from Explosion.
- `FirearmControllerSharedMethods.ApplyLightAndSoundHealthEffects`: native bot
  sound/blind effects happen before player stun/burn-eyes effects. Its target list
  and cache argument are copied and filtered together for an attributed native
  explosion, covering flash grenades as well as damage grenades.

Both effect filters require the exact native Explosion `_playerBuffer` argument,
the captured match/game/world and the same physical team decision. Other callers
and foreign argument copies fail open. This also prevents reentrant flash-ammo or
flare calls from inheriting an outer grenade source. Unattributed, suppressed or
nested unknown explosions shadow any outer source; a finalizer restores the
previous scope, including on exceptions, and preserves the original exception.
The grenade damage factory is never invoked by these patches: native factories
create/release Shots and must not be called a second time. Argument prefixes
respect earlier Harmony suppression via `__runOriginal`.

## Explicit limits

ON does not force bots to shoot allies or bypass PFT's native friendly-fire mind
rules. Staging/respawn protection can independently suppress damage even when
Friendly Fire is ON. Unattributed bleeding/environment effects remain native;
they are not reassigned from a remembered aggressor. Standalone Zvezda flash
ammunition and FlareCartridge visual effects have no grenade context and retain
native behavior; their attributable damage still goes through the damage seams.
Foreign mods that replace the native explosion target-buffer argument retain
native visual-effect handling rather than acquiring inferred source authority.
These limits avoid general flash immunity or profile-only team authorization.

## Verification and review

`tools/FriendlyFireRuntimeChecks` links the production policy, physical roster,
both PFT mode partials and all explosion context/filter patches. It executes the
real explosion Prefix/Finalizer and both effect Prefix methods. The fixture
models world/body registration, destroyed Unity objects, PFT replacement IDs,
phase ownership and native argument identity. It checks admitted/dead/unknown
sources, both Tactical teams, human-only Zombies, self/own replacement, failed
freeze, cross-round/cross-raid, exact body/world/game, current victim ownership,
frozen ON/OFF, logger failure, buffer copy/non-mutation and context cleanup.

The TDM seam in this new fixture is only a delegation stub; the actual existing
TDM physical-affiliation behavior must also pass `TeamDeathmatchRuntimeChecks`.
Combat hook and Tactical Live-transition wiring are source contracts, not a
claim that Unity/Harmony was loaded by this fixture. The existing Zombie Team
fixture additionally links the real roster and executes the actual initial and
rebuilt wave transitions, including historical/own-replacement grenade relations.
The new helper fixture models the frozen bool; it does not execute the full
production ArenaSession MarkArmed/cleanup pipeline. Parent menu/source contracts
and separate OFF/ON native raids own that capture/cleanup verification. Parent
integration owns installed-DLL compilation and GPU acceptance of actual
health/armor/effects.

Four passes completed: full implementation; installed native-path and ownership
review; focused invalid-admission/exception/nested-source negative cases; final
scope/performance/logging polish. Independent adversarial review found no P0-P2
issue and prompted the exact explosion-buffer scope restriction and logger
failure isolation. Compiled mutation verification and native acceptance results
are recorded below by the responsible test owner; source checks alone are not
in-game acceptance.

Offline result: 154 linked-production assertions, all 34 named compiled faults
rejected at their intended assertions, then the unmodified baseline green again.
Evidence: `artifacts/friendly-fire-checks/3ed50b058dca422d8f74c9d0c5c2fd3f`;
the combat leaf ledger reports all three gates met. Zombie Team: 62 assertions
and all 11 existing compiled faults rejected, evidence
`artifacts/zombie-team-checks/e95ac8ce4f824dbcb264a136e29ccf2a`. Zombie Solo: 85;
Zombie Wave Pending: 49. The three existing Zombie projects and the Team mutation
copy manifest now include the real shared roster rather than a no-op test stub.
Parent independently reports the unchanged actual TDM runtime suite at 332 and
installed-API client compilation at zero errors/four existing warnings.

Native result: pending parent-owned exact-build test, not claimed by this combat
leaf. Native load, actual HP/armor and visual grenade-effect acceptance must be
reported separately from this source/fixture evidence.
