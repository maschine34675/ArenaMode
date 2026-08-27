using System.Text.Json;
using System.Text.Json.Serialization;
using ArenaMode.Server.Services;
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
}

[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public sealed class ArenaModeRouter : StaticRouter
{
    private static ArenaSessionService? Sessions => ArenaContext.Sessions;
    private static ProfileSnapshotService? Snapshots => ArenaContext.Snapshots;

    private readonly SaveServer _saveServer;
    private readonly ISptLogger<ArenaModeRouter> _logger;

    public ArenaModeRouter(JsonUtil jsonUtil, SaveServer saveServer, ISptLogger<ArenaModeRouter> logger)
        : base(jsonUtil, GetRoutes())
    {
        _saveServer = saveServer;
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

        new RouteAction<ArenaRaidStartRequest>(
            Protocol.RaidStartRoute,
            async (url, info, sessionId, output, ct) =>
                Instance != null ? await Instance.HandleRaidStart(info, sessionId, ct) : Json(new { armed = false })),

        new RouteAction<EmptyRequestData>(
            Protocol.RaidCancelRoute,
            (url, info, sessionId, output, _) =>
            {
                Snapshots?.DiscardSnapshot(sessionId);
                Sessions?.MarkIdle(sessionId);
                return new ValueTask<string>(Json(new { disarmed = true }));
            }),

        new RouteAction<EmptyRequestData>(
            Protocol.RaidEndRoute,
            async (url, info, sessionId, output, ct) =>
                Instance != null ? await Instance.HandleRaidEnd(sessionId, ct) : Json(new { restored = false })),

        new RouteAction<EmptyRequestData>(
            Protocol.StatusRoute,
            (url, info, sessionId, output, _) =>
            {
                var (state, map, mode) = Sessions?.GetStatus(sessionId) ?? (ArenaSessionState.Idle, null, null);
                return new ValueTask<string>(Json(new { state = state.ToString(), map, mode }));
            }),
    ];

    private async ValueTask<string> HandleRaidStart(ArenaRaidStartRequest request, MongoId sessionId, CancellationToken ct)
    {
        if (Sessions == null || Snapshots == null)
            return Json(new { armed = false, reason = "mod not initialized" });

        // A leftover snapshot means the previous arena raid never reached its
        // post-raid restore (client crash mid-raid). The disk profile is clean
        // (post-raid processing was skipped), but the in-memory profile may carry
        // raid-start mutations — restore before doing anything else.
        if (Snapshots.HasSnapshot(sessionId))
        {
            _logger.Warning("[ArenaMode] Stale snapshot from a previous arena raid found — restoring first.");
            if (Snapshots.RestoreSnapshot(sessionId))
                await _saveServer.SaveProfileAsync(sessionId, ct);
            Sessions.MarkIdle(sessionId);
        }

        if (!ArenaContext.Config.IsMapEnabled(request.Map))
            return Json(new { armed = false, reason = $"map '{request.Map}' not enabled for arena" });

        if (!Snapshots.TakeSnapshot(sessionId))
            return Json(new { armed = false, reason = "profile snapshot failed" });

        Sessions.Arm(sessionId, request.Map!, request.Mode);
        return Json(new { armed = true, snapshotTaken = true });
    }

    private async ValueTask<string> HandleRaidEnd(MongoId sessionId, CancellationToken ct)
    {
        if (Sessions == null || Snapshots == null)
            return Json(new { restored = false });

        // Normal path: PostRaidPmcSkipPatch already restored the snapshot and the
        // profile was saved by EndLocalRaidAsync — this is a no-op confirmation.
        // Fallback path (e.g. the raid ended without PMC post-raid processing):
        // restore and persist here.
        var restored = false;
        if (Snapshots.HasSnapshot(sessionId))
        {
            restored = Snapshots.RestoreSnapshot(sessionId);
            if (restored)
                await _saveServer.SaveProfileAsync(sessionId, ct);
        }

        Sessions.MarkIdle(sessionId);
        return Json(new { restored });
    }

    private static string Json(object value) => JsonSerializer.Serialize(value);
}
