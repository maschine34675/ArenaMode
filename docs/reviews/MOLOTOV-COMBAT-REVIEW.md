# Dedicated Molotov: combat and lifecycle review

Scope: `ArenaFireRuntime`, `ArenaFirePolicy`, `tools/MolotovChecks`. This review does not certify the new model, server template, sound, scoring changes or a GPU run.

## Behavior

- Only `ArenaMolotovItem.TemplateId` (`a12e00000000000000000001`) creates fire. The normal RGD-5 donor remains native, including local Arena throws.
- `Arena incendiary / Enabled` defaults to true. The old prototype section is neither read nor migrated. Custom Molotov explosions are suppressed even without a registered throw: disabled, missing BigBrain companion, foreign/bot owner, ordinary raid, staging, completed match or unsupported mode. The server's harmless template is a separate defense.
- Registration still requires the exact living local player, current hands controller, thrown item instance and native player bridge in the exact live Arena world/round. No Zombies support was added.
- Fire retains its original physical attacker bridge and weapon. It does not bind an old throw to a respawned player's new body. Existing corpse-retention guards protect delayed attribution callbacks.
- Damage stays native `EDamageType.Flame` through `Player.ApplyDamageInfo`. The old Tactical award-eligibility veto and special immunity for the thrower's next body were removed. Common friendly-fire, staging, immunity and respawn protection decide whether native damage is admitted; kill/assist eligibility remains separate.
- Existing limits remain: 100 total base HP/s, at most one 25-HP scan per victim per 250 ms, no hitch catch-up or overlap stacking, six-second lifetime, four fires, eight tracked throws, bounded floor geometry. Configured native environmental multipliers are normalized without overriding a zero multiplier.
- Rendering/audio constructor or update failures do not delete valid fire. A failed view is detached; view disposal exceptions cannot interrupt fire/projectile cleanup. Gameplay keeps a nullable view.

## Native evidence

Installed decompilation under `D:\SPT41\Development\_decompiled`:

- `EFT/Player.cs:15664`: `CreateAndRegisterGrenade` constructs the grenade from the exact hands item before registration; the postfix binds that returned instance.
- `EFT/Grenade.cs:308`: `InvokeBlowUpEvent` calls `Explosion`, then `OnExplosion`. Suppressing only the explosion preserves native destruction. The instance explosion at line 338 owns native grenade VFX and damage; the harmless server template separately covers other/native static entry points.
- `EFT/Player.cs:30020`: `ApplyDamageInfo` assigns `LastAggressor` from the supplied bridge before calling health damage. `OnPlayerDead` at line 30109 receives that aggressor and damage info synchronously. Looking up an alive player by profile ID is native secondary-effects behavior, not a reason to replace the stored fire owner.
- `EFT.HealthSystem/ActiveHealthController.cs:3505`: a non-bleeding destruction of head/chest can kill. The runtime's scoped owner is present through that callback and restored in `finally`.
- Production `CombatPatches` applies `ArenaFriendlyFire` at the damage boundaries. `TeamDeathmatchRelations.ShouldBlockFriendlyDamage` explicitly excludes the same participant across respawns; Tactical delegates to its exact physical team roster. This leaf removes its obsolete additional award-based veto instead of duplicating those rules.

## Verification and limitations

`tools/MolotovChecks/Verify.ps1` compiles the actual runtime, policy, geometry, BigBrain escape code, shared item ID and common `ArenaFriendlyFire` policy into an isolated executable. It runs baseline, individually compiled negative controls, then baseline again. Build failures do not count as rejected mutants. It writes artifacts only beneath ignored `artifacts/molotov-checks` and does not deploy.

The fixture deliberately doubles Unity physics/NavMesh/lifetimes, native player/health dispatch, protection and team membership. Its lethal callback verifies ownership across a modeled native dispatch boundary; it is **not** proof of Harmony installation, native kills/assists, actual HP effects, team roster binding, audio or rendering. Native GPU acceptance must cover local/enemy/self and friend damage with FF OFF/ON, respawn protection, posthumous ownership, kill/assist score, normal RGD-5 and raid/round exit.

Four implementation passes:

1. Implementation: own-template routing, unconditional custom explosion safeguard, delegated damage admission, nullable resilient view.
2. Domain: inspected installed native grenade/health/player call paths and existing common FF delegation; preserved mode scope and original source references.
3. Adversarial: compiled mutations exercise explosive fallback, donor conversion, missing companion, overlap/catch-up, teardown retry, owner/weapon substitution, stale round, obsolete team/respawn immunity, damage after death and view failures. Bounds and native bot handoff remain linked.
4. Polish: reviewed explicit source/round identity, cleanup and diagnostics; kept ordinary grenades and independent server/model code outside the leaf. Independent reviewer and final checker evidence are recorded in the gate ledger, not asserted by this document.
