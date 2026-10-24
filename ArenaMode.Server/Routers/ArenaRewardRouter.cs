using System.Text.Json;
using ArenaMode.Server.Rewards;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace ArenaMode.Server.Routers;

public class ArenaRewardConsentRequest : RewardConsentRequest, IRequestData;
public class ArenaRewardBeginRequest : RewardBeginRequest, IRequestData;
public class ArenaRewardCaptureRequest : RewardCaptureRequest, IRequestData;

/// <summary>Accepts consent and combat observations; never payout or restoration authority.</summary>
[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public sealed class ArenaRewardRouter : StaticRouter
{
    public ArenaRewardRouter(JsonUtil json, SaveServer saves, ArenaSessionService sessions,
        ProfileSnapshotService snapshots, OrdinaryRaidLifecycleService ordinary)
        : base(json, Routes(saves, sessions, snapshots, ordinary)) { }

    private static List<RouteAction> Routes(SaveServer saves, ArenaSessionService sessions,
        ProfileSnapshotService snapshots, OrdinaryRaidLifecycleService ordinary) =>
    [
        new RouteAction<EmptyRequestData>(RewardWire.StatusRoute,
            (url, request, id, output, ct) => Status(id, saves, sessions, snapshots, ordinary)),
        new RouteAction<ArenaRewardConsentRequest>(RewardWire.ConsentRoute,
            async (url, request, id, output, ct) =>
            {
                using (await ArenaRaidStartGate.EnterAsync(id, ct))
                {
                    if (!CanChange(id, saves, sessions, snapshots, ordinary))
                        return Json(new RewardStatusResponse { State = "busy", Detail = "Return to the normal main menu before changing rewards." });
                    try { return Json(RewardRuntimeCoordinator.Current.SetConsent(id.ToString(), request.Enabled)); }
                    catch (Exception) { return Json(Unavailable()); }
                }
            }),
        new RouteAction<ArenaRewardBeginRequest>(RewardWire.BeginRoute,
            async (url, request, id, output, ct) =>
            {
                using (await ArenaRaidStartGate.EnterAsync(id, ct))
                {
                    if (!sessions.TryGetRewardSession(id, out string arm, out string nativeEnd,
                            out string mode, out bool team, out bool ffa) || !snapshots.HasSnapshot(id))
                        return Json(new RewardBeginResponse { Reason = "no-confirmed-arena-session" });
                    try
                    {
                        string? local = saves.GetProfile(id)?.CharacterData?.PmcData?.Id.ToString();
                        if (string.IsNullOrEmpty(local)) return Json(new RewardBeginResponse { Reason = "local-profile-unavailable" });
                        return Json(RewardRuntimeCoordinator.Current.Begin(id.ToString(), arm, nativeEnd,
                            local, mode, team, ffa, request));
                    }
                    catch (Exception) { return Json(new RewardBeginResponse { Reason = "reward-capture-unavailable" }); }
                }
            }),
        new RouteAction<ArenaRewardCaptureRequest>(RewardWire.CaptureRoute,
            async (url, request, id, output, ct) =>
            {
                using (await ArenaRaidStartGate.EnterAsync(id, ct))
                {
                    if (!sessions.TryGetRewardSession(id, out string arm, out string nativeEnd,
                            out _, out _, out _) || arm != request.ArmToken || nativeEnd != request.NativeEndIdentity)
                        return Json(new RewardCaptureResponse { Reason = "no-confirmed-arena-session" });
                    try { return Json(RewardRuntimeCoordinator.Current.Capture(id.ToString(), request)); }
                    catch (Exception) { return Json(new RewardCaptureResponse { Reason = "reward-capture-unavailable" }); }
                }
            }),
    ];

    private static ValueTask<string> Status(MongoId id, SaveServer saves, ArenaSessionService sessions,
        ProfileSnapshotService snapshots, OrdinaryRaidLifecycleService ordinary)
    {
        try
        {
            if (saves.GetProfile(id) is null) return new(Json(Unavailable()));
            var status = RewardRuntimeCoordinator.Current.GetStatus(id.ToString());
            status.CanChange &= CanChange(id, saves, sessions, snapshots, ordinary);
            return new(Json(status));
        }
        catch (Exception) { return new(Json(Unavailable())); }
    }

    private static bool CanChange(MongoId id, SaveServer saves, ArenaSessionService sessions,
        ProfileSnapshotService snapshots, OrdinaryRaidLifecycleService ordinary) =>
        saves.GetProfile(id) is not null && !saves.IsProfileInvalidOrUnloadable(id)
        && sessions.GetStatus(id).state == ArenaSessionState.Idle && !snapshots.HasSnapshot(id)
        && !ordinary.HasActiveTransaction(id);

    private static RewardStatusResponse Unavailable() => new()
    {
        State = "unavailable", Detail = "Rewards are unavailable; ordinary progress and Arena profile protection are unchanged."
    };
    private static string Json(object value) => JsonSerializer.Serialize(value);
}
