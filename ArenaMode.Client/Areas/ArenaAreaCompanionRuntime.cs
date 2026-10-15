using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Game.Spawning;
using ArenaMode.Bots;

namespace ArenaMode.Areas;

internal static partial class ArenaAreaRuntime
{
    /// <summary>
    /// PFT owns authentication and bot creation; the enclosure owns placement.
    /// Never use PFT's default shared leader position or a radius sample that
    /// could cross the surveyed contour. Four sequential reservations remain
    /// distinct even while none of their asynchronous bodies exists yet.
    /// </summary>
    internal static bool TryReserveCompanionSpawn(out BotZone zone, out ISpawnPoint point,
        out TeamSpawnLease lease)
    {
        zone = null; point = null; lease = null;
        if (!IsReady || !Compat.PitFireTeamCompat.HasFrozenTeamRoster
            || (_mode != Modes.ArenaModeCatalog.TacticalId && _mode != Modes.ArenaModeCatalog.ZombieId)
            || _world.MainPlayer == null) return false;
        foreach (var expired in TeamReservations.Values.Where(p => !p.IsCurrent).ToArray()) expired.Dispose();
        var pairs = new List<(BotZone zone, ISpawnPoint point)>();
        foreach (var candidateZone in (_game as IBotGame)?.BotsController?.BotSpawner?.SpawnZones(false)
            ?? Enumerable.Empty<BotZone>())
        {
            if (candidateZone == null || ArenaBotSpawnFilter.IsExcludedZone(candidateZone)) continue;
            foreach (var candidate in candidateZone.SpawnPoints ?? Array.Empty<ISpawnPoint>())
                if (candidate != null && IsBotPoint(candidate) && !candidate.SpawnBlocked
                    && AllowsSpawn(candidate) && !ArenaBotSpawnFilter.IsExcludedPoint(candidate)
                    && IsUnoccupied(candidate.Position, null, null)) pairs.Add((candidateZone, candidate));
        }
        var nearest = pairs.OrderBy(p => (p.point.Position - _world.MainPlayer.Position).sqrMagnitude).FirstOrDefault();
        if (nearest.point == null) return false;
        zone = nearest.zone; point = nearest.point;
        lease = new TeamSpawnLease(_token, point, _game.Profile.Info.Side == EPlayerSide.Usec, requireFaction: false);
        TeamReservations.Add(point.Id, lease);
        Plugin.ModLogger.LogDebug($"Arena area companion reservation: {point.Id}, {point.Position}; area={_area.Id}.");
        return true;
    }

}
