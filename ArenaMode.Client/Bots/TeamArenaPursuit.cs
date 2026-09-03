using ArenaMode.Compat;
using ArenaMode.Session;
using BepInEx.Bootstrap;
using EFT;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;

namespace ArenaMode.Bots;

internal enum TeamArenaSainSourceState
{
    NotPresent,
    Exact,
    Incompatible
}

/// <summary>
/// Keeps a protected 5v5 moving after the local PMC has died. The caller owns
/// roster validity. This service refreshes each bot's nearest live opponent and
/// publishes one NavMesh-validated movement lease. The optional exact-version
/// BigBrain companion is the sole approach owner; native PFT and SAIN combat
/// resume as soon as a target is visible and shootable.
/// </summary>
internal static class TeamArenaPursuit
{
    private const string SainPluginGuid = "me.sol.sain";
    private const float SweepSeconds = 1f;
    private const float BigBrainLeaseSeconds = 2.5f;
    private const float RouteProbeSeconds = 4f;
    private const float MoverAnchorToleranceMeters = 3f;
    private const float MoverAnchorSampleRadius = 0.75f;
    private const float BodyAnchorSampleRadius = 2.6f;
    private const float AnchorHeightToleranceMeters = 1f;

    private static readonly HashSet<string> LoggedFailures = new(StringComparer.Ordinal);
    private static readonly HashSet<string> LoggedRoutes = new(StringComparer.Ordinal);
    private static readonly NavMeshPath RoutePath = new();
    private static readonly Dictionary<string, string> KnownTargetsBySource = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, RouteMovementProbe> RouteMovementBySource = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, HashSet<string>> RejectedTargetsBySource = new(StringComparer.Ordinal);

    private static int _epoch = -1;
    private static float _nextSweepAt;
    private static bool _activationLogged;

    private static bool _sainBridgeResolved;
    private static bool _sainAssemblyDetected;
    private static Type _sainBotType;
    private static MethodInfo _sainGetBot;
    private static PropertyInfo _sainBotOwner;
    private static PropertyInfo _sainPlayer;
    private static PropertyInfo _sainEnemyController;
    private static MethodInfo _sainCheckAddEnemy;
    private static MethodInfo _sainRemoveEnemy;
    private static PropertyInfo _sainGoalEnemy;
    private static PropertyInfo _sainEnemyInfo;
    private static PropertyInfo _sainEnemyPlayer;
    private static MethodInfo _sainUpdateLastSeen;

    private readonly struct RouteMovementProbe
    {
        public RouteMovementProbe(
            string targetProfileId,
            Vector3 sourcePosition,
            float observedAt,
            int stalledWindows)
        {
            TargetProfileId = targetProfileId;
            SourcePosition = sourcePosition;
            ObservedAt = observedAt;
            StalledWindows = stalledWindows;
        }

        public string TargetProfileId { get; }
        public Vector3 SourcePosition { get; }
        public float ObservedAt { get; }
        public int StalledWindows { get; }
    }

    public static void Reset()
    {
        TeamArenaPursuitLeaseBridge.Reset();
        _epoch = -1;
        _nextSweepAt = 0f;
        ClearRoundState();
        ResetSainBridge();
    }

    public static void BeginRound(int epoch)
    {
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
            RefreshSafely(ally, liveOpponents, epoch);

        foreach (var opponent in liveOpponents)
            RefreshSafely(opponent, liveAllies, epoch);
    }

    private static void RefreshSafely(
        BotOwner source,
        IReadOnlyList<BotOwner> candidates,
        int epoch)
    {
        try
        {
            RefreshNearest(source, candidates, epoch);
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
            Player body = candidate?.GetPlayer;
            string profileId = candidate?.ProfileId?.Trim();
            if (candidate == null
                || candidate.BotState != EBotState.Active
                || candidate.HealthController?.IsAlive != true
                || body == null
                || body.HealthController?.IsAlive != true
                || !string.Equals(body.ProfileId?.Trim(), profileId, StringComparison.Ordinal)
                || !ReferenceEquals(body.AIData?.BotOwner, candidate)
                || !IsFinite(candidate.Position))
            {
                continue;
            }

            if (string.IsNullOrEmpty(profileId) || !profiles.Add(profileId))
                continue;

            result.Add(candidate);
        }

        return result;
    }

    private static void RefreshNearest(
        BotOwner source,
        IReadOnlyList<BotOwner> candidates,
        int epoch)
    {
        string sourceProfile = source.ProfileId?.Trim();
        if (string.IsNullOrEmpty(sourceProfile))
            return;

        if (TeamArenaPursuitLeaseBridge.TryConsumeRejection(
                sourceProfile,
                epoch,
                out string rejectedTargetProfile))
        {
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

        // Keep SAIN's exact live wrapper informed for the handoff back to its
        // ordinary combat layer. This mutates awareness only; Arena never calls
        // SAIN's mover or forces its transient combat flag.
        TeamArenaSainAwarenessState sainAwareness = TryRefreshSain(
            source,
            nearest,
            enemyInfo.Person,
            ref enemyInfo,
            epoch,
            targetProfile);
        // SAIN's stale-wrapper repair is destructive to its associated EFT
        // EnemyInfo. Never publish a lease from the pre-repair snapshot unless
        // the exact current-round native relation still exists afterward. A
        // target-only SAIN miss is best effort because the exact native relation
        // and BigBrain consumer can still provide one safe movement owner.
        bool exactNativeRelation = GoalEnemyMatches(source, nearest, enemyInfo);
        if (!TeamArenaPursuitPolicy.CanPublishAfterAwareness(
                sainAwareness,
                exactNativeRelation))
        {
            TeamArenaPursuitLeaseBridge.Revoke(sourceProfile, epoch);
            RejectTargetForCurrentPass(source, nearest, epoch);
            LogAwarenessRejectedOnce(epoch, source, nearest);
            return;
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
            RouteMovementBySource.Remove(sourceProfile);
            if (decision == TeamArenaApproachDecision.Busy
                || decision == TeamArenaApproachDecision.ImmediateDanger)
                LogDecisionOnce(epoch, source, nearest, decision);
            return;
        }

        // A distant opponent can leave EFT's standby distance cache at
        // float.MaxValue. Wake only a validated current-round body before the
        // exclusive BigBrain approach owner receives a route.
        source.StandBy?.Activate();

        if (!TryBuildRouteDestination(
                source,
                nearest,
                out Vector3 destination,
                out bool partialRoute))
        {
            TeamArenaPursuitLeaseBridge.Revoke(sourceProfile, epoch);
            RejectTargetForCurrentPass(source, nearest, epoch);
            LogPathRejectedOnce(epoch, source, nearest, "navmesh");
            return;
        }

        if (!TeamArenaPursuitLeaseBridge.Publish(
                sourceProfile,
                targetProfile,
                destination,
                epoch,
                BigBrainLeaseSeconds,
                out TeamArenaPursuitLease bigBrainLease))
        {
            RouteMovementBySource.Remove(sourceProfile);
            LogOwnerUnavailableOnce(epoch, source, nearest, "bigbrain");
            return;
        }

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
            ObserveRouteProgress(
                source,
                nearest,
                destination,
                epoch);
            LogRouteOnce(epoch, source, nearest, partialRoute ? "bigbrain-partial" : "bigbrain", bestDistance);
            return;
        }

        if (ownerDecision == TeamArenaConsumerDecision.AwaitingAcknowledgement)
            return;

        TeamArenaPursuitLeaseBridge.Revoke(
            sourceProfile,
            epoch,
            bigBrainLease.Generation);
        RouteMovementBySource.Remove(sourceProfile);
        LogOwnerUnavailableOnce(epoch, source, nearest, "bigbrain");
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
        if (!TryResolveCurrentEnemyRelation(
                source,
                target,
                source?.Memory?.GoalEnemy,
                out _,
                out _,
                out EnemyInfo resolved,
                out bool hasEnemyInfo)
            || !hasEnemyInfo
            || resolved == null)
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

        RouteMovementBySource.Remove(sourceProfile);
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
        bool hasCanonicalPlayerGroupKey = TeamArenaEnemyIdentityPolicy.ContainsReferenceKey(
            group.Enemies,
            playerKey);
        if (!hasCanonicalPlayerGroupKey)
            group.AddEnemy(playerKey, EBotEnemyCause.bossKillArena);

        if (!TryResolveCanonicalPlayerEnemy(
                source,
                target,
                out BotGroupEnemyInfo groupInfo,
                out enemyInfo,
                out bool hasEnemyInfo))
        {
            return false;
        }

        // PitFireTeam registers AI bodies as BotOwner while EFT's alive-player
        // lookup returns their separate Player objects. BotsGroup.IsEnemy hides
        // that distinction by comparing IDs, while PFT's SAIN acquire gate
        // intentionally accepts same-side AI only as the concrete Player. Keep
        // PFT's BotOwner relation, but establish and use a complete Player pair.
        group.ReportAboutEnemy(playerKey, EEnemyPartVisibleType.Sence, source);
        groupInfo.IsLastPositionChecked = false;

        if (!hasEnemyInfo)
        {
            source.Memory.AddEnemy(playerKey, groupInfo, onActivation: false);
            if (!TryResolveCanonicalPlayerEnemy(
                    source,
                    target,
                    out BotGroupEnemyInfo reboundGroupInfo,
                    out enemyInfo,
                    out hasEnemyInfo)
                || !hasEnemyInfo
                || !ReferenceEquals(reboundGroupInfo, groupInfo))
            {
                return false;
            }
        }

        if (enemyInfo == null
            || !ReferenceEquals(enemyInfo.Person, playerKey)
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

    private static bool TryResolveCanonicalPlayerEnemy(
        BotOwner source,
        BotOwner target,
        out BotGroupEnemyInfo groupInfo,
        out EnemyInfo enemyInfo,
        out bool hasEnemyInfo)
    {
        groupInfo = null;
        enemyInfo = null;
        hasEnemyInfo = false;
        var group = source?.BotsGroup;
        Player targetPlayer = target?.GetPlayer;
        if (group == null
            || targetPlayer == null
            || source?.EnemiesController?.EnemyInfos == null)
        {
            return false;
        }

        return TeamArenaEnemyIdentityPolicy.TryResolveValid(
            group.Enemies,
            source.EnemiesController.EnemyInfos,
            targetPlayer,
            targetPlayer,
            preferredEnemyInfo: null,
            groupIsValid: (_, candidateGroup) =>
                ReferenceEquals(candidateGroup?._botGroup, group)
                && ReferenceEquals(candidateGroup.Player, targetPlayer),
            pairIsValid: (candidateKey, candidateGroup, candidateEnemy) =>
                ReferenceEquals(candidateEnemy?.Person, candidateKey)
                && ReferenceEquals(candidateEnemy.GroupInfo, candidateGroup)
                && ReferenceEquals(candidateEnemy.Owner, source)
                && ReferenceEquals(candidateEnemy.GroupOwner, group),
            out _,
            out groupInfo,
            out enemyInfo,
            out hasEnemyInfo);
    }

    private static bool TryResolveCurrentEnemyRelation(
        BotOwner source,
        BotOwner target,
        EnemyInfo preferredEnemyInfo,
        out IPlayer enemyKey,
        out BotGroupEnemyInfo groupInfo,
        out EnemyInfo enemyInfo,
        out bool hasEnemyInfo)
    {
        enemyKey = null;
        groupInfo = null;
        enemyInfo = null;
        hasEnemyInfo = false;
        var group = source?.BotsGroup;
        Player targetPlayer = target?.GetPlayer;
        if (group == null
            || targetPlayer == null
            || source?.EnemiesController?.EnemyInfos == null)
        {
            return false;
        }

        return TeamArenaEnemyIdentityPolicy.TryResolveValid(
            group.Enemies,
            source.EnemiesController.EnemyInfos,
            targetPlayer,
            target,
            preferredEnemyInfo,
            groupIsValid: (_, candidateGroup) =>
                ReferenceEquals(candidateGroup?._botGroup, group)
                && ReferenceEquals(candidateGroup.Player, targetPlayer),
            pairIsValid: (candidateKey, candidateGroup, candidateEnemy) =>
                ReferenceEquals(candidateEnemy?.Person, candidateKey)
                && ReferenceEquals(candidateEnemy.GroupInfo, candidateGroup)
                && ReferenceEquals(candidateEnemy.Owner, source)
                && ReferenceEquals(candidateEnemy.GroupOwner, group),
            out enemyKey,
            out groupInfo,
            out enemyInfo,
            out hasEnemyInfo);
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

    private static bool TryBuildRouteDestination(
        BotOwner source,
        BotOwner target,
        out Vector3 destination,
        out bool partialRoute)
    {
        destination = Vector3.zero;
        partialRoute = false;
        if (source?.Mover == null || target == null)
            return false;

        Vector3 sourceMoverPosition = source.Mover.PositionOnWay;
        bool sourceMoverMatchesBody = IsFinite(sourceMoverPosition)
            && IsFinite(source.Position)
            && (sourceMoverPosition - source.Position).sqrMagnitude
                <= MoverAnchorToleranceMeters * MoverAnchorToleranceMeters;
        if (!sourceMoverMatchesBody
            || !TrySampleAnchor(
                sourceMoverPosition,
                MoverAnchorSampleRadius,
                out NavMeshHit sourceHit))
        {
            if (!IsFinite(source.Position)
                || !TrySampleAnchor(
                    source.Position,
                    BodyAnchorSampleRadius,
                    out sourceHit))
            {
                return false;
            }
        }

        Vector3 targetMoverPosition = target.Mover != null
            ? target.Mover.PositionOnWay
            : Vector3.positiveInfinity;
        bool moverAnchorMatchesBody = IsFinite(targetMoverPosition)
            && IsFinite(target.Position)
            && (targetMoverPosition - target.Position).sqrMagnitude
                <= MoverAnchorToleranceMeters * MoverAnchorToleranceMeters;
        TeamArenaRouteCandidate moverRoute = TeamArenaRouteCandidate.None;
        Vector3 moverDestination = Vector3.zero;
        float moverAdvance = -1f;
        if (moverAnchorMatchesBody)
        {
            moverRoute = TryValidateRoute(
                sourceHit.position,
                source.Position,
                targetMoverPosition,
                MoverAnchorSampleRadius,
                out moverDestination);
            if (moverRoute == TeamArenaRouteCandidate.Partial)
                moverAdvance = (moverDestination - source.Position).sqrMagnitude;
        }

        TeamArenaRouteCandidate bodyRoute = TeamArenaRouteCandidate.None;
        Vector3 bodyDestination = Vector3.zero;
        float bodyAdvance = -1f;
        if (IsFinite(target.Position))
        {
            bodyRoute = TryValidateRoute(
                sourceHit.position,
                source.Position,
                target.Position,
                BodyAnchorSampleRadius,
                out bodyDestination);
            if (bodyRoute == TeamArenaRouteCandidate.Partial)
                bodyAdvance = (bodyDestination - source.Position).sqrMagnitude;
        }

        TeamArenaRouteSelection selection = TeamArenaPursuitPolicy.SelectRouteCandidate(
            moverRoute,
            moverAdvance,
            bodyRoute,
            bodyAdvance);
        if (selection == TeamArenaRouteSelection.Mover)
        {
            destination = moverDestination;
            partialRoute = moverRoute == TeamArenaRouteCandidate.Partial;
            return true;
        }
        if (selection == TeamArenaRouteSelection.Body)
        {
            destination = bodyDestination;
            partialRoute = bodyRoute == TeamArenaRouteCandidate.Partial;
            return true;
        }
        return false;
    }

    private static TeamArenaRouteCandidate TryValidateRoute(
        Vector3 sourceAnchor,
        Vector3 sourceBody,
        Vector3 candidate,
        float sampleRadius,
        out Vector3 destination)
    {
        destination = Vector3.zero;
        if (!TrySampleAnchor(candidate, sampleRadius, out NavMeshHit targetHit))
        {
            return TeamArenaRouteCandidate.None;
        }

        if (!NavMesh.CalculatePath(sourceAnchor, targetHit.position, NavMesh.AllAreas, RoutePath))
        {
            return TeamArenaRouteCandidate.None;
        }

        Vector3[] corners = RoutePath.corners;
        if (RoutePath.status == NavMeshPathStatus.PathComplete)
        {
            if (corners == null
                || (corners.Length < 2
                    && (targetHit.position - sourceAnchor).sqrMagnitude
                        > TeamArenaPursuitPolicy.DestinationReachDistanceMeters
                          * TeamArenaPursuitPolicy.DestinationReachDistanceMeters))
            {
                return TeamArenaRouteCandidate.None;
            }

            destination = targetHit.position;
            return IsFinite(destination)
                ? TeamArenaRouteCandidate.Complete
                : TeamArenaRouteCandidate.None;
        }

        if (RoutePath.status != NavMeshPathStatus.PathPartial
            || corners == null
            || !TeamArenaPursuitPolicy.CanUsePartialRoute(
                corners.Length,
                (corners[corners.Length - 1] - sourceAnchor).sqrMagnitude,
                (corners[corners.Length - 1] - sourceBody).sqrMagnitude))
        {
            return TeamArenaRouteCandidate.None;
        }

        // The last partial corner is on the source's reachable NavMesh island.
        // Publishing the original target would make GoToPoint reject the lease;
        // publishing this corner lets the bot close distance and re-probe from a
        // new position on the next bounded sweep.
        destination = corners[corners.Length - 1];
        return IsFinite(destination)
            ? TeamArenaRouteCandidate.Partial
            : TeamArenaRouteCandidate.None;
    }

    private static bool TryCalculateRouteDistance(
        Vector3 source,
        Vector3 destination,
        out float routeDistanceMeters)
    {
        routeDistanceMeters = float.PositiveInfinity;
        if (!IsFinite(source)
            || !IsFinite(destination)
            || !TrySampleAnchor(source, BodyAnchorSampleRadius, out NavMeshHit sourceHit)
            || !NavMesh.CalculatePath(
                sourceHit.position,
                destination,
                NavMesh.AllAreas,
                RoutePath)
            || RoutePath.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        Vector3[] corners = RoutePath.corners;
        if (corners == null)
            return false;

        routeDistanceMeters = 0f;
        for (int index = 1; index < corners.Length; index++)
            routeDistanceMeters += Vector3.Distance(corners[index - 1], corners[index]);

        return !float.IsNaN(routeDistanceMeters)
               && !float.IsInfinity(routeDistanceMeters);
    }

    private static bool TrySampleAnchor(
        Vector3 requested,
        float sampleRadius,
        out NavMeshHit hit)
    {
        if (!NavMesh.SamplePosition(requested, out hit, sampleRadius, NavMesh.AllAreas))
            return false;

        return Mathf.Abs(hit.position.y - requested.y) <= AnchorHeightToleranceMeters;
    }

    private static void ObserveRouteProgress(
        BotOwner source,
        BotOwner target,
        Vector3 destination,
        int epoch)
    {
        string sourceProfile = source.ProfileId;
        string targetProfile = target.ProfileId;
        if (!RouteMovementBySource.TryGetValue(sourceProfile, out RouteMovementProbe probe)
            || !string.Equals(probe.TargetProfileId, targetProfile, StringComparison.Ordinal))
        {
            RouteMovementBySource[sourceProfile] = new RouteMovementProbe(
                targetProfile,
                source.Position,
                Time.time,
                stalledWindows: 0);
            return;
        }

        if (Time.time - probe.ObservedAt < RouteProbeSeconds)
            return;

        float physicalMovement = Vector3.Distance(source.Position, probe.SourcePosition);
        if (!TryCalculateRouteDistance(
                probe.SourcePosition,
                destination,
                out float previousRouteDistanceMeters)
            || !TryCalculateRouteDistance(
                source.Position,
                destination,
                out float currentRouteDistanceMeters))
        {
            RejectTargetForCurrentPass(source, target, epoch);
            LogPathRejectedOnce(epoch, source, target, "progress-navmesh");
            return;
        }

        if (TeamArenaPursuitOwnerPolicy.HasRouteProgress(
                physicalMovement,
                previousRouteDistanceMeters,
                currentRouteDistanceMeters))
        {
            RouteMovementBySource[sourceProfile] = new RouteMovementProbe(
                targetProfile,
                source.Position,
                Time.time,
                stalledWindows: 0);
            return;
        }

        int stalledWindows = TeamArenaPursuitOwnerPolicy.NextStalledWindowCount(
            probe.StalledWindows,
            physicalMovement,
            previousRouteDistanceMeters,
            currentRouteDistanceMeters);
        if (!TeamArenaPursuitOwnerPolicy.ShouldRetarget(stalledWindows))
        {
            // Keep the original source position across consecutive windows. A
            // slow but real detour therefore counts as progress, while pacing
            // or spinning around the same obstacle eventually expires.
            RouteMovementBySource[sourceProfile] = new RouteMovementProbe(
                targetProfile,
                probe.SourcePosition,
                Time.time,
                stalledWindows);
            return;
        }

        RejectTargetForCurrentPass(source, target, epoch);
        LogStalledOnce(
            epoch,
            source,
            target,
            physicalMovement,
            previousRouteDistanceMeters,
            currentRouteDistanceMeters,
            destination);
    }

    private static TeamArenaSainAwarenessState TryRefreshSain(
        BotOwner source,
        BotOwner targetOwner,
        IPlayer targetPerson,
        ref EnemyInfo expectedNativeEnemy,
        int epoch,
        string targetProfile)
    {
        bool exactSourceEstablished = false;
        try
        {
            if (targetOwner?.GetPlayer == null
                || targetPerson == null
                || expectedNativeEnemy == null
                || !ReferenceEquals(expectedNativeEnemy.Person, targetPerson))
            {
                return TeamArenaSainAwarenessState.TargetUnavailable;
            }

            TeamArenaSainSourceState sourceState = TryGetExactSainBot(
                source,
                out object sainBot);
            if (sourceState == TeamArenaSainSourceState.NotPresent)
                return TeamArenaSainAwarenessState.NotPresent;

            if (sourceState != TeamArenaSainSourceState.Exact)
            {
                LogSainIdentityRejectedOnce(epoch, source, targetProfile);
                return TeamArenaSainAwarenessState.SourceIncompatible;
            }
            exactSourceEstablished = true;

            object enemyController = _sainEnemyController.GetValue(sainBot);
            if (enemyController == null)
                return TeamArenaSainAwarenessState.SourceIncompatible;

            EnemyInfo currentNativeEnemy = expectedNativeEnemy;
            EnemyInfo exactSainNativeEnemy = null;
            TeamArenaSainRebindOutcome outcome = TeamArenaSainRebindTransaction.TryEnsureExact(
                resolveOrCreate: () => _sainCheckAddEnemy.Invoke(
                    enemyController,
                    new object[] { currentNativeEnemy.Person }),
                isExact: candidate => IsExactCurrentSainEnemy(
                    candidate,
                    source,
                    targetOwner,
                    out exactSainNativeEnemy),
                removeStale: () => _sainRemoveEnemy.Invoke(
                    enemyController,
                    new object[] { targetProfile }),
                rebuildExactNative: () =>
                {
                    if (!RefreshNative(source, targetOwner, out EnemyInfo rebuiltNativeEnemy)
                        || rebuiltNativeEnemy == null
                        || !IsCurrentTargetPerson(rebuiltNativeEnemy.Person, targetOwner))
                    {
                        return false;
                    }

                    currentNativeEnemy = rebuiltNativeEnemy;
                    return true;
                },
                out object enemy);
            if (outcome != TeamArenaSainRebindOutcome.ExactExisting
                && outcome != TeamArenaSainRebindOutcome.ExactRebuilt)
            {
                expectedNativeEnemy = currentNativeEnemy;
                LogSainIdentityRejectedOnce(epoch, source, targetProfile);
                return TeamArenaSainAwarenessState.TargetUnavailable;
            }

            if (exactSainNativeEnemy == null || source.Memory == null)
            {
                expectedNativeEnemy = currentNativeEnemy;
                LogSainIdentityRejectedOnce(epoch, source, targetProfile);
                return TeamArenaSainAwarenessState.TargetUnavailable;
            }

            // Keep SAIN and EFT on the same one of the two valid current-body
            // pairs. Otherwise visibility on one pair and CanShoot on the other
            // can make each system believe the other owns combat.
            currentNativeEnemy = exactSainNativeEnemy;
            source.Memory.GoalEnemy = currentNativeEnemy;
            expectedNativeEnemy = currentNativeEnemy;
            if (!GoalEnemyMatches(source, targetOwner, currentNativeEnemy))
            {
                LogSainIdentityRejectedOnce(epoch, source, targetProfile);
                return TeamArenaSainAwarenessState.TargetUnavailable;
            }

            _sainUpdateLastSeen.Invoke(enemy, new object[] { targetOwner.Position, Time.time });
            _sainGoalEnemy.GetSetMethod(nonPublic: true)?.Invoke(enemyController, new[] { enemy });
            if (!ReferenceEquals(_sainGoalEnemy.GetValue(enemyController), enemy))
            {
                LogSainIdentityRejectedOnce(epoch, source, targetProfile);
                return TeamArenaSainAwarenessState.TargetUnavailable;
            }

            return TeamArenaSainAwarenessState.Exact;
        }
        catch (Exception ex)
        {
            LogOnce(epoch, source, targetProfile, "sain-awareness", ex);
            return exactSourceEstablished
                ? TeamArenaSainAwarenessState.TargetUnavailable
                : TeamArenaSainAwarenessState.SourceIncompatible;
        }
    }

    private static bool IsExactCurrentSainEnemy(
        object candidate,
        BotOwner source,
        BotOwner target,
        out EnemyInfo wrappedNativeEnemy)
    {
        wrappedNativeEnemy = null;
        if (candidate == null
            || source == null
            || target?.GetPlayer == null
            || !ReferenceEquals(_sainEnemyPlayer.GetValue(candidate), target.GetPlayer)
            || _sainEnemyInfo.GetValue(candidate) is not EnemyInfo candidateNativeEnemy)
        {
            return false;
        }

        // EFT can validly hold both a Player-keyed and a BotOwner-keyed enemy
        // pair for one AI body. SAIN often observes the BotOwner pair first,
        // while Arena establishes the canonical Player pair. Treat either pair
        // as current only when every concrete object still belongs to this exact
        // source, group and live target body. Requiring the same EnemyInfo object
        // across both systems caused us to tear down a valid same-round wrapper.
        if (!TryResolveCurrentEnemyRelation(
                source,
                target,
                candidateNativeEnemy,
                out _,
                out _,
                out EnemyInfo registered,
                out bool hasEnemyInfo)
            || !hasEnemyInfo
            || !ReferenceEquals(registered, candidateNativeEnemy))
        {
            return false;
        }

        wrappedNativeEnemy = candidateNativeEnemy;
        return true;
    }

    private static TeamArenaSainSourceState TryGetExactSainBot(
        BotOwner source,
        out object sainBot)
    {
        sainBot = null;
        ResolveSainBridge();
        if (!_sainBridgeResolved)
            return TeamArenaSainSourceState.Incompatible;

        if (!_sainAssemblyDetected)
            return TeamArenaSainSourceState.NotPresent;

        if (_sainGetBot == null
            || _sainBotType == null
            || source == null
            || string.IsNullOrEmpty(source.ProfileId))
        {
            return TeamArenaSainSourceState.Incompatible;
        }

        object[] getArgs = { source.ProfileId, null };
        if (_sainGetBot.Invoke(null, getArgs) is not bool found)
            return TeamArenaSainSourceState.Incompatible;

        object attached = source.GetComponent(_sainBotType);
        bool registeredExact = IsExactSainSource(getArgs[1], source);
        bool attachedExact = IsExactSainSource(attached, source);
        TeamArenaSainSourceSelection selection = TeamArenaSainSourcePolicy.Select(
            found,
            getArgs[1] != null,
            registeredExact,
            attached != null,
            attachedExact,
            ReferenceEquals(getArgs[1], attached));
        switch (selection)
        {
            case TeamArenaSainSourceSelection.RegisteredExact:
                sainBot = getArgs[1];
                return TeamArenaSainSourceState.Exact;
            case TeamArenaSainSourceSelection.AttachedExact:
                sainBot = attached;
                return TeamArenaSainSourceState.Exact;
            case TeamArenaSainSourceSelection.NotPresent:
                return TeamArenaSainSourceState.NotPresent;
            default:
                return TeamArenaSainSourceState.Incompatible;
        }
    }

    private static bool IsExactSainSource(object sainBot, BotOwner source)
        => sainBot != null
           && source != null
           && ReferenceEquals(_sainBotOwner.GetValue(sainBot), source)
           && ReferenceEquals(_sainPlayer.GetValue(sainBot), source.GetPlayer);

    private static void ResolveSainBridge()
    {
        if (_sainBridgeResolved)
            return;

        Assembly sainAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(
                assembly.GetName().Name,
                "SAIN",
                StringComparison.Ordinal));
        TeamArenaSainAssemblyState assemblyState = TeamArenaSainAssemblyPolicy.Select(
            Chainloader.PluginInfos.ContainsKey(SainPluginGuid),
            sainAssembly != null);
        if (assemblyState != TeamArenaSainAssemblyState.Loaded)
        {
            _sainBridgeResolved = assemblyState == TeamArenaSainAssemblyState.ConfirmedAbsent;
            return;
        }

        _sainBridgeResolved = true;
        _sainAssemblyDetected = true;
        var enableType = sainAssembly.GetType(
            "SAIN.SAINEnableClass",
            throwOnError: false,
            ignoreCase: false);
        var botType = sainAssembly.GetType(
            "SAIN.Components.BotComponent",
            throwOnError: false,
            ignoreCase: false);
        if (enableType == null || botType == null)
            return;

        _sainBotType = botType;

        _sainGetBot = enableType.GetMethod(
            "GetSAIN",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(string), botType.MakeByRefType() },
            null);
        _sainBotOwner = botType.GetProperty(
            "BotOwner",
            BindingFlags.Public | BindingFlags.Instance);
        _sainPlayer = botType.GetProperty(
            "Player",
            BindingFlags.Public | BindingFlags.Instance);
        _sainEnemyController = botType.GetProperty(
            "EnemyController",
            BindingFlags.Public | BindingFlags.Instance);

        Type controllerType = _sainEnemyController?.PropertyType;
        _sainCheckAddEnemy = controllerType?.GetMethod(
            "CheckAddEnemy",
            new[] { typeof(IPlayer) });
        _sainRemoveEnemy = controllerType?.GetMethod(
            "RemoveEnemy",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(string) },
            null);
        _sainGoalEnemy = controllerType?.GetProperty(
            "GoalEnemy",
            BindingFlags.Public | BindingFlags.Instance);
        Type enemyType = _sainCheckAddEnemy?.ReturnType;
        _sainEnemyInfo = enemyType?.GetProperty(
            "EnemyInfo",
            BindingFlags.Public | BindingFlags.Instance);
        _sainEnemyPlayer = enemyType?.GetProperty(
            "EnemyPlayer",
            BindingFlags.Public | BindingFlags.Instance);
        _sainUpdateLastSeen = enemyType?.GetMethod(
            "UpdateLastSeenPosition",
            new[] { typeof(Vector3), typeof(float) });

        if (_sainGetBot == null
            || _sainBotOwner == null
            || _sainPlayer == null
            || _sainEnemyController == null
            || _sainCheckAddEnemy == null
            || _sainRemoveEnemy == null
            || _sainGoalEnemy?.GetSetMethod(nonPublic: true) == null
            || _sainEnemyInfo == null
            || _sainEnemyPlayer == null
            || _sainUpdateLastSeen == null)
        {
            ResetSainMembers();
        }
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
            $"Team Arena pursuit {adapter} could not obtain a usable complete or advancing partial route for {Describe(source)} "
            + $"toward '{target?.Profile?.Nickname ?? "?"}' in round epoch {epoch}; another live target will be tried.");
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
            $"Team Arena could not bind SAIN awareness for {Describe(source)} to the exact live "
            + $"enemy '{targetProfile ?? "?"}'; Arena will continue only if the exact native EFT relation remains valid.");
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
            $"Team Arena pursuit expected {adapter} to own {Describe(source)}, but its exact per-bot "
            + "adapter was unavailable; Arena will retry without starting a competing mover.");
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
        float physicalMovement,
        float previousRouteDistanceMeters,
        float currentRouteDistanceMeters,
        Vector3 destination)
    {
        string targetProfile = target?.ProfileId ?? "?";
        string key = epoch + ":" + (source?.ProfileId ?? "?") + ":"
            + targetProfile + ":bigbrain:stalled";
        if (!LoggedFailures.Add(key))
            return;

        Plugin.ModLogger.LogWarning(
            $"Team Arena pursuit bigbrain stalled for {Describe(source)} toward "
            + $"'{target?.Profile?.Nickname ?? "?"}' in round epoch {epoch}: "
            + $"source displaced {physicalMovement:0.0}m around NavMesh destination "
            + $"({destination.x:0.0}, {destination.y:0.0}, {destination.z:0.0}); "
            + $"remaining route changed from {previousRouteDistanceMeters:0.0}m "
            + $"to {currentRouteDistanceMeters:0.0}m; "
            + "another live target will be tried.");
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
        RouteMovementBySource.Clear();
        RejectedTargetsBySource.Clear();
        _activationLogged = false;
    }

    private static void ResetSainBridge()
    {
        _sainBridgeResolved = false;
        _sainAssemblyDetected = false;
        ResetSainMembers();
    }

    private static void ResetSainMembers()
    {
        _sainGetBot = null;
        _sainBotType = null;
        _sainBotOwner = null;
        _sainPlayer = null;
        _sainEnemyController = null;
        _sainCheckAddEnemy = null;
        _sainRemoveEnemy = null;
        _sainGoalEnemy = null;
        _sainEnemyInfo = null;
        _sainEnemyPlayer = null;
        _sainUpdateLastSeen = null;
    }
}
