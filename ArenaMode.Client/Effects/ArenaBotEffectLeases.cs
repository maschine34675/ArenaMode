using System;
using System.Collections.Generic;

namespace ArenaMode.Effects;

[Flags]
internal enum ArenaBotEffectKind { None = 0, Spawn = 1, Shield = 2, DoubleDamage = 4 }

/// <summary>Reference-keyed presentation ownership; this never grants a gameplay effect.</summary>
internal sealed class ArenaBotEffectLeases<T> where T : class
{
    internal sealed class Entry
    {
        internal T Body;
        internal readonly List<Lease> Leases = new();
        internal Entry(T body) => Body = body;
        internal ArenaBotEffectKind Read(int frame)
        {
            var result = ArenaBotEffectKind.None;
            foreach (var lease in Leases)
                if ((long)frame - lease.Frame >= 0 && (long)frame - lease.Frame <= 2) result |= lease.Kind;
            return result;
        }
    }

    internal sealed class Lease : IDisposable
    {
        private ArenaBotEffectLeases<T> _owner;
        internal Entry Entry;
        internal ArenaBotEffectKind Kind;
        internal int Frame = int.MinValue;
        internal Lease(ArenaBotEffectLeases<T> owner, Entry entry) { _owner = owner; Entry = entry; }
        internal void Set(ArenaBotEffectKind kind, int frame)
        {
            if (_owner == null) return;
            Kind = kind & (ArenaBotEffectKind.Spawn | ArenaBotEffectKind.Shield | ArenaBotEffectKind.DoubleDamage);
            Frame = frame;
        }
        public void Dispose()
        {
            if (_owner == null) return;
            var owner = _owner; _owner = null;
            Entry.Leases.Remove(this);
            if (Entry.Leases.Count == 0) { owner.Entries.Remove(Entry); Entry.Body = null; }
            Entry = null; Kind = ArenaBotEffectKind.None;
        }
    }

    internal readonly List<Entry> Entries = new();
    internal Lease Add(T body)
    {
        if (body == null) throw new ArgumentNullException(nameof(body));
        Entry entry = null;
        foreach (var candidate in Entries)
            if (ReferenceEquals(candidate.Body, body)) { entry = candidate; break; }
        if (entry == null) { entry = new Entry(body); Entries.Add(entry); }
        var lease = new Lease(this, entry); entry.Leases.Add(lease); return lease;
    }
    internal void Retire(Entry entry)
    {
        for (int i = entry.Leases.Count - 1; i >= 0; i--) entry.Leases[i].Dispose();
    }
    internal void Clear()
    {
        for (int i = Entries.Count - 1; i >= 0; i--) Retire(Entries[i]);
    }
}
