using ArenaMode.Areas;
using ArenaMode.Respawn;
using EFT;
using Redeploy.Core.Spawning;
using UnityEngine;

var game = new LocalGame();
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
void Throws(Action action, string message) { bool threw = false; try { action(); } catch (InvalidOperationException) { threw = true; } Check(threw, message); }
ArenaMode.Session.ArenaSession.Game = game;
var fallback = new Selector();

// Full-map code remains lazy and retains the same selector instance.
Check(ArenaAreaRespawn.TryPrepare(game, fallback, out var unchanged), "Full-map prepare failed.");
Check(ReferenceEquals(unchanged, fallback) && fallback.Calls == 0, "Full-map Resolve timing changed.");
new ArenaSpawnSelector(fallback, () => default, 40).Resolve(game);
Check(fallback.Calls == 1, "Full-map native fallback was removed.");

ArenaAreaRuntime.CurrentArea = new Area { Id = "test-area" };
ArenaAreaRuntime.InvalidPosition = false;
ArenaAreaRuntime.RejectPick = false;
ArenaAreaRuntime.Aborted = false;
var bounded = new ArenaSpawnSelector(fallback, () => default, 40);
Check(ArenaAreaRespawn.TryPrepare(game, bounded, out var prepared), "Area preflight failed.");
Check(fallback.Calls == 1 && ArenaAreaRuntime.Picks == 1, "Area used an unrestricted fallback.");
Check(prepared.Resolve(game).position.x == 4 && ArenaAreaRuntime.Picks == 1,
    "Prepared point was redrawn after body mutation.");
ArenaAreaRuntime.InvalidPosition = true;
Throws(() => prepared.Resolve(game), "Changed final position contract was not rechecked.");
ArenaAreaRuntime.InvalidPosition = false;
ArenaMode.Session.ArenaSession.Game = new LocalGame();
Throws(() => prepared.Resolve(game), "Stale raid released a replacement.");
ArenaMode.Session.ArenaSession.Game = game;
ArenaAreaRuntime.CurrentArea.Id = "new-area";
Throws(() => prepared.Resolve(game), "Different area released a prepared replacement.");
ArenaAreaRuntime.CurrentArea.Id = "test-area";
ArenaAreaRuntime.RejectPick = true;
Throws(() => bounded.Resolve(game), "Unavailable bounded selector silently fell back.");
Check(fallback.Calls == 1, "No-point path called native unrestricted fallback.");
Check(!ArenaAreaRespawn.TryPrepare(game, bounded, out _) && ArenaAreaRuntime.Aborted,
    "Failed preflight did not request a safe abort before engine handover.");
Check(fallback.Calls == 1, "Failed preflight used full-map fallback.");
Console.WriteLine($"ARENA_AREA_CLIENT_CONTRACTS_PASSED checks={checks}");

sealed class Selector : ISpawnSelector
{
    public int Calls;
    public (Vector3 position, Quaternion rotation) Resolve(LocalGame game) { Calls++; return (new Vector3(99, 0, 0), default); }
}

// Narrow host doubles: tests execute the production selector/preflight classes,
// not EFT physics or Redeploy's native body creation. GPU coverage is separate.
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public float sqrMagnitude => x*x+y*y+z*z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    public struct Quaternion { }
    public static class Mathf { public static float Max(float a, float b) => Math.Max(a,b); }
    public static class Random { public static int Range(int min, int max) => min; }
}
namespace EFT
{
    public sealed class GameWorld { }
    public sealed class Player { public Vector3 Position; }
    public sealed class LocalGame { public GameWorld GameWorld = new(); public Player LocalPlayer = new(); }
}
namespace EFT.Game.Spawning
{
    [Flags] public enum ESpawnCategoryMask { Player = 1 }
    public interface ISpawnPoint { string Id { get; } bool SpawnBlocked { get; } ESpawnCategoryMask Categories { get; } Vector3 Position { get; } Quaternion Rotation { get; } }
    public sealed class SpawnSystem { public List<ISpawnPoint> _spawnPoints = new(); }
}
namespace Redeploy.Core.Spawning
{
    public interface ISpawnSelector { (Vector3 position, Quaternion rotation) Resolve(LocalGame game); }
    public static class SpawnSystemCache { public static object GetOrCreate(LocalGame game) => new EFT.Game.Spawning.SpawnSystem(); }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static LocalGame Game;
        public static bool IsExactActiveRaid(LocalGame game, GameWorld world) => ReferenceEquals(game, Game) && ReferenceEquals(world, Game.GameWorld);
    }
}
namespace ArenaMode
{
    public static class Plugin { public static Logger ModLogger = new(); }
    public sealed class Logger { public void LogWarning(string text) { } }
}
namespace ArenaMode.Areas
{
    public sealed class Area { public string Id; }
    public static class ArenaAreaRuntime
    {
        public sealed class TeamSpawnLease { public bool CanReleasePlayer = true; }
        public static TeamSpawnLease ReservePlayerPosition(Vector3 position) => null;
        public static Area CurrentArea;
        public static bool InvalidPosition, RejectPick, Aborted;
        public static int Picks;
        public static void RequirePosition(Vector3 position) { if (InvalidPosition) throw new InvalidOperationException("Rejected."); }
        public static void Abort(string reason) { Aborted = true; }
        public static (Vector3 position, Quaternion rotation) PickRespawn(Vector3 avoid, float distance)
        { Picks++; if (RejectPick) throw new InvalidOperationException("No safe point."); return (new Vector3(4,0,0), default); }
    }
}
