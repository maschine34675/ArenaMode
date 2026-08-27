using ArenaMode.Shared;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;

namespace ArenaMode.Session;

/// <summary>
/// Client side of the ArenaMode server routes. All calls are synchronous and
/// fail closed: without the server mod, arena never activates (the profile
/// protection lives server-side).
/// </summary>
public static class ArenaServerClient
{
    private static bool? _available;

    public static bool IsServerModAvailable
    {
        get
        {
            if (_available == null)
            {
                try
                {
                    var response = RequestHandler.GetJson(Protocol.PingRoute);
                    _available = !string.IsNullOrEmpty(response) && response.Contains("version");
                }
                catch (Exception ex)
                {
                    Plugin.ModLogger.LogError(
                        $"ArenaMode server mod not reachable ({ex.Message}) — arena stays disabled.");
                    _available = false;
                }
            }

            return _available.Value;
        }
    }

    public static bool PostRaidStart(string map, string mode)
    {
        try
        {
            var body = JsonConvert.SerializeObject(new { map, mode });
            var response = RequestHandler.PostJson(Protocol.RaidStartRoute, body);
            var ok = response != null && response.Contains("\"armed\":true");
            if (!ok)
                Plugin.ModLogger.LogWarning($"Arena raid-start rejected by server: {response}");
            return ok;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError($"Arena raid-start failed: {ex.Message}");
            return false;
        }
    }

    public static void PostRaidCancel()
    {
        try
        {
            RequestHandler.PostJson(Protocol.RaidCancelRoute, "{}");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena raid-cancel failed: {ex.Message}");
        }
    }

    public static void PostRaidEnd()
    {
        try
        {
            RequestHandler.PostJson(Protocol.RaidEndRoute, "{}");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena raid-end failed: {ex.Message}");
        }
    }
}
