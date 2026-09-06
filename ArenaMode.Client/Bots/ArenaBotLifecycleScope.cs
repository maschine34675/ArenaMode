using ArenaMode.Session;
using Comfort.Common;
using EFT;
using System;
using System.Runtime.CompilerServices;

namespace ArenaMode.Bots;

/// <summary>
/// Arena ownership survives death/GameEnd for the SAME managed owner/body.
/// Weak references cannot retain an ended world or authorize a later raid.
/// No profile-ID-only ownership: respawns reuse IDs, not body identities.
/// </summary>
internal static class ArenaBotLifecycleScope
{
    private sealed class Stamp
    {
        internal WeakReference World, Game, Player;
        internal int GrowthLogs, StopLogs;
    }

    private static ConditionalWeakTable<BotOwner, Stamp> _owners = new();

    internal static void Clear() => _owners = new ConditionalWeakTable<BotOwner, Stamp>();

    internal static bool Owns(BotOwner owner)
    {
        if (ReferenceEquals(owner, null)) return false;
        var world = Singleton<GameWorld>.Instance;
        var game = Singleton<AbstractGame>.Instance;
        var player = owner.GetPlayer;
        if (_owners.TryGetValue(owner, out var stamp))
        {
            // Native Dispose retains GetPlayer. Do not reject a dead/destroyed
            // Unity wrapper, but do reject a reused owner with a different body.
            return !ReferenceEquals(player, null) && ReferenceEquals(player, stamp.Player.Target)
                && (ReferenceEquals(world, null) || ReferenceEquals(world, stamp.World.Target))
                && (ReferenceEquals(game, null) || ReferenceEquals(game, stamp.Game.Target));
        }

        if (world == null || game == null || owner == null || player == null || !player.IsAI)
            return false;
        bool armed = ArenaSession.ArmedForNextRaid
            && !string.IsNullOrEmpty(ArenaSession.ArmedMap)
            && string.Equals(world.LocationId, ArenaSession.ArmedMap, StringComparison.OrdinalIgnoreCase);
        if (!armed && !ArenaSession.IsExactActiveRaid(game, world)) return false;
        if (!ReferenceEquals(world.GetEverExistedPlayerByID(player.ProfileId), player)) return false;
        _owners.Add(owner, new Stamp
        {
            World = new WeakReference(world), Game = new WeakReference(game), Player = new WeakReference(player)
        });
        return true;
    }

    internal static bool LogGrowth(BotOwner owner)
        => _owners.TryGetValue(owner, out var stamp) && ++stamp.GrowthLogs <= 2;

    internal static bool LogStop(BotOwner owner)
        => _owners.TryGetValue(owner, out var stamp) && ++stamp.StopLogs == 1;
}
