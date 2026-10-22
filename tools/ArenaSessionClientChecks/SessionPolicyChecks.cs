using ArenaMode.Session;
using ArenaMode.SessionHelper;
using ArenaMode.Shared.Session;
using BepInEx;
using BepInEx.Bootstrap;

// The third pass over the Arena session's own checks (docs/ARENA-SESSION-20261021.md, section 7, checks 2 and 4;
// section 11, last entry). The gate-policy review found that an Arena session admitted when the policy could not be
// loaded (every comparison with the policy was skipped), and the same trust gap sits in the plugin list: the gate
// filtered with the policy files of the moment the game started, but nothing compared the plugins that are running
// with the policy as it reads now.
//   * no usable policy: the session refuses with the normal session's wording plus the way back;
//   * a loaded plugin the current policy excludes: the session refuses, as for a server mod, and needs no ping.
// Everything is driven through the production sources (StandIns.cs).
internal static partial class Program
{
    private const string EarlyPatcherRefusal = "The preloader patcher Early.Patcher.dll ran before the Arena gate could stop it. "
        + "Allow it in the policy file or remove it, then start the Arena session again.";

    private static string PolicyRefusal(string problem)
        => "Arena is unavailable: its mod whitelist could not be loaded (" + problem
           + "). Repair the ArenaMode installation; ordinary PMC/Scav raids remain available. Return to the normal session.";

    // ------------------------------------------------------------------ no usable policy

    private static void CheckArenaSessionPolicyAvailable()
    {
        // The session used to admit: the server's mods were evaluated only "if the policy loaded", the plugins not at all.
        ResetWorld();
        File.Delete(_policyPath);
        ArenaSession(ReportActive);
        Ping(true, NormalServerMods(), "abc123");
        string missing = PolicyRefusal("default whitelist not found at " + _policyPath);
        Equal(Reason(), missing, "A session whose policy cannot be loaded refuses Arena, with the way back");
        Require(RefusedWarnings("mod whitelist could not be loaded") == 1, "The policy refusal is logged once, as a Warning");
        Ping(true, NormalServerMods(), "abc123");
        _ = Reason();
        Require(RefusedWarnings("mod whitelist could not be loaded") == 1, "A new ping does not log the policy refusal again");
        Require(ArenaWhitelistAdmission.WarningLine == null && !ArenaWhitelistAdmission.CanOfferSessionStart(Reason()),
            "A refused session shows no notes and offers no session start");

        // The ping is not needed to know: the click that has not got an answer yet is refused too.
        ResetWorld();
        File.Delete(_policyPath);
        ArenaSession(ReportActive);
        Equal(Reason(), missing, "The policy refusal does not wait for the server's ping");

        // A repaired installation: the same session admits once the policy loads again (a new ping re-reads the files).
        WritePolicy(_repoPolicyText);
        Ping(true, NormalServerMods(), "abc123");
        Require(Reason() == null, "With the policy back the same session admits");

        // Every way a policy can be unusable.
        ResetWorld();
        WritePolicy(new string(' ', 300 * 1024) + _repoPolicyText);
        ArenaSession(ReportActive);
        Ping(true, NormalServerMods(), "abc123");
        Equal(Reason(), PolicyRefusal("default whitelist is larger than 256 KB and was ignored"),
            "An oversized policy file refuses Arena in a session as well");

        ResetWorld();
        WritePolicy("{\"schema\":9}");
        ArenaSession(ReportActive);
        Ping(true, NormalServerMods(), "abc123");
        Require(Reason() != null && Reason().StartsWith("Arena is unavailable: its mod whitelist could not be loaded (default whitelist", StringComparison.Ordinal)
                && Reason().EndsWith(" Return to the normal session.", StringComparison.Ordinal),
            "An invalid policy file refuses Arena in a session and names the loader's diagnostic");

        ResetWorld();
        ArenaSession(ReportActive);
        using (new FileStream(_policyPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Ping(true, NormalServerMods(), "abc123");
            Require(Reason() != null && Reason().StartsWith("Arena is unavailable: its mod whitelist could not be loaded (default whitelist could not be read: ", StringComparison.Ordinal)
                    && Reason().EndsWith(" Return to the normal session.", StringComparison.Ordinal),
                "A policy file that cannot be read refuses Arena in a session");
        }

        // The user file is optional: a defective one is ignored with a warning and the shipped policy applies.
        ResetWorld();
        WriteUserPolicy("{ not json");
        ArenaSession(ReportActive);
        Ping(true, NormalServerMods(), "abc123");
        Require(Reason() == null, "A defective user file does not make the policy unavailable");
        ResetWorld();
        WriteUserPolicy(new string(' ', 300 * 1024) + "{\"schema\":1}");
        ArenaSession(ReportActive);
        Ping(true, NormalServerMods(), "abc123");
        Require(Reason() == null, "An oversized user file does not make the policy unavailable");

        // The order of the checks: the gate's own refusal, then the policy, then the patchers that ran early.
        ResetWorld();
        File.Delete(_policyPath);
        ArenaSessionMode.Initialize(new[] { "game.exe", "-arena-session" }, "{\"schema\":1,\"active\":false,\"error\":\"boom\"}");
        Require(Reason().Contains("preloader gate is inactive: boom") && !Reason().Contains("mod whitelist"),
            "An inactive gate is reported ahead of a missing policy");

        ResetWorld();
        File.Delete(_policyPath);
        ArenaSession(ReportWith(",\"patchersRanBeforeGate\":[\"Early.Patcher.dll\"]"));
        Ping(true, NormalServerMods());
        Equal(Reason(), missing, "A missing policy is reported ahead of a patcher that ran before the gate");

        // A normal session keeps its refusal: the text without the way back, no session start.
        ResetWorld();
        File.Delete(_policyPath);
        Ping(false, NormalServerMods());
        Equal(Reason(), PolicyRefusal("default whitelist not found at " + _policyPath).Replace(" Return to the normal session.", ""),
            "A normal session without a policy keeps its own refusal, without the way back");
    }

    // ------------------------------------------------------------------ the plugins this game loaded

    private static void CheckArenaSessionLoadedPlugins()
    {
        const string intruderRefusal = "This game loaded Intruder, which the current policy excludes: "
            + "the policy changed during the switch. " + ReturnHint;

        // A plugin the current policy excludes is running in the session.
        ResetWorld();
        AddPlugin("com.example.intruder", "Intruder", "1.0");
        ArenaSession(ReportActive);
        Ping(true, NormalServerMods());
        Equal(Reason(), intruderRefusal, "A loaded plugin the policy does not name refuses Arena in the session");
        Require(RefusedWarnings("Intruder") == 1, "The plugin refusal is logged once, as a Warning");
        Require(ArenaWhitelistAdmission.WarningLine == null, "A refused session shows no notes");
        Ping(true, NormalServerMods());
        _ = Reason();
        Require(RefusedWarnings("Intruder") == 1, "A new ping does not log the plugin refusal again");

        // It needs no ping: the plugin list is the client's own.
        ResetWorld();
        AddPlugin("com.example.intruder", "Intruder", "1.0");
        ArenaSession(ReportActive);
        Equal(Reason(), intruderRefusal, "The plugin refusal does not wait for the server's ping");

        // Every kind of exclusion counts, the names are sorted and listed once.
        ResetWorld();
        WriteUserPolicy("{\"schema\":1,\"deny\":[\"com.example.zed\"]}");
        AddPlugin("com.chazut.orbit", "ORBIT", "1.0");
        AddPlugin("com.example.zed", "Zed", "1.0");
        AddPlugin("com.example.alpha", "Alpha", "1.0");
        ArenaSession(ReportActive);
        Ping(true, NormalServerMods());
        Equal(Reason(), "This game loaded Alpha, ORBIT, Zed, which the current policy excludes: "
            + "the policy changed during the switch. " + ReturnHint,
            "A default-denied, a user-denied and an unlisted plugin all refuse, sorted");

        ResetWorld();
        for (int i = 0; i < 14; i++) AddPlugin("com.example.m" + i.ToString("D2"), "Mod" + i.ToString("D2"), "1.0");
        ArenaSession(ReportActive);
        Ping(true, NormalServerMods());
        Require(Reason().StartsWith("This game loaded Mod00, Mod01", StringComparison.Ordinal) && Reason().Contains("Mod11, and 2 more, which the current policy excludes"),
            "Twelve plugin names are listed, the rest is counted");

        // The policy change itself: allowed when the request was made, gone from the user file by the restart.
        ResetWorld();
        WriteUserPolicy("{\"schema\":1,\"allow\":[{\"guid\":\"com.example.thing\",\"name\":\"Thing Pack\",\"kind\":\"mod\"}]}");
        AddPlugin("com.example.thing", "Thing Pack", "3.0");
        ArenaSession(ReportActive);
        Ping(true, NormalServerMods());
        Require(Reason() == null, "A plugin the user allowed is admitted while the policy still allows it");
        File.Delete(_userPolicyPath);
        Ping(true, NormalServerMods());
        Equal(Reason(), "This game loaded Thing Pack, which the current policy excludes: the policy changed during the switch. " + ReturnHint,
            "The same plugin is refused once the user file no longer allows it");

        // A blacklist excludes only what it denies.
        ResetWorld();
        WriteUserPolicy("{\"schema\":1,\"unlisted\":\"allow\",\"deny\":[\"com.example.denied\"]}");
        AddPlugin("com.example.free", "Free Pack", "1.0");
        ArenaSession(ReportActive);
        Ping(true, NormalServerMods());
        Require(Reason() == null, "Under a blacklist an unlisted plugin is admitted");
        AddPlugin("com.example.denied", "Denied Pack", "1.0");
        Ping(true, NormalServerMods());
        Equal(Reason(), "This game loaded Denied Pack, which the current policy excludes: the policy changed during the switch. " + ReturnHint,
            "Under a blacklist a denied plugin refuses");

        // Deny wins over a dependency: a plugin that needs a denied library is refused with it.
        ResetWorld();
        WriteUserPolicy("{\"schema\":1,\"allow\":[{\"guid\":\"com.example.foreign\"}],\"deny\":[\"com.example.lib\"]}");
        AddPlugin("com.example.lib", "Example Lib", "1.0");
        AddPlugin("com.example.foreign", "Foreign Pack", "1.0", hard: new[] { "com.example.lib" });
        ArenaSession(ReportActive);
        Ping(true, NormalServerMods());
        Equal(Reason(), "This game loaded Example Lib, Foreign Pack, which the current policy excludes: the policy changed during the switch. " + ReturnHint,
            "A plugin that needs a denied library is refused together with it");

        // A library that an allowed plugin needs is part of the session (pulled in): no exclusion, and no second note,
        // because the gate's report carries the plugin notes already.
        ResetWorld();
        Chainloader.PluginInfos["com.acidphantasm.botplacementsystem"].Dependencies = new[]
        {
            new BepInDependency { DependencyGUID = "com.example.lib", Flags = BepInDependency.DependencyFlags.HardDependency },
        };
        AddPlugin("com.example.lib", "Example Lib", "1.0");
        ArenaSession(ReportActive);
        Ping(true, NormalServerMods());
        Require(Reason() == null, "The library an allowed plugin needs is part of the session, not an exclusion");
        Equal(ArenaWhitelistAdmission.WarningLine, "Notes: SAIN: version 4.5.2 differs from reviewed 4.5.1",
            "The gate's report is the only source of the plugin notes in a session");

        // A version that differs from the reviewed one never refuses.
        ResetWorld();
        Chainloader.PluginInfos["me.sol.sain"].Metadata.Version = new Version(4, 5, 2);
        ArenaSession(ReportActive);
        Ping(true, NormalServerMods());
        Require(Reason() == null, "A loaded plugin whose version differs from the reviewed one is admitted");

        // No list can remove the platform or Arena units.
        ResetWorld();
        WriteUserPolicy("{\"schema\":1,\"deny\":[\"com.maschine.ArenaMode\",\"com.SPT.core\"]}");
        ArenaSession(ReportActive);
        Ping(true, NormalServerMods());
        Require(Reason() == null, "A deny cannot exclude the Arena or the platform plugins");

        // Fika is never filtered: its own refusal decides, not this one.
        ResetWorld();
        AddPlugin("com.fika.core", "Fika.Core", "1.0.0");
        AddPlugin("com.example.intruder", "Intruder", "1.0");
        ArenaSession(ReportActive);
        Ping(false, NormalServerMods());
        Require(Reason() == null, "With Fika the loaded plugins are not judged either");

        // The order of the checks: a patcher that ran before the gate, then the loaded plugins, then the server.
        ResetWorld();
        AddPlugin("com.example.intruder", "Intruder", "1.0");
        ArenaSession(ReportWith(",\"patchersRanBeforeGate\":[\"Early.Patcher.dll\"]"));
        Ping(true, NormalServerMods());
        Equal(Reason(), EarlyPatcherRefusal, "A patcher that ran before the gate is reported ahead of a loaded plugin the policy excludes");

        ResetWorld();
        AddPlugin("com.example.intruder", "Intruder", "1.0");
        ArenaSession(ReportActive);
        Ping(false, NormalServerMods());
        Equal(Reason(), intruderRefusal, "A loaded plugin the policy excludes is reported ahead of a server that is no Arena instance");
    }
}
