using System;

namespace ArenaMode.Shared;

/// <summary>Per-request clock, never profile/config state or a server wall-clock timer.</summary>
public sealed class ArenaDifficultyClock
{
    public const string WireKey = "arenaMatchClock";
    public const int CurrentVersion = 1;
    public int Version { get; set; }
    public string ArmToken { get; set; } = string.Empty;
    public int TimeLimitSeconds { get; set; }
    public double PlayingElapsedSeconds { get; set; }

    public bool IsValid() => Version == CurrentVersion && !string.IsNullOrWhiteSpace(ArmToken)
        && TimeLimitSeconds > 0 && TimeLimitSeconds <= 7200
        && !double.IsNaN(PlayingElapsedSeconds) && !double.IsInfinity(PlayingElapsedSeconds)
        && PlayingElapsedSeconds >= 0 && PlayingElapsedSeconds <= TimeLimitSeconds;

    /// <summary>Last fifth of a finite match stays at peak; unlimited raids keep their legacy ramp.</summary>
    public static double Progress(double elapsedSeconds, int timeLimitSeconds, double legacyRampSeconds)
    {
        if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds <= 0) return 0;
        double ramp = timeLimitSeconds > 0 ? timeLimitSeconds * 0.8d
            : !double.IsNaN(legacyRampSeconds) && !double.IsInfinity(legacyRampSeconds) ? Math.Max(60, legacyRampSeconds) : 60;
        return Math.Min(1, elapsedSeconds / ramp);
    }
}
