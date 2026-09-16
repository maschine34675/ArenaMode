using EFT.Game.Spawning;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Pure point planning for direct Arena bot injections. A point in the preferred
/// distance band wins, but a non-empty preferred set must not hide valid points
/// farther away: doing so reduced a requested five-bot Labs squad to three.
/// </summary>
internal static class BurstSpawnPointSelector
{
    internal const float MinSpawnDistance = 25f;
    internal const float MaxSpawnDistance = 70f;

    /// <summary>
    /// Keeps round-robin zone selection, but skips a zone that cannot hold the
    /// complete requested batch when a later allowed zone can. If no zone is
    /// large enough, the largest available one is returned and the caller can
    /// fail closed on the resulting short batch.
    /// </summary>
    internal static BotZone PickZone(
        IEnumerable<BotZone> zones,
        Vector3? playerPosition,
        int requiredCount,
        ref int cursor)
    {
        var available = zones?.Where(zone => zone != null).ToList();
        if (available == null || available.Count == 0)
            return null;

        var start = PositiveModulo(cursor + 1, available.Count);
        var bestIndex = start;
        var bestCapacity = -1;

        for (var offset = 0; offset < available.Count; offset++)
        {
            var index = (start + offset) % available.Count;
            var capacity = CountUsable(available[index], playerPosition);
            if (capacity >= Math.Max(1, requiredCount))
            {
                cursor = index;
                return available[index];
            }

            if (capacity > bestCapacity)
            {
                bestCapacity = capacity;
                bestIndex = index;
            }
        }

        cursor = bestIndex;
        return available[bestIndex];
    }

    /// <summary>
    /// Returns distinct points in priority order: 25-70 m first, then points
    /// beyond 70 m. Points closer than 25 m, blocked and Arena-excluded points
    /// remain excluded before planning a full squad, not after dispatch starts.
    ///
    /// <paramref name="grouped"/> asks for one cluster instead of that spread.
    /// Round-robin over a whole zone is right for the ambient deathmatch drip,
    /// where bots should arrive from everywhere, and wrong for a Tactical team:
    /// five opponents scattered across the map are five duels, not a 5v5. A
    /// grouped request keeps the same exclusions and the same 25 m floor.
    /// </summary>
    internal static List<ISpawnPoint> PickPoints(
        BotZone zone,
        Vector3? playerPosition,
        int count,
        ref int cursor,
        out int preferredAvailable,
        out int distantAvailable,
        bool grouped = false)
    {
        preferredAvailable = 0;
        distantAvailable = 0;

        var all = zone?.SpawnPoints;
        if (all == null || all.Length == 0 || count <= 0)
            return null;

        var preferred = new List<ISpawnPoint>(all.Length);
        var distant = new List<ISpawnPoint>(all.Length);
        var seenPositions = new HashSet<PositionKey>();

        foreach (var point in all)
        {
            if (point == null || point.SpawnBlocked || ArenaBotSpawnFilter.IsExcludedPoint(point))
                continue;

            if (!seenPositions.Add(new PositionKey(point.Position)))
                continue;

            if (!playerPosition.HasValue)
            {
                preferred.Add(point);
                continue;
            }

            var distance = Vector3.Distance(point.Position, playerPosition.Value);
            if (distance < MinSpawnDistance)
                continue;

            if (distance <= MaxSpawnDistance)
                preferred.Add(point);
            else
                distant.Add(point);
        }

        preferredAvailable = preferred.Count;
        distantAvailable = distant.Count;
        if (preferred.Count == 0 && distant.Count == 0)
            return null;

        if (grouped && playerPosition.HasValue)
            return PickCluster(preferred, distant, playerPosition.Value, count, ref cursor);

        var result = new List<ISpawnPoint>(count);
        AppendDistinct(result, preferred, count, ref cursor);
        AppendDistinct(result, distant, count, ref cursor);
        return result;
    }

    /// <summary>
    /// One anchor as far from the player as the zone allows, plus its nearest
    /// neighbours. Both bands are pooled first: the 25-70 m preference exists to
    /// keep an ambient spawn from arriving on top of the player, and for a team
    /// that starts a round together the far band is the better half of the map.
    ///
    /// The anchor rotates with the shared cursor over the farthest third, so a
    /// long match does not replay the identical opening every round. Distance is
    /// straight-line: it orders candidates, it does not prove a path, and the
    /// caller still fails closed when the zone cannot supply the exact count.
    /// </summary>
    private static List<ISpawnPoint> PickCluster(
        List<ISpawnPoint> preferred,
        List<ISpawnPoint> distant,
        Vector3 playerPosition,
        int count,
        ref int cursor)
    {
        var usable = new List<ISpawnPoint>(preferred.Count + distant.Count);
        usable.AddRange(preferred);
        usable.AddRange(distant);
        usable.Sort((left, right) => Vector3.Distance(right.Position, playerPosition)
            .CompareTo(Vector3.Distance(left.Position, playerPosition)));

        var anchorPool = Math.Max(1, usable.Count / 3);
        cursor = PositiveModulo(cursor + 1, anchorPool);
        var anchor = usable[cursor].Position;

        usable.Sort((left, right) => Vector3.Distance(left.Position, anchor)
            .CompareTo(Vector3.Distance(right.Position, anchor)));

        var take = Math.Min(count, usable.Count);
        return usable.GetRange(0, take);
    }

    internal static int CountUsable(BotZone zone, Vector3? playerPosition)
    {
        var all = zone?.SpawnPoints;
        if (all == null)
            return 0;

        var count = 0;
        var seenPositions = new HashSet<PositionKey>();
        foreach (var point in all)
        {
            if (point == null || point.SpawnBlocked || ArenaBotSpawnFilter.IsExcludedPoint(point))
                continue;

            if (!playerPosition.HasValue
                || Vector3.Distance(point.Position, playerPosition.Value) >= MinSpawnDistance)
            {
                if (seenPositions.Add(new PositionKey(point.Position)))
                    count++;
            }
        }

        return count;
    }

    private static void AppendDistinct(
        List<ISpawnPoint> result,
        List<ISpawnPoint> source,
        int wanted,
        ref int cursor)
    {
        var take = Math.Min(wanted - result.Count, source.Count);
        for (var i = 0; i < take; i++)
        {
            cursor = PositiveModulo(cursor + 1, source.Count);
            result.Add(source[cursor]);
        }
    }

    private static int PositiveModulo(int value, int modulus)
    {
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    /// <summary>
    /// Scene/database marker IDs are not a physical uniqueness guarantee. Labs
    /// contains two distinct marker records at one exact coordinate, so key by
    /// position before both capacity planning and selection to prevent stacking.
    /// </summary>
    private readonly struct PositionKey : IEquatable<PositionKey>
    {
        private readonly float _x;
        private readonly float _y;
        private readonly float _z;

        internal PositionKey(Vector3 position)
        {
            _x = position.x;
            _y = position.y;
            _z = position.z;
        }

        public bool Equals(PositionKey other)
            => _x.Equals(other._x) && _y.Equals(other._y) && _z.Equals(other._z);

        public override bool Equals(object obj)
            => obj is PositionKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = _x.GetHashCode();
                hash = (hash * 397) ^ _y.GetHashCode();
                return (hash * 397) ^ _z.GetHashCode();
            }
        }
    }
}
