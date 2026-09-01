namespace ArenaMode.Modes;

internal enum TeamOpponentActivationDecision
{
    Pending,
    Ready,
    Invalid,
}

/// <summary>
/// Classifies the small lifecycle window between EFT reporting a spawned bot
/// and that bot being safe to admit into a live protected team round.
/// </summary>
internal static class TeamOpponentActivationPolicy
{
    public static TeamOpponentActivationDecision Evaluate(
        bool playerPresent,
        bool healthReadable,
        bool alive,
        bool isPreActive,
        bool isActive)
    {
        if (!playerPresent || !healthReadable || !alive)
            return TeamOpponentActivationDecision.Invalid;

        if (isActive)
            return TeamOpponentActivationDecision.Ready;

        return isPreActive
            ? TeamOpponentActivationDecision.Pending
            : TeamOpponentActivationDecision.Invalid;
    }
}
