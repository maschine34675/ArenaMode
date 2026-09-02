using ArenaMode.Modes;
using ArenaMode.Spectator;

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
        string burst = Read(root, "ArenaMode.Client", "Bots", "BurstSpawnService.cs");
        string settings = Read(root, "ArenaMode.Client", "Config", "Settings.cs");
        string tactical = Read(root, "ArenaMode.Client", "Modes", "TacticalMode.cs");
        string combat = Read(root, "ArenaMode.Client", "Combat", "CombatPatches.cs");
        string respawn = Read(root, "ArenaMode.Client", "Respawn", "ArenaRespawnDriver.cs");
        string matchService = Read(root, "ArenaMode.Client", "Modes", "ArenaMatchService.cs");

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

        RequireAll(burst,
            "Interlocked.Increment(ref _operationGeneration)",
            "TryCaptureSpawnLease(",
            "ArenaRespawnDriver.CaptureRaidEpoch()",
            "ArenaRespawnDriver.IsSameActiveRaid(game, raidEpoch)",
            "ReferenceEquals(Singleton<AbstractGame>.Instance, game)",
            "ReferenceEquals(Singleton<GameWorld>.Instance, world)",
            "ArenaSession.IsExactActiveRaid(game, world)",
            "ReferenceEquals(Singleton<IBotGame>.Instance?.BotsController, controller)",
            "ReferenceEquals(controller?.BotSpawner, spawner)",
            "data?.StopSpawn()",
            "ReleaseReservedIfCurrent(",
            "RetireStaleSpawn(bot, role)");
        Assert(burst.IndexOf("await BotCreationData.Create", StringComparison.Ordinal)
               < burst.IndexOf("if (!IsSpawnLeaseCurrent(operationGeneration", StringComparison.Ordinal),
            "profile generation revalidates the exact raid lease immediately after its await");

        CheckTacticalIntegration(settings, tactical, combat, respawn, matchService);
    }

    private static void CheckTacticalIntegration(
        string settings,
        string tactical,
        string combat,
        string respawn,
        string matchService)
    {
        RequireAll(settings,
            "TacticalTeamRoundsToWin",
            "\"RoundsToWin\"",
            "new AcceptableValueRange<int>(1, 9)",
            "TacticalTeamSpectator",
            "\"SpectatorAfterDeath\"");

        string matchStart = Slice(
            tactical,
            "public void OnMatchStart(ArenaMatch match)",
            "public void Tick(float deltaTime)");
        RequireAll(matchStart,
            "TeamArenaLoadoutSnapshot.TryCapture(",
            "_teamGame = teamGame",
            "_teamRoundsToWin = Mathf.Clamp(Settings.TacticalTeamRoundsToWin.Value, 1, 9)",
            "_phase = Phase.Staging");
        Assert(matchStart.IndexOf("TeamArenaLoadoutSnapshot.TryCapture(", StringComparison.Ordinal)
               < matchStart.IndexOf("_phase = Phase.Staging", StringComparison.Ordinal),
            "protected 5v5 captures its process-local entry loadout and round target before combat staging");

        string tick = Slice(
            tactical,
            "public void Tick(float deltaTime)",
            "private void TickLive()");
        RequireAll(tick,
            "_teamContinuation == TeamArenaRoundContinuation.RebindNextRound",
            "EnterTeamRebinding()",
            "EndTeamRaid()",
            "case Phase.Rebinding:",
            "TickTeamRebinding()");

        string endRound = Slice(
            tactical,
            "private void EndTeamRound(bool? won, string reason)",
            "private void EnterTeamRebinding()");
        RequireAll(endRound,
            "ArenaSpectatorService.Stop(\"the protected 5v5 round ended\")",
            "TeamArenaPursuit.Reset()",
            "TeamArenaRoundPolicy.Evaluate(",
            "_teamRoundsToWin",
            "TeamArenaRoundContinuation.EndMatchWon => true",
            "TeamArenaRoundContinuation.EndMatchLost => false",
            "TeamArenaRoundContinuation.EndNoContest => null");
        Assert(endRound.IndexOf("ArenaSpectatorService.Stop", StringComparison.Ordinal)
               < endRound.IndexOf("RetireSquad()", StringComparison.Ordinal)
               && endRound.IndexOf("TeamArenaPursuit.Reset()", StringComparison.Ordinal)
               < endRound.IndexOf("RetireSquad()", StringComparison.Ordinal),
            "round completion releases camera and pursuit ownership before retiring frozen bodies");

        string rebindEnum = Slice(
            tactical,
            "private enum TeamRebindStep",
            "private enum TeamRetirementDecision");
        RequireAll(rebindEnum,
            "Begin",
            "AwaitRetirement",
            "AwaitLocalRespawn",
            "RebuildLeader",
            "PrepareProfiles",
            "AwaitProfiles",
            "StartFollowers",
            "AwaitFollowers",
            "AdoptFollowers");

        string rebindTick = Slice(
            tactical,
            "private void TickTeamRebinding()",
            "private void BeginTeamRebind()");
        RequireAll(rebindTick,
            "PitFireTeamCompat.PersistenceSuppressionActive",
            "ReferenceEquals(",
            "_teamGame",
            "BeginTeamRebind()",
            "TickTeamRetirement()",
            "RebuildTeamLeader()",
            "BeginTeamFollowerProfiles()",
            "TickTeamFollowerProfiles()",
            "StartTeamFollowers()",
            "TickTeamFollowers()",
            "AdoptTeamFollowers()");

        string live = Slice(
            tactical,
            "private void TickTeamLive()",
            "private void TryStartTeamSpectator()");
        RequireAll(live,
            "PitFireTeamCompat.EnsureHostility(_boundTeamEnemies, _boundLocalPlayer)",
            "if (_playerDown)",
            "TeamArenaPursuit.Tick(_teamRoundEpoch, true, followers, _boundTeamEnemies)");
        RequireAll(tactical,
            "TeamArenaPursuit.BeginRound(_teamRoundEpoch)",
            "TryStartTeamSpectator()",
            "Settings.TacticalTeamSpectator.Value",
            "followerProfiles.Concat(enemyProfiles).ToArray()",
            "_teamRoundEpoch == epoch",
            "ReferenceEquals(_boundLocalPlayer, deadPlayer)",
            "BuildTeamSpectatorTargets(",
            "ArenaSpectatorService.RequestStart(request, out string problem)");

        string damageGuard = Slice(
            tactical,
            "internal bool ShouldBlockStagingDamage(Player victim)",
            "private void EndTeamRound(bool? won, string reason)");
        RequireAll(damageGuard,
            "_phase == Phase.Staging",
            "_phase == Phase.Aftermath",
            "_phase == Phase.Rebinding",
            "FailTeamTransition(reason)");
        RequireAll(combat,
            "ShouldBlockProtectedTeamMutation",
            "FailClosedStagingGuard");

        string retirement = Slice(
            tactical,
            "private void TickTeamRetirement()",
            "private TeamRetirementDecision ObserveTeamEnemyRetirement");
        int arm = retirement.IndexOf("_teamLoadoutSnapshot.TryArm(_teamRoundEpoch", StringComparison.Ordinal);
        int validateDeadLeader = retirement.IndexOf(
            "TryValidateDeadTeamLeaderBody(_teamGame, deadPlayer",
            StringComparison.Ordinal);
        int prepare = retirement.IndexOf("_teamLoadoutSnapshot.TryPrepare(", StringComparison.Ordinal);
        int authorize = retirement.IndexOf("_teamRespawnAuthorizationAvailable = true", StringComparison.Ordinal);
        int release = retirement.IndexOf("_playerDown = false", StringComparison.Ordinal);
        Assert(validateDeadLeader >= 0
               && arm > validateDeadLeader
               && prepare > arm
               && authorize > prepare
               && release > authorize,
            "the exact unregistered corpse is revalidated before its snapshot is armed, prepared and released to respawn");

        string failedRebind = Slice(
            tactical,
            "private void FailTeamTransition(string reason)",
            "private string DescribeTeamRebindStep()");
        Assert(failedRebind.Contains(
                   "_teamContinuation = TeamArenaRoundContinuation.EndNoContest",
                   StringComparison.Ordinal)
               && failedRebind.Contains(
                   "PitFireTeamRoundLifecycle.CancelFollowerSpawnLease(",
                   StringComparison.Ordinal)
               && failedRebind.Contains("_authorizedDeadTeamPlayer = null", StringComparison.Ordinal)
               && failedRebind.Contains("_teamRespawnAuthorizationConsumed = false", StringComparison.Ordinal)
               && !failedRebind.Contains("TeamArenaRoundPolicy.Evaluate(", StringComparison.Ordinal)
               && !failedRebind.Contains("RoundsWon =", StringComparison.Ordinal)
               && !failedRebind.Contains("RoundsLost =", StringComparison.Ordinal),
            "a failed rebuild cancels detached follower work and respawn authority before ending no-contest without double scoring");

        string authorization = Slice(
            tactical,
            "internal bool TryTakeTeamRespawnAuthorization(",
            "public void OnMatchEnd()");
        RequireAll(authorization,
            "_phase != Phase.Rebinding",
            "_teamRebindStep != TeamRebindStep.AwaitLocalRespawn",
            "!ReferenceEquals(game, _teamGame)",
            "!ReferenceEquals(deadPlayer, _authorizedDeadTeamPlayer)",
            "_preparedTeamRespawn.RoundEpoch != _teamRoundEpoch",
            "!PitFireTeamCompat.RoundTransitionActive",
            "lifecycle.Stage != PitFireTeamRoundLifecycleStage.ReadyForLeader",
            "TryValidateDeadTeamLeaderBody(game, deadPlayer",
            "_teamRespawnAuthorizationAvailable = false",
            "_teamRespawnAuthorizationConsumed = true");

        string matchEnd = Slice(
            tactical,
            "public void OnMatchEnd()",
            "public void ReadyUp()");
        RequireAll(matchEnd,
            "_squadEpoch++",
            "_phase = Phase.Ended",
            "PitFireTeamRoundLifecycle.CancelFollowerSpawnLease(\"Tactical match ended\")",
            "_squad.Clear()",
            "_teamGame = null");
        Assert(matchEnd.IndexOf("_squadEpoch++", StringComparison.Ordinal)
               < matchEnd.IndexOf("_squad.Clear()", StringComparison.Ordinal),
            "match end invalidates asynchronous opponent observers before releasing frozen bodies");
        Assert(matchEnd.IndexOf("PitFireTeamRoundLifecycle.CancelFollowerSpawnLease(", StringComparison.Ordinal)
               < matchEnd.IndexOf("_squad.Clear()", StringComparison.Ordinal),
            "match end cancels detached PitFireTeam follower work before releasing the exact roster");
        string liveLeaderIdentity = Slice(
            tactical,
            "private bool TryReadBoundLocalPlayer(out bool alive, out string problem)",
            "private bool TryFreezeTeamFollowers(");
        RequireAll(liveLeaderIdentity,
            "IPlayer[] registeredWithProfile = world.RegisteredPlayers",
            "registeredWithProfile.Length == 0",
            "registeredWithProfile.Length == 1",
            "ReferenceEquals(registeredWithProfile[0], _boundLocalPlayer)",
            "GameWorld has {registeredWithProfile.Length} active bodies under the local profile");
        string soloStart = Slice(
            tactical,
            "private void StartRound()",
            "private static void RetireBot(");
        RequireAll(soloStart,
            "epoch != _squadEpoch",
            "_phase != Phase.Live",
            "_squad.Count >= _squadRequested",
            "RetireBot(bot, \"stale solo Tactical squad member\")");
        string soloEnd = Slice(
            tactical,
            "private void EndRound(bool won)",
            "\n}");
        RequireAll(soloEnd, "_squadEpoch++", "RetireSquad()");
        Assert(soloEnd.IndexOf("_squadEpoch++", StringComparison.Ordinal)
               < soloEnd.IndexOf("RetireSquad()", StringComparison.Ordinal),
            "solo Tactical invalidates detached squad observers before retiring the completed-round roster");
        RequireAll(tactical,
            "TryValidateCurrentTeamLeaderBody(player, out string bodyProblem)",
            "TryValidateCurrentTeamLeaderBody(",
            "the current local PMC drifted before PitFireTeam leader rebind",
            "the current local PMC drifted before PitFireTeam follower adoption",
            "world.RegisteredPlayers.Count(candidate =>",
            "world.RegisteredPlayers",
            "world.GetEverExistedPlayerByID(profileId)",
            "ReferenceEquals(game.Profile, deadPlayer.Profile)",
            "ReferenceEquals(_teamGame.Profile, player.Profile)",
            "ReferenceEquals(world.MainPlayer, deadPlayer)",
            "ReferenceEquals(world.MainPlayer, expectedPlayer)");
        string rebuildLeader = Slice(
            tactical,
            "private void RebuildTeamLeader()",
            "private void BeginTeamFollowerProfiles()");
        int revalidateBeforeRebuild = rebuildLeader.IndexOf(
            "TryValidateCurrentTeamLeaderBody(",
            StringComparison.Ordinal);
        int rebuildBoss = rebuildLeader.IndexOf(
            "PitFireTeamRoundLifecycle.TryRebuildBoss(",
            StringComparison.Ordinal);
        Assert(revalidateBeforeRebuild >= 0
               && rebuildBoss > revalidateBeforeRebuild,
            "the exact living local body is rechecked immediately before PFT creates its leader wrapper");
        string adoptFollowers = Slice(
            tactical,
            "private void AdoptTeamFollowers()",
            "private bool TryValidateDeadTeamLeaderBody(");
        int revalidateBeforeAdoption = adoptFollowers.IndexOf(
            "TryValidateCurrentTeamLeaderBody(",
            StringComparison.Ordinal);
        int adoptRoster = adoptFollowers.IndexOf(
            "PitFireTeamCompat.TryAdoptRoundRoster(",
            StringComparison.Ordinal);
        Assert(revalidateBeforeAdoption >= 0
               && adoptRoster > revalidateBeforeAdoption,
            "the exact living local body is rechecked immediately before PFT publishes the new follower roster");
        RequireAll(matchService,
            "Current?.Mode is not TacticalMode tactical",
            "tactical.TryTakeTeamRespawnAuthorization(");
        RequireAll(respawn,
            "ArenaMatchService.TryTakeTeamRespawnAuthorization(",
            "RespawnProtectedTeamPlayer(",
            "prepared.TryCommit(profile, out TeamArenaLoadoutRestoreResult result)",
            "AbortProtectedTeamRespawn(");
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

    private static string Slice(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException($"production source lost method marker: {startMarker}");

        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        if (end < 0)
            throw new InvalidOperationException($"production source lost following marker: {endMarker}");

        return source.Substring(start, end - start);
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
