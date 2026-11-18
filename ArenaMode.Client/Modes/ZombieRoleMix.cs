using System;
using ArenaMode.Shared;

namespace ArenaMode.Modes;

/// <summary>
/// Deterministic role/difficulty draw for one zombie spawn slot.
///
/// Role and difficulty are plain strings, never EFT.WildSpawnType/BotDifficulty,
/// so this file carries no engine dependency and ArenaMode.Tests can link it
/// directly (see ArenaMode.Tests.csproj's ClientRules Compile Include links).
/// ZombieMode owns the one-time string -&gt; native enum mapping when it calls
/// the real spawn seam (ZombieSpawnService.TryRequest).
///
/// Weights are the orchestrator's decision: infectedAssault 50%, infectedPmc
/// 25%, infectedCivil 15%, infectedLaborant 10%. An INDEPENDENT configured
/// shooter chance selects hard (Shooting/PM pistol); remaining melee draws use
/// the wave's slow/fast mix from ZombieCompositionPolicy — see
/// InfectedLayersStrategy's native brain table (decompile-verified:
/// InfectedLayersStrategy.cs:149-158). easy is Slow, normal is Fast; impossible
/// throws in the brain constructor and is never drawn.
/// </summary>
internal static class ZombieRoleMix
{
    internal const string InfectedAssault = "infectedAssault";
    internal const string InfectedPmc = "infectedPmc";
    internal const string InfectedCivil = "infectedCivil";
    internal const string InfectedLaborant = "infectedLaborant";

    internal const string Easy = "easy";
    internal const string Normal = "normal";
    internal const string Hard = "hard";

    internal readonly struct Draw
    {
        internal Draw(string role, string difficulty)
        {
            Role = role;
            Difficulty = difficulty;
        }

        internal string Role { get; }
        internal string Difficulty { get; }
    }

    /// <summary>Role, weight (out of <see cref="TotalWeight"/>), in draw order. Also ZombieProfilePlan's basis.</summary>
    internal static readonly (string Role, int Weight)[] Weights =
    {
        (InfectedAssault, 50),
        (InfectedPmc, 25),
        (InfectedCivil, 15),
        (InfectedLaborant, 10),
    };

    /// <summary>The Weights column above must sum to this.</summary>
    internal const int TotalWeight = 100;

    /// <summary>
    /// One role + difficulty draw. <paramref name="shooterPercent"/> is the configured share;
    /// the frozen <paramref name="step"/> turns it into the wave's share (Maximum grows it,
    /// Reduced keeps it flat) and picks the wave's slow share. Deterministic for a given rng state: two Random instances
    /// constructed with the same seed and driven the same way produce the same sequence of draws.</summary>
    internal static Draw Next(Random rng, int shooterPercent, int waveNumber, ArenaDifficultyStep step)
    {
        if (rng == null)
            throw new ArgumentNullException(nameof(rng));

        string role = PickRole(rng.Next(TotalWeight));
        // rng.Next(100) is 0..99, so a 0% share never rolls true and a 100%
        // share always does — exact, not merely close, regardless of seed.
        bool hard = rng.Next(100) < ArenaDifficultyPolicy.ZombieShooterPercent(step, shooterPercent, waveNumber);
        if (hard) return new Draw(role, Hard);
        bool slow = rng.Next(100) < ZombieCompositionPolicy.SlowPercent(waveNumber, step);
        return new Draw(role, slow ? Easy : Normal);
    }

    private static string PickRole(int roll)
    {
        int cursor = 0;
        foreach (var (role, weight) in Weights)
        {
            cursor += weight;
            if (roll < cursor)
                return role;
        }

        // Unreachable while Weights sums to TotalWeight and roll is 0..TotalWeight-1.
        return Weights[Weights.Length - 1].Role;
    }
}
