using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ArenaMode.Bots;

/// <summary>
/// The pure half of <c>ArenaBackupProfileDuplicatePatch</c>: which of the bot profiles a backup load
/// hands to the pool the pool does not hold yet.
///
/// Identity is the object reference and nothing else. The game's duplicate is the SAME profile object
/// stored a second time (see the patch), so a reference comparison finds exactly that and can never
/// mistake a freshly generated profile for one, whatever its content or an Equals override says.
///
/// No EFT/Unity types, so ArenaMode.Tests links this file directly.
/// </summary>
internal static class BackupProfileDuplicatePolicy
{
    /// <summary>
    /// The entries of <paramref name="reloaded"/> that <paramref name="pool"/> does not hold, in their
    /// order; <paramref name="duplicates"/> counts the ones left out. A reference that occurs twice in
    /// <paramref name="reloaded"/> itself is kept once, for the same reason: a pool entry is one use.
    /// Null entries pass through uncounted (the native add would store them too; nothing here decides
    /// about them). Neither argument is changed.
    /// </summary>
    internal static List<T> NotYetPooled<T>(IReadOnlyList<T> reloaded, IReadOnlyList<T> pool, out int duplicates)
        where T : class
    {
        duplicates = 0;
        var fresh = new List<T>(reloaded?.Count ?? 0);
        if (reloaded == null)
            return fresh;

        var held = new HashSet<T>(ReferenceComparer<T>.Instance);
        if (pool != null)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null)
                    held.Add(pool[i]);
            }
        }

        for (int i = 0; i < reloaded.Count; i++)
        {
            T entry = reloaded[i];
            if (entry == null)
            {
                fresh.Add(null);
                continue;
            }

            if (held.Add(entry))
                fresh.Add(entry);
            else
                duplicates++;
        }

        return fresh;
    }

    private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        internal static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();

        public bool Equals(T x, T y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
