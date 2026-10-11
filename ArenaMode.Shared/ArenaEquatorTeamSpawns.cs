using System;
using System.Collections.Generic;

namespace ArenaMode.Shared;

/// <summary>
/// Original Equator TeamFight formations, owned by independent Arena TDM only.
/// Source: Arena level57 SPAWNS, verified against accepted v5 anchor samples on
/// 2026-10-11. New versioned ids never overwrite MapPort's common spawn records.
/// Coordinates are candidates until the client proves floor, clearance and paths.
/// </summary>
public static class ArenaEquatorTeamSpawns
{
    public const string MapId = "arena_equator";
    public const string ModeId = "teamdeathmatch";
    public const string IdPrefix = "arena-equator-tdm-v1-";
    public const string Zone = "BotZone_Main";
    public const string Infiltration = "ArenaEquator";
    public const string DonorId = "arena_equator-bot-atrium";
    public const int CorePointId = 0;
    public const int CapacityPerTeam = 10;
    public const int MaximumParticipants = 20;
    public const float ColliderRadius = 0.8f;

    public sealed class Point
    {
        public readonly string Id;
        public readonly bool Usec;
        public readonly float X, Y, Z, Yaw;
        internal Point(string id, bool usec, float x, float y, float z, float yaw)
        { Id = id; Usec = usec; X = x; Y = y; Z = z; Yaw = yaw; }
    }

    public static readonly IReadOnlyList<Point> Points = Array.AsReadOnly(new[]
    {
        new Point("arena-equator-tdm-v1-north-01", true, 6.519996f, 0.080000f, 48.588985f, 179.999981f),
        new Point("arena-equator-tdm-v1-north-02", true, 3.089999f, 0.080000f, 48.588985f, 179.999981f),
        new Point("arena-equator-tdm-v1-north-03", true, 0.299996f, 0.080000f, 48.588993f, 179.999981f),
        new Point("arena-equator-tdm-v1-north-04", true, -2.930004f, 0.080000f, 48.588997f, 179.999981f),
        new Point("arena-equator-tdm-v1-north-05", true, -6.230000f, 0.080000f, 48.589001f, 179.999981f),
        new Point("arena-equator-tdm-v1-north-06", true, 4.552997f, 0.080000f, 45.439999f, 179.999981f),
        new Point("arena-equator-tdm-v1-north-07", true, 1.123000f, 0.080000f, 45.439999f, 179.999981f),
        new Point("arena-equator-tdm-v1-north-08", true, -1.667003f, 0.080000f, 45.440006f, 179.999981f),
        new Point("arena-equator-tdm-v1-north-09", true, -4.897004f, 0.080000f, 45.440010f, 179.999981f),
        new Point("arena-equator-tdm-v1-north-10", true, -7.337000f, 0.080000f, 44.488998f, 133.131439f),
        new Point("arena-equator-tdm-v1-south-01", false, -6.240000f, 0.080000f, -41.990002f, -0.000002f),
        new Point("arena-equator-tdm-v1-south-02", false, -2.810000f, 0.080000f, -41.990002f, -0.000002f),
        new Point("arena-equator-tdm-v1-south-03", false, -0.020000f, 0.080000f, -41.990002f, -0.000002f),
        new Point("arena-equator-tdm-v1-south-04", false, 3.210000f, 0.080000f, -41.990002f, -0.000002f),
        new Point("arena-equator-tdm-v1-south-05", false, 6.510000f, 0.080000f, -41.990002f, -0.000002f),
        new Point("arena-equator-tdm-v1-south-06", false, -7.215000f, 0.080000f, -38.368000f, 37.923440f),
        new Point("arena-equator-tdm-v1-south-07", false, -4.346000f, 0.080000f, -39.087002f, -0.000002f),
        new Point("arena-equator-tdm-v1-south-08", false, -1.556000f, 0.080000f, -39.087002f, -0.000002f),
        new Point("arena-equator-tdm-v1-south-09", false, 1.674000f, 0.080000f, -39.087002f, -0.000002f),
        new Point("arena-equator-tdm-v1-south-10", false, 4.974000f, 0.080000f, -39.087002f, -0.000002f),
    });

    public static bool Applies(string map, string mode)
        => string.Equals(map, MapId, StringComparison.OrdinalIgnoreCase)
           && string.Equals(mode, ModeId, StringComparison.OrdinalIgnoreCase);

    public static bool Owns(string id)
        => id != null && id.StartsWith(IdPrefix, StringComparison.Ordinal);

    public static Point Find(string id)
    {
        foreach (var point in Points) if (point.Id == id) return point;
        return null;
    }
}
