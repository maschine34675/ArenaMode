using ArenaMode.SessionHelper;
using ArenaMode.Shared.Session;
using BepInEx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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

    /// <summary>The server mods the Arena server may load: <c>Guid</c> and the folder in <c>File</c>.</summary>
    internal List<ArenaSessionUnit> ServerMods = new List<ArenaSessionUnit>();

    internal string PolicyHash;
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

/// <summary>
/// Requests a controlled game restart into an Arena session ("enter") or back to the normal session
/// ("leave"): writes the request file, starts the session helper and, only when the helper process
/// really started, quits the game. The helper waits for this process to exit, switches the server and
/// starts the game again (docs/ARENA-SESSION-20261021.md, section 6). The restart takes about two
/// minutes; the player accepted that.
///
/// The main thread does no more I/O than writing one small file and starting one process. One Info
/// line is logged per request.
/// </summary>
internal static class ArenaSessionSwitch
{
    private const string TokenPrefix = "-token=";

    private static bool _requested;

    /// <summary>True once a switch started the helper and asked the game to quit.</summary>
    internal static bool RestartRequested => _requested;

    /// <summary>Clears the restart latch; for the offline harness, which runs several successful requests.</summary>
    internal static void ResetRequestLatch() => _requested = false;

    // Seams for the offline harness; production keeps the defaults. The helper launcher returns
    // false with a message when the helper did not start.
    internal delegate bool HelperLauncher(string helper, string direction, string requestPath, out int helperPid, out string error);

    internal static Func<string[]> CommandLineProvider = Environment.GetCommandLineArgs;
    internal static Func<string> RequestPathProvider = ArenaSessionContract.GetRequestFilePath;
    internal static HelperLauncher LaunchHelper = StartHelper;
    internal static Action QuitGame = Application.Quit;

    /// <summary>
    /// Starts the switch. Returns true when the helper started and the game is quitting; otherwise
    /// false with a message for the player in <paramref name="error"/> and nothing changed.
    /// </summary>
    /// <param name="direction"><see cref="ArenaSessionContract.DirectionEnter"/> or <see cref="ArenaSessionContract.DirectionLeave"/>.</param>
    /// <param name="fallbackProfileId">The logged-in backend session's profile id, used only when the
    /// command line carries no usable <c>-token=</c> argument.</param>
    internal static bool Request(string direction, string fallbackProfileId, out string error)
    {
        error = null;
        try
        {
            if (RequestUnsafe(direction, fallbackProfileId, out error)) return true;
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

    private static bool RequestUnsafe(string direction, string fallbackProfileId, out string error)
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

        if (enter == ArenaSessionMode.IsArenaSession)
        {
            error = enter ? "This game already is an Arena session." : "This game is not an Arena session.";
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
            if (!ArenaWhitelistAdmission.TryPrepareSessionStart(out string policyHash, out var serverMods, out error))
                return false;
            request.PolicyHash = policyHash;
            request.ServerMods = serverMods;
        }
        else
        {
            // Leaving needs no policy or server mods: the helper restores the normal server.
            request.PolicyHash = ArenaSessionMode.GateReport?.PolicyHash ?? string.Empty;
        }

        string requestJson = BuildRequestJson(request);

        // The helper refuses a request it cannot parse, and by then this game has already quit and nothing
        // starts it again. So run the helper's own parser (linked source, the rules cannot drift) on exactly
        // the text it will read, before anything is written or started: a request it would refuse leaves the
        // game running and tells the player why.
        if (!HelperRequest.TryParse(requestJson, direction, out _, out string refusal))
        {
            error = "The Arena session cannot be started: " + refusal + ".";
            return false;
        }

        WriteRequestFile(requestPath, requestJson);

        // Only a helper that really started may close the game: quitting first would strand the
        // player with no game and no helper. The helper waits for this process to exit.
        if (!LaunchHelper(helper, direction, requestPath, out int helperPid, out error))
            return false;

        _requested = true;
        Plugin.ModLogger?.LogInfo("Arena session: " + direction + " requested (" + request.ServerMods.Count
            + " server mods, policy " + (string.IsNullOrEmpty(request.PolicyHash) ? "none" : request.PolicyHash)
            + "); helper started (pid " + helperPid + "), closing the game.");
        QuitGame();
        return true;
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
        root["schema"] = ArenaSessionContract.SchemaVersion;
        root["direction"] = request.Direction;
        root["gamePid"] = request.GamePid;
        root["gameExe"] = request.GameExe;
        root["gameDir"] = request.GameDir;
        root["gameArgs"] = new List<string>(request.GameArgs);
        root["profileId"] = request.ProfileId;
        root["sptRuntimeDir"] = request.SptRuntimeDir;
        root["serverMods"] = mods;
        root["policyHash"] = request.PolicyHash ?? string.Empty;
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
            // The helper waits for this process to exit, so a helper that is already gone failed.
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
