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
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Combat;

/// <summary>
/// Observational shot-time awards. EFT reuses Shot instances, and impact can
/// happen after landing or changing hands. Capture once at InitiateShot, copy
/// verified parent facts at Create, then admit only the synchronous native hit.
/// </summary>
internal static class ArenaShotAwardHooks
{
    private static AccessTools.FieldRef<Player.ItemHandsController, Player> Owner;
    private static bool _enabled, _warned;
    private static FireScope _fire;
    private static ImpactScope _impact;

    internal static void Enable()
    {
        if (_enabled) return;
        try
        {
            Owner = AccessTools.FieldRefAccess<Player.ItemHandsController, Player>("_player");
            new FirePatch().Enable();
            new CreatePatch().Enable();
            new ImpactPatch().Enable();
            _enabled = true;
        }
        catch (Exception ex) { Warn(ex); }
    }

    internal static void Reset()
    {
        _fire = null;
        _impact = null;
        ArenaShotAwardHistory.Reset();
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena shot awards unavailable for this observation: " + ex.GetBaseException().Message);
    }

    private static bool TryLocal(out GameWorld world, out Player local)
    {
        world = null;
        local = null;
        if (!_enabled || !ArenaSession.IsActive || ArenaMatchService.Current?.Mode == null
            || ArenaMatchService.Current.Mode.IsComplete
            || !Singleton<GameWorld>.Instantiated || !Singleton<AbstractGame>.Instantiated) return false;
        world = Singleton<GameWorld>.Instance;
        var game = Singleton<AbstractGame>.Instance as LocalGame;
        local = world?.MainPlayer;
        return game != null && game.Status == GameStatus.Started && local != null
            && !local.IsAI && local.IsYourPlayer && ReferenceEquals(game.LocalPlayer, local)
            && ArenaSession.IsExactActiveRaid(game, world);
    }

    internal static IDisposable BeginFire(Player.FirearmController firearm, IWeapon weapon)
    {
        if (!_enabled || !ArenaSession.IsActive) return null;
        // An unqualified nested firing call masks its enclosing snapshot too.
        var scope = new FireScope(_fire);
        _fire = scope;
        try
        {
            if (!TryLocal(out _, out var local) || !ReferenceEquals(Owner(firearm), local)
                || local.HealthController?.IsAlive != true
                || !ReferenceEquals(local.HandsController, firearm)
                || weapon == null || weapon.IsUnderbarrelWeapon
                || weapon.Item is not Weapon actual || actual.IsGrenadeLauncher
                || !ReferenceEquals(firearm.Item, actual) || firearm.IsAiming) return scope;
            scope.BodyId = ArenaHitHistory.BodyId(local);
            scope.WeaponId = actual.Id;
            scope.Known = true;
            scope.Airborne = local.MovementContext != null && local.CharacterController != null
                && !local.MovementContext.IsGrounded && !local.CharacterController.isGrounded;
            scope.BoltAction = actual.BoltAction;
        }
        catch (Exception ex) { scope.Known = false; Warn(ex); }
        return scope;
    }

    internal static void Created(Shot shot)
    {
        if (!_enabled || !ArenaSession.IsActive || shot == null) return;
        try
        {
            // Always overwrite a pooled instance, even for bots/unknown shots.
            long bodyId = ArenaHitHistory.BodyId(shot.Player?.iPlayer);
            string weaponId = shot.Weapon?.Id;
            bool known = TryLocal(out _, out var local) && ReferenceEquals(shot.Player?.iPlayer, local)
                && _fire != null && _fire.Known && _fire.Epoch == ArenaShotAwardHistory.CurrentEpoch
                && _fire.BodyId == bodyId && string.Equals(_fire.WeaponId, weaponId, StringComparison.Ordinal);
            ArenaShotAwardHistory.RecordCreated(shot, shot.Parent, bodyId, weaponId, shot.FireIndex,
                known, known && _fire.Airborne, known && _fire.BoltAction);
        }
        catch (Exception ex)
        {
            ArenaShotAwardHistory.RecordCreated(shot, null, 0, null, 0, false, false, false);
            Warn(ex);
        }
    }

    internal static IDisposable BeginImpact(ClientGameWorld world, Shot shot)
    {
        if (!_enabled || !ArenaSession.IsActive) return null;
        var scope = new ImpactScope(_impact);
        _impact = scope;
        try
        {
            if (!TryLocal(out var currentWorld, out var local) || !ReferenceEquals(world, currentWorld)
                || shot == null || shot.IsFlyingOutOfTime || !ReferenceEquals(shot.Player?.iPlayer, local)) return scope;
            scope.Facts = ArenaShotAwardHistory.ForImpact(shot, ArenaHitHistory.BodyId(local),
                shot.Weapon?.Id, shot.FireIndex);
            if (!scope.Facts.IsKnown) return scope;
            scope.World = world;
            scope.Shot = shot;
            scope.ShotId = new ShotId(shot.Ammo.Id, shot.FragmentIndex);
        }
        catch (Exception ex) { scope.Facts = default; Warn(ex); }
        return scope;
    }

    internal static ArenaShotAwardFacts CurrentFacts(DamageInfo info, ShotId? shotId = null)
    {
        try
        {
            var scope = _impact;
            if (scope?.Shot == null || !ArenaShotAwardHistory.IsCurrent(scope.Facts)
                || info.DamageType != EDamageType.Bullet || !TryLocal(out var world, out var local)
                || !ReferenceEquals(world, scope.World) || !ReferenceEquals(info.Player?.iPlayer, local)
                || !ReferenceEquals(scope.Shot.Player?.iPlayer, local)
                || info.FireIndex != scope.Shot.FireIndex
                || !string.Equals(info.Weapon?.Id, scope.Shot.Weapon?.Id, StringComparison.Ordinal)
                || !string.Equals(info.SourceId, scope.Shot.Ammo.TemplateId, StringComparison.Ordinal)
                || (shotId.HasValue && !shotId.Value.Equals(scope.ShotId))) return default;
            // Re-read at each accepted contact: reject in-stack reuse of the
            // same pooled native object even while ShotDelegate is on the stack.
            var current = ArenaShotAwardHistory.ForImpact(scope.Shot, ArenaHitHistory.BodyId(local),
                info.Weapon?.Id, info.FireIndex);
            return current.ProjectileToken == scope.Facts.ProjectileToken ? current : default;
        }
        catch (Exception ex) { Warn(ex); return default; }
    }

    private sealed class FireScope : IDisposable
    {
        private readonly FireScope _previous;
        internal readonly long Epoch = ArenaShotAwardHistory.CurrentEpoch;
        internal long BodyId;
        internal string WeaponId;
        internal bool Known, Airborne, BoltAction;
        internal FireScope(FireScope previous) { _previous = previous; }
        public void Dispose() { if (ReferenceEquals(_fire, this)) _fire = _previous; }
    }

    private sealed class ImpactScope : IDisposable
    {
        private readonly ImpactScope _previous;
        internal Shot Shot;
        internal ClientGameWorld World;
        internal ShotId ShotId;
        internal ArenaShotAwardFacts Facts;
        internal ImpactScope(ImpactScope previous) { _previous = previous; }
        public void Dispose()
        {
            if (ReferenceEquals(_impact, this)) _impact = _previous;
            Shot = null;
            World = null;
        }
    }

    private sealed class FirePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(Player.FirearmController), nameof(Player.FirearmController.InitiateShot),
                new[] { typeof(IWeapon), typeof(Ammo), typeof(Vector3), typeof(Vector3), typeof(Vector3), typeof(int), typeof(float) });
        [PatchPrefix]
        private static void Prefix(Player.FirearmController __instance, IWeapon weapon, out IDisposable __state)
            => __state = BeginFire(__instance, weapon);
        [PatchFinalizer]
        private static Exception Finalizer(Exception __exception, IDisposable __state)
        { __state?.Dispose(); return __exception; }
    }

    private sealed class CreatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => typeof(Shot).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Single(method => method.Name == nameof(Shot.Create) && method.ReturnType == typeof(Shot));
        [PatchPostfix]
        private static void Postfix(Shot __result) => Created(__result);
    }

    private sealed class ImpactPatch : ModulePatch
    {
        // Offline ClientLocalGameWorld inherits this override; the base
        // GameWorld.ShotDelegate is NOT called by it.
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(ClientGameWorld), nameof(ClientGameWorld.ShotDelegate), new[] { typeof(Shot) });
        [PatchPrefix]
        private static void Prefix(ClientGameWorld __instance, Shot shotResult, out IDisposable __state)
            => __state = BeginImpact(__instance, shotResult);
        [PatchFinalizer]
        private static Exception Finalizer(Exception __exception, IDisposable __state)
        { __state?.Dispose(); return __exception; }
    }
}
