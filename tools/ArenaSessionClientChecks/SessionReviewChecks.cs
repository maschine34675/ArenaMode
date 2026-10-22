using ArenaMode.Session;
using ArenaMode.SessionHelper;
using ArenaMode.Shared.Session;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

// The external review of 2026-10-22 (docs/ARENA-SESSION-REVIEW-RESPONSE-20261022.md) as far as it is the client's:
//   * an Arena session refuses Arena when the server runs a mod the current policy excludes, when the server's
//     policy hash is not the gate's, and when a patcher ran before the gate (spec section 7);
//   * preloader patchers are units of the normal-session decision;
//   * deny wins over hard dependencies, so a dependant of a denied mod is "Blocked for Arena";
//   * the game quits only after the helper acknowledged the request (spec sections 6 and 7).
// Everything is driven through the production sources; the Unity coroutine is run by hand (StandIns.cs).
internal static partial class Program
{
    private const string ReturnHint = "Return to the normal session and start the Arena session again.";

    private static string ReportWith(string extraMembers)
        => ReportActive.Replace("\"patchersNeutralised\":[\"other.dll\"]}", "\"patchersNeutralised\":[\"other.dll\"]" + extraMembers + "}");

    private static int RefusedWarnings(string needle)
        => ArenaMode.Plugin.ModLogger.Warnings.Count(w => w.StartsWith("Arena session refused:", StringComparison.Ordinal) && w.Contains(needle));

    // ------------------------------------------------------------------ Arena session

    private static void CheckArenaSessionReview()
    {
        // (c) A not-allowed patcher that ran before the gate: the gate switched off what was left of it, but the
        // process is not the clean one the session promises.
        const string earlyRefusal = "The preloader patcher Early.Patcher.dll ran before the Arena gate could stop it. "
            + "Allow it in the policy file or remove it, then start the Arena session again.";
        ResetWorld();
        ArenaSession(ReportWith(",\"patchersRanBeforeGate\":[\"Early.Patcher.dll\"]"));
        Ping(true, NormalServerMods());
        Equal(Reason(), earlyRefusal, "A patcher that ran before the gate refuses Arena in the session");
        Require(RefusedWarnings("Early.Patcher.dll") == 1, "The patcher refusal is logged once, as a Warning");
        Ping(true, NormalServerMods());
        _ = Reason();
        Require(RefusedWarnings("Early.Patcher.dll") == 1, "A new ping does not log the patcher refusal again");

        ResetWorld();
        ArenaSession(ReportWith(",\"patchersRanBeforeGate\":[\"Early.Patcher.dll\"]"));
        Equal(Reason(), earlyRefusal, "The patcher refusal does not wait for the server's ping");

        ResetWorld();
        ArenaSession(ReportWith(",\"patchersRanBeforeGate\":[\"b.dll\",\"A.dll\",\"b.dll\"]"));
        Ping(true, NormalServerMods());
        Equal(Reason(),
            "The preloader patchers A.dll, b.dll ran before the Arena gate could stop them. "
            + "Allow them in the policy file or remove them, then start the Arena session again.",
            "Several patchers are listed once each, sorted, in the plural");

        ResetWorld();
        ArenaSession(ReportWith(",\"patchersRanBeforeGate\":[" + string.Join(",", Enumerable.Range(0, 14).Select(i => "\"P" + i.ToString("D2") + ".dll\"")) + "]"));
        Ping(true, NormalServerMods());
        Require(Reason().Contains("P00.dll, P01.dll") && Reason().Contains("P11.dll, and 2 more ran"),
            "Twelve patcher names are listed, the rest is counted");

        ResetWorld();
        ArenaSession(ReportWith(",\"patchersRanBeforeGate\":[]"));
        Ping(true, NormalServerMods());
        Require(Reason() == null, "A gate that reports no patcher that ran early admits");
        ArenaSession(ReportActive);
        Ping(true, NormalServerMods());
        Require(Reason() == null, "A report without the member (an older gate) reads as none");

        ResetWorld();
        ArenaSessionMode.Initialize(new[] { "game.exe", "-arena-session" },
            "{\"schema\":1,\"active\":false,\"error\":\"boom\",\"patchersRanBeforeGate\":[\"Early.Patcher.dll\"]}");
        Require(Reason().Contains("preloader gate is inactive: boom") && !Reason().Contains("Early.Patcher"),
            "An inactive gate keeps its own refusal ahead of the patcher one");

        // (a) The server runs a mod the policy as it reads now excludes: the policy changed during the switch.
        ResetWorld();
        ArenaSession(ReportActive);
        var intruder = NormalServerMods();
        intruder.Add(Mod("com.example.intruder", "Intruder", "1.0"));
        Ping(true, intruder);
        const string intruderRefusal = "The Arena session server runs Intruder, which the current policy excludes: "
            + "the policy changed during the switch. " + ReturnHint;
        Equal(Reason(), intruderRefusal, "A server mod the policy does not name refuses Arena in the session");
        Require(RefusedWarnings("Intruder") == 1, "The server-mod refusal is logged once, as a Warning");
        Require(ArenaWhitelistAdmission.WarningLine == null, "A refused session shows no notes");
        Ping(true, intruder);
        _ = Reason();
        Require(RefusedWarnings("Intruder") == 1, "A new ping does not log the server-mod refusal again");

        // Every kind of exclusion counts, the names are sorted and listed once.
        ResetWorld();
        WriteUserPolicy("{\"schema\":1,\"deny\":[\"com.example.zed\"]}");
        ArenaSession(ReportActive);
        var excluded = NormalServerMods();
        excluded.Add(Mod("com.chazut.orbit", "ORBIT Server", "1.0"));
        excluded.Add(Mod("com.example.zed", "zed", "1"));
        excluded.Add(Mod("com.example.alpha", "Alpha", "1"));
        Ping(true, excluded);
        Require(Reason().StartsWith("The Arena session server runs Alpha, ORBIT Server, zed, which the current policy excludes", StringComparison.Ordinal),
            "A default-denied, a user-denied and an unlisted server mod all refuse, sorted");

        ResetWorld();
        ArenaSession(ReportActive);
        var many = NormalServerMods();
        for (int i = 0; i < 14; i++) many.Add(Mod("com.example.m" + i.ToString("D2"), "Mod" + i.ToString("D2"), "1.0"));
        Ping(true, many);
        Require(Reason().Contains("runs Mod00, Mod01") && Reason().Contains("Mod11, and 2 more, which the current policy excludes"),
            "Twelve server mod names are listed, the rest is counted");

        // The policy change itself: allowed when the request was made, gone from the user file by the restart.
        ResetWorld();
        WriteUserPolicy("{\"schema\":1,\"allow\":[{\"guid\":\"com.example.servermod\",\"name\":\"Server Thing\",\"kind\":\"mod\"}]}");
        ArenaSession(ReportActive);
        var allowedByUser = NormalServerMods();
        allowedByUser.Add(Mod("com.example.servermod", "Server Thing", "3.0"));
        Ping(true, allowedByUser);
        Require(Reason() == null, "A server mod the user allowed is admitted while the policy still allows it");
        File.Delete(_userPolicyPath);
        Ping(true, allowedByUser);
        Require(Reason() != null && Reason().Contains("runs Server Thing, which the current policy excludes"),
            "The same server fails once the user file no longer allows it");

        // A blacklist excludes only what it denies.
        ResetWorld();
        WriteUserPolicy("{\"schema\":1,\"unlisted\":\"allow\",\"deny\":[\"com.example.deniedserver\"]}");
        ArenaSession(ReportActive);
        var blacklisted = NormalServerMods();
        blacklisted.Add(Mod("com.example.servermod", "Server Thing", "3.0"));
        Ping(true, blacklisted);
        Require(Reason() == null, "Under a blacklist an unlisted server mod is admitted");
        blacklisted.Add(Mod("com.example.deniedserver", "Denied Thing", "1.0"));
        Ping(true, blacklisted);
        Require(Reason() != null && Reason().Contains("runs Denied Thing, which"), "Under a blacklist a denied server mod refuses");

        // Deny wins over a dependency: a server mod that needs a denied library is excluded with it.
        ResetWorld();
        WriteUserPolicy("{\"schema\":1,\"allow\":[{\"guid\":\"com.example.content\"}],\"deny\":[\"com.example.commonlib\"]}");
        ArenaSession(ReportActive);
        var needy = NormalServerMods();
        needy.Add(Mod("com.example.content", "Content Pack", "1.0", "com.example.commonlib"));
        needy.Add(Mod("com.example.commonlib", "Common Lib", "3.0.6"));
        Ping(true, needy);
        Require(Reason() != null && Reason().Contains("runs Common Lib, Content Pack, which the current policy excludes"),
            "A server mod that needs a denied library is refused together with it");

        // A library that an allowed server mod needs is part of the session tree (pulled in): no exclusion, only a note.
        ResetWorld();
        WriteUserPolicy("{\"schema\":1,\"allow\":[{\"guid\":\"com.example.content\"}]}");
        ArenaSession(ReportActive);
        var withLibrary = NormalServerMods();
        withLibrary.Add(Mod("com.example.content", "Content Pack", "1.0", "com.example.commonlib"));
        withLibrary.Add(Mod("com.example.commonlib", "Common Lib", "3.0.6"));
        Ping(true, withLibrary);
        Require(Reason() == null && ArenaWhitelistAdmission.WarningLine.Contains("Common Lib: pulled in as hard dependency of Content Pack"),
            "The library an allowed server mod needs is part of the session, not an exclusion");

        // The server's mods are not known yet (the ping has not arrived): nothing to compare, the click asks again.
        ResetWorld();
        ArenaSession(ReportActive);
        Require(Reason() == null, "Without a ping there are no server mods to refuse");

        // (b) The policy hash the server was built for is not the one the gate applied.
        const string hashRefusal = "The Arena session server was built for a different policy than this game applies: "
            + "the policy changed during the switch. " + ReturnHint;
        ResetWorld();
        ArenaSession(ReportActive); // the gate's policy hash is abc123
        Ping(true, NormalServerMods(), "def456");
        Equal(Reason(), hashRefusal, "A server built for another policy hash refuses Arena in the session");
        Require(ArenaMode.Plugin.ModLogger.Warnings.Count(w => w.StartsWith("Arena session refused:", StringComparison.Ordinal)
                && w.Contains("def456") && w.Contains("abc123")) == 1,
            "The refusal is logged once, as a Warning, with both hashes");
        Ping(true, NormalServerMods(), "def456");
        _ = Reason();
        Require(ArenaMode.Plugin.ModLogger.Warnings.Count(w => w.Contains("def456")) == 1, "A new ping does not log the hash refusal again");

        Ping(true, NormalServerMods(), "abc123");
        Require(Reason() == null, "The same policy hash admits");
        Ping(true, NormalServerMods(), "ABC123");
        Equal(Reason(), hashRefusal, "The hashes are compared exactly, not ignoring case");
        Ping(true, NormalServerMods(), null);
        Require(Reason() == null, "A server that reports no hash is unknown, not different");
        Ping(true, NormalServerMods(), "  ");
        Require(Reason() == null, "A blank hash is unknown too");

        ResetWorld();
        ArenaSession(ReportActive.Replace("\"policyHash\":\"abc123\"", "\"policyHash\":\"\""));
        Ping(true, NormalServerMods(), "def456");
        Require(Reason() == null, "A gate report without a hash leaves nothing to compare");

        // Both at once: the refusal that names the mods comes first.
        ResetWorld();
        ArenaSession(ReportActive);
        Ping(true, intruder, "def456");
        Equal(Reason(), intruderRefusal, "The refusal that names the server mods wins over the hash one");

        // A normal session never compares: the hash belongs to the Arena instance, and a normal session has no gate.
        ResetWorld();
        Ping(false, NormalServerMods(), "def456");
        Require(Reason() == null, "A normal session ignores the ping's policy hash");
    }

    // ------------------------------------------------------------------ patcher units

    private static void CheckPatcherUnits()
    {
        const string offer = "Start an Arena session to play Arena without them; the game restarts.";
        const string remove = "Remove them and restart the game to play Arena.";

        ResetWorld();
        Ping(false, NormalServerMods());
        Require(Reason() == null && ArenaWhitelistAdmission.WarningLine == null,
            "The patchers of the development hosts are all on the list: the platform ones, LogTools and the WTT patcher");

        // A patcher the policy does not name blocks a direct start. Nested folders count, like the gate's list.
        ResetWorld();
        AddPatcher(Path.Combine("Evil", "Evil.Patcher.dll"));
        Ping(false, NormalServerMods());
        Equal(Reason(), "Arena runs only with whitelisted mods. Not on the whitelist: Evil.Patcher.dll. " + offer,
            "An unlisted preloader patcher refuses Arena in a normal session and is named");
        Require(ArenaWhitelistAdmission.CanOfferSessionStart(Reason()), "The session action stays available: the session neutralises the patcher");
        Require(ArenaWhitelistAdmission.TryPrepareSessionStart(out _, out _, out string why) && why == null,
            "A session can be started from there");
        OldServerPing();
        Equal(Reason(), "Arena runs only with whitelisted mods. Not on the whitelist: Evil.Patcher.dll. " + remove,
            "Without a session-capable server the refusal asks to remove it");

        ResetWorld();
        AddPatcher(Path.Combine("a", "b", "c", "Deep.Patcher.dll"));
        AddPatcher("Top.Patcher.dll");
        Ping(false, NormalServerMods());
        Require(Reason().Contains("Not on the whitelist: Deep.Patcher.dll, Top.Patcher.dll."),
            "Patchers are found at any depth below BepInEx\\patchers, sorted");

        // Matched by file name, ignoring case; the same name in two folders is one patcher; other files are no patchers.
        ResetWorld();
        AddPatcher(Path.Combine("Upper", "SPT-PREPATCH.DLL"));
        AddPatcher(Path.Combine("copy", "maschine-LogTools.dll"));
        AddPatcher("notes.txt");
        AddPatcher(Path.Combine("Evil", "Twin.Patcher.dll"));
        AddPatcher(Path.Combine("Other", "Twin.Patcher.dll"));
        Ping(false, NormalServerMods());
        Equal(Reason(), "Arena runs only with whitelisted mods. Not on the whitelist: Twin.Patcher.dll. " + offer,
            "Patchers are matched by file name, case-insensitively, once per name, and only *.dll files count");

        // Denied patchers are told apart from unlisted ones.
        ResetWorld();
        AddPatcher("Evil.Patcher.dll");
        WriteUserPolicy("{\"schema\":1,\"deny\":[\"maschine-LogTools.dll\"]}");
        Ping(false, NormalServerMods());
        Equal(Reason(),
            "Arena runs only with whitelisted mods. Blocked for Arena: maschine-LogTools.dll. Not on the whitelist: Evil.Patcher.dll. " + offer,
            "A patcher the user denied is blocked, an unlisted one is not on the whitelist");

        // The user's allow list brings a patcher back.
        ResetWorld();
        AddPatcher("Evil.Patcher.dll");
        WriteUserPolicy("{\"schema\":1,\"allow\":[{\"patcher\":\"Evil.Patcher.dll\",\"name\":\"Evil\",\"kind\":\"mod\"}]}");
        Ping(false, NormalServerMods());
        Require(Reason() == null, "A patcher the user allowed is admitted");

        // A blacklist lets unlisted patchers through and refuses the ones it denies.
        ResetWorld();
        AddPatcher("Evil.Patcher.dll");
        AddPatcher("Worse.Patcher.dll");
        WriteUserPolicy("{\"schema\":1,\"unlisted\":\"allow\",\"deny\":[\"Worse.Patcher.dll\"]}");
        Ping(false, NormalServerMods());
        Equal(Reason(), "Arena does not run with denied mods. Blocked for Arena: Worse.Patcher.dll. " + offer,
            "Under a blacklist only the denied patcher refuses");

        // Plugins and patchers share the two parts of the refusal.
        ResetWorld();
        AddPatcher("Evil.Patcher.dll");
        AddPlugin("com.example.zombies", "Zombie Pack", "1.0.0");
        Ping(false, NormalServerMods());
        Equal(Reason(), "Arena runs only with whitelisted mods. Not on the whitelist: Evil.Patcher.dll, Zombie Pack. " + offer,
            "An excluded patcher and an excluded plugin are named together");

        // The patcher alone asks the server for its ping when the screen opens, like an excluded plugin.
        ResetWorld();
        ArenaServerClient.OnRefresh = () => Ping(false, NormalServerMods());
        AddPatcher("Evil.Patcher.dll");
        ArenaServerClient.PingGeneration++;
        ArenaWhitelistAdmission.PrepareForScreen();
        Require(ArenaServerClient.RefreshCalls == 1 && ArenaWhitelistAdmission.CanOfferSessionStart(Reason()),
            "An excluded patcher alone asks for the ping, so the session is offered on the first display");

        // A library that sits in the patchers folder without being a patcher is no unit: the gate leaves it alone, and it
        // must not block a direct start (the only way out would be an allow entry for a file that is not a mod).
        ResetWorld();
        AddLibrary(Path.Combine("Evil", "Support.Library.dll"));
        Ping(false, NormalServerMods());
        Require(Reason() == null && ArenaWhitelistAdmission.WarningLine == null,
            "A library in the patchers folder is not a patcher and does not block a direct start");

        ResetWorld();
        AddLibrary("Support.Library.dll");
        AddPatcher("Evil.Patcher.dll");
        Ping(false, NormalServerMods());
        Equal(Reason(), "Arena runs only with whitelisted mods. Not on the whitelist: Evil.Patcher.dll. " + offer,
            "Only the patcher is named, not the library beside it");

        // The same name in two folders: a library copy does not hide the patcher copy.
        ResetWorld();
        AddLibrary(Path.Combine("a", "Twin.dll"));
        AddPatcher(Path.Combine("b", "Twin.dll"));
        Ping(false, NormalServerMods());
        Equal(Reason(), "Arena runs only with whitelisted mods. Not on the whitelist: Twin.dll. " + offer,
            "A library copy of a file name does not hide the patcher copy of it");

        // The member name is found wherever it sits in a large file, also where two reads of the file meet.
        foreach (int offset in new[] { 0, 1, 65530, 65536, 65541, 65546, 65550, 65555, 131072, 131078, 140000 })
        {
            ResetWorld();
            AddBinaryDll("Big.Patcher.dll", 150_000, offset);
            Ping(false, NormalServerMods());
            Equal(Reason(), "Arena runs only with whitelisted mods. Not on the whitelist: Big.Patcher.dll. " + offer,
                "The patcher member name at byte " + offset + " of a large file marks a patcher");
        }

        ResetWorld();
        AddBinaryDll("Big.Library.dll", 150_000, -1);
        Ping(false, NormalServerMods());
        Require(Reason() == null, "A large file without the member name is a library");

        // A file that cannot be read cannot be shown to be a library: it counts as a patcher.
        ResetWorld();
        AddLibrary("Locked.Library.dll");
        using (new FileStream(Path.Combine(Paths.PatcherPluginPath, "Locked.Library.dll"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Ping(false, NormalServerMods());
            Equal(Reason(), "Arena runs only with whitelisted mods. Not on the whitelist: Locked.Library.dll. " + offer,
                "A file that cannot be read counts as a patcher");
        }

        // No patchers folder, no patchers, nothing to refuse.
        ResetWorld();
        Directory.Delete(Paths.PatcherPluginPath, true);
        Ping(false, NormalServerMods());
        Require(Reason() == null, "A missing patchers folder is no refusal");

        // With Fika the whitelist stands aside, patchers included.
        ResetWorld();
        AddPlugin("com.fika.core", "Fika.Core", "1.0.0");
        AddPatcher("Evil.Patcher.dll");
        Ping(false, NormalServerMods());
        Require(Reason() == null, "With Fika the patchers are not judged either");
    }

    // ------------------------------------------------------------------ deny wins over hard dependencies

    private static void CheckDenyWinsInTheRefusal()
    {
        const string offer = "Start an Arena session to play Arena without them; the game restarts.";

        // A client mod the user allowed needs a mod the default list denies: it is excluded with it, as blocked.
        ResetWorld();
        AddPlugin("com.chazut.orbit", "ORBIT", "1.0.0");
        AddPlugin("com.example.foreign", "Foreign Pack", "1.0.0", hard: new[] { "com.chazut.orbit" });
        WriteUserPolicy("{\"schema\":1,\"allow\":[{\"guid\":\"com.example.foreign\",\"name\":\"Foreign Pack\",\"kind\":\"mod\"}]}");
        Ping(false, NormalServerMods());
        Equal(Reason(), "Arena runs only with whitelisted mods. Blocked for Arena: Foreign Pack, ORBIT. " + offer,
            "A mod that needs a denied mod is listed under Blocked for Arena with it, not under Not on the whitelist");
        Equal(ArenaWhitelistAdmission.WarningLine, "Notes: Foreign Pack: excluded because it requires ORBIT (denied by the default list)",
            "The note names the dependency and the list that denies it");

        // The user's own deny is named as such.
        ResetWorld();
        AddPlugin("com.example.lib", "Example Lib", "1.0.0");
        AddPlugin("com.example.foreign", "Foreign Pack", "1.0.0", hard: new[] { "com.example.lib" });
        WriteUserPolicy("{\"schema\":1,\"allow\":[{\"guid\":\"com.example.foreign\"}],\"deny\":[\"com.example.lib\"]}");
        Ping(false, NormalServerMods());
        Equal(Reason(), "Arena runs only with whitelisted mods. Blocked for Arena: Example Lib, Foreign Pack. " + offer,
            "A mod that needs a library the user denied is blocked with it");
        Equal(ArenaWhitelistAdmission.WarningLine, "Notes: Foreign Pack: excluded because it requires Example Lib (denied by the user's policy file)",
            "The note names the user's policy file");

        // The session tree gets neither of them, but the session can still be started.
        Require(ArenaWhitelistAdmission.CanOfferSessionStart(Reason()), "The session is offered for a mod that needs a denied one");

        // Allowing the denied mod lifts the block for the whole chain.
        ResetWorld();
        AddPlugin("com.chazut.orbit", "ORBIT", "1.0.0");
        AddPlugin("com.example.foreign", "Foreign Pack", "1.0.0", hard: new[] { "com.chazut.orbit" });
        WriteUserPolicy("{\"schema\":1,\"allow\":[{\"guid\":\"com.example.foreign\"},{\"guid\":\"com.chazut.orbit\"}]}");
        Ping(false, NormalServerMods());
        Require(Reason() == null && ArenaWhitelistAdmission.WarningLine == null,
            "A user allow of the denied mod lifts the block for the mod that needs it");
    }

    // ------------------------------------------------------------------ the helper's acknowledgement

    private static string AcknowledgementPath() => HelperAcknowledgement.GetPath(_requestPath);

    /// <summary>What the helper does once it took the request over: it leaves its acknowledgement beside the request.</summary>
    private static void WriteAcknowledgement(int pid, string content = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_requestPath));
        File.WriteAllText(AcknowledgementPath(), content ?? HelperAcknowledgement.BuildContent(pid, DateTime.UtcNow));
    }

    /// <summary>
    /// One frame of the production wait: the clock moves on and the coroutine the switch started on the plugin
    /// runs once. False when the wait is over (accepted, failed or timed out).
    /// </summary>
    private static bool Frame(float seconds)
    {
        _clock += seconds;
        var routines = ArenaMode.Plugin.Instance.Coroutines;
        Require(routines.Count > 0, "The switch started its wait as a coroutine on the plugin");
        return routines[routines.Count - 1].MoveNext();
    }

    /// <summary>The helper acknowledges and the game's next look at it ends the wait.</summary>
    private static void AcceptAndQuit(int helperPid)
    {
        WriteAcknowledgement(helperPid);
        Require(!Frame(ArenaHelperHandshake.PollIntervalSeconds), "The wait ends at the first look after the acknowledgement");
    }

    private static void CheckHelperHandshake()
    {
        // The request returns at once with the helper only started; the game neither quits nor blocks.
        ReadyToEnter();
        string failure = null;
        int failures = 0;
        Require(ArenaSessionSwitch.Request("enter", null, out string error, m => { failure = m; failures++; }) && error == null,
            "The request returns as soon as the helper has started");
        Require(Application.QuitCalls == 0 && ArenaSessionSwitch.HelperPending && !ArenaSessionSwitch.RestartRequested,
            "A running helper that has not acknowledged yet does not close the game");
        Require(ArenaMode.Plugin.Instance.Coroutines.Count == 1, "The wait is one coroutine on the plugin, not a blocking loop");
        Require(ArenaMode.Plugin.ModLogger.Info.Count(l => l.StartsWith("Arena session:")) == 0,
            "Nothing is logged at Info before the helper accepted");

        // Ten seconds of frames without an acknowledgement: the game keeps running.
        for (int i = 0; i < 40; i++) Require(Frame(0.25f), "The wait goes on while the helper is silent");
        Require(Application.QuitCalls == 0 && failure == null && ArenaSessionSwitch.HelperPending, "Ten silent seconds are no failure yet");

        // The acknowledgement arrives: the game quits, once.
        AcceptAndQuit(HelperPid);
        Require(Application.QuitCalls == 1 && ArenaSessionSwitch.RestartRequested && !ArenaSessionSwitch.HelperPending && failures == 0,
            "The acknowledgement closes the game");
        Require(!Frame(1f) && Application.QuitCalls == 1, "Nothing quits the game a second time");

        // Silence for fifteen seconds: the game stays, the player is told, the warning is logged, a retry works.
        ReadyToEnter();
        failure = null;
        failures = 0;
        Require(ArenaSessionSwitch.Request("enter", null, out error, m => { failure = m; failures++; }), "The request starts");
        // Frames that do not fall on the quarter-second grid of the looks, so that the deadline lies between two looks.
        Require(Frame(0.3125f), "The first look comes after a quarter second: nothing yet");
        for (int i = 0; i < 58; i++) Require(Frame(0.25f), "Fourteen and eleven sixteenths of a second: still waiting");
        Require(Frame(0.125f), "Fourteen seconds and 15 sixteenths: still waiting");
        Require(failure == null && Application.QuitCalls == 0 && ArenaSessionSwitch.HelperPending, "Not before fifteen seconds");
        Require(!Frame(0.0625f), "At fifteen seconds the wait is over, even between two looks");
        Require(failures == 1 && failure.Contains("did not accept the request within 15 seconds") && failure.Contains("keeps running")
                && failure.Contains("helper.log"),
            "The player is told that the helper stayed silent, that the game keeps running and where the log is");
        Require(Application.QuitCalls == 0 && !ArenaSessionSwitch.RestartRequested && !ArenaSessionSwitch.HelperPending,
            "A silent helper leaves the game running and the switch unlatched");
        Require(!File.Exists(_requestPath), "A request that no helper took over is withdrawn when the wait times out");
        Require(ArenaMode.Plugin.ModLogger.Warnings.Count(w => w.Contains("not completed") && w.Contains("15 seconds")) == 1,
            "The failure is logged once as a Warning");
        Require(ArenaMode.Plugin.ModLogger.Info.Count(l => l.StartsWith("Arena session:")) == 0, "No Info line for a switch that did not happen");
        WriteAcknowledgement(HelperPid);
        Require(!Frame(1f) && Application.QuitCalls == 0 && failures == 1,
            "An acknowledgement that comes after the player was told is not acted on, and the failure is reported once");
        Require(ArenaSessionSwitch.Request("enter", null, out error) && Launch.Calls == 2, "After a failed wait the player can try again");

        // The helper stops before it acknowledged.
        ReadyToEnter();
        failure = null;
        failures = 0;
        Require(ArenaSessionSwitch.Request("enter", null, out error, m => { failure = m; failures++; }), "The request starts");
        for (int i = 0; i < 4; i++) Require(Frame(0.25f), "Still waiting");
        ExitedHelpers.Add(HelperPid);
        Require(!Frame(0.25f), "A helper that is gone ends the wait at the next look");
        Require(failures == 1 && failure.Contains("stopped before it accepted the request") && failure.Contains("keeps running")
                && failure.Contains("helper.log"),
            "The player is told that the helper stopped, that the game keeps running and where the log is");
        Require(Application.QuitCalls == 0 && !ArenaSessionSwitch.HelperPending, "A helper that stopped leaves the game running");
        Require(ArenaMode.Plugin.ModLogger.Warnings.Count(w => w.Contains("not completed") && w.Contains("stopped before")) == 1,
            "The helper's end is logged once as a Warning");
        Require(!File.Exists(_requestPath),
            "A request is taken back when the helper stopped: a helper that read it earlier steps aside instead of switching later");

        // An acknowledgement from a helper that is no longer there counts for nothing.
        ReadyToEnter();
        failure = null;
        Require(ArenaSessionSwitch.Request("enter", null, out error, m => failure = m), "The request starts");
        WriteAcknowledgement(HelperPid);
        ExitedHelpers.Add(HelperPid);
        Require(!Frame(0.25f) && Application.QuitCalls == 0 && failure != null && failure.Contains("stopped before"),
            "A helper that acknowledged and then vanished cannot switch anything, so the game stays");
        Require(!File.Exists(_requestPath), "Its request is taken back as well");

        // A file from another process id is not this helper's word (a stale file, another helper).
        ReadyToEnter();
        failure = null;
        Require(ArenaSessionSwitch.Request("enter", null, out error, m => failure = m), "The request starts");
        WriteAcknowledgement(HelperPid + 1);
        for (int i = 0; i < 8; i++) Require(Frame(0.25f), "Another helper's acknowledgement is ignored");
        Require(Application.QuitCalls == 0, "The game does not quit for another process's acknowledgement");
        WriteAcknowledgement(HelperPid);
        Require(!Frame(0.25f) && Application.QuitCalls == 1, "Its own acknowledgement then closes the game");

        // Content that is not an acknowledgement is ignored.
        ReadyToEnter();
        Require(ArenaSessionSwitch.Request("enter", null, out error, m => failure = m), "The request starts");
        foreach (string content in new[]
        {
            "{ not json",
            "{\"schema\":2,\"helperPid\":" + HelperPid + ",\"acceptedUtc\":\"2026-10-22T12:00:00.000Z\"}",
            "{\"schema\":1,\"helperPid\":" + HelperPid + "}",
            "{\"schema\":1,\"helperPid\":0,\"acceptedUtc\":\"2026-10-22T12:00:00.000Z\"}",
            "",
            HelperAcknowledgement.BuildContent(HelperPid, DateTime.UtcNow) + new string(' ', 5000),
        })
        {
            WriteAcknowledgement(HelperPid, content);
            Require(Frame(0.25f) && Application.QuitCalls == 0, "Not an acknowledgement: " + (content.Length > 60 ? content.Substring(0, 60) : content));
        }

        WriteAcknowledgement(HelperPid);
        Require(!Frame(0.25f) && Application.QuitCalls == 1, "The real content closes the game");

        // The look happens every quarter second and not more often.
        ReadyToEnter();
        Require(ArenaSessionSwitch.Request("enter", null, out error), "The request starts");
        Require(_exitChecks == 0, "The request itself and the first frame the coroutine gets inside it do not look at the helper");
        Require(Frame(0.0625f) && Frame(0.0625f) && Frame(0.0625f) && _exitChecks == 0, "Three frames inside a quarter second look at nothing");
        Require(Frame(0.125f) && _exitChecks == 1, "The first look comes a quarter second after the helper started");
        Require(Frame(0.125f) && Frame(0.0625f) && _exitChecks == 1, "The next frames inside the next quarter second look at nothing");
        Require(Frame(0.0625f) && _exitChecks == 2, "The second look comes a quarter second after the first");
        for (int i = 0; i < 8; i++) Frame(0.25f);
        Require(_exitChecks == 10, "A look every quarter second and no other");

        // A stale acknowledgement is deleted before the helper starts and cannot be taken for the new one.
        ReadyToEnter();
        WriteAcknowledgement(HelperPid);
        Require(ArenaSessionSwitch.Request("enter", null, out error), "The request starts with a stale acknowledgement lying there");
        Require(!Launch.AcknowledgementExistedAtLaunch && !File.Exists(AcknowledgementPath()),
            "A stale acknowledgement is deleted before the helper is started");
        for (int i = 0; i < 8; i++) Frame(0.25f);
        Require(Application.QuitCalls == 0, "A stale acknowledgement, even with the new helper's pid, does not close the game");

        // A second request while the helper has not answered starts no second helper.
        ReadyToEnter();
        Require(ArenaSessionSwitch.Request("enter", null, out error), "The request starts");
        Require(!ArenaSessionSwitch.Request("enter", null, out error) && error.Contains("still starting") && Launch.Calls == 1,
            "A second request while the helper is starting is refused and starts no second helper");
        Require(!ArenaSessionSwitch.Request("leave", null, out error) && Launch.Calls == 1, "So is a request in the other direction");

        // The failure notice can fail or the look can throw without taking the game down.
        ReadyToEnter();
        Require(ArenaSessionSwitch.Request("enter", null, out error, m => throw new InvalidOperationException("notice broke")), "The request starts");
        ExitedHelpers.Add(HelperPid);
        Require(!Frame(0.25f) && Application.QuitCalls == 0
                && ArenaMode.Plugin.ModLogger.Warnings.Any(w => w.Contains("failure notice failed") && w.Contains("notice broke")),
            "A failure callback that throws is caught and logged");

        ReadyToEnter();
        failure = null;
        Require(ArenaSessionSwitch.Request("enter", null, out error, m => failure = m), "The request starts");
        ArenaSessionSwitch.HelperHasExited = pid => throw new InvalidOperationException("process table unreadable");
        Require(!Frame(0.25f) && Application.QuitCalls == 0 && failure != null && failure.Contains("failed while it waited")
                && failure.Contains("process table unreadable"),
            "A look that throws ends the wait with a failure and never closes the game");
        Require(!File.Exists(_requestPath), "A wait that failed takes its request back too: nobody may switch the session behind the player's back");

        // The wait needs the plugin: without it the helper is not even started.
        ReadyToEnter();
        ArenaMode.Plugin.Instance = null;
        Require(!ArenaSessionSwitch.Request("enter", null, out error) && error.Contains("not running") && Launch.Calls == 0
                && Application.QuitCalls == 0 && !ArenaSessionSwitch.HelperPending && !File.Exists(_requestPath),
            "Without the plugin object there is nothing to wait on: no request file, no helper, no quit");

        // A coroutine that cannot be started after the helper started leaves the game running and says so.
        ReadyToEnter();
        ArenaMode.Plugin.Instance.ThrowOnStart = true;
        Require(!ArenaSessionSwitch.Request("enter", null, out error) && error.Contains("could not wait") && Launch.Calls == 1
                && Application.QuitCalls == 0 && !ArenaSessionSwitch.HelperPending,
            "A wait that cannot be scheduled keeps the game running and is reported");
        Require(!File.Exists(_requestPath),
            "The helper that was started is already running, so the request is taken back: it cannot switch the session while the game stays");

        // The production question "is the helper process gone?" (every scenario above replaced it with a double).
        Require(!_realHelperHasExited(Environment.ProcessId), "A running process is not gone");
        Require(_realHelperHasExited(int.MaxValue), "A process id nobody has counts as a helper that is gone");
        using (var finished = System.Diagnostics.Process.Start(
                   new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c exit 0") { CreateNoWindow = true, UseShellExecute = false }))
        {
            int finishedPid = finished.Id;
            finished.WaitForExit();
            Require(_realHelperHasExited(finishedPid), "A process that has finished is gone");
        }
    }
}
