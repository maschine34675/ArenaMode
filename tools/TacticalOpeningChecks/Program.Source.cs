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
        CheckBuyLine(tactical,
            Source("ARENA_OPENING_SCREEN_SOURCE", "ArenaMode.Client", "Hud", "TacticalBuyScreen.cs"),
            Source("ARENA_OPENING_PANEL_SOURCE", "ArenaMode.Client", "Hud", "TacticalBuyPanel.cs"));
        CheckSender(client);
        CheckPublishedState(Source("ARENA_OPENING_STATE_SOURCE", "ArenaMode.Client", "Bots", "ArenaTacticalOpening.cs"));

        server = Code(server);
        gate = Code(gate);
        Assert(server.Contains("Protocol.TacticalRoundRaidEndingRefusal", StringComparison.Ordinal)
               && !server.Contains("\"raid is ending\"", StringComparison.Ordinal),
            "the server writes the shared raid-ending constant, not its own copy of the text");
        Assert(gate.Contains("Protocol.TacticalRoundRaidEndingRefusal", StringComparison.Ordinal)
               && !gate.Contains("\"raid is ending\"", StringComparison.Ordinal),
            "the gate compares the raid-ending refusal through the shared constant");
        Assert(gate.Contains("TaskContinuationOptions.OnlyOnFaulted", StringComparison.Ordinal)
               && gate.Contains("_ = finished.Exception;", StringComparison.Ordinal),
            "a send the gate stops polling still has its fault observed");
    }

    // ArenaTacticalOpening links game types (GameWorld, Singleton) and cannot be compiled here, so what it
    // promises is pinned as text: the state of an earlier world is inert, a match end never unbinds a
    // newer match's gate, and the gear numbers are read live from the map, the frozen step and the role.
    private static void CheckPublishedState(string stateSource)
    {
        string state = Code(stateSource);
        Assert(state.Contains("_gate != null && Singleton<GameWorld>.Instantiated\n            && ReferenceEquals(_world, Singleton<GameWorld>.Instance);", StringComparison.Ordinal),
            "a gate bound in another world is inert: the published state compares the world it was bound in");
        Assert(state.Contains("internal static bool Applies => IsCurrentWorld() && _gate?.Applies == true;", StringComparison.Ordinal)
               && state.Contains("internal static int AckedRound => IsCurrentWorld() && _gate != null ? _gate.AckedRound : 0;", StringComparison.Ordinal)
               && state.Contains("internal static int? CurrentGearLevel => IsCurrentWorld() ? _gate?.CurrentGearLevel : null;", StringComparison.Ordinal),
            "every reader of the published state answers through the world check");
        string unbind = MethodBody(state, "internal static void Unbind(TacticalOpeningBandGate gate)");
        int guard = unbind.IndexOf("if (!ReferenceEquals(_gate, gate))\n            return;", StringComparison.Ordinal);
        int clear = unbind.IndexOf("_gate = null;", StringComparison.Ordinal);
        Assert(guard >= 0 && clear > guard,
            "a match end unbinds only its own gate, never a newer match's");
        string read = MethodBody(state, "internal static TacticalGearContext? ReadGearContext()");
        Assert(read.Contains("|| !ArenaClientConfig.GetScaleBotTier(map))\n            return null;", StringComparison.Ordinal),
            "a map without tier scaling offers no gear numbers");
        Assert(read.Contains("ArenaDifficulty.Step,", StringComparison.Ordinal)
               && read.Contains("ArenaClientConfig.GetBotTierStartLevel(map),", StringComparison.Ordinal)
               && read.Contains("ArenaClientConfig.GetBotTierEndLevel(map),", StringComparison.Ordinal),
            "the gear numbers are the frozen difficulty step and the map's start and end level");
        Assert(read.Contains("Settings.TacticalSquadIsPmc.Value);", StringComparison.Ordinal),
            "the squad's role is read live at every call, never frozen");
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
        int dispatchCall = block.IndexOf("DispatchOpeningSquad(RoundNumber, ", StringComparison.Ordinal);
        int dispatchEnd = dispatchCall < 0 ? -1 : block.IndexOf(");", dispatchCall, StringComparison.Ordinal);
        Assert(block.Contains("if (band == TacticalBandRelease.Hold)\n                    return;", StringComparison.Ordinal)
               && dispatchEnd >= 0
               && block.Substring(dispatchEnd).StartsWith(");\n                return;", StringComparison.Ordinal),
            "while the gate holds TickLive returns, and once it lets go the request is dispatched and the frame ends");
        string dispatch = MethodBody(tactical, "private void DispatchOpeningSquad(");
        Assert(block.Contains("DispatchOpeningSquad(RoundNumber, _bandHeldRelease, _bandHeldSeconds);", StringComparison.Ordinal)
               && dispatch.Contains("_bandHeldRelease = release;", StringComparison.Ordinal)
               && dispatch.Contains("_bandHeldSeconds = heldSeconds;", StringComparison.Ordinal),
            "a held request is dispatched later with the release and seconds it was held with, not with a made-up body-move landing");
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

    // The buy-screen line is its own label in both windows (never appended to the plan line, which is one
    // 1000 px line holding up to 102 characters already) and is rebuilt on its own test.
    private static void CheckBuyLine(string tacticalSource, string screenSource, string panelSource)
    {
        string tactical = Code(tacticalSource);
        Assert(tactical.Contains("internal string OpponentGearNote => _openingBand.OpponentGearNote(RoundNumber);", StringComparison.Ordinal),
            "the mode's note is the gate's line for the current round");
        string screen = Code(screenSource);
        Assert(screen.Contains("_plan.text = bought != null ? $\"Buying: {bought.Name ?? bought.Id}\" : mode.NoPurchaseDescription;", StringComparison.Ordinal)
               && screen.Contains("_note = ArenaKitUi.Text(rect, \"OpponentGear\", style,", StringComparison.Ordinal)
               && screen.Contains("TextAlignmentOptions.MidlineRight, WindowWidth - Margin - 380f, 54f, 380f, 24f);", StringComparison.Ordinal)
               && Count(screen, "OpponentGearNote") == 1,
            "the line is its own label on the card screen, never part of the plan line");
        Assert(screen.Contains("string note = mode.OpponentGearNote ?? string.Empty;\n            if (fresh || !string.Equals(note, _shownNote, StringComparison.Ordinal))\n            {\n                _note.text = note;\n                _shownNote = note;\n            }", StringComparison.Ordinal),
            "the note is rebuilt on its own test, so an acknowledgement that arrives after the first frame shows");
        string panel = Code(panelSource);
        Assert(panel.Contains("            : mode.NoPurchaseDescription, _bodyStyle);", StringComparison.Ordinal)
               && panel.Contains("string opponentGear = mode.OpponentGearNote;\n        if (!string.IsNullOrEmpty(opponentGear))\n            GUILayout.Label(opponentGear, _bodyStyle);", StringComparison.Ordinal)
               && Count(panel, "OpponentGearNote") == 1,
            "the line is its own label in the plain window, drawn only when it has text");
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
