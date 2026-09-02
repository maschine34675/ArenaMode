using ArenaMode.Compat;

internal static class Program
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            CheckCanonicalRoster();
            CheckRetirementBarrier();
            CheckFreshIdentityRemap();
            CheckFollowerBarrier();
            CheckCapability();
            CheckSourceContract();
            Console.WriteLine($"PITFIRETEAM_ROUND_LIFECYCLE_CHECKS_PASSED ({_assertions} assertions)");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"PITFIRETEAM_ROUND_LIFECYCLE_CHECKS_FAILED after {_assertions} assertions: {exception}");
            return 1;
        }
    }

    private static void CheckCanonicalRoster()
    {
        var valid = Roster(4);
        Assert(PitFireTeamRoundLifecyclePolicy.TryValidateCanonicalRoster(valid, out _),
            "four unique account/canonical-profile pairs are accepted");

        Assert(!PitFireTeamRoundLifecyclePolicy.TryValidateCanonicalRoster(Roster(3), out var countProblem)
               && countProblem.Contains("exactly 4", StringComparison.Ordinal),
            "a partial roster fails closed");

        var duplicateProfiles = Roster(4);
        duplicateProfiles["account-4"] = duplicateProfiles["account-1"];
        Assert(!PitFireTeamRoundLifecyclePolicy.TryValidateCanonicalRoster(duplicateProfiles, out _),
            "duplicate canonical profile identities are rejected");
    }

    private static void CheckRetirementBarrier()
    {
        Assert(PitFireTeamRoundLifecyclePolicy.EvaluateRetirement(
                   new PitFireTeamRetirementObservation
                   {
                       ActiveOldBodyCount = 1,
                       RegisteredOldBodyCount = 1
                   }).Decision == PitFireTeamRoundPollDecision.Pending,
            "a dead-body registry tail keeps retirement pending");

        Assert(PitFireTeamRoundLifecyclePolicy.EvaluateRetirement(
                   new PitFireTeamRetirementObservation()).Decision
               == PitFireTeamRoundPollDecision.Ready,
            "zero old wrappers and bodies releases the leader rebuild");

        Assert(PitFireTeamRoundLifecyclePolicy.EvaluateRetirement(
                   new PitFireTeamRetirementObservation
                   {
                       OldWrapperCount = 1
                   }).Decision == PitFireTeamRoundPollDecision.Failed,
            "a stale PFT wrapper fails immediately");

        Assert(PitFireTeamRoundLifecyclePolicy.EvaluateRetirement(
                   new PitFireTeamRetirementObservation
                   {
                       RegisteredOldBodyCount = 1,
                       TimedOut = true
                   }).Decision == PitFireTeamRoundPollDecision.Failed,
            "a registry timeout cannot fall through to the next round");
    }

    private static void CheckFreshIdentityRemap()
    {
        string account = "account-1";
        string canonical = Mongo(1);
        string runtime = Mongo(101);
        string[] sourceItems = { Mongo(201), Mongo(202), Mongo(203) };
        string[] runtimeItems = { Mongo(301), Mongo(302), Mongo(303) };

        Assert(PitFireTeamRoundLifecyclePolicy.TryValidateRuntimeIdentityRemap(
                account,
                canonical,
                account,
                canonical,
                account,
                runtime,
                sourceItems,
                runtimeItems,
                Array.Empty<string>(),
                Array.Empty<string>(),
                out _),
            "a fresh profile plus a fully fresh equipment tree is accepted");

        Assert(!PitFireTeamRoundLifecyclePolicy.TryValidateRuntimeIdentityRemap(
                account,
                canonical,
                account,
                canonical,
                account,
                canonical,
                sourceItems,
                runtimeItems,
                Array.Empty<string>(),
                Array.Empty<string>(),
                out _),
            "reusing the canonical profile ID is rejected");

        Assert(!PitFireTeamRoundLifecyclePolicy.TryValidateRuntimeIdentityRemap(
                account,
                canonical,
                account,
                canonical,
                account,
                runtime,
                sourceItems,
                new[] { sourceItems[0], Mongo(302), Mongo(303) },
                Array.Empty<string>(),
                Array.Empty<string>(),
                out _),
            "one reused canonical item ID rejects the complete clone");

        Assert(!PitFireTeamRoundLifecyclePolicy.TryValidateRuntimeIdentityRemap(
                account,
                canonical,
                account,
                canonical,
                account,
                runtime,
                sourceItems,
                runtimeItems,
                new[] { runtime },
                Array.Empty<string>(),
                out _),
            "a runtime profile ID cannot be reused across rounds");

        Assert(!PitFireTeamRoundLifecyclePolicy.TryValidateRuntimeIdentityRemap(
                account,
                canonical,
                account,
                canonical,
                account,
                runtime,
                sourceItems,
                runtimeItems,
                Array.Empty<string>(),
                new[] { runtimeItems[2] },
                out _),
            "a runtime item ID cannot be reused across rounds");
    }

    private static void CheckFollowerBarrier()
    {
        Assert(EvaluateFollowers(wrappers: 3, active: 3, registered: 3, stable: 0)
                   .Decision == PitFireTeamRoundPollDecision.Pending,
            "three followers remain pending");
        Assert(EvaluateFollowers(wrappers: 4, active: 4, registered: 4, stable: 1)
                   .Decision == PitFireTeamRoundPollDecision.Pending,
            "one complete registry pass is insufficient");
        Assert(EvaluateFollowers(wrappers: 4, active: 4, registered: 4, stable: 2)
                   .Decision == PitFireTeamRoundPollDecision.Ready,
            "two stable complete passes release the four followers");
        Assert(EvaluateFollowers(wrappers: 5, active: 5, registered: 5, stable: 2)
                   .Decision == PitFireTeamRoundPollDecision.Failed,
            "a fifth follower fails immediately");

        var wrongLeader = new PitFireTeamFollowerObservation
        {
            ExpectedCount = 4,
            InvalidBinding = true,
            InvalidBindingProblem = "split leader"
        };
        Assert(PitFireTeamRoundLifecyclePolicy.EvaluateFollowers(wrongLeader).Decision
               == PitFireTeamRoundPollDecision.Failed,
            "a split/wrong leader fails immediately");

        var timedOut = new PitFireTeamFollowerObservation
        {
            ExpectedCount = 4,
            WrapperCount = 3,
            ActiveBodyCount = 3,
            RegisteredBodyCount = 3,
            DistinctAccountCount = 3,
            DistinctProfileCount = 3,
            TimedOut = true
        };
        Assert(PitFireTeamRoundLifecyclePolicy.EvaluateFollowers(timedOut).Decision
               == PitFireTeamRoundPollDecision.Failed,
            "a partial follower timeout cannot start combat");
    }

    private static void CheckCapability()
    {
        var complete = PitFireTeamRoundCapability.Available();
        Assert(complete.CanStartAdditionalRound && complete.RequiresPersistenceSuppression,
            "all four capabilities plus persistence suppression define multi-round eligibility");

        var missingRemap = PitFireTeamRoundCapability.Create(
            contractCompatible: true,
            safeWrapperTeardown: true,
            safeProfilePrefetch: true,
            freshRuntimeIdentityRemap: false);
        Assert(!missingRemap.CanStartAdditionalRound,
            "missing fresh-ID remap fails closed instead of exposing a partial capability");
    }

    private static void CheckSourceContract()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            root,
            "ArenaMode.Client",
            "Compat",
            "PitFireTeamRoundLifecycle.cs"));

        RequireAll(source,
            "GameWorld is unavailable while proving old follower retirement",
            "_bossFollowersProperty.GetValue(_currentBoss)",
            "the rebuilt PFT boss does not own the exact four wrapper bodies",
            "_bossGroupProperty.GetValue(_currentBoss) as BotsGroup",
            "candidate.BotFollower.HaveBoss",
            "ReferenceEquals(candidate.BotFollower.BossToFollow, _currentBoss)",
            "ReferenceEquals(candidate.BotsGroup, bossGroup)",
            "bossGroup.Contains(candidate)",
            "bool wasReady = _stage == PitFireTeamRoundLifecycleStage.FollowersReady",
            "the previously ready PFT follower roster lost its physical binding");

        Assert(source.IndexOf(
                   "PitFireTeam persistence suppression was lost during profile preparation",
                   StringComparison.Ordinal)
               < source.IndexOf(
                   "if (_stage == PitFireTeamRoundLifecycleStage.ProfilesReady)",
                   StringComparison.Ordinal),
            "profile idempotence checks persistence before returning ready");
        Assert(source.IndexOf(
                   "PitFireTeam persistence suppression was lost during follower spawning",
                   StringComparison.Ordinal)
               < source.IndexOf(
                   "bool wasReady = _stage == PitFireTeamRoundLifecycleStage.FollowersReady",
                   StringComparison.Ordinal),
            "follower idempotence checks persistence before revalidating ready");
    }

    private static PitFireTeamRoundPolicyDecision EvaluateFollowers(
        int wrappers,
        int active,
        int registered,
        int stable)
        => PitFireTeamRoundLifecyclePolicy.EvaluateFollowers(
            new PitFireTeamFollowerObservation
            {
                ExpectedCount = 4,
                WrapperCount = wrappers,
                ActiveBodyCount = active,
                RegisteredBodyCount = registered,
                DistinctAccountCount = wrappers,
                DistinctProfileCount = wrappers,
                StablePasses = stable
            });

    private static Dictionary<string, string> Roster(int count)
        => Enumerable.Range(1, count).ToDictionary(
            number => $"account-{number}",
            number => Mongo(number),
            StringComparer.Ordinal);

    private static string Mongo(int number) => number.ToString("x24");

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ArenaMode.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find ArenaMode.sln above the check output.");
    }

    private static void RequireAll(string source, params string[] fragments)
    {
        foreach (string fragment in fragments)
        {
            Assert(source.Contains(fragment, StringComparison.Ordinal),
                "production source retains required contract fragment: " + fragment);
        }
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
