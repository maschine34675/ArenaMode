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
        private readonly float _originX, _originZ, _cos, _sin;
        public Bounds(float minX, float maxX, float minY, float maxY, float minZ, float maxZ,
            float originX = 0f, float originZ = 0f, float yaw = 0f)
        {
            _minX = minX; _maxX = maxX; _minY = minY; _maxY = maxY; _minZ = minZ; _maxZ = maxZ;
            _originX = originX; _originZ = originZ;
            _cos = (float)Math.Cos(yaw * Math.PI / 180); _sin = (float)Math.Sin(yaw * Math.PI / 180);
        }
        public bool Contains(float x, float y, float z)
        {
            // Inverse Unity yaw: preserve the actual rotated interior, not its
            // larger world AABB, which would admit paths around a closed gate.
            float dx = x - _originX, dz = z - _originZ;
            float localX = _cos * dx - _sin * dz, localZ = _sin * dx + _cos * dz;
            return localX >= _minX && localX <= _maxX && y >= _minY && y <= _maxY
                && localZ >= _minZ && localZ <= _maxZ;
        }
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
        // Legacy fallback only: older MapPort builds have no GatesOpen signal.
        // Bowl's longest authored drive is 13.5 scaled seconds; an alternate exit
        // can reconnect sooner. Current builds use their actual completion status.
        // Both paths still require complete navigation and physical spawn clearance.
        public float MinimumGateOpeningSeconds => MapId == "arena_bowl" ? 13.5f : 0f;
        public float GateOpeningTimeoutSeconds => MapId == "arena_bowl" ? 20f : 10f;
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

    // Original Bowl TeamFight starts (bowl.modes.json, bowl-native-v2).
    // Source pocket labels north/south mean east/west here; the installed map
    // assigns USEC east and BEAR west despite every source marker's Bear name.
    public static readonly Formation Bowl = new Formation("arena_bowl", "Bowl", "arena-bowl-tdm-v1-",
        "ArenaBowl", "USEC east / BEAR west",
        new Point("arena_bowl-bot-core-02", false, 2.5f, .11f, -21.1f),
        new Point("", false, 2.5f, .11f, -21.1f), new[]
        {
            new Point("arena-bowl-tdm-v1-east-01", true, 50.16f, .047f, -3.25f, 216.8446f),
            new Point("arena-bowl-tdm-v1-east-02", true, 53.346f, .047f, -3.043f, 225.5344f),
            new Point("arena-bowl-tdm-v1-east-03", true, 52.986f, .047f, -6.003f, 230.1743f),
            new Point("arena-bowl-tdm-v1-east-04", true, 55.966f, .047f, -5.853f, 237.9887f),
            new Point("arena-bowl-tdm-v1-east-05", true, 55.976f, .047f, -8.703f, 238.7445f),
            new Point("arena-bowl-tdm-v1-east-06", true, 46.224f, .047f, -5.707f, 258.2478f),
            new Point("arena-bowl-tdm-v1-east-07", true, 49.41f, .047f, -5.5f, 247.4753f),
            new Point("arena-bowl-tdm-v1-east-08", true, 49.05f, .047f, -8.46f, 230.1743f),
            new Point("arena-bowl-tdm-v1-east-09", true, 52.03f, .047f, -8.31f, 209.5248f),
            new Point("arena-bowl-tdm-v1-east-10", true, 52.04f, .047f, -11.16f, 198.9134f),
            new Point("arena-bowl-tdm-v1-west-01", false, -52.144f, .047f, -68.883f, 51.3698f),
            new Point("arena-bowl-tdm-v1-west-02", false, -55.33f, .047f, -69.09f, 51.3698f),
            new Point("arena-bowl-tdm-v1-west-03", false, -54.97f, .047f, -66.13f, 51.3698f),
            new Point("arena-bowl-tdm-v1-west-04", false, -57.95f, .047f, -66.28f, 51.3698f),
            new Point("arena-bowl-tdm-v1-west-05", false, -57.96f, .047f, -63.43f, 51.3698f),
            new Point("arena-bowl-tdm-v1-west-06", false, -48.764f, .047f, -66.183f, 51.3698f),
            new Point("arena-bowl-tdm-v1-west-07", false, -51.95f, .047f, -66.39f, 51.3698f),
            new Point("arena-bowl-tdm-v1-west-08", false, -51.59f, .047f, -63.43f, 51.3698f),
            new Point("arena-bowl-tdm-v1-west-09", false, -54.57f, .047f, -63.58f, 51.3698f),
            new Point("arena-bowl-tdm-v1-west-10", false, -54.58f, .047f, -60.73f, 51.3698f),
        },
        // Original rotated SPAWN_BOUND_FIGHTER, inset by a standing capsule's
        // radius horizontally. All held-path corners must stay in this OBB.
        new Bounds(-11.1677f, 11.1677f, -.1498f, 4.1334f, -12.82095f, 12.82095f,
            51.2529f, -7.0645f, 318.9815f),
        new Bounds(-9.82035f, 9.82035f, -.1498f, 4.1334f, -10.53575f, 10.53575f,
            -52.8998f, -64.8147f, 318.9815f), 2, 16);

    public static Formation? Find(string? map)
        => string.Equals(map, Equator.MapId, StringComparison.OrdinalIgnoreCase) ? Equator
            : string.Equals(map, Bay5.MapId, StringComparison.OrdinalIgnoreCase) ? Bay5
            : string.Equals(map, AirPit.MapId, StringComparison.OrdinalIgnoreCase) ? AirPit
            : string.Equals(map, Bowl.MapId, StringComparison.OrdinalIgnoreCase) ? Bowl : null;
    public static bool Applies(string? map, string? mode)
        => string.Equals(mode, ModeId, StringComparison.OrdinalIgnoreCase) && Find(map) != null;
    public static bool Owns(string? id) => Equator.Owns(id) || Bay5.Owns(id) || AirPit.Owns(id) || Bowl.Owns(id);
}
