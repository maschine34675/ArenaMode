using ArenaMode.Areas;
using ArenaMode.Shared;
using EFT;
using EFT.Game.Spawning;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArenaMode.Pickups;

/// <summary>
/// Freezes read-only positions from this exact raid's native spawn data. These
/// are candidates, not placement authority: the live floor/path/clearance gate
/// still decides every initial appearance and rotation. Never creates spawns.
/// </summary>
internal static class ArenaPickupMapLayout
{
    internal const int MaximumCandidates = 16;

    internal static ArenaPickupLayoutFile Create(LocalGame game, string map, string area, string kit)
    {
        // Preserve the eight reviewed Factory positions byte-for-byte, including
        // day/night identity and their previous per-type initial candidate.
        var file = ArenaPickupLayouts.Factory(kit);
        if (ArenaPickupLayouts.Select(file, map, area) != null) return file;
        file.Layouts.Clear();
        if (!Supports(map, area)) return file;

        var contour = ArenaAreaRuntime.CurrentArea;
        if (area != null && (contour == null || contour.Id != area)) return file;
        var rows = game?.Location?.SpawnPointParams;
        if (rows == null || rows.Length > 4096) return file;
        var formation = ArenaTeamBoxCatalog.Find(map);
        var candidates = new List<ArenaPickupPosition>();
        foreach (var row in rows)
        {
            var p = row.Position;
            // Bot/BotPmc points avoid Labyrinth's trap-lined player entrances and
            // Customs player-only edge locations. The Yard's private additions
            // are legitimate seeds in the exact selected clone, never invented here.
            if (p == null || string.IsNullOrWhiteSpace(row.Id)
                || (row.Categories & (ESpawnCategoryMask.Bot | ESpawnCategoryMask.BotPmc)) == 0
                || !ArenaPickupLayouts.Finite(p.x) || !ArenaPickupLayouts.Finite(p.y) || !ArenaPickupLayouts.Finite(p.z)
                || Math.Abs(p.x) > 100000 || Math.Abs(p.y) > 100000 || Math.Abs(p.z) > 100000
                || ArenaBotSpawnPolicy.IsExcludedPoint(map, row.Id)
                || ArenaBotSpawnPolicy.IsExcludedZone(map, row.BotZoneName)
                || row.BotZoneName?.IndexOf("snipe", StringComparison.OrdinalIgnoreCase) >= 0
                || contour != null && (contour.IsExcludedSpawn(row.Id) || !contour.Bounds.Contains(p.x, p.y, p.z, .5f))
                || formation != null && (formation.Owns(row.Id) || NearBooth(formation, p.x, p.y, p.z))) continue;
            candidates.Add(new ArenaPickupPosition { X = p.x, Y = p.y, Z = p.z });
        }
        var pool = Spread(candidates);
        if (pool.Count == 0) return file;
        var layout = new ArenaPickupLayout { MapId = map, AreaId = area };
        var kinds = new[] { ArenaPickupKind.DoubleDamage, ArenaPickupKind.Shield, ArenaPickupKind.Weapon,
            ArenaPickupKind.JumpBoots, ArenaPickupKind.InfiniteAmmo, ArenaPickupKind.Health };
        for (int i = 0; i < kinds.Length; i++)
        {
            var p = pool[i % pool.Count];
            layout.Points.Add(new ArenaPickupPoint
            {
                Id = map + "-" + kinds[i].ToString().ToLowerInvariant(), Kind = kinds[i],
                X = p.X, Y = p.Y, Z = p.Z,
                KitId = kinds[i] == ArenaPickupKind.Weapon ? kit : null,
                RespawnSeconds = kinds[i] == ArenaPickupKind.Health ? 20f
                    : kinds[i] == ArenaPickupKind.Weapon || kinds[i] == ArenaPickupKind.InfiniteAmmo ? 45f : 30f,
                Alternatives = pool.Where(other => !ReferenceEquals(other, p))
                    .Select(other => new ArenaPickupPosition { X = other.X, Y = other.Y, Z = other.Z }).ToList()
            });
        }
        file.Layouts.Add(layout);
        ArenaPickupLayouts.Validate(file);
        return file;
    }

    private static bool Supports(string map, string area)
    {
        if (string.Equals(map, "bigmap", StringComparison.OrdinalIgnoreCase))
            return area != null && ArenaAreaRuntime.CurrentArea?.Id == area
                && ArenaAreaCatalog.TryResolve(area, map, "deathmatch", false, out var selected, out _) && selected != null;
        return area == null && (string.Equals(map, "laboratory", StringComparison.OrdinalIgnoreCase)
            || string.Equals(map, "labyrinth", StringComparison.OrdinalIgnoreCase)
            || string.Equals(map, "arena_equator", StringComparison.OrdinalIgnoreCase)
            || string.Equals(map, "arena_bay5", StringComparison.OrdinalIgnoreCase)
            || string.Equals(map, "arena_airpit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(map, "arena_bowl", StringComparison.OrdinalIgnoreCase));
    }

    private static bool NearBooth(ArenaTeamBoxCatalog.Formation formation, float x, float y, float z)
    {
        // A candidate's final built-in repair stays within 2m. This slightly
        // larger margin keeps those repairs out of either original team box.
        foreach (float dx in new[] { -2.5f, 0f, 2.5f })
        foreach (float dz in new[] { -2.5f, 0f, 2.5f })
            if (formation.InsideBooth(x + dx, y, z + dz, true)
                || formation.InsideBooth(x + dx, y, z + dz, false)) return true;
        return false;
    }

    internal static List<ArenaPickupPosition> Spread(IEnumerable<ArenaPickupPosition> candidates)
    {
        var remaining = candidates.GroupBy(p => (p.X, p.Y, p.Z)).Select(g => g.First())
            .OrderBy(p => p.X).ThenBy(p => p.Z).ThenBy(p => p.Y).ToList();
        var selected = new List<ArenaPickupPosition>();
        if (remaining.Count == 0) return selected;
        // A central seed followed by farthest-point sampling avoids a pool of
        // adjacent records from one spawn cluster. Stable ordering makes the
        // frozen input independent of backend row order; rounds still shuffle.
        float centerX = remaining.Average(p => p.X), centerZ = remaining.Average(p => p.Z);
        selected.Add(remaining.OrderBy(p => Square(p.X - centerX) + Square(p.Z - centerZ)).First());
        remaining.Remove(selected[0]);
        while (remaining.Count > 0 && selected.Count < MaximumCandidates)
        {
            var next = remaining.OrderByDescending(p => selected.Min(q => HorizontalSquared(p, q))).First();
            if (selected.Any(p => HorizontalSquared(p, next) < 9f)) break;
            selected.Add(next); remaining.Remove(next);
        }
        return selected;
    }

    private static float Square(float value) => value * value;
    private static float HorizontalSquared(ArenaPickupPosition a, ArenaPickupPosition b)
        => Square(a.X - b.X) + Square(a.Z - b.Z);
}
