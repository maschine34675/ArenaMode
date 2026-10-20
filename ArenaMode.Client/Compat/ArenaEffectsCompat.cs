using System;
using System.Linq;
using System.Reflection;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using JsonType;
using Redeploy.Core.Api;
using Redeploy.Core.GameAccess;

namespace ArenaMode.Compat;

/// <summary>Rebind optional custom health effects only after a committed Arena body replacement.</summary>
internal static class ArenaEffectsCompat
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".EffectsCompat");
    private static readonly ArenaEffectsScope Scope = new();
    private static ArenaEffectsContract _contract;
    private static bool _initialized, _dirty, _fault, _newerWarned, _patchAttempted;
    private static string _failure, _warning;

    internal static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        AppDomain.CurrentDomain.AssemblyLoad += Loaded;
        RespawnApi.Replaced += Replaced;
        RespawnApi.RaidEnded += Ended;
        Refresh();
    }
    private static void Loaded(object sender, AssemblyLoadEventArgs args)
    { if (args.LoadedAssembly.GetName().Name == ArenaEffectsContract.AssemblyName) _dirty = true; }
    internal static string GetRejectionReason()
    { if (!_initialized) Initialize(); else Refresh(); return _failure; }
    private static void Refresh()
    {
        if (_fault) return;
        _dirty = false;
        try
        {
            if (_contract != null) { _contract.VerifyNativeBindings(); VerifyPatches(); return; }
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name == ArenaEffectsContract.AssemblyName).ToArray();
            if (assemblies.Length == 0) { _failure = null; return; }
            if (assemblies.Length != 1) throw new InvalidOperationException("Multiple EFT Effects Manager libraries are loaded");
            var contract = ArenaEffectsContract.Resolve(assemblies[0]);
            _contract = contract; _patchAttempted = true;
            Patches.Patch(contract.Started, postfix: new HarmonyMethod(typeof(ArenaEffectsCompat), nameof(AfterStarted)) { priority = Priority.Last });
            Patches.Patch(contract.Ended, prefix: new HarmonyMethod(typeof(ArenaEffectsCompat), nameof(BeforeEnded)) { priority = Priority.First });
            VerifyPatches(); _failure = null;
            Plugin.ModLogger.LogDebug("Arena Effects Manager: physical-body lifecycle adapter ready.");
            if (assemblies[0].ManifestModule.ModuleVersionId.ToString() != ArenaEffectsContract.ReviewedMvid && !_newerWarned)
            {
                _newerWarned = true;
                Plugin.ModLogger.LogWarning("EFT Effects Manager differs from reviewed release 1.1.0; required APIs match, native compatibility is unverified.");
            }
        }
        catch (Exception ex) { ClearPatches(); Reject(ex); }
    }
    private static void VerifyPatches()
    {
        bool Has(MethodInfo target, string name, bool prefix)
        {
            var info = Harmony.GetPatchInfo(target);
            return (prefix ? info?.Prefixes : info?.Postfixes)?.Count(p => p.owner == Patches.Id
                && p.PatchMethod == typeof(ArenaEffectsCompat).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)) == 1;
        }
        if (!Has(_contract.Started, nameof(AfterStarted), false) || !Has(_contract.Ended, nameof(BeforeEnded), true))
            throw new MissingMethodException("Effects compatibility hooks were removed");
    }
    private static LocalGame Game => Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
    private static GameWorld World => Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
    private static bool Exact(LocalGame game, GameWorld world)
    {
        bool pair = game != null && world != null && world is not HideoutGameWorld
            && ReferenceEquals(game.GameWorld, world) && ReferenceEquals(World, world)
            && Singleton<IBotGame>.Instantiated && ReferenceEquals(Singleton<IBotGame>.Instance, game);
        var settings = pair ? LocalGameAccessor.GetLocalRaidSettings(game) : null;
        return ArenaEffectsScope.ExactRaid(pair,
            game != null && (game.Status == GameStatus.Running || game.Status == GameStatus.Starting || game.Status == GameStatus.Started),
            settings?.playerSide == ESideType.Pmc, world?.LocationId, settings?.location,
            pair && ArenaSession.IsExactActiveRaid(game, world), ArenaSession.ArmedForNextRaid,
            ArenaServerClient.HasPendingArm, ArenaSession.ArmedMap, ArenaServerClient.PendingArmToken, settings?.serverId);
    }
    private static bool Current(Player body, LocalGame game, GameWorld world)
        => body != null && body is not HideoutPlayer && body.HealthController is PlayerHealthController
            && ArenaEffectsScope.CanBind(Exact(game, world), body.IsYourPlayer, ReferenceEquals(body.GameWorld, world),
                ReferenceEquals(body.Profile, game?.Profile), ReferenceEquals(world?.MainPlayer, body),
                ReferenceEquals(game?.LocalPlayer, body), body.HealthController.IsAlive);
    private static void AfterStarted(GameWorld __0)
    {
        if (_contract == null) return;
        try
        {
            var body = _contract.Player; var game = Game;
            if (!Current(body, game, __0)) return;
            if (!_contract.IsBound(body) || !ReferenceEquals(_contract.World, __0))
                throw new InvalidOperationException("Effects initial Arena binding is incomplete");
            Scope.Capture(game, __0, body, body.HealthController);
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static void Replaced(Player previous, Player next, string reason)
    {
        if (_contract == null) return;
        try
        {
            var old = Scope.Find(previous); var game = Game; var world = World;
            // An initial kit replacement can finish before our observer has
            // seen the library's OnGameStarted binding. Core's exact old/new
            // event plus the library's physical old binding is sufficient.
            if (old == null && previous != null && previous.IsYourPlayer && Exact(game, world)
                && ReferenceEquals(previous.GameWorld, world) && ReferenceEquals(previous.Profile, game.Profile)
                && ReferenceEquals(_contract.World, world) && _contract.IsBound(previous)
                && Current(next, game, world))
                old = Scope.Capture(game, world, previous, previous.HealthController);
            if (old == null) return;
            if (!ArenaEffectsScope.CanReplace(old, game, world, previous, next) || !Current(next, game, world)) return;
            Replace(old, next, game, world);
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static void Replace(ArenaEffectsScope.Lease old, Player next, LocalGame game, GameWorld world)
    {
        if (!ReferenceEquals(_contract.World, old.World)) throw new InvalidOperationException("Effects world changed before transfer");
        _contract.RemoveBodyState((Player)old.Body);
        Scope.Retire(old);
        Scope.Capture(game, world, next, next.HealthController);
        _contract.BindBody(next, world);
        Plugin.ModLogger.LogDebug("Arena Effects Manager: custom effects rebound to the confirmed replacement body.");
    }
    internal static int SkillBonusFor(Skill skill)
    {
        if (_contract == null || skill == null) return 0;
        try
        {
            var body = _contract.Player;
            if (ReferenceEquals(body, null)) return 0;
            var lease = Scope.Find(body);
            bool owned = lease != null && !lease.Retired && ReferenceEquals(_contract.World, lease.World)
                && ReferenceEquals(body.HealthController, lease.Controller);
            if (!owned && Current(body, Game, World))
            {
                if (!_contract.IsBound(body) || !ReferenceEquals(_contract.World, World))
                    throw new InvalidOperationException("Effects skill owner is incomplete");
                Scope.Capture(Game, World, body, body.HealthController);
                owned = true;
            }
            return owned ? _contract.SkillBonusFor(body, skill) : 0;
        }
        catch (Exception ex)
        {
            Fault(ex);
            // ArenaSkills retains its restore snapshot on a failing setter
            // path. Returning zero would silently erase a still-owned bonus.
            throw new InvalidOperationException("Effects temporary skill ownership could not be read", ex);
        }
    }
    internal static void Tick()
    {
        if (!_initialized) return;
        if (_dirty) Refresh();
        if (_contract == null || _fault) return;
        try
        {
            var game = Game; var world = World; var next = world?.MainPlayer;
            if (RespawnApi.IsReplacing || !Current(next, game, world)) return;
            var old = Scope.Current;
            if (old != null && ArenaEffectsScope.CanReplace(old, game, world, old.Body, next))
                Replace(old, next, game, world);
            else if (old == null && ReferenceEquals(_contract.World, world) && _contract.IsBound(next))
                Scope.Capture(game, world, next, next.HealthController);
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static bool BeforeEnded(GameWorld __0)
    {
        if (_contract == null || Scope.Current == null) return true;
        var lease = Scope.Current;
        if (!ReferenceEquals(__0, null) && !ReferenceEquals(__0, lease.World)) return true;
        if (!ReferenceEquals(_contract.Player, lease.Body) || !ReferenceEquals(_contract.World, lease.World)) return true;
        try { Release(lease); }
        catch (Exception ex) { Fault(ex); }
        // Never let an exact Arena cleanup erase foreign-controller bonus pools.
        return false;
    }
    private static void Release(ArenaEffectsScope.Lease lease)
    {
        _contract.RemoveBodyState((Player)lease.Body);
        _contract.WhizzDetach.Invoke(null, null);
        Scope.Retire(lease);
    }
    private static void Ended()
    {
        if (_contract == null || Scope.Current == null) return;
        var lease = Scope.Current;
        if (!ReferenceEquals(_contract.Player, lease.Body) || !ReferenceEquals(_contract.World, lease.World)) return;
        try { Release(lease); } catch (Exception ex) { Fault(ex); }
    }
    private static void Fault(Exception ex) { _fault = true; Reject(ex); }
    private static void Reject(Exception ex)
    {
        _failure = "EFT Effects Manager compatibility could not be verified: " + (ex.InnerException ?? ex).Message
            + ". Arena is unavailable; ordinary PMC/Scav raids remain available.";
        if (_warning != _failure) { _warning = _failure; Plugin.ModLogger.LogWarning(_failure); }
    }
    private static void ClearPatches()
    {
        if (_patchAttempted)
            try { Patches.UnpatchSelf(); }
            catch (Exception ex) { Plugin.ModLogger.LogWarning("Arena Effects hook cleanup failed: " + ex.Message); }
        _patchAttempted = false; _contract = null;
    }
    internal static void Shutdown()
    {
        if (!_initialized) return;
        Ended();
        RespawnApi.Replaced -= Replaced; RespawnApi.RaidEnded -= Ended;
        AppDomain.CurrentDomain.AssemblyLoad -= Loaded;
        ClearPatches(); Scope.Clear(); _initialized = _dirty = _fault = _newerWarned = false;
        _failure = _warning = null;
    }
}
