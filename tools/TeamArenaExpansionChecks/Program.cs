using ArenaMode.Spectator;
using ArenaMode.Modes;

internal static class Program
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            CheckFrozenRosterPolicy();
            CheckStartPolicy();
            CheckTargetCycling();
            CheckRoundContinuation();
            CheckSourceContract();
            Console.WriteLine($"TEAM_ARENA_EXPANSION_CHECKS_PASSED ({_assertions} assertions)");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"TEAM_ARENA_EXPANSION_CHECKS_FAILED after {_assertions} assertions: {exception}");
            return 1;
        }
    }

    private static void CheckFrozenRosterPolicy()
    {
        string[] nine = Enumerable.Range(1, 9).Select(index => $"profile-{index}").ToArray();
        Assert(SpectatorPolicy.TryFreezeAllowedProfileIds(nine, out string[] frozen, out string problem),
            "exactly nine distinct non-local actors form a valid frozen spectator roster");
        Assert(problem == null && frozen.SequenceEqual(nine),
            "freezing keeps the exact identity order and reports no failure");

        Assert(!SpectatorPolicy.TryFreezeAllowedProfileIds(nine.Take(8), out _, out _),
            "an incomplete 5v5 roster is rejected");
        Assert(!SpectatorPolicy.TryFreezeAllowedProfileIds(nine.Append("profile-10"), out _, out _),
            "an oversized roster is rejected");
        Assert(!SpectatorPolicy.TryFreezeAllowedProfileIds(
                nine.Take(8).Append("profile-1"), out _, out problem)
               && problem.Contains("duplicate", StringComparison.OrdinalIgnoreCase),
            "duplicate profile identities are rejected explicitly");
        Assert(!SpectatorPolicy.TryFreezeAllowedProfileIds(
                nine.Take(8).Append(" "), out _, out problem)
               && problem.Contains("empty", StringComparison.OrdinalIgnoreCase),
            "empty profile identities are rejected explicitly");
    }

    private static void CheckStartPolicy()
    {
        SpectatorStartGate ready = Gate();
        Assert(SpectatorPolicy.DecideStart(ready) == SpectatorStartDecision.Start,
            "an eligible dead player with a living target and no camera owner starts spectating");
        Assert(SpectatorPolicy.DecideStart(Gate(killcam: OptionalCameraOwnerState.Idle))
               == SpectatorStartDecision.Start,
            "an installed idle Killcam permits the hand-off");

        Assert(SpectatorPolicy.DecideStart(Gate(killcam: OptionalCameraOwnerState.Busy))
               == SpectatorStartDecision.Wait,
            "an active Killcam retains camera ownership");
        Assert(SpectatorPolicy.DecideStart(Gate(freecam: OptionalCameraOwnerState.Busy))
               == SpectatorStartDecision.Wait,
            "an active external freecam retains camera ownership");
        Assert(SpectatorPolicy.DecideStart(Gate(targets: 0)) == SpectatorStartDecision.Wait,
            "a valid lease waits for its first living target");

        Assert(SpectatorPolicy.DecideStart(Gate(arena: false)) == SpectatorStartDecision.Cancel,
            "spectating fails closed outside Arena");
        Assert(SpectatorPolicy.DecideStart(Gate(team: false)) == SpectatorStartDecision.Cancel,
            "spectating fails closed outside protected team Arena");
        Assert(SpectatorPolicy.DecideStart(Gate(eligible: false)) == SpectatorStartDecision.Cancel,
            "a revoked round lease cancels spectating");
        Assert(SpectatorPolicy.DecideStart(Gate(epoch: false)) == SpectatorStartDecision.Cancel,
            "an old round epoch cancels spectating");
        Assert(SpectatorPolicy.DecideStart(Gate(dead: false)) == SpectatorStartDecision.Cancel,
            "a living local player cannot enter the death spectator");
        Assert(SpectatorPolicy.DecideStart(Gate(killcam: OptionalCameraOwnerState.Incompatible))
               == SpectatorStartDecision.Cancel,
            "an installed but unreadable Killcam contract fails closed");
        Assert(SpectatorPolicy.DecideStart(Gate(freecam: OptionalCameraOwnerState.Incompatible))
               == SpectatorStartDecision.Cancel,
            "an installed but unreadable Freecam contract fails closed");
    }

    private static void CheckTargetCycling()
    {
        Assert(SpectatorPolicy.CycleIndex(4, 0, 1) == 1,
            "forward cycling advances one participant");
        Assert(SpectatorPolicy.CycleIndex(4, 3, 1) == 0,
            "forward cycling wraps to the first participant");
        Assert(SpectatorPolicy.CycleIndex(4, 0, -1) == 3,
            "backward cycling wraps to the last participant");
        Assert(SpectatorPolicy.CycleIndex(4, -1, 1) == 0,
            "a missing current target recovers at the first living participant");
        Assert(SpectatorPolicy.CycleIndex(0, 0, 1) == -1,
            "an empty target set cannot produce an index");
    }

    private static void CheckRoundContinuation()
    {
        TeamArenaRoundDecision firstWin = TeamArenaRoundPolicy.Evaluate(
            won: true,
            roundsWon: 0,
            roundsLost: 0,
            roundsToWin: 3);
        Assert(firstWin.RoundsWon == 1
               && firstWin.RoundsLost == 0
               && firstWin.Continuation == TeamArenaRoundContinuation.RebindNextRound,
            "the first best-of-five win advances to another round");

        TeamArenaRoundDecision matchWin = TeamArenaRoundPolicy.Evaluate(true, 2, 1, 3);
        Assert(matchWin.RoundsWon == 3
               && matchWin.Continuation == TeamArenaRoundContinuation.EndMatchWon,
            "the configured third win completes the match");

        TeamArenaRoundDecision matchLoss = TeamArenaRoundPolicy.Evaluate(false, 2, 2, 3);
        Assert(matchLoss.RoundsLost == 3
               && matchLoss.Continuation == TeamArenaRoundContinuation.EndMatchLost,
            "the configured third loss completes the match");

        TeamArenaRoundDecision noContest = TeamArenaRoundPolicy.Evaluate(null, 1, 1, 3);
        Assert(noContest.RoundsWon == 1
               && noContest.RoundsLost == 1
               && noContest.Continuation == TeamArenaRoundContinuation.EndNoContest,
            "a technical no-contest preserves the score and ends the protected match");

        bool rejected = false;
        try
        {
            TeamArenaRoundPolicy.Evaluate(true, 0, 0, 0);
        }
        catch (ArgumentOutOfRangeException)
        {
            rejected = true;
        }

        Assert(rejected, "an invalid rounds-to-win setting fails closed");
    }

    private static void CheckSourceContract()
    {
        string root = FindRepositoryRoot();
        string service = Read(root, "ArenaMode.Client", "Spectator", "ArenaSpectatorService.cs");
        string cameraPatch = Read(root, "ArenaMode.Client", "Spectator", "ArenaSpectatorCameraPatches.cs");
        string killcam = Read(root, "ArenaMode.Client", "Spectator", "KillcamReplayCompat.cs");
        string freecam = Read(root, "ArenaMode.Client", "Spectator", "TerkoizFreecamProbe.cs");
        string plugin = Read(root, "ArenaMode.Client", "Plugin.cs");
        string controller = Read(root, "ArenaMode.Client", "ArenaController.cs");
        string project = Read(root, "ArenaMode.Client", "ArenaMode.Client.csproj");
        string pursuit = Read(root, "ArenaMode.Client", "Bots", "TeamArenaPursuit.cs");

        RequireAll(service,
            "private static void TickUnsafe()",
            "Arena spectator tick failed closed",
            "Application.onBeforeRender -= ApplyRenderPose",
            "Application.onBeforeRender += ApplyRenderPose",
            "ArenaInputBlocker.Request(InputOwner, true)",
            "ArenaInputBlocker.Request(InputOwner, false)",
            "InitiateOperation<LockCameraState>()",
            "InitiateOperation<FirstPersonCameraState>()",
            "PitFireTeamCompat.TeamArenaActive",
            "request.ReadEpoch() != request.Epoch",
            "TryFreezeAllowedProfileIds",
            "ReferenceEquals(controller.Player, _request.DeadPlayer)",
            "Stop(\"the local player became alive again\")",
            "returnOwnedCameraToNative",
            "OptionalOwnersPermitNativeRestore()",
            "HasBoundCameraController(playerToRestore)",
            "&& optionalOwnersPermitRestore",
            "RestoreControllerOperation(playerToRestore, restoreFirstPerson)",
            "FrozenBodiesByProfileId.TryGetValue",
            "!ReferenceEquals(frozenBody, player)",
            "SeenProfiles.SetEquals(AllowedProfileIds)",
            "supplied.Count != SpectatorPolicy.RequiredNonLocalTeamActors");
        Assert(service.IndexOf("_ownsCamera = false", StringComparison.Ordinal)
               < service.IndexOf("Application.onBeforeRender -= ApplyRenderPose", StringComparison.Ordinal),
            "synchronous teardown drops patch ownership before releasing callbacks");

        RequireAll(cameraPatch,
            "nameof(CameraManager.ForceSetPosition)",
            "!ArenaSession.IsActive",
            "!PitFireTeamCompat.TeamArenaActive",
            "!ArenaSpectatorService.OwnsCamera");
        RequireAll(killcam,
            "PendingReplayKiller",
            "IsPlaying",
            "ReadyForStop",
            "GetExtraDelay",
            "BindingFlags.Public");
        RequireAll(freecam,
            "com.terkoiz.freecam",
            "IsFreecamActive",
            "FindObjectOfType",
            "MissingControllerProbeIntervalSeconds",
            "Time.unscaledTime < _nextControllerProbeAt",
            "_activeProperty.GetValue(_controller)");
        Assert(CountOf(freecam, "FindObjectOfType") == 1,
            "the optional Freecam controller has one throttled scene-scan site rather than a per-frame search");
        RequireAll(plugin,
            "ArenaSpectatorService.Initialize()",
            "ArenaSpectatorCameraPatches.Enable()",
            "AddComponent<Spectator.ArenaSpectatorOverlay>()",
            "ArenaSpectatorService.Shutdown()");
        RequireAll(controller, "ArenaSpectatorService.Tick()");

        Assert(!project.Contains("KillcamReplay", StringComparison.Ordinal)
               && !project.Contains("Terkoiz", StringComparison.Ordinal)
               && !project.Contains("Fika", StringComparison.Ordinal),
            "the client project has no hard camera-mod dependency");

        RequireAll(pursuit,
            "private const float SweepSeconds = 1f",
            "public static void Reset()",
            "public static void BeginRound(int epoch)",
            "!localPlayerDown",
            "epoch != _epoch",
            "!ArenaSession.IsActive",
            "!PitFireTeamCompat.TeamArenaActive",
            "Time.time < _nextSweepAt",
            "candidate.BotState != EBotState.Active",
            "candidate.HealthController?.IsAlive != true",
            "!profiles.Add(profileId)",
            "RefreshNearest(ally, liveOpponents, epoch, follower: true)",
            "RefreshNearest(opponent, liveAllies, epoch, follower: false)",
            "distance < bestDistance",
            "string.CompareOrdinal(profile, bestProfile) < 0",
            "group.AddEnemy(target, EBotEnemyCause.bossKillArena)",
            "group.ReportAboutEnemy(target, EEnemyPartVisibleType.Sence, source)",
            "info.IsLastPositionChecked = false",
            "TrySyncEnemyState",
            "new object[] { source, target, true }",
            "GetSAIN",
            "CheckAddEnemy",
            "UpdateLastSeenPosition",
            "SetInCombat",
            "No references are retained beyond this sweep");
        Assert(!pursuit.Contains("static BotOwner", StringComparison.Ordinal)
               && !pursuit.Contains("GoToPoint", StringComparison.Ordinal)
               && !pursuit.Contains("BotMover", StringComparison.Ordinal)
               && !pursuit.Contains("LookSensor", StringComparison.Ordinal),
            "post-death pursuit stores no bot body and does not command movement or visibility");
    }

    private static SpectatorStartGate Gate(
        bool arena = true,
        bool team = true,
        bool eligible = true,
        bool epoch = true,
        bool dead = true,
        int targets = 1,
        OptionalCameraOwnerState killcam = OptionalCameraOwnerState.Absent,
        OptionalCameraOwnerState freecam = OptionalCameraOwnerState.Absent)
        => new(arena, team, eligible, epoch, dead, targets, killcam, freecam);

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ArenaMode.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find ArenaMode.sln above the check output.");
    }

    private static string Read(string root, params string[] relativeParts)
        => File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));

    private static void RequireAll(string source, params string[] fragments)
    {
        foreach (string fragment in fragments)
        {
            Assert(source.Contains(fragment, StringComparison.Ordinal),
                $"production source retains required contract fragment: {fragment}");
        }
    }

    private static int CountOf(string source, string fragment)
    {
        int count = 0;
        int offset = 0;
        while ((offset = source.IndexOf(fragment, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += fragment.Length;
        }

        return count;
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
