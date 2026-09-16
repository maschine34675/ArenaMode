using System.Text.Json;
using System.Text.Json.Serialization;
using ArenaMode.Server.Services;
using ArenaMode.Server.Patches;
using ArenaMode.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace ArenaMode.Server.Routers;

public record ArenaRaidStartRequest : IRequestData
{
    [JsonPropertyName("map")]
    public string? Map { get; set; }

    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("pitFireTeamTeam")]
    public bool PitFireTeamTeam { get; set; }

    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }

    [JsonPropertyName("areaId")]
    public string? AreaId { get; set; }

    [JsonPropertyName("areaTeamSize")]
    public int? AreaTeamSize { get; set; }

    [JsonPropertyName("deathmatchHostility")]
    public string? DeathmatchHostility { get; set; }
}

public record ArenaRaidLifecycleRequest : IRequestData
{
    [JsonPropertyName("armToken")]
    public string? ArmToken { get; set; }

    [JsonPropertyName("startFailed")]
    public bool StartFailed { get; set; }

    [JsonPropertyName("recoverWithoutNativeEnd")]
    public bool RecoverWithoutNativeEnd { get; set; }

    [JsonPropertyName("nativeEndIdentity")]
    public string? NativeEndIdentity { get; set; }
}

public record OrdinaryRaidLifecycleRequest : IRequestData
{
    [JsonPropertyName("processId")]
    public string? ProcessId { get; set; }

    [JsonPropertyName("startIdentity")]
    public string? StartIdentity { get; set; }

    [JsonPropertyName("nativeEndIdentity")]
    public string? NativeEndIdentity { get; set; }
}

/// <summary>
/// The client's settings as it currently has them, for the config page to show.
/// A class, not a record: the shared schema type is a plain class, and a record
/// may only inherit from object or another record.
/// </summary>
public class ArenaClientSettingsRequest : ArenaMode.Shared.ClientSettingsSchema, IRequestData
{
}

[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public sealed class ArenaModeRouter : StaticRouter
{
    private static ArenaSessionService? Sessions => ArenaContext.Sessions;
    private static ProfileSnapshotService? Snapshots => ArenaContext.Snapshots;

    private readonly SaveServer _saveServer;
    private readonly ISptLogger<ArenaModeRouter> _logger;
    private readonly SPTarkov.Server.Core.Models.Spt.Config.InRaidConfig _inRaidConfig;

    public ArenaModeRouter(
        JsonUtil jsonUtil,
        SaveServer saveServer,
        SPTarkov.Server.Core.Models.Spt.Config.InRaidConfig inRaidConfig,
        ISptLogger<ArenaModeRouter> logger)
        : base(jsonUtil, GetRoutes())
    {
        _saveServer = saveServer;
        _inRaidConfig = inRaidConfig;
        _logger = logger;
        Instance = this;
    }

    // The route lambdas are static-built before the instance exists; resolved via
    // the singleton instance (routers are registered as singletons by the DI layer).
    private static ArenaModeRouter? Instance;

    private static List<RouteAction> GetRoutes() =>
    [
        new RouteAction<EmptyRequestData>(
            Protocol.PingRoute,
            (url, info, sessionId, output, _) =>
                new ValueTask<string>($"{{\"version\":\"{typeof(ArenaModeRouter).Assembly.GetName().Version}\"}}")),

        new RouteAction<EmptyRequestData>(
            Protocol.ConfigRoute,
            (url, info, sessionId, output, _) =>
                new ValueTask<string>(JsonSerializer.Serialize(ArenaContext.Config))),

        new RouteAction<EmptyRequestData>(
            Protocol.KitsRoute,
            (url, info, sessionId, output, _) =>
                new ValueTask<string>(Sessions?.GetKits(sessionId) ?? ArenaContext.KitsJson)),

        new RouteAction<EmptyRequestData>(
            Protocol.LadderRoute,
            (url, info, sessionId, output, _) =>
                new ValueTask<string>(Sessions?.GetLadder(sessionId) ?? ArenaContext.LadderJson)),

        new RouteAction<EmptyRequestData>(
            Protocol.LadderRedrawRoute,
            (url, info, sessionId, output, _) =>
                new ValueTask<string>(Sessions?.GetLadder(sessionId, redraw: true) ?? ArenaContext.LadderJson)),

        new RouteAction<EmptyRequestData>(
            Protocol.ClientSettingsRoute,
            (url, info, sessionId, output, _) =>
                new ValueTask<string>(Services.ClientSettingsStore.Serialize())),

        new RouteAction<ArenaClientSettingsRequest>(
            Protocol.ClientSettingsPushRoute,
            (url, info, sessionId, output, _) =>
            {
                Services.ClientSettingsStore.AcceptFromClient(info);
                return new ValueTask<string>(Json(new { accepted = true }));
            }),

        new RouteAction<ArenaRaidStartRequest>(
            Protocol.RaidStartRoute,
            async (url, info, sessionId, output, ct) =>
                Instance != null ? await Instance.HandleRaidStart(info, sessionId, ct) : Json(new { armed = false })),

        new RouteAction<ArenaRaidLifecycleRequest>(
            Protocol.RaidCancelRoute,
            async (url, info, sessionId, output, ct) =>
                Instance != null
                    ? await Instance.HandleRaidCancel(info, sessionId, ct)
                    : Json(new { disarmed = false, state = ArenaSessionState.Idle.ToString() })),

        new RouteAction<ArenaRaidLifecycleRequest>(
            Protocol.RaidConfirmRoute,
            (url, info, sessionId, output, _) =>
            {
                bool confirmed = Sessions?.ConfirmClientRaidStarted(
                    sessionId,
                    info.ArmToken,
                    info.NativeEndIdentity) == true;
                return new ValueTask<string>(Json(new
                {
                    confirmed,
                    state = Sessions?.GetStatus(sessionId).state.ToString()
                        ?? ArenaSessionState.Idle.ToString(),
                }));
            }),

        new RouteAction<ArenaRaidLifecycleRequest>(
            Protocol.RaidEndRoute,
            async (url, info, sessionId, output, ct) =>
                Instance != null
                    ? await Instance.HandleRaidEnd(info, sessionId, ct)
                    : Json(new { restored = false })),

        new RouteAction<OrdinaryRaidLifecycleRequest>(
            Protocol.OrdinaryConfirmRoute,
            async (url, info, sessionId, output, ct) =>
                Instance != null
                    ? await Instance.HandleOrdinaryConfirm(info, sessionId, ct)
                    : Json(new { confirmed = false })),

        new RouteAction<OrdinaryRaidLifecycleRequest>(
            Protocol.OrdinaryRecoveryRoute,
            async (url, info, sessionId, output, ct) =>
                Instance != null
                    ? await Instance.HandleOrdinaryRecovery(info, sessionId, ct)
                    : Json(new { recovered = false })),

        new RouteAction<EmptyRequestData>(
            Protocol.StatusRoute,
            async (url, info, sessionId, output, ct) =>
                Instance != null
                    ? await Instance.HandleStatus(sessionId, ct)
                    : Json(new { state = ArenaSessionState.Idle.ToString() })),
    ];

    private async ValueTask<string> HandleRaidStart(ArenaRaidStartRequest request, MongoId sessionId, CancellationToken ct)
    {
        if (ArenaContext.OrdinaryRaids?.HasActiveTransaction(sessionId) == true)
        {
            return Json(new
            {
                armed = false,
                state = "OrdinaryRaidActive",
                reason = "An ordinary local raid already owns this session",
            });
        }

        using (await ArenaRaidStartGate.EnterAsync(sessionId, ct))
        {
            if (ArenaContext.OrdinaryRaids?.HasActiveTransaction(sessionId) == true)
            {
                return Json(new
                {
                    armed = false,
                    state = "OrdinaryRaidActive",
                    reason = "An ordinary local raid already owns this session",
                });
            }

            return await HandleRaidStartLocked(request, sessionId, ct);
        }
    }

    private async ValueTask<string> HandleStatus(
        MongoId sessionId,
        CancellationToken ct)
    {
        using (await ArenaRaidStartGate.EnterAsync(sessionId, ct))
        {
            var (state, map, mode) = Sessions?.GetStatus(sessionId)
                ?? (ArenaSessionState.Idle, null, null);
            var (protectedTeam, locationReady, locationFailure) =
                Sessions?.GetProtectedTeamRaidValidation(sessionId)
                ?? (false, false, null);
            var ordinary = ArenaContext.OrdinaryRaids?.GetRecoveryStatus(sessionId)
                ?? (false, null, null, null, null, false);
            return Json(new
            {
                state = state.ToString(),
                map,
                mode,
                areaId = Sessions?.GetAreaId(sessionId) ?? ArenaAreaCatalog.FullMapId,
                areaTeamSize = Sessions?.GetAreaTeamSize(sessionId) ?? 0,
                deathmatchHostility = Sessions?.GetDeathmatchHostility(sessionId),
                armToken = Sessions?.GetArmToken(sessionId),
                clientRaidStarted = Sessions?.IsClientRaidStarted(sessionId) == true,
                pitFireTeamTeam = protectedTeam,
                locationReady,
                locationFailure,
                ordinaryRaidState = ordinary.Item2?.ToString(),
                ordinaryStartIdentity = ordinary.Item3,
                ordinaryClientProcessId = ordinary.Item4,
                ordinaryNativeEndIdentity = ordinary.Item5,
                ordinaryClientRaidStarted = ordinary.Item6,
            });
        }
    }

    private async ValueTask<string> HandleOrdinaryConfirm(
        OrdinaryRaidLifecycleRequest request,
        MongoId sessionId,
        CancellationToken ct)
    {
        using (await ArenaRaidStartGate.EnterAsync(sessionId, ct))
        {
            bool confirmed = ArenaContext.OrdinaryRaids?.ConfirmClientRaidStarted(
                sessionId,
                request.StartIdentity,
                request.ProcessId,
                request.NativeEndIdentity) == true;
            return Json(new { confirmed });
        }
    }

    private async ValueTask<string> HandleOrdinaryRecovery(
        OrdinaryRaidLifecycleRequest request,
        MongoId sessionId,
        CancellationToken ct)
    {
        using (await ArenaRaidStartGate.EnterAsync(sessionId, ct))
        {
            OrdinaryRaidLifecycleService? ordinary = ArenaContext.OrdinaryRaids;
            string? reason = ordinary == null
                ? "ordinary lifecycle service is unavailable"
                : null;
            if (ordinary == null || !ordinary.TryBeginAbandonedStartRecovery(
                    sessionId,
                    request.StartIdentity,
                    request.ProcessId,
                    out reason))
            {
                return Json(new { recovered = false, reason });
            }

            try
            {
                if (ArenaContext.ProtectedRaidEnd == null)
                    throw new InvalidOperationException(
                        "ordinary lifecycle cleanup service is unavailable");
                ArenaContext.ProtectedRaidEnd.ResetNonProfileLifecycle();
            }
            catch (Exception ex)
            {
                _logger.Error(
                    $"[ArenaMode] Ordinary abandoned-start lifecycle cleanup remains retryable: {ex}");
                return Json(new
                {
                    recovered = false,
                    reason = "ordinary start cleanup failed and remains retryable",
                });
            }

            bool recovered = ordinary.CompleteAbandonedStartRecovery(
                sessionId,
                request.StartIdentity);
            return Json(new
            {
                recovered,
                state = recovered ? "Idle" : "Ending",
            });
        }
    }

    private async ValueTask<string> HandleRaidCancel(
        ArenaRaidLifecycleRequest request,
        MongoId sessionId,
        CancellationToken ct)
    {
        if (Sessions == null || Snapshots == null)
            return Json(new { disarmed = false, state = ArenaSessionState.Idle.ToString() });

        using (await ArenaRaidStartGate.EnterAsync(sessionId, ct))
        {
            ArenaSessionState state = Sessions.GetStatus(sessionId).state;
            if (state == ArenaSessionState.Idle)
                return Json(new { disarmed = false, state = state.ToString() });

            if (state == ArenaSessionState.Armed)
            {
                if (!Sessions.TryBeginArmedCancellation(sessionId, request.ArmToken))
                {
                    return Json(new
                    {
                        disarmed = false,
                        state = state.ToString(),
                        reason = "Arena arm token did not match",
                    });
                }

                Snapshots.DiscardSnapshot(sessionId);
                Sessions.MarkIdle(sessionId);
                return Json(new { disarmed = true, state = ArenaSessionState.Idle.ToString() });
            }

            if ((state == ArenaSessionState.InRaid
                    || state == ArenaSessionState.RecoveringStartFailure)
                && request.StartFailed)
            {
                if (ArenaContext.ProtectedRaidEnd == null)
                {
                    return Json(new
                    {
                        disarmed = false,
                        state = state.ToString(),
                        reason = "Arena failed-start lifecycle cleanup is unavailable",
                    });
                }

                bool recoveryAccepted = state == ArenaSessionState.InRaid
                    ? Sessions.TryBeginFailedStartRecovery(sessionId, request.ArmToken)
                    : Sessions.CanRetryFailedStartRecovery(sessionId, request.ArmToken);
                if (!recoveryAccepted)
                {
                    return Json(new
                    {
                        disarmed = false,
                        state = state.ToString(),
                        reason = "Arena raid was already confirmed or its arm token did not match",
                    });
                }

                try
                {
                    ArenaContext.ProtectedRaidEnd.ResetNonProfileLifecycle();
                }
                catch (Exception ex)
                {
                    _logger.Error(
                        $"[ArenaMode] Failed-start lifecycle cleanup failed before snapshot restore: {ex}");
                    return Json(new
                    {
                        disarmed = false,
                        state = ArenaSessionState.RecoveringStartFailure.ToString(),
                        reason = "Arena start failed and its server lifecycle cleanup remains retryable",
                    });
                }

                if (!Snapshots.RestoreSnapshot(sessionId))
                {
                    return Json(new
                    {
                        disarmed = false,
                        state = ArenaSessionState.RecoveringStartFailure.ToString(),
                        reason = "Arena start failed but its profile snapshot could not be restored",
                    });
                }

                try
                {
                    // Recovery is committed once the exact task has settled and
                    // the snapshot swap succeeds. Do not let a lost HTTP reply
                    // cancel persistence and strand RecoveringStartFailure.
                    await ArenaContext.ProtectedRaidEnd
                        .PersistRestoredProfileAfterFailedStartAsync(sessionId);
                }
                catch (Exception ex)
                {
                    // The in-memory profile and the still-clean pre-raid disk
                    // profile are both safe; report the persistence warning and
                    // still enter the bounded PFT finalization window below.
                    _logger.Error($"[ArenaMode] Failed to persist restored profile after local-start failure: {ex}");
                }

                // The server raid-start already published InRaid, so PFT follower
                // generation or teardown work may have escaped even though the
                // client never confirmed play. Preserve the same bounded delayed
                // side-effect protection used by a normal protected raid end.
                Sessions.BeginPostRaidFinalization(sessionId);
                return Json(new
                {
                    disarmed = true,
                    restored = true,
                    state = ArenaSessionState.Idle.ToString(),
                });
            }

            return Json(new
            {
                disarmed = false,
                state = state.ToString(),
                reason = state == ArenaSessionState.InRaid
                    ? "Arena raid is running; only an unconfirmed failed start can be cancelled"
                    : "Arena profile transition is still in progress",
            });
        }
    }

    private async ValueTask<string> HandleRaidStartLocked(
        ArenaRaidStartRequest request,
        MongoId sessionId,
        CancellationToken ct)
    {
        if (Sessions == null || Snapshots == null)
            return Json(new { armed = false, reason = "mod not initialized" });

        if (!Protocol.IsLifecycleRequestId(request.RequestId ?? string.Empty))
        {
            return Json(new
            {
                armed = false,
                state = ArenaSessionState.Idle.ToString(),
                reason = "Arena raid-start request identity was missing or malformed",
            });
        }

        // Reject contradictory rules before even stale-snapshot recovery. A
        // request/retry must identify the same advertised bot relationships.
        if (!DeathmatchSelection.TryResolve(request.Mode, request.DeathmatchHostility,
                out var deathmatchHostility, out var hostilityFailure))
            return Json(new { armed = false, reason = hostilityFailure });

        // Resolve before snapshot recovery/preparation, and before replay: an
        // unknown or mismatched area must never silently become a full-map raid.
        string areaId = ArenaAreaCatalog.NormalizeId(request.AreaId!);
        if (!ArenaAreaCatalog.TryResolve(areaId, request.Map!, request.Mode!,
                request.PitFireTeamTeam, out var area, out string? areaFailure)
            || !ArenaAreaCatalog.TryResolveTeamSize(area, request.AreaTeamSize, out int areaTeamSize, out areaFailure))
            return Json(new { armed = false, reason = areaFailure });

        ArenaSessionState existingState = Sessions.GetStatus(sessionId).state;
        if (existingState != ArenaSessionState.Idle)
        {
            // SPT's installed POST client retries the same serialized body after
            // a transport failure. If the first response was lost after commit,
            // replay the original token so the client can consume or cancel the
            // exact snapshot it already owns. A different request remains a
            // hard failure and never replaces the active arm.
            if (existingState == ArenaSessionState.Armed
                && Snapshots.HasSnapshot(sessionId)
                && Sessions.TryReplayArmedRequest(
                    sessionId,
                    request.Map,
                    request.Mode,
                    request.PitFireTeamTeam,
                    request.RequestId,
                    out string? replayedArmToken,
                    areaId,
                    areaTeamSize,
                    deathmatchHostility))
            {
                _logger.Info(
                    $"[ArenaMode] Replayed identical committed raid-start for session {sessionId}.");
                return Json(new
                {
                    armed = true,
                    snapshotTaken = true,
                    armToken = replayedArmToken,
                    areaId = Sessions.GetAreaId(sessionId),
                    areaTeamSize = Sessions.GetAreaTeamSize(sessionId),
                    deathmatchHostility = Sessions.GetDeathmatchHostility(sessionId),
                    replayed = true,
                });
            }

            // Never interpret a replayed /raid-start as crash recovery. A live
            // Arena necessarily owns a snapshot; restoring it here would roll
            // back the player mid-raid and drop the PFT guard. A restarted
            // client uses the explicit exact-token raid-end recovery route.
            return Json(new
            {
                armed = false,
                state = existingState.ToString(),
                reason = $"Arena session is already {existingState}",
            });
        }


        if (Sessions.IsExternalProfileWriteProtected(sessionId))
        {
            return Json(new
            {
                armed = false,
                state = existingState.ToString(),
                reason = "PitFireTeam post-raid finalization is still settling; retry shortly",
            });
        }

        // A leftover snapshot means the previous arena raid never reached its
        // post-raid restore (client crash mid-raid). The disk profile is clean
        // (post-raid processing was skipped), but the in-memory profile may carry
        // raid-start mutations — restore before doing anything else.
        if (Snapshots.HasSnapshot(sessionId))
        {
            _logger.Warning("[ArenaMode] Stale snapshot from a previous arena raid found — restoring first.");
            if (!Snapshots.RestoreSnapshot(sessionId))
            {
                return Json(new
                {
                    armed = false,
                    state = ArenaSessionState.Idle.ToString(),
                    reason = "stale Arena profile snapshot could not be restored; retry retained",
                });
            }

            await _saveServer.SaveProfileAsync(sessionId, ct);
            Sessions.MarkIdle(sessionId);
        }

        string? pitFireTeamRejection = PitFireTeamRaidStartPolicy.GetRejectionReason(
            request.PitFireTeamTeam,
            request.Mode,
            PitFireTeamDynamicPersistencePatches.ProtectionReady,
            PitFireTeamDynamicPersistencePatches.IncompatibilityReason);
        if (pitFireTeamRejection != null)
        {
            _logger.Warning(
                $"[ArenaMode] Rejected PitFireTeam team Arena before snapshot/arm: {pitFireTeamRejection}");
            return Json(new { armed = false, reason = pitFireTeamRejection });
        }

        if (!ArenaContext.Config.IsMapEnabled(request.Map))
        {
            return Json(new { armed = false, reason = $"map '{request.Map}' not enabled for arena" });
        }

        // SVM (or any config tool) forcing the raid-menu AI amount to "NoBots" would
        // short-circuit spawn systems upstream of every cap we set. Warn loudly.
        if (string.Equals(_inRaidConfig.RaidMenuSettings?.AiAmount, "NoBots", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warning(
                "[ArenaMode] inraid config forces AiAmount=NoBots (SVM preset?) — the arena will have NO bots!");
        }

        string armToken = request.RequestId!;
        if (!Sessions.BeginArmPreparation(
                sessionId,
                request.Map!,
                request.Mode,
                request.PitFireTeamTeam,
                armToken,
                areaId,
                areaTeamSize,
                deathmatchHostility))
        {
            return Json(new
            {
                armed = false,
                reason = "another Arena arm is already being prepared",
            });
        }

        bool snapshotTaken;
        try
        {
            snapshotTaken = Snapshots.TakeSnapshot(sessionId);
        }
        catch (Exception ex)
        {
            Sessions.CancelArmPreparation(sessionId);
            Snapshots.DiscardSnapshot(sessionId);
            _logger.Error($"[ArenaMode] Profile snapshot threw while arming Arena: {ex}");
            return Json(new { armed = false, reason = "profile snapshot failed" });
        }

        if (!snapshotTaken)
        {
            Sessions.CancelArmPreparation(sessionId);
            Snapshots.DiscardSnapshot(sessionId);
            return Json(new { armed = false, reason = "profile snapshot failed" });
        }

        if (!Sessions.CompleteArmPreparation(sessionId))
        {
            Snapshots.DiscardSnapshot(sessionId);
            Sessions.CancelArmPreparation(sessionId);
            return Json(new { armed = false, reason = "protected Arena arm was cancelled during snapshot" });
        }

        return Json(new { armed = true, snapshotTaken = true, armToken, areaId, areaTeamSize, deathmatchHostility });
    }

    private async ValueTask<string> HandleRaidEnd(
        ArenaRaidLifecycleRequest request,
        MongoId sessionId,
        CancellationToken ct)
    {
        if (Sessions == null || Snapshots == null)
            return Json(new { restored = false });

        using (await ArenaRaidStartGate.EnterAsync(sessionId, ct))
        {
            ArenaSessionState state = Sessions.GetStatus(sessionId).state;

            if (state == ArenaSessionState.Idle
                && Sessions.IsCompletedNativeEndArm(sessionId, request.ArmToken))
            {
                return Json(new
                {
                    restored = true,
                    completed = true,
                    state = ArenaSessionState.Idle.ToString(),
                });
            }

            if (state == ArenaSessionState.FinalizingEnd)
            {
                if (!Sessions.TryGetFinalizingNativeEnd(
                        sessionId,
                        request.ArmToken,
                        out string? finalizingIdentity)
                    || ArenaContext.ProtectedRaidEnd == null)
                {
                    return Json(new
                    {
                        restored = false,
                        completed = false,
                        state = state.ToString(),
                        reason = "Arena end token did not match the finalizing transaction",
                    });
                }

                try
                {
                    await ArenaContext.ProtectedRaidEnd
                        .GetOrStart(sessionId, finalizingIdentity!)
                        .WaitAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // The shared persistence task continues independently. Let
                    // the HTTP cancellation propagate; the retained client token
                    // can acknowledge it on the next negotiation.
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error(
                        $"[ArenaMode] Token-bound raid-end recovery remains retryable: {ex}");
                    return Json(new
                    {
                        restored = false,
                        completed = false,
                        state = ArenaSessionState.FinalizingEnd.ToString(),
                        reason = ex.GetBaseException().Message,
                    });
                }

                return Json(new
                {
                    restored = true,
                    completed = Sessions.IsCompletedNativeEndArm(
                        sessionId,
                        request.ArmToken),
                    state = Sessions.GetStatus(sessionId).state.ToString(),
                });
            }

            // GameEnd precedes /client/match/local/end. Never consume InRaid here;
            // the post-raid prefix owns the authoritative restore. A new client
            // process can explicitly recover a crashed, already-confirmed raid
            // with the exact arm token and the server-bound native end identity.
            if (state == ArenaSessionState.InRaid)
            {
                if (request.RecoverWithoutNativeEnd
                    && ArenaContext.ProtectedRaidEnd == null)
                {
                    return Json(new
                    {
                        restored = false,
                        completed = false,
                        state = state.ToString(),
                        reason = "Arena protected-end service is unavailable",
                    });
                }

                if (request.RecoverWithoutNativeEnd
                    && Sessions.TryGetCrashRecoveryNativeEnd(
                        sessionId,
                        request.ArmToken,
                        out string? crashRecoveryIdentity))
                {
                    if (!ArenaNativeEndAdmission.TryClaimAndRestore(
                            Sessions,
                            Snapshots,
                            sessionId,
                            crashRecoveryIdentity))
                    {
                        return Json(new
                        {
                            restored = false,
                            completed = false,
                            state = Sessions.GetStatus(sessionId).state.ToString(),
                            reason = "crashed Arena end could not be claimed and restored",
                        });
                    }

                    try
                    {
                        await ArenaContext.ProtectedRaidEnd!
                            .GetOrStart(sessionId, crashRecoveryIdentity!)
                            .WaitAsync(ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(
                            $"[ArenaMode] Crashed Arena recovery remains retryable: {ex}");
                        return Json(new
                        {
                            restored = false,
                            completed = false,
                            state = ArenaSessionState.FinalizingEnd.ToString(),
                            reason = ex.GetBaseException().Message,
                        });
                    }

                    return Json(new
                    {
                        restored = true,
                        completed = true,
                        state = ArenaSessionState.Idle.ToString(),
                    });
                }

                _logger.Info(
                    "[ArenaMode] raid-end received while raid result is still pending — restore deferred to post-raid processing.");
                return Json(new { restored = false, deferred = true, state = state.ToString() });
            }

            // Only the exact current arm may disarm an Arena that never reached
            // StartLocalRaid. A delayed raid-end from an older run cannot touch a
            // new snapshot. Preparing/recovery and Idle are observation-only;
            // stale Idle snapshots are handled by the next start transaction.
            if (state == ArenaSessionState.Armed
                && Sessions.TryBeginArmedCancellation(sessionId, request.ArmToken))
            {
                Snapshots.DiscardSnapshot(sessionId);
                Sessions.MarkIdle(sessionId);
                return Json(new
                {
                    restored = false,
                    disarmed = true,
                    state = ArenaSessionState.Idle.ToString(),
                });
            }

            return Json(new { restored = false, state = state.ToString() });
        }
    }

    private static string Json(object value) => JsonSerializer.Serialize(value);
}
