using ArenaMode.Modes;
using UnityEngine;

namespace UnityEngine
{
    public readonly struct Vector3 : IFormattable
    {
        public readonly float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up => new(0, 1, 0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x * b, a.y * b, a.z * b);
        public override string ToString() => ToString(null, null);
        public string ToString(string format, IFormatProvider provider)
            => $"({x.ToString(format, provider)}, {y.ToString(format, provider)}, {z.ToString(format, provider)})";
    }
}
namespace UnityEngine.AI
{
    public enum NavMeshPathStatus { PathComplete, PathPartial, PathInvalid }
    public struct NavMeshHit { public Vector3 position; }
    public sealed class NavMeshPath { public NavMeshPathStatus status; }
    public static class NavMesh
    {
        public const int AllAreas = -1;
        public static Func<Vector3, (bool Ok, Vector3 Position)> Sample;
        public static Func<Vector3, Vector3, (bool Ok, NavMeshPathStatus Status)> Route;
        public static readonly List<(Vector3 From, Vector3 To)> Routes = new();
        public static readonly List<(Vector3 Position, float Range, int Areas)> Samples = new();
        public static bool SamplePosition(Vector3 point, out NavMeshHit hit, float range, int areas)
        {
            Samples.Add((point, range, areas));
            var result = Sample(point); hit = new() { position = result.Position }; return result.Ok;
        }
        public static bool CalculatePath(Vector3 from, Vector3 to, int areas, NavMeshPath path)
        {
            Routes.Add((from, to)); var result = Route(from, to); path.status = result.Status; return result.Ok;
        }
    }
}
namespace EFT
{
    public sealed class GameWorld { public string LocationId; }
    public sealed class Player { public Vector3 Position; public bool Eligible = true, Friendly = true; }
    public sealed class BotOwner { public Player GetPlayer; }
}
namespace ArenaMode.Modes
{
    internal sealed class TeamDeathmatchMode
    {
        internal bool IsSearchActive = true;
        internal bool TryClassifyCurrentBody(EFT.Player player, out bool friendly)
        { friendly = player?.Friendly == true; return player?.Eligible == true; }
    }
}
namespace ArenaMode.Areas { internal static class ArenaAreaRuntime { internal static object CurrentArea; } }
namespace ArenaMode.Pickups
{
    internal static class ArenaPickupLayouts
    {
        internal static object Factory(string kit) => kit;
        internal static object Select(object file, string map, object area) => file;
    }
    internal static class ArenaPickupGeometry
    {
        internal static bool FactoryOk = true;
        internal static int FactoryCalls;
        internal static readonly Vector3 FactoryAnchor = new(100, 0, 100);
        internal static Func<Vector3, bool, (bool Ok, Vector3 Floor)> Place;
        internal static Func<Vector3, Vector3, bool> Sight;
        internal static readonly List<(Vector3 Requested, Vector3 Anchor, bool AllowNearby)> Placements = new();
        internal static bool TryFactoryAnchor(object layout, out Vector3 anchor)
        { FactoryCalls++; anchor = FactoryAnchor; return FactoryOk; }
        internal static bool TryPlacePoint(Vector3 point, Vector3 anchor, bool allowNearby,
            out Vector3 floor, out string reason, out bool relocated)
        {
            Placements.Add((point, anchor, allowNearby)); var result = Place(point, allowNearby);
            floor = result.Floor; reason = result.Ok ? null : "test floor refused";
            relocated = !floor.Equals(point); return result.Ok;
        }
        internal static bool LineClear(Vector3 from, Vector3 to) => Sight(from, to);
    }
}
namespace ArenaMode.Hud
{
    internal sealed class DominationPointView : IDisposable
    {
        internal static readonly List<DominationPointView> Created = new();
        internal static int ThrowAt = -1;
        internal readonly string Id;
        internal readonly Vector3 Floor;
        internal readonly float Radius;
        internal bool Disposed;
        internal int Ticks;
        internal DominationPointState State;
        internal DominationPointView(string id, Vector3 floor, float radius)
        {
            if (Created.Count == ThrowAt) throw new InvalidOperationException("test view allocation failed");
            Id = id; Floor = floor; Radius = radius; Created.Add(this);
        }
        internal void Tick(DominationPointState state) { Ticks++; State = state; }
        internal static string Describe(string id, DominationPointState state, bool rich)
            => id + ":" + (state.OwnerUsec.HasValue ? state.OwnerUsec.Value ? "USEC" : "BEAR" : "NEUTRAL");
        public void Dispose() => Disposed = true;
    }
}
namespace ArenaMode.Bots
{
    internal readonly struct DominationObjectivePoint
    {
        internal readonly string Id;
        internal readonly Vector3 Position;
        internal readonly bool? OwnerUsec;
        internal readonly bool Contested;
        internal DominationObjectivePoint(string id, Vector3 position, bool? owner, bool contested)
        { Id = id; Position = position; OwnerUsec = owner; Contested = contested; }
    }
    internal static class DominationObjectiveBridge
    {
        internal static int Calls;
        internal static DominationObjectivePoint[] Points;
        internal static EFT.BotOwner[] Bots;
        internal static bool Live;
        internal static void Update(IReadOnlyList<DominationObjectivePoint> points, IReadOnlyList<EFT.BotOwner> bots, bool live)
        { Calls++; Points = points.ToArray(); Bots = bots.ToArray(); Live = live; }
    }
}
namespace ArenaMode
{
    internal sealed class Logger { internal void LogDebug(string text) { } }
    internal static class Plugin { internal static readonly Logger ModLogger = new(); }
}
namespace ArenaMode.Diagnostics { internal static class ArenaLog { internal static void Developer(string text) { } } }
