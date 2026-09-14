#nullable disable
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ArenaMode.Combat;

/// <summary>Text-only presentation for a proven direct own-hand-grenade death.</summary>
internal static class ArenaSelfGrenadeTaunt
{
    internal const string Label = "NICE THROW, GENIUS!";
    private static AccessTools.FieldRef<Player.ItemHandsController, Player> Owner;
    private static ConditionalWeakTable<ThrowWeap, Origin> _thrown = new();
    private static ConditionalWeakTable<Player, Reported> _reported = new();
    private static DamageScope _damage;
    private static bool _enabled, _warned;

    private sealed class Origin
    {
        internal long BodyId, Epoch;
        internal string Template, Fragment;
    }
    private sealed class Reported { internal bool Value; }

    internal static void Enable()
    {
        if (_enabled) return;
        try
        {
            Owner = AccessTools.FieldRefAccess<Player.ItemHandsController, Player>("_player");
            new ThrowPatch().Enable();
            new DamagePatch().Enable();
            _enabled = true;
        }
        catch (Exception ex) { Warn(ex); }
    }

    internal static void Reset()
    {
        _thrown = new();
        _reported = new();
        _damage = null;
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena self-grenade text unavailable: " + ex.GetBaseException().Message);
    }

    private static bool TryLocal(out Player local)
    {
        local = null;
        if (!_enabled || !ArenaSession.IsActive || ArenaMatchService.Current?.Mode == null
            || ArenaMatchService.Current.Mode.IsComplete
            || !Singleton<GameWorld>.Instantiated || !Singleton<AbstractGame>.Instantiated) return false;
        var world = Singleton<GameWorld>.Instance;
        var game = Singleton<AbstractGame>.Instance as LocalGame;
        local = world?.MainPlayer;
        return game != null && game.Status == GameStatus.Started && local != null
            && !local.IsAI && local.IsYourPlayer && ReferenceEquals(game.LocalPlayer, local)
            && ArenaSession.IsExactActiveRaid(game, world);
    }

    internal static void ObserveThrow(Player.BaseGrenadeHandsController controller)
    {
        if (!_enabled || !ArenaSession.IsActive) return;
        try
        {
            if (controller == null || !TryLocal(out var local) || !ReferenceEquals(Owner(controller), local)
                || !ReferenceEquals(local.HandsController, controller) || local.HealthController?.IsAlive != true
                || controller.Item == null) return;
            var item = controller.Item;
            _thrown.Remove(item);
            _thrown.Add(item, new Origin
            {
                BodyId = ArenaHitHistory.BodyId(local), Epoch = ArenaShotAwardHistory.CurrentEpoch,
                Template = item.TemplateId, Fragment = item.FragmentType
            });
        }
        catch (Exception ex) { Warn(ex); }
    }

    private static bool Matches(Player local, DamageInfo info)
    {
        if (!ReferenceEquals(info.Player?.iPlayer, local) || info.DelayedDamage
            || info.Weapon is not ThrowWeap grenade || !_thrown.TryGetValue(grenade, out var origin)
            || origin.Epoch != ArenaShotAwardHistory.CurrentEpoch
            || origin.BodyId != ArenaHitHistory.BodyId(local)) return false;
        // Native blast damage uses the original grenade template; physical
        // fragments are Bullet shots using FragmentType, not GrenadeFragment.
        string expected = info.DamageType == EDamageType.GrenadeFragment ? origin.Template
            : info.DamageType == EDamageType.Bullet ? origin.Fragment : null;
        return !string.IsNullOrEmpty(expected) && string.Equals(info.SourceId, expected, StringComparison.Ordinal);
    }

    internal static IDisposable BeginDamage(Player victim, DamageInfo info)
    {
        if (!_enabled || !ArenaSession.IsActive) return null;
        // An unrelated nested native damage call must mask the outer one.
        var scope = new DamageScope(_damage);
        _damage = scope;
        try
        {
            if (!TryLocal(out var local) || !ReferenceEquals(victim, local)
                || local.HealthController?.IsAlive != true || !Matches(local, info)) return scope;
            scope.Victim = local;
            scope.Weapon = info.Weapon;
            scope.Type = info.DamageType;
            scope.Source = info.SourceId;
            scope.FireIndex = info.FireIndex;
            scope.Epoch = ArenaShotAwardHistory.CurrentEpoch;
        }
        catch (Exception ex) { scope.Victim = null; Warn(ex); }
        return scope;
    }

    internal static void ObserveDeath(Player victim, IPlayer aggressor, DamageInfo info, EDamageType lethalType)
    {
        if (!_enabled || !ArenaSession.IsActive) return;
        try
        {
            var scope = _damage;
            if (scope == null || scope.Previous != null || !TryLocal(out var local)
                || !ReferenceEquals(victim, local) || !ReferenceEquals(aggressor, local)
                || !ReferenceEquals(scope.Victim, local) || local.HealthController?.IsAlive != false
                || ArenaAdministrativeDeathGate.IsActive(local.ProfileId)
                || scope.Epoch != ArenaShotAwardHistory.CurrentEpoch
                || !ReferenceEquals(scope.Weapon, info.Weapon) || scope.Type != lethalType || info.DamageType != lethalType
                || scope.FireIndex != info.FireIndex || !string.Equals(scope.Source, info.SourceId, StringComparison.Ordinal)
                || !Matches(local, info)) return;
            var reported = _reported.GetValue(local, _ => new Reported());
            if (reported.Value) return;
            reported.Value = true;
            // Never ReportKill/ReportLocalDeath: native respawn/scoring ownership
            // and the existing self-kill early return remain unchanged.
            ArenaMatchService.Current.ReportTextCallout(Label);
        }
        catch (Exception ex) { Warn(ex); }
    }

    private sealed class DamageScope : IDisposable
    {
        internal readonly DamageScope Previous;
        internal Player Victim;
        internal Item Weapon;
        internal EDamageType Type;
        internal string Source;
        internal int FireIndex;
        internal long Epoch;
        internal DamageScope(DamageScope previous) { Previous = previous; }
        public void Dispose()
        {
            if (ReferenceEquals(_damage, this)) _damage = Previous;
            Victim = null;
            Weapon = null;
        }
    }

    private sealed class ThrowPatch : ModulePatch
    {
        // This hand-controller path carries the physical thrower, unlike
        // Grenade.Init's profile lookup. Planted tripwires never call it.
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(Player.BaseGrenadeHandsController), "CreateAndRegisterGrenade",
                new[] { typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(float) });
        [PatchPrefix]
        private static void Prefix(Player.BaseGrenadeHandsController __instance) => ObserveThrow(__instance);
    }

    private sealed class DamagePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(Player), nameof(Player.ApplyDamageInfo),
                new[] { typeof(DamageInfo), typeof(EBodyPart), typeof(EBodyPartColliderType), typeof(float) });
        [PatchPrefix]
        private static void Prefix(Player __instance, DamageInfo damageInfo, out IDisposable __state)
            => __state = BeginDamage(__instance, damageInfo);
        [PatchFinalizer]
        private static Exception Finalizer(Exception __exception, IDisposable __state)
        { __state?.Dispose(); return __exception; }
    }
}
