using ArenaMode.Modes;
using ArenaMode.Spectator;
using ArenaMode.Bots;

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
            CheckPursuitPolicy();
            CheckPursuitOwnerPolicy();
            CheckPftLeasePolicy();
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

    private static void CheckPursuitPolicy()
    {
        static TeamArenaApproachDecision Decide(
            float squaredDistance,
            bool visible = false,
            bool shootable = false,
            bool healing = false,
            bool reloading = false,
            bool throwing = false,
            bool door = false,
            bool immediateDanger = false)
            => TeamArenaPursuitPolicy.Decide(
                squaredDistance,
                visible,
                shootable,
                healing,
                reloading,
                throwing,
                door,
                immediateDanger);

        float reachDistance = TeamArenaPursuitPolicy.DestinationReachDistanceMeters;
        Assert(Decide((reachDistance + 1f) * (reachDistance + 1f))
               == TeamArenaApproachDecision.Approach,
            "an idle separated bot is ordered toward its nearest opponent");
        Assert(Decide(reachDistance * reachDistance)
               == TeamArenaApproachDecision.Approach,
            "an unseen target inside one metre may still be separated by Labs geometry");
        Assert(Decide(36f, visible: false, shootable: false)
               == TeamArenaApproachDecision.Approach,
            "an unseen opponent within ordinary combat range still receives an approach route");
        Assert(Decide(400f, visible: true, shootable: true)
               == TeamArenaApproachDecision.Engaged,
            "a bot with a visible shootable enemy keeps combat ownership");
        Assert(Decide(400f, visible: true, shootable: false)
               == TeamArenaApproachDecision.Approach,
            "visibility without a firing solution does not permit a distant standoff");
        Assert(Decide(400f, healing: true) == TeamArenaApproachDecision.Busy
               && Decide(400f, reloading: true) == TeamArenaApproachDecision.Busy
               && Decide(400f, throwing: true) == TeamArenaApproachDecision.Busy
               && Decide(400f, door: true) == TeamArenaApproachDecision.Busy,
            "medicine, reload, grenade and door actions are never interrupted");
        Assert(Decide(400f, immediateDanger: true)
               == TeamArenaApproachDecision.ImmediateDanger,
            "grenade, BTR, artillery and flash danger releases pursuit movement immediately");
        Assert(Decide(float.NaN) == TeamArenaApproachDecision.InvalidDistance
               && Decide(float.PositiveInfinity) == TeamArenaApproachDecision.InvalidDistance,
            "invalid world distances fail closed");

        var liveTargets = new HashSet<string>(new[] { "A", "B", "C", "D", "E" }, StringComparer.Ordinal);
        var rejectedTargets = new HashSet<string>(new[] { "A", "B", "C" }, StringComparer.Ordinal);
        TeamArenaPursuitPolicy.PrepareRejectedTargetPass(rejectedTargets, liveTargets);
        Assert(rejectedTargets.SetEquals(new[] { "A", "B", "C" }),
            "rejected pursuit targets remain excluded until every live candidate has been tried");

        rejectedTargets.Add("D");
        rejectedTargets.Add("E");
        TeamArenaPursuitPolicy.PrepareRejectedTargetPass(rejectedTargets, liveTargets);
        Assert(rejectedTargets.Count == 0,
            "a completed pursuit pass resets only after all five candidates were considered");

        rejectedTargets.Add("dead");
        TeamArenaPursuitPolicy.PrepareRejectedTargetPass(rejectedTargets, liveTargets);
        Assert(rejectedTargets.Count == 0,
            "dead pursuit identities are pruned without excluding a live target");
    }

    private static void CheckPursuitOwnerPolicy()
    {
        Assert(TeamArenaPursuitOwnerPolicy.Evaluate(
                   publicationAccepted: false,
                   executionStarted: false,
                   ageSeconds: 0f)
               == TeamArenaConsumerDecision.Fallback,
            "a missing movement consumer immediately preserves the guarded fallback");
        Assert(TeamArenaPursuitOwnerPolicy.Evaluate(
                   publicationAccepted: true,
                   executionStarted: false,
                   ageSeconds: 0.5f)
               == TeamArenaConsumerDecision.AwaitingAcknowledgement,
            "a newly published generation receives a short bounded execution window");
        Assert(TeamArenaPursuitOwnerPolicy.Evaluate(
                   publicationAccepted: true,
                   executionStarted: true,
                   ageSeconds: 30f)
               == TeamArenaConsumerDecision.Active,
            "a concrete execution acknowledgement proves ownership after the startup window");
        Assert(TeamArenaPursuitOwnerPolicy.Evaluate(
                   publicationAccepted: true,
                   executionStarted: false,
                   ageSeconds: TeamArenaPursuitOwnerPolicy.AcknowledgementTimeoutSeconds)
               == TeamArenaConsumerDecision.DisableAndFallback,
            "an unacknowledged registered layer times out into the fallback");
        Assert(TeamArenaPursuitOwnerPolicy.Evaluate(true, false, float.NaN)
                   == TeamArenaConsumerDecision.DisableAndFallback
               && TeamArenaPursuitOwnerPolicy.Evaluate(true, false, -0.1f)
                   == TeamArenaConsumerDecision.DisableAndFallback,
            "malformed or time-reversed acknowledgement ages fail closed into fallback");

        Assert(TeamArenaPursuitOwnerPolicy.CanAcceptConsumerReport(
                consumerMatches: true,
                sourceReady: true,
                activeEpoch: 3,
                leaseEpoch: 3,
                leaseGeneration: 42,
                reportedEpoch: 3,
                reportedGeneration: 42),
            "an exact consumer, source, epoch and generation report is accepted");
        Assert(!TeamArenaPursuitOwnerPolicy.CanAcceptConsumerReport(
                   true, true, 3, 3, 42, 2, 42)
               && !TeamArenaPursuitOwnerPolicy.CanAcceptConsumerReport(
                   true, true, 3, 3, 42, 3, 41)
               && !TeamArenaPursuitOwnerPolicy.CanAcceptConsumerReport(
                   false, true, 3, 3, 42, 3, 42)
               && !TeamArenaPursuitOwnerPolicy.CanAcceptConsumerReport(
                   true, false, 3, 3, 42, 3, 42),
            "stale generations, stale epochs, foreign consumers and unbound layers cannot acknowledge execution");

        Assert(TeamArenaPursuitOwnerPolicy.HasDirectionalProgress(
                   TeamArenaPursuitOwnerPolicy.ProgressMeters)
               && !TeamArenaPursuitOwnerPolicy.HasDirectionalProgress(0f)
               && !TeamArenaPursuitOwnerPolicy.HasDirectionalProgress(float.NaN),
            "only source movement toward its target counts; a stationary source gets no credit when the target moves");
        Assert(TeamArenaPursuitOwnerPolicy.IsFreshHeartbeat(10f, 8.5f, 1.5f)
               && !TeamArenaPursuitOwnerPolicy.IsFreshHeartbeat(10.01f, 8.5f, 1.5f)
               && !TeamArenaPursuitOwnerPolicy.IsFreshHeartbeat(9f, 10f, 1.5f)
               && !TeamArenaPursuitOwnerPolicy.IsFreshHeartbeat(float.NaN, 8f, 1.5f),
            "only a finite, ordered consumer heartbeat inside the exact age boundary proves live execution");
    }

    private static void CheckPftLeasePolicy()
    {
        static TeamArenaPftLeaseState Evaluate(
            bool tracked = true,
            bool cancelPending = false,
            bool raw = false,
            bool rawPush = false,
            bool targetLock = false,
            bool lockMatches = false,
            bool pushLease = false,
            bool sequenceMatches = false,
            bool tacticalLease = false,
            bool tacticalRawMatches = false,
            bool tacticalActionMatches = false)
            => TeamArenaPftLeasePolicy.Evaluate(
                tracked,
                cancelPending,
                raw,
                rawPush,
                targetLock,
                lockMatches,
                pushLease,
                sequenceMatches,
                tacticalLease,
                tacticalRawMatches,
                tacticalActionMatches);

        Assert(Evaluate(tracked: false, raw: true)
               == TeamArenaPftLeaseState.Stale,
            "Arena's first post-death route may replace an older follower command");
        Assert(Evaluate(raw: true, rawPush: true, pushLease: true, sequenceMatches: true)
               == TeamArenaPftLeaseState.Active,
            "an exact Arena push sequence with the raw Push command remains active");
        Assert(Evaluate(raw: true, rawPush: true, targetLock: true, lockMatches: false,
                   pushLease: true, sequenceMatches: true)
               == TeamArenaPftLeaseState.Foreign,
            "a mismatched ordered target lock defeats otherwise matching raw push metadata");
        Assert(Evaluate(targetLock: true, lockMatches: true, pushLease: true, sequenceMatches: true)
               == TeamArenaPftLeaseState.Active,
            "an exact ordered target lock proves ownership after the raw push was consumed");
        Assert(Evaluate(cancelPending: true, raw: true, rawPush: true,
                   pushLease: true, sequenceMatches: true)
               == TeamArenaPftLeaseState.CancelPending,
            "a pending ordered-push cancellation blocks a replacement command");
        Assert(Evaluate(raw: true, rawPush: true, pushLease: true, sequenceMatches: false)
               == TeamArenaPftLeaseState.Foreign,
            "a newer PFT push sequence is preserved as a foreign producer command");
        Assert(Evaluate(pushLease: true, sequenceMatches: false)
               == TeamArenaPftLeaseState.Stale,
            "a vanished superseded command releases Arena's stale push tracking");
        Assert(Evaluate(raw: true, targetLock: true, lockMatches: true,
                   pushLease: true, sequenceMatches: true)
               == TeamArenaPftLeaseState.Foreign,
            "a foreign raw command wins over an old matching ordered target lock");
        Assert(Evaluate(raw: true, tacticalLease: true, tacticalRawMatches: true)
               == TeamArenaPftLeaseState.Active,
            "an exact Arena tactical-point command remains active");
        Assert(Evaluate(tacticalLease: true, tacticalActionMatches: true)
               == TeamArenaPftLeaseState.Active,
            "a consumed tactical point stays active only while PFT owns its exact physical action");
        Assert(Evaluate(raw: true, tacticalLease: true, tacticalActionMatches: true)
               == TeamArenaPftLeaseState.Foreign,
            "a newer raw follower command overrides an old consumed tactical action");
        Assert(Evaluate(tacticalLease: true)
               == TeamArenaPftLeaseState.Stale,
            "a consumed tactical point without its physical action is not treated as active ownership");
        Assert(Evaluate(raw: true)
               == TeamArenaPftLeaseState.Foreign,
            "a new producer command is preserved after Arena has owned a route");
    }

    private static void CheckSourceContract()
    {
        string root = FindRepositoryRoot();
        string service = Read(root, "ArenaMode.Client", "Spectator", "ArenaSpectatorService.cs");
        string healthEffects = Read(root, "ArenaMode.Client", "Spectator", "SpectatorHealthEffects.cs");
        string cameraPatch = Read(root, "ArenaMode.Client", "Spectator", "ArenaSpectatorCameraPatches.cs");
        string killcam = Read(root, "ArenaMode.Client", "Spectator", "KillcamReplayCompat.cs");
        string freecam = Read(root, "ArenaMode.Client", "Spectator", "TerkoizFreecamProbe.cs");
        string plugin = Read(root, "ArenaMode.Client", "Plugin.cs");
        string controller = Read(root, "ArenaMode.Client", "ArenaController.cs");
        string project = Read(root, "ArenaMode.Client", "ArenaMode.Client.csproj");
        string pursuit = Read(root, "ArenaMode.Client", "Bots", "TeamArenaPursuit.cs");
        string pursuitBridge = Read(root, "ArenaMode.Client", "Bots", "TeamArenaPursuitLeaseBridge.cs");
        string pursuitOwnerPolicy = Read(root, "ArenaMode.Client", "Bots", "TeamArenaPursuitOwnerPolicy.cs");
        string pftLeasePolicy = Read(root, "ArenaMode.Client", "Bots", "TeamArenaPftLeasePolicy.cs");
        string bigBrainProject = Read(root, "ArenaMode.BigBrainCompat", "ArenaMode.BigBrainCompat.csproj");
        string bigBrainPlugin = Read(root, "ArenaMode.BigBrainCompat", "Plugin.cs");
        string bigBrainLayer = Read(root, "ArenaMode.BigBrainCompat", "ArenaPursuitLayer.cs");
        string bigBrainLogic = Read(root, "ArenaMode.BigBrainCompat", "ArenaPursuitLogic.cs");
        string bigBrainRuntime = Read(root, "ArenaMode.BigBrainCompat", "ArenaPursuitRuntime.cs");
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
        RequireAll(healthEffects,
            "!ArenaSession.IsActive || !PitFireTeamCompat.TeamArenaActive",
            "expectedDeadPlayer.HealthController?.IsAlive != false",
            "ReferenceEquals(BoundPlayerField.GetValue(controller), expectedDeadPlayer)",
            "List<EffectsController.EffectAccumulator>",
            "accumulator.DeleteEffect(accumulator.ActiveEffects[0])",
            "accumulator.ActiveEffects.Count >= before",
            "wiggle._stimulatorBuffs?.Clear()",
            "motionBlur._stimulatorBuffs?.Clear()",
            "flash._indoorEffects?.Clear()",
            "Shader.SetGlobalFloat(\"_BlindnessCoef\", 0f)",
            "blend.Toggle(false)",
            "frostbite._effect.Radius = 1f",
            "sharpen.HealthDesaturate = 0f",
            "sharpen.MaskDesaturate = 0f",
            "provider.HealthDesaturate = 0f",
            "eyeBurn.enabled = false",
            "drops._freeDrops.Enqueue(drops._dropsToRender.Dequeue())",
            "ClearRenderTexture(BloodTextureField.GetValue(blood) as RenderTexture)",
            "LastBloodHitField.SetValue(blood, float.MinValue)",
            "LastHeavyBleedHitField.SetValue(blood, float.MinValue)",
            "BloodLockedField.SetValue(blood, false)",
            "GL.Clear(clearDepth: true, clearColor: true, Color.black)",
            "DeathEffectsHelper.ClearDeathScreenEffects()");
        RequireAll(healthEffects,
            "_automaticAttempts >= 2",
            "_automaticAttempts++",
            "if (!force)",
            "allocation-free for the rest");
        Assert(!healthEffects.Contains("RemoveNegativeEffects", StringComparison.Ordinal)
               && !healthEffects.Contains("PauseAllEffects", StringComparison.Ordinal)
               && !healthEffects.Contains(".ActiveEffects.Clear(", StringComparison.Ordinal)
               && !healthEffects.Contains("Object.Destroy", StringComparison.Ordinal)
               && !healthEffects.Contains("Destroy(controller", StringComparison.Ordinal),
            "spectator cleanup changes only first-person presentation and neither health state nor persistent camera components");
        string requestStart = Slice(
            service,
            "internal static bool RequestStart(",
            "internal static void Tick()");
        Assert(requestStart.IndexOf("Stop(\"superseded spectator request\")", StringComparison.Ordinal)
               < requestStart.IndexOf("SpectatorHealthEffects.ClearForDeadLocal(request.DeadPlayer)", StringComparison.Ordinal)
               && requestStart.IndexOf("SpectatorHealthEffects.ClearForDeadLocal(request.DeadPlayer)", StringComparison.Ordinal)
               < requestStart.IndexOf("Arena spectator pending for epoch", StringComparison.Ordinal),
            "an accepted exact-body lease clears health visuals before Killcam's delayed playback can begin");
        string spectatorTick = Slice(
            service,
            "private static void TickUnsafe()",
            "internal static void Stop(string reason)");
        RequireAll(spectatorTick,
            "SpectatorHealthEffects.ClearForDeadLocal(_request.DeadPlayer)");
        string startFollowing = Slice(
            service,
            "private static void StartFollowing()",
            "private static bool TryValidateLease(");
        RequireAll(startFollowing,
            "DeathEffectsHelper.ClearDeathScreenEffects()",
            "SpectatorHealthEffects.ClearForDeadLocal(_request.DeadPlayer, force: true)");

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
            "TeamArenaPursuitLeaseBridge.Reset()",
            "TeamArenaPursuitLeaseBridge.BeginEpoch(epoch)",
            "TeamArenaPursuitLeaseBridge.Publish(",
            "TeamArenaPursuitLeaseBridge.TryConsumeRejection(",
            "TeamArenaPursuitOwnerPolicy.Evaluate(",
            "TeamArenaPursuitLeaseBridge.HasStarted(",
            "BigBrainUnavailableSources.Add(sourceProfile)",
            "TeamArenaPursuitLeaseBridge.Revoke(sourceProfile, epoch)",
            "\"bigbrain\"",
            "!localPlayerDown",
            "epoch != _epoch",
            "!ArenaSession.IsActive",
            "!PitFireTeamCompat.TeamArenaActive",
            "Time.time < _nextSweepAt",
            "candidate.BotState != EBotState.Active",
            "candidate.HealthController?.IsAlive != true",
            "!IsFinite(candidate.Position)",
            "!profiles.Add(profileId)",
            "RefreshSafely(ally, liveOpponents, epoch, follower: true)",
            "RefreshSafely(opponent, liveAllies, epoch, follower: false)",
            "SelectEngagedTarget(source, candidates)",
            "SelectRetainedTarget(sourceProfile, candidates, rejectedTargets)",
            "PrepareRejectedTargetPass(rejectedTargets, liveTargets)",
            "current?.IsVisible == true && current.CanShoot == true",
            "ReferenceEquals(candidate.GetPlayer, current.Person)",
            "knownEnemies.TryGetValue(candidate.GetPlayer, out EnemyInfo enemy)",
            "enemy?.IsVisible != true",
            "distance < bestDistance",
            "string.CompareOrdinal(profile, bestProfile) < 0",
            "group.AddEnemy(target, EBotEnemyCause.bossKillArena)",
            "group.ReportAboutEnemy(target, EEnemyPartVisibleType.Sence, source)",
            "info.IsLastPositionChecked = false",
            "source.Memory.GoalEnemy = enemyInfo",
            "TeamArenaPursuitPolicy.Decide(",
            "HasPendingMedicalWork(source)",
            "source.DoorOpener?.Interacting == true",
            "source.StandBy?.Activate()",
            "HasImmediateDanger(source)",
            "SetPushEnemy",
            "RequestOrderedPushCancel",
            "GetFollowerByProfileId",
            "SetCombatMoveToPointTactical",
            "PushEnemyIssueSequence",
            "TryPeekActiveCommand",
            "TryGetOrderedPushTargetLock",
            "PftPushLeasesBySource",
            "PftTacticalLeasesBySource",
            "currentSequence != pushLease.Sequence",
            "Keep Arena's old",
            "if (hasRawCommand || hasTargetLock || cancelPending)",
            "PftTacticalLeaseMatches(follower, tacticalLease)",
            "PftConsumedTacticalLeaseMatches(source, follower, tacticalLease)",
            "GetActiveLogic",
            "pitTeam.BigBrain.Actions.CombatGoToPointTacticalAction",
            "command.thereTactical",
            "ReferenceEquals(followerBot, source)",
            "Time.time >= lease.UntilTime",
            "TeamArenaPftLeasePolicy.Evaluate(",
            "LogPftLeaseDeferredOnce(epoch, source, target, leaseState)",
            "HasOrderedPushCancelRequest",
            "TryConsumeOrderedPushCancelRequest",
            "UsesPftSainFollowerCombat(source, nearest, epoch)",
            "\"sain-pft-run\"",
            "ownerRequired: true",
            "TrySyncEnemyState",
            "new object[] { source, target, true }",
            "GetSAIN",
            "CheckAddEnemy",
            "UpdateLastSeenPosition",
            "SetInCombat",
            "Mover",
            "RunToPoint",
            "TeamArenaPursuitPolicy.DestinationReachDistanceMeters",
            "mustHaveCompletePath: true",
            "source.MoveToEnemyData.TryMoveToEnemy(target.Position)",
            "ObserveRouteProgress(source, target, squaredDistance, epoch, adapter)",
            "Vector3.Dot(",
            "TeamArenaPursuitOwnerPolicy.HasDirectionalProgress(approachProgress)",
            "RejectTargetForCurrentPass(source, target, epoch)",
            "LogStalledOnce(",
            "return _pftAssemblyDetected ? PftPushResult.Rejected : PftPushResult.Unavailable",
            "return _sainAssemblyDetected ? SainMoveResult.Rejected : SainMoveResult.Unavailable",
            "Only profile IDs, timers and value snapshots survive a sweep");
        Assert(!System.Text.RegularExpressions.Regex.IsMatch(
                   pursuit,
                   @"private\s+static(?:\s+readonly)?\s+[^;\r\n]*BotOwner[^;\r\n]*;")
               && !pursuit.Contains("source.GoToPoint", StringComparison.Ordinal)
               && !pursuit.Contains("mustHaveWay: true", StringComparison.Ordinal)
               && !pursuit.Contains("DoorOpener?.NearDoor", StringComparison.Ordinal)
               && !pursuit.Contains("Teleport", StringComparison.Ordinal)
               && !pursuit.Contains("LookSensor", StringComparison.Ordinal)
               && !pursuit.Contains("SetVisible", StringComparison.Ordinal)
               && !pursuit.Contains("distanceGain >= PftProgressMeters", StringComparison.Ordinal),
            "post-death pursuit retains no bot body and never fakes sight or uses teleport-capable movement");
        Assert(pursuit.IndexOf("TeamArenaPursuitLeaseBridge.TryConsumeRejection(", StringComparison.Ordinal)
                   < pursuit.IndexOf("TeamArenaPursuitLeaseBridge.Publish(", StringComparison.Ordinal),
            "a rejected consumer generation is handled before the source can publish another lease");
        int ownerPublish = pursuit.IndexOf(
            "&& TeamArenaPursuitLeaseBridge.Publish(",
            StringComparison.Ordinal);
        int followerFallbackPin = pursuit.IndexOf(
            "if (follower)\n            BigBrainUnavailableSources.Add(sourceProfile);",
            ownerPublish,
            StringComparison.Ordinal);
        int pftOwnerProbe = pursuit.IndexOf(
            "bool pftSainFollower = follower",
            followerFallbackPin,
            StringComparison.Ordinal);
        Assert(ownerPublish >= 0
               && followerFallbackPin > ownerPublish
               && pftOwnerProbe > followerFallbackPin,
            "a follower that entered fallback cannot be taken over by a late BigBrain registration");
        int bigBrainStall = pursuit.IndexOf(
            "if (string.Equals(adapter, \"bigbrain\", StringComparison.Ordinal))",
            StringComparison.Ordinal);
        int bigBrainDisable = pursuit.IndexOf(
            "BigBrainUnavailableSources.Add(sourceProfile)",
            bigBrainStall,
            StringComparison.Ordinal);
        int stalledTargetReject = pursuit.IndexOf(
            "RejectTargetForCurrentPass(source, target, epoch)",
            bigBrainDisable,
            StringComparison.Ordinal);
        Assert(bigBrainStall >= 0
               && bigBrainDisable > bigBrainStall
               && stalledTargetReject > bigBrainDisable,
            "a physically stalled BigBrain route disables its owner and revokes the lease through target rejection");

        RequireAll(pftLeasePolicy,
            "if (cancelPending)",
            "TeamArenaPftLeaseState.CancelPending",
            "if (!pushSequenceMatches)",
            "hasRawCommand || hasTargetLock",
            "TeamArenaPftLeaseState.Foreign",
            "rawIsPush || targetLockMatches",
            "tacticalRawMatches",
            "tacticalActionMatches",
            "hasTrackedRoute && (hasRawCommand || hasTargetLock)");

        RequireAll(pursuitOwnerPolicy,
            "AcknowledgementTimeoutSeconds = 2f",
            "TeamArenaConsumerDecision.AwaitingAcknowledgement",
            "TeamArenaConsumerDecision.DisableAndFallback",
            "CanAcceptConsumerReport(",
            "leaseGeneration == reportedGeneration",
            "HasDirectionalProgress(float sourceApproachMeters)",
            "IsFreshHeartbeat(");

        RequireAll(pursuitBridge,
            "RegisterConsumer(string consumerId)",
            "UnregisterConsumer(string consumerId)",
            "RegisterSource(string consumerId, string sourceProfileId)",
            "UnregisterSource(string consumerId, string sourceProfileId)",
            "public static bool HasExactTeamArenaContext()",
            "ReadySourceRegistrations.ContainsKey(source)",
            "public static void BeginEpoch(int epoch)",
            "public static bool Publish(",
            "MaximumLeaseLifetimeSeconds",
            "MaximumHeartbeatAgeSeconds = 1.5f",
            "public float IssuedAt { get; }",
            "public static bool ReportStarted(",
            "public static bool ReportRejected(",
            "TeamArenaPursuitOwnerPolicy.CanAcceptConsumerReport(",
            "TeamArenaPursuitOwnerPolicy.IsFreshHeartbeat(",
            "internal static bool HasStarted(",
            "internal static bool TryConsumeRejection(",
            "public static bool TryGet(",
            "public static bool Revoke(",
            "HasExactPostDeathContext()",
            "ArenaSession.IsExactActiveRaid(game, world)",
            "PitFireTeamCompat.TeamArenaActive",
            "world.MainPlayer?.HealthController?.IsAlive == false",
            "current.Generation != generation");
        Assert(!System.Text.RegularExpressions.Regex.IsMatch(
                   pursuitBridge,
                   @"private\s+static(?:\s+readonly)?\s+[^;\r\n]*(?:BotOwner|Player)[^;\r\n]*;")
               && !pursuitBridge.Contains("Dictionary<string, BotOwner>", StringComparison.Ordinal)
               && !pursuitBridge.Contains("Dictionary<string, Player>", StringComparison.Ordinal),
            "the cross-assembly movement lease retains identities and values rather than bot bodies");

        RequireAll(bigBrainProject,
            "<ProjectReference Include=\"..\\ArenaMode.Client\\ArenaMode.Client.csproj\" Private=\"false\" />",
            "<Reference Include=\"DrakiaXYZ-BigBrain\">",
            "<Private>false</Private>");
        RequireAll(bigBrainPlugin,
            "[BepInDependency(ArenaMode.Plugin.PluginGuid, ArenaMode.Plugin.PluginVersion)]",
            "[BepInDependency(BigBrainPluginGuid, SupportedBigBrainVersion)]",
            "SupportedBigBrainVersion = \"1.5.0\"",
            "PursuitLayerPriority = 74",
            "BrainManager.AddCustomLayer(",
            "BrainManager.CustomLayersReadOnly.TryGetValue",
            "TeamArenaPursuitLeaseBridge.RegisterConsumer(PluginGuid)",
            "TeamArenaPursuitLeaseBridge.UnregisterConsumer(PluginGuid)");
        RequireAll(bigBrainLayer,
            "ArenaPursuitRuntime.HasExactTeamArenaContext()",
            "ReleaseSourceRegistration()",
            "TeamArenaPursuitLeaseBridge.RegisterSource(",
            "EnsureSourceRegistered()",
            "OnPlayerDeadOrUnspawn += OnPlayerDeadOrUnspawn",
            "TeamArenaPursuitLeaseBridge.UnregisterSource(",
            "ArenaPursuitRuntime.TryGetUsableLease(",
            "lease.Generation");
        int layerConstructor = bigBrainLayer.IndexOf("public ArenaPursuitLayer(", StringComparison.Ordinal);
        int layerGetName = bigBrainLayer.IndexOf("public override string GetName()", StringComparison.Ordinal);
        Assert(layerConstructor >= 0
               && layerGetName > layerConstructor
               && !bigBrainLayer.Substring(layerConstructor, layerGetName - layerConstructor)
                   .Contains("RegisterSource(", StringComparison.Ordinal),
            "a merely constructed BigBrain wrapper cannot advertise a movement-ready source");
        RequireAll(bigBrainLogic,
            "_nativeRunToEnemy = new ArenaRunToEnemy(botOwner)",
            "UnresolvedArrivalGraceSeconds = 1.5f",
            "BotOwner.MovementResume()",
            "BotOwner.MoveToEnemyData.TryMoveToEnemy(lease.Destination)",
            "_nativeRunToEnemy.UpdateNodeByBrain(null)",
            "TrackUnresolvedArrival(",
            "out bool unresolvedArrivalExpired",
            "if (unresolvedArrivalExpired)",
            "if (waitingAtUnresolvedArrival)",
            "BotOwner.Mover?.HasPathAndNoComplete != true",
            "BotOwner.WeaponManager?.TryReloadWeaponOrUnderbarrelLauncher()",
            "TeamArenaPursuitLeaseBridge.ReportStarted(",
            "TeamArenaPursuitLeaseBridge.ReportRejected(",
            "lease.Generation",
            "BotOwner?.Mover?.Stop()");
        RequireAll(bigBrainLogic,
            "private sealed class ArenaRunToEnemy : RunToEnemy",
            "internal const float ArenaReachDistanceMeters = 1f",
            "public override void UpdateNodeByBrain(CoreActionResultParams data)",
            "bool canRun = DoorOpen() == DoorInteractionStatus.CanRun",
            "NotMovingCheck()",
            "PeriodChangeWeapon()",
            "_owner.Steering.LookToMovingDirection()",
            "_owner.LookData.SetLookPointByHearing()",
            "if (!_owner.Mover.HasPathAndNoComplete",
            "_owner.Mover.IsComeTo(ArenaReachDistanceMeters, onCover: false)",
            "ReachedDestinationThisUpdate = true",
            "InPoint()");
        RequireAll(bigBrainLogic,
            "private bool TrackUnresolvedArrival(",
            "BotOwner.Mover?.HasPathAndNoComplete == true",
            "_unresolvedArrivalEpoch == lease.Epoch",
            "_unresolvedArrivalGeneration == lease.Generation",
            "(lease.Destination - _unresolvedArrivalDestination).sqrMagnitude",
            "(lease.Destination - BotOwner.Position).sqrMagnitude",
            "_nativeRunToEnemy.ReachedDestinationThisUpdate",
            "Time.time - _unresolvedArrivalSince",
            ">= UnresolvedArrivalGraceSeconds",
            "ResetUnresolvedArrival()");
        int arenaHasPathGuard = bigBrainLogic.IndexOf(
            "if (!_owner.Mover.HasPathAndNoComplete",
            StringComparison.Ordinal);
        int arenaArrivalCheck = bigBrainLogic.IndexOf(
            "_owner.Mover.IsComeTo(ArenaReachDistanceMeters, onCover: false)",
            arenaHasPathGuard,
            StringComparison.Ordinal);
        Assert(arenaHasPathGuard >= 0
               && arenaArrivalCheck > arenaHasPathGuard,
            "zero remaining distance without a path cannot masquerade as Arena arrival");
        Assert(!bigBrainLogic.Contains(
                   "_owner.Settings.FileSettings.Move.REACH_DIST",
                   StringComparison.Ordinal),
            "the Arena action cannot inherit PitFireTeam's wider arrival radius");
        Assert(!bigBrainLogic.Contains("Teleport", StringComparison.Ordinal)
               && !bigBrainLogic.Contains("GoToPoint", StringComparison.Ordinal),
            "the BigBrain owner drives EFT's native enemy route and never teleports or bypasses its path helper");
        int actionPath = bigBrainLogic.IndexOf(
            "!BotOwner.MoveToEnemyData.TryMoveToEnemy(lease.Destination)",
            StringComparison.Ordinal);
        int unresolvedArrivalWait = bigBrainLogic.IndexOf(
            "if (waitingAtUnresolvedArrival)\n                return;",
            StringComparison.Ordinal);
        int actionNativeUpdate = bigBrainLogic.IndexOf(
            "_nativeRunToEnemy.UpdateNodeByBrain(null)",
            StringComparison.Ordinal);
        int actionStarted = bigBrainLogic.IndexOf(
            "TeamArenaPursuitLeaseBridge.ReportStarted(",
            StringComparison.Ordinal);
        Assert(actionNativeUpdate >= 0
               && unresolvedArrivalWait > actionNativeUpdate
               && actionPath > actionNativeUpdate
               && actionPath > unresolvedArrivalWait
               && actionStarted > actionPath,
            "the BigBrain consumer withholds acknowledgement during no-path contact grace, verifies a missing path once, then acknowledges execution");
        RequireAll(bigBrainRuntime,
            "!ArenaSession.IsActive",
            "TeamArenaPursuitLeaseBridge.HasExactTeamArenaContext()",
            "bot.BotState == EBotState.Active",
            "bot.GetPlayer.HealthController?.IsAlive == true",
            "HasImmediateDanger(bot)",
            "bot.BewareGrenade?.ShallRunAway() == true",
            "bot.BewareBTR?.ShallRunAway() == true",
            "bot.ArtilleryDangerPlace?.ShallRunAway() == true",
            "bot.FlashGrenade?.IsFlashed == true",
            "IsBusy(bot)",
            "HasPendingMedicalWork(bot)",
            "bot.Medecine.FirstAid?.Have2Do == true",
            "bot.Medecine.SurgicalKit?.HaveWork == true",
            "decision == BotLogicDecision.heal",
            "decision == BotLogicDecision.healStimulators",
            "TryGetExactEnemy(bot, current.TargetProfileId",
            "enemy.Person?.HealthController?.IsAlive != true",
            "IsExactGoalEnemy(bot, enemy)",
            "enemy.IsVisible && enemy.CanShoot");

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
