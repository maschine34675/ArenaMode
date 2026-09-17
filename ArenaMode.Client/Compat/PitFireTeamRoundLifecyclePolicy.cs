using System;
using System.Collections;
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

internal enum PitFireTeamLeaderBindingDecision
{
    Create,
    Adopt,
    Failed
}

internal sealed class PitFireTeamLeaderBindingObservation
{
    public bool ProfileCacheWasCleanAtTeardown { get; set; }
    public bool ExistingBossPresent { get; set; }
    public bool IsExactReplacementBody { get; set; }
    public bool BossWrapsCurrentLeader { get; set; }
    public bool BossUsesCurrentController { get; set; }
    public int BossFollowerCount { get; set; }
    public int BossWrapperCount { get; set; }
    public bool BossGroupPresent { get; set; }
    public bool CachedProfileTaskPresent { get; set; }
    public bool CachedProfileTaskIsTask { get; set; }
}

internal sealed class PitFireTeamLeaderBindingPolicyDecision
{
    private PitFireTeamLeaderBindingPolicyDecision(
        PitFireTeamLeaderBindingDecision decision,
        string reason)
    {
        Decision = decision;
        Reason = reason ?? string.Empty;
    }

    public PitFireTeamLeaderBindingDecision Decision { get; }
    public string Reason { get; }

    public static PitFireTeamLeaderBindingPolicyDecision Create()
        => new(PitFireTeamLeaderBindingDecision.Create, string.Empty);

    public static PitFireTeamLeaderBindingPolicyDecision Adopt()
        => new(PitFireTeamLeaderBindingDecision.Adopt, string.Empty);

    public static PitFireTeamLeaderBindingPolicyDecision Failed(string reason)
        => new(PitFireTeamLeaderBindingDecision.Failed, reason);
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
    public bool ActivationLeaseComplete { get; set; }
    public bool PhysicalOwnershipComplete { get; set; }
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
    public string IncompleteDetail { get; set; }
}

internal enum PitFireTeamWeaponTakeRetryDecision
{
    /// <summary>Leave the body alone: healthy, published, or not yet due.</summary>
    Skip,

    /// <summary>A weapon change is still open. Report it once; never re-issue.</summary>
    ReportOpenChange,

    /// <summary>Re-issue the first weapon take.</summary>
    Retry
}

internal sealed class PitFireTeamWeaponTakeObservation
{
    public bool GraceElapsed { get; set; }
    public bool WrapperPublished { get; set; }
    public bool LeasedProfile { get; set; }
    public bool BodyPresent { get; set; }
    public bool Dead { get; set; }
    public bool PreActive { get; set; }
    public bool ProfileIdMatches { get; set; }
    public bool WeaponManagerPresent { get; set; }
    public bool SelectorPresent { get; set; }
    public bool WeaponReady { get; set; }
    public bool PlayerPresent { get; set; }
    public bool Alive { get; set; }
    public bool InventoryPresent { get; set; }
    public bool ChangingWeapon { get; set; }
    public int Attempts { get; set; }
    public bool AttemptDue { get; set; }
}

/// <summary>
/// Whether Arena may re-issue a stuck follower's first weapon take.
///
/// Pure so the harness can drive every branch, because the live half cannot be:
/// it calls into EFT's bot weapon selector. The adapter observes the body and
/// applies this answer; it decides nothing itself.
/// </summary>
internal static class PitFireTeamWeaponTakeRetryPolicy
{
    public const int MaxAttempts = 3;

    public static PitFireTeamWeaponTakeRetryDecision Evaluate(
        PitFireTeamWeaponTakeObservation observation)
    {
        if (observation == null
            || !observation.GraceElapsed
            || observation.WrapperPublished
            || !observation.LeasedProfile
            || !observation.BodyPresent
            || observation.Dead
            || !observation.PreActive
            || !observation.ProfileIdMatches
            || !observation.WeaponManagerPresent
            || !observation.SelectorPresent
            // The one flag that releases PreActive. Set means nothing is stuck.
            || observation.WeaponReady
            || !observation.PlayerPresent
            || !observation.Alive
            || !observation.InventoryPresent)
        {
            return PitFireTeamWeaponTakeRetryDecision.Skip;
        }

        // SetItemInHands refuses while a change is open and answers the refusal by
        // invoking its callback with null, which faults inside native
        // OnWeaponTaken. Such a body is reported, never poked.
        if (observation.ChangingWeapon)
            return PitFireTeamWeaponTakeRetryDecision.ReportOpenChange;

        if (observation.Attempts >= MaxAttempts || !observation.AttemptDue)
            return PitFireTeamWeaponTakeRetryDecision.Skip;

        return PitFireTeamWeaponTakeRetryDecision.Retry;
    }
}

internal enum PitFireTeamFollowerDetachDecision
{
    Invalid,
    Native,
    Disposed
}

/// <summary>
/// Pure classification and exact-reference mutation for PFT wrapper teardown.
/// The live adapter proves EFT/PFT state before handing reflected collections
/// here; the offline harness executes the same registry mutation with fakes.
/// </summary>
internal static class PitFireTeamFollowerDetachPolicy
{
    public static PitFireTeamFollowerDetachDecision Classify(
        bool nativeFollowerAvailable,
        bool isDead,
        bool botStateActive,
        bool healthReadable,
        bool healthAlive)
    {
        if (nativeFollowerAvailable
            && !isDead
            && botStateActive
            && healthReadable
            && healthAlive)
        {
            return PitFireTeamFollowerDetachDecision.Native;
        }

        if (!nativeFollowerAvailable
            && isDead
            && !botStateActive
            && healthReadable
            && !healthAlive)
        {
            return PitFireTeamFollowerDetachDecision.Disposed;
        }

        return PitFireTeamFollowerDetachDecision.Invalid;
    }

    public static bool TryValidateRegistryEntry(
        IList followers,
        IDictionary followersByProfileId,
        object wrapper,
        string profileId,
        out string reason)
    {
        reason = string.Empty;
        if (followers == null
            || followersByProfileId == null
            || wrapper == null
            || string.IsNullOrWhiteSpace(profileId))
        {
            reason = "PFT follower registry validation received an incomplete wrapper identity";
            return false;
        }

        int exactWrapperCount = CountExactReferences(followers, wrapper);
        if (exactWrapperCount != 1)
        {
            reason = $"PFT _followers contains {exactWrapperCount} exact wrappers for {profileId}";
            return false;
        }

        if (!followersByProfileId.Contains(profileId)
            || !ReferenceEquals(followersByProfileId[profileId], wrapper))
        {
            reason = $"PFT _followersByProfileId does not map {profileId} to its exact wrapper";
            return false;
        }

        return true;
    }

    public static bool TryUnlinkDisposed(
        IList followers,
        IDictionary followersByProfileId,
        IList bossFollowers,
        object wrapper,
        object body,
        string profileId,
        out string reason)
    {
        if (!TryValidateRegistryEntry(
                followers,
                followersByProfileId,
                wrapper,
                profileId,
                out reason))
        {
            return false;
        }

        if (bossFollowers == null || body == null)
        {
            reason = "PFT disposed follower unlink received an incomplete boss/body binding";
            return false;
        }

        int exactBodyCount = CountExactReferences(bossFollowers, body);
        if (exactBodyCount > 1)
        {
            reason = $"PFT old boss contains {exactBodyCount} exact bodies for {profileId}";
            return false;
        }

        RemoveExactReferences(followers, wrapper);
        followersByProfileId.Remove(profileId);
        RemoveExactReferences(bossFollowers, body);

        if (CountExactReferences(followers, wrapper) != 0
            || followersByProfileId.Contains(profileId)
            || CountExactReferences(bossFollowers, body) != 0)
        {
            reason = $"PFT retained disposed follower ownership for {profileId}";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static int CountExactReferences(IList values, object expected)
    {
        int count = 0;
        foreach (object value in values)
        {
            if (ReferenceEquals(value, expected))
                count++;
        }

        return count;
    }

    private static void RemoveExactReferences(IList values, object expected)
    {
        for (int index = values.Count - 1; index >= 0; index--)
        {
            if (ReferenceEquals(values[index], expected))
                values.RemoveAt(index);
        }
    }
}

/// <summary>
/// Pure fail-closed decisions shared by the live reflection adapter and its
/// offline harness. No EFT or PitFireTeam type appears here.
/// </summary>
internal static class PitFireTeamRoundLifecyclePolicy
{
    public const int RequiredCompanionCount = 4;
    public const int RequiredStablePasses = 2;

    public static PitFireTeamLeaderBindingPolicyDecision EvaluateLeaderBinding(
        PitFireTeamLeaderBindingObservation observation)
    {
        if (observation == null)
            return PitFireTeamLeaderBindingPolicyDecision.Failed("leader binding state is missing");

        if (!observation.ProfileCacheWasCleanAtTeardown)
        {
            return PitFireTeamLeaderBindingPolicyDecision.Failed(
                "the completed round did not prove a clean follower-profile task cache");
        }

        if (observation.BossFollowerCount < 0 || observation.BossWrapperCount < 0)
            return PitFireTeamLeaderBindingPolicyDecision.Failed("leader ownership counters are invalid");

        if (!observation.ExistingBossPresent)
        {
            if (observation.CachedProfileTaskPresent)
            {
                return PitFireTeamLeaderBindingPolicyDecision.Failed(
                    "PFT exposed a follower-profile task without a current leader wrapper");
            }

            if (observation.BossWrapsCurrentLeader
                || observation.BossUsesCurrentController
                || observation.BossFollowerCount != 0
                || observation.BossWrapperCount != 0
                || observation.BossGroupPresent)
            {
                return PitFireTeamLeaderBindingPolicyDecision.Failed(
                    "PFT exposed contradictory ownership without a current leader wrapper");
            }

            return PitFireTeamLeaderBindingPolicyDecision.Create();
        }

        if (!observation.IsExactReplacementBody)
        {
            return PitFireTeamLeaderBindingPolicyDecision.Failed(
                "PFT still has a boss under the surviving or wrong local body");
        }

        if (!observation.BossWrapsCurrentLeader)
        {
            return PitFireTeamLeaderBindingPolicyDecision.Failed(
                "PFT's existing boss does not wrap the exact replacement local body");
        }

        if (!observation.BossUsesCurrentController)
        {
            return PitFireTeamLeaderBindingPolicyDecision.Failed(
                "PFT's existing boss does not use the current BotsController");
        }

        if (observation.BossFollowerCount != 0
            || observation.BossWrapperCount != 0
            || observation.BossGroupPresent)
        {
            return PitFireTeamLeaderBindingPolicyDecision.Failed(
                $"PFT's replacement boss already owns round state (followers={observation.BossFollowerCount}, wrappers={observation.BossWrapperCount}, group={observation.BossGroupPresent})");
        }

        if (observation.CachedProfileTaskPresent && !observation.CachedProfileTaskIsTask)
        {
            return PitFireTeamLeaderBindingPolicyDecision.Failed(
                "PFT's replacement follower-profile cache entry is not a Task");
        }

        return PitFireTeamLeaderBindingPolicyDecision.Adopt();
    }

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

        bool complete = observation.ActivationLeaseComplete
                        && observation.PhysicalOwnershipComplete
                        && observation.WrapperCount == expected
                        && observation.ActiveBodyCount == expected
                        && observation.RegisteredBodyCount == expected
                        && observation.DistinctAccountCount == expected
                        && observation.DistinctProfileCount == expected;
        if (!complete)
        {
            string incomplete = string.IsNullOrWhiteSpace(observation.IncompleteDetail)
                ? string.Empty
                : ": " + observation.IncompleteDetail.Trim();
            if (observation.TimedOut)
            {
                return PitFireTeamRoundPolicyDecision.Failed(
                    $"follower assembly timed out (activations={observation.ActivationLeaseComplete}, ownership={observation.PhysicalOwnershipComplete}, wrappers={observation.WrapperCount}, active={observation.ActiveBodyCount}, registered={observation.RegisteredBodyCount}){incomplete}");
            }

            return PitFireTeamRoundPolicyDecision.Pending(
                $"waiting for four followers (activations={observation.ActivationLeaseComplete}, ownership={observation.PhysicalOwnershipComplete}, wrappers={observation.WrapperCount}, active={observation.ActiveBodyCount}, registered={observation.RegisteredBodyCount}){incomplete}");
        }

        if (observation.StablePasses < RequiredStablePasses)
            return PitFireTeamRoundPolicyDecision.Pending("complete follower roster is awaiting its second stable registry pass");

        return PitFireTeamRoundPolicyDecision.Ready();
    }

    public static bool HasDuplicateReferences<T>(IReadOnlyList<T> values)
        where T : class
    {
        if (values == null)
            return false;

        for (int left = 0; left < values.Count; left++)
        {
            for (int right = left + 1; right < values.Count; right++)
            {
                if (ReferenceEquals(values[left], values[right]))
                    return true;
            }
        }

        return false;
    }

    public static bool HasExactActivationSeams(
        IEnumerable<string> expectedProfileIds,
        IEnumerable<string> startedProfileIds,
        IEnumerable<string> invocationProfileIds)
    {
        if (expectedProfileIds == null
            || startedProfileIds == null
            || invocationProfileIds == null)
        {
            return false;
        }

        List<string> expectedValues = expectedProfileIds.ToList();
        List<string> startedValues = startedProfileIds.ToList();
        List<string> invocationValues = invocationProfileIds.ToList();
        if (expectedValues.Count != RequiredCompanionCount
            || startedValues.Count != RequiredCompanionCount
            || invocationValues.Count != RequiredCompanionCount
            || expectedValues.Any(string.IsNullOrWhiteSpace)
            || startedValues.Any(string.IsNullOrWhiteSpace)
            || invocationValues.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        var expected = new HashSet<string>(expectedValues, StringComparer.OrdinalIgnoreCase);
        var started = new HashSet<string>(startedValues, StringComparer.OrdinalIgnoreCase);
        var invocations = new HashSet<string>(invocationValues, StringComparer.OrdinalIgnoreCase);

        return expected.Count == RequiredCompanionCount
               && started.Count == RequiredCompanionCount
               && invocations.Count == RequiredCompanionCount
               && expected.SetEquals(started)
               && expected.SetEquals(invocations);
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
