using System;
using System.Collections.Generic;
using System.Linq;
using ArenaMode.Shared;
using EFT.Game.Spawning;
using UnityEngine;
using UnityEngine.AI;

namespace ArenaMode.Areas;

internal static partial class ArenaAreaRuntime
{
    private sealed class ConnectivityVertex
    {
        internal readonly ISpawnPoint Point;
        internal readonly string Id;
        internal readonly Vector3 Position, Sample;
        internal ConnectivityVertex(ISpawnPoint point, Vector3 position, Vector3 sample)
        { Point = point; Id = point?.Id ?? "initial-anchor"; Position = position; Sample = sample; }
    }

    // Unlike one global shortest-path query, the tree proves that each used leg
    // stays inside the contour. It does not approve the path later chosen by AI.
    private const int ConnectivityQueriesPerFrame = 24;
    // One final linear fresh snapshot costs at most two queries per marker
    // (56 for today's 28 markers; hard maximum 256), once per attempt. It avoids
    // publishing stale ancestry discovered over earlier preparation frames.
    private static ArenaAreaConnectivityGraph _connectivity;
    private static readonly List<ConnectivityVertex> ConnectivityVertices = new List<ConnectivityVertex>();
    private static readonly Dictionary<string, string> ConnectivitySampleFailures = new Dictionary<string, string>();
    private static string[,] _connectivityProbeResults;
    private static string _connectivityToken;
    private static int _connectivityAttempt, _connectivityFrame = -1, _connectivityQueries;
    private static bool _connectivityLogged, _connectivityRetry;
    private static bool ConnectivityScanning => _connectivity != null && !_connectivity.Complete && !_connectivityRetry;

    private static void ResetConnectivityAttempt()
    {
        _connectivity = null; _connectivityToken = null; _connectivityProbeResults = null;
        ConnectivityVertices.Clear(); ConnectivitySampleFailures.Clear();
        _connectivityLogged = false; _connectivityRetry = false; _connectivityQueries = 0;
        Connected.Clear();
    }

    private static void ResetConnectivity()
    {
        ResetConnectivityAttempt();
        _connectivityAttempt = 0; _connectivityFrame = -1;
    }

    private static bool TryTeamConnectivity(Vector3 liveAnchor, out string problem)
    {
        problem = null;
        if (_connectivityRetry) ResetConnectivityAttempt();
        if (_connectivity != null && (liveAnchor - _anchor).sqrMagnitude > 0.01f)
        {
            LogConnectivityAttempt("initial anchor moved during preparation", false);
            ResetConnectivityAttempt();
        }
        if (_connectivity == null)
        {
            _anchor = liveAnchor;
            _connectivityAttempt++;
            if (Candidates.Count > 128)
            { problem = "too many scene markers for bounded waypoint validation: " + Candidates.Count; return false; }
            ConnectivityVertices.Add(new ConnectivityVertex(null, _anchor, _anchor));
            foreach (var point in Candidates)
            {
                if (point == null || _area.IsExcludedSpawn(point.Id) || !AllowsGeometrySpawn(point.Position)) continue;
                if (!TrySample(point.Position, out var sample, out string sampleReason))
                { ConnectivitySampleFailures[point.Id] = $"sample rejected at {point.Position}: {sampleReason}"; continue; }
                ConnectivityVertices.Add(new ConnectivityVertex(point, point.Position, sample));
            }
            _connectivityToken = _token;
            _connectivity = new ArenaAreaConnectivityGraph(ConnectivityVertices.Count);
            _connectivityProbeResults = new string[_connectivity.Count, _connectivity.Count];
        }
        if (_connectivityFrame != Time.frameCount && !_connectivity.Complete)
        {
            _connectivityFrame = Time.frameCount;
            _connectivityQueries += _connectivity.Step(ConnectivityQueriesPerFrame, ProbeConnectivityEdge);
        }
        if (!_connectivity.Complete)
        {
            problem = $"waypoint validation attempt {_connectivityAttempt}: {_connectivity.ReachedCount - 1}/{_connectivity.Count - 1} markers reached, {_connectivityQueries} directed queries";
            return false;
        }
        Connected.Clear();
        bool[] verified = _connectivity.VerifiedSnapshot(ConnectivityVertexCurrent,
            (from, to) => { _connectivityQueries++; return ProbeConnectivityEdge(from, to); });
        for (int i = 1; i < _connectivity.Count; i++)
            if (verified[i]) Connected.Add(ConnectivityVertices[i].Point);
        bool ready = ValidateTeamStarts(out problem);
        LogConnectivityAttempt(ready ? "team starts verified" : problem, false);
        _connectivityRetry = !ready;
        return ready;
    }

    private static bool ProbeConnectivityEdge(int from, int to)
    {
        if (!ConnectivityVertexCurrent(from) || !ConnectivityVertexCurrent(to))
        { _connectivityProbeResults[from, to] = "stale marker/sample"; return false; }
        bool result = DirectConnectivityPath(ConnectivityVertices[from].Sample, ConnectivityVertices[to].Sample, out string reason);
        _connectivityProbeResults[from, to] = result ? "complete inside" : reason;
        return result;
    }

    private static bool ConnectivityVertexCurrent(int index)
    {
        if (_connectivity == null || _connectivityToken != _token || !IsCurrentActive()
            || index < 0 || index >= ConnectivityVertices.Count) return false;
        var vertex = ConnectivityVertices[index];
        if (index != 0 && (vertex.Point == null || vertex.Point.Id != vertex.Id
            || !Candidates.Any(p => ReferenceEquals(p, vertex.Point))
            || (vertex.Point.Position - vertex.Position).sqrMagnitude >= 0.0001f
            || _area.IsExcludedSpawn(vertex.Id) || !AllowsGeometrySpawn(vertex.Point.Position))) return false;
        return TrySample(vertex.Position, out var sample) && (sample - vertex.Sample).sqrMagnitude < 0.0001f;
    }

    private static bool DirectConnectivityPath(Vector3 origin, Vector3 target, out string reason)
    {
        reason = null;
        if (!Contains(origin, ArenaAreaCatalog.NavigationInset) || !Contains(target, ArenaAreaCatalog.NavigationInset))
        { reason = "outside/inset endpoint"; return false; }
        // Exactly the same sampled point needs no edge. Nearby points may still
        // straddle a wall or a one-way link and require the full native query.
        if ((origin - target).sqrMagnitude == 0f) return true;
        if (!NavMesh.CalculatePath(origin, target, NavMesh.AllAreas, ScratchPath))
        { reason = "native path calculation failed"; return false; }
        if (ScratchPath.status != NavMeshPathStatus.PathComplete)
        { reason = "native path " + ScratchPath.status; return false; }
        if (!InsideCorners(origin, ScratchPath.corners))
        { reason = "outside/inset corner or segment"; return false; }
        return true;
    }

    /// <summary>
    /// Placement proof only. Actual bot paths still pass InsideCorners/AllowPath.
    /// Freshly checks a complete bidirectional chain; cached membership alone is
    /// never enough after a door, marker, carving result or raid identity changes.
    /// </summary>
    private static bool CompleteConnectedPath(Vector3 sample)
    {
        if (_area?.TeamStarts == null) return CompleteInsidePath(_anchor, sample);
        if (_connectivity == null || !_connectivity.Complete || !ConnectivityVertexCurrent(0)) return false;
        // The hot bot-ranking path already supplies an exact sampled marker.
        // Revalidate its one stored chain, not every possible attachment. A stale
        // chain rejects this candidate; it is never replaced with cached success.
        for (int i = 1; i < _connectivity.Count; i++)
            if (_connectivity.Reached(i) && (ConnectivityVertices[i].Sample - sample).sqrMagnitude == 0f)
                return _connectivity.ValidateChain(i, ConnectivityVertexCurrent, ProbeConnectivityEdge);
        if (DirectConnectivityPath(_anchor, sample, out _) && DirectConnectivityPath(sample, _anchor, out _)) return true;
        foreach (int i in Enumerable.Range(1, _connectivity.Count - 1)
            .Where(_connectivity.Reached).OrderBy(n => (ConnectivityVertices[n].Sample - sample).sqrMagnitude))
        {
            if (!ConnectivityVertexCurrent(i)) continue;
            var vertex = ConnectivityVertices[i];
            if (!DirectConnectivityPath(vertex.Sample, sample, out _) || !DirectConnectivityPath(sample, vertex.Sample, out _)) continue;
            if (_connectivity.ValidateChain(i, ConnectivityVertexCurrent, ProbeConnectivityEdge)) return true;
        }
        return false;
    }

    private static void LogConnectivityAttempt(string result, bool timeout)
    {
        if (!ArenaMode.Diagnostics.ArenaLog.DeveloperEnabled) return;
        if (timeout && _area?.TeamStarts != null) LogTeamStartDiagnostics();
        if (_area?.TeamStarts == null || _connectivityLogged && !timeout) return;
        if (_connectivityLogged && timeout)
        { ArenaMode.Diagnostics.ArenaLog.Developer("Arena area waypoint timeout after last completed attempt: " + result); return; }
        _connectivityLogged = true;
        int reached = _connectivity?.ReachedCount - 1 ?? 0;
        int via = _connectivity == null ? 0 : Enumerable.Range(1, _connectivity.Count - 1)
            .Count(i => _connectivity.Reached(i) && _connectivity.Parent(i) != 0);
        ArenaMode.Diagnostics.ArenaLog.Developer($"Arena area waypoint attempt {_connectivityAttempt}{(timeout ? " timeout" : "")}: anchor {_anchor}, sampled {Math.Max(0, ConnectivityVertices.Count - 1)}/{Candidates.Count}, reached {reached}, fresh markers {Connected.Count}, recovered-via-waypoint {via}, directed queries {_connectivityQueries}; {result}.");
        foreach (var failure in ConnectivitySampleFailures)
            ArenaMode.Diagnostics.ArenaLog.Developer($"Arena area waypoint marker {failure.Key}: {failure.Value}.");
        if (_connectivity == null) return;
        for (int i = 1; i < _connectivity.Count; i++)
        {
            if (_connectivity.Reached(i) && ConnectivityVertexCurrent(i) && (!_connectivity.Complete || Connected.Contains(ConnectivityVertices[i].Point))) continue;
            int parent = _connectivity.LastSource(i);
            string from = parent < 0 ? "not visited" : ConnectivityVertices[parent].Id;
            string forward = parent < 0 ? "not queried" : _connectivityProbeResults[parent, i] ?? "not queried";
            string reverse = parent < 0 ? "not queried" : _connectivityProbeResults[i, parent] ?? "not queried";
            ArenaMode.Diagnostics.ArenaLog.Developer($"Arena area waypoint marker {ConnectivityVertices[i].Id} at {ConnectivityVertices[i].Position}: from {from}; forward={forward}; reverse={reverse}; current={ConnectivityVertexCurrent(i)}; fresh-parent-chain={Connected.Contains(ConnectivityVertices[i].Point)}.");
        }
    }
}
