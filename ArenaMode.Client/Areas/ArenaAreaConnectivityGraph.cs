using System;
using System.Collections.Generic;

namespace ArenaMode.Areas;

/// <summary>
/// Incremental tree of independently verified bidirectional native paths.
/// Vertices and native geometry belong to the exact raid adapter; this class
/// never infers a reverse edge or permission to use an arbitrary bot path.
/// </summary>
internal sealed class ArenaAreaConnectivityGraph
{
    private readonly int[] _parents, _lastSources;
    private readonly List<int> _frontier = new List<int>();
    private int _sourceCursor, _targetCursor = 1, _pendingTarget = -1;
    private bool _forward;
    internal int Count => _parents.Length;
    internal int ReachedCount => _frontier.Count;
    internal bool Complete { get; private set; }
    internal int Parent(int node) => _parents[node];
    internal int LastSource(int node) => _lastSources[node];
    internal bool Reached(int node) => node >= 0 && node < Count && _parents[node] >= 0;

    internal ArenaAreaConnectivityGraph(int count)
    {
        if (count < 1 || count > 129) throw new ArgumentOutOfRangeException(nameof(count));
        _parents = new int[count]; _lastSources = new int[count];
        for (int i = 0; i < count; i++) { _parents[i] = -1; _lastSources[i] = -1; }
        _parents[0] = 0; _frontier.Add(0);
        Complete = count == 1;
    }

    /// <summary>Budget counts directed native queries, including failed/reverse queries.</summary>
    internal int Step(int budget, Func<int, int, bool> travel)
    {
        if (budget < 0) throw new ArgumentOutOfRangeException(nameof(budget));
        if (travel == null) throw new ArgumentNullException(nameof(travel));
        int used = 0;
        while (!Complete && used < budget)
        {
            if (_pendingTarget < 0)
            {
                while (_sourceCursor < _frontier.Count)
                {
                    while (_targetCursor < Count && Reached(_targetCursor)) _targetCursor++;
                    if (_targetCursor < Count) break;
                    _sourceCursor++; _targetCursor = 1;
                }
                if (_sourceCursor >= _frontier.Count) { Complete = true; break; }
                _pendingTarget = _targetCursor++;
                int source = _frontier[_sourceCursor];
                _lastSources[_pendingTarget] = source;
                _forward = travel(source, _pendingTarget);
                used++;
            }
            else
            {
                int source = _frontier[_sourceCursor];
                bool reverse = travel(_pendingTarget, source);
                used++;
                if (_forward && reverse)
                {
                    _parents[_pendingTarget] = source;
                    _frontier.Add(_pendingTarget);
                }
                _pendingTarget = -1;
            }
        }
        return used;
    }

    /// <summary>Membership is not a lease: revalidate every used vertex and both directions now.</summary>
    internal bool ValidateChain(int node, Func<int, bool> current, Func<int, int, bool> travel)
    {
        if (!Reached(node) || !current(0)) return false;
        for (int hops = 0; node != 0 && hops < Count; hops++)
        {
            int parent = _parents[node];
            if (parent < 0 || parent == node || !current(node) || !current(parent)
                || !travel(parent, node) || !travel(node, parent)) return false;
            node = parent;
        }
        return node == 0;
    }

    /// <summary>
    /// Final preparation snapshot: at most two directed queries per tree edge.
    /// Discovery order is parent-before-child, so a failed parent prunes every
    /// descendant without pretending an old graph edge is still a live proof.
    /// </summary>
    internal bool[] VerifiedSnapshot(Func<int, bool> current, Func<int, int, bool> travel)
    {
        var verified = new bool[Count];
        if (!Complete || !current(0)) return verified;
        verified[0] = true;
        foreach (int node in _frontier)
        {
            if (node == 0) continue;
            int parent = _parents[node];
            verified[node] = verified[parent] && current(parent) && current(node)
                && travel(parent, node) && travel(node, parent);
        }
        return verified;
    }
}
