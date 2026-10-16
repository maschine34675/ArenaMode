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

    /// <summary>Refresh at an explicit menu choice / raid start; hot-reloaded server settings must win.</summary>
    public static bool Refresh()
    {
        _config = null;
        _nextAttemptAt = 0f;
        EnsureLoaded();
        return _config != null;
    }

    /// <summary>
    /// Whether the SERVER has this map enabled for the arena.
    ///
    /// The keycard waiver asks this before it opens a locked map. Without it the
    /// waiver was gated on the client's map list alone, so a one-sided config
    /// drift — the map named on the client, disabled or missing on the server —
    /// would have unlocked the Lab and then dropped the player into a fully
    /// vanilla raid there, with their real kit and their real profile at stake.
    ///
    /// Unknown means NO: failing closed costs a keycard the player was never
    /// going to have, and failing open costs a stash.
    /// </summary>
    public static bool IsMapEnabledOnServer(string map)
    {
        EnsureLoaded();

        try
        {
            var token = _config?["maps"]?[map?.ToLowerInvariant() ?? string.Empty];
            return token != null && (token["enabled"]?.Value<bool>() ?? true);
        }
        catch
        {
            return false;
        }
    }

    public static int GetBotCap(string map, int fallback = 24)
        => GetMapValue<int?>(map, "botMax") ?? fallback;

    /// <summary>How often to tell bot groups where the player is, in seconds.</summary>
    public static int GetHuntSweepSeconds(string map, int fallback = 15)
        => GetMapValue<int?>(map, "huntSweepSeconds") ?? fallback;

    /// <summary>How near a group must be to be told. 0 means every group, at any range.</summary>
    public static int GetHuntRadius(string map, int fallback = 60)
        => GetMapValue<int?>(map, "huntRadius") ?? fallback;

    public static int GetBurstMinAliveBots(string map, int fallback = 12)
        => GetMapValue<int?>(map, "burstMinAliveBots") ?? fallback;

    public static int GetBurstCooldownSeconds(string map, int fallback = 8)
        => GetMapValue<int?>(map, "burstCooldownSeconds") ?? fallback;

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
            Plugin.ModLogger.LogDebug("Arena server config loaded.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena server config load failed ({ex.Message}) — using defaults for now.");
        }
    }
}
