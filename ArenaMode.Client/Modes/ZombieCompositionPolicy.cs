namespace ArenaMode.Modes;

/// <summary>
/// Wave-dependent share among melee infected only. The user's shooter chance,
/// role weights and total population stay independent of this bounded rhythm.
/// Native easy/normal select walking/running brains; no health/damage scaling.
/// </summary>
internal static class ZombieCompositionPolicy
{
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
}
