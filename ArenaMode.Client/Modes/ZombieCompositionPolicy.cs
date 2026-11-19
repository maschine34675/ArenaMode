using ArenaMode.Shared;

namespace ArenaMode.Modes;

/// <summary>
/// Wave-dependent share among melee infected only. The user's shooter chance,
/// role weights and total population stay independent of this bounded rhythm.
/// Native easy/normal select walking/running brains; no health/damage scaling.
/// <see cref="SlowPercent(int)"/> is the rhythm itself; the frozen difficulty step decides
/// whether the horde keeps it (Reduced) or fades it toward all-fast (Maximum,
/// <see cref="SlowPercent(int, ArenaDifficultyStep)"/>).
/// </summary>
internal static class ZombieCompositionPolicy
{
    /// <summary>The slow share of the shared rhythm, before the difficulty step's fade.</summary>
    internal static int SlowPercent(int waveNumber)
    {
        if (waveNumber <= 1) return 75;
        if (waveNumber == 2) return 50;
        if (waveNumber == 3) return 25;
        return ((waveNumber - 4) % 3) switch
        {
            0 => 40,
            1 => 20,
            _ => 30
        };
    }

    /// <summary>The slow share a draw of this wave uses at the frozen step.</summary>
    internal static int SlowPercent(int waveNumber, ArenaDifficultyStep step)
        => ArenaDifficultyPolicy.ZombieSlowPercent(step, SlowPercent(waveNumber), waveNumber);
}
