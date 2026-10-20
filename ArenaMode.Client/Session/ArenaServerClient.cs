using ArenaMode.Shared;
using EFT;
using JsonType;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using System;
using System.Text;
using System.Threading.Tasks;

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
    private static string _serverAdmissionFailure;
    internal static string ServerAdmissionFailure => _available == true ? _serverAdmissionFailure : null;
    private static readonly string ClientProcessId = Guid.NewGuid().ToString("N");
    private static string _pendingArmToken;
    private static string _activeArmToken;
    private static string _molotovArmToken;

    // The server validates its exact registered item, not just a matching ID.
    // Binding to the confirmed token prevents a previous raid authorizing supply.
    internal static bool MolotovAvailableForConfirmedRaid =>
        !string.IsNullOrEmpty(_activeArmToken) && _molotovArmToken == _activeArmToken;
    private static string _boundNativeEndIdentity;
    private static string _ordinaryStartIdentity;
    private static string _ordinaryNativeEndIdentity;
    private static bool _ordinaryRaidConfirmed;

    public static bool NeedsInitialReconciliation => !_initialStateReconciled;

    /// <summary>An announcement may have reached the server even if its reply was lost.</summary>
    public static bool HasPendingArm { get; private set; }

    /// <summary>
    /// The exact arm token stays live until /raid-end acknowledges completed
    /// snapshot persistence. A deferred or lost response is retried before the
    /// next local raid negotiation.
    /// </summary>
    public static bool HasPendingEnd { get; private set; }
    public static bool HasOrdinaryLifecycle =>
        !string.IsNullOrWhiteSpace(_ordinaryStartIdentity);

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
                    var ping = JObject.Parse(response);
                    var version = ping["version"];
                    var failure = ping["admissionFailure"];
                    _serverAdmissionFailure = failure?.Type == JTokenType.String ? failure.Value<string>() : null;
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
            try
            {
                var status = JObject.Parse(RequestHandler.GetJson(Protocol.StatusRoute));
                var state = status["state"]?.Value<string>();
                var clientRaidStarted = status["clientRaidStarted"]?.Value<bool>() == true;
                _pendingArmToken = status["armToken"]?.Value<string>();

                string ordinaryState = status["ordinaryRaidState"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(ordinaryState))
                {
                    string ordinaryStartIdentity =
                        status["ordinaryStartIdentity"]?.Value<string>();
                    bool ordinaryClientRaidStarted =
                        status["ordinaryClientRaidStarted"]?.Value<bool>() == true;
                    string ordinaryOwnerProcessId =
                        status["ordinaryClientProcessId"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(ordinaryStartIdentity)
                        || (ordinaryClientRaidStarted
                            && string.Equals(
                                ordinaryOwnerProcessId,
                                ClientProcessId,
                                StringComparison.Ordinal)))
                    {
                        Plugin.ModLogger.LogWarning(
                            "The current client process still owns a confirmed ordinary raid; refusing to replace its native end transaction.");
                        return false;
                    }

                    _ordinaryStartIdentity = ordinaryStartIdentity;
                    _ordinaryNativeEndIdentity =
                        status["ordinaryNativeEndIdentity"]?.Value<string>();
                    _ordinaryRaidConfirmed = false;
                    if (!PostOrdinaryStartRecovery())
                    {
                        Plugin.ModLogger.LogWarning(
                            "The server still owns an ordinary local-start that could not be recovered; initial recovery will retry.");
                        return false;
                    }
                }

                if (state == "Armed")
                {
                    HasPendingArm = true;
                    if (!PostRaidCancel())
                        return false;
                }
                else if (state == "RecoveringStartFailure")
                {
                    HasPendingArm = true;
                    if (!PostRaidCancel(startFailed: true))
                    {
                        Plugin.ModLogger.LogWarning(
                            "Arena server profile recovery is still incomplete; initial reconciliation will retry.");
                        return false;
                    }
                }
                else if (state == "InRaid" && !clientRaidStarted)
                {
                    // A restarted client cannot still be loading the same native
                    // request. The exact unconfirmed token can therefore recover
                    // immediately instead of blocking a new Arena negotiation.
                    HasPendingArm = true;
                    if (!PostRaidCancel(startFailed: true))
                    {
                        Plugin.ModLogger.LogWarning(
                            "Arena server still owns an unconfirmed local start; initial recovery will retry.");
                        return false;
                    }
                }
                else if (state == "FinalizingEnd")
                {
                    _activeArmToken = _pendingArmToken;
                    _pendingArmToken = null;
                    _boundNativeEndIdentity = null;
                    HasPendingArm = false;
                    HasPendingEnd = true;
                    if (!PostRaidEnd())
                    {
                        Plugin.ModLogger.LogWarning(
                            "Arena server end finalization is still incomplete; initial reconciliation will retry.");
                        return false;
                    }
                }
                else if (state == "InRaid" && clientRaidStarted)
                {
                    // This is a new client process, so it cannot still own the
                    // confirmed native raid. Recover the exact server-bound end
                    // identity through the explicit token route.
                    _activeArmToken = _pendingArmToken;
                    _pendingArmToken = null;
                    _boundNativeEndIdentity = null;
                    HasPendingArm = false;
                    HasPendingEnd = true;
                    if (!PostRaidEnd(recoverWithoutNativeEnd: true))
                    {
                        Plugin.ModLogger.LogWarning(
                            "Arena server still owns a crashed confirmed raid; initial recovery will retry.");
                        return false;
                    }
                }
                else if (state == "Preparing")
                {
                    Plugin.ModLogger.LogWarning(
                        $"Arena server profile transition is still '{state}'; initial reconciliation will retry.");
                    return false;
                }
                else
                {
                    HasPendingArm = false;
                    HasPendingEnd = false;
                    _pendingArmToken = null;
                    _activeArmToken = null;
                    _boundNativeEndIdentity = null;
                }
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning($"Arena initial state reconciliation failed: {ex.Message}");
                return false;
            }
        }
        else if (_probeFailed)
        {
            return false;
        }

        _initialStateReconciled = true;
        return true;
    }

    internal static string PendingArmToken => _pendingArmToken;
    internal static string ActiveArmToken => _activeArmToken;
    internal static bool PendingParticipantBosses { get; private set; }

    public static bool PostRaidStart(string map, string mode, bool pitFireTeamTeam = false,
        string areaId = ArenaAreaCatalog.FullMapId, int? areaTeamSize = null, string deathmatchHostility = null,
        int? participants = null)
    {
        PendingParticipantBosses = false;
        if (!DeathmatchSelection.TryResolve(mode, deathmatchHostility, out var frozenHostility, out _)
            || !ArenaAreaCatalog.TryResolve(areaId, map, mode, pitFireTeamTeam, out var area, out _)
            || !ArenaAreaCatalog.TryResolveTeamSize(area, mode, pitFireTeamTeam, areaTeamSize, out int selectedSize, out _)
            || !ArenaParticipantPolicy.TryResolve(map, mode, area != null, pitFireTeamTeam,
                participants, out _, out _))
            return false;
        HasPendingArm = true;
        HasPendingEnd = false;
        // The client must know the transaction identity before the first byte is
        // sent. A response can be lost while the server is still cloning the
        // profile; a server-generated token would leave no exact cancellation
        // claim during that interval.
        _pendingArmToken = Guid.NewGuid().ToString("N");
        _activeArmToken = null;
        _boundNativeEndIdentity = null;
        try
        {
            var payload = JObject.FromObject(new
            {
                map,
                mode,
                pitFireTeamTeam,
                areaId,
                requestId = _pendingArmToken,
            });
            if (selectedSize != 0) payload["areaTeamSize"] = selectedSize;
            if (frozenHostility != null) payload["deathmatchHostility"] = frozenHostility;
            if (participants.HasValue) payload["participants"] = participants.Value;
            var body = payload.ToString(Formatting.None);
            var response = RequestHandler.PostJson(Protocol.RaidStartRoute, body);
            var parsed = string.IsNullOrEmpty(response) ? null : JObject.Parse(response);
            string returnedArmToken = parsed?["armToken"]?.Value<string>();
            var ok = parsed?["armed"]?.Value<bool>() == true
                && (participants.HasValue
                    ? parsed?["participants"]?.Type == JTokenType.Integer
                        && parsed["participants"].Value<int>() == participants.Value
                        && parsed["participantBosses"]?.Type == JTokenType.Boolean
                        && (!parsed["participantBosses"].Value<bool>()
                            || ArenaParticipantPolicy.IsSolo(mode)
                            && string.Equals(map, ArenaMapCatalog.LabyrinthId, StringComparison.OrdinalIgnoreCase))
                    : parsed?["participants"] == null || parsed["participants"].Type == JTokenType.Null)
                && (frozenHostility == null || parsed?["deathmatchHostility"]?.Type == JTokenType.String
                    && string.Equals(parsed["deathmatchHostility"].Value<string>(), frozenHostility, StringComparison.Ordinal))
                && (selectedSize == 0
                    ? parsed?["areaTeamSize"] == null || parsed["areaTeamSize"].Type == JTokenType.Null
                        || parsed["areaTeamSize"].Type == JTokenType.Integer && parsed["areaTeamSize"].Value<int>() == 0
                    : parsed?["areaTeamSize"]?.Type == JTokenType.Integer && parsed["areaTeamSize"].Value<int>() == selectedSize)
                && (areaId == ArenaAreaCatalog.FullMapId
                    || string.Equals(parsed?["areaId"]?.Value<string>(), areaId, StringComparison.Ordinal))
                && string.Equals(
                    returnedArmToken,
                    _pendingArmToken,
                    StringComparison.Ordinal);
            if (!ok)
            {
                // A false/malformed/lost response is not proof that the server
                // stayed Idle: SPT may have retried after the first response was
                // lost, or the server may already own Armed + snapshot. Retain
                // uncertainty until /status proves Idle or a token-bound cancel
                // completes. Exact committed retries normally return the same
                // token as success from the server.
                Plugin.ModLogger.LogWarning($"Arena raid-start rejected by server: {response}");
            }
            if (ok && participants.HasValue)
                PendingParticipantBosses = parsed["participantBosses"].Value<bool>();
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
            // This is the one periodic route in ArenaMode. RequestHandler's
            // convenience wrapper writes two Info lines for every successful
            // poll; its underlying client keeps the same backend host, session
            // cookie, retries, certificate handling and zlib response handling
            // without those wrapper messages. Task.Run mirrors GetJson's sync
            // bridge so an async continuation cannot wait on Unity's context.
            byte[] payload = Task.Run(
                () => RequestHandler.HttpClient.GetAsync(Protocol.ClientSettingsRoute)).Result;
            var json = Encoding.UTF8.GetString(payload);
            if (string.IsNullOrWhiteSpace(json))
            {
                // RequestHandler.ValidateJson used to report this case. Retain
                // failure visibility while suppressing only successful polls.
                Plugin.ModLogger.LogWarning("Arena client-settings fetch returned an empty response.");
                return null;
            }

            return JsonConvert.DeserializeObject<ClientSettingsSchema>(json);
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

    public static bool PostRaidCancel(bool startFailed = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_pendingArmToken))
            {
                var status = JObject.Parse(RequestHandler.GetJson(Protocol.StatusRoute));
                var currentState = status["state"]?.Value<string>();
                _pendingArmToken = status["armToken"]?.Value<string>();
                if (currentState == "Idle")
                {
                    HasPendingArm = false;
                    HasPendingEnd = false;
                    _activeArmToken = null;
                    _boundNativeEndIdentity = null;
                    return true;
                }
            }

            var body = JsonConvert.SerializeObject(new
            {
                armToken = _pendingArmToken,
                startFailed,
            });
            var response = JObject.Parse(RequestHandler.PostJson(Protocol.RaidCancelRoute, body));
            // Idempotent retries return Idle. InRaid is not a cancellation
            // acknowledgement: only the matching unconfirmed start-failure
            // token may restore and disarm it.
            var state = response["state"]?.Value<string>();
            var confirmed = response["disarmed"]?.Value<bool>() == true
                || state == "Idle";
            if (confirmed)
            {
                HasPendingArm = false;
                HasPendingEnd = false;
                _pendingArmToken = null;
                _activeArmToken = null;
                _boundNativeEndIdentity = null;
            }
            return confirmed;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena raid-cancel failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Bind EFT's retryable native start request to the exact Arena arm. SPT
    /// otherwise receives a null serverId on every local start, so it cannot
    /// distinguish a lost-response retry from a later crash-recovery start.
    /// </summary>
    public static bool BindNativeStartIdentity(LocalRaidSettings settings)
    {
        if (settings == null || string.IsNullOrWhiteSpace(_pendingArmToken))
            return false;

        settings.serverId = Protocol.BuildNativeStartIdentity(_pendingArmToken);
        return true;
    }

    /// <summary>
    /// Give every ordinary local-start invocation a client-owned generation id.
    /// EFT reuses the same settings object for its transport retries, while a
    /// genuinely new raid receives a different id. The server can therefore
    /// share one complete vanilla start task without guessing from map/side.
    /// </summary>
    public static bool BindOrdinaryNativeStartIdentity(LocalRaidSettings settings)
    {
        if (settings == null)
            return false;

        // Client-only installs must remain byte-identical to stock SPT. Initial
        // reconciliation distinguishes a confirmed 404 from an uncertain
        // outage; only the known-present server may receive our idempotency tag.
        if (!IsServerModKnownAvailable)
            return true;

        if (Protocol.TryParseNativeOrdinaryStartIdentity(
                settings.serverId,
                out string processId,
                out _)
            && string.Equals(processId, ClientProcessId, StringComparison.Ordinal))
        {
            _ordinaryStartIdentity = settings.serverId;
            _ordinaryNativeEndIdentity = null;
            _ordinaryRaidConfirmed = false;
            return true;
        }

        // A transition/reconnect or another ordinary integration may already
        // own this field. Preserve it byte-for-byte and let the server's legacy
        // ordinary path fail open; Arena uncertainty is handled separately.
        if (!string.IsNullOrWhiteSpace(settings.serverId))
            return true;

        settings.serverId = Protocol.BuildNativeOrdinaryStartIdentity(
            ClientProcessId,
            Guid.NewGuid().ToString("N"));
        _ordinaryStartIdentity = settings.serverId;
        _ordinaryNativeEndIdentity = null;
        _ordinaryRaidConfirmed = false;
        return true;
    }

    public static bool BindOrdinaryNativeEndIdentity(LocalSettings response)
    {
        if (response == null
            || string.IsNullOrWhiteSpace(response.serverId)
            || string.IsNullOrWhiteSpace(_ordinaryStartIdentity)
            || Protocol.HasNativeStartMarker(response.serverId)
            || Protocol.HasNativeEndMarker(response.serverId))
        {
            return false;
        }

        _ordinaryNativeEndIdentity = response.serverId;
        return true;
    }

    public static bool ConfirmOrdinaryRaidStarted()
    {
        if (string.IsNullOrWhiteSpace(_ordinaryStartIdentity)
            || string.IsNullOrWhiteSpace(_ordinaryNativeEndIdentity))
        {
            return true;
        }

        try
        {
            var body = JsonConvert.SerializeObject(new
            {
                processId = ClientProcessId,
                startIdentity = _ordinaryStartIdentity,
                nativeEndIdentity = _ordinaryNativeEndIdentity,
            });
            var response = JObject.Parse(
                RequestHandler.PostJson(Protocol.OrdinaryConfirmRoute, body));
            _ordinaryRaidConfirmed = response["confirmed"]?.Value<bool>() == true;
            return _ordinaryRaidConfirmed;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                $"Ordinary raid-start confirmation failed: {ex.Message}");
            return false;
        }
    }

    public static bool ReconcileOrdinaryLifecycleForNewRaid()
    {
        if (!HasOrdinaryLifecycle)
            return true;

        if (!IsServerModKnownAvailable)
        {
            ClearOrdinaryLifecycle();
            return true;
        }

        try
        {
            var status = JObject.Parse(RequestHandler.GetJson(Protocol.StatusRoute));
            string state = status["ordinaryRaidState"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(state))
            {
                ClearOrdinaryLifecycle();
                return true;
            }

            bool confirmed = status["ordinaryClientRaidStarted"]?.Value<bool>() == true;
            string serverIdentity = status["ordinaryStartIdentity"]?.Value<string>();
            if (!string.Equals(
                    serverIdentity,
                    _ordinaryStartIdentity,
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (confirmed || _ordinaryRaidConfirmed)
                return false;

            return PostOrdinaryStartRecovery();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                $"Ordinary lifecycle reconciliation failed: {ex.Message}");
            return false;
        }
    }

    public static bool PostOrdinaryStartRecovery()
    {
        if (string.IsNullOrWhiteSpace(_ordinaryStartIdentity))
            return true;

        try
        {
            var body = JsonConvert.SerializeObject(new
            {
                processId = ClientProcessId,
                startIdentity = _ordinaryStartIdentity,
                nativeEndIdentity = _ordinaryNativeEndIdentity,
            });
            var response = JObject.Parse(
                RequestHandler.PostJson(Protocol.OrdinaryRecoveryRoute, body));
            if (response["recovered"]?.Value<bool>() != true)
                return false;

            ClearOrdinaryLifecycle();
            return true;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                $"Ordinary abandoned-start recovery failed: {ex.Message}");
            return false;
        }
    }

    private static void ClearOrdinaryLifecycle()
    {
        _ordinaryStartIdentity = null;
        _ordinaryNativeEndIdentity = null;
        _ordinaryRaidConfirmed = false;
    }

    /// <summary>
    /// Validate that the server atomically bound SPT's actual returned serverId
    /// to this arm before serialization. The server already appended the token;
    /// mutating it client-side would create an identity the server never saw.
    /// </summary>
    public static bool BindNativeEndIdentity(LocalSettings response)
    {
        if (response == null
            || string.IsNullOrWhiteSpace(response.serverId)
            || string.IsNullOrWhiteSpace(_pendingArmToken))
        {
            return false;
        }

        if (!Protocol.HasNativeEndIdentity(response.serverId, _pendingArmToken))
            return false;

        _boundNativeEndIdentity = response.serverId;
        return true;
    }

    /// <summary>
    /// A protected 5v5 may only become playable after the server confirms that
    /// the per-raid location clone was made silent. This live check turns a
    /// mutator exception or lost server state into an immediate no-contest stop
    /// instead of mixing vanilla waves into the requested five opponents.
    /// </summary>
    public static bool ValidateProtectedTeamRaid(out string reason)
    {
        reason = string.Empty;
        try
        {
            var response = JObject.Parse(RequestHandler.GetJson(Protocol.StatusRoute));
            var state = response["state"]?.Value<string>();
            var protectedTeam = response["pitFireTeamTeam"]?.Value<bool>() == true;
            var locationReady = response["locationReady"]?.Value<bool>() == true;
            var clientRaidStarted = response["clientRaidStarted"]?.Value<bool>() == true;
            var serverArmToken = response["armToken"]?.Value<string>();
            bool exactToken = !string.IsNullOrWhiteSpace(_activeArmToken)
                && string.Equals(serverArmToken, _activeArmToken, StringComparison.Ordinal);
            if (state == "InRaid"
                && protectedTeam
                && locationReady
                && clientRaidStarted
                && exactToken)
                return true;

            reason = response["locationFailure"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(reason))
            {
                reason = !protectedTeam
                    ? "the server did not retain the protected PitFireTeam raid"
                    : state != "InRaid"
                        ? $"the server reported Arena state '{state ?? "unknown"}'"
                        : !locationReady
                            ? "the server did not confirm a silent Arena location"
                            : !clientRaidStarted
                                ? "the server did not retain client raid-start confirmation"
                                : "the server did not retain the exact Arena arm token";
            }

            return false;
        }
        catch (Exception ex)
        {
            reason = "the server could not confirm the protected Arena location: "
                     + ex.GetBaseException().Message;
            return false;
        }
    }

    public static bool ConfirmRaidStarted()
    {
        if (!HasPendingArm)
            return false;

        _molotovArmToken = null;
        try
        {
            if (string.IsNullOrWhiteSpace(_pendingArmToken))
                return false;

            if (string.IsNullOrWhiteSpace(_boundNativeEndIdentity))
                return false;

            var body = JsonConvert.SerializeObject(new
            {
                armToken = _pendingArmToken,
                nativeEndIdentity = _boundNativeEndIdentity,
            });
            var response = JObject.Parse(RequestHandler.PostJson(Protocol.RaidConfirmRoute, body));
            if (response["confirmed"]?.Value<bool>() != true)
            {
                Plugin.ModLogger.LogWarning($"Arena raid-start confirmation rejected: {response}");
                return false;
            }

            HasPendingArm = false;
            HasPendingEnd = false;
            _activeArmToken = _pendingArmToken;
            _molotovArmToken = response["molotovAvailable"]?.Type == JTokenType.Boolean
                && response["molotovAvailable"].Value<bool>() ? _activeArmToken : null;
            _pendingArmToken = null;
            return true;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena raid-start confirmation failed: {ex.Message}");
            return false;
        }
    }

    public static bool PostRaidEnd(bool recoverWithoutNativeEnd = false)
    {
        try
        {
            string armToken = _activeArmToken ?? _pendingArmToken;
            if (string.IsNullOrWhiteSpace(armToken))
                return false;

            HasPendingEnd = true;
            var body = JsonConvert.SerializeObject(new
            {
                armToken,
                recoverWithoutNativeEnd,
            });
            var response = JObject.Parse(
                RequestHandler.PostJson(Protocol.RaidEndRoute, body));
            bool completed = response["completed"]?.Value<bool>() == true;
            bool disarmed = response["disarmed"]?.Value<bool>() == true;
            string state = response["state"]?.Value<string>();
            bool acknowledged = (completed || disarmed)
                && string.Equals(state, "Idle", StringComparison.Ordinal);
            if (!acknowledged)
            {
                // GameEnd reaches the server before /client/match/local/end, so the first
                // call normally finds the raid still InRaid and the server answers
                // deferred:true: the post-raid prefix owns the snapshot restore and runs a
                // moment later (ArenaModeRouter.HandleRaidEnd logs it as Info too). Only
                // that exact answer is expected; every other unacknowledged shape is a
                // real failure and stays a warning.
                bool expectedDeferral = response["deferred"]?.Value<bool>() == true
                    && string.Equals(state, "InRaid", StringComparison.Ordinal);
                if (expectedDeferral)
                    Plugin.ModLogger.LogDebug(
                        $"Arena raid-end remains pending: {response}");
                else
                    Plugin.ModLogger.LogWarning(
                        $"Arena raid-end remains pending: {response}");
                return false;
            }

            HasPendingArm = false;
            HasPendingEnd = false;
            _pendingArmToken = null;
            _activeArmToken = null;
            _boundNativeEndIdentity = null;
            return true;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena raid-end failed: {ex.Message}");
            HasPendingEnd = true;
            return false;
        }
    }
}
