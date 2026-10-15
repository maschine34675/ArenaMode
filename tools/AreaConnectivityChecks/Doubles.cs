using ArenaMode.Areas;
using EFT.Game.Spawning;
using UnityEngine;
using UnityEngine.AI;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public float sqrMagnitude => x*x+y*y+z*z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
        public override string ToString() => $"({x},{y},{z})";
    }
    public static class Time { public static int frameCount; }
}
namespace UnityEngine.AI
{
    public enum NavMeshPathStatus { PathComplete, PathPartial, PathInvalid }
    public sealed class NavMeshPath { public NavMeshPathStatus status; public Vector3[] corners; }
    public static class NavMesh
    {
        public const int AllAreas = -1;
        public static Func<Vector3,Vector3,NavMeshPath,bool> Query;
        public static int Calls;
        public static bool CalculatePath(Vector3 a, Vector3 b, int mask, NavMeshPath path)
        { Calls++; return Query(a,b,path); }
    }
}
namespace EFT.Game.Spawning
{
    public interface ISpawnPoint { string Id { get; } Vector3 Position { get; } }
    public sealed class Point : ISpawnPoint
    { public string Id { get; set; } public Vector3 Position { get; set; } }
}
namespace ArenaMode.Shared
{
    public static class ArenaAreaCatalog { public const float NavigationInset=0.4f; }
    internal sealed class AreaDouble
    {
        public object TeamStarts = new();
        public bool IsExcludedSpawn(string id) => id == "excluded";
    }
}
namespace ArenaMode
{
    public sealed class LogDouble
    { public readonly List<string> Lines = new(); public void LogInfo(string s) => Lines.Add(s); public void LogDebug(string s) => Lines.Add(s); }
    public static class Plugin { public static readonly LogDouble ModLogger=new(); }
}
namespace ArenaMode.Areas
{
    // Native surface doubles expose controllable results to the unmodified
    // production adapter. They do not recreate its graph or placement logic.
    internal static partial class ArenaAreaRuntime
    {
        private static Shared.AreaDouble _area;
        private static string _token;
        private static readonly List<ISpawnPoint> Candidates=new();
        private static readonly HashSet<ISpawnPoint> Connected=new();
        private static readonly NavMeshPath ScratchPath=new();
        private static Vector3 _anchor;
        private static bool Active;
        internal static Func<Vector3, bool> SampleOK;
        internal static Func<Vector3, Vector3> SampleResult;
        internal static Func<Vector3, Vector3[], bool> CornersOK;
        private static bool IsCurrentActive() => Active;
        private static bool AllowsGeometrySpawn(Vector3 p) => p.x>=0 && p.x<=1000;
        private static bool Contains(Vector3 p,float inset) => AllowsGeometrySpawn(p);
        private static bool TrySample(Vector3 p,out Vector3 sample)
        { sample=SampleResult(p); return SampleOK(p); }
        private static bool TrySample(Vector3 p,out Vector3 sample,out string reason)
        { bool ok=TrySample(p,out sample); reason=ok?null:"controlled sample failure"; return ok; }
        private static void LogTeamStartDiagnostics() => Plugin.ModLogger.LogInfo("team start diagnostics");
        private static bool InsideCorners(Vector3 p,IReadOnlyList<Vector3> corners)
            => corners != null && corners.Count>0 && CornersOK(p,corners.ToArray());
        private static bool CompleteInsidePath(Vector3 a,Vector3 b)
            => NavMesh.CalculatePath(a,b,NavMesh.AllAreas,ScratchPath)
               && ScratchPath.status==NavMeshPathStatus.PathComplete && InsideCorners(a,ScratchPath.corners);
        private static bool ValidateTeamStarts(out string problem)
        { problem=Connected.Count==Candidates.Count ? null : "missing connected starts"; return problem==null; }
        internal static void SetUp(params Point[] points)
        {
            ResetConnectivity(); Candidates.Clear(); Candidates.AddRange(points);
            _area=new(); _token="raid-one"; _anchor=new(0,0,0); Active=true;
            Time.frameCount=0; NavMesh.Calls=0; Plugin.ModLogger.Lines.Clear();
            SampleOK=_=>true; SampleResult=p=>p; CornersOK=(_,_)=>true;
        }
        internal static bool Advance() => TryTeamConnectivity(new Vector3(0,0,0),out _);
        internal static bool Placement(Vector3 p) => CompleteConnectedPath(p);
        internal static int ConnectedCount => Connected.Count;
        internal static bool ScanComplete => _connectivity?.Complete==true;
        internal static bool Discovered(int i) => _connectivity?.Reached(i)==true;
        internal static void DifferentToken() => _token="raid-two";
        internal static void Inactive() => Active=false;
        internal static void Legacy() => _area.TeamStarts=null;
        internal static void ClearGraph() => ResetConnectivity();
        internal static void ReplaceCandidate(Point before, Point after)
        { int index=Candidates.IndexOf(before);Candidates[index]=after; }
        internal static void Timeout() => LogConnectivityAttempt("bounded timeout",true);
    }
}

// Explicitly enabled for these diagnostic-observation scenarios; the real
// default-off BepInEx routing is exercised by tools/LoggingChecks.
namespace ArenaMode.Diagnostics
{
    internal static class ArenaLog
    {
        internal static bool DeveloperEnabled = true;
        internal static void Developer(string message)
        {
            if (DeveloperEnabled) ArenaMode.Plugin.ModLogger?.LogInfo("[dev] " + message);
        }
    }
}
