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
        // Iceberg needs no case of its own: every MapPort build that ships it has
        // GatesOpen, and its longest drive (GATES_A leaf B) is 5 s at drive scale
        // 1.0 (2.5 s at the default 0.5), inside the ordinary ten-second watchdog.
        // Chop Shop needs no legacy minimum either (GatesOpen exists in every build that
        // ships it), but it gets a longer watchdog: its big gate drives 8.0 s at MapPort's
        // largest drive scale 1.0 (4.0 s at the default 0.5), and the match flow polls the
        // opening once a second (GPU-2, 2026-11-12: MapPort reported the gates open 4.0 s
        // after the release, the formation was revalidated at 5.00 s). The ordinary ten
        // seconds would leave two polls after an eight-second drive, and a missed one ends
        // the protected match; fifteen leave seven. The watchdog still only catches a
        // formation that never reconnects, which is all it is for.
        public float MinimumGateOpeningSeconds => MapId == "arena_bowl" ? 13.5f : 0f;
        public float GateOpeningTimeoutSeconds => MapId == "arena_bowl" ? 20f
            : MapId == "arena_autoservice" ? 15f : 10f;
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

    // Original Iceberg pockets (iceberg.modes.json, iceberg-native-v2). Iceberg has
    // no TeamFight mode; they come from CheckPoint: USEC = Usec house (5..14) in
    // SPAWN_BOUND_DEFENCE (west), BEAR = Bear house (5..14) in SPAWN_BOUND_FIGHTER
    // (east), as MapPort's player-usec/player-bear rows assign them. Bear keeps its
    // three authored heights (floor .7345, terrace 1.8862, (14) at .8753).
    //
    // The interiors are not the authored SPAWN_BOUNDs, which reach past the
    // closed gates: DEFENCE 3.1 m east of the GATES_B leaf plane (x -36.042),
    // FIGHTER 1.4 m west of the GATES_A leaf plane (x 82.382). A held path or a
    // pickup seed in that outside strip would count as inside a closed box. Each
    // box therefore ends at its gate plane minus the .35 m capsule radius
    // FloorAndClear uses; the other faces are the authored bounds inset by the
    // same .35 m horizontally, as Bowl's are. The west pocket continues north past
    // the authored box to the rotating CheckPoint gate (z -75.664), so the USEC box
    // runs to that gate minus the inset (Usec house (6) sits on the authored edge
    // and would fall out of a strict inset). USEC keeps the full authored height;
    // FIGHTER's 16.3 m would admit other floors and is cut to -.4..5 m, both pocket
    // floors plus a standing body. The authored yaws (179.9999, 270.2255) are
    // within .23 degrees of the axes, so these boxes are world-aligned.
    //
    // Donor and outside destination: the installed arena_iceberg-bot-core-01, which
    // had a complete path from the Bear spawn in MapPort's GPU-2 raid (2026-10-30).
    // Both exits lie outside the gates (East 10.4 m beyond GATES_A, West 5.6 m
    // beyond GATES_B): no booth exits. Anchors: Usec house (7), Bear house (14).
    public static readonly Formation Iceberg = new Formation("arena_iceberg", "Iceberg", "arena-iceberg-tdm-v1-",
        "ArenaIceberg", "USEC west / BEAR east",
        new Point("arena_iceberg-bot-core-01", false, -10.88f, 3.25f, -80.66f),
        new Point("", false, -10.88f, 3.25f, -80.66f), new[]
        {
            new Point("arena-iceberg-tdm-v1-west-01", true, -39.3812f, 3.2493f, -87.6749f, 75.183f),
            new Point("arena-iceberg-tdm-v1-west-02", true, -40.7162f, 3.2473f, -79.931f, 88.2443f),
            new Point("arena-iceberg-tdm-v1-west-03", true, -41.3192f, 3.2473f, -89.603f, 45.3416f),
            new Point("arena-iceberg-tdm-v1-west-04", true, -39.8452f, 3.2473f, -91.1639f, 52.3511f),
            new Point("arena-iceberg-tdm-v1-west-05", true, -42.9652f, 3.2443f, -92.504f, 85.0953f),
            new Point("arena-iceberg-tdm-v1-west-06", true, -39.7352f, 3.2473f, -94.964f, 358.2447f),
            new Point("arena-iceberg-tdm-v1-west-07", true, -39.9732f, 3.2473f, -85.4959f, 81.4048f),
            new Point("arena-iceberg-tdm-v1-west-08", true, -40.0992f, 3.2473f, -82.904f, 89.3089f),
            new Point("arena-iceberg-tdm-v1-west-09", true, -37.0262f, 3.2473f, -93.7889f, 354.4831f),
            new Point("arena-iceberg-tdm-v1-west-10", true, -44.5252f, 3.2503f, -90.424f, 99.4831f),
            new Point("arena-iceberg-tdm-v1-east-01", false, 97.9361f, .7345f, -83.7253f, 251.5087f),
            new Point("arena-iceberg-tdm-v1-east-02", false, 97.3612f, .7345f, -86.7347f, 267.5384f),
            new Point("arena-iceberg-tdm-v1-east-03", false, 90.7486f, 1.8862f, -94.3529f, 303.1341f),
            new Point("arena-iceberg-tdm-v1-east-04", false, 95.9648f, .7345f, -94.074f, 265.3582f),
            new Point("arena-iceberg-tdm-v1-east-05", false, 89.2948f, 1.8862f, -86.464f, 271.4964f),
            new Point("arena-iceberg-tdm-v1-east-06", false, 94.6365f, .7345f, -84.9785f, 266.5088f),
            new Point("arena-iceberg-tdm-v1-east-07", false, 90.7653f, 1.8862f, -91.9553f, 258.906f),
            new Point("arena-iceberg-tdm-v1-east-08", false, 97.7513f, .7345f, -96.1495f, 276.4523f),
            new Point("arena-iceberg-tdm-v1-east-09", false, 96.6548f, .7345f, -91.174f, 258.9876f),
            new Point("arena-iceberg-tdm-v1-east-10", false, 95.2948f, .8753f, -89.564f, 273.3514f),
        },
        new Bounds(-48.31f, -36.392f, 2.328f, 10.271f, -96.926f, -76.014f),
        new Bounds(82.732f, 114.444f, -.4f, 5f, -98.465f, -78.223f), 2, 19);

    // Original Chop Shop TeamFight pockets (autoservice.modes.json, autoservice-native-v2;
    // the map is arena_autoservice, ArenaMapPort's id, Arena's name is Chop Shop). USEC = the
    // east pocket (SPAWN_BOUND_Fighter), BEAR = the west pocket (SPAWN_BOUND_Fighter (1)), as
    // MapPort's player-usec/player-bear rows assign them (the same split as Bowl). Every marker
    // is named "Usec house (n)": the name says nothing about the faction, only the containing
    // bound does. The twenty points keep their authored positions and yaws, in sidecar order.
    //
    // Interiors, derived face by face. Both authored bounds have yaw exactly 270 (scene
    // quaternion 0.0003 degrees off), so the boxes are world-aligned. Horizontally each face is
    // the authored AABB inset by the .35 m capsule radius FloorAndClear uses (east x -344.2145 ..
    // -332.6485, z 473.471 .. 512.1141; west x -413.9845 .. -402.4185, z 473.1481 .. 511.7911).
    // The face towards the gates is additionally held at the gate leaf's pocket-side face plus
    // .35 m where that is stricter. East: the big gate (big_gate_metal, leaf "gate 1" x -344.202
    // .. -343.937, movement collider -344.150 .. -344.090) gives -343.937 + .35 = -343.587, which
    // is stricter than the inset alone (-343.865); the equator shutter leaf (x -344.650 ..
    // -344.558) lies further west and is excluded by the same face. West: the techlight leaves
    // (x -402.320 .. -402.200, frame to -402.048) give -402.320 - .35 = -402.670, looser than the
    // inset -402.7685, so the inset stays. No gate sits in a z end. Without the clip a held path
    // or a pickup seed in the strip between leaf and authored edge would count as inside the
    // closed box; the east authored x min (-344.2145) coincides with the leaf's yard face.
    // The smallest horizontal margins of a marker to a face are 1.817 m (east x max, Usec house
    // (19)) and 2.60 m (west z min, (13)).
    //
    // Height is cut, not authored: the east bound spans 17.1 m (y -11.36 .. 5.75, down 7.5 m
    // below the floor and up past the tribune decks at 4.35 .. 5.3), the west 9.8 m (-7.72 ..
    // 2.12). Measured on GPU-2 on 2026-11-12 (ordinary PMC raid, gates opened by MapPort, no
    // ArenaMode involvement; native acceptance with held gates is still pending): the west
    // pocket's roof is walkable navmesh at y 1.7 .. 1.9 over the whole footprint and the east
    // pocket has separate navmesh islands at -1.8, -0.8, -0.2 and 1.0 (path_partial from
    // inside the pocket with the gates open); the highest connected pocket surface is a step at
    // (-343.5, -2.5, 489.5). The box therefore runs y -4.8 .. -1.0 for both pockets: 1.0 m
    // below the lowest marker (-3.84), 1.5 m above that step and 2.7 m below the west roof.
    // A second survey the same day (2.5 m grid over both footprints, surfaces between the
    // floor and y 0.4) found nothing else connected: curbs at -3.3 along the east wall and
    // the step are part of the pocket; a strip along the east pocket's north edge (y -1.7 ..
    // -2.0 at x -340.5 .. -341.7, z 511.2 .. 511.5) and an unstandable shelf at the west
    // pocket's south end lie INSIDE the band, but no path reaches them from the pocket floor
    // or from the yard, so no held path and no formation point can use them. The band is
    // not narrowed to cut them out: 0.3 m above the step would be the only room for it.
    // The smallest marker margin overall is that 0.96 m to the floor of the box ((19) at
    // -3.84). Arena's own NavMesh has no polygon inside either footprint above -3.35 or below
    // -3.83, so the band loses no walkable surface of Arena's mesh.
    //
    // Donor and outside destination: the installed arena_autoservice-bot-core-03 row
    // (-367.44, -3.79, 492.11) of ArenaMapPort 0.5.0+b7876d3, central in the yard, 7.6 m from
    // the capture point. All seven Bot rows are reachable from both pockets on GPU-2 on
    // 2026-11-12; core-03 has complete paths of 44.4 m (west, 10 corners) and 39.9 m (east,
    // 9 corners). Both exits ("Chop Shop East Gate" 4.9 m, "Chop Shop West Gate" about 9 m)
    // lie outside the gate planes: no booth exits. Anchors: Usec house (4) = MapPort player-usec
    // (east-05) and Usec house (6) = MapPort player-bear (west-02).
    public static readonly Formation AutoService = new Formation("arena_autoservice", "Chop Shop", "arena-autoservice-tdm-v1-",
        "ArenaAutoService", "USEC east / BEAR west",
        new Point("arena_autoservice-bot-core-03", false, -367.44f, -3.79f, 492.11f),
        new Point("", false, -367.44f, -3.79f, 492.11f), new[]
        {
            new Point("arena-autoservice-tdm-v1-east-01", true, -337.789f, -3.8f, 485.627f, 239.9997f),
            new Point("arena-autoservice-tdm-v1-east-02", true, -338.448f, -3.8f, 490.47f, 269.9997f),
            new Point("arena-autoservice-tdm-v1-east-03", true, -337.87f, -3.8f, 495.48f, 314.9997f),
            new Point("arena-autoservice-tdm-v1-east-04", true, -336.619f, -3.8f, 497.847f, 239.9997f),
            new Point("arena-autoservice-tdm-v1-east-05", true, -337.278f, -3.8f, 502.69f, 269.9997f),
            new Point("arena-autoservice-tdm-v1-east-06", true, -336.7f, -3.8f, 507.7f, 269.9998f),
            new Point("arena-autoservice-tdm-v1-east-07", true, -338.781f, -3.783f, 476.541f, 281.4502f),
            new Point("arena-autoservice-tdm-v1-east-08", true, -338.85f, -3.783f, 482.13f, 255.353f),
            new Point("arena-autoservice-tdm-v1-east-09", true, -340.59f, -3.797f, 506.675f, 271.065f),
            new Point("arena-autoservice-tdm-v1-east-10", true, -334.816f, -3.84f, 482.536f, 255.0871f),
            new Point("arena-autoservice-tdm-v1-west-01", false, -409.9721f, -3.74f, 491.243f, 59.9998f),
            new Point("arena-autoservice-tdm-v1-west-02", false, -409.313f, -3.74f, 487.0409f, 89.9998f),
            new Point("arena-autoservice-tdm-v1-west-03", false, -409.451f, -3.74f, 483.84f, 134.9998f),
            new Point("arena-autoservice-tdm-v1-west-04", false, -409.256f, -3.74f, 499.843f, 89.9997f),
            new Point("arena-autoservice-tdm-v1-west-05", false, -409.402f, -3.74f, 477.391f, 89.9998f),
            new Point("arena-autoservice-tdm-v1-west-06", false, -407.045f, -3.74f, 482.428f, 134.9998f),
            new Point("arena-autoservice-tdm-v1-west-07", false, -406.688f, -3.74f, 501.696f, 92.8713f),
            new Point("arena-autoservice-tdm-v1-west-08", false, -405.933f, -3.72f, 476.098f, 71.763f),
            new Point("arena-autoservice-tdm-v1-west-09", false, -409.503f, -3.72f, 495.572f, 77.189f),
            new Point("arena-autoservice-tdm-v1-west-10", false, -405.941f, -3.72f, 486.419f, 159.278f),
        },
        new Bounds(-343.587f, -332.999f, -4.8f, -1f, 473.821f, 511.764f),
        new Bounds(-413.635f, -402.769f, -4.8f, -1f, 473.498f, 511.441f), 4, 11);

    public static Formation? Find(string? map)
        => string.Equals(map, Equator.MapId, StringComparison.OrdinalIgnoreCase) ? Equator
            : string.Equals(map, Bay5.MapId, StringComparison.OrdinalIgnoreCase) ? Bay5
            : string.Equals(map, AirPit.MapId, StringComparison.OrdinalIgnoreCase) ? AirPit
            : string.Equals(map, Bowl.MapId, StringComparison.OrdinalIgnoreCase) ? Bowl
            : string.Equals(map, Iceberg.MapId, StringComparison.OrdinalIgnoreCase) ? Iceberg
            : string.Equals(map, AutoService.MapId, StringComparison.OrdinalIgnoreCase) ? AutoService : null;
    public static bool Applies(string? map, string? mode)
        => (string.Equals(mode, ModeId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "domination", StringComparison.OrdinalIgnoreCase)) && Find(map) != null;
    public static bool Owns(string? id) => Equator.Owns(id) || Bay5.Owns(id) || AirPit.Owns(id) || Bowl.Owns(id)
        || Iceberg.Owns(id) || AutoService.Owns(id);
}
