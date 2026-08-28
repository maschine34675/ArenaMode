using ArenaMode.Shared;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using System;

namespace ArenaMode.Session;

/// <summary>
/// Client-side view of the server's arena config (single source of truth:
/// user/mods/ArenaMode/config/config.json). Fetched during the raid loading
/// screen; transient failures fall back to safe defaults and retry next raid.
/// </summary>
public static class ArenaClientConfig
{
    private static JObject _config;
    private static float _nextAttemptAt;

    public static void Preload()
    {
        _nextAttemptAt = 0f;
        EnsureLoaded();
    }

    public static int GetBotCap(string map, int fallback = 24)
        => GetMapValue<int?>(map, "botMax") ?? fallback;

    public static int GetBurstMinAliveBots(string map, int fallback = 12)
        => GetMapValue<int?>(map, "burstMinAliveBots") ?? fallback;

    public static int GetBurstCooldownSeconds(string map, int fallback = 8)
        => GetMapValue<int?>(map, "burstCooldownSeconds") ?? fallback;

    public static string GetSpawnZones(string map, string fallback = "BotZone")
        => GetMapValue<string>(map, "spawnZones") ?? fallback;

    private static T GetMapValue<T>(string map, string key)
    {
        EnsureLoaded();
        try
        {
            var token = _config?["maps"]?[map?.ToLowerInvariant() ?? string.Empty]?[key];
            return token != null ? token.Value<T>() : default;
        }
        catch
        {
            return default;
        }
    }

    private static void EnsureLoaded()
    {
        // Backoff: callers include per-frame gameplay code (burst ticks) — a failing
        // synchronous HTTP request must not be retried every access.
        if (_config != null || UnityEngine.Time.realtimeSinceStartup < _nextAttemptAt)
            return;

        _nextAttemptAt = UnityEngine.Time.realtimeSinceStartup + 30f;

        try
        {
            _config = JObject.Parse(RequestHandler.GetJson(Protocol.ConfigRoute));
            Plugin.ModLogger.LogInfo("Arena server config loaded.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena server config load failed ({ex.Message}) — using defaults for now.");
        }
    }
}
