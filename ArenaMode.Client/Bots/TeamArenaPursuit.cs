using ArenaMode.Compat;
using ArenaMode.Session;
using EFT;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Keeps a protected 5v5 moving after the local PMC has died. The caller owns
/// roster validity. This service refreshes each bot's nearest live opponent,
/// then gives the optional BigBrain companion a short movement lease. If that
/// exact per-bot layer is unavailable, it falls back to PFT's ordered push for
/// followers, SAIN's mover for SAIN bots, or EFT's safe move-to-enemy helper.
/// </summary>
internal static class TeamArenaPursuit
{
    private const float SweepSeconds = 1f;
    private const float BigBrainLeaseSeconds = 2.5f;
    private const float SainMoveRefreshSeconds = 2f;
    private const float PftStallProbeSeconds = 4f;

    private static readonly HashSet<string> LoggedFailures = new(StringComparer.Ordinal);
    private static readonly HashSet<string> LoggedRoutes = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> KnownTargetsBySource = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, WeakReference<EnemyInfo>> SainAwarenessBySource = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> PftPushTargetsBySource = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, PftPushLease> PftPushLeasesBySource = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, PftTacticalLease> PftTacticalLeasesBySource = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, float> NextSainMoveBySource = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, PftMovementProbe> PftMovementBySource = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, RouteMovementProbe> RouteMovementBySource = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, HashSet<string>> RejectedTargetsBySource = new(StringComparer.Ordinal);
    private static readonly HashSet<string> BigBrainUnavailableSources = new(StringComparer.Ordinal);

    private static int _epoch = -1;
    private static float _nextSweepAt;
    private static bool _activationLogged;

    private static bool _pftBridgeResolved;
    private static bool _pftAssemblyDetected;
    private static PropertyInfo _pftCombatEnabled;
    private static PropertyInfo _pftRuntimeCallbacks;
    private static PropertyInfo _pftBossPlayersInstance;
    private static MethodInfo _pftGetFollower;
    private static MethodInfo _pftGetFollowerByProfileId;
    private static MethodInfo _pftGetBot;
    private static MethodInfo _pftSetPushEnemy;
    private static MethodInfo _pftSetTacticalPoint;
    private static MethodInfo _pftCancelPush;
    private static MethodInfo _pftClearCommand;
    private static MethodInfo _pftTryPeekCommand;
    private static MethodInfo _pftTryGetOrderedPushTargetLock;
    private static PropertyInfo _pftHasPushCancelRequest;
    private static MethodInfo _pftTryConsumePushCancelRequest;
    private static PropertyInfo _pftCombatTactic;
    private static Type _pftCommandType;
    private static PropertyInfo _pftPushSequence;
    private static MethodInfo _bigBrainGetActiveLogic;

    private static bool _sainBridgeResolved;
    private static bool _sainAssemblyDetected;
    private static MethodInfo _sainGetBot;
    private static PropertyInfo _sainEnemyController;
    private static PropertyInfo _sainBotActivation;
    private static PropertyInfo _sainInCombat;
    private static PropertyInfo _sainMover;
    private static PropertyInfo _sainNavMeshPosition;
    private static MethodInfo _sainCheckAddEnemy;
    private static PropertyInfo _sainGoalEnemy;
    private static PropertyInfo _sainEnemyInfo;
    private static MethodInfo _sainUpdateLastSeen;
    private static MethodInfo _sainSetInCombat;
    private static MethodInfo _sainRunToPoint;
    private static Type _sainSprintUrgency;

    private readonly struct PftMovementProbe
    {
        public PftMovementProbe(
            string targetProfileId,
            Vector3 sourcePosition,
            Vector3 targetPosition,
            float squaredDistance,
            float observedAt,
            int stallCount)
        {
            TargetProfileId = targetProfileId;
            SourcePosition = sourcePosition;
            TargetPosition = targetPosition;
            SquaredDistance = squaredDistance;
            ObservedAt = observedAt;
            StallCount = stallCount;
        }

        public string TargetProfileId { get; }
        public Vector3 SourcePosition { get; }
        public Vector3 TargetPosition { get; }
        public float SquaredDistance { get; }
        public float ObservedAt { get; }
        public int StallCount { get; }
    }

    private readonly struct RouteMovementProbe
    {
        public RouteMovementProbe(
            string targetProfileId,
            string adapter,
            Vector3 sourcePosition,
            Vector3 targetPosition,
            float squaredDistance,
            float observedAt,
            int stalledWindows)
        {
            TargetProfileId = targetProfileId;
            Adapter = adapter;
            SourcePosition = sourcePosition;
            TargetPosition = targetPosition;
            SquaredDistance = squaredDistance;
            ObservedAt = observedAt;
            StalledWindows = stalledWindows;
        }

        public string TargetProfileId { get; }
        public string Adapter { get; }
        public Vector3 SourcePosition { get; }
        public Vector3 TargetPosition { get; }
        public float SquaredDistance { get; }
        public float ObservedAt { get; }
        public int StalledWindows { get; }
    }

    private readonly struct PftTacticalLease
    {
        public PftTacticalLease(string targetProfileId, Vector3 target, float untilTime)
        {
            TargetProfileId = targetProfileId;
            Target = target;
            UntilTime = untilTime;
        }

        public string TargetProfileId { get; }
        public Vector3 Target { get; }
        public float UntilTime { get; }
    }

    private readonly struct PftPushLease
    {
        public PftPushLease(int sequence, string targetProfileId)
        {
            Sequence = sequence;
            TargetProfileId = targetProfileId;
        }

        public int Sequence { get; }
        public string TargetProfileId { get; }
    }

    private enum SainMoveResult
    {
        Unavailable,
        Deferred,
        Accepted,
        Rejected
    }

    private enum PftPushResult
    {
        Unavailable,
        Deferred,
        Accepted,
        Rejected
    }

    public static void Reset()
    {
        TeamArenaPursuitLeaseBridge.Reset();
        CancelIssuedPftPushes("Arena pursuit reset");
        _epoch = -1;
        _nextSweepAt = 0f;
        ClearRoundState();
        ResetOptionalBridges();
    }

    public static void BeginRound(int epoch)
    {
        // Defensive cleanup if a transition reached the new epoch without the
        // normal end-round Reset. The command cancellation is scoped to the
        // exact follower profile IDs which Arena itself previously commanded.
        CancelIssuedPftPushes("Arena replacement round began");
        _epoch = epoch;
        _nextSweepAt = 0f;
        ClearRoundState();
        TeamArenaPursuitLeaseBridge.BeginEpoch(epoch);
    }

    /// <summary>
    /// Called only after Tactical has validated both fixed rosters and their
    /// hostility matrix. Only profile IDs, timers, value snapshots and weak
    /// identity references survive a sweep.
    /// </summary>
    public static void Tick(
        int epoch,
        bool localPlayerDown,
        IReadOnlyList<BotOwner> allies,
        IReadOnlyList<BotOwner> opponents)
    {
        if (!localPlayerDown
            || epoch != _epoch
            || !ArenaSession.IsActive
            || !PitFireTeamCompat.TeamArenaActive
            || Time.time < _nextSweepAt)
        {
            return;
        }

        _nextSweepAt = Time.time + SweepSeconds;

        var liveAllies = Snapshot(allies);
        var liveOpponents = Snapshot(opponents);
        if (liveAllies.Count == 0 || liveOpponents.Count == 0)
            return;

        if (!_activationLogged)
        {
            _activationLogged = true;
            Plugin.ModLogger.LogInfo(
                $"Team Arena post-death pursuit active for round epoch {epoch}: "
                + $"allies {liveAllies.Count}, opponents {liveOpponents.Count}.");
        }

        foreach (var ally in liveAllies)
            RefreshSafely(ally, liveOpponents, epoch, follower: true);

        foreach (var opponent in liveOpponents)
            RefreshSafely(opponent, liveAllies, epoch, follower: false);
    }

    private static void RefreshSafely(
        BotOwner source,
        IReadOnlyList<BotOwner> candidates,
        int epoch,
        bool follower)
    {
        try
        {
            RefreshNearest(source, candidates, epoch, follower);
        }
        catch (Exception ex)
        {
            LogOnce(epoch, source, null, "sweep", ex);
        }
    }

    private static List<BotOwner> Snapshot(IReadOnlyList<BotOwner> candidates)
    {
        var result = new List<BotOwner>();
        var profiles = new HashSet<string>(StringComparer.Ordinal);
        if (candidates == null)
            return result;

        foreach (var candidate in candidates)
        {
            if (candidate == null
                || candidate.BotState != EBotState.Active
                || candidate.HealthController?.IsAlive != true
                || candidate.GetPlayer == null
                || candidate.GetPlayer.HealthController?.IsAlive != true
                || !IsFinite(candidate.Position))
            {
                continue;
            }

            string profileId = candidate.ProfileId?.Trim();
            if (string.IsNullOrEmpty(profileId) || !profiles.Add(profileId))
                continue;

            result.Add(candidate);
        }

        return result;
    }

    private static void RefreshNearest(
        BotOwner source,
        IReadOnlyList<BotOwner> candidates,
        int epoch,
        bool follower)
    {
        string sourceProfile = source.ProfileId?.Trim();
        if (string.IsNullOrEmpty(sourceProfile))
            return;

        if (TeamArenaPursuitLeaseBridge.TryConsumeRejection(
                sourceProfile,
                epoch,
                out string rejectedTargetProfile))
        {
            BigBrainUnavailableSources.Add(sourceProfile);
            RejectTargetForCurrentPass(sourceProfile, rejectedTargetProfile, epoch);
            BotOwner rejectedTarget = candidates.FirstOrDefault(candidate =>
                string.Equals(candidate?.ProfileId, rejectedTargetProfile, StringComparison.Ordinal));
            LogPathRejectedOnce(epoch, source, rejectedTarget, "bigbrain");
        }

        HashSet<string> rejectedTargets = PrepareRejectedTargetPass(sourceProfile, candidates);
        BotOwner engagedTarget = SelectEngagedTarget(source, candidates);
        if (engagedTarget != null)
            rejectedTargets.Remove(engagedTarget.ProfileId);

        BotOwner nearest = engagedTarget
            ?? SelectRetainedTarget(sourceProfile, candidates, rejectedTargets);
        float bestDistance = nearest == null
            ? float.MaxValue
            : (nearest.Position - source.Position).sqrMagnitude;
        string bestProfile = nearest?.ProfileId;

        if (nearest == null)
        {
            foreach (var candidate in candidates)
            {
                string profile = candidate.ProfileId ?? string.Empty;
                if (rejectedTargets.Contains(profile))
                    continue;

                float distance = (candidate.Position - source.Position).sqrMagnitude;
                if (nearest == null
                    || distance < bestDistance
                    || (Math.Abs(distance - bestDistance) < 0.0001f
                        && string.CompareOrdinal(profile, bestProfile) < 0))
                {
                    nearest = candidate;
                    bestDistance = distance;
                    bestProfile = profile;
                }
            }
        }

        if (nearest == null)
        {
            TeamArenaPursuitLeaseBridge.Revoke(sourceProfile, epoch);
            return;
        }

        string targetProfile = nearest.ProfileId?.Trim();
        if (string.IsNullOrEmpty(targetProfile))
        {
            TeamArenaPursuitLeaseBridge.Revoke(sourceProfile, epoch);
            return;
        }

        EnemyInfo enemyInfo;
        try
        {
            if (!RefreshNative(source, nearest, out enemyInfo))
            {
                TeamArenaPursuitLeaseBridge.Revoke(sourceProfile, epoch);
                RejectTargetForCurrentPass(source, nearest, epoch);
                LogAwarenessRejectedOnce(epoch, source, nearest);
                return;
            }
        }
        catch (Exception ex)
        {
            TeamArenaPursuitLeaseBridge.Revoke(sourceProfile, epoch);
            RejectTargetForCurrentPass(source, nearest, epoch);
            LogOnce(epoch, source, targetProfile, "eft-awareness", ex);
            return;
        }

        KnownTargetsBySource[sourceProfile] = targetProfile;

        bool sainNeedsSync = !SainAwarenessBySource.TryGetValue(
                sourceProfile,
                out WeakReference<EnemyInfo> syncedReference)
            || !syncedReference.TryGetTarget(out EnemyInfo syncedEnemy)
            || !ReferenceEquals(syncedEnemy, enemyInfo);
        if (sainNeedsSync
            && TryRefreshSain(
                source,
                enemyInfo.Person,
                enemyInfo,
                epoch,
                targetProfile))
        {
            // Do not refresh a successfully bound unseen contact every second;
            // that would keep SAIN's search lifetime artificially fresh. A
            // stale profile cache remains retryable until the exact body binds.
            SainAwarenessBySource[sourceProfile] = new WeakReference<EnemyInfo>(enemyInfo);
        }

        TeamArenaApproachDecision decision = TeamArenaPursuitPolicy.Decide(
            bestDistance,
            enemyInfo.IsVisible,
            enemyInfo.CanShoot,
            IsMedicalActionActive(source),
            source.WeaponManager?.Reload?.Reloading == true,
            source.WeaponManager?.Grenades?.ThrowindNow == true,
            source.DoorOpener?.Interacting == true,
            HasImmediateDanger(source));
        if (decision != TeamArenaApproachDecision.Approach)
        {
            TeamArenaPursuitLeaseBridge.Revoke(sourceProfile, epoch);
            PftMovementBySource.Remove(sourceProfile);
            RouteMovementBySource.Remove(sourceProfile);
            if (decision == TeamArenaApproachDecision.Busy
                || decision == TeamArenaApproachDecision.ImmediateDanger)
                LogDecisionOnce(epoch, source, nearest, decision);
            return;
        }

        // A distant opponent can leave EFT's standby distance cache at
        // float.MaxValue. Wake only a validated, live current-round body in
        // the exact post-death pursuit context before assigning ownership.
        source.StandBy?.Activate();

        if (!BigBrainUnavailableSources.Contains(sourceProfile)
            && TeamArenaPursuitLeaseBridge.Publish(
                sourceProfile,
                targetProfile,
                nearest.Position,
                epoch,
                BigBrainLeaseSeconds,
                out TeamArenaPursuitLease bigBrainLease))
        {
            bool executionStarted = TeamArenaPursuitLeaseBridge.HasStarted(
                    sourceProfile,
                    epoch,
                    bigBrainLease.Generation);
            TeamArenaConsumerDecision ownerDecision = TeamArenaPursuitOwnerPolicy.Evaluate(
                publicationAccepted: true,
                executionStarted,
                Time.time - bigBrainLease.IssuedAt);
            if (ownerDecision == TeamArenaConsumerDecision.Active)
            {
                ObserveRouteProgress(source, nearest, bestDistance, epoch, "bigbrain");
                LogRouteOnce(epoch, source, nearest, "bigbrain", bestDistance);
                return;
            }

            if (ownerDecision == TeamArenaConsumerDecision.AwaitingAcknowledgement)
                return;

            TeamArenaPursuitLeaseBridge.Revoke(
                sourceProfile,
                epoch,
                bigBrainLease.Generation);
            BigBrainUnavailableSources.Add(sourceProfile);
            LogOwnerUnavailableOnce(epoch, source, nearest, "bigbrain");
        }

        // Once this follower has entered a guarded PFT/SAIN/native fallback,
        // keep that owner for the rest of the round. A BigBrain wrapper can be
        // injected one sweep late; letting it take over afterward would leave
        // the Arena-issued PFT command/lock alive under a higher-priority layer.
        if (follower)
            BigBrainUnavailableSources.Add(sourceProfile);

        bool pftSainFollower = follower
            && UsesPftSainFollowerCombat(source, nearest, epoch);
        if (follower
            && !pftSainFollower
            && !PreparePftTargetSwitch(source, nearest, epoch))
        {
            return;
        }

        if (follower)
        {
            if (pftSainFollower)
            {
                HandleSainMovement(
                    source,
                    nearest,
                    bestDistance,
                    epoch,
                    "sain-pft-run",
                    ownerRequired: true);
                return;
            }

            PftPushResult pftPush = TryIssuePftPush(source, nearest, bestDistance, epoch);
            if (pftPush == PftPushResult.Accepted || pftPush == PftPushResult.Deferred)
                return;

            if (pftPush == PftPushResult.Rejected)
            {
                TryClearOwnedPftTactical(
                    sourceProfile,
                    "Arena pursuit target rejected",
                    epoch,
                    source,
                    nearest);
                RejectTargetForCurrentPass(source, nearest, epoch);
                return;
            }

            // Native movement is only safe when no PFT wrapper owns the bot.
            // Faults after PFT has been detected return Rejected above.
            TryMoveNative(source, nearest, bestDistance, epoch, "native-pft-fallback");
            return;
        }

        if (HandleSainMovement(
                source,
                nearest,
                bestDistance,
                epoch,
                "sain-run",
                ownerRequired: false))
        {
            return;
        }

        TryMoveNative(source, nearest, bestDistance, epoch, "native");
    }

    private static bool HandleSainMovement(
        BotOwner source,
        BotOwner target,
        float squaredDistance,
        int epoch,
        string adapter,
        bool ownerRequired)
    {
        SainMoveResult sainMove = TryMoveSain(
            source,
            target,
            epoch,
            mustHaveCompletePath: true,
            adapter: adapter);
        if (sainMove == SainMoveResult.Accepted)
        {
            ObserveRouteProgress(source, target, squaredDistance, epoch, adapter);
            LogRouteOnce(epoch, source, target, adapter, squaredDistance);
            return true;
        }

        if (sainMove == SainMoveResult.Deferred)
        {
            ObserveRouteProgress(source, target, squaredDistance, epoch, adapter);
            return true;
        }

        if (sainMove == SainMoveResult.Rejected)
        {
            RejectTargetForCurrentPass(source, target, epoch);
            LogPathRejectedOnce(epoch, source, target, adapter);
            return true;
        }

        if (ownerRequired)
        {
            LogOwnerUnavailableOnce(epoch, source, target, adapter);
            return true;
        }

        return false;
    }

    private static BotOwner SelectEngagedTarget(
        BotOwner source,
        IReadOnlyList<BotOwner> candidates)
    {
        EnemyInfo current = source?.Memory?.GoalEnemy;
        if (current?.IsVisible == true && current.CanShoot == true && current.Person != null)
        {
            foreach (var candidate in candidates)
            {
                if (IsCurrentTargetPerson(current.Person, candidate)
                    && TryResolveRegisteredEnemy(source, candidate, out EnemyInfo registered)
                    && ReferenceEquals(current, registered))
                {
                    return candidate;
                }
            }
        }

        BotOwner selected = null;
        float selectedDistance = float.MaxValue;
        var knownEnemies = source?.EnemiesController?.EnemyInfos;
        if (knownEnemies == null)
            return null;

        foreach (var candidate in candidates)
        {
            if (!TryResolveRegisteredEnemy(source, candidate, out EnemyInfo enemy)
                || enemy?.IsVisible != true
                || enemy.CanShoot != true)
            {
                continue;
            }

            float distance = (candidate.Position - source.Position).sqrMagnitude;
            if (selected == null
                || distance < selectedDistance
                || (Math.Abs(distance - selectedDistance) < 0.0001f
                    && string.CompareOrdinal(candidate.ProfileId, selected.ProfileId) < 0))
            {
                selected = candidate;
                selectedDistance = distance;
            }
        }

        return selected;
    }

    private static bool TryResolveRegisteredEnemy(
        BotOwner source,
        BotOwner target,
        out EnemyInfo enemyInfo)
    {
        enemyInfo = null;
        if (target?.GetPlayer == null
            || !TeamArenaEnemyIdentityPolicy.TryResolve(
                source?.BotsGroup?.Enemies,
                source?.EnemiesController?.EnemyInfos,
                target.GetPlayer,
                target,
                source?.Memory?.GoalEnemy,
                out IPlayer enemyKey,
                out BotGroupEnemyInfo groupInfo,
                out EnemyInfo resolved,
                out bool hasEnemyInfo)
            || !hasEnemyInfo
            || resolved == null
            || !ReferenceEquals(resolved.Person, enemyKey)
            || !ReferenceEquals(resolved.GroupInfo, groupInfo)
            || !ReferenceEquals(resolved.Owner, source)
            || !ReferenceEquals(resolved.GroupOwner, source.BotsGroup)
            || !ReferenceEquals(groupInfo._botGroup, source.BotsGroup)
            || !ReferenceEquals(groupInfo.Player, target.GetPlayer))
        {
            return false;
        }

        enemyInfo = resolved;
        return true;
    }

    private static BotOwner SelectRetainedTarget(
        string sourceProfile,
        IReadOnlyList<BotOwner> candidates,
        HashSet<string> rejectedTargets)
    {
        if (!KnownTargetsBySource.TryGetValue(sourceProfile, out string retainedProfile)
            || rejectedTargets.Contains(retainedProfile))
        {
            return null;
        }

        return candidates.FirstOrDefault(candidate =>
            string.Equals(candidate?.ProfileId, retainedProfile, StringComparison.Ordinal));
    }

    private static HashSet<string> PrepareRejectedTargetPass(
        string sourceProfile,
        IReadOnlyList<BotOwner> candidates)
    {
        if (!RejectedTargetsBySource.TryGetValue(sourceProfile, out HashSet<string> rejectedTargets))
        {
            rejectedTargets = new HashSet<string>(StringComparer.Ordinal);
            RejectedTargetsBySource[sourceProfile] = rejectedTargets;
        }

        var liveTargets = new HashSet<string>(
            candidates.Select(candidate => candidate?.ProfileId)
                .Where(profile => !string.IsNullOrEmpty(profile)),
            StringComparer.Ordinal);
        TeamArenaPursuitPolicy.PrepareRejectedTargetPass(rejectedTargets, liveTargets);
        return rejectedTargets;
    }

    private static void RejectTargetForCurrentPass(BotOwner source, BotOwner target, int epoch)
        => RejectTargetForCurrentPass(source?.ProfileId, target?.ProfileId, epoch);

    private static void RejectTargetForCurrentPass(
        string sourceProfile,
        string targetProfile,
        int epoch)
    {
        if (string.IsNullOrEmpty(sourceProfile) || string.IsNullOrEmpty(targetProfile))
            return;

        TeamArenaPursuitLeaseBridge.Revoke(sourceProfile, epoch);

        if (!RejectedTargetsBySource.TryGetValue(sourceProfile, out HashSet<string> rejectedTargets))
        {
            rejectedTargets = new HashSet<string>(StringComparer.Ordinal);
            RejectedTargetsBySource[sourceProfile] = rejectedTargets;
        }

        rejectedTargets.Add(targetProfile);
        if (KnownTargetsBySource.TryGetValue(sourceProfile, out string knownTarget)
            && string.Equals(knownTarget, targetProfile, StringComparison.Ordinal))
        {
            KnownTargetsBySource.Remove(sourceProfile);
        }

        PftPushTargetsBySource.Remove(sourceProfile);
        PftMovementBySource.Remove(sourceProfile);
        RouteMovementBySource.Remove(sourceProfile);
        NextSainMoveBySource.Remove(sourceProfile);
    }

    private static bool RefreshNative(BotOwner source, BotOwner target, out EnemyInfo enemyInfo)
    {
        enemyInfo = null;
        var group = source?.BotsGroup;
        if (group == null
            || target == null
            || target.GetPlayer == null
            || target.GetPlayer.HealthController?.IsAlive != true
            || source.Memory == null
            || source.EnemiesController == null)
        {
            return false;
        }

        IPlayer playerKey = target.GetPlayer;
        IPlayer ownerKey = target;
        bool hasCurrentGroupKey = TeamArenaEnemyIdentityPolicy.ContainsReferenceKey(
                group.Enemies,
                playerKey)
            || TeamArenaEnemyIdentityPolicy.ContainsReferenceKey(
                group.Enemies,
                ownerKey);
        if (!hasCurrentGroupKey)
            group.AddEnemy(playerKey, EBotEnemyCause.bossKillArena);

        if (!TeamArenaEnemyIdentityPolicy.TryResolve(
                group.Enemies,
                source.EnemiesController.EnemyInfos,
                playerKey,
                ownerKey,
                source.Memory.GoalEnemy,
                out IPlayer enemyKey,
                out BotGroupEnemyInfo groupInfo,
                out enemyInfo,
                out bool hasEnemyInfo)
            || !ReferenceEquals(groupInfo._botGroup, group)
            || !ReferenceEquals(groupInfo.Player, target.GetPlayer))
        {
            return false;
        }

        // PitFireTeam registers AI bodies as BotOwner while EFT's alive-player
        // lookup returns their separate Player objects. BotsGroup.IsEnemy hides
        // that distinction by comparing IDs, but both dictionaries use exact
        // IPlayer object keys. Continue with the key that is actually registered.
        group.ReportAboutEnemy(enemyKey, EEnemyPartVisibleType.Sence, source);
        groupInfo.IsLastPositionChecked = false;

        if (!hasEnemyInfo)
        {
            source.Memory.AddEnemy(enemyKey, groupInfo, onActivation: false);
            if (!TeamArenaEnemyIdentityPolicy.TryResolve(
                    group.Enemies,
                    source.EnemiesController.EnemyInfos,
                    playerKey,
                    ownerKey,
                    source.Memory.GoalEnemy,
                    out IPlayer reboundKey,
                    out BotGroupEnemyInfo reboundGroupInfo,
                    out enemyInfo,
                    out hasEnemyInfo)
                || !hasEnemyInfo
                || !ReferenceEquals(reboundKey, enemyKey)
                || !ReferenceEquals(reboundGroupInfo, groupInfo))
            {
                return false;
            }
        }

        if (enemyInfo == null
            || !ReferenceEquals(enemyInfo.Person, enemyKey)
            || !ReferenceEquals(enemyInfo.GroupInfo, groupInfo)
            || !ReferenceEquals(enemyInfo.Owner, source)
            || !ReferenceEquals(enemyInfo.GroupOwner, group))
        {
            return false;
        }

        enemyInfo.IgnoreUntilAggression = false;
        source.Memory.IsPeace = false;
        source.Memory.GoalEnemy = enemyInfo;
        return GoalEnemyMatches(source, target, enemyInfo);
    }

    private static bool GoalEnemyMatches(BotOwner source, BotOwner target, EnemyInfo expected = null)
    {
        EnemyInfo actual = source?.Memory?.GoalEnemy;
        return actual != null
            && TryResolveRegisteredEnemy(source, target, out EnemyInfo registered)
            && ReferenceEquals(actual, registered)
            && IsCurrentTargetPerson(actual.Person, target)
            && (expected == null || ReferenceEquals(actual, expected));
    }

    private static bool IsCurrentTargetPerson(IPlayer person, BotOwner target)
        => person != null
           && target != null
           && (ReferenceEquals(person, target)
               || ReferenceEquals(person, target.GetPlayer)
               || (person.Id == target.Id
                   && string.Equals(person.ProfileId, target.ProfileId, StringComparison.Ordinal)
                   && ReferenceEquals(person.AIData?.BotOwner, target)));

    private static bool PreparePftTargetSwitch(BotOwner source, BotOwner target, int epoch)
    {
        string sourceProfile = source?.ProfileId;
        string targetProfile = target?.ProfileId;
        if (string.IsNullOrEmpty(sourceProfile) || string.IsNullOrEmpty(targetProfile))
            return true;

        bool hasPushLease = PftPushLeasesBySource.TryGetValue(
            sourceProfile,
            out PftPushLease previousPushLease);
        bool hasTacticalLease = PftTacticalLeasesBySource.TryGetValue(
            sourceProfile,
            out PftTacticalLease previousTacticalLease);
        string previousTargetProfile = hasPushLease
            ? previousPushLease.TargetProfileId
            : hasTacticalLease ? previousTacticalLease.TargetProfileId : null;
        if (string.IsNullOrEmpty(previousTargetProfile)
            || string.Equals(previousTargetProfile, targetProfile, StringComparison.Ordinal))
            return true;

        try
        {
            ResolvePftBridge();
            object follower = _pftGetFollowerByProfileId?.Invoke(null, new object[] { sourceProfile });
            if (follower == null)
                throw new InvalidOperationException("PitFireTeam lost the verified follower wrapper during target rotation.");

            bool hasRawCommand = TryReadPftCommand(
                follower,
                out string command,
                out _,
                out _);
            bool hasTargetLock = TryReadPftOrderedPushTarget(follower, out string lockedTarget);
            bool cancelPending = _pftHasPushCancelRequest?.GetValue(follower) is bool pending
                && pending;

            if (hasTacticalLease)
            {
                if (PftTacticalLeaseMatches(follower, previousTacticalLease))
                {
                    _pftClearCommand?.Invoke(
                        follower,
                        new object[] { "Arena pursuit tactical target rotation" });
                    PftTacticalLeasesBySource.Remove(sourceProfile);
                    LogPftTargetSwitchDeferredOnce(
                        epoch,
                        source,
                        previousTargetProfile,
                        target);
                    return false;
                }

                if (hasRawCommand || hasTargetLock || cancelPending)
                    return false;

                // The raw command disappears when PFT consumes it. Preserve
                // only the exact live tactical action and point which PFT then
                // owns; UntilTime by itself does not prove physical movement.
                if (PftConsumedTacticalLeaseMatches(source, follower, previousTacticalLease))
                    return false;

                PftTacticalLeasesBySource.Remove(sourceProfile);
            }

            if (!hasPushLease)
            {
                PftPushTargetsBySource.Remove(sourceProfile);
                return !hasRawCommand && !hasTargetLock && !cancelPending;
            }

            if (_pftPushSequence?.GetValue(follower) is not int currentSequence
                || _pftCancelPush == null)
                throw new MissingMemberException("PitFireTeam push release contract is incomplete.");

            if (currentSequence != previousPushLease.Sequence)
            {
                // A newer producer owns the current sequence. Keep Arena's old
                // lease only as a foreign-ownership barrier until every raw,
                // lock and cancellation signal disappears. Dropping it early
                // would make the next sweep look like an initial takeover and
                // overwrite the newer producer.
                LogPftOwnershipChangedOnce(epoch, source, target);
                if (hasRawCommand || hasTargetLock || cancelPending)
                    return false;

                PftPushLeasesBySource.Remove(sourceProfile);
                PftPushTargetsBySource.Remove(sourceProfile);
                PftMovementBySource.Remove(sourceProfile);
                return true;
            }

            bool pendingArenaPush = hasRawCommand
                && string.Equals(command, "PushEnemy", StringComparison.Ordinal);
            bool lockedArenaPush = hasTargetLock
                && string.Equals(
                    lockedTarget,
                    previousPushLease.TargetProfileId,
                    StringComparison.Ordinal);

            if (pendingArenaPush || lockedArenaPush)
            {
                if (!cancelPending)
                    _pftCancelPush.Invoke(follower, new object[] { "Arena pursuit target rotation" });

                LogPftTargetSwitchDeferredOnce(epoch, source, previousPushLease.TargetProfileId, target);
                return false;
            }

            if (hasRawCommand || hasTargetLock)
                return false;

            if (cancelPending)
            {
                // A pending raw push has no consumed objective to acknowledge
                // its cancel. Once neither raw state nor a lock remains, consume
                // that stale flag before issuing the replacement push.
                if (_pftTryConsumePushCancelRequest == null)
                    return false;

                object[] consumeArgs = { string.Empty };
                _pftTryConsumePushCancelRequest.Invoke(follower, consumeArgs);
                if (_pftHasPushCancelRequest?.GetValue(follower) is bool stillPending
                    && stillPending)
                {
                    return false;
                }
            }

            PftPushLeasesBySource.Remove(sourceProfile);
            PftPushTargetsBySource.Remove(sourceProfile);
            PftMovementBySource.Remove(sourceProfile);
            return true;
        }
        catch (Exception ex)
        {
            LogOnce(epoch, source, targetProfile, "pft-target-switch", ex);
            return false;
        }
    }

    private static PftPushResult TryIssuePftPush(
        BotOwner source,
        BotOwner target,
        float squaredDistance,
        int epoch)
    {
        string sourceProfile = source.ProfileId;
        string targetProfile = target.ProfileId;
        try
        {
            ResolvePftBridge();
            if (_pftBossPlayersInstance == null || _pftGetFollower == null)
                return _pftAssemblyDetected ? PftPushResult.Rejected : PftPushResult.Unavailable;

            object follower = GetPftFollower(source);
            if (follower == null)
                return _pftAssemblyDetected ? PftPushResult.Rejected : PftPushResult.Unavailable;

            if (!GoalEnemyMatches(source, target))
                throw new InvalidOperationException(
                    $"PitFireTeam follower GoalEnemy did not bind requested profile '{targetProfile}'.");

            if (_pftSetPushEnemy == null || _pftPushSequence == null)
                throw new MissingMethodException("PitFireTeam follower push contract is incomplete.");

            bool targetChanged = !PftPushTargetsBySource.TryGetValue(
                    sourceProfile,
                    out string pushedTarget)
                || !string.Equals(pushedTarget, targetProfile, StringComparison.Ordinal);
            TeamArenaPftLeaseState leaseState = InspectPftLease(
                source,
                follower,
                sourceProfile,
                targetProfile);
            if (leaseState == TeamArenaPftLeaseState.CancelPending
                || leaseState == TeamArenaPftLeaseState.Foreign)
            {
                LogPftLeaseDeferredOnce(epoch, source, target, leaseState);
                return PftPushResult.Deferred;
            }

            if (!targetChanged && leaseState == TeamArenaPftLeaseState.Stale)
            {
                ClearTrackedPftRoute(sourceProfile);
                targetChanged = true;
            }

            bool tacticalEscalation = false;
            bool abandonTarget = false;
            bool stalled = !targetChanged
                && ShouldRetryStalledPftPush(
                    source,
                    target,
                    squaredDistance,
                    epoch,
                    out tacticalEscalation,
                    out abandonTarget);
            if (abandonTarget)
                return PftPushResult.Rejected;

            if (!targetChanged && !stalled && leaseState == TeamArenaPftLeaseState.Active)
                return PftPushResult.Accepted;

            bool useTacticalPoint = IsPftMarksman(follower) || tacticalEscalation;

            if (useTacticalPoint)
            {
                if (_pftSetTacticalPoint == null || _pftTryPeekCommand == null || _pftCommandType == null)
                    throw new MissingMethodException("PitFireTeam tactical movement contract is incomplete.");

                Vector3 tacticalTarget = target.Position;
                bool hadPriorCommand = TryReadPftCommand(
                    follower,
                    out string priorCommand,
                    out Vector3 priorTarget,
                    out float priorUntil);
                _pftSetTacticalPoint.Invoke(follower, new object[] { tacticalTarget, 5f });
                if (!TryReadPftCommand(
                        follower,
                        out string command,
                        out Vector3 acceptedTarget,
                        out float acceptedUntil)
                    || !string.Equals(command, "CombatMoveToPointTactical", StringComparison.Ordinal)
                    || (acceptedTarget - tacticalTarget).sqrMagnitude >= 0.01f
                    || float.IsNaN(acceptedUntil)
                    || float.IsInfinity(acceptedUntil)
                    || acceptedUntil <= Time.time
                    || (hadPriorCommand
                        && string.Equals(priorCommand, command, StringComparison.Ordinal)
                        && (priorTarget - acceptedTarget).sqrMagnitude < 0.0001f
                        && Math.Abs(priorUntil - acceptedUntil) < 0.0001f))
                {
                    LogPathRejectedOnce(epoch, source, target, "pft-tactical");
                    return PftPushResult.Rejected;
                }

                PftTacticalLeasesBySource[sourceProfile] = new PftTacticalLease(
                    targetProfile,
                    acceptedTarget,
                    acceptedUntil);
                LogRouteOnce(epoch, source, target, "pft-tactical", squaredDistance);
            }
            else
            {
                int before = Convert.ToInt32(_pftPushSequence.GetValue(follower));
                _pftSetPushEnemy.Invoke(follower, new object[] { 30f });
                int after = Convert.ToInt32(_pftPushSequence.GetValue(follower));
                if (after == before)
                {
                    LogPftDeferredOnce(epoch, source, target);
                    return PftPushResult.Deferred;
                }

                PftPushLeasesBySource[sourceProfile] = new PftPushLease(after, targetProfile);
                if (!TryReadPftCommand(
                        follower,
                        out string command,
                        out _,
                        out _)
                    || !string.Equals(command, "PushEnemy", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "PitFireTeam incremented PushEnemyIssueSequence without exposing the matching command.");
                }

                PftTacticalLeasesBySource.Remove(sourceProfile);
                LogRouteOnce(epoch, source, target, stalled ? "pft-push-replan" : "pft-push", squaredDistance);
            }

            PftPushTargetsBySource[sourceProfile] = targetProfile;
            if (targetChanged)
            {
                PftMovementBySource[sourceProfile] = new PftMovementProbe(
                    targetProfile,
                    source.Position,
                    target.Position,
                    squaredDistance,
                    Time.time,
                    stallCount: 0);
            }

            return PftPushResult.Accepted;
        }
        catch (Exception ex)
        {
            LogOnce(epoch, source, targetProfile, "pft-push", ex);
            return _pftAssemblyDetected ? PftPushResult.Rejected : PftPushResult.Unavailable;
        }
    }

    private static TeamArenaPftLeaseState InspectPftLease(
        BotOwner source,
        object follower,
        string sourceProfile,
        string targetProfile)
    {
        bool hasTrackedRoute = PftPushTargetsBySource.ContainsKey(sourceProfile)
            || PftPushLeasesBySource.ContainsKey(sourceProfile)
            || PftTacticalLeasesBySource.ContainsKey(sourceProfile);
        bool hasRawCommand = TryReadPftCommand(
            follower,
            out string command,
            out _,
            out _);
        bool hasTargetLock = TryReadPftOrderedPushTarget(follower, out string lockedTarget);
        bool cancelPending = _pftHasPushCancelRequest?.GetValue(follower) is bool pending
            && pending;
        bool hasPushLeaseForTarget = PftPushLeasesBySource.TryGetValue(
                sourceProfile,
                out PftPushLease pushLease)
            && string.Equals(pushLease.TargetProfileId, targetProfile, StringComparison.Ordinal);
        bool pushSequenceMatches = hasPushLeaseForTarget
            && _pftPushSequence?.GetValue(follower) is int currentSequence
            && currentSequence == pushLease.Sequence;
        bool hasTacticalLeaseForTarget = PftTacticalLeasesBySource.TryGetValue(
                sourceProfile,
                out PftTacticalLease tacticalLease)
            && string.Equals(tacticalLease.TargetProfileId, targetProfile, StringComparison.Ordinal);
        bool tacticalRawMatches = hasTacticalLeaseForTarget
            && PftTacticalLeaseMatches(follower, tacticalLease);

        return TeamArenaPftLeasePolicy.Evaluate(
            hasTrackedRoute,
            cancelPending,
            hasRawCommand,
            rawIsPush: hasRawCommand
                && string.Equals(command, "PushEnemy", StringComparison.Ordinal),
            hasTargetLock,
            targetLockMatches: hasTargetLock
                && string.Equals(lockedTarget, targetProfile, StringComparison.Ordinal),
            hasPushLeaseForTarget,
            pushSequenceMatches,
            hasTacticalLeaseForTarget,
            tacticalRawMatches,
            tacticalActionMatches: hasTacticalLeaseForTarget
                && PftConsumedTacticalLeaseMatches(source, follower, tacticalLease));
    }

    private static bool IsPftMarksman(object follower)
        => string.Equals(
            _pftCombatTactic?.GetValue(follower)?.ToString(),
            "Marksman",
            StringComparison.Ordinal);

    private static void ClearTrackedPftRoute(string sourceProfile)
    {
        PftPushTargetsBySource.Remove(sourceProfile);
        PftPushLeasesBySource.Remove(sourceProfile);
        PftTacticalLeasesBySource.Remove(sourceProfile);
        PftMovementBySource.Remove(sourceProfile);
    }

    private static bool UsesPftSainFollowerCombat(
        BotOwner source,
        BotOwner target,
        int epoch)
    {
        try
        {
            ResolvePftBridge();
            if (_pftCombatEnabled?.GetValue(null) is bool enabled)
                return enabled;

            return FindType(
                "pitTeam.SAINAddon.SAINFollowerCombatLayer",
                "pitFireTeam.SAINAddon") != null;
        }
        catch (Exception ex)
        {
            LogOnce(epoch, source, target?.ProfileId, "pft-movement-owner", ex);
            return FindType(
                "pitTeam.SAINAddon.SAINFollowerCombatLayer",
                "pitFireTeam.SAINAddon") != null;
        }
    }

    private static bool ShouldRetryStalledPftPush(
        BotOwner source,
        BotOwner target,
        float squaredDistance,
        int epoch,
        out bool useTacticalPoint,
        out bool abandonTarget)
    {
        useTacticalPoint = false;
        abandonTarget = false;
        string sourceProfile = source.ProfileId;
        string targetProfile = target.ProfileId;
        if (!PftMovementBySource.TryGetValue(sourceProfile, out PftMovementProbe probe)
            || !string.Equals(probe.TargetProfileId, targetProfile, StringComparison.Ordinal))
        {
            PftMovementBySource[sourceProfile] = new PftMovementProbe(
                targetProfile,
                source.Position,
                target.Position,
                squaredDistance,
                Time.time,
                stallCount: 0);
            return false;
        }

        if (Time.time - probe.ObservedAt < PftStallProbeSeconds)
            return false;

        float movement = (source.Position - probe.SourcePosition).sqrMagnitude;
        float oldDistance = Mathf.Sqrt(Mathf.Max(0f, probe.SquaredDistance));
        float currentDistance = Mathf.Sqrt(Mathf.Max(0f, squaredDistance));
        float approachProgress = CalculateApproachProgress(
            probe.SourcePosition,
            probe.TargetPosition,
            source.Position);
        bool progressed = TeamArenaPursuitOwnerPolicy.HasDirectionalProgress(approachProgress);
        if (progressed)
        {
            PftMovementBySource[sourceProfile] = new PftMovementProbe(
                targetProfile,
                source.Position,
                target.Position,
                squaredDistance,
                Time.time,
                stallCount: 0);
            return false;
        }

        int stallCount = probe.StallCount + 1;
        PftMovementBySource[sourceProfile] = new PftMovementProbe(
            targetProfile,
            source.Position,
            target.Position,
            squaredDistance,
            Time.time,
            stallCount);
        useTacticalPoint = stallCount == 2;
        if (stallCount >= 2)
        {
            string adapter = PftTacticalLeasesBySource.ContainsKey(sourceProfile)
                ? "pft-tactical"
                : "pft-push";
            LogStalledOnce(
                epoch,
                source,
                target,
                adapter,
                Mathf.Sqrt(Mathf.Max(0f, movement)),
                approachProgress,
                oldDistance - currentDistance);
        }

        if (stallCount >= 3)
        {
            abandonTarget = true;
            return false;
        }

        // One push replan and one bounded tactical-point command are enough to
        // recover ordinary decision stalls without restarting PFT's objectives
        // forever when the NavMesh route is genuinely impossible.
        return stallCount <= 2;
    }

    private static bool TryReadPftCommand(
        object follower,
        out string command,
        out Vector3 target,
        out float untilTime)
    {
        command = null;
        target = Vector3.zero;
        untilTime = 0f;
        if (_pftTryPeekCommand == null || _pftCommandType == null)
            return false;

        object[] arguments =
        {
            Activator.CreateInstance(_pftCommandType),
            Vector3.zero,
            0f
        };
        if (_pftTryPeekCommand.Invoke(follower, arguments) is not bool active || !active
            || arguments[1] is not Vector3 activeTarget
            || arguments[2] is not float activeUntil)
        {
            return false;
        }

        command = arguments[0]?.ToString();
        target = activeTarget;
        untilTime = activeUntil;
        return !string.IsNullOrEmpty(command);
    }

    private static void TryMoveNative(
        BotOwner source,
        BotOwner target,
        float squaredDistance,
        int epoch,
        string adapter)
    {
        try
        {
            source.MovementResume();
            source.SetPose(1f);
            source.SetTargetMoveSpeed(1f);
            if (!source.MoveToEnemyData.TryMoveToEnemy(target.Position))
            {
                RejectTargetForCurrentPass(source, target, epoch);
                LogPathRejectedOnce(epoch, source, target, adapter);
                return;
            }

            ObserveRouteProgress(source, target, squaredDistance, epoch, adapter);
            LogRouteOnce(epoch, source, target, adapter, squaredDistance);
        }
        catch (Exception ex)
        {
            RejectTargetForCurrentPass(source, target, epoch);
            LogOnce(epoch, source, target.ProfileId, adapter, ex);
        }
    }

    private static void ObserveRouteProgress(
        BotOwner source,
        BotOwner target,
        float squaredDistance,
        int epoch,
        string adapter)
    {
        string sourceProfile = source.ProfileId;
        string targetProfile = target.ProfileId;
        if (!RouteMovementBySource.TryGetValue(sourceProfile, out RouteMovementProbe probe)
            || !string.Equals(probe.TargetProfileId, targetProfile, StringComparison.Ordinal)
            || !string.Equals(probe.Adapter, adapter, StringComparison.Ordinal))
        {
            RouteMovementBySource[sourceProfile] = new RouteMovementProbe(
                targetProfile,
                adapter,
                source.Position,
                target.Position,
                squaredDistance,
                Time.time,
                stalledWindows: 0);
            return;
        }

        if (Time.time - probe.ObservedAt < PftStallProbeSeconds)
            return;

        float movement = (source.Position - probe.SourcePosition).sqrMagnitude;
        float oldDistance = Mathf.Sqrt(Mathf.Max(0f, probe.SquaredDistance));
        float currentDistance = Mathf.Sqrt(Mathf.Max(0f, squaredDistance));
        float approachProgress = CalculateApproachProgress(
            probe.SourcePosition,
            probe.TargetPosition,
            source.Position);
        float distanceGain = oldDistance - currentDistance;
        // Only movement by this source counts. A stationary source must not
        // receive progress merely because its independently moving target
        // happened to approach it during the probe window.
        bool progressed = TeamArenaPursuitOwnerPolicy.HasDirectionalProgress(approachProgress);
        if (progressed)
        {
            RouteMovementBySource[sourceProfile] = new RouteMovementProbe(
                targetProfile,
                adapter,
                source.Position,
                target.Position,
                squaredDistance,
                Time.time,
                stalledWindows: 0);
            return;
        }

        // Labs routes frequently begin with a lateral corridor, stairs, or a
        // short move away from the Euclidean target. Preserve two complete
        // detour windows, but carry the counter forward so unrelated movement
        // cannot mask a bad route forever.
        int stalledWindows = probe.StalledWindows + 1;
        if (stalledWindows < 3)
        {
            RouteMovementBySource[sourceProfile] = new RouteMovementProbe(
                targetProfile,
                adapter,
                source.Position,
                target.Position,
                squaredDistance,
                Time.time,
                stalledWindows);
            return;
        }

        if (string.Equals(adapter, "bigbrain", StringComparison.Ordinal))
            BigBrainUnavailableSources.Add(sourceProfile);

        RejectTargetForCurrentPass(source, target, epoch);
        LogStalledOnce(
            epoch,
            source,
            target,
            adapter,
            Mathf.Sqrt(Mathf.Max(0f, movement)),
            approachProgress,
            distanceGain);
    }

    private static float CalculateApproachProgress(
        Vector3 sourcePosition,
        Vector3 targetPosition,
        Vector3 currentSourcePosition)
    {
        Vector3 towardTarget = targetPosition - sourcePosition;
        if (towardTarget.sqrMagnitude < 0.0001f)
            return 0f;

        return Vector3.Dot(
            currentSourcePosition - sourcePosition,
            towardTarget.normalized);
    }

    private static bool TryRefreshSain(
        BotOwner source,
        IPlayer target,
        EnemyInfo expectedNativeEnemy,
        int epoch,
        string targetProfile)
    {
        try
        {
            if (target == null
                || expectedNativeEnemy == null
                || !ReferenceEquals(expectedNativeEnemy.Person, target))
            {
                return false;
            }

            if (!TryGetSainBot(source, out object sainBot))
                return false;

            object enemyController = _sainEnemyController.GetValue(sainBot);
            if (enemyController == null)
                return false;

            object enemy = _sainCheckAddEnemy.Invoke(enemyController, new object[] { target });
            if (enemy == null)
                return false;

            // SAIN caches enemies by ProfileId and may return an old round's
            // wrapper before consulting EFT's exact IPlayer-keyed dictionary.
            // Never point SAIN at that stale object or refresh its lifetime.
            if (!ReferenceEquals(_sainEnemyInfo.GetValue(enemy), expectedNativeEnemy))
            {
                LogSainIdentityRejectedOnce(epoch, source, targetProfile);
                return false;
            }

            _sainUpdateLastSeen.Invoke(enemy, new object[] { target.Position, Time.time });
            _sainGoalEnemy.GetSetMethod(nonPublic: true)?.Invoke(enemyController, new[] { enemy });
            object activation = _sainBotActivation.GetValue(sainBot);
            if (activation != null
                && (_sainInCombat.GetValue(sainBot) is not bool inCombat || !inCombat))
            {
                _sainSetInCombat.Invoke(activation, new object[] { true });
            }

            return true;
        }
        catch (Exception ex)
        {
            LogOnce(epoch, source, targetProfile, "sain-awareness", ex);
            return false;
        }
    }

    private static SainMoveResult TryMoveSain(
        BotOwner source,
        BotOwner target,
        int epoch,
        bool mustHaveCompletePath,
        string adapter)
    {
        string sourceProfile = source.ProfileId;
        if (NextSainMoveBySource.TryGetValue(sourceProfile, out float nextMoveAt)
            && Time.time < nextMoveAt)
        {
            return SainMoveResult.Deferred;
        }

        try
        {
            ResolveSainBridge();
            if (_sainGetBot == null)
                return _sainAssemblyDetected ? SainMoveResult.Rejected : SainMoveResult.Unavailable;

            object[] sourceArgs = { sourceProfile, null };
            if (_sainGetBot.Invoke(null, sourceArgs) is not bool found
                || !found
                || sourceArgs[1] == null)
            {
                return SainMoveResult.Unavailable;
            }

            object sainBot = sourceArgs[1];

            object mover = _sainMover.GetValue(sainBot);
            if (mover == null || _sainRunToPoint == null || _sainSprintUrgency == null)
                throw new MissingMemberException("SAIN bot movement contract is incomplete.");

            Vector3 destination = TryGetSainDestination(source, target, epoch);

            object urgency = Enum.ToObject(_sainSprintUrgency, 3);
            bool accepted = _sainRunToPoint.Invoke(
                mover,
                new object[]
                {
                    destination,
                    mustHaveCompletePath,
                    TeamArenaPursuitPolicy.DestinationReachDistanceMeters,
                    urgency,
                    true
                }) is bool moved && moved;
            NextSainMoveBySource[sourceProfile] = Time.time
                + (accepted ? SainMoveRefreshSeconds : SweepSeconds);
            return accepted ? SainMoveResult.Accepted : SainMoveResult.Rejected;
        }
        catch (Exception ex)
        {
            LogOnce(epoch, source, target.ProfileId, adapter, ex);
            return _sainAssemblyDetected ? SainMoveResult.Rejected : SainMoveResult.Unavailable;
        }
    }

    private static Vector3 TryGetSainDestination(BotOwner source, BotOwner target, int epoch)
    {
        try
        {
            if (TryGetSainBot(target, out object targetSain)
                && _sainNavMeshPosition.GetValue(targetSain) is Vector3 navPosition
                && IsFinite(navPosition))
            {
                return navPosition;
            }
        }
        catch (Exception ex)
        {
            // The target may legitimately be a PFT bot without a SAIN component.
            // A target-side lookup fault must not surrender the source bot's mover.
            LogOnce(epoch, source, target?.ProfileId, "sain-target-nav", ex);
        }

        return target.Position;
    }

    private static bool TryGetSainBot(BotOwner source, out object sainBot)
    {
        sainBot = null;
        ResolveSainBridge();
        if (_sainGetBot == null || source == null || string.IsNullOrEmpty(source.ProfileId))
            return false;

        object[] getArgs = { source.ProfileId, null };
        if (_sainGetBot.Invoke(null, getArgs) is not bool found || !found || getArgs[1] == null)
            return false;

        sainBot = getArgs[1];
        return true;
    }

    private static void CancelIssuedPftPushes(string reason)
    {
        if (PftPushLeasesBySource.Count == 0 && PftTacticalLeasesBySource.Count == 0)
            return;

        try
        {
            ResolvePftBridge();
            if (_pftGetFollowerByProfileId == null)
                return;

            var profiles = new HashSet<string>(PftPushLeasesBySource.Keys, StringComparer.Ordinal);
            profiles.UnionWith(PftTacticalLeasesBySource.Keys);
            foreach (string profileId in profiles)
            {
                try
                {
                    object follower = _pftGetFollowerByProfileId.Invoke(null, new object[] { profileId });
                    if (follower == null)
                        continue;

                    if (PftTacticalLeasesBySource.TryGetValue(profileId, out PftTacticalLease tacticalLease)
                        && PftTacticalLeaseMatches(follower, tacticalLease))
                    {
                        _pftClearCommand?.Invoke(follower, new object[] { reason });
                    }

                    if (!PftPushLeasesBySource.TryGetValue(profileId, out PftPushLease pushLease)
                        || _pftPushSequence?.GetValue(follower) is not int currentSequence
                        || currentSequence != pushLease.Sequence)
                    {
                        continue;
                    }

                    bool hasRawCommand = TryReadPftCommand(
                        follower,
                        out string command,
                        out _,
                        out _);
                    bool pendingArenaPush = hasRawCommand
                        && string.Equals(command, "PushEnemy", StringComparison.Ordinal);
                    bool consumedArenaPush = !hasRawCommand
                        && TryReadPftOrderedPushTarget(follower, out string lockedTarget)
                        && string.Equals(
                            lockedTarget,
                            pushLease.TargetProfileId,
                            StringComparison.Ordinal);
                    if (pendingArenaPush || consumedArenaPush)
                        _pftCancelPush?.Invoke(follower, new object[] { reason });
                }
                catch (Exception ex)
                {
                    Plugin.ModLogger.LogWarning(
                        $"Team Arena could not release its PitFireTeam pursuit ownership for "
                        + $"'{profileId}'; round teardown will retire that wrapper: {ex.GetBaseException()}");
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                $"Team Arena could not cancel its PitFireTeam pursuit commands; "
                + $"round teardown will retire the affected wrappers: {ex.GetBaseException()}");
        }
    }

    private static void TryClearOwnedPftTactical(
        string sourceProfile,
        string reason,
        int epoch,
        BotOwner source,
        BotOwner target)
    {
        if (!PftTacticalLeasesBySource.TryGetValue(sourceProfile, out PftTacticalLease lease))
            return;

        try
        {
            ResolvePftBridge();
            object follower = _pftGetFollowerByProfileId?.Invoke(null, new object[] { sourceProfile });
            if (follower != null && PftTacticalLeaseMatches(follower, lease))
                _pftClearCommand?.Invoke(follower, new object[] { reason });
        }
        catch (Exception ex)
        {
            LogOnce(epoch, source, target?.ProfileId, "pft-tactical-clear", ex);
        }
        finally
        {
            PftTacticalLeasesBySource.Remove(sourceProfile);
        }
    }

    private static bool PftTacticalLeaseMatches(object follower, PftTacticalLease lease)
        => TryReadPftCommand(follower, out string command, out Vector3 target, out float untilTime)
           && string.Equals(command, "CombatMoveToPointTactical", StringComparison.Ordinal)
           && (target - lease.Target).sqrMagnitude < 0.01f
           && Math.Abs(untilTime - lease.UntilTime) < 0.01f
           && untilTime > Time.time;

    private static bool PftConsumedTacticalLeaseMatches(
        BotOwner source,
        object follower,
        PftTacticalLease lease)
    {
        var point = source?.GoToSomePointData;
        var agent = source?.Brain?.Agent;
        if (_pftGetBot?.Invoke(follower, null) is not BotOwner followerBot
            || !ReferenceEquals(followerBot, source)
            || !string.Equals(followerBot.ProfileId, source?.ProfileId, StringComparison.Ordinal)
            || _bigBrainGetActiveLogic?.Invoke(null, new object[] { source }) is not object activeLogic
            || !string.Equals(
                activeLogic.GetType().FullName,
                "pitTeam.BigBrain.Actions.CombatGoToPointTacticalAction",
                StringComparison.Ordinal)
            || !string.Equals(
                activeLogic.GetType().Assembly.GetName().Name,
                "pitFireTeam",
                StringComparison.Ordinal)
            || Time.time >= lease.UntilTime
            || point == null
            || agent == null
            || !point.HaveTarget()
            || !IsFinite(point.Point)
            || (point.Point - lease.Target).sqrMagnitude >= 0.01f)
        {
            return false;
        }

        var decision = agent.LastResult();
        return string.Equals(decision.Reason, "command.thereTactical", StringComparison.Ordinal);
    }

    private static bool TryReadPftOrderedPushTarget(object follower, out string targetProfile)
    {
        targetProfile = null;
        if (_pftTryGetOrderedPushTargetLock == null)
            return false;

        object[] arguments = { string.Empty, Vector3.zero };
        if (_pftTryGetOrderedPushTargetLock.Invoke(follower, arguments) is not bool active
            || !active
            || arguments[0] is not string profile
            || string.IsNullOrEmpty(profile))
        {
            return false;
        }

        targetProfile = profile;
        return true;
    }

    private static object GetPftFollower(BotOwner source)
    {
        object instance = _pftBossPlayersInstance?.GetValue(null);
        return instance == null || _pftGetFollower == null
            ? null
            : _pftGetFollower.Invoke(instance, new object[] { source });
    }

    private static void ResolvePftBridge()
    {
        if (_pftBridgeResolved)
            return;

        _pftBridgeResolved = true;

        var addonType = FindType("pitTeam.Modules.SainAddonBridge", "pitFireTeam");
        if (addonType != null)
        {
            _pftCombatEnabled = addonType.GetProperty(
                "IsFollowerCombatEnabled",
                BindingFlags.Public | BindingFlags.Static);
            _pftRuntimeCallbacks = addonType.GetProperty(
                "HasRuntimeCallbacks",
                BindingFlags.Public | BindingFlags.Static);
        }

        var bossPlayersType = FindType("pitTeam.Modules.BossPlayers", "pitFireTeam");
        var followerType = FindType("pitTeam.Components.BotFollowerPlayer", "pitFireTeam");
        _pftAssemblyDetected = bossPlayersType != null || followerType != null;
        if (bossPlayersType == null || followerType == null)
            return;

        _pftBossPlayersInstance = bossPlayersType.GetProperty(
            "Instance",
            BindingFlags.Public | BindingFlags.Static);
        _pftGetFollower = bossPlayersType.GetMethod(
            "GetFollower",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(BotOwner) },
            null);
        _pftGetFollowerByProfileId = bossPlayersType.GetMethod(
            "GetFollowerByProfileId",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(string) },
            null);
        _pftGetBot = followerType.GetMethod(
            "GetBot",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            Type.EmptyTypes,
            null);
        _pftSetPushEnemy = followerType.GetMethod(
            "SetPushEnemy",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(float) },
            null);
        _pftSetTacticalPoint = followerType.GetMethod(
            "SetCombatMoveToPointTactical",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(Vector3), typeof(float) },
            null);
        _pftCancelPush = followerType.GetMethod(
            "RequestOrderedPushCancel",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(string) },
            null);
        _pftClearCommand = followerType.GetMethod(
            "ClearCommand",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(string) },
            null);
        _pftTryPeekCommand = followerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, "TryPeekActiveCommand", StringComparison.Ordinal)
                    || method.ReturnType != typeof(bool))
                {
                    return false;
                }

                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 3
                    && parameters[0].ParameterType.IsByRef
                    && parameters[0].ParameterType.GetElementType()?.IsEnum == true
                    && parameters[1].ParameterType == typeof(Vector3).MakeByRefType()
                    && parameters[2].ParameterType == typeof(float).MakeByRefType();
            });
        _pftCommandType = _pftTryPeekCommand?.GetParameters()[0].ParameterType.GetElementType();
        _pftTryGetOrderedPushTargetLock = followerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, "TryGetOrderedPushTargetLock", StringComparison.Ordinal)
                    || method.ReturnType != typeof(bool))
                {
                    return false;
                }

                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 2
                    && parameters[0].ParameterType == typeof(string).MakeByRefType()
                    && parameters[1].ParameterType == typeof(Vector3).MakeByRefType();
            });
        _pftHasPushCancelRequest = followerType.GetProperty(
            "HasOrderedPushCancelRequest",
            BindingFlags.Public | BindingFlags.Instance);
        _pftTryConsumePushCancelRequest = followerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, "TryConsumeOrderedPushCancelRequest", StringComparison.Ordinal)
                    || method.ReturnType != typeof(bool))
                {
                    return false;
                }

                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 1
                    && parameters[0].ParameterType == typeof(string).MakeByRefType();
            });
        _pftCombatTactic = followerType.GetProperty(
            "CombatTactic",
            BindingFlags.Public | BindingFlags.Instance);
        _pftPushSequence = followerType.GetProperty(
            "PushEnemyIssueSequence",
            BindingFlags.Public | BindingFlags.Instance);

        Type bigBrainManager = FindType(
            "DrakiaXYZ.BigBrain.Brains.BrainManager",
            "DrakiaXYZ-BigBrain");
        _bigBrainGetActiveLogic = bigBrainManager?.GetMethod(
            "GetActiveLogic",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(BotOwner) },
            null);
    }

    private static void ResolveSainBridge()
    {
        if (_sainBridgeResolved)
            return;

        _sainBridgeResolved = true;
        var enableType = FindType("SAIN.SAINEnableClass", "SAIN");
        var botType = FindType("SAIN.Components.BotComponent", "SAIN");
        _sainAssemblyDetected = enableType != null || botType != null;
        if (enableType == null || botType == null)
            return;

        _sainGetBot = enableType.GetMethod(
            "GetSAIN",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(string), botType.MakeByRefType() },
            null);
        _sainEnemyController = botType.GetProperty("EnemyController", BindingFlags.Public | BindingFlags.Instance);
        _sainBotActivation = botType.GetProperty("BotActivation", BindingFlags.Public | BindingFlags.Instance);
        _sainInCombat = botType.GetProperty("IsInCombat", BindingFlags.Public | BindingFlags.Instance);
        _sainMover = botType.GetProperty("Mover", BindingFlags.Public | BindingFlags.Instance);
        _sainNavMeshPosition = botType.GetProperty("NavMeshPosition", BindingFlags.Public | BindingFlags.Instance);

        var controllerType = _sainEnemyController?.PropertyType;
        _sainCheckAddEnemy = controllerType?.GetMethod("CheckAddEnemy", new[] { typeof(IPlayer) });
        _sainGoalEnemy = controllerType?.GetProperty("GoalEnemy", BindingFlags.Public | BindingFlags.Instance);
        var enemyType = _sainCheckAddEnemy?.ReturnType;
        _sainEnemyInfo = enemyType?.GetProperty("EnemyInfo", BindingFlags.Public | BindingFlags.Instance);
        _sainUpdateLastSeen = enemyType?.GetMethod(
            "UpdateLastSeenPosition",
            new[] { typeof(Vector3), typeof(float) });

        var activationType = _sainBotActivation?.PropertyType;
        _sainSetInCombat = activationType?.GetMethod("SetInCombat", new[] { typeof(bool) });

        var moverType = _sainMover?.PropertyType;
        _sainRunToPoint = moverType?.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, "RunToPoint", StringComparison.Ordinal))
                    return false;

                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 5
                    && parameters[0].ParameterType == typeof(Vector3)
                    && parameters[1].ParameterType == typeof(bool)
                    && parameters[2].ParameterType == typeof(float)
                    && parameters[3].ParameterType.IsEnum
                    && parameters[4].ParameterType == typeof(bool)
                    && method.ReturnType == typeof(bool);
            });
        _sainSprintUrgency = _sainRunToPoint?.GetParameters()[3].ParameterType;

        if (_sainGetBot == null
            || _sainEnemyController == null
            || _sainBotActivation == null
            || _sainInCombat == null
            || _sainMover == null
            || _sainNavMeshPosition == null
            || _sainCheckAddEnemy == null
            || _sainGoalEnemy?.GetSetMethod(nonPublic: true) == null
            || _sainEnemyInfo == null
            || _sainUpdateLastSeen == null
            || _sainSetInCombat == null
            || _sainRunToPoint == null
            || _sainSprintUrgency == null)
        {
            ResetSainMembers();
        }
    }

    private static Type FindType(string fullName, string assemblyName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
                continue;

            return assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
        }

        return null;
    }

    private static bool IsFinite(Vector3 value)
        => !float.IsNaN(value.x)
           && !float.IsNaN(value.y)
           && !float.IsNaN(value.z)
           && !float.IsInfinity(value.x)
           && !float.IsInfinity(value.y)
           && !float.IsInfinity(value.z);

    private static bool HasImmediateDanger(BotOwner source)
        => source?.BewareGrenade?.ShallRunAway() == true
           || source?.BewareBTR?.ShallRunAway() == true
           || source?.ArtilleryDangerPlace?.ShallRunAway() == true
           || source?.FlashGrenade?.IsFlashed == true;

    // Child Using means a medical hands operation is active. Aggregate Using,
    // the available-work flags and the last brain decision can all remain stale
    // after cancellation and must not strand a surviving bot.
    private static bool IsMedicalActionActive(BotOwner source)
        => source?.Medecine?.FirstAid?.Using == true
           || source?.Medecine?.SurgicalKit?.Using == true
           || source?.Medecine?.Stimulators?.Using == true;

    private static void LogRouteOnce(
        int epoch,
        BotOwner source,
        BotOwner target,
        string adapter,
        float squaredDistance)
    {
        string key = epoch + ":" + (source?.ProfileId ?? "?") + ":"
            + (target?.ProfileId ?? "?") + ":" + adapter;
        if (!LoggedRoutes.Add(key))
            return;

        Plugin.ModLogger.LogInfo(
            $"Team Arena pursuit {adapter}: {Describe(source)} -> "
            + $"'{target?.Profile?.Nickname ?? "?"}' ({Mathf.Sqrt(Mathf.Max(0f, squaredDistance)):0.0}m).");
    }

    private static void LogPathRejectedOnce(
        int epoch,
        BotOwner source,
        BotOwner target,
        string adapter)
    {
        string targetProfile = target?.ProfileId ?? "?";
        string key = epoch + ":" + (source?.ProfileId ?? "?") + ":" + targetProfile + ":" + adapter + ":path";
        if (!LoggedFailures.Add(key))
            return;

        Plugin.ModLogger.LogWarning(
            $"Team Arena pursuit {adapter} could not obtain movement for {Describe(source)} "
            + $"toward '{target?.Profile?.Nickname ?? "?"}' in round epoch {epoch}; it will retry without teleporting.");
    }

    private static void LogAwarenessRejectedOnce(
        int epoch,
        BotOwner source,
        BotOwner target)
    {
        string targetProfile = target?.ProfileId ?? "?";
        string key = epoch + ":" + (source?.ProfileId ?? "?") + ":" + targetProfile + ":eft-awareness:rejected";
        if (!LoggedFailures.Add(key))
            return;

        Plugin.ModLogger.LogWarning(
            $"Team Arena pursuit could not register '{target?.Profile?.Nickname ?? "?"}' as a live enemy "
            + $"for {Describe(source)} in round epoch {epoch}; another live target will be tried.");
    }

    private static void LogSainIdentityRejectedOnce(
        int epoch,
        BotOwner source,
        string targetProfile)
    {
        string key = epoch + ":" + (source?.ProfileId ?? "?") + ":"
            + (targetProfile ?? "?") + ":sain-awareness:stale";
        if (!LoggedFailures.Add(key))
            return;

        Plugin.ModLogger.LogWarning(
            $"Team Arena left SAIN awareness unchanged for {Describe(source)} because its cached "
            + $"enemy '{targetProfile ?? "?"}' belongs to another EFT body or round.");
    }

    private static void LogOwnerUnavailableOnce(
        int epoch,
        BotOwner source,
        BotOwner target,
        string adapter)
    {
        string targetProfile = target?.ProfileId ?? "?";
        string key = epoch + ":" + (source?.ProfileId ?? "?") + ":" + targetProfile + ":" + adapter + ":owner";
        if (!LoggedFailures.Add(key))
            return;

        Plugin.ModLogger.LogWarning(
            $"Team Arena pursuit expected {adapter} to own {Describe(source)}, but its per-bot "
            + "adapter was unavailable; Arena did not start a competing native mover.");
    }

    private static void LogDecisionOnce(
        int epoch,
        BotOwner source,
        BotOwner target,
        TeamArenaApproachDecision decision)
    {
        string targetProfile = target?.ProfileId ?? "?";
        string key = epoch + ":" + (source?.ProfileId ?? "?") + ":" + targetProfile + ":decision:" + decision;
        if (!LoggedFailures.Add(key))
            return;

        Plugin.ModLogger.LogInfo(
            $"Team Arena pursuit paused for {Describe(source)} toward "
            + $"'{target?.Profile?.Nickname ?? "?"}' while its current {decision} action owns movement.");
    }

    private static void LogStalledOnce(
        int epoch,
        BotOwner source,
        BotOwner target,
        string adapter,
        float sourceMovement,
        float approachProgress,
        float distanceGain)
    {
        string targetProfile = target?.ProfileId ?? "?";
        string key = epoch + ":" + (source?.ProfileId ?? "?") + ":" + targetProfile + ":" + adapter + ":stalled";
        if (!LoggedFailures.Add(key))
            return;

        Plugin.ModLogger.LogWarning(
            $"Team Arena pursuit {adapter} stalled for {Describe(source)} toward "
            + $"'{target?.Profile?.Nickname ?? "?"}' in round epoch {epoch}: "
            + $"source moved {sourceMovement:0.0}m, approached {approachProgress:0.0}m, "
            + $"distance gain {distanceGain:0.0}m over "
            + $"{PftStallProbeSeconds:0}s.");
    }

    private static void LogPftDeferredOnce(
        int epoch,
        BotOwner source,
        BotOwner target)
    {
        string targetProfile = target?.ProfileId ?? "?";
        string key = epoch + ":" + (source?.ProfileId ?? "?") + ":" + targetProfile + ":pft-deferred";
        if (!LoggedFailures.Add(key))
            return;

        Plugin.ModLogger.LogInfo(
            $"Team Arena pursuit pft-push deferred for {Describe(source)} while PitFireTeam "
            + "keeps a committed action; Arena will retry without taking over its mover.");
    }

    private static void LogPftLeaseDeferredOnce(
        int epoch,
        BotOwner source,
        BotOwner target,
        TeamArenaPftLeaseState state)
    {
        string targetProfile = target?.ProfileId ?? "?";
        string key = epoch + ":" + (source?.ProfileId ?? "?") + ":" + targetProfile
            + ":pft-lease-deferred:" + state;
        if (!LoggedFailures.Add(key))
            return;

        Plugin.ModLogger.LogInfo(
            $"Team Arena pursuit deferred for {Describe(source)} toward "
            + $"'{target?.Profile?.Nickname ?? "?"}' because PitFireTeam still reports "
            + $"{state} movement ownership; Arena will retry without overwriting it.");
    }

    private static void LogPftTargetSwitchDeferredOnce(
        int epoch,
        BotOwner source,
        string previousTargetProfile,
        BotOwner nextTarget)
    {
        string nextProfile = nextTarget?.ProfileId ?? "?";
        string key = epoch + ":" + (source?.ProfileId ?? "?") + ":"
            + previousTargetProfile + ":" + nextProfile + ":pft-target-switch";
        if (!LoggedFailures.Add(key))
            return;

        Plugin.ModLogger.LogInfo(
            $"Team Arena pursuit is releasing {Describe(source)} from PFT target "
            + $"'{previousTargetProfile}' before binding '{nextTarget?.Profile?.Nickname ?? "?"}' ({nextProfile}).");
    }

    private static void LogPftOwnershipChangedOnce(
        int epoch,
        BotOwner source,
        BotOwner nextTarget)
    {
        string nextProfile = nextTarget?.ProfileId ?? "?";
        string key = epoch + ":" + (source?.ProfileId ?? "?") + ":"
            + nextProfile + ":pft-owner-changed";
        if (!LoggedFailures.Add(key))
            return;

        Plugin.ModLogger.LogInfo(
            $"Team Arena pursuit found a newer PFT push owner for {Describe(source)}; "
            + $"Arena left it intact before attempting target '{nextProfile}'.");
    }

    private static void LogOnce(
        int epoch,
        BotOwner source,
        string targetProfile,
        string adapter,
        Exception exception)
    {
        string profile = source?.ProfileId ?? "?";
        string key = epoch + ":" + profile + ":" + (targetProfile ?? "?") + ":" + adapter;
        if (!LoggedFailures.Add(key))
            return;

        Plugin.ModLogger.LogWarning(
            $"Team Arena pursuit {adapter} failed for {Describe(source)} "
            + $"in round epoch {epoch}; movement ownership was left unchanged: {exception.GetBaseException()}");
    }

    private static string Describe(BotOwner source)
    {
        string name = source?.Profile?.Nickname ?? "?";
        string profile = source?.ProfileId ?? "?";
        string side = PitFireTeamCompat.IsTeamMember(source) ? "ally" : "opponent";
        return $"{side} '{name}' ({profile}, state={source?.BotState.ToString() ?? "missing"})";
    }

    private static void ClearRoundState()
    {
        LoggedFailures.Clear();
        LoggedRoutes.Clear();
        KnownTargetsBySource.Clear();
        SainAwarenessBySource.Clear();
        PftPushTargetsBySource.Clear();
        PftPushLeasesBySource.Clear();
        PftTacticalLeasesBySource.Clear();
        NextSainMoveBySource.Clear();
        PftMovementBySource.Clear();
        RouteMovementBySource.Clear();
        RejectedTargetsBySource.Clear();
        BigBrainUnavailableSources.Clear();
        _activationLogged = false;
    }

    private static void ResetOptionalBridges()
    {
        _pftBridgeResolved = false;
        _pftAssemblyDetected = false;
        _pftCombatEnabled = null;
        _pftRuntimeCallbacks = null;
        _pftBossPlayersInstance = null;
        _pftGetFollower = null;
        _pftGetFollowerByProfileId = null;
        _pftGetBot = null;
        _pftSetPushEnemy = null;
        _pftSetTacticalPoint = null;
        _pftCancelPush = null;
        _pftClearCommand = null;
        _pftTryPeekCommand = null;
        _pftTryGetOrderedPushTargetLock = null;
        _pftHasPushCancelRequest = null;
        _pftTryConsumePushCancelRequest = null;
        _pftCombatTactic = null;
        _pftCommandType = null;
        _pftPushSequence = null;
        _bigBrainGetActiveLogic = null;
        _sainBridgeResolved = false;
        _sainAssemblyDetected = false;
        ResetSainMembers();
    }

    private static void ResetSainMembers()
    {
        _sainGetBot = null;
        _sainEnemyController = null;
        _sainBotActivation = null;
        _sainInCombat = null;
        _sainMover = null;
        _sainNavMeshPosition = null;
        _sainCheckAddEnemy = null;
        _sainGoalEnemy = null;
        _sainEnemyInfo = null;
        _sainUpdateLastSeen = null;
        _sainSetInCombat = null;
        _sainRunToPoint = null;
        _sainSprintUrgency = null;
    }
}
