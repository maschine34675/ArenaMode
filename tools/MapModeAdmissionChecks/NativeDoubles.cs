// Native boundaries only. Selection, distance floor, position deduplication and
// map exclusions run as production source; this does not emulate NavMesh.
public sealed class BotZone
{
    public string NameZone = "";
    public EFT.Game.Spawning.ISpawnPoint[] SpawnPoints = [];
}
namespace UnityEngine
{
    public readonly struct Vector3(float x, float y, float z)
    {
        public readonly float x = x, y = y, z = z;
        public static float Distance(Vector3 a, Vector3 b)
            => MathF.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z));
    }
}
namespace EFT.Game.Spawning
{
    public interface ISpawnPoint
    {
        string Id { get; }
        UnityEngine.Vector3 Position { get; }
        bool SpawnBlocked { get; set; }
    }
    public sealed class Point(string id, UnityEngine.Vector3 position) : ISpawnPoint
    {
        public string Id { get; } = id;
        public UnityEngine.Vector3 Position { get; } = position;
        public bool SpawnBlocked { get; set; }
    }
}
namespace ArenaMode.Bots
{
    internal static class ArenaBotSpawnFilter
    {
        internal static string CurrentMap = "";
        internal static bool IsExcludedPoint(EFT.Game.Spawning.ISpawnPoint point)
            => ArenaMode.Shared.ArenaBotSpawnPolicy.IsExcludedPoint(CurrentMap, point.Id);
    }
}
