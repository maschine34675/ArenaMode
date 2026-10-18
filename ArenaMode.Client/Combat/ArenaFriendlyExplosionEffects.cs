using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace ArenaMode.Combat;

/// <summary>
/// Native blast contusion/flash are outside Player damage methods. Filter only
/// arguments of synchronous calls owned by one attributed native explosion;
/// never edit EFT's shared buffers or grant general flash immunity.
/// </summary>
internal static class ArenaFriendlyExplosionEffects
{
    [ThreadStatic] private static Scope _current;

    internal sealed class Scope : IDisposable
    {
        private readonly Scope _previous;
        internal readonly IPlayer Source;
        internal readonly object Match;
        internal readonly GameWorld World;
        internal readonly AbstractGame Game;
        private bool _disposed;
        internal Scope(IPlayer source)
        {
            _previous = _current;
            Source = source;
            Match = ArenaMatchService.Current;
            World = Singleton<GameWorld>.Instance;
            Game = Singleton<AbstractGame>.Instance;
            _current = this;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (ReferenceEquals(_current, this)) _current = _previous;
        }
    }

    internal static void Enable()
    {
        new ArenaFriendlyExplosionEffectsExplosionContextPatch().Enable();
        new ArenaFriendlyExplosionEffectsBlastTargetsPatch().Enable();
        new ArenaFriendlyExplosionEffectsFlashTargetsPatch().Enable();
    }

    internal static Scope Begin(string throwerId, bool runOriginal)
    {
        bool eligible = runOriginal && ArenaSession.IsActive && !ArenaSession.FriendlyFireEnabledSelection;
        if (_current == null && !eligible) return null;
        IPlayer source = null;
        if (eligible
            && !string.IsNullOrEmpty(throwerId) && Singleton<GameWorld>.Instantiated)
        {
            // Match native Shot attribution. The ID resolves only a candidate;
            // the damage policy still requires its exact admitted physical body.
            try { source = Singleton<GameWorld>.Instance.GetEverExistedBridgeByProfileID(throwerId)?.iPlayer; }
            catch (Exception) { }
        }
        // Even unattributed/nested/suppressed calls shadow the outer source.
        return new Scope(source);
    }

    private static bool Blocks(IObserverToPlayerBridge target)
    {
        var scope = _current;
        if (scope == null || ReferenceEquals(scope.Source, null)
            || !ReferenceEquals(scope.Match, ArenaMatchService.Current)
            || !ReferenceEquals(scope.World, Singleton<GameWorld>.Instance)
            || !ReferenceEquals(scope.Game, Singleton<AbstractGame>.Instance)) return false;
        try { return ArenaFriendlyFire.ShouldBlockFriendlyDamage(target?.iPlayer as Player, scope.Source, reportDamage: false); }
        catch (Exception) { return false; }
    }

    internal static IReadOnlyDictionary<IObserverToPlayerBridge, PlayerEmitterCache> FilterBlast(
        IReadOnlyDictionary<IObserverToPlayerBridge, PlayerEmitterCache> original)
    {
        if (original == null || _current == null
            || !ReferenceEquals(original, ExplosionSharedMethods._playerBuffer)) return original;
        Dictionary<IObserverToPlayerBridge, PlayerEmitterCache> copy = null;
        foreach (var pair in original)
            if (Blocks(pair.Key))
            {
                copy ??= new Dictionary<IObserverToPlayerBridge, PlayerEmitterCache>();
                break;
            }
        if (copy == null) return original;
        foreach (var pair in original) if (!Blocks(pair.Key)) copy.Add(pair.Key, pair.Value);
        return copy;
    }

    internal static void FilterFlash(ref List<IObserverToPlayerBridge> players,
        ref Dictionary<IObserverToPlayerBridge, PlayerEmitterCache> caches)
    {
        // Other native callers include flash ammunition and flares. Even if a
        // mod re-enters those from an explosion event, they must not inherit its
        // thrower; only Explosion's actual cache argument is attributable here.
        if (players == null || caches == null || _current == null
            || !ReferenceEquals(caches, ExplosionSharedMethods._playerBuffer)) return;
        List<IObserverToPlayerBridge> filtered = null;
        for (int i = 0; i < players.Count; i++)
        {
            if (Blocks(players[i])) { filtered ??= players.GetRange(0, i); }
            else filtered?.Add(players[i]);
        }
        if (filtered == null) return;
        var copy = new Dictionary<IObserverToPlayerBridge, PlayerEmitterCache>(caches);
        foreach (var player in players) if (Blocks(player)) copy.Remove(player);
        players = filtered;
        caches = copy;
    }

    private sealed class ArenaFriendlyExplosionEffectsExplosionContextPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(ExplosionSharedMethods), nameof(ExplosionSharedMethods.Explosion));
        [PatchPrefix, HarmonyPriority(Priority.Last)]
        private static void Prefix(string playerProfileIDWhoThrew, bool __runOriginal, out Scope __state)
            => __state = Begin(playerProfileIDWhoThrew, __runOriginal);
        [PatchFinalizer]
        private static Exception Finalizer(Exception __exception, Scope __state)
        {
            __state?.Dispose();
            return __exception;
        }
    }

    private sealed class ArenaFriendlyExplosionEffectsBlastTargetsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(ExplosionSharedMethods), nameof(ExplosionSharedMethods.AffectPlayersShared));
        [PatchPrefix, HarmonyPriority(Priority.Last)]
        private static void Prefix(bool __runOriginal,
            ref IReadOnlyDictionary<IObserverToPlayerBridge, PlayerEmitterCache> playerAndDistances)
        {
            if (__runOriginal) playerAndDistances = FilterBlast(playerAndDistances);
        }
    }

    private sealed class ArenaFriendlyExplosionEffectsFlashTargetsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(FirearmControllerSharedMethods), nameof(FirearmControllerSharedMethods.ApplyLightAndSoundHealthEffects));
        [PatchPrefix, HarmonyPriority(Priority.Last)]
        private static void Prefix(bool __runOriginal, ref List<IObserverToPlayerBridge> players,
            ref Dictionary<IObserverToPlayerBridge, PlayerEmitterCache> playerEmitterCaches)
        {
            if (__runOriginal) FilterFlash(ref players, ref playerEmitterCaches);
        }
    }
}
