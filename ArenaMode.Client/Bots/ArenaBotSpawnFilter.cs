using ArenaMode.Session;
using ArenaMode.Shared;
using Comfort.Common;
using EFT;
using EFT.Game.Spawning;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArenaMode.Bots;

/// <summary>Bot-only views of spawn choices; never edit the scene or player points.</summary>
internal static class ArenaBotSpawnFilter
{
    private static bool IsEnabled
        => (ArenaSession.IsActive || ArenaSession.ArmedForNextRaid)
           && (ArenaBotSpawnPolicy.IsRestrictedMap(ArenaSession.ArmedMap)
               || ArenaBotSpawnPolicy.HasPointExclusions(ArenaSession.ArmedMap));

    // Unlike a Labs zone name, the Factory ID can also be a player's spawn.
    // Do not let stale state from a previous match restrict a new GameWorld.
    internal static bool IsPointRestrictedRaid
        => (ArenaSession.IsActive || ArenaSession.ArmedForNextRaid)
           && ArenaBotSpawnPolicy.HasPointExclusions(ArenaSession.ArmedMap)
           && Singleton<GameWorld>.Instantiated
           && string.Equals(Singleton<GameWorld>.Instance.LocationId, ArenaSession.ArmedMap,
               StringComparison.OrdinalIgnoreCase)
           && (ArenaSession.ArmedForNextRaid
               || (Singleton<AbstractGame>.Instantiated
                   && ArenaSession.IsExactActiveRaid(Singleton<AbstractGame>.Instance, Singleton<GameWorld>.Instance)));

    public static bool IsExcludedZone(BotZone zone)
        => IsEnabled
           && ArenaBotSpawnPolicy.IsExcludedZone(ArenaSession.ArmedMap, zone?.NameZone);

    public static bool IsExcludedPoint(ISpawnPoint point)
        => IsEnabled
           && (ArenaBotSpawnPolicy.IsExcludedZone(ArenaSession.ArmedMap, point?.BotZoneName)
               || (IsPointRestrictedRaid && ArenaBotSpawnPolicy.IsExcludedPoint(ArenaSession.ArmedMap, point?.Id)));

    public static IEnumerable<BotZone> FilterZones(IEnumerable<BotZone> zones)
    {
        if (zones == null || !IsEnabled || !ArenaBotSpawnPolicy.HasZoneExclusions(ArenaSession.ArmedMap))
            return zones;

        // Materialize our view without changing BotSpawner's zone/patrol arrays.
        return zones.Where(zone => !IsExcludedZone(zone)).ToArray();
    }

    public static List<ISpawnPoint> FilterBotPoints(List<ISpawnPoint> points)
    {
        if (points == null || !IsEnabled || !points.Any(IsExcludedPoint))
            return points;

        // The public AI selector returns before its interface wrapper applies
        // spawn cooldowns. Reject the whole choice so profile/point counts stay
        // paired, and never edit the caller's list or any player spawn marker.
        return null;
    }

    public static bool CanDispatch(BotZone zone, List<ISpawnPoint> points)
        => !IsEnabled || (!IsExcludedZone(zone) && (points == null || !points.Any(IsExcludedPoint)));
}
