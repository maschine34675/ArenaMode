using ArenaMode.Config;
using ArenaMode.Events;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Combat;

/// <summary>
/// Kill capture, damage feed and the ragdoll punch. All hooks verified against
/// the 4.1 decompile and the proven CombatLog/KillAndDamageInfo patches:
/// OnBeenKilledByAggressor fires on the VICTIM instance (base declaration
/// catches every offline death — LocalPlayer's override chains to base),
/// ApplyDamage runs on the victim's health controller with the attacker bridge
/// in DamageInfo.
/// </summary>
internal static class CombatPatches
{
    /// <summary>Victims already scored this raid (the game can re-enter the handler).</summary>
    private static readonly HashSet<string> SeenVictims = new HashSet<string>();

    public static void Enable()
    {
        new KillCapturePatch().Enable();
        new TeamStagingShotPatch().Enable();
        new TeamStagingPlayerDamagePatch().Enable();
        new TeamStagingExplosionArmorPatch().Enable();
        new TeamStagingDamagePatch().Enable();
        new TeamStagingContusionPatch().Enable();
        new DamageFeedPatch().Enable();
        new RagdollImpulsePatch().Enable();
    }

    public static void ResetRaidState()
    {
        SeenVictims.Clear();
        ArenaAdministrativeDeathGate.Reset();
    }

    /// <summary>Killfeed/HUD names go through TMP rich text — neuter markup in nicknames.</summary>
    private static string Sanitize(string name)
        => string.IsNullOrEmpty(name) ? "?" : name.Replace('<', '‹').Replace('>', '›');

    /// <summary>
    /// One fail-closed decision for every mutation surface used by shots,
    /// environmental damage and explosions. The victim is the physical Player
    /// body, including PitFireTeam's derived follower bodies.
    /// </summary>
    private static bool ShouldBlockProtectedTeamMutation(Player victim, string surface)
    {
        try
        {
            if (ArenaMatchService.TeamStartAbortPending)
                return true;

            if (!ArenaSession.IsActive)
                return false;

            var tactical = ArenaMatchService.Current?.Mode as TacticalMode;
            return tactical != null && tactical.ShouldBlockStagingDamage(victim);
        }
        catch (Exception ex)
        {
            var tactical = ArenaMatchService.Current?.Mode as TacticalMode;
            if (tactical?.FailClosedStagingGuard(
                    $"the {surface} guard could not verify a safe actor state") == true)
            {
                Plugin.ModLogger.LogError(
                    $"Arena protected-team {surface} guard failed closed: {ex}");
                return true;
            }

            Plugin.ModLogger.LogWarning(
                $"Arena {surface} guard failed outside protected team staging/aftermath: {ex.Message}");
            return false;
        }
    }

    private sealed class KillCapturePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player), nameof(Player.OnBeenKilledByAggressor));

        [PatchPostfix]
        private static void Postfix(Player __instance, IPlayer aggressor, DamageInfo damageInfo, EBodyPart bodyPart)
        {
            if (!ArenaSession.IsActive)
                return;

            var match = ArenaMatchService.Current;
            if (match == null || __instance == null || aggressor == null)
                return;

            try
            {
                var victimId = __instance.ProfileId;
                if (string.IsNullOrEmpty(victimId) || aggressor.ProfileId == victimId)
                    return;

                // Retiring a damaged body with Kill(Undefined) can make EFT
                // replay its retained LastAggressor. This check must precede
                // SeenVictims so a technical removal neither scores nor consumes
                // the victim's one real combat-death slot.
                if (ArenaAdministrativeDeathGate.IsActive(victimId))
                    return;

                var localId = Singleton<GameWorld>.Instantiated
                    ? Singleton<GameWorld>.Instance.MainPlayer?.ProfileId
                    : null;

                // Dedupe guards against handler re-entry per victim — but the LOCAL
                // player keeps one ProfileId across all respawns (the engine builds
                // every replacement from the same profile), so local deaths are
                // exempt: each Player instance dies exactly once anyway.
                var localIsVictim = localId != null && victimId == localId;
                if (!localIsVictim && !SeenVictims.Add(victimId))
                    return;

                string weaponName = null;
                try
                {
                    weaponName = damageInfo.Weapon?.ShortName?.Localized();
                }
                catch (Exception)
                {
                    // Conjured/modded items without localization stay nameless.
                }

                // A corpse-cleaned aggressor (delayed grenade/burn kills) is a
                // destroyed MonoBehaviour — Position would throw; vanilla guards the
                // same read behind GetAlivePlayerByProfileID.
                var distance = 0f;
                try
                {
                    distance = Vector3.Distance(aggressor.Position, __instance.Position);
                }
                catch (Exception)
                {
                }

                var kill = new ArenaKill
                {
                    KillerProfileId = aggressor.ProfileId,
                    KillerName = Sanitize(RoleClassifier.DisplayName(aggressor)),
                    KillerRoleLabel = RoleClassifier.Label(aggressor),
                    VictimProfileId = victimId,
                    VictimName = Sanitize(RoleClassifier.DisplayName(__instance)),
                    VictimRole = RoleClassifier.Bucket(__instance),
                    VictimRoleLabel = RoleClassifier.Label(__instance),
                    KilledByLocal = localId != null && aggressor.ProfileId == localId,
                    LocalIsVictim = localIsVictim,
                    Headshot = bodyPart == EBodyPart.Head,
                    Distance = distance,
                    WeaponName = weaponName,
                    Time = Time.time,
                };

                match.ReportKill(kill);
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning($"Arena kill capture failed: {ex}");
            }
        }
    }

    /// <summary>
    /// Stop bullets before Player.ProceedDamageThroughArmor changes durability.
    /// The later ActiveHealthController guard remains necessary for explosions,
    /// fire, falls and every other non-shot health path.
    /// </summary>
    private sealed class TeamStagingShotPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(
                typeof(Player),
                nameof(Player.ApplyShot),
                new[]
                {
                    typeof(DamageInfo),
                    typeof(EBodyPart),
                    typeof(EBodyPartColliderType),
                    typeof(EArmorPlateCollider),
                    typeof(ShotId)
                });

        [PatchPrefix]
        private static bool Prefix(Player __instance, ref PlayerHitInfo __result)
        {
            if (!ShouldBlockProtectedTeamMutation(__instance, "shot"))
                return true;

            __result = null;
            return false;
        }
    }

    /// <summary>
    /// Environmental and explosion damage reaches Player.ApplyDamageInfo before
    /// ActiveHealthController.ApplyDamage. Blocking only the latter still changes
    /// wound/aggressor/debuff/stamina/BeingHit state, so stop at Player first.
    /// </summary>
    private sealed class TeamStagingPlayerDamagePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(
                typeof(Player),
                nameof(Player.ApplyDamageInfo),
                new[]
                {
                    typeof(DamageInfo),
                    typeof(EBodyPart),
                    typeof(EBodyPartColliderType),
                    typeof(float)
                });

        [PatchPrefix]
        private static bool Prefix(Player __instance)
            => !ShouldBlockProtectedTeamMutation(__instance, "player-damage");
    }

    /// <summary>
    /// Grenade armor durability is applied after environmental body damage via a
    /// separate Player method. It must share the same staging/aftermath freeze.
    /// </summary>
    private sealed class TeamStagingExplosionArmorPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(
                typeof(Player),
                nameof(Player.ApplyExplosionDamageToArmor),
                new[]
                {
                    typeof(Dictionary<ExplosionDamageInfo, float>),
                    typeof(DamageInfo)
                });

        [PatchPrefix]
        private static bool Prefix(Player __instance)
            => !ShouldBlockProtectedTeamMutation(__instance, "explosion-armor");
    }

    /// <summary>
    /// A PitFireTeam 5v5 is not live until all ten actors exist and every
    /// opponent/follower pair is mutually hostile. Async bot creation otherwise
    /// lets the first arrivals kill an incomplete side while the remaining
    /// profiles are still loading. Only known actors in that short staging phase
    /// are protected; every other raid and every unrelated body fails open.
    /// </summary>
    private sealed class TeamStagingDamagePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.ApplyDamage),
                new[] { typeof(EBodyPart), typeof(float), typeof(DamageInfo) });

        [PatchPrefix]
        private static bool Prefix(ActiveHealthController __instance, ref float __result)
        {
            if (!ShouldBlockProtectedTeamMutation(__instance?.Player, "health-damage"))
                return true;

            __result = 0f;
            return false;
        }
    }

    /// <summary>
    /// ExplosionSharedMethods applies contusion independently after body and
    /// armor damage. Blocking it keeps the protected aftermath mutation-free.
    /// </summary>
    private sealed class TeamStagingContusionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(
                typeof(ActiveHealthController),
                nameof(ActiveHealthController.DoContusion),
                new[] { typeof(float), typeof(float) });

        [PatchPrefix]
        private static bool Prefix(ActiveHealthController __instance)
            => !ShouldBlockProtectedTeamMutation(__instance?.Player, "contusion");
    }

    private sealed class DamageFeedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.ApplyDamage),
                new[] { typeof(EBodyPart), typeof(float), typeof(DamageInfo) });

        [PatchPostfix]
        private static void Postfix(ActiveHealthController __instance, EBodyPart bodyPart, DamageInfo damageInfo, float __result)
        {
            if (!ArenaSession.IsActive
                || (!Settings.ShowHitmarker.Value && !Settings.ShowDamageNumbers.Value))
            {
                return;
            }

            try
            {
                var attacker = damageInfo.Player?.iPlayer;
                var local = Singleton<GameWorld>.Instantiated
                    ? Singleton<GameWorld>.Instance.MainPlayer
                    : null;
                if (attacker == null || local == null || attacker.ProfileId != local.ProfileId)
                    return;

                var victim = __instance.Player;
                if (victim == null || victim.ProfileId == local.ProfileId)
                    return;

                // Post-mortem pellets/burn ticks deal nothing but would still flag
                // Kill=true (the victim is already dead) — spurious kill markers.
                if (__result <= 0.01f && damageInfo.DidArmorDamage <= 0.01f)
                    return;

                var damage = new ArenaDamage
                {
                    BodyDamage = __result,
                    ArmorDamage = damageInfo.DidArmorDamage,
                    Headshot = bodyPart == EBodyPart.Head,
                    Kill = victim.HealthController != null && !victim.HealthController.IsAlive,
                    ArmorOnly = (damageInfo.DidArmorDamage > 0.01f && __result <= 0.01f)
                        || damageInfo.BlockedBy.HasValue
                        || damageInfo.DeflectedBy.HasValue,
                    WorldPos = damageInfo.HitPoint,
                    Time = Time.unscaledTime,
                };

                ArenaEvents.RaiseLocalDamageDealt(damage);
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning($"Arena damage feed failed: {ex}");
            }
        }
    }

    /// <summary>
    /// Punchy deaths (ragdollkinetics technique, Apache-2.0): scale the fatal
    /// impulse. Targets the FAT Collider overload — the Rigidbody one is a
    /// ~17-byte inline candidate whose only caller sits in the same class.
    /// </summary>
    private sealed class RagdollImpulsePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(CorpseRagdoll), nameof(CorpseRagdoll.ApplyImpulse),
                new[] { typeof(Collider), typeof(Vector3), typeof(Vector3), typeof(float) });

        [PatchPrefix]
        private static void Prefix(ref float thrust)
        {
            if (!ArenaSession.IsActive)
                return;

            var scale = Settings.RagdollImpulseScale.Value;
            if (scale > 1f)
                thrust *= scale;
        }
    }
}
