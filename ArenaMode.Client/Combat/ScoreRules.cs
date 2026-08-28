namespace ArenaMode.Combat;

/// <summary>Role buckets an arena victim can fall into (coarse, score-relevant).</summary>
public enum ArenaRole
{
    Scav,
    Raider,
    Pmc,
    Boss,
    Follower,
    Other,
}

/// <summary>
/// v1 scoring: flat per-role values plus headshot and streak bonuses.
/// Win conditions always count RAW KILLS — points are presentation/flavor,
/// so tuning them never changes mode pacing.
/// </summary>
public static class ScoreRules
{
    public const int HeadshotBonus = 50;
    public const int StreakBonus = 100;
    public const int StreakBonusEvery = 5;

    /// <summary>Kills within this window chain into a multi-kill (Double, Triple…).</summary>
    public const float MultiKillWindowSeconds = 4f;

    public static int PointsFor(ArenaRole role)
    {
        switch (role)
        {
            case ArenaRole.Boss:
                return 300;
            case ArenaRole.Pmc:
                return 200;
            case ArenaRole.Raider:
            case ArenaRole.Follower:
                return 150;
            default:
                return 100;
        }
    }

    public static string MultiKillLabel(int chain)
    {
        switch (chain)
        {
            case 2: return "DOUBLE KILL";
            case 3: return "TRIPLE KILL";
            case 4: return "MULTI KILL";
            default: return chain >= 5 ? "MONSTER KILL" : null;
        }
    }

    public static string StreakLabel(int streak)
    {
        switch (streak)
        {
            case 5: return "KILLING SPREE";
            case 10: return "RAMPAGE";
            case 15: return "DOMINATING";
            case 20: return "UNSTOPPABLE";
            default: return streak > 0 && streak % 10 == 5 && streak > 20 ? "GODLIKE" : null;
        }
    }
}
