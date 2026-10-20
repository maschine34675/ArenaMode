using System;
using System.Linq;
using System.Reflection;
using ArenaMode.Session;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using JsonType;
using Redeploy.Core.GameAccess;
using Version = System.Version;

namespace ArenaMode.Compat;

/// <summary>Suppress nonpersistent Arena XP at its producer; never open or rewrite the foreign journal.</summary>
internal static class ArenaSkillsExtendedCompat
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".SkillsExtendedCompat");
    private static readonly ArenaSkillsExtendedScope Scope = new();
    private static ArenaSkillsExtendedContract _contract;
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static bool _initialized, _attempted, _faulted, _reportedNewer;
    private static string _failure, _lastWarning;
    private static WeakReference<GameWorld> _reportedWorld;

    internal static void Initialize()
    { if (_initialized) return; _initialized = true; Refresh(); }
    internal static string GetRejectionReason()
    { if (!_initialized) Initialize(); else Refresh(); return _failure; }

    private static void Refresh()
    {
        if (!Chainloader.PluginInfos.TryGetValue(ArenaSkillsExtendedContract.PluginGuid, out var info))
        { ClearBinding(); _failure = null; _faulted = false; return; }
        if (_faulted) return;
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("Skills Extended has not finished loading");
            if (_contract != null && ReferenceEquals(_plugin, info.Instance)
                && _version == ArenaSkillsExtendedContract.Normalize(info.Metadata?.Version))
            {
                _contract.VerifyNativeBinding();
                if (!OwnsPatches()) throw new InvalidOperationException("Skills Extended compatibility hooks were removed");
                return;
            }
            ClearBinding();
            var contract = ArenaSkillsExtendedContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID,
                info.Metadata?.Version, out var version);
            _attempted = true;
            Patch(contract.Boot, nameof(BeforeBoot));
            Patch(contract.ApplyXp, nameof(BeforeSignalsXp));
            Patch(contract.Started, nameof(BeforeStarted), nameof(AfterStarted));
            Patch(contract.Medical, nameof(BeforeBodyXp));
            Patch(contract.Nato, nameof(BeforeBodyXp));
            Patch(contract.Eastern, nameof(BeforeBodyXp));
            _contract = contract; _plugin = info.Instance; _version = version;
            if (!OwnsPatches()) throw new InvalidOperationException("Skills Extended compatibility hooks did not install");
            _failure = null;
            Plugin.ModLogger.LogDebug("Arena Skills Extended: exact-world reward and XP subscription guards ready.");
            if (version > ArenaSkillsExtendedContract.ReviewedVersion && !_reportedNewer)
            {
                _reportedNewer = true;
                Plugin.ModLogger.LogWarning("Arena Skills Extended " + version
                    + " is newer than reviewed 3.1.4; required APIs were checked, but runtime compatibility is unverified.");
            }
        }
        catch (Exception ex) { ClearBinding(); Reject(ex); }
    }

    private static void Patch(MethodInfo method, string prefix, string postfix = null)
        => Patches.Patch(method, prefix: new HarmonyMethod(typeof(ArenaSkillsExtendedCompat), prefix) { priority = Priority.First },
            postfix: postfix == null ? null : new HarmonyMethod(typeof(ArenaSkillsExtendedCompat), postfix) { priority = Priority.Last });
    private static bool Receipt(MethodBase method, string name, bool postfix = false)
    {
        var patches = Harmony.GetPatchInfo(method);
        return (postfix ? patches?.Postfixes : patches?.Prefixes)?.Any(p => p.owner == Patches.Id
            && p.PatchMethod == typeof(ArenaSkillsExtendedCompat).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)) == true;
    }
    private static bool OwnsPatches() => _contract != null
        && Receipt(_contract.Boot, nameof(BeforeBoot)) && Receipt(_contract.ApplyXp, nameof(BeforeSignalsXp))
        && Receipt(_contract.Started, nameof(BeforeStarted)) && Receipt(_contract.Started, nameof(AfterStarted), true)
        && new[] { _contract.Medical, _contract.Nato, _contract.Eastern }.All(m => Receipt(m, nameof(BeforeBodyXp)));

    private static bool OwnsWorld(GameWorld candidate)
    {
        if (ReferenceEquals(candidate, null)) return false;
        if (Scope.OwnsWorld(candidate, null, false)) return true;
        var game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        if (ReferenceEquals(game, null) || ReferenceEquals(world, null)
            || !ReferenceEquals(game.GameWorld, world) || !ReferenceEquals(candidate, world)) return false;
        // Signals Boot runs while native loot loads, before IBotGame exists.
        // The exact native server-confirmed arm identity is still mandatory.
        // Once started, require the actual bot-game pair as well.
        bool loading = game.Status == GameStatus.Running && game.Profile != null
            && (ReferenceEquals(game.LocalPlayer, null)
                || ReferenceEquals(game.LocalPlayer, world.MainPlayer) && game.LocalPlayer.IsYourPlayer
                    && ReferenceEquals(game.LocalPlayer.Profile, game.Profile));
        bool started = game.Status == GameStatus.Started && Singleton<IBotGame>.Instantiated
            && ReferenceEquals(Singleton<IBotGame>.Instance, game);
        var settings = LocalGameAccessor.GetLocalRaidSettings(game);
        bool own = ArenaSkillsExtendedScope.CanCapture(true, loading || started, settings?.playerSide == ESideType.Pmc,
            world.LocationId, settings?.location, ArenaSession.IsExactActiveRaid(game, world),
            ArenaSession.ArmedForNextRaid, ArenaServerClient.HasPendingArm, ArenaSession.ArmedMap,
            ArenaServerClient.PendingArmToken, settings?.serverId);
        return Scope.OwnsWorld(candidate, world, own);
    }

    private static void Capture(GameWorld world)
    {
        if (!OwnsWorld(world)) return;
        var body = world.MainPlayer;
        if (!ReferenceEquals(body, null) && body.IsYourPlayer && ReferenceEquals(body.GameWorld, world))
            Scope.OwnsBody(body, world);
        if (_reportedWorld == null || !_reportedWorld.TryGetTarget(out var previous) || !ReferenceEquals(previous, world))
        {
            _reportedWorld = new WeakReference<GameWorld>(world);
            Plugin.ModLogger.LogInfo("Arena: Skills Extended Signals rewards and medical/weapon mastery XP are paused for this match. Other skill features and ordinary raids remain enabled.");
        }
    }

    private static void BeforeBoot(GameWorld __0)
    { if (_contract == null) return; try { Capture(__0); } catch (Exception ex) { Fault(ex); } }
    private static void BeforeStarted(GameWorld __0)
    { if (_contract == null) return; try { Capture(__0); } catch (Exception ex) { Fault(ex); } }

    private static bool BeforeSignalsXp(object __instance)
    {
        if (_contract == null) return true;
        try
        {
            var world = _contract.RuntimeWorld.GetValue(__instance) as GameWorld;
            return !OwnsWorld(world);
        }
        catch (Exception ex) { Fault(ex); return true; }
    }

    private static bool BeforeBodyXp()
    {
        if (_contract == null) return true;
        try
        {
            var body = _contract.BoundPlayer.GetValue(null) as Player;
            return ReferenceEquals(body, null) || !Scope.OwnsBody(body, body.GameWorld);
        }
        catch (Exception ex) { Fault(ex); return true; }
    }

    private static void AfterStarted(GameWorld __0)
    {
        if (_contract == null) return;
        try
        {
            if (!OwnsWorld(__0)) return;
            var body = _contract.BoundPlayer.GetValue(null) as Player;
            if (ReferenceEquals(body, null) || !body.IsYourPlayer || !ReferenceEquals(body.GameWorld, __0)
                || !Scope.OwnsBody(body, __0)) return;
            // Keep the foreign original (including its door fix), but suspend
            // exactly these progression subscribers for this Arena body.
            // No Arena replacements subscribe them later, so no reinitializing
            // the foreign world or copying callbacks to a new body is needed.
            body.ActiveHealthController.EffectStartedEvent -= _contract.MedicalHandler;
            body.Skills.OnMasteringExperienceChanged -= _contract.NatoHandler;
            body.Skills.OnMasteringExperienceChanged -= _contract.EasternHandler;
        }
        catch (Exception ex) { Fault(ex); }
    }

    private static void Fault(Exception ex)
    { _faulted = true; Reject(ex); }
    private static void Reject(Exception ex)
    {
        _failure = "Arena cannot safely isolate Skills Extended: " + ex.GetBaseException().Message
            + ". Update or remove Skills Extended, then restart. Ordinary raids remain available.";
        if (_lastWarning == _failure) return;
        _lastWarning = _failure; Plugin.ModLogger.LogWarning(_failure);
    }
    private static void ClearBinding()
    {
        _contract = null; _plugin = null; _version = null; _reportedWorld = null; Scope.Clear();
        if (!_attempted) return;
        _attempted = false;
        try { Patches.UnpatchSelf(); } catch (Exception ex) { Reject(ex); }
    }
    internal static void Shutdown()
    { ClearBinding(); _initialized = _faulted = false; _failure = _lastWarning = null; }
}
