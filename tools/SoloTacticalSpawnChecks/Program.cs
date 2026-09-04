using ArenaMode.Modes;

internal static class Program
{
    private static int _assertions;

    private static int Main(string[] args)
    {
        try
        {
            string root = FindRepositoryRoot();
            string tacticalPath = Argument(args, "--tactical-source")
                                  ?? Path.Combine(root, "ArenaMode.Client", "Modes", "TacticalMode.cs");
            string burstPath = Argument(args, "--burst-source")
                               ?? Path.Combine(root, "ArenaMode.Client", "Bots", "BurstSpawnService.cs");

            CheckTimingPolicy();
            CheckTacticalSource(File.ReadAllText(tacticalPath));
            CheckBurstSource(File.ReadAllText(burstPath));
            Console.WriteLine($"SOLO_TACTICAL_SPAWN_CHECKS_PASSED ({_assertions} assertions)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"SOLO_TACTICAL_SPAWN_CHECKS_FAILED after {_assertions} assertions: {ex.Message}");
            return 1;
        }
    }

    private static void CheckTimingPolicy()
    {
        float dispatchedDeadline = SoloTacticalSpawnPolicy.Deadline(0f, requestDispatched: true);
        Assert(SoloTacticalSpawnPolicy.Evaluate(5, 0, 12.728f, dispatchedDeadline)
               == SoloTacticalSpawnDecision.Waiting,
            "the measured 12.728-second first request is not discarded by the old 12-second boundary");
        Assert(Math.Abs(dispatchedDeadline - 45f) < 0.001f,
            "a dispatched cold squad receives the full 45-second assembly window");
        Assert(SoloTacticalSpawnPolicy.Evaluate(5, 4, 44.999f, dispatchedDeadline)
               == SoloTacticalSpawnDecision.Waiting,
            "a partial squad remains pending for the complete assembly window");
        Assert(SoloTacticalSpawnPolicy.Evaluate(5, 4, 45f, dispatchedDeadline)
               == SoloTacticalSpawnDecision.Active,
            "the established partial-squad fallback becomes active after the full assembly window");
        Assert(SoloTacticalSpawnPolicy.Evaluate(5, 5, 0.01f, dispatchedDeadline)
               == SoloTacticalSpawnDecision.Active,
            "an exact squad becomes ready immediately without waiting out its deadline");

        float preflightDeadline = SoloTacticalSpawnPolicy.Deadline(10f, requestDispatched: false);
        Assert(Math.Abs(preflightDeadline - 13f) < 0.001f,
            "a synchronous spawn-plan failure uses the bounded three-second retry delay");
        Assert(SoloTacticalSpawnPolicy.Evaluate(5, 0, 12.999f, preflightDeadline)
               == SoloTacticalSpawnDecision.Waiting
               && SoloTacticalSpawnPolicy.Evaluate(5, 0, 13f, preflightDeadline)
               == SoloTacticalSpawnDecision.Retry,
            "the short preflight retry neither spins each frame nor waits 45 seconds");
    }

    private static void CheckTacticalSource(string source)
    {
        string tick = Slice(source, "private void TickLive()", "public void OnKill(");
        int assemblyDecision = tick.IndexOf("SoloTacticalSpawnPolicy.Evaluate(", StringComparison.Ordinal);
        int combatTimeout = tick.IndexOf("ran out of time", StringComparison.Ordinal);
        Assert(assemblyDecision >= 0 && combatTimeout > assemblyDecision,
            "opponent assembly is resolved before the combat round timer can score a loss");
        RequireAll(tick,
            "SoloTacticalSpawnDecision.Waiting",
            "SoloTacticalSpawnDecision.Retry",
            "RequestSoloSquad(retry: true)");

        string start = Slice(source, "private void StartRound()", "private void RequestSoloSquad(");
        Assert(Count(start, "Money -= price") == 1
               && Count(start, "LiveKitRespawn.AtRoundBreak(") == 1
               && Count(start, "ArenaAnnouncerCue.RoundStart") == 1,
            "a purchased kit, body swap and round cue remain one-time round-start work");
        RequireAll(start,
            "int roundNumber = RoundNumber",
            "_phase == Phase.Live && RoundNumber == roundNumber");

        string request = Slice(source, "private void RequestSoloSquad(bool retry)",
            "private static void RetireBot(");
        int publishExpected = request.IndexOf("_squadRequested = expected;", StringComparison.Ordinal);
        int spawn = request.IndexOf("BurstSpawnService.SpawnSquad(", StringComparison.Ordinal);
        Assert(publishExpected >= 0 && spawn > publishExpected,
            "the exact observer limit is published before a warm synchronous SpawnSquad callback");
        RequireAll(request,
            "_squad.Count >= expected",
            "requestStillCurrent: () => epoch == _squadEpoch",
            "SoloTacticalSpawnPolicy.Deadline(Time.time, requestDispatched: false)",
            "RetireBot(bot, \"stale solo Tactical squad member\")",
            "if (_squad.Count == 0)",
            "_phaseEndsAt = Time.time + Mathf.Max(30f, Settings.TacticalRoundSeconds.Value)");
        Assert(!request.Contains("_squad.Count >= _squadRequested", StringComparison.Ordinal),
            "the observer closes over its request's expected count instead of mutable prior-round state");
        Assert(!request.Contains("EnterBuy()", StringComparison.Ordinal)
               && !request.Contains("Money -=", StringComparison.Ordinal)
               && !request.Contains("LiveKitRespawn", StringComparison.Ordinal)
               && !request.Contains("ReportAnnouncerCue", StringComparison.Ordinal),
            "a failed opponent request retries inside the same round without another buy, charge, body swap or cue");

        Assert(!request.Contains("requireExactCount: true", StringComparison.Ordinal),
            "the fix preserves configured partial-squad fallback instead of making large valid settings loop forever");
    }

    private static void CheckBurstSource(string source)
    {
        string spawnPair = Slice(source, "private static async void SpawnPair(",
            "private static bool TryCaptureSpawnLease(");
        int profileAwait = spawnPair.IndexOf("await BotCreationData.Create", StringComparison.Ordinal);
        const string requestLease = "IsRequestStillCurrent(requestStillCurrent, role)";
        int requestCheck = spawnPair.IndexOf(requestLease, StringComparison.Ordinal);
        int secondRequestCheck = spawnPair.IndexOf(
            requestLease,
            requestCheck + requestLease.Length,
            StringComparison.Ordinal);
        int pointTrim = spawnPair.IndexOf("if (points.Count > data.Count)", StringComparison.Ordinal);
        int ledger = spawnPair.IndexOf("reserved = points.Count", StringComparison.Ordinal);
        int dispatch = spawnPair.IndexOf("spawner.SpawnBotsInZoneOnPositions", StringComparison.Ordinal);
        Assert(profileAwait >= 0 && requestCheck > profileAwait && dispatch > requestCheck,
            "a superseded mode request stops after profile loading and before EFT body dispatch");
        Assert(Count(spawnPair, requestLease) == 2
               && secondRequestCheck > pointTrim
               && ledger > secondRequestCheck
               && dispatch > ledger,
            "the second request lease sits after point trimming and before ledger or native dispatch mutation");
        Assert(spawnPair.LastIndexOf(requestLease, StringComparison.Ordinal) < dispatch,
            "request cancellation never runs after EFT has charged its per-point dispatch counter");
        RequireAll(spawnPair,
            "data?.StopSpawn()",
            "discarded superseded {role} profiles before dispatch");

        string publicRequest = Slice(source, "public static int SpawnSquad(",
            "private static void RetrySoon()");
        RequireAll(publicRequest,
            "Func<bool> requestStillCurrent = null",
            "requireExactCount",
            "requestStillCurrent);");
    }

    private static string Argument(string[] args, string name)
    {
        for (int index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal))
                return Path.GetFullPath(args[index + 1]);
        }

        return null;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "ArenaMode.Client")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("ArenaMode repository root not found");
    }

    private static string Slice(string source, string start, string end)
    {
        int first = source.IndexOf(start, StringComparison.Ordinal);
        int last = first < 0 ? -1 : source.IndexOf(end, first + start.Length, StringComparison.Ordinal);
        if (first < 0 || last <= first)
            throw new InvalidOperationException($"source slice not found: {start} .. {end}");
        return source.Substring(first, last - first);
    }

    private static int Count(string source, string value)
    {
        int count = 0;
        int offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static void RequireAll(string source, params string[] values)
    {
        foreach (string value in values)
            Assert(source.Contains(value, StringComparison.Ordinal), $"source contract contains '{value}'");
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
