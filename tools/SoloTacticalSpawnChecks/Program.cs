using ArenaMode.Modes;

internal static partial class Program
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
            string sptRoot = Argument(args, "--spt-root") ?? @"D:\SPT41";
            string tactical = File.ReadAllText(tacticalPath);

            CheckTimingPolicy();
            CheckDispatchGate();
            CheckDispatchOrderOnInstalledRows(tactical, sptRoot);
            CheckTacticalSource(tactical);
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
        string tickLf = tick.Replace("\r\n", "\n");
        int held = tickLf.IndexOf("if (_squadDispatch.Waiting(RoundNumber))\n                return;", StringComparison.Ordinal);
        Assert(held > tickLf.IndexOf("if (!_soloCombatReady)", StringComparison.Ordinal)
               && held < tickLf.IndexOf("SoloTacticalSpawnPolicy.Evaluate(", StringComparison.Ordinal),
            "the assembly check waits while the request is held for the body move, and asks the gate's cap every frame");
        RequireAll(tick,
            "if (!_soloCombatReady)",
            "bot.BotState == EBotState.Active",
            "_squadRequested, ready + eliminated",
            "SoloTacticalSpawnDecision.Waiting",
            "SoloTacticalSpawnDecision.Retry",
            "RequestSoloSquad(retry: true)");
        int readyLatch = tick.IndexOf("_soloCombatReady = true;", StringComparison.Ordinal);
        int cue = tick.IndexOf("ArenaAnnouncerCue.RoundStart", StringComparison.Ordinal);
        int roundTimer = tick.IndexOf("_phaseEndsAt = Time.time + Mathf.Max(30f, Settings.TacticalRoundSeconds.Value)", StringComparison.Ordinal);
        Assert(readyLatch > assemblyDecision && roundTimer > readyLatch && cue > roundTimer
               && tick.Contains("if (ready > 0)", StringComparison.Ordinal)
               && tick.Contains("ArenaAnnouncerCue.RoundStart, beginAwardRound: false", StringComparison.Ordinal),
            "physical readiness owns the combat timer and one-time cue, with no fight cue for an already-eliminated squad");

        string buyTick = Slice(source, "public void Tick(float deltaTime)", "private bool HoldRoundStartForKitSwap()");
        Assert(buyTick.Contains("if (Time.time >= _phaseEndsAt && !HoldRoundStartForKitSwap())", StringComparison.Ordinal),
            "the buy phase ends only when the raid-start kit swap is no longer pending, or the wait has given up");
        string hold = Slice(source, "private bool HoldRoundStartForKitSwap()", "internal bool TrySynchronizeScoreboardRoster(");
        RequireAll(hold,
            "SoloTacticalRoundStartPolicy.Evaluate(",
            "Respawn.LiveKitRespawn.IsInitialSwapPending",
            "(!_playerDown && Respawn.ArenaRespawnDriver.IsRespawnInProgress)",
            "SoloTacticalRoundStartDecision.StartAfterGivingUp");
        // The first "return" after each hold label must be "return true;" — a
        // wait the policy asked for that the switch then ignores is no wait.
        Assert(System.Text.RegularExpressions.Regex.IsMatch(hold,
                   @"case SoloTacticalRoundStartDecision\.HoldBegun:(?:(?!return )[\s\S])*return true;")
               && System.Text.RegularExpressions.Regex.IsMatch(hold,
                   @"case SoloTacticalRoundStartDecision\.Hold:(?:(?!return )[\s\S])*return true;"),
            "a wait in progress keeps the buy phase open");

        string start = Slice(source, "private void StartRound()", "private void RequestSoloSquad(");
        Assert(Count(start, "Money -= price") == 1
               && Count(start, "LiveKitRespawn.AtRoundBreak(") == 1
               && Count(start, "_match.BeginAwardRound();") == 1
               && Count(start, "ArenaAnnouncerCue.RoundStart") == 0,
            "purchased kit and body swap remain one-time start work, while the cue waits for physical opponents");
        RequireAll(start,
            "int roundNumber = RoundNumber",
            "_phase == Phase.Live && RoundNumber == roundNumber");

        // Dispatch order (docs/reviews/SOLO-TACTICAL-SPAWN-REVIEW.md): the round's request waits
        // for the body move; its count, deadline and toast belong to the real request.
        string startBody = MethodBody(source, "private void StartRound()");
        int clearCount = startBody.IndexOf("_squadRequested = 0;", StringComparison.Ordinal);
        int open = startBody.IndexOf(
            "_squadDispatch.Open(roundNumber, landed => StartSoloBodyMove(roundNumber, wasDown, landed));",
            StringComparison.Ordinal);
        Assert(!startBody.Contains("RequestSoloSquad(", StringComparison.Ordinal)
               && !startBody.Contains("ArenaToasts.Show(", StringComparison.Ordinal)
               && clearCount >= 0 && open > clearCount,
            "StartRound sends no request and no toast itself: it clears the old count and holds the new request for the body move");
        Assert(source.Contains("_squadDispatch = new SoloTacticalSquadDispatch(() => Time.time, DispatchOpeningSquad);",
                   StringComparison.Ordinal),
            "the dispatch gate runs on the mode's clock and sends the mode's opening request");
        string move = MethodBody(source, "private bool StartSoloBodyMove(");
        int heldRespawn = move.IndexOf("return Respawn.ArenaRespawnDriver.IsRespawnInProgress;", StringComparison.Ordinal);
        Assert(move.Contains("if (wasDown)", StringComparison.Ordinal)
               && heldRespawn > move.IndexOf("if (wasDown)", StringComparison.Ordinal)
               && Count(move, "LiveKitRespawn.AtRoundBreak(") == 1
               && move.Contains("() => _phase == Phase.Live && RoundNumber == roundNumber,\n            landed);",
                   StringComparison.Ordinal),
            "the survivor's round-break swap reports its landing to the held request; a dead player's move is the held respawn");
        string landedBody = MethodBody(source, "private void OnSoloLocalRespawn()");
        Assert(landedBody.Contains("_squadDispatch.Report(RoundNumber, landed: true);", StringComparison.Ordinal)
               && MethodBody(source, "public void OnLocalRespawn(Player player)")
                   .Contains("OnSoloLocalRespawn();", StringComparison.Ordinal),
            "the held respawn's landing releases the round's request");
        string opening = MethodBody(source, "private void DispatchOpeningSquad(");
        int openingGuard = opening.IndexOf("if (_teamArena || _phase != Phase.Live || round != RoundNumber)", StringComparison.Ordinal);
        int openingRequest = opening.IndexOf("RequestSoloSquad(retry: false);", StringComparison.Ordinal);
        int openingToast = opening.IndexOf("assembling {_squadRequested} opponents", StringComparison.Ordinal);
        Assert(openingGuard >= 0 && openingRequest > openingGuard && openingToast > openingRequest,
            "the released request goes out only into its own live round, and the assembling toast follows it");
        Assert(MethodBody(source, "private void EndRound(bool won)").Contains("_squadDispatch.Cancel();", StringComparison.Ordinal)
               && MethodBody(source, "public void OnMatchEnd()").Contains("_squadDispatch.Cancel();", StringComparison.Ordinal),
            "a round or match that ends before the body lands releases nothing");

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
            "RetireSquad()");
        Assert(!request.Contains("_phaseEndsAt =", StringComparison.Ordinal),
            "arrival callbacks do not start the combat timer before native Active readiness");
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
        string plan = Slice(spawnPair, "private static async void SpawnPair(",
            "Action<BotOwner> onArrival = bot =>");
        int profileAwait = plan.IndexOf("await BotCreationData.Create", StringComparison.Ordinal);
        const string requestLease = "IsRequestStillCurrent(requestStillCurrent, role)";
        int requestCheck = plan.IndexOf(requestLease, StringComparison.Ordinal);
        int secondRequestCheck = plan.IndexOf(
            requestLease,
            requestCheck + requestLease.Length,
            StringComparison.Ordinal);
        int pointTrim = plan.IndexOf("if (points.Count > data.Count)", StringComparison.Ordinal);
        int ledger = plan.IndexOf("reserved = points.Count", StringComparison.Ordinal);
        int dispatch = spawnPair.IndexOf("spawner.SpawnBotsInZoneOnPositions", StringComparison.Ordinal);
        Assert(profileAwait >= 0 && requestCheck > profileAwait && dispatch > requestCheck,
            "a superseded mode request stops after profile loading and before EFT body dispatch");
        Assert(Count(plan, requestLease) == 2
               && secondRequestCheck > pointTrim
               && ledger > secondRequestCheck
               && dispatch > ledger,
            "the second request lease sits after point trimming and before ledger or native dispatch mutation");

        string arrival = Slice(spawnPair, "Action<BotOwner> onArrival = bot =>", "\n            };" );
        Assert(!arrival.Contains("StopSpawn(", StringComparison.Ordinal),
            "a late activation retires its body without cancelling charged native spawn data");
        int duplicate = arrival.IndexOf("ReferenceEquals(observedFactionBody, bot)", StringComparison.Ordinal);
        int arrivalLease = arrival.IndexOf(requestLease, StringComparison.Ordinal);
        int observe = arrival.IndexOf("OnBotSpawned(bot", StringComparison.Ordinal);
        Assert(duplicate >= 0 && arrivalLease > duplicate && observe > arrivalLease,
            "duplicate body callbacks return before the settled request predicate, while stale arrivals precede observation");
        string rejection = Slice(arrival, "if (explicitFaction && (!IsRequestStillCurrent", "OnBotSpawned(bot");
        RequireAll(rejection, "HasRequestedFaction(bot, role)", "RetireStaleSpawn(bot, role)",
            "NotifyArrivalRejected(arrivalRejected)", "return;");
        Assert(rejection.IndexOf("RetireStaleSpawn", StringComparison.Ordinal)
               < rejection.IndexOf("NotifyArrivalRejected", StringComparison.Ordinal),
            "retirement precedes releasing a rejected arrival's population ticket");

        RequireAll(spawnPair,
            "data?.StopSpawn()",
            "discarded superseded {role} profiles before dispatch",
            "if (explicitFaction && !handedToNative)",
            "NotifyDispatchFinished(dispatchFinished, handedToNative)");

        string ownership = Slice(spawnPair, "using (var scope = TeamDeathmatchSpawnDispatch.Enter(",
            "if (!handedToNative)");
        Assert(!ownership.Contains("await ", StringComparison.Ordinal)
               && ownership.Contains("spawner.SpawnBotsInZoneOnPositions", StringComparison.Ordinal)
               && ownership.Contains("if (scope.WasRejected)", StringComparison.Ordinal),
            "exact dispatch ownership spans only the synchronous native call and tracks proven prefix rejection");

        string factionRequest = Slice(source, "internal static int SpawnFactionBot(",
            "private static bool HasRequestedFaction(");
        RequireAll(factionRequest,
            "role != WildSpawnType.pmcUSEC && role != WildSpawnType.pmcBEAR",
            "TeamDeathmatchSpawnDispatch.IsActive",
            "SpawnPair(spawner, zone, role, difficulty, true, 1, observer",
            "explicitFaction: true",
            "NotifyDispatchFinished(dispatchFinished, false)");
        Assert(!factionRequest.Contains("_nextPmcIsBear", StringComparison.Ordinal),
            "explicit faction refills cannot alter existing Tactical or ambient faction alternation");

        string publicRequest = Slice(source, "public static int SpawnSquad(",
            "internal static int SpawnFactionBot(");
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
