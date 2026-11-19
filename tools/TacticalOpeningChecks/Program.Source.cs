// Source pins: the glue in TacticalMode.cs, the HTTP sender and the server's use of the shared refusal
// constant cannot be linked (they need the game's types), so they are read as text, with LF line ends.
// Each mutant in Verify.ps1 breaks exactly one of these.
internal static partial class Program
{
    private static void CheckSources()
    {
        string tactical = Source("ARENA_OPENING_TACTICAL_SOURCE", "ArenaMode.Client", "Modes", "TacticalMode.cs");
        string client = Source("ARENA_OPENING_CLIENT_SOURCE", "ArenaMode.Client", "Session", "ArenaTacticalRoundClient.cs");
        string server = Source("ARENA_OPENING_SERVER_SOURCE", "ArenaMode.Server", "Services", "ArenaSessionService.cs");
        string gate = Source("ARENA_OPENING_GATE_SOURCE", "ArenaMode.Client", "Modes", "TacticalOpeningBandGate.cs");

        CheckEndRoundGlue(tactical);
        CheckDispatchGlue(tactical);
        CheckTickLiveGlue(tactical);
        CheckMatchStartGlue(tactical);
        CheckTickGlue(tactical);
        CheckMatchEndGlue(tactical);
        CheckConstructorGlue(tactical);
        CheckSender(client);

        server = Code(server);
        gate = Code(gate);
        Assert(server.Contains("Protocol.TacticalRoundRaidEndingRefusal", StringComparison.Ordinal)
               && !server.Contains("\"raid is ending\"", StringComparison.Ordinal),
            "the server writes the shared raid-ending constant, not its own copy of the text");
        Assert(gate.Contains("Protocol.TacticalRoundRaidEndingRefusal", StringComparison.Ordinal)
               && !gate.Contains("\"raid is ending\"", StringComparison.Ordinal),
            "the gate compares the raid-ending refusal through the shared constant");
    }

    private static void CheckEndRoundGlue(string tactical)
    {
        string end = MethodBody(tactical, "private void EndRound(bool won)");
        int increment = end.IndexOf("RoundNumber++;", StringComparison.Ordinal);
        int report = end.IndexOf("_openingBand.Report(RoundNumber);", StringComparison.Ordinal);
        Assert(increment >= 0 && report > increment && Count(end, "_openingBand.Report(") == 1,
            "EndRound reports the new round right after RoundNumber++");
        Assert(!end.Contains("_openingBand.Reset()", StringComparison.Ordinal),
            "a round end must not drop the report it has just made");
    }

    private static void CheckDispatchGlue(string tactical)
    {
        string dispatch = MethodBody(tactical, "private void DispatchOpeningSquad(");
        int guard = dispatch.IndexOf("if (_teamArena || _phase != Phase.Live || round != RoundNumber)", StringComparison.Ordinal);
        int mark = dispatch.IndexOf("_openingBand.MarkHeld(round);", StringComparison.Ordinal);
        int ask = dispatch.IndexOf("if (!_openingBand.ShouldRelease(round))", StringComparison.Ordinal);
        int held = dispatch.IndexOf("_bandHeldRound = round;", StringComparison.Ordinal);
        int request = dispatch.IndexOf("RequestSoloSquad(retry: false);", StringComparison.Ordinal);
        Assert(guard >= 0 && mark > guard && ask > mark && held > ask && request > held
               && dispatch.Substring(held, request - held).Contains("return;", StringComparison.Ordinal),
            "the band gate sits between the guard and the squad request: it stamps its clock, asks, and returns while it holds");
    }

    private static void CheckTickLiveGlue(string tactical)
    {
        string tick = MethodBody(tactical, "private void TickLive()");
        int waiting = tick.IndexOf("if (_squadDispatch.Waiting(RoundNumber))\n                return;", StringComparison.Ordinal);
        int hold = tick.IndexOf("if (_bandHeldRound == RoundNumber)", StringComparison.Ordinal);
        int evaluate = tick.IndexOf("SoloTacticalSpawnPolicy.Evaluate(", StringComparison.Ordinal);
        Assert(waiting >= 0 && hold > waiting && evaluate > hold,
            "the band hold returns before SoloTacticalSpawnPolicy.Evaluate, which would answer Retry and send the request ahead of the ack");
        string block = tick.Substring(hold, evaluate - hold);
        Assert(block.Contains("if (band == TacticalBandRelease.Hold)\n                    return;", StringComparison.Ordinal)
               && block.Contains("DispatchOpeningSquad(RoundNumber, SoloTacticalSquadRelease.BodyLanded, 0f);\n                return;", StringComparison.Ordinal),
            "while the gate holds TickLive returns, and once it lets go the request is dispatched and the frame ends");
        Assert(block.Contains("{_openingBand.HeldSeconds(RoundNumber):0.000}s", StringComparison.Ordinal)
               && block.Contains("released by the cap: {band == TacticalBandRelease.CapExpired}", StringComparison.Ordinal)
               && block.Contains("Plugin.ModLogger.LogDebug(", StringComparison.Ordinal),
            "the Debug line of a held request carries the real held seconds and whether the cap released it");
    }

    private static void CheckMatchStartGlue(string tactical)
    {
        string start = MethodBody(tactical, "public void OnMatchStart(ArenaMatch match)");
        int provider = start.IndexOf("KitCatalog.KitProvider = IssueSoloKit;", StringComparison.Ordinal);
        int begin = start.IndexOf("BeginOpeningReports();", StringComparison.Ordinal);
        Assert(provider >= 0 && begin > provider && Count(start, "BeginOpeningReports();") == 1
               && start.LastIndexOf("return;", StringComparison.Ordinal) < begin,
            "OnMatchStart starts the reports in the solo branch only, after the opening kit is wired");
        string reports = MethodBody(tactical, "private void BeginOpeningReports()");
        int reset = reports.IndexOf("_openingBand.Reset();", StringComparison.Ordinal);
        int bind = reports.IndexOf("ArenaTacticalOpening.BindToCurrentWorld(_openingBand);", StringComparison.Ordinal);
        int first = reports.IndexOf("_openingBand.Report(1);", StringComparison.Ordinal);
        Assert(reset >= 0 && bind > reset && first > bind
               && reports.Contains("catch (Exception ex)", StringComparison.Ordinal)
               && reports.Contains("LogWarning(", StringComparison.Ordinal),
            "the match start resets the gate, publishes it and reports round 1, and nothing in it can throw");
    }

    private static void CheckTickGlue(string tactical)
    {
        string tick = MethodBody(tactical, "public void Tick(float deltaTime)");
        int nullCheck = tick.IndexOf("if (_match == null)", StringComparison.Ordinal);
        int poll = tick.IndexOf("if (!_teamArena)\n            _openingBand.Tick();", StringComparison.Ordinal);
        int menu = tick.IndexOf("if (AllowsCountdownMenu && Hud.TacticalBuyPanel.IsNativeMenuOpen)", StringComparison.Ordinal);
        Assert(nullCheck >= 0 && poll > nullCheck && menu > poll,
            "the report's answer is polled every frame, before the native-menu early return");
    }

    private static void CheckMatchEndGlue(string tactical)
    {
        string end = MethodBody(tactical, "public void OnMatchEnd()");
        Assert(end.Contains("_bandHeldRound = 0;\n        _openingBand.Reset();\n        ArenaTacticalOpening.Unbind(_openingBand);", StringComparison.Ordinal),
            "the match end clears the held round, resets the gate and unpublishes it");
    }

    private static void CheckConstructorGlue(string tactical)
    {
        Assert(tactical.Contains("() => Time.unscaledTime,\n            ArenaTacticalRoundClient.SendAsync,", StringComparison.Ordinal),
            "the gate runs on unscaled time with the HTTP sender");
        Assert(tactical.Contains("message => Plugin.ModLogger.LogDebug(message),\n            message => Plugin.ModLogger.LogWarning(message));", StringComparison.Ordinal),
            "the gate's lines are Debug and its one-per-raid failure a Warning, never Info");
    }

    private static void CheckSender(string clientSource)
    {
        // The doc comment names the calls it avoids; only code counts.
        string client = Code(clientSource);
        Assert(client.Contains("RequestHandler.HttpClient.PostAsync(", StringComparison.Ordinal)
               && client.Contains("Task.Run(", StringComparison.Ordinal)
               && !client.Contains("PostJson", StringComparison.Ordinal)
               && !client.Contains("GetJson", StringComparison.Ordinal)
               && !client.Contains("LogInfo", StringComparison.Ordinal),
            "the report goes through RequestHandler.HttpClient.PostAsync inside Task.Run, without the wrapper's Info lines");
        Assert(!client.Contains(".Result", StringComparison.Ordinal)
               && !client.Contains(".Wait(", StringComparison.Ordinal)
               && !client.Contains("GetResult(", StringComparison.Ordinal),
            "the report never blocks the calling thread on its task");
        Assert(client.Contains("Protocol.TacticalRoundRoute", StringComparison.Ordinal),
            "the report is posted to the Tactical round route");
        Assert(client.Contains("new { armToken, round, seq }", StringComparison.Ordinal),
            "the report body carries armToken, round and seq");
    }

    /// <summary>The source without its comment lines: a doc comment may name what the code avoids.</summary>
    private static string Code(string source)
        => string.Join("\n", source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string MethodBody(string source, string signature)
    {
        int first = source.IndexOf(signature, StringComparison.Ordinal);
        int last = first < 0 ? -1 : source.IndexOf("\n    }\n", first, StringComparison.Ordinal);
        if (first < 0 || last < 0)
            throw new InvalidOperationException($"source method not found: {signature}");
        return source.Substring(first, last - first + "\n    }".Length);
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
}
