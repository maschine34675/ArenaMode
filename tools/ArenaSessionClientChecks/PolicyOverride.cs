using ArenaMode.Session;
using BepInEx.Bootstrap;

// The policy override (docs/ARENA-SESSION-20261021.md, section 3): a user file with "unlisted": "allow"
// turns the whitelist into a blacklist, and the client must not let that go unnoticed. The shared decision
// adds ONE warning per side ("policy override: N unlisted mod(s) allowed by your policy file: ..."); this
// part of the harness follows it through the production admission into the notes line that the Arena menu
// shows as the second notice line and the session notice carries. Whether the screen actually draws that
// line is pinned by source text in SourcePins.cs.
internal static partial class Program
{
    private const string BlacklistFile = "{\"schema\":1,\"unlisted\":\"allow\"}";

    private static int InfoLines(string exactMessage) => ArenaMode.Plugin.ModLogger.Info.Count(l => l == exactMessage);

    private static void CheckPolicyOverrideNote()
    {
        // The override note is never counted into the cap of four notes. Five version differences, four of
        // them sorting ahead of "policy override", would push it behind "and N more" if it were sorted in.
        ResetWorld();
        WriteUserPolicy(BlacklistFile);
        AddPlugin("com.example.gamma", "Gamma Probe", "1.0.0");
        AddPlugin("com.maschine.Killcam", "Killcam", "9.0");
        foreach (string guid in new[] { "com.acidphantasm.botplacementsystem", "xyz.drakia.bigbrain", "me.sol.sain" })
            Chainloader.PluginInfos[guid].Metadata.Version = Version.Parse("9.0");
        var versionMods = NormalServerMods();
        versionMods[2] = Mod("com.acidphantasm.progressivebotsystem", "APBS", "9.0");
        Ping(false, versionMods);
        Require(Reason() == null, "In blacklist mode a mod nothing names is no refusal, whatever notes there are");
        Equal(ArenaWhitelistAdmission.WarningLine,
            "Notes: policy override: 1 unlisted mod(s) allowed by your policy file: Gamma Probe"
            + "; ABPS: version 9.0 differs from reviewed 2.1.0; APBS: version 9.0 differs from reviewed 2.4.1"
            + "; BigBrain: version 9.0 differs from reviewed 1.5.0; Killcam: version 9.0 differs from reviewed 1.0.1; and 1 more",
            "The policy override is shown first and never counted into the cap of four notes");

        // A normal session whose unlisted mods the user file lets through: Arena is admitted, the notes line
        // announces the override once per side (client first, as evaluated) and each note is logged once at Info.
        ResetWorld();
        WriteUserPolicy(BlacklistFile);
        AddPlugin("com.example.zombies", "Override Zombie Pack", "1.0.0");
        AddPlugin("com.example.alpha", "Override Alpha Pack", "1.0.0");
        var unlistedMods = NormalServerMods();
        unlistedMods.Add(Mod("com.example.servermod", "Override Server Thing", "3.0"));
        Ping(false, unlistedMods);
        Require(Reason() == null, "In blacklist mode mods nothing names do not refuse Arena");
        const string clientNote = "policy override: 2 unlisted mod(s) allowed by your policy file: Override Alpha Pack, Override Zombie Pack";
        const string serverNote = "policy override: 1 unlisted mod(s) allowed by your policy file: Override Server Thing";
        Equal(ArenaWhitelistAdmission.WarningLine, "Notes: " + clientNote + "; " + serverNote,
            "The policy override is announced once per side, client first");
        Require(InfoLines("Arena whitelist: " + clientNote) == 1 && InfoLines("Arena whitelist: " + serverNote) == 1,
            "Each policy-override note is logged once at Info");
        Ping(false, unlistedMods);
        _ = Reason();
        Require(InfoLines("Arena whitelist: " + clientNote) == 1 && InfoLines("Arena whitelist: " + serverNote) == 1,
            "A new ping does not log the policy override again");
        Require(ArenaWhitelistAdmission.TryPrepareSessionStart(out _, out var sessionMods, out _)
                && sessionMods.Any(m => m.Guid == "com.example.servermod"),
            "The override changes no session rule: the unlisted server mod goes into the session as before");

        // A mod that is both a client and a server part under the same name is announced on each side with the
        // same text, and a note is shown once.
        ResetWorld();
        WriteUserPolicy(BlacklistFile);
        AddPlugin("com.example.both", "Override Both Parts", "1.0.0");
        var bothMods = NormalServerMods();
        bothMods.Add(Mod("com.example.both", "Override Both Parts", "1.0"));
        Ping(false, bothMods);
        Equal(ArenaWhitelistAdmission.WarningLine,
            "Notes: policy override: 1 unlisted mod(s) allowed by your policy file: Override Both Parts",
            "Two sides with the same note show it once");

        // The refusal of a denied mod carries the override note as its second line, and the offer is unchanged.
        ResetWorld();
        WriteUserPolicy("{\"schema\":1,\"unlisted\":\"allow\",\"deny\":[\"com.example.looting\"]}");
        AddPlugin("com.example.looting", "Looting Pack", "2.0.0");
        AddPlugin("com.example.beta", "Override Beta Probe", "1.0.0");
        Ping(false, NormalServerMods());
        Require(Reason() != null && Reason().Contains("Blocked for Arena: Looting Pack."),
            "In blacklist mode a denied mod refuses Arena");
        Equal(ArenaWhitelistAdmission.WarningLine,
            "Notes: policy override: 1 unlisted mod(s) allowed by your policy file: Override Beta Probe",
            "A refusal carries the policy override as its second line");
        Require(ArenaWhitelistAdmission.CanOfferSessionStart(Reason()) && !Reason().Contains("Override Beta Probe"),
            "The override note is no part of the refusal and changes nothing about the session offer");

        // No override, no note: a whitelist refuses the same mods and says nothing about an override, and so
        // does a blacklist whose installed mods are all named or denied.
        ResetWorld();
        AddPlugin("com.example.zombies", "Override Zombie Pack", "1.0.0");
        var whitelistMods = NormalServerMods();
        whitelistMods.Add(Mod("com.example.servermod", "Override Server Thing", "3.0"));
        Ping(false, whitelistMods);
        Require(Reason() != null && ArenaWhitelistAdmission.WarningLine == null,
            "A whitelist announces no policy override");
        WriteUserPolicy("{\"schema\":1,\"unlisted\":\"deny\",\"deny\":[\"com.example.zombies\"]}");
        Ping(false, whitelistMods);
        Require(Reason() != null && ArenaWhitelistAdmission.WarningLine == null,
            "An explicit \"unlisted\": \"deny\" is a whitelist and announces no policy override");

        ResetWorld();
        WriteUserPolicy("{\"schema\":1,\"unlisted\":\"allow\",\"deny\":[\"com.example.looting\"]}");
        AddPlugin("com.example.looting", "Looting Pack", "2.0.0");
        Ping(false, NormalServerMods());
        Require(Reason() != null && ArenaWhitelistAdmission.WarningLine == null,
            "A blacklist that lets no unlisted mod through announces no policy override");

        // An Arena session: the gate's own note for the client side comes with its report, the server side is
        // decided here from the ping; both lead the notes line, and the flag arrives with the report.
        ResetWorld();
        WriteUserPolicy(BlacklistFile);
        const string gateNote = "policy override: 1 unlisted mod(s) allowed by your policy file: Override Gate Probe";
        string gateReport = ReportActive
            .Replace("\"warnings\":[\"SAIN: version 4.5.2 differs from reviewed 4.5.1\"]",
                "\"warnings\":[\"SAIN: version 4.5.2 differs from reviewed 4.5.1\",\"" + gateNote + "\"]")
            .Replace("\"policyHash\":\"abc123\",", "\"policyHash\":\"abc123\",\"unlistedAllowed\":true,");
        Require(gateReport.Contains("unlistedAllowed") && gateReport.Contains(gateNote), "The harness report carries the gate's note and flag");
        ArenaSession(gateReport);
        Require(ArenaSessionMode.GateActive && ArenaSessionMode.GateReport.UnlistedAllowed,
            "The gate's unlistedAllowed flag reaches the client");
        var sessionServerMods = NormalServerMods();
        sessionServerMods.Add(Mod("com.example.servermod", "Override Session Thing", "3.0"));
        Ping(true, sessionServerMods);
        Require(Reason() == null, "An Arena session with the override is admitted");
        Equal(ArenaWhitelistAdmission.WarningLine,
            "Notes: " + gateNote + "; policy override: 1 unlisted mod(s) allowed by your policy file: Override Session Thing"
            + "; SAIN: version 4.5.2 differs from reviewed 4.5.1",
            "The session notice carries the policy override of both sides first");
        Require(InfoLines("Arena whitelist: " + gateNote) == 1,
            "The gate's policy-override note is logged once at Info in the session too");
    }
}
