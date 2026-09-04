#nullable disable

using System;
using System.Collections.Generic;

namespace ArenaMode.Combat;

/// <summary>
/// Marks a profile death as mod-owned retirement rather than combat. EFT can
/// still report the body's retained LastAggressor when Kill(Undefined) runs;
/// this profile-scoped gate prevents that cleanup from becoming a real score.
/// </summary>
internal static class ArenaAdministrativeDeathGate
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, int> ActiveProfiles =
        new(StringComparer.Ordinal);
    private static int _generation;

    internal static IDisposable Enter(string profileId)
    {
        string normalized = Normalize(profileId);
        if (normalized == null)
            return EmptyLease.Instance;

        int generation;
        lock (Sync)
        {
            ActiveProfiles.TryGetValue(normalized, out int depth);
            ActiveProfiles[normalized] = depth + 1;
            generation = _generation;
        }

        return new Lease(normalized, generation);
    }

    internal static bool IsActive(string profileId)
    {
        string normalized = Normalize(profileId);
        if (normalized == null)
            return false;

        lock (Sync)
            return ActiveProfiles.ContainsKey(normalized);
    }

    internal static void Reset()
    {
        lock (Sync)
        {
            ActiveProfiles.Clear();
            _generation++;
        }
    }

    private static string Normalize(string value)
    {
        string normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private sealed class Lease : IDisposable
    {
        private string _profileId;
        private readonly int _generation;

        internal Lease(string profileId, int generation)
        {
            _profileId = profileId;
            _generation = generation;
        }

        public void Dispose()
        {
            string profileId = _profileId;
            if (profileId == null)
                return;

            _profileId = null;
            lock (Sync)
            {
                // Reset starts a new raid/generation. A late Dispose from an
                // earlier one must never decrement a new scope reusing the ID.
                if (_generation != ArenaAdministrativeDeathGate._generation)
                    return;

                if (!ActiveProfiles.TryGetValue(profileId, out int depth))
                    return;

                if (depth <= 1)
                    ActiveProfiles.Remove(profileId);
                else
                    ActiveProfiles[profileId] = depth - 1;
            }
        }
    }

    private sealed class EmptyLease : IDisposable
    {
        internal static readonly EmptyLease Instance = new();
        public void Dispose()
        {
        }
    }
}
