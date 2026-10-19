using System;
using System.Linq;
using System.Reflection;
using ArenaMode.Diagnostics;
using ArenaMode.Session;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using HarmonyLib;
using Redeploy.Core.GameAccess;
using Version = System.Version;

namespace ArenaMode.Compat;

/// <summary>Keep Arena activity out of StatTrack without touching its ordinary pending saves.</summary>
internal static class ArenaStatTrackCompat
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".StatTrackCompat");
    private static readonly ArenaStatTrackScope Scope = new();
    private static readonly string[] Guards = { nameof(BeforeShot), nameof(BeforeKill), nameof(BeforeHit), nameof(BeforeMerge) };
    private static MethodInfo[] _foreign;
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static bool _initialized, _attempted, _faulted, _reportedNewer;
    private static int _reported;
    private static string _failure, _lastWarning;

    internal static void Initialize()
    { if (_initialized) return; _initialized = true; Refresh(); }

    internal static string GetRejectionReason()
    { if (!_initialized) Initialize(); else Refresh(); return _failure; }

    private static void Refresh()
    {
        if (!Chainloader.PluginInfos.TryGetValue(ArenaStatTrackContract.PluginGuid, out var info))
        { ClearBinding(); _failure = null; _faulted = false; return; }
        if (_faulted) return;
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("StatTrack has not finished loading");
            if (_foreign != null && ReferenceEquals(_plugin, info.Instance)
                && _version == ArenaStatTrackContract.Normalize(info.Metadata?.Version))
            {
                ArenaStatTrackContract.VerifyNativeBindings(_foreign);
                if (!OwnsPatches()) throw new InvalidOperationException("StatTrack compatibility hooks were removed");
                return;
            }
            ClearBinding();
            var methods = ArenaStatTrackContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID,
                info.Metadata?.Version, out var version);
            _attempted = true;
            for (int i = 0; i < methods.Length; ++i)
                Patches.Patch(methods[i], prefix: new HarmonyMethod(typeof(ArenaStatTrackCompat), Guards[i]) { priority = Priority.First });
            Patches.Patch(AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted)),
                prefix: new HarmonyMethod(typeof(ArenaStatTrackCompat), nameof(BeforeWorldStarted)) { priority = Priority.First });
            Patches.Patch(AccessTools.Method(typeof(GameWorld), nameof(GameWorld.RegisterPlayer)),
                postfix: new HarmonyMethod(typeof(ArenaStatTrackCompat), nameof(AfterRegister)) { priority = Priority.First });
            Patches.Patch(InventoryConstructor,
                prefix: new HarmonyMethod(typeof(ArenaStatTrackCompat), nameof(BeforeInventoryConstruction)) { priority = Priority.First });
            _foreign = methods; _plugin = info.Instance; _version = version;
            if (!OwnsPatches()) throw new InvalidOperationException("StatTrack compatibility hooks did not install");
            _failure = null;
            Plugin.ModLogger.LogDebug("Arena StatTrack: exact-body collection and end-merge guards ready; ordinary saves remain enabled.");
            if (version > ArenaStatTrackContract.ReviewedVersion && !_reportedNewer)
            {
                _reportedNewer = true;
                Plugin.ModLogger.LogWarning("Arena StatTrack " + version
                    + " is newer than reviewed 2.1.1; collection/save APIs were checked, but runtime compatibility is unverified.");
            }
        }
        catch (Exception ex) { ClearBinding(); Reject(ex); }
    }

    private static bool Receipt(MethodBase target, string hook, bool postfix = false)
    {
        var info = Harmony.GetPatchInfo(target);
        return (postfix ? info?.Postfixes : info?.Prefixes)?.Any(p => p.owner == Patches.Id
            && p.PatchMethod == typeof(ArenaStatTrackCompat).GetMethod(hook, BindingFlags.NonPublic | BindingFlags.Static)) == true;
    }
    private static bool OwnsPatches()
        => _foreign != null && _foreign.Select((m, i) => Receipt(m, Guards[i])).All(x => x)
            && Receipt(AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted)), nameof(BeforeWorldStarted))
            && Receipt(AccessTools.Method(typeof(GameWorld), nameof(GameWorld.RegisterPlayer)), nameof(AfterRegister), true)
            && Receipt(InventoryConstructor, nameof(BeforeInventoryConstruction));
    private static ConstructorInfo InventoryConstructor => typeof(Player.SinglePlayerInventoryController)
        .GetConstructor(new[] { typeof(Player), typeof(Profile), typeof(bool), typeof(bool) });

    private static bool CurrentWorld(out GameWorld world)
    {
        world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        var game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
        if (ReferenceEquals(world, null) || ReferenceEquals(game, null)
            || !ReferenceEquals(game.GameWorld, world) || !Singleton<IBotGame>.Instantiated
            || !ReferenceEquals(Singleton<IBotGame>.Instance, game)) return false;
        var settings = LocalGameAccessor.GetLocalRaidSettings(game);
        return ArenaStatTrackScope.CanCaptureWorld(true, game.Status == GameStatus.Started,
            settings?.playerSide == ESideType.Pmc, world.LocationId, settings?.location,
            ArenaSession.IsExactActiveRaid(game, world), ArenaSession.ArmedForNextRaid,
            ArenaServerClient.HasPendingArm, ArenaSession.ArmedMap, ArenaServerClient.PendingArmToken, settings?.serverId);
    }

    private static bool Owns(Player body)
    {
        if (ReferenceEquals(body, null)) return false;
        if (Scope.OwnsBody(body, body.GameWorld, null, false)) return true;
        bool current = CurrentWorld(out var world);
        return Scope.OwnsBody(body, body.GameWorld, world, current);
    }
    private static bool Owns(Weapon weapon)
    {
        if (ReferenceEquals(weapon, null)) return false;
        var owner = weapon.Owner;
        var body = (owner as Player.PlayerInventoryController)?.Player;
        if (Scope.OwnsWeapon(weapon, owner, body, body?.GameWorld, null, false)) return true;
        bool current = CurrentWorld(out var world);
        return Scope.OwnsWeapon(weapon, owner, body, body?.GameWorld, world, current);
    }
    private static bool Guard(int kind, Func<bool> owned)
    {
        if (_foreign == null) return true;
        try
        {
            if (!owned()) return true;
            if (ArenaLog.DeveloperEnabled && (_reported & (1 << kind)) == 0)
            {
                _reported |= 1 << kind;
                ArenaLog.Developer("StatTrack: Arena " + new[] { "shot", "kill", "hit", "body end/merge" }[kind]
                    + " excluded from persistent weapon statistics.");
            }
            return false;
        }
        catch (Exception ex) { _faulted = true; Reject(ex); return true; }
    }
    private static bool BeforeShot(Weapon __0) => Guard(0, () => Owns(__0));
    private static bool BeforeKill(Player __0, IPlayer __1, DamageInfo __2)
        => Guard(1, () => Owns(__0) || Owns(__1 as Player) || Owns(__2.Player?.iPlayer as Player) || Owns(__2.Weapon as Weapon));
    private static bool BeforeHit(Player __0, DamageInfo __3)
        => Guard(2, () => Owns(__0) || Owns(__3.Player?.iPlayer as Player) || Owns(__3.Weapon as Weapon));
    private static bool BeforeMerge(IPlayer __0) => Guard(3, () => Owns(__0 as Player));

    private static void BeforeWorldStarted(GameWorld __instance)
    {
        if (_foreign == null) return;
        try
        {
            if (!CurrentWorld(out var world) || !ReferenceEquals(world, __instance)) return;
            foreach (var player in world.RegisteredPlayers) if (player is Player body)
                Scope.OwnsBody(body, body.GameWorld, world, true);
        }
        catch (Exception ex) { _faulted = true; Reject(ex); }
    }
    private static void AfterRegister(GameWorld __instance, IPlayer iPlayer)
    {
        if (_foreign == null || iPlayer is not Player body || !ReferenceEquals(body.GameWorld, __instance)) return;
        try
        {
            if (Owns(body)) return;
            // Initial LocalPlayer.Init registers before BotsController.Init and
            // OnGameStarted. Lease that exact confirmed constructor too, so an
            // aborted loading attempt cannot re-merge a preceding ordinary raid.
            if (__instance.RegisteredPlayers.Contains(body)) CaptureInitialBody(body, body.Profile);
        }
        catch (Exception ex) { _faulted = true; Reject(ex); }
    }
    // LocalPlayer.Create sets IsYourPlayer and immediately constructs this
    // controller, before quest setup and Player.Init can fail. The body has no
    // assigned Profile yet; the exact native constructor argument proves it.
    private static void BeforeInventoryConstruction(Player __0, Profile __1)
    {
        if (_foreign == null) return;
        try { if (!Owns(__0)) CaptureInitialBody(__0, __1); }
        catch (Exception ex) { _faulted = true; Reject(ex); }
    }
    private static void CaptureInitialBody(Player body, Profile profile)
    {
        var game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        bool initial = body is LocalPlayer && body.IsYourPlayer && game != null
            && game.Status == GameStatus.Running && ReferenceEquals(game.LocalPlayer, null)
            && profile != null && ReferenceEquals(profile, game.Profile)
            && ReferenceEquals(body.GameWorld, world) && ReferenceEquals(game.GameWorld, world);
        if (!initial) return;
        var settings = LocalGameAccessor.GetLocalRaidSettings(game);
        bool confirmed = ArenaStatTrackScope.CanCaptureWorld(true, true, settings?.playerSide == ESideType.Pmc,
            world.LocationId, settings?.location, false, ArenaSession.ArmedForNextRaid,
            ArenaServerClient.HasPendingArm, ArenaSession.ArmedMap, ArenaServerClient.PendingArmToken, settings?.serverId);
        Scope.OwnsBody(body, body.GameWorld, world, confirmed);
    }
    private static void Reject(Exception ex)
    {
        _failure = "Arena cannot safely isolate StatTrack: " + ex.GetBaseException().Message
            + ". Update or remove StatTrack, then restart. Ordinary raids remain available.";
        if (_lastWarning == _failure) return;
        _lastWarning = _failure; Plugin.ModLogger.LogWarning(_failure);
    }
    private static void ClearBinding()
    {
        _foreign = null; _plugin = null; _version = null; _reported = 0; Scope.Clear();
        if (!_attempted) return;
        _attempted = false;
        try { Patches.UnpatchSelf(); } catch (Exception ex) { Reject(ex); }
    }
    internal static void Shutdown()
    { ClearBinding(); _initialized = _faulted = false; _failure = _lastWarning = null; }
}
