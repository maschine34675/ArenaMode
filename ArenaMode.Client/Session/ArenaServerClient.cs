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

    /// <summary>
    /// Whether the server mod is ALREADY known to be there — never probes.
    ///
    /// The difference matters. IsServerModAvailable latches its answer on the
    /// first call, so anything that asks it early decides for the whole session.
    /// The menu-time settings poll must not be that thing: a game started before
    /// the server would latch "no" and leave arena mode switched off until the
    /// game is restarted, when waiting until raid start would have found it up.
    /// </summary>
    public static bool IsServerModKnownAvailable => _available == true;

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

    /// <summary>What the config page wants this client's settings to be.</summary>
    public static ClientSettingsSchema GetClientSettings()
    {
        try
        {
            var json = RequestHandler.GetJson(Protocol.ClientSettingsRoute);
            return string.IsNullOrEmpty(json)
                ? null
                : JsonConvert.DeserializeObject<ClientSettingsSchema>(json);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena client-settings fetch failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Tells the server what this client actually has, for the page to show.</summary>
    public static void PostClientSettings(ClientSettingsSchema schema)
    {
        try
        {
            RequestHandler.PostJson(Protocol.ClientSettingsPushRoute, JsonConvert.SerializeObject(schema));
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena client-settings upload failed: {ex.Message}");
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
