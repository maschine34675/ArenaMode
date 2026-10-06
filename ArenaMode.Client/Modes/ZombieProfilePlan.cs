using System;
using System.Collections.Generic;
using System.Linq;

namespace ArenaMode.Modes;

/// <summary>
/// Pure role/difficulty/count tuples for the native zombie profile pipeline.
///
/// <see cref="OpeningProfiles"/> is the small opening-wave list that replaces
/// BotHalloweenWithZombies.GetProfilesOnStart's native 498-profile preload
/// (decompile-verified; see the orchestrator's decision 3c — Stage C wires
/// the actual Harmony prefix that returns this as __result).
/// <see cref="BackupTargets"/> is what ZombieMode asks the native backup
/// loader to keep warm, in place of that same native method's
/// AddBackupProfiles call (decision 3d), floored to
/// <see cref="MinimumBackupCount"/> because the backup loader only tops up
/// once its deficit exceeds 3 (BotProfileBackuploader.cs:144,
/// decompile-verified) — a target at or below that count would silently
/// never trigger a top-up.
///
/// Both use the same deterministic apportionment as ZombieRoleMix's weight
/// table, not a live random draw: a preload/backup plan only needs to be a
/// representative sample of what the role mix can produce, and determinism
/// keeps it testable without a seed. (The actual per-spawn draws ZombieMode
/// makes once the raid is running use ZombieRoleMix.Next with a live Random,
/// and are not required to replay this exact list — the native preload runs
/// during loading, before that Random even exists.)
///
/// No EFT/Unity types, so ArenaMode.Tests can link this file directly (see
/// ArenaMode.Tests.csproj's ClientRules Compile Include links).
/// </summary>
internal static class ZombieProfilePlan
{
    internal readonly struct Entry
    {
        internal Entry(string role, string difficulty, int count)
        {
            Role = role;
            Difficulty = difficulty;
            Count = count;
        }

        internal string Role { get; }
        internal string Difficulty { get; }
        internal int Count { get; }
    }

    internal const int MinimumBackupCount = 4;

    /// <summary>
    /// The opening wave's roles at their chosen difficulties, collapsed to one
    /// entry per distinct (role, difficulty) pair so the native preload warms
    /// exactly the opening wave's composition and nothing more.
    /// </summary>
    internal static List<Entry> OpeningProfiles(int waveStartSize, int shooterPercent)
    {
        waveStartSize = Math.Max(0, waveStartSize);
        shooterPercent = Math.Clamp(shooterPercent, 0, 100);
        var entries = new List<Entry>();
        if (waveStartSize == 0)
            return entries;

        foreach (var (role, count) in Apportion(waveStartSize, ZombieRoleMix.Weights))
        {
            if (count <= 0)
                continue;

            int hardCount = shooterPercent <= 0 ? 0
                : shooterPercent >= 100 ? count
                : (int)Math.Round(count * (shooterPercent / 100.0), MidpointRounding.AwayFromZero);
            hardCount = Math.Clamp(hardCount, 0, count);
            int normalCount = count - hardCount;

            if (normalCount > 0) entries.Add(new Entry(role, ZombieRoleMix.Normal, normalCount));
            if (hardCount > 0) entries.Add(new Entry(role, ZombieRoleMix.Hard, hardCount));
        }

        return entries;
    }

    /// <summary>
    /// One backup target per (role, difficulty) pair the role mix can draw —
    /// every melee role always, plus every role's hard/shooter variant when
    /// ZombieShooterPercent &gt; 0 — each floored to MinimumBackupCount.
    /// </summary>
    internal static List<Entry> BackupTargets(int shooterPercent, int countPerPair = MinimumBackupCount)
    {
        int count = Math.Max(MinimumBackupCount, countPerPair);
        var entries = new List<Entry>();
        foreach (var (role, _) in ZombieRoleMix.Weights)
        {
            entries.Add(new Entry(role, ZombieRoleMix.Normal, count));
            if (shooterPercent > 0)
                entries.Add(new Entry(role, ZombieRoleMix.Hard, count));
        }

        return entries;
    }

    /// <summary>
    /// Largest-remainder apportionment: splits <paramref name="total"/> across
    /// the weighted roles so the counts sum back to exactly total, breaking
    /// ties by original weight order for determinism.
    /// </summary>
    private static List<(string Role, int Count)> Apportion(int total, (string Role, int Weight)[] weights)
    {
        int totalWeight = weights.Sum(w => w.Weight);
        var shares = new (string Role, int Floor, double Remainder)[weights.Length];
        int assigned = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            double exact = (double)total * weights[i].Weight / totalWeight;
            int floor = (int)Math.Floor(exact);
            shares[i] = (weights[i].Role, floor, exact - floor);
            assigned += floor;
        }

        var result = shares.Select(s => (s.Role, Count: s.Floor)).ToList();
        int remainder = total - assigned;
        var order = Enumerable.Range(0, shares.Length)
            .OrderByDescending(i => shares[i].Remainder)
            .ToList();
        for (int i = 0; i < remainder && i < order.Count; i++)
        {
            int index = order[i];
            result[index] = (result[index].Role, result[index].Count + 1);
        }

        return result;
    }
}
