using System;
using System.Collections.Generic;
using System.Linq;

namespace ArenaMode.Compat;

internal enum PitFireTeamRoundLifecycleStage
{
    Unconfigured,
    ContractReady,
    TearingDown,
    AwaitingRetirement,
    ReadyForLeader,
    LeaderReady,
    PreparingProfiles,
    ProfilesReady,
    SpawnRequested,
    FollowersReady,
    Failed
}

internal enum PitFireTeamRoundPollDecision
{
    Pending,
    Ready,
    Failed
}

internal sealed class PitFireTeamRoundCapability
{
    private PitFireTeamRoundCapability(
        bool contractCompatible,
        bool safeWrapperTeardown,
        bool safeProfilePrefetch,
        bool freshRuntimeIdentityRemap,
        string reason)
    {
        ContractCompatible = contractCompatible;
        SafeWrapperTeardown = safeWrapperTeardown;
        SafeProfilePrefetch = safeProfilePrefetch;
        FreshRuntimeIdentityRemap = freshRuntimeIdentityRemap;
        Reason = reason ?? string.Empty;
    }

    public bool ContractCompatible { get; }
    public bool SafeWrapperTeardown { get; }
    public bool SafeProfilePrefetch { get; }
    public bool FreshRuntimeIdentityRemap { get; }
    public bool CanStartAdditionalRound
        => ContractCompatible
           && SafeWrapperTeardown
           && SafeProfilePrefetch
           && FreshRuntimeIdentityRemap;
    public bool RequiresPersistenceSuppression => true;
    public string Reason { get; }

    public static PitFireTeamRoundCapability Create(
        bool contractCompatible,
        bool safeWrapperTeardown,
        bool safeProfilePrefetch,
        bool freshRuntimeIdentityRemap,
        string reason = "")
        => new(
            contractCompatible,
            safeWrapperTeardown,
            safeProfilePrefetch,
            freshRuntimeIdentityRemap,
            reason);

    public static PitFireTeamRoundCapability Available()
        => Create(true, true, true, true);

    public static PitFireTeamRoundCapability Unavailable(string reason)
        => Create(
            false,
            false,
            false,
            false,
            string.IsNullOrWhiteSpace(reason)
                ? "PitFireTeam round lifecycle is unavailable."
                : reason);
}

internal sealed class PitFireTeamRoundPolicyDecision
{
    private PitFireTeamRoundPolicyDecision(
        PitFireTeamRoundPollDecision decision,
        string reason)
    {
        Decision = decision;
        Reason = reason ?? string.Empty;
    }

    public PitFireTeamRoundPollDecision Decision { get; }
    public string Reason { get; }

    public static PitFireTeamRoundPolicyDecision Pending(string reason)
        => new(PitFireTeamRoundPollDecision.Pending, reason);

    public static PitFireTeamRoundPolicyDecision Ready()
        => new(PitFireTeamRoundPollDecision.Ready, string.Empty);

    public static PitFireTeamRoundPolicyDecision Failed(string reason)
        => new(PitFireTeamRoundPollDecision.Failed, reason);
}

internal sealed class PitFireTeamRetirementObservation
{
    public bool ReflectionFailed { get; set; }
    public string ReflectionProblem { get; set; }
    public bool OldBossPresent { get; set; }
    public int OldWrapperCount { get; set; }
    public int ActiveOldBodyCount { get; set; }
    public int RegisteredOldBodyCount { get; set; }
    public bool TimedOut { get; set; }
}

internal sealed class PitFireTeamFollowerObservation
{
    public int ExpectedCount { get; set; }
    public int WrapperCount { get; set; }
    public int ActiveBodyCount { get; set; }
    public int RegisteredBodyCount { get; set; }
    public int DistinctAccountCount { get; set; }
    public int DistinctProfileCount { get; set; }
    public bool InvalidBinding { get; set; }
    public string InvalidBindingProblem { get; set; }
    public bool SpawnFaulted { get; set; }
    public string SpawnProblem { get; set; }
    public int StablePasses { get; set; }
    public bool TimedOut { get; set; }
}

/// <summary>
/// Pure fail-closed decisions shared by the live reflection adapter and its
/// offline harness. No EFT or PitFireTeam type appears here.
/// </summary>
internal static class PitFireTeamRoundLifecyclePolicy
{
    public const int RequiredCompanionCount = 4;
    public const int RequiredStablePasses = 2;

    public static bool TryValidateCanonicalRoster(
        IReadOnlyDictionary<string, string> profilesByAccountId,
        out string reason)
    {
        reason = string.Empty;
        if (profilesByAccountId == null)
        {
            reason = "the authenticated companion roster is missing";
            return false;
        }

        if (profilesByAccountId.Count != RequiredCompanionCount)
        {
            reason = $"expected exactly {RequiredCompanionCount} authenticated companions, found {profilesByAccountId.Count}";
            return false;
        }

        HashSet<string> accounts = new(StringComparer.Ordinal);
        HashSet<string> profiles = new(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in profilesByAccountId)
        {
            string accountId = pair.Key?.Trim();
            string profileId = pair.Value?.Trim();
            if (string.IsNullOrEmpty(accountId) || !accounts.Add(accountId))
            {
                reason = "the authenticated companion roster contains a missing or duplicate account ID";
                return false;
            }

            if (!IsMongoId(profileId) || !profiles.Add(profileId))
            {
                reason = "the authenticated companion roster contains a missing, invalid or duplicate canonical profile ID";
                return false;
            }
        }

        return true;
    }

    public static PitFireTeamRoundPolicyDecision EvaluateRetirement(
        PitFireTeamRetirementObservation observation)
    {
        if (observation == null)
            return PitFireTeamRoundPolicyDecision.Failed("retirement state is missing");

        if (observation.ReflectionFailed)
        {
            return PitFireTeamRoundPolicyDecision.Failed(
                string.IsNullOrWhiteSpace(observation.ReflectionProblem)
                    ? "PitFireTeam retirement inspection failed"
                    : observation.ReflectionProblem);
        }

        if (observation.OldBossPresent || observation.OldWrapperCount != 0)
        {
            return PitFireTeamRoundPolicyDecision.Failed(
                $"old PitFireTeam ownership survived teardown (boss={observation.OldBossPresent}, wrappers={observation.OldWrapperCount})");
        }

        if (observation.ActiveOldBodyCount < 0 || observation.RegisteredOldBodyCount < 0)
            return PitFireTeamRoundPolicyDecision.Failed("retirement counters are invalid");

        if (observation.ActiveOldBodyCount == 0 && observation.RegisteredOldBodyCount == 0)
            return PitFireTeamRoundPolicyDecision.Ready();

        if (observation.TimedOut)
        {
            return PitFireTeamRoundPolicyDecision.Failed(
                $"old follower retirement timed out (active={observation.ActiveOldBodyCount}, registered={observation.RegisteredOldBodyCount})");
        }

        return PitFireTeamRoundPolicyDecision.Pending(
            $"waiting for old follower retirement (active={observation.ActiveOldBodyCount}, registered={observation.RegisteredOldBodyCount})");
    }

    public static PitFireTeamRoundPolicyDecision EvaluateFollowers(
        PitFireTeamFollowerObservation observation)
    {
        if (observation == null)
            return PitFireTeamRoundPolicyDecision.Failed("follower state is missing");

        if (observation.ExpectedCount != RequiredCompanionCount)
            return PitFireTeamRoundPolicyDecision.Failed("the expected follower count is not the protected four-companion contract");

        if (observation.SpawnFaulted)
        {
            return PitFireTeamRoundPolicyDecision.Failed(
                string.IsNullOrWhiteSpace(observation.SpawnProblem)
                    ? "PitFireTeam follower spawning faulted"
                    : observation.SpawnProblem);
        }

        if (observation.InvalidBinding)
        {
            return PitFireTeamRoundPolicyDecision.Failed(
                string.IsNullOrWhiteSpace(observation.InvalidBindingProblem)
                    ? "PitFireTeam returned an invalid follower binding"
                    : observation.InvalidBindingProblem);
        }

        int expected = observation.ExpectedCount;
        if (observation.WrapperCount > expected
            || observation.ActiveBodyCount > expected
            || observation.RegisteredBodyCount > expected
            || observation.DistinctAccountCount > expected
            || observation.DistinctProfileCount > expected)
        {
            return PitFireTeamRoundPolicyDecision.Failed("PitFireTeam produced more than four current-round followers");
        }

        bool complete = observation.WrapperCount == expected
                        && observation.ActiveBodyCount == expected
                        && observation.RegisteredBodyCount == expected
                        && observation.DistinctAccountCount == expected
                        && observation.DistinctProfileCount == expected;
        if (!complete)
        {
            if (observation.TimedOut)
            {
                return PitFireTeamRoundPolicyDecision.Failed(
                    $"follower assembly timed out (wrappers={observation.WrapperCount}, active={observation.ActiveBodyCount}, registered={observation.RegisteredBodyCount})");
            }

            return PitFireTeamRoundPolicyDecision.Pending(
                $"waiting for four followers (wrappers={observation.WrapperCount}, active={observation.ActiveBodyCount}, registered={observation.RegisteredBodyCount})");
        }

        if (observation.StablePasses < RequiredStablePasses)
            return PitFireTeamRoundPolicyDecision.Pending("complete follower roster is awaiting its second stable registry pass");

        return PitFireTeamRoundPolicyDecision.Ready();
    }

    public static bool TryValidateRuntimeIdentityRemap(
        string selectedAccountId,
        string canonicalProfileId,
        string returnedAccountId,
        string returnedProfileId,
        string runtimeAccountId,
        string runtimeProfileId,
        IReadOnlyCollection<string> originalItemIds,
        IReadOnlyCollection<string> runtimeItemIds,
        IReadOnlyCollection<string> historicalRuntimeProfileIds,
        IReadOnlyCollection<string> historicalRuntimeItemIds,
        out string reason)
    {
        reason = string.Empty;
        string account = selectedAccountId?.Trim();
        string canonical = canonicalProfileId?.Trim();
        string returnedAccount = returnedAccountId?.Trim();
        string returnedProfile = returnedProfileId?.Trim();
        string runtimeAccount = runtimeAccountId?.Trim();
        string runtimeProfile = runtimeProfileId?.Trim();

        if (string.IsNullOrEmpty(account)
            || !IsMongoId(canonical)
            || !string.Equals(account, returnedAccount, StringComparison.Ordinal)
            || !string.Equals(canonical, returnedProfile, StringComparison.OrdinalIgnoreCase))
        {
            reason = "the prefetched profile does not match its authenticated account/canonical-profile pair";
            return false;
        }

        if (!string.Equals(account, runtimeAccount, StringComparison.Ordinal)
            || !IsMongoId(runtimeProfile)
            || string.Equals(runtimeProfile, canonical, StringComparison.OrdinalIgnoreCase))
        {
            reason = "the runtime clone did not preserve the account while replacing the canonical profile ID";
            return false;
        }

        if (historicalRuntimeProfileIds != null
            && historicalRuntimeProfileIds.Contains(runtimeProfile, StringComparer.OrdinalIgnoreCase))
        {
            reason = "the runtime profile ID was already used by an earlier round";
            return false;
        }

        if (!TryReadUniqueIds(originalItemIds, out var originals)
            || !TryReadUniqueIds(runtimeItemIds, out var runtimeItems))
        {
            reason = "the source or runtime equipment contains a missing or duplicate item ID";
            return false;
        }

        if (originals.Count == 0 || runtimeItems.Count != originals.Count)
        {
            reason = "the runtime equipment clone is empty or changed the equipment item count";
            return false;
        }

        if (runtimeItems.Overlaps(originals))
        {
            reason = "the runtime equipment clone reused an item ID from the canonical profile";
            return false;
        }

        if (historicalRuntimeItemIds != null
            && runtimeItems.Overlaps(historicalRuntimeItemIds.Where(id => !string.IsNullOrWhiteSpace(id))))
        {
            reason = "the runtime equipment clone reused an item ID from an earlier round";
            return false;
        }

        return true;
    }

    private static bool TryReadUniqueIds(
        IReadOnlyCollection<string> values,
        out HashSet<string> identities)
    {
        identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (values == null)
            return false;

        foreach (string value in values)
        {
            string identity = value?.Trim();
            if (string.IsNullOrEmpty(identity) || !identities.Add(identity))
                return false;
        }

        return true;
    }

    private static bool IsMongoId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 24)
            return false;

        foreach (char character in value)
        {
            if (!Uri.IsHexDigit(character))
                return false;
        }

        return true;
    }
}
