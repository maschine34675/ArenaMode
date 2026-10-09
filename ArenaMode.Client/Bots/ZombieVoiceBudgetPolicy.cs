namespace ArenaMode.Bots;

/// <summary>Only repetitive atmosphere competes for slots; no hearing/AI event is filtered.</summary>
internal static class ZombieVoiceBudgetPolicy
{
    internal const int MaximumVoices = 6;
    internal const int DistantVoices = 4;
    internal const float NearbyDistanceSquared = 20f * 20f;
    internal const float ThreatDistanceSquared = 8f * 8f;

    internal static bool IsThreatExempt(bool attackPhrase, float distanceSquared)
        => attackPhrase && distanceSquared <= ThreatDistanceSquared;

    internal static bool HasRoom(int active, int distant, bool candidateDistant)
        => active < MaximumVoices && (!candidateDistant || distant < DistantVoices);
}
