using ArenaMode.SessionHelper;
using ArenaMode.Shared.Session;
using BepInEx;
using BepInEx.Bootstrap;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ArenaMode.Session;

/// <summary>
/// Whitelist admission for Arena (docs/ARENA-SESSION-20261021.md, section 7). Arena runs only with
/// whitelisted mods. This class answers "may Arena run in this process, and if not, what is the way
/// out" and nothing else; it never disables or changes another plugin.
///
/// Two process kinds:
/// <list type="bullet">
/// <item><b>Arena session</b> (started with <c>-arena-session</c>): the preloader gate already
/// removed every mod that is not on the whitelist. Arena is admitted only if that gate ran to
/// completion and the server answers as an Arena instance; the player can only return to the normal
/// session.</item>
/// <item><b>Normal session</b>: the installed plugins (and the server's mods from the ping) are
/// evaluated with the same policy and the same <see cref="ArenaWhitelistDecision"/> the gate uses.
/// Anything excluded refuses Arena and, when the server and the helper can do it, offers an Arena
/// session. Version warnings never refuse.</item>
/// </list>
///
/// The policy may be a blacklist (the user file says <c>"unlisted": "allow"</c>): then everything that
/// neither the user file nor the default file denies is allowed, only denied mods are excluded, and
/// nothing else in this class changes. Only the refusal text differs, because "not on the whitelist"
/// would be wrong there.
///
/// The shipped default list has a deny list of its own (mods ArenaMode refuses by design). A denied mod,
/// whether the default or the user's file denies it, is named apart from the mods that are merely not on
/// the whitelist: a "Blocked for Arena" mod stays out of every session until the user file lifts the
/// block (an allow entry lifts the default list's deny; a user-denied mod needs that deny removed),
/// whereas a mod that is not listed only needs an allow entry in the user file.
///
/// Fika is never filtered and keeps its own refusal: with Fika present this class admits (returns no
/// reason) and offers no session, so the existing "unavailable with Fika" rule decides.
///
/// <see cref="GetRejectionReason"/> is polled from UI frames. It performs no network I/O and reads
/// the two small policy files only when its inputs change (a new server ping, a different plugin
/// count), so the answer is cached per ping generation. Main thread only.
/// </summary>
internal static class ArenaWhitelistAdmission
{
    internal const string SessionNoticeText =
        "ARENA SESSION: only Arena is available. Profile changes in this session are discarded.";

    internal const string StartActionLabel = "START ARENA SESSION";
    internal const string ReturnActionLabel = "RETURN TO NORMAL SESSION";

    /// <summary>The longest the main thread waits for the ping when a screen opens.</summary>
    private const int ProbeBudgetMilliseconds = 250;

    private const long MaxPolicyFileBytes = 256 * 1024;
    private const int MaxNamesListed = 12;
    private const int MaxWarningsListed = 4;

    private static Outcome _outcome;
    private static long _outcomeKey = -1;
    private static readonly HashSet<string> LoggedOnce = new HashSet<string>(StringComparer.Ordinal);

    private sealed class Outcome
    {
        /// <summary>The refusal, or null when whitelist admission lets Arena run.</summary>
        internal string Reason;

        /// <summary>
        /// The second line: the policy-override notes (the user file lets unlisted mods through),
        /// reviewed-version differences and pulled-in dependencies.
        /// </summary>
        internal string WarningLine;

        /// <summary>The individual warnings behind <see cref="WarningLine"/>, for the log.</summary>
        internal List<string> Notes = new List<string>();

        /// <summary>The refusal is a whitelist refusal and an Arena session can be started from here.</summary>
        internal bool CanStartSession;

        /// <summary>The server and the helper could start a session, whether or not anything is excluded.</summary>
        internal bool SessionPossible;

        /// <summary>Why <see cref="SessionPossible"/> is false; null when it is true.</summary>
        internal string SessionImpossibleReason;

        internal ArenaWhitelistPolicy Policy;

        /// <summary>The server mods the Arena session server may load (folder in <c>File</c>).</summary>
        internal List<ArenaSessionUnit> AllowedServerMods = new List<ArenaSessionUnit>();
    }

    // ------------------------------------------------------------------ admission

    /// <summary>
    /// The first provider of <c>ArenaOptionalMods.GetRejectionReason</c>. Null admits. Never throws:
    /// an internal failure refuses Arena in both session kinds. Since 2026-10-22 the whitelist is the
    /// only thing that keeps unlisted and denied mods out of Arena (the per-mod refusals are gone), so
    /// a whitelist that cannot be evaluated must not let Arena start; ordinary raids are unaffected.
    /// </summary>
    internal static string GetRejectionReason()
    {
        try { return Current().Reason; }
        catch (Exception ex)
        {
            LogOnce("failure:" + ex.GetType().Name, true,
                "Arena whitelist admission could not be evaluated: " + ex.Message);
            return ArenaSessionMode.IsArenaSession
                ? "Arena could not verify the session state. Return to the normal session and check the client log."
                : "Arena could not evaluate its mod whitelist (" + ex.Message + "). Check the client log; ordinary PMC/Scav raids remain available.";
        }
    }

    /// <summary>
    /// The notes of the last evaluation, or null: the policy-override notes first, then reviewed-version
    /// differences and pulled-in dependencies. The Arena menu shows them as the second notice line (with
    /// a refusal, or as an information notice once ARENA is selected) and the Arena session notice
    /// carries them. Never throws.
    /// </summary>
    internal static string WarningLine
    {
        get
        {
            try { return Current().WarningLine; }
            catch (Exception) { return null; }
        }
    }

    /// <summary>
    /// True when <paramref name="reason"/> is the current whitelist refusal and a session can be
    /// started from this process (the START ARENA SESSION action). Never throws.
    /// </summary>
    internal static bool CanOfferSessionStart(string reason)
    {
        try
        {
            var outcome = Current();
            return outcome.CanStartSession && reason != null
                && string.Equals(reason, outcome.Reason, StringComparison.Ordinal);
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Called by the side-selection screen before it evaluates eligibility: makes sure the server's
    /// ping is known when the answer depends on it (always in an Arena session; in a normal session
    /// only when the client side alone already shows excluded mods). The ping carries the server's
    /// mods and the Arena-instance flag. The wait is bounded (<see cref="ProbeBudgetMilliseconds"/>)
    /// and never throws; a ping that does not arrive in time simply leaves them unknown, and the
    /// explicit Arena click asks again.
    /// </summary>
    internal static void PrepareForScreen()
    {
        try
        {
            if (ArenaServerClient.IsServerModKnownAvailable || HasFika()) return;
            // Nothing to ask without a policy either: that refusal names the installation, not the server.
            if (!ArenaSessionMode.IsArenaSession && (Current().Reason == null || Current().Policy == null)) return;
            ArenaServerClient.TryRefreshWithin(ProbeBudgetMilliseconds);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger?.LogDebug("Arena whitelist could not refresh the server ping: " + ex.Message);
        }
    }

    /// <summary>
    /// What the session switch needs for an <c>enter</c> request: the policy hash and the server mods
    /// the Arena server tree may contain, with their folders from the last ping.
    /// </summary>
    internal static bool TryPrepareSessionStart(out string policyHash, out List<ArenaSessionUnit> serverMods, out string error)
    {
        policyHash = null;
        serverMods = null;
        error = null;
        if (ArenaSessionMode.IsArenaSession)
        {
            error = "This game already is an Arena session.";
            return false;
        }

        var outcome = Current();
        if (outcome.Policy == null || !outcome.SessionPossible)
        {
            error = outcome.SessionImpossibleReason ?? "An Arena session cannot be started from here.";
            return false;
        }

        policyHash = outcome.Policy.Hash;
        serverMods = new List<ArenaSessionUnit>(outcome.AllowedServerMods);
        return true;
    }

    // ------------------------------------------------------------------ evaluation

    private static Outcome Current()
    {
        long key = ((long)ArenaServerClient.PingGeneration << 32) | (uint)Chainloader.PluginInfos.Count;
        if (_outcome == null || key != _outcomeKey)
        {
            var outcome = Compute();
            _outcome = outcome;
            _outcomeKey = key;
            LogNotesOnce(outcome);
        }

        return _outcome;
    }

    private static Outcome Compute()
    {
        var outcome = new Outcome();
        // Fika is never filtered and keeps its own refusal; a session is never offered with it.
        if (HasFika())
        {
            outcome.SessionImpossibleReason = "Arena is unavailable with Fika.";
            return outcome;
        }

        outcome.Policy = LoadPolicy(out string policyProblem);
        if (ArenaSessionMode.IsArenaSession) ComputeArenaSession(outcome);
        else if (outcome.Policy != null) ComputeNormalSession(outcome, outcome.Policy);
        else outcome.Reason = PolicyUnavailableRefusal(policyProblem);
        return outcome;
    }

    private static void ComputeArenaSession(Outcome outcome)
    {
        if (!ArenaSessionMode.GateActive)
        {
            string problem = ArenaSessionMode.Problem ?? "the preloader gate did not run";
            outcome.Reason = "The Arena session could not filter the installed mods (" + problem
                + "). Arena is unavailable in this session; return to the normal session.";
            LogOnce("gate:" + problem, true, "Arena session without an active mod filter: " + problem);
            return;
        }

        if (ArenaServerClient.IsServerModKnownAvailable && !ArenaServerClient.IsArenaInstance)
        {
            outcome.Reason = "This game is not connected to the Arena session server. "
                + "Arena is unavailable in this session; return to the normal session.";
            return;
        }

        var notes = new List<string>(ArenaSessionMode.GateReport.Warnings);
        var serverMods = ArenaServerClient.ServerMods;
        if (outcome.Policy != null && serverMods != null)
        {
            var decision = ArenaWhitelistDecision.Evaluate(outcome.Policy, ArenaWhitelistSide.Server, ServerUnits(serverMods));
            notes.AddRange(decision.Warnings);
        }

        outcome.Notes = notes;
        outcome.WarningLine = FormatNotes(notes);
    }

    private static void ComputeNormalSession(Outcome outcome, ArenaWhitelistPolicy policy)
    {
        var client = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, ClientUnits());
        var serverMods = ArenaServerClient.ServerMods;
        ArenaWhitelistDecision server = serverMods == null
            ? null
            : ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Server, ServerUnits(serverMods));

        // A mod with a client and a server part has the same GUID on both sides but often two
        // different names ("DynamicMaps" / "Dynamic Maps"): list it once, by GUID, under its
        // client name. Denied mods (either deny list) and unlisted ones are told apart.
        var denied = new List<string>();
        var unlisted = new List<string>();
        var listedGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var unit in client.Excluded) AddExcluded(denied, unlisted, listedGuids, unit);
        if (server != null) foreach (var unit in server.Excluded) AddExcluded(denied, unlisted, listedGuids, unit);

        var notes = new List<string>(client.Warnings);
        if (server != null)
        {
            notes.AddRange(server.Warnings);
            outcome.AllowedServerMods.AddRange(server.Allowed);
        }

        outcome.Notes = notes;
        outcome.WarningLine = FormatNotes(notes);
        outcome.SessionImpossibleReason = FindSessionObstacle(outcome.AllowedServerMods);
        outcome.SessionPossible = outcome.SessionImpossibleReason == null;
        if (denied.Count == 0 && unlisted.Count == 0) return;

        outcome.CanStartSession = outcome.SessionPossible;
        outcome.Reason = FormatRefusal(denied, unlisted, outcome.CanStartSession, policy.AllowsUnlisted);
        if (!outcome.CanStartSession)
            Plugin.ModLogger?.LogDebug("Arena session not offered: " + outcome.SessionImpossibleReason);
    }

    /// <summary>
    /// Why no Arena session can be started from a normal session, or null when one can. Everything the
    /// helper would refuse after this game has quit belongs here, so that the session is not offered
    /// (the request itself is checked once more with the helper's own parser before the game quits).
    /// </summary>
    private static string FindSessionObstacle(List<ArenaSessionUnit> allowedServerMods)
    {
        // Older servers do not send the session fields: no offer, the client-side check stands alone.
        if (!ArenaServerClient.IsServerModKnownAvailable)
            return "The Arena server mod has not answered yet.";
        if (!ArenaServerClient.SessionFieldsPresent || ArenaServerClient.ServerMods == null)
            return "The installed ArenaMode server does not support Arena sessions.";
        if (ArenaServerClient.IsArenaInstance)
            return "The server already is an Arena session server.";
        if (ArenaSessionContract.GetRequestFilePath() == null)
            return "The local application data folder is not available.";
        string helper = HelperPath();
        if (!File.Exists(helper))
            return "The session helper is missing: " + helper;
        string server = Path.Combine(SptRuntimeDirectory(), "SPT.Server.exe");
        if (!File.Exists(server))
            return "No local SPT server was found at " + server;
        if (!BackendIsLocal())
            return "The game is not connected to a local server.";

        // The helper copies exactly these folders and accepts only folders that sit directly in the
        // user\mods of the SPT_Runtime beside this game (HelperRequest.TryParse): a server that runs from
        // elsewhere, or that reports no folder, cannot be switched.
        if (allowedServerMods.Count == 0)
            return "The server reported no mod that an Arena session could load.";
        string modsRoot = Path.Combine(Path.Combine(SptRuntimeDirectory(), HelperLayout.UserFolder), HelperLayout.ModsFolder);
        foreach (var mod in allowedServerMods)
        {
            if (string.IsNullOrEmpty(mod.File))
                return "The server did not report the folder of its mod " + mod.DisplayName + ".";
            if (!HelperPathSafety.IsDirectChild(mod.File, modsRoot))
                return "The server mod " + mod.DisplayName + " is not in " + modsRoot + ", so the session helper cannot copy it.";
        }

        return null;
    }

    private static bool BackendIsLocal()
    {
        try { return SPT.Common.Http.RequestHandler.IsLocal; }
        catch (Exception) { return false; }
    }

    internal static string HelperPath()
        => Path.Combine(Path.Combine(BepInEx.Paths.PluginPath, ArenaSessionContract.PluginFolderName), ArenaSessionContract.HelperFileName);

    internal static string SptRuntimeDirectory()
        => Path.Combine(BepInEx.Paths.GameRootPath, "SPT_Runtime");

    // ------------------------------------------------------------------ units

    private static bool HasFika()
    {
        foreach (var guid in Chainloader.PluginInfos.Keys)
            if (ArenaWhitelistPolicy.IsFikaGuid(guid)) return true;
        return false;
    }

    private static List<ArenaSessionUnit> ClientUnits()
    {
        var units = new List<ArenaSessionUnit>(Chainloader.PluginInfos.Count);
        foreach (var pair in Chainloader.PluginInfos)
        {
            var info = pair.Value;
            var metadata = info?.Metadata;
            if (metadata == null) continue;
            // Only HARD dependencies pull a mod into the whitelist: ArenaMode itself declares many soft ones.
            var hard = new List<string>();
            if (info.Dependencies != null)
            {
                foreach (var dependency in info.Dependencies)
                {
                    if (dependency != null && (dependency.Flags & BepInDependency.DependencyFlags.HardDependency) != 0
                        && !string.IsNullOrEmpty(dependency.DependencyGUID))
                        hard.Add(dependency.DependencyGUID);
                }
            }

            units.Add(new ArenaSessionUnit(metadata.GUID ?? pair.Key, metadata.Name, metadata.Version?.ToString(),
                info.Location, hard));
        }

        return units;
    }

    private static List<ArenaSessionUnit> ServerUnits(IReadOnlyList<ArenaServerModInfo> mods)
    {
        var units = new List<ArenaSessionUnit>(mods.Count);
        // The server's ModDependencies are hard by nature: its mod validator refuses to load any mod at
        // all when one listed dependency is missing, so the same closure as for client plugins applies.
        foreach (var mod in mods)
            units.Add(new ArenaSessionUnit(mod.Guid, mod.Name, mod.Version, mod.Folder, mod.Dependencies));
        return units;
    }

    // ------------------------------------------------------------------ policy files

    /// <summary>
    /// A normal session without a usable shipped whitelist refuses Arena (nothing else keeps unlisted
    /// or denied mods out of it) and offers no session; ordinary raids are untouched.
    /// </summary>
    private static string PolicyUnavailableRefusal(string problem)
        => "Arena is unavailable: its mod whitelist could not be loaded (" + problem
           + "). Repair the ArenaMode installation; ordinary PMC/Scav raids remain available.";

    private static ArenaWhitelistPolicy LoadPolicy(out string problem)
    {
        string defaultPath = Path.Combine(Path.Combine(BepInEx.Paths.PluginPath, ArenaSessionContract.PluginFolderName),
            ArenaSessionContract.DefaultPolicyFileName);
        string userPath = Path.Combine(BepInEx.Paths.ConfigPath, ArenaSessionContract.UserPolicyFileName);

        string defaultJson = ReadPolicyFile(defaultPath, "default whitelist", true, out problem);
        if (defaultJson == null)
        {
            LogOnce("policy:" + problem, true, "Arena whitelist unavailable: " + problem);
            return null;
        }

        // The user file is optional: absent is normal and silent; a defective one is reported by the
        // policy loader (it is ignored with a diagnostic), an unreadable one the same way here.
        string userJson = ReadPolicyFile(userPath, "user whitelist", false, out string userProblem);
        if (userProblem != null) LogOnce("policy:" + userProblem, true, "Arena whitelist: " + userProblem);

        if (!ArenaWhitelistPolicy.TryLoad(defaultJson, userJson, out var policy, out var diagnostics))
        {
            foreach (string diagnostic in diagnostics)
                LogOnce("policy:" + diagnostic, true, "Arena whitelist unavailable: " + diagnostic);
            problem = diagnostics.Count > 0 ? diagnostics[0] : "the default whitelist is invalid";
            return null;
        }

        foreach (string diagnostic in diagnostics)
            LogOnce("policy:" + diagnostic, true, "Arena whitelist: " + diagnostic);
        return policy;
    }

    private static string ReadPolicyFile(string path, string label, bool required, out string problem)
    {
        problem = null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                if (required) problem = label + " not found at " + path;
                return null;
            }

            if (info.Length > MaxPolicyFileBytes)
            {
                problem = label + " is larger than " + (MaxPolicyFileBytes / 1024) + " KB and was ignored";
                return null;
            }

            return File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            problem = label + " could not be read: " + ex.Message;
            return null;
        }
    }

    // ------------------------------------------------------------------ text

    /// <summary>
    /// The refusal: a lead sentence for the mode, then the mods apart by cause, then what to do.
    /// <c>Blocked for Arena</c> names the mods a deny list excludes (the shipped default's or the
    /// user's own), <c>Not on the whitelist</c> the mods nothing names (whitelist mode only: in
    /// blacklist mode, the user file says <c>"unlisted": "allow"</c>, a mod can only be excluded by a
    /// deny list, so the text does not claim a whitelist). A part without names is left out.
    /// </summary>
    private static string FormatRefusal(List<string> denied, List<string> unlisted, bool sessionOffered, bool blacklist)
    {
        var text = new StringBuilder(blacklist
            ? "Arena does not run with denied mods."
            : "Arena runs only with whitelisted mods.");
        if (denied.Count > 0) text.Append(" Blocked for Arena: ").Append(JoinLimited(denied, MaxNamesListed)).Append('.');
        if (unlisted.Count > 0) text.Append(" Not on the whitelist: ").Append(JoinLimited(unlisted, MaxNamesListed)).Append('.');
        return text.Append(sessionOffered
            ? " Start an Arena session to play Arena without them; the game restarts."
            : " Remove them and restart the game to play Arena.").ToString();
    }

    /// <summary>
    /// The notes line. The policy-override notes (one per side at most, client first as they were
    /// evaluated) come first and are never counted into the cap: they say that the user's own file lets
    /// unlisted mods through, which must not disappear behind "and N more" because a handful of version
    /// differences sort ahead of it. The remaining notes are sorted and capped at
    /// <see cref="MaxWarningsListed"/>.
    /// </summary>
    private static string FormatNotes(List<string> notes)
    {
        if (notes == null || notes.Count == 0) return null;

        var policyOverrides = new List<string>();
        var others = new List<string>();
        foreach (string note in notes)
        {
            if (!ArenaWhitelistDecision.IsPolicyOverrideWarning(note)) others.Add(note);
            else if (!policyOverrides.Contains(note)) policyOverrides.Add(note);
        }

        var parts = new List<string>(2);
        if (policyOverrides.Count > 0) parts.Add(string.Join("; ", policyOverrides.ToArray()));
        if (others.Count > 0) parts.Add(JoinLimited(others, MaxWarningsListed, "; "));
        return "Notes: " + string.Join("; ", parts);
    }

    /// <summary>
    /// Adds an excluded unit to the denied or the unlisted names, once per GUID. A unit is unlisted only
    /// when nothing names it; every other exclusion (<c>denied-by-user</c>, <c>denied</c>) is a deny.
    /// </summary>
    private static void AddExcluded(List<string> denied, List<string> unlisted, HashSet<string> listedGuids, ArenaSessionUnit unit)
    {
        if (!string.IsNullOrEmpty(unit.Guid) && !listedGuids.Add(unit.Guid)) return;
        if (string.Equals(unit.Reason, ArenaWhitelistDecision.ReasonNotOnWhitelist, StringComparison.Ordinal)) unlisted.Add(unit.DisplayName);
        else denied.Add(unit.DisplayName);
    }

    private static string JoinLimited(List<string> items, int limit, string separator = ", ")
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<string>();
        foreach (string item in items)
        {
            string text = string.IsNullOrWhiteSpace(item) ? "(unnamed)" : item.Trim();
            if (seen.Add(text)) unique.Add(text);
        }

        unique.Sort(StringComparer.OrdinalIgnoreCase);
        if (unique.Count <= limit) return string.Join(separator, unique);
        return string.Join(separator, unique.GetRange(0, limit)) + separator + "and "
            + (unique.Count - limit) + " more";
    }

    // ------------------------------------------------------------------ logging

    private static void LogNotesOnce(Outcome outcome)
    {
        foreach (string note in outcome.Notes)
            LogOnce("note:" + note, false, "Arena whitelist: " + note);
    }

    private static void LogOnce(string key, bool warning, string message)
    {
        if (!LoggedOnce.Add(key)) return;
        var log = Plugin.ModLogger;
        if (log == null) return;
        if (warning) log.LogWarning(message);
        else log.LogInfo(message);
    }
}
