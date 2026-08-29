using EFT;
using EFT.Game.Spawning;
using Redeploy.Core.Spawning;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Respawn;

/// <summary>
/// Picks where the player comes back, on purpose rather than by calculation.
///
/// Asking the engine for a spawn point looks right and is not: its selection
/// ends in "the point farthest from everyone else", which is a COMPUTATION, not
/// a draw. Give it the same map and roughly the same bots and it returns
/// roughly the same corner — which is why a map with eighty-five player spawn
/// points was handing out about five of them, sometimes the same one ten times
/// running. Nothing was broken; it was answering a different question than the
/// one an arena wants to ask.
///
/// So this asks the arena's question instead: somewhere random, and not on top
/// of the fight you just lost. Recently used points are skipped while anything
/// else is available, because true randomness deals repeats often enough to
/// feel broken on a map with a handful of usable spots.
/// </summary>
internal sealed class ArenaSpawnSelector : ISpawnSelector
{
    /// <summary>Enough to stop a repeat being noticed, few enough that a small map still works.</summary>
    private const int RememberLast = 4;

    private static readonly List<string> Recent = new List<string>();

    private readonly ISpawnSelector _fallback;
    private readonly Func<Vector3> _avoid;
    private readonly float _minDistance;

    public ArenaSpawnSelector(ISpawnSelector fallback, Func<Vector3> avoid, float minDistance)
    {
        _fallback = fallback;
        _avoid = avoid;
        _minDistance = Mathf.Max(0f, minDistance);
    }

    public static void Reset() => Recent.Clear();

    public (Vector3 position, Quaternion rotation) Resolve(LocalGame game)
    {
        try
        {
            var picked = Pick(game);
            if (picked != null)
            {
                Remember(picked.Id);
                return (picked.Position, picked.Rotation);
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena spawn point selection failed ({ex.Message}) — using the engine's choice.");
        }

        return _fallback.Resolve(game);
    }

    private ISpawnPoint Pick(LocalGame game)
    {
        // The concrete system, because the spawn point list is not on the
        // interface. A cast that fails just means the fallback runs.
        if (!(SpawnSystemCache.GetOrCreate(game) is SpawnSystem system))
            return null;

        var all = system._spawnPoints;
        if (all == null)
            return null;

        Vector3? avoid = null;
        if (_avoid != null && _minDistance > 0f)
            avoid = _avoid();

        var minSqr = _minDistance * _minDistance;
        var candidates = new List<ISpawnPoint>();
        var nearby = new List<ISpawnPoint>();

        foreach (var point in all)
        {
            if (point == null || point.SpawnBlocked)
                continue;

            if ((point.Categories & ESpawnCategoryMask.Player) == 0)
                continue;

            // Kept separately rather than dropped: on a small map every point
            // can be near the fight, and coming back close beats not at all.
            if (avoid.HasValue && (point.Position - avoid.Value).sqrMagnitude < minSqr)
                nearby.Add(point);
            else
                candidates.Add(point);
        }

        return Draw(candidates) ?? Draw(nearby);
    }

    /// <summary>Random, but skipping what was just used while there is a choice.</summary>
    private static ISpawnPoint Draw(List<ISpawnPoint> pool)
    {
        if (pool.Count == 0)
            return null;

        var fresh = pool.FindAll(point => !Recent.Contains(point.Id));
        var draw = fresh.Count > 0 ? fresh : pool;
        return draw[UnityEngine.Random.Range(0, draw.Count)];
    }

    private static void Remember(string id)
    {
        if (string.IsNullOrEmpty(id))
            return;

        Recent.Remove(id);
        Recent.Add(id);
        while (Recent.Count > RememberLast)
            Recent.RemoveAt(0);
    }
}
