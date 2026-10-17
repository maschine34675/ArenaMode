#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArenaMode.Shared;

/// <summary>Original TeamFight formations; all coordinates still require live geometry checks.</summary>
public static class ArenaTeamBoxCatalog
{
    public const string ModeId = "teamdeathmatch";
    public sealed class Point
    {
        public readonly string Id;
        public readonly bool Usec;
        public readonly float X, Y, Z, Yaw;
        // EFT compares backend yaw with Quaternion.eulerAngles.y as a scalar,
        // not an angle. Preserve the authored yaw above, but serialize its
        // Euler equivalent using Unity's Internal_MakePositive tolerance.
        // Tiny negative source values must stay negative near zero.
        public float NativeYaw
        {
            get
            {
                float yaw = Yaw % 360f;
                if (yaw < -.005729578f) return yaw + 360f;
                return yaw > 359.99427f ? yaw - 360f : yaw;
            }
        }
        public Point(string id, bool usec, float x, float y, float z, float yaw = 0f)
        { Id = id; Usec = usec; X = x; Y = y; Z = z; Yaw = yaw; }
    }

    public sealed class Bounds
    {
        private readonly float _minX, _maxX, _minY, _maxY, _minZ, _maxZ;
        public Bounds(float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
        { _minX = minX; _maxX = maxX; _minY = minY; _maxY = maxY; _minZ = minZ; _maxZ = maxZ; }
        public bool Contains(float x, float y, float z)
            => x >= _minX && x <= _maxX && y >= _minY && y <= _maxY && z >= _minZ && z <= _maxZ;
    }

    public sealed class Formation
    {
        public readonly string MapId, DisplayName, IdPrefix, Infiltration, TeamDescription;
        public readonly Point Donor, Destination;
        public readonly IReadOnlyList<Point> Points;
        public readonly IReadOnlyList<string> BoothExits;
        private readonly Bounds _usecBounds, _bearBounds;
        private readonly int _usecAnchor, _bearAnchor;
        public string Zone => "BotZone_Main";
        public int CorePointId => 0;
        public int CapacityPerTeam => 10;
        public int MaximumParticipants => CapacityPerTeam * 2;
        public float ColliderRadius => .8f;
        internal Formation(string map, string display, string prefix, string infiltration, string teams,
            Point donor, Point destination, Point[] points, Bounds usecBounds, Bounds bearBounds,
            int usecAnchor, int bearAnchor, params string[] boothExits)
        {
            MapId = map; DisplayName = display; IdPrefix = prefix; Infiltration = infiltration; TeamDescription = teams;
            Donor = donor; Destination = destination; Points = Array.AsReadOnly(points);
            _usecBounds = usecBounds; _bearBounds = bearBounds; _usecAnchor = usecAnchor; _bearAnchor = bearAnchor;
            BoothExits = Array.AsReadOnly(boothExits);
        }
        public bool Owns(string? id) => id != null && id.StartsWith(IdPrefix, StringComparison.Ordinal);
        public Point? Find(string? id) => Points.FirstOrDefault(p => p.Id == id);
        public Point BoothAnchor(bool usec) => Points[usec ? _usecAnchor : _bearAnchor];
        public bool InsideBooth(float x, float y, float z, bool usec)
            => (usec ? _usecBounds : _bearBounds).Contains(x, y, z);
    }

    // Keep Equator's accepted source table unchanged. The catalogue owns its
    // own immutable copies; map selection never mutates shared coordinates.
    public static readonly Formation Equator = new Formation("arena_equator", "Equator",
        ArenaEquatorTeamSpawns.IdPrefix, "ArenaEquator", "USEC north / BEAR south",
        new Point(ArenaEquatorTeamSpawns.DonorId, false, 0f, .2f, 2f), new Point("", false, 0f, .2f, 2f),
        ArenaEquatorTeamSpawns.Points.Select(p => new Point(p.Id, p.Usec, p.X, p.Y, p.Z, p.Yaw)).ToArray(),
        new Bounds(-9.8f, 9.8f, -.22f, .38f, 40.5f, 51f), new Bounds(-9.8f, 9.8f, -.22f, .38f, -43f, -34f),
        2, 12, "Equator North Gate", "Equator South Gate");

    // Arena level88 SPAWNS/Usec house (0..9), Bear house (0..9). In particular,
    // the depot has two authored elevations: never flatten these start points.
    public static readonly Formation Bay5 = new Formation("arena_bay5", "Bay5", "arena-bay5-tdm-v1-",
        "ArenaBay5", "USEC warehouse / BEAR depot",
        new Point("arena_bay5-bot-west-centre", false, 63.497f, -3.8574f, 42.2217f),
        new Point("", false, 89.159f, -3.693f, 35.203f), new[]
        {
            new Point("arena-bay5-tdm-v1-warehouse-01", true, 143.009003f, -2.892f, 51.568001f, -90.000355f),
            new Point("arena-bay5-tdm-v1-warehouse-02", true, 139.727005f, -2.892f, 55.222000f, -90.000355f),
            new Point("arena-bay5-tdm-v1-warehouse-03", true, 143.031006f, -2.892f, 54.198002f, -90.000355f),
            new Point("arena-bay5-tdm-v1-warehouse-04", true, 143.125000f, -2.892f, 49.327000f, -90.000355f),
            new Point("arena-bay5-tdm-v1-warehouse-05", true, 142.923004f, -2.892f, 46.603001f, -90.000355f),
            new Point("arena-bay5-tdm-v1-warehouse-06", true, 142.856003f, -2.892f, 43.527000f, -90.000355f),
            new Point("arena-bay5-tdm-v1-warehouse-07", true, 139.746994f, -2.892f, 57.638000f, -90.000355f),
            new Point("arena-bay5-tdm-v1-warehouse-08", true, 139.667999f, -2.892f, 52.910999f, -90.000355f),
            new Point("arena-bay5-tdm-v1-warehouse-09", true, 139.684998f, -2.892f, 50.188000f, -90.000355f),
            new Point("arena-bay5-tdm-v1-warehouse-10", true, 139.826004f, -2.892f, 47.021999f, -90.000355f),
            new Point("arena-bay5-tdm-v1-depot-01", false, 53.588001f, -2.920f, -11.539000f, -.000273f),
            new Point("arena-bay5-tdm-v1-depot-02", false, 51.981998f, -2.920f, -11.319000f, -.000273f),
            new Point("arena-bay5-tdm-v1-depot-03", false, 55.909000f, -2.920f, -11.646000f, -.000273f),
            new Point("arena-bay5-tdm-v1-depot-04", false, 49.633999f, -2.920f, -10.908000f, 35.362692f),
            new Point("arena-bay5-tdm-v1-depot-05", false, 48.335999f, -2.920f, -8.990000f, 89.999631f),
            new Point("arena-bay5-tdm-v1-depot-06", false, 61.165001f, -3.816f, -12.189000f, 57.056374f),
            new Point("arena-bay5-tdm-v1-depot-07", false, 49.111000f, -3.815f, -5.086000f, 3.326737f),
            new Point("arena-bay5-tdm-v1-depot-08", false, 59.973000f, -3.816f, -8.822000f, -.000273f),
            new Point("arena-bay5-tdm-v1-depot-09", false, 55.268002f, -3.815f, -6.678000f, 15.816699f),
            new Point("arena-bay5-tdm-v1-depot-10", false, 52.182999f, -3.815f, -6.296000f, -.000377f),
        },
        // Native SPAWN_BOUND_FIGHTER interiors. Only canonical starts gain the
        // held-gate exception, and every real path corner must remain inside.
        new Bounds(129.01f, 149.59f, -3.24f, 1.04f, 39.78f, 69.46f),
        new Bounds(45.87f, 66.45f, -4.08f, .20f, -14.46f, 1.53f), 8, 19);

    // Original AirPit TeamFight SPAWNS: north 6..10,16..20; south 1..5,11..15.
    // Requires MapPort 0.2.2's extended north navigation and passport held gates.
    // Live geometry remains authoritative; older/changed bundles fail closed.
    public static readonly Formation AirPit = new Formation("arena_airpit", "AirPit", "arena-airpit-tdm-v1-",
        "ArenaAirPit", "USEC north / BEAR south",
        new Point("arena_airpit-bot-center-east", false, -3.21f, .1f, -12.09f),
        new Point("", false, -15.51f, .1f, -37.64f), new[]
        {
            new Point("arena-airpit-tdm-v1-north-01", true, -12.590000153f, .101999998f, 79.209999084f, -179.851960318f),
            new Point("arena-airpit-tdm-v1-north-02", true, -15.108654022f, .101999998f, 76.089179993f, -179.851960318f),
            new Point("arena-airpit-tdm-v1-north-03", true, -17.191347122f, .101999998f, 80.067764282f, -179.851960318f),
            new Point("arena-airpit-tdm-v1-north-04", true, -22.415035248f, .101999998f, 79.616569519f, -179.851960318f),
            new Point("arena-airpit-tdm-v1-north-05", true, -19.129999161f, .101999998f, 76.260002136f, -179.851960318f),
            new Point("arena-airpit-tdm-v1-north-06", true, .326310158f, .101999998f, 78.296867371f, -179.851960318f),
            new Point("arena-airpit-tdm-v1-north-07", true, -2.813618660f, .101999998f, 75.182609558f, -179.851960318f),
            new Point("arena-airpit-tdm-v1-north-08", true, -4.896311760f, .101999998f, 79.161193848f, -179.851960318f),
            new Point("arena-airpit-tdm-v1-north-09", true, -10.244000435f, .101999998f, 76.738998413f, -179.851960318f),
            new Point("arena-airpit-tdm-v1-north-10", true, -6.834963799f, .101999998f, 75.353431702f, -179.851960318f),
            new Point("arena-airpit-tdm-v1-south-01", false, -12.569999695f, .079999998f, -74.480003357f, -.000001708f),
            new Point("arena-airpit-tdm-v1-south-02", false, -15.219999313f, .120999992f, -74.400001526f, -.000001708f),
            new Point("arena-airpit-tdm-v1-south-03", false, -18.139999390f, .092999995f, -74.290000916f, 35.438785069f),
            new Point("arena-airpit-tdm-v1-south-04", false, -9.939999580f, .122999966f, -74.400001526f, -.000001708f),
            new Point("arena-airpit-tdm-v1-south-05", false, -7.269999504f, .136999965f, -74.580001831f, -.000001708f),
            new Point("arena-airpit-tdm-v1-south-06", false, -11.340000153f, .079999998f, -71.889999390f, -.000001708f),
            new Point("arena-airpit-tdm-v1-south-07", false, -13.989999771f, .120999992f, -71.809997559f, -.000001708f),
            new Point("arena-airpit-tdm-v1-south-08", false, -16.909999847f, .092999995f, -71.699996948f, -.000001708f),
            new Point("arena-airpit-tdm-v1-south-09", false, -8.710000038f, .122999966f, -71.809997559f, -.000001708f),
            new Point("arena-airpit-tdm-v1-south-10", false, -6.039999962f, .136999965f, -71.989997864f, -36.495658363f),
        },
        new Bounds(-36.45f, 13.50f, -.62f, 4.97f, 69.16f, 85.15f),
        new Bounds(-22.81f, -.50f, -.62f, 4.97f, -80.41f, -64.42f), 1, 17);

    public static Formation? Find(string? map)
        => string.Equals(map, Equator.MapId, StringComparison.OrdinalIgnoreCase) ? Equator
            : string.Equals(map, Bay5.MapId, StringComparison.OrdinalIgnoreCase) ? Bay5
            : string.Equals(map, AirPit.MapId, StringComparison.OrdinalIgnoreCase) ? AirPit : null;
    public static bool Applies(string? map, string? mode)
        => string.Equals(mode, ModeId, StringComparison.OrdinalIgnoreCase) && Find(map) != null;
    public static bool Owns(string? id) => Equator.Owns(id) || Bay5.Owns(id) || AirPit.Owns(id);
}
