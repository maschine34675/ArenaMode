using ArenaMode.SessionHelper;
using ArenaMode.Shared;
using ArenaMode.Shared.Session;
using BepInEx;
using SPT.Common.Http;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ArenaMode.Session;

/// <summary>
/// Everything the session helper needs to restart the game into (or out of) an Arena session. The
/// JSON shape is the request file of docs/ARENA-SESSION-20261021.md, section 6.
/// </summary>
internal sealed class ArenaSessionRequest
{
    internal string Direction;
    internal int GamePid;
    internal string GameExe;
    internal string GameDir;

    /// <summary>The game's own command line without the executable path (argv[0]).</summary>
    internal List<string> GameArgs = new List<string>();

    internal string ProfileId;
    internal string SptRuntimeDir;
    internal string RewardControlId;
    internal string OfflineSessionId;

    /// <summary>The server mods the Arena server may load: <c>Guid</c> and the folder in <c>File</c>.</summary>
    internal List<ArenaSessionUnit> ServerMods = new List<ArenaSessionUnit>();

    internal string PolicyHash;
    internal ArenaSessionVersionPlan VersionPlan;
}

internal sealed class ArenaSessionPreparation
{
    internal bool RewardControl;
    internal bool OfflineEntry;
}

/// <summary>
/// Two clicks within a window: the first arms, the second inside the window confirms. A pure state
/// machine on an explicit clock, so it is testable without Unity. Used by the notice's action button,
/// which restarts the whole game and therefore must not fire on a stray click.
/// </summary>
internal sealed class ArenaTwoClickGate
{
    internal const float WindowSeconds = 5f;

    private float _armedUntil = float.NegativeInfinity;

    internal bool IsArmed(float now) => now <= _armedUntil;

    internal int SecondsRemaining(float now)
        => IsArmed(now) ? Math.Max(1, (int)Math.Ceiling(_armedUntil - now)) : 0;

    /// <summary>Registers a click; true when it completed the confirmation.</summary>
    internal bool Click(float now)
    {
        if (IsArmed(now))
        {
            _armedUntil = float.NegativeInfinity;
            return true;
        }

        _armedUntil = now + WindowSeconds;
        return false;
    }

    internal void Reset() => _armedUntil = float.NegativeInfinity;
}

/// <summary>How a wait for the helper's acknowledgement stands.</summary>
internal enum ArenaHandshakeStatus
{
    /// <summary>Nothing decisive yet (or not yet time to look again).</summary>
    Waiting,

    /// <summary>The helper wrote its acknowledgement and is still running: it has taken the request over.</summary>
    Accepted,

    /// <summary>The helper process is gone before it acknowledged anything.</summary>
    HelperExited,

    /// <summary>The time ran out without an acknowledgement.</summary>
    TimedOut,
}

/// <summary>
/// One wait for the session helper's acknowledgement (docs/ARENA-SESSION-20261021.md, sections 6 and 7). The
/// client must not close the game on the strength of <c>Process.Start</c> alone: a helper that fails at once
/// (an unusable request, a busy mutex) would leave the player without a game and the reason only in
/// <c>helper.log</c>. So the game quits once the helper has written <c>&lt;request file&gt;.accepted</c> after
/// it validated the request and took its mutex. The class looks again every <see cref="PollIntervalSeconds"/>
/// (it is polled each frame and does nothing in between), for at most <see cref="TimeoutSeconds"/>, on the clock
/// the caller passes in. It is the caller that drives it from the main thread without ever blocking.
///
/// The file only counts when its content parses, names this helper's process id (a file an earlier request
/// left behind, or another helper's, is not this helper's word) and the helper is still running: a helper that
/// acknowledged and then vanished cannot switch anything, and the game must stay.
/// </summary>
internal sealed class ArenaHelperHandshake
{
    internal const float PollIntervalSeconds = 0.25f;
    internal const float TimeoutSeconds = 15f;

    /// <summary>An acknowledgement is a few dozen bytes; anything much larger is not one.</summary>
    private const long MaxAcknowledgementBytes = 4096;

    private readonly string _acknowledgementPath;
    private readonly int _helperPid;
    private readonly float _deadline;
    private float _nextPoll;

    internal ArenaHelperHandshake(string requestPath, int helperPid, float now)
    {
        _acknowledgementPath = HelperAcknowledgement.GetPath(requestPath);
        _helperPid = helperPid;
        _deadline = now + TimeoutSeconds;
        _nextPoll = now + PollIntervalSeconds;
    }

    internal string AcknowledgementPath => _acknowledgementPath;

    /// <summary>
    /// Looks at the helper and its acknowledgement when <see cref="PollIntervalSeconds"/> have passed since the
    /// last look (and once more when the deadline is reached, so the time is up at the first call on or after
    /// it, not up to a quarter second later), and at nothing otherwise. A helper that has exited wins over a
    /// file that is there: the switch cannot happen without it.
    /// </summary>
    internal ArenaHandshakeStatus Poll(float now, Func<int, bool> helperHasExited)
    {
        if (now < _nextPoll && now < _deadline) return ArenaHandshakeStatus.Waiting;
        _nextPoll = now + PollIntervalSeconds;

        if (helperHasExited(_helperPid)) return ArenaHandshakeStatus.HelperExited;
        if (IsAcknowledged()) return ArenaHandshakeStatus.Accepted;
        return now >= _deadline ? ArenaHandshakeStatus.TimedOut : ArenaHandshakeStatus.Waiting;
    }

    private bool IsAcknowledged()
    {
        string text;
        try
        {
            var info = new FileInfo(_acknowledgementPath);
            if (!info.Exists || info.Length > MaxAcknowledgementBytes) return false;
            using (var stream = new FileStream(_acknowledgementPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                text = reader.ReadToEnd();
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }

        return HelperAcknowledgement.TryParse(text, out int pid, out _, out _) && pid == _helperPid;
    }
}

/// <summary>
/// Requests a controlled game restart into an Arena session ("enter") or back to the normal session
/// ("leave"): writes the request file, starts the session helper and, only once the helper has
/// acknowledged the request, quits the game. The helper waits for this process to exit, switches the server and
/// starts the game again (docs/ARENA-SESSION-20261021.md, section 6). The restart takes about two
/// minutes; the player accepted that.
///
/// Optional reward-control preparation runs asynchronously, with a five-second fallback to read-only rewards.
/// The main thread otherwise does no more I/O than writing one small file, starting one process and, while the helper
/// has not answered yet, looking at one small file and one process every quarter second (a coroutine on the
/// plugin, never a wait). If the helper stops first or does not answer within
/// <see cref="ArenaHelperHandshake.TimeoutSeconds"/> seconds the game keeps running and the caller's failure
/// callback gets the reason. One Info line is logged per request, when the helper accepted it.
/// </summary>
internal static class ArenaSessionSwitch
{
    private const string TokenPrefix = "-token=";

    /// <summary>Shown during optional reward preparation and the helper acknowledgement.</summary>
    internal const string WaitingText = "Preparing restart…";
    internal const float RewardPrepareTimeoutSeconds = 5f;

    private static bool _requested;
    private static PendingSwitch _pending;
    private static object _preparing;

    /// <summary>Everything the wait for one helper acknowledgement needs once <c>Request</c> has returned.</summary>
    private sealed class PendingSwitch
    {
        internal string Direction;
        internal string Summary;
        internal string RequestPath;
        internal int HelperPid;
        internal ArenaHelperHandshake Handshake;
        internal Action<string> OnFailure;
    }

    /// <summary>True once a helper acknowledged a switch and the game was asked to quit.</summary>
    internal static bool RestartRequested => _requested;

    /// <summary>True during preparation and until the helper acknowledged, stopped or timed out.</summary>
    internal static bool HelperPending => _pending != null || _preparing != null;

    /// <summary>Clears the restart latch and any wait in progress; for the offline harness, which runs several requests.</summary>
    internal static void ResetRequestLatch()
    {
        _requested = false;
        _pending = null;
        _preparing = null;
    }

    // Seams for the offline harness; production keeps the defaults. The helper launcher returns
    // false with a message when the helper did not start.
    internal delegate bool HelperLauncher(string helper, string direction, string requestPath, out int helperPid, out string error);

    internal static Func<string[]> CommandLineProvider = Environment.GetCommandLineArgs;
    internal static Func<string> RequestPathProvider = ArenaSessionContract.GetRequestFilePath;
    internal static HelperLauncher LaunchHelper = StartHelper;
    internal static Action QuitGame = Application.Quit;
    internal static Func<string, Task<ArenaSessionPreparation>> PrepareRewardControl = PrepareRewardControlOnServer;
    internal static Func<string, string, string> OfflineSessionIdReader = ReadOfflineSessionId;

    /// <summary>The clock of the wait for the helper: real seconds, not game time.</summary>
    internal static Func<float> Clock = () => Time.realtimeSinceStartup;

    /// <summary>Whether the helper process with this id is gone.</summary>
    internal static Func<int, bool> HelperHasExited = HelperProcessHasExited;

    /// <summary>
    /// Starts the switch. Returns true once optional preparation or the helper acknowledgement is pending:
    /// the game has NOT quit yet, it quits when the helper has acknowledged, and if the helper stops or stays
    /// silent the game keeps running and <paramref name="onFailure"/> (on the main thread, later) gets the
    /// message for the player. Otherwise false with the message in <paramref name="error"/> and nothing changed.
    /// </summary>
    /// <param name="direction"><see cref="ArenaSessionContract.DirectionEnter"/> or <see cref="ArenaSessionContract.DirectionLeave"/>.</param>
    /// <param name="fallbackProfileId">The logged-in backend session's profile id, used only when the
    /// command line carries no usable <c>-token=</c> argument.</param>
    /// <param name="onFailure">Called once if a pending switch could not start or complete its helper handshake.</param>
    internal static bool Request(string direction, string fallbackProfileId, out string error, Action<string> onFailure = null)
    {
        error = null;
        try
        {
            if (RequestUnsafe(direction, fallbackProfileId, onFailure, out error)) return true;
            Plugin.ModLogger?.LogWarning("Arena session switch (" + direction + ") not started: " + error);
            return false;
        }
        catch (Exception ex)
        {
            error = "The Arena session switch failed: " + ex.Message;
            Plugin.ModLogger?.LogWarning("Arena session switch (" + direction + ") failed: " + ex);
            return false;
        }
    }

    private static bool RequestUnsafe(string direction, string fallbackProfileId, Action<string> onFailure, out string error)
    {
        error = null;
        bool enter = string.Equals(direction, ArenaSessionContract.DirectionEnter, StringComparison.Ordinal);
        bool leave = string.Equals(direction, ArenaSessionContract.DirectionLeave, StringComparison.Ordinal);
        if (!enter && !leave)
        {
            error = "Unknown Arena session direction.";
            return false;
        }

        if (_requested)
        {
            error = "The game is already restarting.";
            return false;
        }

        if (HelperPending)
        {
            error = "The Arena session helper is still starting.";
            return false;
        }

        if (enter == ArenaSessionMode.IsArenaSession)
        {
            error = enter ? "This game already is an Arena session." : "This game is not an Arena session.";
            return false;
        }

        // The wait for the helper's acknowledgement runs on the plugin object. Without it the game could never
        // decide to quit, so the helper is not started at all.
        if (Plugin.Instance == null)
        {
            error = "The ArenaMode plugin is not running, so the game cannot wait for the Arena session helper.";
            return false;
        }

        string helper = ArenaWhitelistAdmission.HelperPath();
        string requestPath = RequestPathProvider();
        if (requestPath == null)
        {
            error = "The local application data folder is not available.";
            return false;
        }

        if (!File.Exists(helper))
        {
            error = "The Arena session helper is missing: " + helper;
            return false;
        }

        string[] commandLine = CommandLineProvider();
        string profileId = ResolveProfileId(commandLine, fallbackProfileId);
        if (profileId == null)
        {
            error = "The logged-in profile could not be determined.";
            return false;
        }

        var request = new ArenaSessionRequest
        {
            Direction = direction,
            GamePid = System.Diagnostics.Process.GetCurrentProcess().Id,
            GameExe = BepInEx.Paths.ExecutablePath,
            GameDir = BepInEx.Paths.GameRootPath,
            GameArgs = SkipExecutable(commandLine),
            ProfileId = profileId,
            SptRuntimeDir = ArenaWhitelistAdmission.SptRuntimeDirectory(),
        };

        if (enter)
        {
            if (!ArenaWhitelistAdmission.TryPrepareSessionStart(out string policyHash, out var serverMods, out var versionPlan, out error))
                return false;
            request.PolicyHash = policyHash;
            request.VersionPlan = versionPlan;
            request.ServerMods = serverMods;
        }
        else
        {
            // Leaving needs no policy or server mods: the helper restores the normal server.
            request.PolicyHash = ArenaSessionMode.GateReport?.PolicyHash ?? string.Empty;
            request.OfflineSessionId = OfflineSessionIdReader(request.ProfileId, request.SptRuntimeDir);
        }

        string requestJson = BuildRequestJson(request);

        // The helper refuses a request it cannot parse, and by then this game has already quit and nothing
        // starts it again. So run the helper's own parser (linked source, the rules cannot drift) on exactly
        // the text it will read, before anything is written or started: a request it would refuse leaves the
        // game running and tells the player why.
        if (!ValidateHelperRequest(requestJson, direction, out error)) return false;

        if (enter)
        {
            string controlId = Guid.NewGuid().ToString("N");
            Task<ArenaSessionPreparation> preparation = null;
            try { preparation = PrepareRewardControl(controlId); }
            catch (Exception)
            {
                error = "Arena session preparation failed. The game keeps running; please try again.";
                return false;
            }
            // Observe even a cancelled switch's later transport fault; this continuation cannot launch a helper.
            if (preparation != null)
                _ = preparation.ContinueWith(task => { var observed = task.Exception; },
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            if (preparation != null && !preparation.IsCompleted)
            {
                var ticket = new object();
                _preparing = ticket;
                try
                {
                    Plugin.Instance.StartCoroutine(WaitForRewardPreparation(ticket, preparation, controlId,
                        request, helper, requestPath, onFailure, Clock() + RewardPrepareTimeoutSeconds));
                    return true;
                }
                catch (Exception)
                {
                    _preparing = null;
                    error = "Arena session preparation could not be completed. The game keeps running.";
                    return false;
                }
            }
            if (preparation == null || preparation.Status != TaskStatus.RanToCompletion || preparation.Result == null)
            {
                error = "Arena session preparation failed. The game keeps running; please try again.";
                return false;
            }
            ApplyPreparation(request, preparation, controlId);
        }
        return StartPreparedSwitch(request, helper, requestPath, onFailure, out error);
    }

    private static IEnumerator WaitForRewardPreparation(object ticket, Task<ArenaSessionPreparation> preparation, string controlId,
        ArenaSessionRequest request, string helper, string requestPath, Action<string> onFailure, float deadline)
    {
        while (ReferenceEquals(_preparing, ticket) && !preparation.IsCompleted && Clock() < deadline)
            yield return null;
        if (!ReferenceEquals(_preparing, ticket)) yield break;
        _preparing = null;
        string failure = "Arena session preparation did not finish. The game keeps running; please try again.";
        try
        {
            if (preparation.Status == TaskStatus.RanToCompletion && preparation.Result != null)
            {
                ApplyPreparation(request, preparation, controlId);
                if (StartPreparedSwitch(request, helper, requestPath, onFailure, out failure)) yield break;
            }
        }
        catch (Exception) { failure = "The Arena session helper could not be started. The game keeps running."; }
        Plugin.ModLogger?.LogWarning("Arena session switch not started: " + failure);
        try { onFailure?.Invoke(failure); }
        catch (Exception) { Plugin.ModLogger?.LogDebug("Arena session switch failure notice could not be displayed."); }
    }

    private static void ApplyPreparation(ArenaSessionRequest request, Task<ArenaSessionPreparation> preparation, string controlId)
    {
        if (preparation != null && preparation.Status == TaskStatus.RanToCompletion && preparation.Result != null)
        {
            request.RewardControlId = preparation.Result.RewardControl ? controlId : null;
            request.OfflineSessionId = preparation.Result.OfflineEntry ? controlId : null;
        }
        if (request.OfflineSessionId == null)
            Plugin.ModLogger?.LogDebug("Arena entry shutdown unavailable; the normal server remains running.");
        if (request.RewardControlId == null && request.OfflineSessionId == null)
            Plugin.ModLogger?.LogDebug("Arena reward control was not prepared; private-session rewards remain read-only.");
    }

    private static async Task<ArenaSessionPreparation> PrepareRewardControlOnServer(string controlId)
    {
        // Independent: stopping an idle local server must not require enabled rewards.
        var control = PrepareOne(RewardWire.PrepareControlRoute, nameof(RewardControlPrepareRequest.ControlId), controlId);
        var entry = PrepareOne(RewardWire.PrepareEntryRoute, nameof(RewardEntryPrepareRequest.SessionId), controlId, true);
        // A delayed optional consent grant must never hide an already accepted entry
        // grant and downgrade the helper to legacy startup. Offline control uses the lease.
        bool offline = await entry.ConfigureAwait(false);
        return new ArenaSessionPreparation { RewardControl = control.Status == TaskStatus.RanToCompletion && control.Result,
            OfflineEntry = offline };
    }

    // This ID is only a routing hint. The helper verifies the full lease, marker,
    // source runtime and stopped-process identity before it can restore normal SPT.
    internal static string ReadOfflineSessionId(string profileId, string sourceRuntime)
    {
        string root = ArenaSessionContract.GetServerTreePath();
        return root == null ? null : ReadOfflineSessionIdFromMarker(
            Path.Combine(root, ArenaSessionContract.MarkerFileName), profileId, sourceRuntime);
    }

    internal static string ReadOfflineSessionIdFromMarker(string path, string profileId, string sourceRuntime)
    {
        try
        {
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 16384) throw new InvalidDataException("Arena session marker is too large.");
            if (!ArenaMiniJson.TryParse(File.ReadAllText(path), out var raw, out _))
                throw new InvalidDataException("Arena session marker is unreadable.");
            var marker = ArenaMiniJson.AsObject(raw);
            string id = ArenaMiniJson.GetString(marker, "offlineSessionId");
            if (id == null) return null;
            if (!Guid.TryParseExact(id, "N", out _)
                || ArenaMiniJson.GetString(marker, "profileId") != profileId
                || !string.Equals(Path.GetFullPath(ArenaMiniJson.GetString(marker, "sourceRuntime") ?? ".").TrimEnd('\\', '/'),
                    Path.GetFullPath(sourceRuntime).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Arena session marker belongs to another session.");
            return id;
        }
        catch (Exception ex)
        {
            // No downgrade of a damaged offline session into the legacy return path.
            throw new InvalidOperationException("The Arena session could not be verified for returning to normal SPT.", ex);
        }
    }

    private static async Task<bool> PrepareOne(string route, string field, string id, bool strictEntry = false)
    {
        try
        {
            // SPT JsonUtil preserves case; match the actual public DTO.
            string response = await RequestHandler.PostJsonAsync(route,
                ArenaMiniJson.Write(new Dictionary<string, object> { [field] = id })).ConfigureAwait(false);
            object root;
            string error;
            if (response == null || response.Length > 4096 || !ArenaMiniJson.TryParse(response, out root, out error))
                throw new InvalidDataException();
            var result = ArenaMiniJson.AsObject(root);
            object accepted;
            if (result == null || (!result.TryGetValue("Accepted", out accepted) && !result.TryGetValue("accepted", out accepted))
                || !(accepted is bool)) throw new InvalidDataException();
            return (bool)accepted;
        }
        catch (Exception)
        {
            if (strictEntry) throw new InvalidOperationException("Arena entry preparation could not be confirmed.");
            return false;
        }
    }

    private static bool StartPreparedSwitch(ArenaSessionRequest request, string helper, string requestPath,
        Action<string> onFailure, out string error)
    {
        error = null;
        string direction = request.Direction;
        string requestJson = BuildRequestJson(request);
        if (!ValidateHelperRequest(requestJson, direction, out error)) return false;

        // An acknowledgement of an earlier request must not be mistaken for this one's. The helper deletes it too
        // and the pid check would catch it, but the client has to be sure on its own.
        DeleteStaleAcknowledgement(requestPath);
        WriteRequestFile(requestPath, requestJson);

        // Only a helper that really started may close the game: quitting first would strand the
        // player with no game and no helper. The helper waits for this process to exit.
        if (!LaunchHelper(helper, direction, requestPath, out int helperPid, out error))
            return false;

        // A running helper is still no reason to quit: it may yet refuse the request or fail on the mutex. The
        // game quits when the helper has acknowledged (Step), and stays if it does not.
        var pending = new PendingSwitch
        {
            Direction = direction,
            RequestPath = requestPath,
            HelperPid = helperPid,
            Handshake = new ArenaHelperHandshake(requestPath, helperPid, Clock()),
            OnFailure = onFailure,
            Summary = direction + " requested (" + request.ServerMods.Count + " server mods, policy "
                + (string.IsNullOrEmpty(request.PolicyHash) ? "none" : request.PolicyHash) + ")",
        };

        // Latched BEFORE the coroutine starts: Unity runs a new coroutine up to its first yield inside StartCoroutine,
        // and that first look must find this wait still registered.
        _pending = pending;
        try
        {
            StartWaiting(pending);
        }
        catch (Exception ex)
        {
            _pending = null;
            WithdrawRequest(pending);
            error = "The game could not wait for the Arena session helper (" + ex.Message + "), so it keeps running.";
            return false;
        }

        Plugin.ModLogger?.LogDebug("Arena session: " + pending.Summary + "; helper started (pid " + helperPid
            + "), waiting for " + pending.Handshake.AcknowledgementPath);
        return true;
    }

    // ------------------------------------------------------------------ the wait for the helper

    private static bool ValidateHelperRequest(string requestJson, string direction, out string error)
    {
        error = null;
        if (!HelperRequest.TryParse(requestJson, direction, out _, out string refusal))
        {
            error = "The Arena session cannot be started: " + refusal + ".";
            return false;
        }
        return true;
    }

    private static void StartWaiting(PendingSwitch pending)
    {
        var plugin = Plugin.Instance;
        if (plugin == null) throw new InvalidOperationException("the ArenaMode plugin is not running");
        plugin.StartCoroutine(WaitForHelper(pending));
    }

    /// <summary>One look per frame at most (the handshake itself decides when it is time); never blocks.</summary>
    private static IEnumerator WaitForHelper(PendingSwitch pending)
    {
        while (true)
        {
            bool finished;
            try { finished = Step(pending); }
            catch (Exception ex)
            {
                WithdrawRequest(pending);
                Fail(pending, "The Arena session switch failed while it waited for the helper: " + ex.Message);
                yield break;
            }

            if (finished) yield break;
            yield return null;
        }
    }

    /// <summary>One look at the helper. True when the wait is over, whatever its outcome.</summary>
    private static bool Step(PendingSwitch pending)
    {
        if (!ReferenceEquals(_pending, pending)) return true; // withdrawn (the harness resets the latch)

        switch (pending.Handshake.Poll(Clock(), HelperHasExited))
        {
            case ArenaHandshakeStatus.Waiting:
                return false;
            case ArenaHandshakeStatus.Accepted:
                _pending = null;
                _requested = true;
                Plugin.ModLogger?.LogInfo("Arena session: " + pending.Summary + "; helper accepted it (pid " + pending.HelperPid
                    + "), closing the game.");
                QuitGame();
                return true;
            case ArenaHandshakeStatus.HelperExited:
                WithdrawRequest(pending);
                Fail(pending, "The Arena session helper stopped before it accepted the request, so the game keeps running. See "
                    + (ArenaSessionContract.GetHelperLogPath() ?? "its log") + ".");
                return true;
            default:
                WithdrawRequest(pending);
                Fail(pending, "The Arena session helper did not accept the request within "
                    + ArenaHelperHandshake.TimeoutSeconds.ToString("0") + " seconds, so the game keeps running. See "
                    + (ArenaSessionContract.GetHelperLogPath() ?? "its log") + ".");
                return true;
        }
    }

    /// <summary>
    /// Whenever the client reports that the switch did not happen, the request is taken back (the file is deleted):
    /// after a timeout, after the helper stopped, after a failed wait. A helper that is merely slow and has not read
    /// the file yet then cannot read it and ends without touching anything. A helper that already read it and
    /// acknowledged late watches the file while it waits for the game: when the file is gone it ends without
    /// touching anything, so a player who closes the game by hand afterwards does not get an unannounced switch
    /// (<c>HelperRequestWatch</c>). A new attempt writes a new file. Never throws.
    /// </summary>
    private static void WithdrawRequest(PendingSwitch pending)
    {
        try
        {
            if (File.Exists(pending.RequestPath)) File.Delete(pending.RequestPath);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger?.LogDebug("Arena session: the request file could not be withdrawn: " + ex.Message);
        }
    }

    private static void Fail(PendingSwitch pending, string message)
    {
        if (ReferenceEquals(_pending, pending)) _pending = null;
        Plugin.ModLogger?.LogWarning("Arena session switch (" + pending.Direction + ") not completed: " + message);
        try { pending.OnFailure?.Invoke(message); }
        catch (Exception ex) { Plugin.ModLogger?.LogWarning("Arena session switch failure notice failed: " + ex.Message); }
    }

    // ------------------------------------------------------------------ pure parts

    /// <summary>The request file for the helper; compact JSON in the documented key order.</summary>
    internal static string BuildRequestJson(ArenaSessionRequest request)
    {
        var mods = new List<object>();
        foreach (var unit in request.ServerMods)
        {
            if (unit == null || string.IsNullOrEmpty(unit.Guid) || string.IsNullOrEmpty(unit.File)) continue;
            var entry = new Dictionary<string, object>();
            entry["guid"] = unit.Guid;
            entry["folder"] = unit.File;
            mods.Add(entry);
        }

        var root = new Dictionary<string, object>();
        root["schema"] = request.OfflineSessionId != null ? ArenaSessionContract.OfflineRequestSchema
            : request.VersionPlan == null ? ArenaSessionContract.SchemaVersion : ArenaSessionContract.VersionPlanRequestSchema;
        root["direction"] = request.Direction;
        root["gamePid"] = request.GamePid;
        root["gameExe"] = request.GameExe;
        root["gameDir"] = request.GameDir;
        root["gameArgs"] = new List<string>(request.GameArgs);
        root["profileId"] = request.ProfileId;
        if (!string.IsNullOrEmpty(request.RewardControlId)) root["rewardControlId"] = request.RewardControlId;
        if (!string.IsNullOrEmpty(request.OfflineSessionId)) root["offlineSessionId"] = request.OfflineSessionId;
        root["sptRuntimeDir"] = request.SptRuntimeDir;
        root["serverMods"] = mods;
        root["policyHash"] = request.PolicyHash ?? string.Empty;
        if (request.VersionPlan != null) root["versionPlan"] = request.VersionPlan.ToObject();
        return ArenaMiniJson.Write(root);
    }

    /// <summary>
    /// The profile id: the <c>-token=</c> argument (the launcher's session id, which is the profile
    /// id), else the logged-in backend session's profile. Null when neither is an id the helper
    /// accepts (24 hexadecimal characters): the id becomes a file name there, and a request with
    /// another id would be refused after this game has quit.
    /// </summary>
    internal static string ResolveProfileId(IEnumerable<string> commandLine, string fallbackProfileId)
    {
        if (commandLine != null)
        {
            foreach (string argument in commandLine)
            {
                if (argument == null || !argument.StartsWith(TokenPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                string token = argument.Substring(TokenPrefix.Length).Trim();
                if (IsSafeProfileId(token)) return token;
            }
        }

        string fallback = fallbackProfileId?.Trim();
        return IsSafeProfileId(fallback) ? fallback : null;
    }

    /// <summary>The helper's own rule for a profile id (the same code), so that client and helper agree.</summary>
    internal static bool IsSafeProfileId(string id) => HelperRequest.IsValidProfileId(id);

    /// <summary>The command line without argv[0], in order, nulls dropped.</summary>
    internal static List<string> SkipExecutable(IList<string> commandLine)
    {
        var args = new List<string>();
        if (commandLine == null) return args;
        for (int i = 1; i < commandLine.Count; i++)
            if (commandLine[i] != null) args.Add(commandLine[i]);
        return args;
    }

    // ------------------------------------------------------------------ side effects

    /// <summary>Deletes <c>&lt;request&gt;.accepted</c> of an earlier request. Never throws: if it stays, the pid check decides.</summary>
    private static void DeleteStaleAcknowledgement(string requestPath)
    {
        string path = HelperAcknowledgement.GetPath(requestPath);
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger?.LogWarning("Arena session: an old helper acknowledgement could not be removed (" + ex.Message
                + "); only an acknowledgement from the new helper's process id will count.");
        }
    }

    /// <summary>
    /// True when no running process has this id. A process that cannot be inspected (access denied) is not
    /// reported as gone: the timeout covers a helper that never answers.
    /// </summary>
    private static bool HelperProcessHasExited(int pid)
    {
        try
        {
            using (var process = System.Diagnostics.Process.GetProcessById(pid))
                return process.HasExited;
        }
        catch (ArgumentException) { return true; } // no process with this id
        catch (InvalidOperationException) { return true; }
        catch (Exception) { return false; }
    }

    private static void WriteRequestFile(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temp = path + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(false));
        if (File.Exists(path)) File.Delete(path);
        File.Move(temp, path);
    }

    private static bool StartHelper(string helper, string direction, string requestPath, out int helperPid, out string error)
    {
        helperPid = 0;
        error = null;
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = helper,
            Arguments = direction + " --request \"" + requestPath + "\"",
            WorkingDirectory = Path.GetDirectoryName(helper),
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using (var process = System.Diagnostics.Process.Start(startInfo))
        {
            if (process == null)
            {
                error = "The Arena session helper could not be started.";
                return false;
            }

            helperPid = process.Id;
            // The helper waits for this process to exit, so a helper that is already gone failed. One that is
            // still running has not necessarily taken the request over: that is the acknowledgement's job.
            if (process.HasExited)
            {
                error = "The Arena session helper stopped right after starting (exit code "
                    + process.ExitCode + "). See " + (ArenaSessionContract.GetHelperLogPath() ?? "its log") + ".";
                return false;
            }
        }

        return true;
    }
}
