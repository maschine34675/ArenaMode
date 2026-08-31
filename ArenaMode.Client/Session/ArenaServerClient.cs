using ArenaMode.Shared;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
    private static bool _initialStateReconciled;
    private static bool _probeFailed;

    public static bool NeedsInitialReconciliation => !_initialStateReconciled;

    /// <summary>An announcement may have reached the server even if its reply was lost.</summary>
    public static bool HasPendingArm { get; private set; }

    public static bool RefreshAvailability()
    {
        _available = null;
        return IsServerModAvailable;
    }

    public static bool IsServerModAvailable
    {
        get
        {
            if (_available == null)
            {
                try
                {
                    _probeFailed = false;
                    var response = RequestHandler.GetJson(Protocol.PingRoute);
                    var version = JObject.Parse(response)["version"];
                    _available = version?.Type == JTokenType.String && !string.IsNullOrEmpty(version.Value<string>());
                    _probeFailed = !_available.Value;
                }
                catch (Exception ex)
                {
                    // SPT 4.1's installed Client.SendAsync throws this generic
                    // exception for a 404; GetJson wraps it in AggregateException.
                    // There is no status-code property on that exception. Only
                    // this exact confirmed absence is safe to treat as no mod.
                    _probeFailed = ex.GetBaseException().Message != "Http response status code: NotFound";
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
    /// Passive menu-time settings polling must not probe before the backend is
    /// ready. Explicit Arena selection and raid negotiation refresh the cached
    /// result and can therefore recover from a transient outage.
    /// </summary>
    public static bool IsServerModKnownAvailable => _available == true;

    /// <summary>
    /// A restarted client has forgotten its pending flag, but the server may
    /// still hold an arm. Reconcile once before ANY first local raid, including
    /// Scav. A missing server mod is fine; an unreachable server is uncertain.
    /// </summary>
    public static bool ReconcileInitialState()
    {
        if (_initialStateReconciled)
            return true;

        if (RefreshAvailability())
        {
            HasPendingArm = true;
            if (!PostRaidCancel())
                return false;
        }
        else if (_probeFailed)
        {
            return false;
        }

        _initialStateReconciled = true;
        return true;
    }

    public static bool PostRaidStart(string map, string mode)
    {
        HasPendingArm = true;
        try
        {
            var body = JsonConvert.SerializeObject(new { map, mode });
            var response = RequestHandler.PostJson(Protocol.RaidStartRoute, body);
            var ok = !string.IsNullOrEmpty(response) && JObject.Parse(response)["armed"]?.Value<bool>() == true;
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

    public static bool PostRaidCancel()
    {
        try
        {
            var response = JObject.Parse(RequestHandler.PostJson(Protocol.RaidCancelRoute, "{}"));
            // Idempotent retries return Idle rather than disarmed=true. InRaid
            // must keep its snapshot; StartLocalRaidPatch restores that stale
            // running raid before allowing the next ordinary raid to start.
            var state = response["state"]?.Value<string>();
            var confirmed = response["disarmed"]?.Value<bool>() == true
                || state == "Idle" || state == "InRaid";
            if (confirmed)
                HasPendingArm = false;
            return confirmed;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena raid-cancel failed: {ex.Message}");
            return false;
        }
    }

    public static void ConfirmRaidStarted() => HasPendingArm = false;

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
