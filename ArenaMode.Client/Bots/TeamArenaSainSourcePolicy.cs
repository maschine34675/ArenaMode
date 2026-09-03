namespace ArenaMode.Bots;

internal enum TeamArenaSainSourceSelection
{
    NotPresent,
    RegisteredExact,
    AttachedExact,
    Incompatible
}

/// <summary>
/// Selects a SAIN source wrapper without allowing an attached component to
/// conceal stale profile-registry state used by SAIN's Harmony patches.
/// </summary>
internal static class TeamArenaSainSourcePolicy
{
    internal static TeamArenaSainSourceSelection Select(
        bool registryFound,
        bool registryReturnedWrapper,
        bool registryWrapperExact,
        bool attachedWrapperPresent,
        bool attachedWrapperExact,
        bool registeredAndAttachedSame)
    {
        if (registryFound)
        {
            if (!registryReturnedWrapper
                || !registryWrapperExact
                || (attachedWrapperPresent
                    && (!attachedWrapperExact || !registeredAndAttachedSame)))
            {
                return TeamArenaSainSourceSelection.Incompatible;
            }

            return TeamArenaSainSourceSelection.RegisteredExact;
        }

        if (registryReturnedWrapper)
            return TeamArenaSainSourceSelection.Incompatible;

        if (!attachedWrapperPresent)
            return TeamArenaSainSourceSelection.NotPresent;

        return attachedWrapperExact
            ? TeamArenaSainSourceSelection.AttachedExact
            : TeamArenaSainSourceSelection.Incompatible;
    }
}
