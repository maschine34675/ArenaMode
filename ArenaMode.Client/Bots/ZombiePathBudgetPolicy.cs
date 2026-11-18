using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ArenaMode.Bots;

/// <summary>
/// Fair admission of whole native planning jobs, not NavMesh calls or elapsed
/// milliseconds. A native job may contain zigzag searches and several fallbacks.
/// The queue and retained identities are bounded; no per-frame lists are built.
/// </summary>
internal sealed class ZombiePathBudgetPolicy
{
    internal const int JobsPerFrame = 2;
    internal const int MaximumEntries = 128;
    internal const int MissedFrames = 2;
    private sealed class IdentityComparer : IEqualityComparer<object>
    {
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
    private sealed class Entry
    {
        internal object Body, Target;
        internal int RequestedFrame, GrantedFrame = int.MinValue, QueuedFrame;
        internal readonly LinkedListNode<Entry> Node;
        internal Entry() { Node = new LinkedListNode<Entry>(this); }
    }
    private readonly Dictionary<object, Entry> _byBody = new Dictionary<object, Entry>(MaximumEntries, new IdentityComparer());
    private readonly List<Entry> _entries = new List<Entry>(MaximumEntries);
    private readonly LinkedList<Entry> _pending = new LinkedList<Entry>();
    private int _frame = int.MinValue;
    internal int Frame => _frame;
    internal int FrameJobs { get; private set; }
    internal int PeakJobs { get; private set; }
    internal int PeakPending { get; private set; }
    internal int Pending => _pending.Count;
    internal int Retained => _entries.Count;
    internal int OldestWaitFrames => _pending.First == null ? 0 : Math.Max(0, _frame - _pending.First.Value.QueuedFrame);

    internal void Reset()
    {
        _byBody.Clear(); _entries.Clear(); _pending.Clear();
        _frame = int.MinValue;
        FrameJobs = PeakJobs = PeakPending = 0;
    }

    internal bool TryAcquire(object body, object target, int frame, Func<object, object, bool> isCurrent)
    {
        if (body == null || target == null) return false;
        Advance(frame, isCurrent);
        if (!_byBody.TryGetValue(body, out var entry))
        {
            if (_entries.Count >= MaximumEntries) return false;
            entry = new Entry { Body = body, Target = target };
            _byBody.Add(body, entry); _entries.Add(entry);
        }
        if (!ReferenceEquals(entry.Target, target))
        {
            if (entry.Node.List != null) _pending.Remove(entry.Node);
            entry.Target = target;
        }
        entry.RequestedFrame = frame;
        // In particular, a throwing reverse copy cannot replay route work via
        // its fallback in the same frame. A permit is consumed, never cached.
        if (entry.GrantedFrame == frame) return false;
        if (entry.Node.List == null)
        {
            entry.QueuedFrame = frame;
            _pending.AddLast(entry.Node);
            PeakPending = Math.Max(PeakPending, _pending.Count);
        }
        if (FrameJobs >= JobsPerFrame || !ReferenceEquals(_pending.First.Value, entry)) return false;
        _pending.RemoveFirst();
        entry.GrantedFrame = frame;
        FrameJobs++;
        PeakJobs = Math.Max(PeakJobs, FrameJobs);
        return true;
    }

    private void Advance(int frame, Func<object, object, bool> isCurrent)
    {
        if (_frame == frame) return;
        if (frame < _frame) Reset();
        _frame = frame; FrameJobs = 0;
        // Active denied nodes ask every frame. A stopped/changed node may waste
        // at most two frames, then cannot hold up the queue. Validate once per
        // frame, not once for every caller in the horde.
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];
            bool current = false;
            if ((long)frame - entry.RequestedFrame <= MissedFrames)
            {
                // One disappearing native body must not poison every other
                // request while this bounded queue is pruned.
                try { current = isCurrent(entry.Body, entry.Target); }
                catch { current = false; }
            }
            if (current) continue;
            if (entry.Node.List != null) _pending.Remove(entry.Node);
            _byBody.Remove(entry.Body);
            _entries.RemoveAt(i);
        }
    }
}
