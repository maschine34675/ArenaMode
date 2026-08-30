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
                new ValueTask<string>(ArenaContext.KitsJson)),

        new RouteAction<EmptyRequestData>(
            Protocol.LadderRoute,
            (url, info, sessionId, output, _) =>
                new ValueTask<string>(Services.LadderPicker.Current)),

        new RouteAction<EmptyRequestData>(
            Protocol.LadderRedrawRoute,
            (url, info, sessionId, output, _) =>
                new ValueTask<string>(Services.LadderPicker.Redraw(
                    ArenaContext.LadderJson,
                    ArenaContext.Config.GunGameRungs,
                    message => Instance?._logger.Info(message)))),

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

        new RouteAction<EmptyRequestData>(
            Protocol.RaidCancelRoute,
            (url, info, sessionId, output, _) =>
            {
                // Cancel only ever applies to an armed-but-not-started raid. A running
                // raid keeps its snapshot — the post-raid skip patch consumes it.
                var state = Sessions?.GetStatus(sessionId).state ?? ArenaSessionState.Idle;
                if (state == ArenaSessionState.Armed)
                {
                    Snapshots?.DiscardSnapshot(sessionId);
                    Sessions?.MarkIdle(sessionId);
                    return new ValueTask<string>(Json(new { disarmed = true }));
                }

                return new ValueTask<string>(Json(new { disarmed = false, state = state.ToString() }));
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

        // SVM (or any config tool) forcing the raid-menu AI amount to "NoBots" would
        // short-circuit spawn systems upstream of every cap we set. Warn loudly.
        if (string.Equals(_inRaidConfig.RaidMenuSettings?.AiAmount, "NoBots", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warning(
                "[ArenaMode] inraid config forces AiAmount=NoBots (SVM preset?) — the arena will have NO bots!");
        }

        if (!Snapshots.TakeSnapshot(sessionId))
            return Json(new { armed = false, reason = "profile snapshot failed" });

        Sessions.Arm(sessionId, request.Map!, request.Mode);
        return Json(new { armed = true, snapshotTaken = true });
    }

    private async ValueTask<string> HandleRaidEnd(MongoId sessionId, CancellationToken ct)
    {
        if (Sessions == null || Snapshots == null)
            return Json(new { restored = false });

        var state = Sessions.GetStatus(sessionId).state;

        // The client fires this at GameEnd, which is BEFORE its /client/match/local/end
        // request goes out. While the session is InRaid the snapshot and the state MUST
        // stay untouched: the authoritative restore runs inside the HandlePostRaidPmc
        // skip patch when local/end is processed. Consuming the state here would make
        // that patch think it is a normal raid and let full post-raid processing through
        // (the exact race the first playtest hit).
        if (state == ArenaSessionState.InRaid)
        {
            _logger.Info("[ArenaMode] raid-end received while raid result is still pending — restore deferred to post-raid processing.");
            return Json(new { restored = false, deferred = true });
        }

        // Armed-but-never-started leftovers: discard, nothing was mutated yet.
        if (state == ArenaSessionState.Armed)
        {
            Snapshots.DiscardSnapshot(sessionId);
            Sessions.MarkIdle(sessionId);
            return Json(new { restored = false, disarmed = true });
        }

        // Idle: the skip patch already restored (normal path) — or a stale snapshot
        // survived an unusual flow; restore it defensively.
        var restored = false;
        if (Snapshots.HasSnapshot(sessionId))
        {
            restored = Snapshots.RestoreSnapshot(sessionId);
            if (restored)
                await _saveServer.SaveProfileAsync(sessionId, ct);
        }

        return Json(new { restored });
    }

    private static string Json(object value) => JsonSerializer.Serialize(value);
}
