using ArenaMode.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;

namespace ArenaMode.Server.Services;

public enum ArenaSessionState
{
    Idle,
    Preparing,
    Armed,
    InRaid,
    FinalizingEnd,
    RecoveringStartFailure,
}

public enum ArenaLocalRaidStartDisposition
{
    ArenaStarted,
    Ordinary,
    DuplicateArenaStart,
    ProtectedTransitionInProgress,
    RejectedArenaStart,
}

public enum ArenaLocalRaidEndDisposition
{
    Ordinary,
    FirstArenaEnd,
    FinalizingArenaEndRetry,
    CompletedArenaEndRetry,
    RejectedProtectedEnd,
}

/// <summary>
/// Per-session arena state machine: Idle → Preparing (protected team snapshot in
/// progress) → Armed → InRaid → Idle. A failed local-start can briefly enter
/// RecoveringStartFailure while the original profile is restored. Armed expires
/// after a TTL so an abandoned matchmaking request cannot live forever.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class ArenaSessionService
{
    private sealed class SessionEntry
    {
        public ArenaSessionState State;
        public string? Map;
        public string? Mode;
        public string AreaId = ArenaAreaCatalog.FullMapId;
        public DateTime ArmedAtUtc;
        public DateTime InRaidAtUtc;
        public bool ProtectPitFireTeam;
        public string? ArmToken;
        public bool ClientRaidStarted;
        public string? NativeStartIdentity;
        public bool NativeStartTaskSettled;
        public string? NativeEndIdentity;
        public string? LastCompletedArmToken;
        public DateTime NativeEndCompletedAtUtc;
        public bool LocationMutationReady;
        public string? LocationMutationFailure;
        public object? LocationClone;
        public string? KitsJson;
        public string? LadderJson;
        public string? LadderPoolJson;
        public int LadderRungs;
    }

    private readonly Dictionary<string, SessionEntry> _sessions = new();
    private readonly Dictionary<string, Dictionary<string, DateTime>> _completedNativeEnds = new();
    private readonly Lock _lock = new();
    private readonly PitFireTeamPersistenceGuard _pitFireTeamGuard;
    private readonly ISptLogger<ArenaSessionService> logger;

    public ArenaSessionService(ISptLogger<ArenaSessionService> logger)
        : this(logger, new PitFireTeamPersistenceGuard())
    {
    }

    internal ArenaSessionService(
        ISptLogger<ArenaSessionService> logger,
        PitFireTeamPersistenceGuard pitFireTeamGuard)
    {
        this.logger = logger;
        _pitFireTeamGuard = pitFireTeamGuard;
    }

    public string Arm(
        MongoId sessionId,
        string map,
        string? mode,
        bool protectPitFireTeam = false,
        string? armToken = null,
        string? areaId = null)
    {
        if (!ArenaAreaCatalog.TryResolve(areaId!, map, mode!, protectPitFireTeam, out _, out var areaFailure))
            throw new ArgumentException(areaFailure, nameof(areaId));
        string key = sessionId.ToString();
        string token = string.IsNullOrWhiteSpace(armToken)
            ? Guid.NewGuid().ToString("N")
            : armToken;
        lock (_lock)
        {
            _sessions[key] = new SessionEntry
            {
                State = ArenaSessionState.Armed,
                Map = map.ToLowerInvariant(),
                Mode = mode,
                AreaId = ArenaAreaCatalog.NormalizeId(areaId!),
                ArmedAtUtc = DateTime.UtcNow,
                ProtectPitFireTeam = protectPitFireTeam,
                ArmToken = token,
                ClientRaidStarted = false,
                LocationMutationReady = false,
                LocationMutationFailure = null,
            };

            if (protectPitFireTeam)
            {
                _pitFireTeamGuard.BeginArmed(
                    key,
                    ArmedProtectionTtl());
            }
            // A bounded finalization guard from the previous protected team raid
            // may still be suppressing delayed PFT work. A new solo Arena arm has
            // no reason to cut that window short; it expires independently.
        }

        logger.Info(
            $"[ArenaMode] Armed for session {sessionId}: map={map}, mode={mode}, " +
            $"pitFireTeamProtection={protectPitFireTeam}, area={ArenaAreaCatalog.NormalizeId(areaId!)}");
        return token;
    }

    /// <summary>
    /// Replays the result of an already committed /raid-start announcement.
    /// SPT's POST transport can resend an identical body after losing the first
    /// response; creating another token or reporting failure would leave the
    /// client unable to consume or cancel the snapshot that is already Armed.
    /// Only the same live logical request receives the original token.
    /// </summary>
    public bool TryReplayArmedRequest(
        MongoId sessionId,
        string? map,
        string? mode,
        bool protectPitFireTeam,
        string? requestId,
        out string? armToken,
        string? areaId = null)
    {
        armToken = null;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId.ToString(), out var entry)
                || entry.State != ArenaSessionState.Armed
                || string.IsNullOrWhiteSpace(entry.ArmToken)
                || !string.Equals(entry.ArmToken, requestId, StringComparison.Ordinal)
                || entry.ProtectPitFireTeam != protectPitFireTeam
                || !string.Equals(entry.AreaId, ArenaAreaCatalog.NormalizeId(areaId!), StringComparison.Ordinal)
                || !string.Equals(entry.Map, map, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(entry.Mode, mode, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var age = DateTime.UtcNow - entry.ArmedAtUtc;
            if (age.TotalSeconds > ArenaContext.Config.ArmedFlagTtlSeconds)
                return false;

            armToken = entry.ArmToken;
            return true;
        }
    }

    /// <summary>
    /// Reserve the exact PFT transaction boundary before the main-profile
    /// snapshot. The write lease first drains already-running PFT operations;
    /// state and file protection are then published together before new work can
    /// resume. Preparing cannot be consumed as a raid arm.
    /// </summary>
    public bool BeginTeamArmPreparation(
        MongoId sessionId,
        string map,
        string? mode,
        string armToken)
        => BeginArmPreparation(
            sessionId,
            map,
            mode,
            protectPitFireTeam: true,
            armToken);

    public bool BeginArmPreparation(
        MongoId sessionId,
        string map,
        string? mode,
        bool protectPitFireTeam,
        string armToken,
        string? areaId = null)
    {
        if (!Protocol.IsLifecycleRequestId(armToken)
            || !ArenaAreaCatalog.TryResolve(areaId!, map, mode!, protectPitFireTeam, out _, out _))
            return false;

        string key = sessionId.ToString();
        if (protectPitFireTeam)
        {
            using (PitFireTeamOperationGate.ReserveProfileBoundary())
            {
                if (!PitFireTeamAsyncSideEffectGate.WaitForDrain(TimeSpan.FromSeconds(15)))
                {
                    logger.Warning(
                        "[ArenaMode] PitFireTeam delayed invite work did not settle within 15s; " +
                        "protected Arena arm rejected before snapshot.");
                    return false;
                }

                lock (_lock)
                    return PublishArmPreparationLocked(
                        key,
                        map,
                        mode,
                        protectPitFireTeam,
                        armToken,
                        ArenaAreaCatalog.NormalizeId(areaId!));
            }
        }

        lock (_lock)
            return PublishArmPreparationLocked(
                key,
                map,
                mode,
                protectPitFireTeam,
                armToken,
                ArenaAreaCatalog.NormalizeId(areaId!));
    }

    private bool PublishArmPreparationLocked(
        string key,
        string map,
        string? mode,
        bool protectPitFireTeam,
        string armToken,
        string areaId)
    {
        if (_sessions.TryGetValue(key, out var existing)
            && existing.State != ArenaSessionState.Idle)
        {
            return false;
        }

        _sessions[key] = new SessionEntry
        {
            State = ArenaSessionState.Preparing,
            Map = map.ToLowerInvariant(),
            Mode = mode,
            AreaId = areaId,
            ArmedAtUtc = DateTime.UtcNow,
            ProtectPitFireTeam = protectPitFireTeam,
            ArmToken = armToken,
            ClientRaidStarted = false,
            LocationMutationReady = false,
            LocationMutationFailure = null,
        };
        if (protectPitFireTeam)
        {
            _pitFireTeamGuard.BeginArmed(
                key,
                PreparationProtectionTtl());
        }

        logger.Info(
            $"[ArenaMode] Reserved {(protectPitFireTeam ? "PitFireTeam " : string.Empty)}" +
            $"profile boundary for session {key} before snapshot.");
        return true;
    }

    public bool CompleteTeamArmPreparation(MongoId sessionId)
        => CompleteArmPreparation(sessionId);

    public bool CompleteArmPreparation(MongoId sessionId)
    {
        string key = sessionId.ToString();
        lock (_lock)
        {
            if (!_sessions.TryGetValue(key, out var entry)
                || entry.State != ArenaSessionState.Preparing)
            {
                return false;
            }

            entry.State = ArenaSessionState.Armed;
            entry.ArmedAtUtc = DateTime.UtcNow;
            // Preparing has a deliberately longer safety window because the
            // snapshot duration is outside the session lock. Once capture is
            // complete, restart the normal armed TTL from this exact boundary.
            if (entry.ProtectPitFireTeam)
                _pitFireTeamGuard.BeginArmed(key, ArmedProtectionTtl());
            logger.Info(
                $"[ArenaMode] Completed protected PitFireTeam arm for session {sessionId}: " +
                $"map={entry.Map}, mode={entry.Mode}.");
            return true;
        }
    }

    public void CancelTeamArmPreparation(MongoId sessionId)
        => CancelArmPreparation(sessionId);

    public void CancelArmPreparation(MongoId sessionId)
    {
        string key = sessionId.ToString();
        lock (_lock)
        {
            if (!_sessions.TryGetValue(key, out var entry)
                || entry.State != ArenaSessionState.Preparing)
            {
                return;
            }

            entry.State = ArenaSessionState.Idle;
            entry.AreaId = ArenaAreaCatalog.FullMapId;
            entry.KitsJson = entry.LadderJson = entry.LadderPoolJson = null;
            entry.ProtectPitFireTeam = false;
            entry.ArmToken = null;
            entry.ClientRaidStarted = false;
            entry.LocationMutationReady = false;
            entry.LocationMutationFailure = null;
            entry.LocationClone = null;
            _pitFireTeamGuard.Clear(key);
        }
    }

    /// <summary>
    /// Consume the armed flag when a local raid actually starts. Returns true only
    /// for a live (non-expired) flag whose map matches the starting raid.
    /// </summary>
    public bool TryConsumeArm(
        MongoId sessionId,
        string? location,
        string? nativeStartIdentity = null)
        => ProcessLocalRaidStart(sessionId, location, nativeStartIdentity)
            == ArenaLocalRaidStartDisposition.ArenaStarted;

    /// <summary>
    /// Makes the complete local-start claim under one session lock. Every
    /// request carrying Arena's marker fails closed unless it is the exact live
    /// arm. Untagged requests never recover, discard or mutate an Arena-owned
    /// snapshot implicitly; recovery is an explicit token-bound route.
    /// </summary>
    public ArenaLocalRaidStartDisposition ProcessLocalRaidStart(
        MongoId sessionId,
        string? location,
        string? nativeStartIdentity = null)
    {
        string key = sessionId.ToString();
        bool arenaTagged = Protocol.HasNativeStartMarker(nativeStartIdentity ?? string.Empty);
        lock (_lock)
        {
            if (!_sessions.TryGetValue(key, out var entry))
            {
                return arenaTagged
                    ? ArenaLocalRaidStartDisposition.RejectedArenaStart
                    : ArenaLocalRaidStartDisposition.Ordinary;
            }

            if (entry.State == ArenaSessionState.InRaid)
            {
                if (!string.IsNullOrWhiteSpace(entry.NativeStartIdentity)
                    && string.Equals(
                        entry.NativeStartIdentity,
                        nativeStartIdentity,
                        StringComparison.Ordinal))
                {
                    return ArenaLocalRaidStartDisposition.DuplicateArenaStart;
                }

                return arenaTagged
                    ? ArenaLocalRaidStartDisposition.RejectedArenaStart
                    : ArenaLocalRaidStartDisposition.ProtectedTransitionInProgress;
            }

            if (entry.State is ArenaSessionState.Preparing
                or ArenaSessionState.FinalizingEnd
                or ArenaSessionState.RecoveringStartFailure)
            {
                return ArenaLocalRaidStartDisposition.ProtectedTransitionInProgress;
            }

            if (entry.State != ArenaSessionState.Armed)
            {
                return arenaTagged
                    ? ArenaLocalRaidStartDisposition.RejectedArenaStart
                    : ArenaLocalRaidStartDisposition.Ordinary;
            }

            if (!arenaTagged)
                return ArenaLocalRaidStartDisposition.ProtectedTransitionInProgress;

            var age = DateTime.UtcNow - entry.ArmedAtUtc;
            if (age.TotalSeconds > ArenaContext.Config.ArmedFlagTtlSeconds)
            {
                logger.Warning(
                    $"[ArenaMode] Rejected expired Arena local-start after {age.TotalSeconds:0}s; " +
                    "the token-bound cancellation route still owns its snapshot.");
                return ArenaLocalRaidStartDisposition.RejectedArenaStart;
            }

            if (location == null || !string.Equals(entry.Map, location, StringComparison.OrdinalIgnoreCase))
            {
                logger.Warning(
                    $"[ArenaMode] Rejected Arena local-start for '{location}'; exact arm owns '{entry.Map}'.");
                return ArenaLocalRaidStartDisposition.RejectedArenaStart;
            }

            if (!Protocol.IsNativeStartIdentity(
                    nativeStartIdentity ?? string.Empty,
                    entry.ArmToken!))
            {
                logger.Error(
                    "[ArenaMode] Armed raid reached native local-start without its exact arm identity; start rejected.");
                return ArenaLocalRaidStartDisposition.RejectedArenaStart;
            }

            entry.State = ArenaSessionState.InRaid;
            entry.InRaidAtUtc = DateTime.UtcNow;
            entry.ClientRaidStarted = false;
            entry.NativeStartIdentity = nativeStartIdentity;
            entry.NativeStartTaskSettled = false;
            entry.NativeEndIdentity = null;
            entry.LastCompletedArmToken = null;
            entry.NativeEndCompletedAtUtc = default;
            if (entry.ProtectPitFireTeam)
            {
                _pitFireTeamGuard.BeginRaid(
                    key,
                    TimeSpan.FromMinutes(Math.Max(1, ArenaContext.Config.RaidTimeMinutes) + 15));
            }
            Patches.BotTierScalePatch.ResetReporting();

            // KitCatalog preloads while Armed, before this native request. Keep
            // that exact result, or draw here if no catalogue was requested yet.
            EnsureWeaponCatalogsLocked(entry);
            return ArenaLocalRaidStartDisposition.ArenaStarted;
        }
    }

    /// <summary>
    /// Binds the exact server id produced by SPT's successful native start to
    /// this arm before the response is serialized. The client receives and later
    /// returns this full value; an invented map/side prefix is never accepted.
    /// </summary>
    public bool BindNativeEndIdentity(
        MongoId sessionId,
        string nativeStartIdentity,
        string nativeServerId,
        out string? boundNativeEndIdentity)
    {
        boundNativeEndIdentity = null;
        if (string.IsNullOrWhiteSpace(nativeServerId)
            || Protocol.HasNativeEndMarker(nativeServerId))
        {
            return false;
        }

        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId.ToString(), out var entry)
                || entry.State != ArenaSessionState.InRaid
                || string.IsNullOrWhiteSpace(entry.ArmToken)
                || !string.Equals(
                    entry.NativeStartIdentity,
                    nativeStartIdentity,
                    StringComparison.Ordinal))
            {
                return false;
            }

            string exact = Protocol.BuildNativeEndIdentity(
                nativeServerId,
                entry.ArmToken);
            if (!string.IsNullOrWhiteSpace(entry.NativeEndIdentity)
                && !string.Equals(entry.NativeEndIdentity, exact, StringComparison.Ordinal))
            {
                return false;
            }

            entry.NativeEndIdentity = exact;
            entry.NativeStartTaskSettled = true;
            boundNativeEndIdentity = exact;
            return true;
        }
    }

    public void MarkNativeStartTaskSettled(
        MongoId sessionId,
        string nativeStartIdentity)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId.ToString(), out var entry)
                && entry.State == ArenaSessionState.InRaid
                && string.Equals(
                    entry.NativeStartIdentity,
                    nativeStartIdentity,
                    StringComparison.Ordinal))
            {
                entry.NativeStartTaskSettled = true;
            }
        }
    }

    public bool CanReplayNativeStartResponse(
        MongoId sessionId,
        string nativeStartIdentity)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionId.ToString(), out var entry)
                   && entry.State == ArenaSessionState.InRaid
                   && entry.NativeStartTaskSettled
                   && !string.IsNullOrWhiteSpace(entry.NativeEndIdentity)
                   && string.Equals(
                       entry.NativeStartIdentity,
                       nativeStartIdentity,
                       StringComparison.Ordinal);
        }
    }

    public bool OwnsNativeStartTransaction(
        MongoId sessionId,
        string nativeStartIdentity)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionId.ToString(), out var entry)
                   && entry.State == ArenaSessionState.InRaid
                   && string.Equals(
                       entry.NativeStartIdentity,
                       nativeStartIdentity,
                       StringComparison.Ordinal);
        }
    }

    public bool ConfirmClientRaidStarted(
        MongoId sessionId,
        string? armToken,
        string? nativeEndIdentity)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId.ToString(), out var entry)
                || entry.State != ArenaSessionState.InRaid
                || !entry.NativeStartTaskSettled
                || string.IsNullOrWhiteSpace(entry.NativeEndIdentity)
                || !string.Equals(
                    entry.NativeEndIdentity,
                    nativeEndIdentity,
                    StringComparison.Ordinal)
                || !ArmTokenMatches(entry, armToken))
            {
                return false;
            }

            if (entry.AreaId != ArenaAreaCatalog.FullMapId
                && !IsAreaLocationReady(entry, out _))
                return false;

            entry.ClientRaidStarted = true;
            return true;
        }
    }

    public bool IsClientRaidStarted(MongoId sessionId)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionId.ToString(), out var entry)
                   && entry.State == ArenaSessionState.InRaid
                   && entry.ClientRaidStarted;
        }
    }

    public bool TryBeginFailedStartRecovery(MongoId sessionId, string? armToken)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId.ToString(), out var entry)
                || entry.State != ArenaSessionState.InRaid
                || entry.ClientRaidStarted
                || !entry.NativeStartTaskSettled
                || !ArmTokenMatches(entry, armToken))
            {
                return false;
            }

            entry.State = ArenaSessionState.RecoveringStartFailure;
            return true;
        }
    }

    public bool CanRetryFailedStartRecovery(MongoId sessionId, string? armToken)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionId.ToString(), out var entry)
                   && entry.State == ArenaSessionState.RecoveringStartFailure
                   && !entry.ClientRaidStarted
                   && ArmTokenMatches(entry, armToken);
        }
    }

    public bool TryBeginArmedCancellation(MongoId sessionId, string? armToken)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId.ToString(), out var entry)
                || entry.State != ArenaSessionState.Armed
                || !ArmTokenMatches(entry, armToken))
            {
                return false;
            }

            // Keep the guard until the router has discarded the matching
            // snapshot, then MarkIdle clears both atomically visible pieces.
            entry.State = ArenaSessionState.RecoveringStartFailure;
            return true;
        }
    }

    public string? GetArmToken(MongoId sessionId)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionId.ToString(), out var entry)
                ? entry.ArmToken
                : null;
        }
    }

    public bool IsInRaid(MongoId sessionId)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionId.ToString(), out var entry)
                   && entry.State == ArenaSessionState.InRaid;
        }
    }

    /// <summary>The immutable area belongs to one non-idle arm, never a later ordinary raid.</summary>
    public string GetAreaId(MongoId sessionId)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionId.ToString(), out var entry)
                   && entry.State != ArenaSessionState.Idle
                   && !string.IsNullOrEmpty(entry.ArmToken)
                ? entry.AreaId : ArenaAreaCatalog.FullMapId;
        }
    }

    /// <summary>
    /// A bounded raid may leave native server start only after its exact clone
    /// was filtered. Full-map raids retain their existing location behavior.
    /// </summary>
    public bool ValidateAreaLocation(MongoId sessionId, out string? reason)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId.ToString(), out var entry)
                || entry.State != ArenaSessionState.InRaid)
            {
                reason = "the exact Arena raid is no longer active";
                return false;
            }
            return IsAreaLocationReady(entry, out reason);
        }
    }

    public bool ValidateAreaResponse(MongoId sessionId, object? responseLocation, out string? reason)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId.ToString(), out var entry)
                || entry.State != ArenaSessionState.InRaid)
            {
                reason = "the exact Arena raid is no longer active";
                return false;
            }
            if (entry.AreaId != ArenaAreaCatalog.FullMapId
                && !ReferenceEquals(entry.LocationClone, responseLocation))
            {
                reason = "the bounded Arena response did not retain its exact filtered location clone";
                return false;
            }
            return IsAreaLocationReady(entry, out reason);
        }
    }

    private static bool IsAreaLocationReady(SessionEntry entry, out string? reason)
    {
        reason = null;
        if (entry.AreaId == ArenaAreaCatalog.FullMapId)
            return true;
        if (!ArenaAreaCatalog.TryResolve(entry.AreaId, entry.Map!, entry.Mode!,
                entry.ProtectPitFireTeam, out _, out reason))
            return false;
        if (entry.LocationMutationReady
            && entry.LocationClone is SPTarkov.Server.Core.Models.Eft.Common.LocationBase location
            && string.IsNullOrEmpty(entry.LocationMutationFailure))
        {
            try
            {
                ArenaLocationMutator.ValidateAreaSpawns(location, entry.Mode, entry.AreaId);
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetBaseException().Message;
                return false;
            }
        }
        reason = entry.LocationMutationFailure ?? "the bounded Arena location filter has not completed";
        return false;
    }

    public bool IsProtectedTeamRaid(MongoId sessionId)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionId.ToString(), out var entry)
                   && entry.State == ArenaSessionState.InRaid
                   && entry.ProtectPitFireTeam;
        }
    }

    /// <summary>
    /// Atomically classify the retryable native local-end request. The exact
    /// arm token appended by the client is both the claim identity and the
    /// completed-request tombstone; a delayed end from another raid is never
    /// allowed to touch a protected snapshot.
    /// </summary>
    public ArenaLocalRaidEndDisposition ClassifyLocalRaidEnd(
        MongoId sessionId,
        string? nativeEndIdentity)
    {
        string key = sessionId.ToString();
        bool arenaTagged = Protocol.HasNativeEndMarker(nativeEndIdentity ?? string.Empty);
        lock (_lock)
        {
            if (IsCompletedNativeEndLocked(key, nativeEndIdentity))
                return ArenaLocalRaidEndDisposition.CompletedArenaEndRetry;

            if (!_sessions.TryGetValue(key, out var entry))
            {
                return arenaTagged
                    ? ArenaLocalRaidEndDisposition.RejectedProtectedEnd
                    : ArenaLocalRaidEndDisposition.Ordinary;
            }

            bool exactRecordedEnd = !string.IsNullOrWhiteSpace(nativeEndIdentity)
                && string.Equals(
                    entry.NativeEndIdentity,
                    nativeEndIdentity,
                    StringComparison.Ordinal);

            if (entry.State == ArenaSessionState.FinalizingEnd)
            {
                return exactRecordedEnd
                    ? ArenaLocalRaidEndDisposition.FinalizingArenaEndRetry
                    : ArenaLocalRaidEndDisposition.RejectedProtectedEnd;
            }

            if (entry.State == ArenaSessionState.Idle)
            {
                return arenaTagged
                    ? ArenaLocalRaidEndDisposition.RejectedProtectedEnd
                    : ArenaLocalRaidEndDisposition.Ordinary;
            }

            if (entry.State != ArenaSessionState.InRaid)
                return ArenaLocalRaidEndDisposition.RejectedProtectedEnd;

            return MatchesNativeEndIdentity(entry, nativeEndIdentity)
                ? ArenaLocalRaidEndDisposition.FirstArenaEnd
                : ArenaLocalRaidEndDisposition.RejectedProtectedEnd;
        }
    }

    public bool BeginNativeEndFinalization(
        MongoId sessionId,
        string? nativeEndIdentity)
    {
        string key = sessionId.ToString();
        lock (_lock)
        {
            if (!_sessions.TryGetValue(key, out var entry)
                || entry.State != ArenaSessionState.InRaid
                || !MatchesNativeEndIdentity(entry, nativeEndIdentity))
            {
                return false;
            }

            entry.State = ArenaSessionState.FinalizingEnd;
            entry.NativeEndIdentity = nativeEndIdentity;
            entry.NativeEndCompletedAtUtc = default;
            if (entry.ProtectPitFireTeam)
                _pitFireTeamGuard.BeginRaid(key, ActiveBoundaryRefreshTtl());
            return true;
        }
    }

    public bool CompleteNativeEndFinalization(
        MongoId sessionId,
        string? nativeEndIdentity)
    {
        string key = sessionId.ToString();
        lock (_lock)
        {
            if (!_sessions.TryGetValue(key, out var entry)
                || entry.State != ArenaSessionState.FinalizingEnd
                || string.IsNullOrWhiteSpace(nativeEndIdentity)
                || !string.Equals(
                    entry.NativeEndIdentity,
                    nativeEndIdentity,
                    StringComparison.Ordinal))
            {
                return false;
            }

            bool protectPitFireTeam = entry.ProtectPitFireTeam;
            string? completedArmToken = entry.ArmToken;
            entry.State = ArenaSessionState.Idle;
            entry.AreaId = ArenaAreaCatalog.FullMapId;
            entry.KitsJson = entry.LadderJson = entry.LadderPoolJson = null;
            entry.ProtectPitFireTeam = false;
            entry.ArmToken = null;
            entry.ClientRaidStarted = false;
            entry.NativeStartIdentity = null;
            entry.NativeStartTaskSettled = false;
            entry.LastCompletedArmToken = completedArmToken;
            entry.NativeEndCompletedAtUtc = DateTime.UtcNow;
            entry.LocationMutationReady = false;
            entry.LocationMutationFailure = null;
            entry.LocationClone = null;

            if (protectPitFireTeam)
                _pitFireTeamGuard.BeginPostRaidFinalization(key);
            else
                _pitFireTeamGuard.Clear(key);

            RememberCompletedNativeEndLocked(key, nativeEndIdentity);
            return true;
        }
    }

    public bool TryGetFinalizingNativeEnd(
        MongoId sessionId,
        string? armToken,
        out string? nativeEndIdentity)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId.ToString(), out var entry)
                && entry.State == ArenaSessionState.FinalizingEnd
                && ArmTokenMatches(entry, armToken)
                && !string.IsNullOrWhiteSpace(entry.NativeEndIdentity))
            {
                nativeEndIdentity = entry.NativeEndIdentity;
                return true;
            }
        }

        nativeEndIdentity = null;
        return false;
    }

    public bool TryGetCrashRecoveryNativeEnd(
        MongoId sessionId,
        string? armToken,
        out string? nativeEndIdentity)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId.ToString(), out var entry)
                && entry.State == ArenaSessionState.InRaid
                && entry.ClientRaidStarted
                && entry.NativeStartTaskSettled
                && ArmTokenMatches(entry, armToken)
                && !string.IsNullOrWhiteSpace(entry.NativeEndIdentity))
            {
                nativeEndIdentity = entry.NativeEndIdentity;
                return true;
            }
        }

        nativeEndIdentity = null;
        return false;
    }

    public bool IsCompletedNativeEndArm(MongoId sessionId, string? armToken)
    {
        if (string.IsNullOrWhiteSpace(armToken))
            return false;

        lock (_lock)
        {
            return _sessions.TryGetValue(sessionId.ToString(), out var entry)
                   && entry.State == ArenaSessionState.Idle
                   && string.Equals(
                       entry.LastCompletedArmToken,
                       armToken,
                       StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Records the server prerequisite for a protected 5v5. The client checks
    /// this after /client/match/local/start and aborts before play if the clone
    /// could not be made silent. Otherwise Arena's five requested opponents
    /// would be mixed with the map's vanilla waves.
    /// </summary>
    public void MarkLocationMutationReady(MongoId sessionId)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId.ToString(), out var entry)
                && entry.State == ArenaSessionState.InRaid)
            {
                entry.LocationMutationReady = true;
                entry.LocationMutationFailure = null;
            }
        }
    }

    public void MarkLocationMutationFailed(MongoId sessionId, string reason)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId.ToString(), out var entry)
                && entry.State == ArenaSessionState.InRaid)
            {
                entry.LocationMutationReady = false;
                entry.LocationMutationFailure = string.IsNullOrWhiteSpace(reason)
                    ? "location mutation failed"
                    : reason;
            }
        }
    }

    public (bool protectedTeam, bool locationReady, string? failure)
        GetProtectedTeamRaidValidation(MongoId sessionId)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId.ToString(), out var entry)
                || entry.State != ArenaSessionState.InRaid
                || !entry.ProtectPitFireTeam)
            {
                return (false, false, "the server has no protected PitFireTeam raid for this session");
            }

            return (
                true,
                entry.LocationMutationReady,
                entry.LocationMutationFailure ?? (entry.LocationMutationReady
                    ? null
                    : "the Arena location mutation has not completed"));
        }
    }

    public bool IsArmedOrInRaid(MongoId sessionId)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionId.ToString(), out var entry)
                   && entry.State != ArenaSessionState.Idle;
        }
    }

    public void MarkIdle(MongoId sessionId)
    {
        string key = sessionId.ToString();
        lock (_lock)
        {
            if (_sessions.TryGetValue(key, out var entry))
            {
                entry.State = ArenaSessionState.Idle;
                entry.AreaId = ArenaAreaCatalog.FullMapId;
                entry.KitsJson = entry.LadderJson = entry.LadderPoolJson = null;
                entry.ProtectPitFireTeam = false;
                entry.ArmToken = null;
                entry.ClientRaidStarted = false;
                entry.NativeStartIdentity = null;
                entry.NativeStartTaskSettled = false;
                entry.NativeEndIdentity = null;
                entry.LastCompletedArmToken = null;
                entry.NativeEndCompletedAtUtc = default;
                entry.LocationMutationReady = false;
                entry.LocationMutationFailure = null;
                entry.LocationClone = null;
            }

            _pitFireTeamGuard.Clear(key);
        }
    }

    /// <summary>
    /// The main profile is restored synchronously during local/end, but
    /// PitFireTeam can queue follower results from client teardown. Keep its
    /// separate store protected for a short, bounded period after Arena becomes
    /// idle so those late tasks cannot escape the snapshot boundary.
    /// </summary>
    public bool BeginPostRaidFinalization(MongoId sessionId)
    {
        string key = sessionId.ToString();
        bool protectPitFireTeam = false;
        lock (_lock)
        {
            if (_sessions.TryGetValue(key, out var entry))
            {
                protectPitFireTeam = entry.ProtectPitFireTeam;
                entry.State = ArenaSessionState.Idle;
                entry.AreaId = ArenaAreaCatalog.FullMapId;
                entry.KitsJson = entry.LadderJson = entry.LadderPoolJson = null;
                entry.ArmToken = null;
                entry.ClientRaidStarted = false;
                entry.NativeStartIdentity = null;
                entry.NativeStartTaskSettled = false;
                entry.NativeEndIdentity = null;
                entry.LastCompletedArmToken = null;
                entry.NativeEndCompletedAtUtc = default;
                entry.LocationMutationReady = false;
                entry.LocationMutationFailure = null;
                entry.LocationClone = null;
            }

            if (protectPitFireTeam)
                _pitFireTeamGuard.BeginPostRaidFinalization(key);
            else
                _pitFireTeamGuard.Clear(key);
        }

        return protectPitFireTeam;
    }

    public bool IsExternalProfileWriteProtected(MongoId sessionId)
    {
        string key = sessionId.ToString();
        lock (_lock)
        {
            if (_sessions.TryGetValue(key, out var entry)
                && entry.ProtectPitFireTeam
                && entry.State != ArenaSessionState.Idle)
            {
                // The timer is only a leak limiter for an abandoned guard object;
                // it must never reopen a still-owned main-profile snapshot. Any
                // access refreshes the bounded low-level window while the session
                // state proves that recovery has not closed the boundary.
                _pitFireTeamGuard.BeginRaid(key, ActiveBoundaryRefreshTtl());
                return true;
            }

            return _pitFireTeamGuard.IsProtected(key);
        }
    }

    public bool AnyExternalProfileWriteProtection()
    {
        lock (_lock)
        {
            RefreshOpenProtectedBoundariesLocked();
            return _pitFireTeamGuard.AnyProtected();
        }
    }

    public bool IsSoleExternalProfileProtection(MongoId sessionId)
    {
        lock (_lock)
        {
            RefreshOpenProtectedBoundariesLocked();
            return _pitFireTeamGuard.IsSoleProtectedSession(sessionId.ToString());
        }
    }

    public bool IsGlobalExternalMutationAuthorized()
    {
        lock (_lock)
        {
            RefreshOpenProtectedBoundariesLocked();
            return _pitFireTeamGuard.IsEveryProtectedSessionAuthorized(
                PitFireTeamManagementScope.Authorizes);
        }
    }

    public bool ShouldBlockPitFireTeamFileMutation(
        string? filePath,
        string? pitFireTeamModPath,
        out string? protectedSessionId)
    {
        lock (_lock)
        {
            RefreshOpenProtectedBoundariesLocked();
            return _pitFireTeamGuard.ShouldBlockFileMutation(
                filePath,
                pitFireTeamModPath,
                out protectedSessionId);
        }
    }

    /// <summary>
    /// Called as soon as an ordinary raid is identified. This is deliberately
    /// separate from MarkIdle: the session may already be Idle while a bounded
    /// post-raid window is still suppressing delayed PitFireTeam work.
    /// </summary>
    public void ClearExternalProfileProtection(MongoId sessionId)
    {
        lock (_lock)
            _pitFireTeamGuard.Clear(sessionId.ToString());
    }

    /// <summary>
    /// Atomically changes the PFT persistence policy from the previous Arena
    /// finalization window to an ordinary raid. The caller must hold
    /// ArenaRaidStartGate so the Arena state/snapshot checks remain stable.
    /// Existing PFT calls finish while protection is still visible; calls that
    /// enter after the write lease see the ordinary, unprotected boundary.
    /// </summary>
    internal void CutOverToOrdinaryRaid(
        MongoId sessionId,
        Func<bool> hasSnapshot)
    {
        ArgumentNullException.ThrowIfNull(hasSnapshot);
        using (PitFireTeamOperationGate.ReserveProfileBoundary())
        {
            if (IsArmedOrInRaid(sessionId) || hasSnapshot())
            {
                throw new InvalidOperationException(
                    "An Arena snapshot requires explicit token-bound recovery before ordinary local-start");
            }

            ClearExternalProfileProtection(sessionId);
        }
    }

    private static bool ArmTokenMatches(SessionEntry entry, string? armToken)
        => !string.IsNullOrWhiteSpace(armToken)
           && string.Equals(entry.ArmToken, armToken, StringComparison.Ordinal);

    private static bool MatchesNativeEndIdentity(
        SessionEntry entry,
        string? nativeEndIdentity)
    {
        return !string.IsNullOrWhiteSpace(entry.NativeEndIdentity)
               && !string.IsNullOrWhiteSpace(nativeEndIdentity)
               && string.Equals(
                   entry.NativeEndIdentity,
                   nativeEndIdentity,
                   StringComparison.Ordinal);
    }

    private bool IsCompletedNativeEndLocked(
        string sessionKey,
        string? nativeEndIdentity)
    {
        if (string.IsNullOrWhiteSpace(nativeEndIdentity)
            || !_completedNativeEnds.TryGetValue(sessionKey, out var tombstones))
        {
            return false;
        }

        PruneCompletedNativeEndsLocked(tombstones);
        return tombstones.ContainsKey(nativeEndIdentity);
    }

    private void RememberCompletedNativeEndLocked(
        string sessionKey,
        string nativeEndIdentity)
    {
        if (!_completedNativeEnds.TryGetValue(sessionKey, out var tombstones))
        {
            tombstones = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            _completedNativeEnds[sessionKey] = tombstones;
        }

        PruneCompletedNativeEndsLocked(tombstones);
        tombstones[nativeEndIdentity] = DateTime.UtcNow;

        // A client cannot legitimately have many concurrent local raids. Keep a
        // small bounded history anyway so a very delayed retry from the previous
        // match stays harmless after a new Arena arm replaces SessionEntry.
        foreach (string oldest in tombstones
                     .OrderBy(pair => pair.Value)
                     .Take(Math.Max(0, tombstones.Count - 8))
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            tombstones.Remove(oldest);
        }
    }

    private static void PruneCompletedNativeEndsLocked(
        Dictionary<string, DateTime> tombstones)
    {
        DateTime cutoff = DateTime.UtcNow - TimeSpan.FromHours(2);
        foreach (string expired in tombstones
                     .Where(pair => pair.Value < cutoff)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            tombstones.Remove(expired);
        }
    }

    private static TimeSpan ArmedProtectionTtl()
        => TimeSpan.FromSeconds(Math.Max(1, ArenaContext.Config.ArmedFlagTtlSeconds));

    private static TimeSpan PreparationProtectionTtl()
    {
        // Profile cloning is synchronous and normally takes milliseconds. Keep
        // a separately bounded 15-minute window so even an abnormally slow disk
        // cannot let raw PFT file work cross the snapshot; completion refreshes
        // the shorter user-configured armed TTL.
        TimeSpan armed = ArmedProtectionTtl();
        return armed > TimeSpan.FromMinutes(15)
            ? armed
            : TimeSpan.FromMinutes(15);
    }

    private static TimeSpan ActiveBoundaryRefreshTtl()
        => TimeSpan.FromMinutes(5);

    private void RefreshOpenProtectedBoundariesLocked()
    {
        foreach (var pair in _sessions)
        {
            if (pair.Value.ProtectPitFireTeam
                && pair.Value.State != ArenaSessionState.Idle)
            {
                _pitFireTeamGuard.BeginRaid(pair.Key, ActiveBoundaryRefreshTtl());
            }
        }
    }

    /// <summary>Remember the per-raid LocationBase clone so the hostility postfix can
    /// verify by reference that it is adjusting OUR arena clone, not a normal raid's.</summary>
    public void StashLocationClone(MongoId sessionId, object clone)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId.ToString(), out var entry))
                entry.LocationClone = clone;
        }
    }

    public bool IsStashedClone(object candidate)
    {
        lock (_lock)
        {
            foreach (var entry in _sessions.Values)
            {
                if (entry.State == ArenaSessionState.InRaid && ReferenceEquals(entry.LocationClone, candidate))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Whether an InRaid entry can still plausibly BE a raid.
    ///
    /// Nothing removes session entries and only the armed flag has a deadline,
    /// so a raid that ended by crash or Alt-F4 leaves its entry claiming to be
    /// in progress for the life of the server. The map-wide lookups below would
    /// then answer for a raid that finished hours ago — and since the tier ramp
    /// clamps at its maximum, a later NORMAL raid on the same map would face
    /// fully kitted bots. The same shape of deadline the armed flag already has,
    /// with slack for a raid that ran into overtime.
    /// </summary>
    private static bool IsLiveRaid(SessionEntry entry)
    {
        if (entry.State != ArenaSessionState.InRaid)
            return false;

        if (entry.InRaidAtUtc == default)
            return false;

        var limit = TimeSpan.FromMinutes(Math.Max(1, ArenaContext.Config.RaidTimeMinutes) + 15);
        return DateTime.UtcNow - entry.InRaidAtUtc <= limit;
    }

    /// <summary>
    /// Atomically bind bot-tier generation to one exact live Arena session and
    /// its map. The protected-team flag decides whether the caller preserves
    /// SPT's native PMC-relative values or applies Arena's solo progression.
    /// Requests from another session on the same map must not inherit either.
    /// </summary>
    public bool TryGetBotTierContext(
        MongoId sessionId,
        string? location,
        out TimeSpan elapsed,
        out bool protectedTeam)
    {
        elapsed = TimeSpan.Zero;
        protectedTeam = false;
        if (string.IsNullOrEmpty(location))
            return false;

        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId.ToString(), out var entry)
                || !IsLiveRaid(entry)
                || !string.Equals(entry.Map, location, StringComparison.OrdinalIgnoreCase))
                return false;

            elapsed = DateTime.UtcNow - entry.InRaidAtUtc;
            protectedTeam = entry.ProtectPitFireTeam;
            return true;
        }
    }

    /// <summary>
    /// Bind final bot equipment to an exact live Team DM raid. The arm token
    /// is captured before synchronous generation and rechecked afterwards so
    /// a delayed result cannot acquire a later raid's equipment policy.
    /// Protected PFT equipment is deliberately outside this contract.
    /// </summary>
    public bool TryGetTeamDeathmatchBotContext(MongoId sessionId, string? location, out string? identity)
    {
        identity = null;
        if (string.IsNullOrEmpty(location))
            return false;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId.ToString(), out var entry)
                || !IsLiveRaid(entry)
                || entry.ProtectPitFireTeam
                || !string.Equals(entry.Mode, "teamdeathmatch", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(entry.Map, location, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(entry.ArmToken))
                return false;
            identity = entry.ArmToken;
            return true;
        }
    }

    /// <summary>
    /// Resolve the mode and ownership together. A map id alone can match an
    /// ordinary raid or another player's session; only this exact live clone
    /// may receive a mode-specific hostility policy.
    /// </summary>
    public bool TryGetStashedCloneMode(object candidate, out string? mode)
    {
        mode = null;
        if (candidate == null)
            return false;

        lock (_lock)
        {
            foreach (var entry in _sessions.Values)
            {
                if (IsLiveRaid(entry) && ReferenceEquals(entry.LocationClone, candidate))
                {
                    mode = entry.Mode;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Whether any arena raid is armed or running. Coarser than the per-session
    /// checks on purpose: some client requests carry no session id — the bulk
    /// bot-difficulty fetch is one — and still need answering for this raid.
    /// </summary>
    public bool AnyArmedOrInRaid()
    {
        lock (_lock)
        {
            foreach (var entry in _sessions.Values)
            {
                if (IsLiveRaid(entry))
                    return true;

                if (entry.State == ArenaSessionState.Armed
                    && (DateTime.UtcNow - entry.ArmedAtUtc).TotalSeconds
                        <= ArenaContext.Config.ArmedFlagTtlSeconds)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public bool AnyInRaidOnMap(string? location)
    {
        if (location == null)
            return false;

        lock (_lock)
        {
            foreach (var entry in _sessions.Values)
            {
                // Same staleness guard: a crashed raid must not keep raising the
                // bot cap for every ordinary raid on this map afterwards.
                if (IsLiveRaid(entry)
                    && string.Equals(entry.Map, location, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public (ArenaSessionState state, string? map, string? mode) GetStatus(MongoId sessionId)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId.ToString(), out var entry))
                return (entry.State, entry.Map, entry.Mode);

            return (ArenaSessionState.Idle, null, null);
        }
    }

    public string GetKits(MongoId sessionId)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId.ToString(), out var entry) || !CanServeWeaponCatalog(entry))
                return ArenaContext.KitsJson;
            EnsureWeaponCatalogsLocked(entry);
            return entry.KitsJson!;
        }
    }

    public string GetLadder(MongoId sessionId, bool redraw = false)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId.ToString(), out var entry) || !CanServeWeaponCatalog(entry))
                return ArenaContext.LadderJson;
            EnsureWeaponCatalogsLocked(entry);
            if (redraw && entry.State == ArenaSessionState.InRaid)
                entry.LadderJson = LadderPicker.Redraw(entry.LadderPoolJson ?? ArenaContext.LadderJson,
                    entry.LadderRungs, message => logger.Info(message), ArenaContext.WeaponVariants);
            return entry.LadderJson ?? ArenaContext.LadderJson;
        }
    }

    private static bool CanServeWeaponCatalog(SessionEntry entry)
        => entry.State == ArenaSessionState.InRaid
           || (entry.State == ArenaSessionState.Armed
               && (DateTime.UtcNow - entry.ArmedAtUtc).TotalSeconds <= ArenaContext.Config.ArmedFlagTtlSeconds);

    private void EnsureWeaponCatalogsLocked(SessionEntry entry)
    {
        if (entry.KitsJson != null && entry.LadderJson != null) return;
        // Exact session ownership already checked by the caller. Freeze before
        // loading-screen preload and retain through Armed -> InRaid and reloads.
        entry.KitsJson = ArenaContext.KitsJson;
        entry.LadderPoolJson = ArenaContext.LadderJson;
        entry.LadderRungs = ArenaContext.Config.GunGameRungs;
        if (ArenaContext.WeaponVariants is { } variants)
        {
            try
            {
                entry.KitsJson = variants.CreateCatalog(entry.KitsJson, Random.Shared.Next(), out var report);
                logger.Info(ArenaWeaponVariants.Summarize("kit", report));
            }
            catch (Exception ex)
            { logger.Warning($"[ArenaMode] Kit attachment draw failed ({ex.Message}); authored builds retained."); }
        }
        // Protected 5v5 uses the PMC entry loadout, never catalog supplies.
        // Freeze requests before loading-screen preload, together with the
        // weapons; re-reading the catalog or advancing Gun Game does not refill.
        if (!entry.ProtectPitFireTeam && ArenaKitSupplies.Current is { } supplies)
        {
            try
            {
                entry.KitsJson = supplies.CreateCatalog(entry.KitsJson, out int added);
                logger.Info($"[ArenaMode] Kit supplies: {added} optional grenade requests; native rig/pocket space decides at kit build.");
            }
            catch (Exception ex)
            { logger.Warning($"[ArenaMode] Optional kit supplies unavailable ({ex.Message}); existing kit contents retained."); }
        }
        entry.LadderJson = LadderPicker.Redraw(entry.LadderPoolJson, entry.LadderRungs,
            message => logger.Info(message), ArenaContext.WeaponVariants);
    }
}
