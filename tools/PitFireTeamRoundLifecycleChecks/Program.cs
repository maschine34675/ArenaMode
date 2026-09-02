using System.Collections;
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
            CheckFollowerDetachPolicy();
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

    private static void CheckFollowerDetachPolicy()
    {
        Assert(PitFireTeamFollowerDetachPolicy.Classify(
                   nativeFollowerAvailable: true,
                   isDead: false,
                   botStateActive: true,
                   healthReadable: true,
                   healthAlive: true)
               == PitFireTeamFollowerDetachDecision.Native,
            "a fully live native follower uses PFT's own removal path");
        Assert(PitFireTeamFollowerDetachPolicy.Classify(
                   nativeFollowerAvailable: false,
                   isDead: true,
                   botStateActive: false,
                   healthReadable: true,
                   healthAlive: false)
               == PitFireTeamFollowerDetachDecision.Disposed,
            "a fully disposed follower uses exact registry unlinking");

        var unsafeStates = new[]
        {
            PitFireTeamFollowerDetachPolicy.Classify(false, false, true, true, true),
            PitFireTeamFollowerDetachPolicy.Classify(true, true, false, true, false),
            PitFireTeamFollowerDetachPolicy.Classify(false, true, false, false, false),
            PitFireTeamFollowerDetachPolicy.Classify(false, false, false, true, false),
            PitFireTeamFollowerDetachPolicy.Classify(true, false, true, true, false)
        };
        Assert(unsafeStates.All(decision => decision == PitFireTeamFollowerDetachDecision.Invalid),
            "mixed, unreadable and active-without-BotFollower states fail closed before mutation");

        object wrapper = new();
        object body = new();
        object unrelatedWrapper = new();
        object unrelatedBody = new();
        var followers = new ArrayList { unrelatedWrapper, wrapper };
        var followersByProfileId = new Hashtable
        {
            ["unrelated"] = unrelatedWrapper,
            ["disposed"] = wrapper
        };
        var bossFollowers = new ArrayList { unrelatedBody, body };
        Assert(PitFireTeamFollowerDetachPolicy.TryUnlinkDisposed(
                followers,
                followersByProfileId,
                bossFollowers,
                wrapper,
                body,
                "disposed",
                out string unlinkProblem)
               && unlinkProblem.Length == 0,
            "disposed-wrapper unlink succeeds for one exact wrapper/index/body binding");
        Assert(followers.Count == 1
               && ReferenceEquals(followers[0], unrelatedWrapper)
               && followersByProfileId.Count == 1
               && ReferenceEquals(followersByProfileId["unrelated"], unrelatedWrapper)
               && bossFollowers.Count == 1
               && ReferenceEquals(bossFollowers[0], unrelatedBody),
            "disposed-wrapper unlink removes only exact target references and preserves unrelated state");

        object duplicateWrapper = new();
        var duplicateFollowers = new ArrayList { duplicateWrapper, duplicateWrapper };
        var duplicateIndex = new Hashtable { ["duplicate"] = duplicateWrapper };
        var duplicateBossFollowers = new ArrayList();
        Assert(!PitFireTeamFollowerDetachPolicy.TryUnlinkDisposed(
                   duplicateFollowers,
                   duplicateIndex,
                   duplicateBossFollowers,
                   duplicateWrapper,
                   new object(),
                   "duplicate",
                   out string duplicateProblem)
               && duplicateProblem.Contains("2 exact wrappers", StringComparison.Ordinal)
               && duplicateFollowers.Count == 2
               && duplicateIndex.Contains("duplicate"),
            "duplicate wrapper state is rejected without a partial registry mutation");

        object indexedWrapper = new();
        var mismatchedFollowers = new ArrayList { indexedWrapper };
        var mismatchedIndex = new Hashtable { ["mismatch"] = new object() };
        var mismatchedBossFollowers = new ArrayList();
        Assert(!PitFireTeamFollowerDetachPolicy.TryUnlinkDisposed(
                   mismatchedFollowers,
                   mismatchedIndex,
                   mismatchedBossFollowers,
                   indexedWrapper,
                   new object(),
                   "mismatch",
                   out string mismatchProblem)
               && mismatchProblem.Contains("exact wrapper", StringComparison.Ordinal)
               && mismatchedFollowers.Count == 1
               && mismatchedIndex.Contains("mismatch"),
            "a mismatched profile index is rejected before either PFT store changes");
    }

    private static void CheckSourceContract()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            root,
            "ArenaMode.Client",
            "Compat",
            "PitFireTeamRoundLifecycle.cs"));
        string compatSource = File.ReadAllText(Path.Combine(
            root,
            "ArenaMode.Client",
            "Compat",
            "PitFireTeamCompat.cs"));
        string tacticalSource = File.ReadAllText(Path.Combine(
            root,
            "ArenaMode.Client",
            "Modes",
            "TacticalMode.cs"));
        string pitFireTeamSource = File.ReadAllText(Path.GetFullPath(Path.Combine(
            root,
            "..",
            "_reference",
            "SPT-PitFireTeam-main",
            "client",
            "Patches",
            "BotsControllerPatch.cs")));
        string pitFireTeamBossPlayersSource = File.ReadAllText(Path.GetFullPath(Path.Combine(
            root,
            "..",
            "_reference",
            "SPT-PitFireTeam-main",
            "client",
            "Modules",
            "BossPlayers.cs")));
        string pitFireTeamFollowerSource = File.ReadAllText(Path.GetFullPath(Path.Combine(
            root,
            "..",
            "_reference",
            "SPT-PitFireTeam-main",
            "client",
            "Components",
            "BotFollowerPlayer.cs")));
        string botCreationDataSource = File.ReadAllText(Path.GetFullPath(Path.Combine(
            root,
            "..",
            "_decompiled",
            "BotCreationData.cs")));
        string botOwnerSource = File.ReadAllText(Path.GetFullPath(Path.Combine(
            root,
            "..",
            "_decompiled",
            "EFT",
            "BotOwner.cs")));
        string botFollowerSource = File.ReadAllText(Path.GetFullPath(Path.Combine(
            root,
            "..",
            "_decompiled",
            "BotFollower.cs")));
        string localGameSource = File.ReadAllText(Path.GetFullPath(Path.Combine(
            root,
            "..",
            "_decompiled",
            "EFT",
            "LocalGame.cs")));
        string botCreatorClientSource = File.ReadAllText(Path.GetFullPath(Path.Combine(
            root,
            "..",
            "_decompiled",
            "BotCreatorClient.cs")));
        string baseLocalGameSource = File.ReadAllText(Path.GetFullPath(Path.Combine(
            root,
            "..",
            "_decompiled",
            "EFT",
            "BaseLocalGame.cs")));

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
            "the previously ready PFT follower roster lost its physical binding",
            "assemblyName.Version != new System.Version(0, 10, 0, 0)",
            "_botCreationDataCreateMethod = RequireMethod(",
            "nameof(BotCreationData.Create)",
            "typeof(IGetProfileData)",
            "typeof(IBotCreator)",
            "typeof(ITokenGetter)",
            "BotCreationData.Create is not the exact SPT 4.1 creation seam used by PitFireTeam 0.10.0",
            "[ThreadStatic]",
            "private static FollowerSpawnLease _spawnGroupInvocationLease",
            "private static bool SpawnGroupBotsPrefix(",
            "private static void BotCreationDataCreatePostfix(",
            "postfix: new HarmonyMethod(creationPostfix)",
            "prefix: new HarmonyMethod(groupPrefix)",
            "postfix: new HarmonyMethod(groupPostfix)",
            "finalizer: new HarmonyMethod(groupFinalizer)",
            "if (markedLease == null)",
            "count != 0",
            "ReferenceEquals(botCreator, lease.Spawner._botCreator)",
            "ReferenceEquals(token, lease.Spawner)",
            "profileData is not GetProfileDataParams parameters",
            "group.StartCount != expectedGroupCount",
            "group.RemainCount != expectedGroupCount",
            "ContinuationContext = SynchronizationContext.Current",
            "state.ContinuationContext == null",
            "ReferenceEquals(SynchronizationContext.Current, state.ContinuationContext)",
            "ArenaSession.IsExactActiveRaid(lease.Game, lease.World)",
            "state.Lease.Cancellation.Token.Register(",
            "state.Completion.TrySetCanceled()",
            "state.Completion.TrySetResult(data)",
            "state.Lease.StartedProfileIds.Count != expected",
            "StopPftBotCreationData(data, \"revoked creation-task completion\")",
            "data?.StopSpawn()",
            "_localGameCreateBotByProfileMethod = RequireMethod(",
            "nameof(LocalGame.CreateBotByProfile)",
            "typeof(Task<LocalPlayer>)",
            "typeof(UnityEngine.Vector3)",
            "LocalGame.CreateBotByProfile is not the exact SPT 4.1 local-player creation seam",
            "_localGameBotsField = RequireField(",
            "typeof(Dictionary<string, Player>)",
            "postfix: new HarmonyMethod(rawPlayerPostfix)",
            "WeakReference<Profile>",
            "WeakReference<LocalGame>",
            "WeakReference<GameWorld>",
            "WeakReference<SpawnInvocationState>",
            "record.Invocation = new WeakReference<SpawnInvocationState>(state)",
            "private static void LocalGameCreateBotByProfilePostfix(",
            "__result = LeasePftRawLocalPlayerTask(rawState)",
            "private static async Task ObserveLeasedRawLocalPlayerTask(",
            "PendingRawPlayerCompletions.Enqueue",
            "internal static void TickPendingRawPlayerCompletions()",
            "Thread.CurrentThread.ManagedThreadId != pending.OwnerThreadId",
            "CompleteLeasedRawLocalPlayerTaskSafely(",
            "state.Completion.TrySetResult(player)",
            "completeWithNull = true",
            "ReferenceEquals(state.Invocation.Body?.GetPlayer, player)",
            "state.LocalBots.Remove(state.Profile.ProfileId)",
            "state.World.UnregisterPlayer(player)",
            "player.Dispose()",
            "AssetPoolObject.ReturnToPool(player.gameObject)",
            "botCreator._playerCreator.Target",
            "botCreator._playerCreator.Method",
            "ActivateBotAtPositionMethodName",
            "typeof(BotCreatorClient)",
            "typeof(Func<BotOwner, BotZone, BotsGroup>)",
            "typeof(Action<BotOwner>)",
            "typeof(CancellationToken)",
            "_activateBotAtPositionMethod.IsPrivate",
            "BotsControllerPatch.ActivateBotAtPosition is not the exact private 0.10.0 activation seam",
            "_shallBeFollowerMethod = RequireMethod(",
            "private static bool ShallBeFollowerPrefix(string profileId)",
            "PFT increments BotSpawner._inSpawnProcess immediately before this marker",
            "SpawnLeaseHarmony.Patch(",
            "_shallBeFollowerMethod",
            "prefix: new HarmonyMethod(markerPrefix)",
            "prefix: new HarmonyMethod(spawnPrefix)",
            "postfix: new HarmonyMethod(spawnPostfix)",
            "new CancellationTokenSource()",
            "ArenaRespawnDriver.CaptureRaidEpoch()",
            "ArenaRespawnDriver.IsSameActiveRaid(lease.Game, lease.RaidEpoch)",
            "ReferenceEquals(Singleton<IBotGame>.Instance, lease.Game)",
            "ReferenceEquals(Singleton<GameWorld>.Instance, lease.World)",
            "ReferenceEquals(_botsControllerField.GetValue(null), lease.Controller)",
            "ReferenceEquals(lease.Controller.BotSpawner, lease.Spawner)",
            "ReferenceEquals(lease.Spawner._botCreator, botCreator)",
            "if (!HistoricalRuntimeProfileIds.Contains(profileId))",
            "token = lease.Cancellation.Token",
            "OnActivate = owner => OnLeasedFollowerActivated(state, originalOnActivate, owner)",
            "lease.StartedProfileIds.Count == expected",
            "lease.ActivatedProfileIds.Count == expected",
            "lease.SettledProfileIds.Count == expected",
            "invocation.CallbackCompleted",
            "Volatile.Read(ref invocation.CounterSettled) == 1",
            "public static void CancelFollowerSpawnLease(string reason)",
            "lease.Cancellation.Cancel()",
            "if (!invocation.CallbackCompleted)",
            "SettleSpawnCounterUnsafe(invocation)",
            "Interlocked.Exchange(ref state.CounterSettled, 1)",
            "Math.Max(0, state.Spawner._inSpawnProcess - 1)",
            "ClearRaidStateUnsafe(clearHistory: false)",
            "WeakReference<BotSpawner>",
            "RemovePftBookkeepingUnsafe(profileId)",
            "_manualUpdateField?.GetValue(null) is IDictionary manualUpdates",
            "_shallBeFollowerField?.GetValue(bossPlayers) is IList shallBeFollower",
            "_dismissFollowerMethod.Invoke(wrapper, new object[] { false })",
            "_removeFollowerMethod.Invoke(null, new object[] { bot, boss })",
            "bot.LeaveData.RemoveFromMap()",
            "world.UnregisterPlayer(player)",
            "controller.DestroyInfo(player)",
            "AssetPoolObject.ReturnToPool(bot.gameObject, true)",
            "RevokeFollowerSpawnLeaseUnsafe(_diagnostic, cleanupBodies: true)",
            "SpawnLeaseHarmony.UnpatchSelf()",
            "PitFireTeam round lifecycle configuration rolled back");

        RequireOrder(
            pitFireTeamSource,
            "PFT 0.10.0 awaits count-zero creation before every mutable late-continuation seam",
            "botsData = await BotCreationData.Create(data, botCreator, 0, botSpawnerClass);",
            "followerCreationTask.Remove(player.realPlayer.ProfileId);",
            "botsData.Profiles.ForEach(async profile =>",
            "InteractableObjects.StoreEquipment(profile);",
            "botSpawnerClass._inSpawnProcess = _inSpawnProcess + 1;",
            "BossPlayers.ShallBeFollower(profile.ProfileId);",
            "await ActivateBotAtPosition(");
        RequireOrder(
            botCreationDataSource,
            "SPT 4.1 constructs BotCreationData before awaiting profile and bundle work",
            "public static async Task<BotCreationData> Create(IGetProfileData profileData, IBotCreator botCreator, int count, ITokenGetter token)",
            "BotCreationData botCreationData = new BotCreationData(profileData)",
            "await botProfileClient.FillCreationDataWithProfiles(botCreationData, token.GetCancelToken());",
            "return botCreationData;");
        RequireAll(
            botCreationDataSource,
            "public void StopSpawn()",
            "_cancellationTokenSource.Cancel();",
            "Singleton<GlobalEventDispatcher>.Instance.OnStopBotSpawn -= StopSpawn;");
        RequireOrder(
            botCreatorClientSource,
            "BotCreatorClient can expose a registered raw LocalPlayer when cancellation wins after its awaited creator",
            "LocalPlayer localPlayer = await _playerCreator(Singleton<GameWorld>.Instance, profile, bornInfo.position);",
            "!cancellationToken.IsCancellationRequested",
            "BotOwner botOwner = BotOwner.Create",
            "callback(botOwner);");
        RequireOrder(
            localGameSource,
            "LocalGame publishes the raw LocalPlayer before returning it to BotCreatorClient",
            "public async Task<LocalPlayer> CreateBotByProfile(GameWorld gw, Profile profile, Vector3 position)",
            "LocalPlayer localPlayer = await LocalPlayer.Create",
            "_bots.Add(localPlayer.ProfileId, localPlayer);",
            "return localPlayer;");
        RequireOrder(
            baseLocalGameSource,
            "raw-player retirement mirrors SPT's native LocalGame cleanup ownership",
            "public static void CleanUp(IDictionary<string, Player> players)",
            "value.Dispose();",
            "AssetPoolObject.ReturnToPool(value.gameObject);",
            "players.Clear();");

        string botOwnerDeath = Slice(
            botOwnerSource,
            "public void OnDied(EDamageType damageType)",
            "public void Dispose()");
        RequireOrder(
            botOwnerDeath,
            "SPT reports a bot death, disposes the BotOwner, then invokes its late death callback",
            "BotsController.BotDied(this);",
            "Dispose();",
            "IsDead = true;",
            "_botDiedCallback(this);");
        string botOwnerDispose = Slice(
            botOwnerSource,
            "public void Dispose()",
            "public bool IsRole(");
        Assert(botOwnerDispose.Contains("BotFollower = null;", StringComparison.Ordinal),
            "SPT BotOwner disposal destroys the BotFollower component before late death callbacks finish");

        string pftRemoveFollower = Slice(
            pitFireTeamBossPlayersSource,
            "private void RemoveBotFollower(BotOwner bot, pitAIBossPlayer player)",
            "public BotFollowerPlayer GetFollower(BotOwner bot)");
        RequireOrder(
            pftRemoveFollower,
            "PFT 0.10.0 removes its wrapper and ownership lists before dereferencing BotFollower",
            "_followers.Remove(_follower);",
            "_followersByProfileId.Remove(bot.ProfileId);",
            "player.RemoveFollower(bot);",
            "bot.BotFollower.BossToFollow = null;");

        string pftRemoveBoss = Slice(
            pitFireTeamBossPlayersSource,
            "private bool RemoveBossPlayer(string name, bool died = false)",
            "private void Destroy()");
        RequireOrder(
            pftRemoveBoss,
            "PFT 0.10.0 removes follower registries before dismissing their wrappers",
            "_followers.Remove(_follower);",
            "_followersByProfileId.Remove(followerBot.ProfileId);",
            "_follower.Dismiss();",
            "boss.Followers.Clear();",
            "boss.DisposeBoss();");

        string nativeFollowerDispose = SliceToEnd(
            botFollowerSource,
            "public virtual bool Dispose()");
        RequireOrder(
            nativeFollowerDispose,
            "SPT BotFollower disposal removes its owner from the boss before clearing BossToFollow",
            "BossToFollow.RemoveFollower(_owner);",
            "BossToFollow = null;",
            "return true;");

        string pftDismiss = Slice(
            pitFireTeamFollowerSource,
            "public virtual void Dismiss(bool warnPlayer = false)",
            "private void ClearInvalidEnemyState()");
        RequireOrder(
            pftDismiss,
            "PFT Dismiss skips physical handoff for disposed bots but still runs wrapper cleanup",
            "if (_bot.IsDead || _bot.BotState != EBotState.Active) return;",
            "finally",
            "Modules.SainAddonBridge.RaiseFollowerLifecycleEvent(_bot, FollowerLifecycleEvent.OnDismiss);",
            "ClearCommand(\"Dismiss:finally\");",
            "UnhookManualUpdate();",
            "UnhookPeaceChange();");

        string beginTeardown = Slice(
            source,
            "public static bool BeginTeardown(",
            "public static PitFireTeamRoundPolicyDecision PollRetirement(");
        Assert(beginTeardown.IndexOf("TryValidateBoundFollowersUnsafe(", StringComparison.Ordinal)
               < beginTeardown.IndexOf("RevokeFollowerSpawnLeaseUnsafe(", StringComparison.Ordinal),
            "round teardown validates the frozen four-body roster before advancing lifecycle state");
        RequireOrder(
            beginTeardown,
            "round teardown validates every PFT wrapper and registry entry before unlinking any follower",
            "List<object> wrappers = ReadFollowerWrappersUnsafe()",
            "if (wrappers.Count != oldBodies.Count)",
            "HashSet<BotOwner> wrappedBodies =",
            "if (oldBodies.Any(body => !wrappedBodies.Contains(body)))",
            "ValidateFollowerRegistryEntryUnsafe(bossPlayers, wrapper, bot);",
            "_unhookFollowerDeathMethod.Invoke(oldBoss, new object[] { bot });",
            "DetachFollowerWrapperUnsafe(bossPlayers, oldBoss, wrapper, bot);");

        string detachFollower = Slice(
            source,
            "private static void DetachFollowerWrapperUnsafe(",
            "private static void CleanupRuntimeProfilesUnsafe(");
        RequireOrder(
            detachFollower,
            "wrapper teardown validates exact registries and disposed group state before the active native removal path",
            "ValidateFollowerRegistryEntryUnsafe(bossPlayers, wrapper, bot);",
            "if (_followersField.GetValue(bossPlayers) is not IList followers)",
            "if (_followersByProfileIdField.GetValue(bossPlayers) is not IDictionary followersByProfileId)",
            "if (_bossFollowersProperty.GetValue(boss) is not IList bossFollowers)",
            "bool nativeFollowerAvailable = bot.BotFollower != null;",
            "PitFireTeamFollowerDetachDecision decision = PitFireTeamFollowerDetachPolicy.Classify(",
            "if (decision == PitFireTeamFollowerDetachDecision.Invalid)",
            "PFT follower {bot.ProfileId} has an unsafe teardown state",
            "active PFT follower {bot.ProfileId} is not bound to the exact old boss",
            "active PFT follower {bot.ProfileId} is not in the exact old boss group",
            "active PFT follower {bot.ProfileId} is not present exactly once on the old boss",
            "if (decision == PitFireTeamFollowerDetachDecision.Disposed",
            "disposed PFT follower {bot.ProfileId} is still in the old boss group",
            "if (decision == PitFireTeamFollowerDetachDecision.Native)",
            "_removeFollowerMethod.Invoke(null, new object[] { bot, boss });",
            "PitFireTeamFollowerDetachPolicy.TryUnlinkDisposed(",
            "_dismissFollowerMethod.Invoke(wrapper, new object[] { false });");
        string disposedUnlinkTail = Slice(
            detachFollower,
            "PitFireTeamFollowerDetachPolicy.TryUnlinkDisposed(",
            "_dismissFollowerMethod.Invoke(wrapper, new object[] { false });");
        Assert(!disposedUnlinkTail.Contains("bot.BotFollower", StringComparison.Ordinal),
            "the disposed-wrapper unlink tail never dereferences the BotFollower that SPT already nulled");

        RequireOrder(
            detachFollower,
            "wrapper teardown proves all PFT ownership postconditions after Dismiss",
            "_dismissFollowerMethod.Invoke(wrapper, new object[] { false });",
            "remainingFollowers.Cast<object>().Any(candidate => ReferenceEquals(candidate, wrapper))",
            "remainingByProfileId.Contains(bot.ProfileId)",
            "remainingBossFollowers.Cast<object>().Any(candidate => ReferenceEquals(candidate, bot))",
            "remainingBossGroup.Contains(bot)");
        Assert(beginTeardown.IndexOf("_removeBossPlayerMethod.Invoke(", StringComparison.Ordinal)
               < beginTeardown.IndexOf("health.Kill(EDamageType.Undefined);", StringComparison.Ordinal),
            "round teardown removes PFT boss ownership before killing any surviving old body");
        RequireAll(
            beginTeardown,
            "string teardownStep = \"resolve the old PFT boss\";",
            "string teardownProfileId = oldLeader.ProfileId;",
            "reason = $\"PFT old-round teardown failed during {teardownStep} for {teardownProfileId}: \"",
            "root.GetType().Name",
            "root.Message",
            "Plugin.ModLogger.LogError($\"Arena {reason}\\n{ex}\");");
        Assert(tacticalSource.Contains("FailTeamTransition(teardownProblem);", StringComparison.Ordinal)
               && !tacticalSource.Contains(
                   "FailTeamTransition(\"PitFireTeam old-round teardown failed: \" + teardownProblem);",
                   StringComparison.Ordinal),
            "Tactical mode presents the lifecycle's detailed teardown error exactly once");

        string creationPatch = Slice(
            source,
            "private static void BotCreationDataCreatePostfix(",
            "private static Task<BotCreationData> LeasePftBotCreationTask(");
        Assert(creationPatch.IndexOf("if (markedLease == null)", StringComparison.Ordinal)
               < creationPatch.IndexOf("Task<BotCreationData> originalTask = __result", StringComparison.Ordinal)
               && creationPatch.Contains("__result = LeasePftBotCreationTask(state)", StringComparison.Ordinal)
               && creationPatch.Contains("__result = RejectPftBotCreationTask(originalTask, problem)", StringComparison.Ordinal),
            "ordinary creation calls return untouched while the exact marked call is replaced or rejected");

        string creationObserver = Slice(
            source,
            "private static async Task ObserveLeasedBotCreationTask(",
            "private static Task<BotCreationData> RejectPftBotCreationTask(");
        Assert(creationObserver.IndexOf("Volatile.Read(ref state.Settled)", StringComparison.Ordinal)
               < creationObserver.IndexOf("state.Completion.TrySetResult(data)", StringComparison.Ordinal)
               && creationObserver.IndexOf("state.Completion.TrySetResult(data)", StringComparison.Ordinal)
               < creationObserver.IndexOf("state.Lease.StartedProfileIds.Count != expected", StringComparison.Ordinal)
               && creationObserver.IndexOf("StopPftBotCreationData", StringComparison.Ordinal)
               > creationObserver.IndexOf("state.CancellationRegistration.Dispose()", StringComparison.Ordinal),
            "late creation completion is observed/stopped, while valid release proves all synchronous activation seams");

        string creationLease = Slice(
            source,
            "private static Task<BotCreationData> LeasePftBotCreationTask(",
            "private static async Task ObserveLeasedBotCreationTask(");
        Assert(creationLease.IndexOf("Cancellation.Token.Register", StringComparison.Ordinal)
               < creationLease.IndexOf("ObserveLeasedBotCreationTask(state)", StringComparison.Ordinal)
               && creationLease.IndexOf("Interlocked.CompareExchange", StringComparison.Ordinal)
               < creationLease.IndexOf("state.Completion.TrySetCanceled()", StringComparison.Ordinal),
            "cancellation owns wrapper settlement before the original creation task can resume PFT mutations");

        string rawPlayerLease = Slice(
            source,
            "private static void LocalGameCreateBotByProfilePostfix(",
            "private static bool ShallBeFollowerPrefix(");
        int rawIdentityGate = rawPlayerLease.IndexOf(
            "HistoricalSpawnRecords.TryGetValue",
            StringComparison.Ordinal);
        int ordinaryReturn = rawPlayerLease.IndexOf("return;", rawIdentityGate, StringComparison.Ordinal);
        int rawWrapper = rawPlayerLease.IndexOf(
            "__result = LeasePftRawLocalPlayerTask(rawState)",
            StringComparison.Ordinal);
        Assert(rawIdentityGate >= 0
               && ordinaryReturn > rawIdentityGate
               && rawWrapper > ordinaryReturn
               && rawPlayerLease.Contains("ReferenceEquals(recordedProfile, profile)", StringComparison.Ordinal)
               && rawPlayerLease.Contains("ReferenceEquals(recordedGame, __instance)", StringComparison.Ordinal)
               && rawPlayerLease.Contains("ReferenceEquals(invocation.Lease.World, recordedWorld)", StringComparison.Ordinal)
               && rawPlayerLease.Contains("RequestedWorld = gw", StringComparison.Ordinal),
            "ordinary LocalGame creations remain untouched unless every weak Arena identity matches");
        string rawObserver = Slice(
            source,
            "private static async Task ObserveLeasedRawLocalPlayerTask(",
            "private static void CompleteLeasedRawLocalPlayerTaskSafely(");
        Assert(rawObserver.IndexOf("state.ContinuationContext.Post", StringComparison.Ordinal)
               < rawObserver.IndexOf("PendingRawPlayerCompletions.Enqueue", StringComparison.Ordinal)
               && !rawObserver.Contains("state.Completion.TrySetException", StringComparison.Ordinal),
            "a rejected context Post keeps the wrapper closed and queues its exact raw result for owner-thread retirement");
        Assert(compatSource.Contains(
                "PitFireTeamRoundLifecycle.TickPendingRawPlayerCompletions();",
                StringComparison.Ordinal),
            "the persistent Unity Update path drains rejected context-Post completions");
        Assert(rawPlayerLease.IndexOf("state.Completion.TrySetResult(player)", StringComparison.Ordinal)
               < rawPlayerLease.IndexOf("ReferenceEquals(state.Invocation.Body?.GetPlayer, player)", StringComparison.Ordinal),
            "valid raw-player release proves the inline BotOwner callback before leaving StateLock");
        Assert(rawPlayerLease.IndexOf("completeWithNull = true", StringComparison.Ordinal)
               < rawPlayerLease.IndexOf("BotOwner existingOwner = player.AIData?.BotOwner", StringComparison.Ordinal)
               && rawPlayerLease.IndexOf("RetireLeasedRawLocalPlayer(state, player", StringComparison.Ordinal)
               < rawPlayerLease.LastIndexOf("state.Completion.TrySetResult(null)", StringComparison.Ordinal)
               && rawPlayerLease.Contains("if (completeWithNull)", StringComparison.Ordinal),
            "every rejected raw result completes with null, after raw-player retirement when no BotOwner owns cleanup");
        Assert(!rawPlayerLease.Contains("SettleSpawnCounterUnsafe", StringComparison.Ordinal)
               && !rawPlayerLease.Contains("DestroyInfo", StringComparison.Ordinal)
               && !rawPlayerLease.Contains("BotDied", StringComparison.Ordinal),
            "raw-player retirement does not touch BotSpawner or BotOwner accounting before publication");

        string reset = Slice(
            source,
            "public static void Reset()",
            "private static void RevokeFollowerSpawnLeaseUnsafe(");
        Assert(reset.Contains("ClearRaidStateUnsafe(clearHistory: false)", StringComparison.Ordinal)
               && !reset.Contains("ClearRaidStateUnsafe(clearHistory: true)", StringComparison.Ordinal),
            "raid reset revokes historical runtime IDs while releasing strong round state");

        string taskObserver = Slice(
            source,
            "private static async Task<bool> ObserveLeasedActivationTask(",
            "private static void OnLeasedFollowerActivated(");
        Assert(taskObserver.Contains("SettledProfileIds.Add", StringComparison.Ordinal)
               && !taskObserver.Contains("CallbackCompleted = true", StringComparison.Ordinal),
            "a true private task return is recorded separately from PFT's detached physical callback");

        string revoke = Slice(
            source,
            "private static void RevokeFollowerSpawnLeaseUnsafe(",
            "private static void DiscoverExactContract(");
        Assert(revoke.IndexOf("lease.Cancellation.Cancel()", StringComparison.Ordinal)
               < revoke.IndexOf("if (!invocation.CallbackCompleted)", StringComparison.Ordinal)
               && revoke.IndexOf("if (!invocation.CallbackCompleted)", StringComparison.Ordinal)
               < revoke.IndexOf("SettleSpawnCounterUnsafe(invocation)", StringComparison.Ordinal)
               && revoke.IndexOf("_spawnLease = null", StringComparison.Ordinal)
               > revoke.IndexOf("CleanupRuntimeProfilesUnsafe", StringComparison.Ordinal),
            "cancellation settles detached no-callback counters once, cleans bodies, then releases the strong lease");

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

    private static void RequireOrder(string source, string message, params string[] fragments)
    {
        int previous = -1;
        foreach (string fragment in fragments)
        {
            int current = source.IndexOf(fragment, previous + 1, StringComparison.Ordinal);
            Assert(current > previous, message + ": " + fragment);
            previous = current;
        }
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start + Math.Max(1, startMarker.Length),
            StringComparison.Ordinal);
        Assert(start >= 0 && end > start,
            $"source slice exists: {startMarker} -> {endMarker}");
        return source.Substring(start, end - start);
    }

    private static string SliceToEnd(string source, string startMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert(start >= 0, $"source tail exists: {startMarker}");
        return source.Substring(start);
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
