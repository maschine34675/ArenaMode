#nullable disable
using ArenaMode.Bots;
using ArenaMode.Combat;
using ArenaMode.Effects;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ArenaMode.Pickups;

/// <summary>
/// Bounded Arena-only effects. Native Player.ApplyShot first resolves armor, then
/// calls ApplyDamageInfo -> ActiveHealthController.ApplyDamage exactly once.
/// Native overflow calls ChangeHealth directly, so hooking only ApplyDamage avoids
/// multiplying pellets or overflow twice. Explosion armor is a separate native call
/// and remains unchanged. Shield admits bullet, explosion and grenade-fragment
/// impacts. Outgoing double damage admits non-launcher firearm bullets at impact
/// time only: EFT rebuilds a grenade's owner bridge from profile ID on detonation,
/// which could otherwise
/// lend an old-life grenade the replacement body's new effect.
/// Shield does not suppress Player's wound relapse, hit/stamina reactions or armor
/// durability; health side effects continue with the reduced health damage argument.
/// CombatPatches and ArenaAssistCapture still see native actual HP loss/return values.
/// See tools/ArenaPowerupChecks/README.md for the native contracts and pool units.
/// </summary>
internal static class ArenaPowerups
{
    private sealed class Life
    {
        internal readonly ActiveHealthController Health;
        internal readonly ArenaPowerupState State = new();
        internal ArenaPowerupIndicator Indicator;
        internal Life(ActiveHealthController health) => Health = health;
    }

    private static ConditionalWeakTable<Player, Life> _lives = new();
    // Only active visible bot effects hold bodies here; no historical profile-ID lookup.
    private static readonly List<Player> IndicatorBodies = new();
    private static int _generation;
    private static bool _enabled, _attempted, _warned;

    internal static void Enable()
    {
        if (_attempted) return;
        _attempted = true;
        try
        {
            new ArenaPowerupsDamagePatch().Enable();
            new ArenaPowerupsDeathPatch().Enable();
            _enabled = true;
        }
        catch (Exception ex) { Warn(ex); }
    }

    internal static void Reset()
    {
        foreach (var player in IndicatorBodies)
            if (_lives.TryGetValue(player, out var life)) life.Indicator?.Dispose();
        IndicatorBodies.Clear();
        _lives = new ConditionalWeakTable<Player, Life>();
        _generation = ArenaPickupRuntime.Generation;
    }

    internal static bool GrantDamage(Player player, float duration)
    {
        try
        {
            var life = CurrentLife(player, create: true);
            if (life?.State.GrantDamage(Time.time, duration) != true) return false;
            RefreshIndicator(player, life);
            return true;
        }
        catch (Exception ex) { Warn(ex); return false; }
    }

    internal static bool GrantShield(Player player, float points, float duration)
    {
        try
        {
            var life = CurrentLife(player, create: true);
            if (life?.State.GrantShield(Time.time, points, duration) != true) return false;
            RefreshIndicator(player, life);
            return true;
        }
        catch (Exception ex) { Warn(ex); return false; }
    }

    internal static ArenaPowerupSnapshot Read(Player player)
    {
        try { return CurrentLife(player, create: false)?.State.Read(Time.time) ?? default; }
        catch (Exception ex) { Warn(ex); return default; }
    }

    internal static bool GrantJump(Player player, float duration)
    {
        // AI has no Arena jump input; never consume boots on its behalf.
        if (player == null || player.IsAI) return false;
        try { return CurrentLife(player, create: true)?.State.GrantJump(Time.time, duration) == true; }
        catch (Exception ex) { Warn(ex); return false; }
    }

    internal static bool GrantAmmo(Player player, float duration)
    {
        if (player == null || player.IsAI || !ArenaInfiniteAmmo.Enabled) return false;
        try { return CurrentLife(player, create: true)?.State.GrantAmmo(Time.time, duration) == true; }
        catch (Exception ex) { Warn(ex); return false; }
    }

    private static Life CurrentLife(Player player, bool create)
    {
        if (_generation != ArenaPickupRuntime.Generation) Reset();
        if (!_enabled || !ArenaSession.IsActive || ReferenceEquals(player, null)) return null;
        if (!ArenaPickupRuntime.IsLiveActor(player) || player.ActiveHealthController?.IsAlive != true
            || ArenaAdministrativeDeathGate.IsActive(player.ProfileId))
        {
            Forget(player);
            return null;
        }
        if (_lives.TryGetValue(player, out var life))
        {
            if (ReferenceEquals(life.Health, player.ActiveHealthController)) return life;
            Forget(player);
        }
        return create ? _lives.GetValue(player, body => new Life(body.ActiveHealthController)) : null;
    }

    internal static void Forget(Player player)
    {
        if (ReferenceEquals(player, null)) return;
        if (_lives.TryGetValue(player, out var life)) ReleaseIndicator(player, life);
        _lives.Remove(player);
    }

    internal static void TickIndicators()
    {
        if (_generation != ArenaPickupRuntime.Generation || !_enabled || !ArenaSession.IsActive)
        { Reset(); return; }
        for (int i = IndicatorBodies.Count - 1; i >= 0; i--)
        {
            var player = IndicatorBodies[i];
            try
            {
                var life = CurrentLife(player, create: false);
                if (life != null) RefreshIndicator(player, life);
            }
            catch (Exception ex) { Forget(player); Warn(ex); }
        }
    }

    private static void ReleaseIndicator(Player player, Life life)
    {
        life.Indicator?.Dispose(); life.Indicator = null;
        for (int i = IndicatorBodies.Count - 1; i >= 0; i--)
            if (ReferenceEquals(IndicatorBodies[i], player)) IndicatorBodies.RemoveAt(i);
    }

    private static void RefreshIndicator(Player player, Life life)
    {
        // Presentation is subordinate to the same validated physical effect life.
        // Never bind through a profile ID or the local HUD visibility preference.
        try
        {
            var state = life.State.Read(Time.time);
            if (!player.IsAI || (state.DamageSeconds <= 0f && state.ShieldPoints <= 0f))
            { ReleaseIndicator(player, life); return; }
            if (life.Indicator == null)
            {
                var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
                if (world == null) return;
                life.Indicator = new ArenaPowerupIndicator(world, player);
                IndicatorBodies.Add(player);
            }
            // A temporarily disabled native body must not leave its contour visible.
            // Keep the timer/life while hidden; re-enabling this exact registered
            // body can show only whatever effect time remains, never a fresh grant.
            life.Indicator.Update(player.gameObject.activeInHierarchy ? state : default);
        }
        catch (Exception ex) { ReleaseIndicator(player, life); Warn(ex); }
    }

    // Last prefix + __runOriginal keeps an earlier Harmony damage veto authoritative.
    // Native's two early rejection conditions must also be checked before spending:
    // DamageCoeff<=0 and the AI mutual-friendly-ignore flag live INSIDE ApplyDamage.
    internal static float Adjust(ActiveHealthController health, EBodyPart bodyPart, float damage,
        DamageInfo info, bool runOriginal)
    {
        if (!_enabled || !ArenaSession.IsActive || !runOriginal
            || !ArenaPowerupState.PositiveFinite(damage) || health?.IsAlive != true
            || !ArenaPowerupState.PositiveFinite(health.DamageCoeff)
            || info.OverDamageFrom.HasValue
            || (info.DamageType != EDamageType.Bullet && info.DamageType != EDamageType.Explosion
                && info.DamageType != EDamageType.GrenadeFragment)) return damage;
        try
        {
            var victim = health.Player;
            var attacker = info.Player?.iPlayer as Player;
            if (ReferenceEquals(victim, null) || ReferenceEquals(attacker, null)
                || !ReferenceEquals(victim.ActiveHealthController, health)
                || ReferenceEquals(attacker, victim)
                || string.Equals(attacker.ProfileId, victim.ProfileId, StringComparison.Ordinal)
                || !ArenaPickupRuntime.IsLiveActor(victim)
                || ArenaAdministrativeDeathGate.IsActive(victim.ProfileId)
                || ArenaAdministrativeDeathGate.IsActive(attacker.ProfileId)) return damage;
            var mode = ArenaMatchService.Current?.Mode;
            if (mode is TeamDeathmatchMode teams)
            {
                if (teams.ShouldBlockMatchDamage(victim)
                    || !TeamDeathmatchRelations.AreOpponents(attacker, victim)) return damage;
            }
            else if (mode is RoundsMode rounds)
            {
                if (!Singleton<GameWorld>.Instantiated || rounds.ShouldBlockMatchDamage(victim)) return damage;
                var world = Singleton<GameWorld>.Instance;
                // Dead projectile owners remain registered here until replaced.
                // Never borrow another body's eligibility through profile ID alone.
                if (!ReferenceEquals(world.GetEverExistedPlayerByID(attacker.ProfileId), attacker)
                    || (!rounds.FreeForAll && !ReferenceEquals(world.MainPlayer, attacker)
                        && !ReferenceEquals(world.MainPlayer, victim))) return damage;
            }
            else return damage;
            if (victim.AIData?.IsAI == true
                && victim.AIData.BotOwner?.Settings?.FileSettings?.Mind?.MUTUAL_IGNORE_FRIENDLY_FIRE == true
                && info.Player.IsAI
                && info.Player.AIData?.BotOwner?.Settings?.FileSettings?.Mind?.MUTUAL_IGNORE_FRIENDLY_FIRE == true)
                return damage;

            // ApplyDamage exempts enemy head hits from reductions (<1), but not
            // increases (>1). All three admitted types are native enemy damage.
            var multiplier = bodyPart == EBodyPart.Head && health.DamageMultiplier <= 1f
                ? 1f : health.DamageMultiplier;
            var now = Time.time;
            var outgoing = info.DamageType == EDamageType.Bullet && info.Weapon is Weapon weapon && !weapon.IsGrenadeLauncher
                && ArenaPickupRuntime.IsLiveActor(attacker)
                && CurrentLife(attacker, create: false)?.State.Read(now).DamageSeconds > 0f;
            var victimLife = CurrentLife(victim, create: false);
            float adjusted = ArenaPowerupState.Adjust(damage, multiplier, outgoing, victimLife?.State, now);
            if (victimLife != null) RefreshIndicator(victim, victimLife); // Shield depletion hides it in the same call.
            return adjusted;
        }
        catch (Exception ex) { Warn(ex); return damage; }
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena powerups unavailable: " + ex.GetBaseException().Message);
    }

    private sealed class ArenaPowerupsDamagePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(ActiveHealthController), nameof(ActiveHealthController.ApplyDamage),
                new[] { typeof(EBodyPart), typeof(float), typeof(DamageInfo) });

        [PatchPrefix, HarmonyPriority(Priority.Last)]
        private static void Prefix(ActiveHealthController __instance, EBodyPart bodyPart,
            ref float damage, DamageInfo damageInfo, bool __runOriginal)
            => damage = Adjust(__instance, bodyPart, damage, damageInfo, __runOriginal);
    }

    private sealed class ArenaPowerupsDeathPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(Player), nameof(Player.OnDead), new[] { typeof(EDamageType) });

        [PatchPrefix]
        private static void Prefix(Player __instance)
        {
            if (ArenaSession.IsActive) Forget(__instance);
        }
    }
}
