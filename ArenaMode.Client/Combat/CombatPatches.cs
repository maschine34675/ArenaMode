using ArenaMode.Config;
using ArenaMode.Events;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using EFT.Interactive;
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
        new DamageFeedPatch().Enable();
        new RagdollImpulsePatch().Enable();
    }

    public static void ResetRaidState() => SeenVictims.Clear();

    /// <summary>Killfeed/HUD names go through TMP rich text — neuter markup in nicknames.</summary>
    private static string Sanitize(string name)
        => string.IsNullOrEmpty(name) ? "?" : name.Replace('<', '‹').Replace('>', '›');

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
