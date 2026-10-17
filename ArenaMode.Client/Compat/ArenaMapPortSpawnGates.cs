using System;
using System.Reflection;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using ArenaMode.Session;
using ArenaMode.Shared;
using Redeploy.Core.GameAccess;

namespace ArenaMode.Compat;

/// <summary>
/// Optional MapPort contract, resolved only after an exact team-box TDM loading
/// owner exists. The API is process-global, so never borrow an existing hold or
/// release it from a replacement world/arm. No menu-time request is made.
/// </summary>
internal static class ArenaMapPortSpawnGates
{
    private const string PluginId = "com.maschine.ArenaMapPort";

    internal static Lease TryHold(LocalGame game, GameWorld world, int epoch, string token)
    {
        if (!Chainloader.PluginInfos.TryGetValue(PluginId, out var plugin) || plugin.Instance == null)
            return null;
        return TryHoldContract(plugin.Instance.GetType().Assembly.GetType("ArenaMapPort.Client.SpawnGates"),
            game, world, epoch, token);
    }

    // Kept separate for the production-linked optional-contract checks. The
    // real caller supplies only the registered MapPort plugin's own assembly.
    internal static Lease TryHoldContract(Type type, LocalGame game, GameWorld world, int epoch, string token)
    {
        if (type == null || game == null || world == null
            || !ArenaSession.ArmedForNextRaid
            || !Shared.ArenaTeamBoxCatalog.Applies(ArenaSession.ArmedMap, ArenaSession.ArmedMode)
            || !string.Equals(world.LocationId, ArenaSession.ArmedMap, StringComparison.OrdinalIgnoreCase)
            || !IsOwner(game, world, epoch, token)) return null;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
        MethodInfo hold, release, held, parts;
        try
        {
            hold = type.GetMethod("Hold", flags, null, new[] { typeof(string) }, null);
            release = type.GetMethod("Release", flags, null, new[] { typeof(string) }, null);
            held = type.GetProperty("IsHeld", flags)?.GetGetMethod();
            parts = type.GetProperty("ClosedParts", flags)?.GetGetMethod();
        }
        catch (AmbiguousMatchException) { return null; }
        if (hold == null || hold.ReturnType != typeof(bool) || hold.ContainsGenericParameters
            || release == null || release.ReturnType != typeof(void) || release.ContainsGenericParameters
            || held == null || held.ReturnType != typeof(bool) || held.GetParameters().Length != 0
            || parts == null || parts.ReturnType != typeof(int) || parts.GetParameters().Length != 0) return null;
        Lease lease = null;
        try
        {
            var isHeld = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), held);
            var closedParts = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), parts);
            // Another mode/developer hold is not ours to alter. No gate geometry
            // has been offered yet means this is not a usable owned map scene.
            if (isHeld() || closedParts() <= 0) return null;
            lease = new Lease(game, world, epoch, token, isHeld,
                (Action<string>)Delegate.CreateDelegate(typeof(Action<string>), release));
            var request = (Func<string, bool>)Delegate.CreateDelegate(typeof(Func<string, bool>), hold);
            if (!request("ArenaMode team-box TDM assembling")) return null;
            lease.Accepted = true;
            if (lease.IsHeld) return lease;
            lease.Release("ArenaMode hold did not remain active");
        }
        catch (Exception ex)
        {
            // A throwing Hold may already have changed MapPort's request. We
            // observed it unheld immediately before this synchronous attempt.
            if (lease != null) { lease.Accepted = true; lease.Release("ArenaMode gate request failed"); }
            Plugin.ModLogger.LogWarning("Arena team-box spawn gates unavailable: " + ex.GetBaseException().Message);
        }
        return null;
    }

    private static bool IsOwner(LocalGame game, GameWorld world, int epoch, string token)
    {
        if (epoch != ArenaSession.ArmEpoch || !ReferenceEquals(game.GameWorld, world)
            || !Singleton<AbstractGame>.Instantiated || !ReferenceEquals(Singleton<AbstractGame>.Instance, game)
            || !Singleton<GameWorld>.Instantiated || !ReferenceEquals(Singleton<GameWorld>.Instance, world)) return false;
        var settings = LocalGameAccessor.GetLocalRaidSettings(game);
        return settings != null && Protocol.IsLifecycleRequestId(token)
            && string.Equals(settings.location, world.LocationId, StringComparison.OrdinalIgnoreCase)
            && (Protocol.IsNativeStartIdentity(settings.serverId, token)
                || Protocol.HasNativeEndIdentity(settings.serverId, token));
    }

    internal sealed class Lease
    {
        private readonly LocalGame _game;
        private readonly GameWorld _world;
        private readonly int _epoch;
        private readonly string _token;
        private readonly Func<bool> _isHeld;
        private readonly Action<string> _release;
        internal bool Accepted;
        internal bool Released { get; private set; }
        internal Lease(LocalGame game, GameWorld world, int epoch, string token, Func<bool> held, Action<string> release)
        { _game = game; _world = world; _epoch = epoch; _token = token; _isHeld = held; _release = release; }
        internal bool OwnsCurrentRaid => Accepted && IsOwner(_game, _world, _epoch, _token);
        internal bool IsHeld => !Released && OwnsCurrentRaid && _isHeld();
        internal void Release(string reason)
        {
            if (!Accepted || Released) return;
            // A new world/arm owns any new MapPort state; its scene unload owns
            // disposal of the old one. Never send a global release into it.
            if (!IsOwner(_game, _world, _epoch, _token)) { Released = true; return; }
            try { _release(reason); Released = true; }
            catch (Exception ex)
            { Plugin.ModLogger.LogWarning("Arena team-box spawn gate release failed: " + ex.GetBaseException().Message); }
        }
    }
}
