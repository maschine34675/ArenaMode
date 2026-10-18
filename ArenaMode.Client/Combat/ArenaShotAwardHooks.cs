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
    private static ContinuationScope _continuation;
    internal const float MinimumAirborneSeconds = 0.5f;
    private static FlightObservation _flight;

    private struct FlightObservation
    {
        internal long Body, Context, World, Game, Epoch;
        internal float StartedAt, LastAt, LastStep;
    }

    internal static void Enable()
    {
        if (_enabled) return;
        try
        {
            Owner = AccessTools.FieldRefAccess<Player.ItemHandsController, Player>("_player");
            new ArenaShotAwardHooksFirePatch().Enable();
            new ArenaShotAwardHooksCreatePatch().Enable();
            new ArenaShotAwardHooksImpactPatch().Enable();
            new ArenaShotAwardHooksFlightPatch().Enable();
            new ArenaShotAwardHooksContinuationPatch().Enable();
            _enabled = true;
        }
        catch (Exception ex) { Warn(ex); }
    }

    internal static void Reset()
    {
        _fire = null;
        _impact = null;
        _continuation = null;
        _flight = default;
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
            if (!TryLocal(out var world, out var local) || !ReferenceEquals(Owner(firearm), local)
                || local.HealthController?.IsAlive != true
                || !ReferenceEquals(local.HandsController, firearm)
                || weapon == null || weapon.IsUnderbarrelWeapon
                || weapon.Item is not Weapon actual || actual.IsGrenadeLauncher
                || !ReferenceEquals(firearm.Item, actual)) return scope;
            scope.BodyId = ArenaHitHistory.BodyId(local);
            scope.WorldId = ArenaHitHistory.BodyId(world);
            scope.GameId = ArenaHitHistory.BodyId(Singleton<AbstractGame>.Instance);
            scope.WeaponId = actual.Id;
            scope.Known = true;
            scope.Airborne = !firearm.IsAiming && HasQualifiedFlight(world, local);
            scope.BoltAction = !firearm.IsAiming && actual.BoltAction;
        }
        catch (Exception ex) { scope.Known = false; Warn(ex); }
        return scope;
    }

    // Native FreefallTime resets at stalled height/apex and is shared with
    // gravity. Observe the refreshed ground state without changing movement.
    internal static void ObserveFlight(MovementContext context, float deltaTime)
    {
        if (!_enabled || !ArenaSession.IsActive) { _flight = default; return; }
        try
        {
            if (!TryLocal(out var world, out var local)) { _flight = default; return; }
            if (!ReferenceEquals(context, local.MovementContext)) return; // Bot updates cannot reset local flight.
            float now = Time.time;
            if (!IsFreeFlight(local) || !Finite(now) || !Finite(deltaTime) || deltaTime < 0f)
            { _flight = default; return; }
            if (!SameFlight(world, local) || !FreshFlight(now, deltaTime))
                _flight = new FlightObservation
                {
                    Body = ArenaHitHistory.BodyId(local), Context = ArenaHitHistory.BodyId(context),
                    World = ArenaHitHistory.BodyId(world), Game = ArenaHitHistory.BodyId(Singleton<AbstractGame>.Instance),
                    Epoch = ArenaShotAwardHistory.CurrentEpoch, StartedAt = now
                };
            _flight.LastAt = now;
            _flight.LastStep = deltaTime;
        }
        catch (Exception ex) { _flight = default; Warn(ex); }
    }

    private static bool IsFreeFlight(Player local)
        => local.HealthController?.IsAlive == true && local.MovementContext != null && local.CharacterController != null
            && !local.MovementContext.IsGrounded && !local.CharacterController.isGrounded
            && !local.MovementContext.PlayerAnimatorGetIsVaulting()
            && !local.MovementContext.IsInMountedState && !local.MovementContext.IsStationaryWeaponInHands;

    private static bool SameFlight(GameWorld world, Player local)
        => _flight.Body != 0 && _flight.Epoch == ArenaShotAwardHistory.CurrentEpoch
            && _flight.Body == ArenaHitHistory.BodyId(local) && _flight.Context == ArenaHitHistory.BodyId(local.MovementContext)
            && _flight.World == ArenaHitHistory.BodyId(world) && _flight.Game == ArenaHitHistory.BodyId(Singleton<AbstractGame>.Instance);

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool FreshFlight(float now, float step)
        // Permit fixed/render update ordering and a slow actual movement step;
        // missing movement observations must not age into a qualifying flight.
        => Finite(now) && Finite(step) && step >= 0f && now >= _flight.LastAt
            && now - _flight.LastAt <= Math.Max(0.1f, 2f * Math.Max(step, _flight.LastStep));

    private static bool HasQualifiedFlight(GameWorld world, Player local)
    {
        if (!IsFreeFlight(local) || !SameFlight(world, local) || !FreshFlight(Time.time, Time.deltaTime))
        { _flight = default; return false; }
        return Time.time - _flight.StartedAt >= MinimumAirborneSeconds;
    }

    internal static void Created(Shot shot)
    {
        if (!_enabled || !ArenaSession.IsActive || shot == null) return;
        try
        {
            // Always overwrite a pooled instance, even for bots/unknown shots.
            long bodyId = ArenaHitHistory.BodyId(shot.Player?.iPlayer);
            string weaponId = shot.Weapon?.Id;
            bool localShot = TryLocal(out var world, out var local) && ReferenceEquals(shot.Player?.iPlayer, local);
            long worldId = ArenaHitHistory.BodyId(world), gameId = ArenaHitHistory.BodyId(Singleton<AbstractGame>.Instance);
            bool known = localShot
                && _fire != null && _fire.Known && _fire.Epoch == ArenaShotAwardHistory.CurrentEpoch
                && _fire.WorldId == worldId && _fire.GameId == gameId
                && _fire.BodyId == bodyId && string.Equals(_fire.WeaponId, weaponId, StringComparison.Ordinal);
            bool singleBullet = localShot && shot.Ammo is Ammo ammo && ammo.ProjectileCount == 1;
            bool continued = localShot && _continuation?.Parent != null
                && ReferenceEquals(shot.Parent, _continuation.Parent)
                && shot.FragmentIndex == _continuation.FragmentIndex
                && ReferenceEquals(shot.Ammo, _continuation.Ammo);
            ArenaShotAwardHistory.RecordCreated(shot, shot.Parent, bodyId, weaponId, shot.FireIndex,
                known, known && _fire.Airborne, known && _fire.BoltAction,
                singleBullet, continued, continued ? _continuation.Token : 0, worldId, gameId);
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
                shot.Weapon?.Id, shot.FireIndex, ArenaHitHistory.BodyId(world),
                ArenaHitHistory.BodyId(Singleton<AbstractGame>.Instance));
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
                info.Weapon?.Id, info.FireIndex, ArenaHitHistory.BodyId(world),
                ArenaHitHistory.BodyId(Singleton<AbstractGame>.Instance));
            return current.ProjectileToken == scope.Facts.ProjectileToken ? current : default;
        }
        catch (Exception ex) { Warn(ex); return default; }
    }

    // Capture only proof here. The match must still admit/classify the death
    // before ArenaCombatAwards may settle its contribution to this lineage.
    internal static ArenaCollateralProof CaptureCollateral(ArenaShotAwardFacts facts)
    {
        try
        {
            var scope = _impact;
            if (scope?.Shot == null || !ArenaShotAwardHistory.IsCurrent(facts)
                || facts.ProjectileToken != scope.Facts.ProjectileToken
                || !TryLocal(out var world, out var local) || !ReferenceEquals(world, scope.World)) return default;
            var current = ArenaShotAwardHistory.ForImpact(scope.Shot, ArenaHitHistory.BodyId(local),
                scope.Shot.Weapon?.Id, scope.Shot.FireIndex, ArenaHitHistory.BodyId(world),
                ArenaHitHistory.BodyId(Singleton<AbstractGame>.Instance));
            return current.ProjectileToken == facts.ProjectileToken
                ? ArenaShotAwardHistory.CaptureCollateral(scope.Shot, current) : default;
        }
        catch (Exception ex) { Warn(ex); return default; }
    }

    internal static IDisposable BeginContinuation(Shot parent)
    {
        if (!_enabled || !ArenaSession.IsActive) return null;
        var scope = new ContinuationScope(_continuation);
        _continuation = scope;
        try
        {
            if (!TryLocal(out var world, out var local) || parent == null
                || !ReferenceEquals(parent.Player?.iPlayer, local)) return scope;
            var facts = ArenaShotAwardHistory.ForImpact(parent, ArenaHitHistory.BodyId(local),
                parent.Weapon?.Id, parent.FireIndex, ArenaHitHistory.BodyId(world),
                ArenaHitHistory.BodyId(Singleton<AbstractGame>.Instance));
            if (!facts.IsKnown) return scope;
            scope.Ammo = parent.Ammo;
            scope.FragmentIndex = parent.FragmentIndex;
            scope.Token = facts.ProjectileToken;
            scope.Parent = parent;
        }
        catch (Exception ex) { scope.Parent = null; Warn(ex); }
        return scope;
    }

    private sealed class FireScope : IDisposable
    {
        private readonly FireScope _previous;
        internal readonly long Epoch = ArenaShotAwardHistory.CurrentEpoch;
        internal long BodyId, WorldId, GameId;
        internal string WeaponId;
        internal bool Known, Airborne, BoltAction;
        internal FireScope(FireScope previous) { _previous = previous; }
        public void Dispose() { if (ReferenceEquals(_fire, this)) _fire = _previous; }
    }

    private sealed class ContinuationScope : IDisposable
    {
        private readonly ContinuationScope _previous;
        internal Shot Parent;
        internal Item Ammo;
        internal int FragmentIndex;
        internal long Token;
        internal ContinuationScope(ContinuationScope previous) { _previous = previous; }
        public void Dispose()
        {
            if (ReferenceEquals(_continuation, this)) _continuation = _previous;
            Parent = null;
            Ammo = null;
        }
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

    private sealed class ArenaShotAwardHooksFirePatch : ModulePatch
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

    private sealed class ArenaShotAwardHooksFlightPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(MovementContext), nameof(MovementContext.CheckFlying), new[] { typeof(float) });
        [PatchPostfix]
        private static void Postfix(MovementContext __instance, float deltaTime) => ObserveFlight(__instance, deltaTime);
    }

    private sealed class ArenaShotAwardHooksCreatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => typeof(Shot).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Single(method => method.Name == nameof(Shot.Create) && method.ReturnType == typeof(Shot));
        [PatchPostfix]
        private static void Postfix(Shot __result) => Created(__result);
    }

    private sealed class ArenaShotAwardHooksImpactPatch : ModulePatch
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

    // Native penetration creates a child with the SAME FragmentIndex, while
    // ricochet and fragmentation create independent projectiles. Do not infer
    // a lineage from a shared FireIndex, cartridge ID, timestamp or parent alone.
    private sealed class ArenaShotAwardHooksContinuationPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(Shot), nameof(Shot.CreateDeviatedFragment), Type.EmptyTypes);
        [PatchPrefix]
        private static void Prefix(Shot __instance, out IDisposable __state)
            => __state = BeginContinuation(__instance);
        [PatchFinalizer]
        private static Exception Finalizer(Exception __exception, IDisposable __state)
        { __state?.Dispose(); return __exception; }
    }
}
