using ArenaMode.Session;
using ArenaMode.Shared.Session;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

// Offline checks of the client side of the Arena session feature (docs/ARENA-SESSION-20261021.md,
// section 7): the session state, the whitelist admission and the session switch. The production
// sources are compiled unchanged; the game, BepInEx and the server ping are stand-ins (StandIns.cs).
// The real ping parser is checked by NativeMenuChecks, the real notice/screen wiring by source pins in
// Verify.ps1; what no offline check can prove is the Unity UI itself.
internal static partial class Program
{
    private static int _checks;
    private static string _root;
    private static string _gameDir;
    private static string _policyPath;
    private static string _userPolicyPath;
    private static string _helperPath;
    private static string _requestPath;
    private static string _repoPolicyText;

    private static int Main()
    {
        try
        {
            Setup();
            CheckSessionMode();
            CheckNormalSessionAdmission();
            CheckSessionOffer();
            CheckNamesAndNotes();
            CheckPolicyFiles();
            CheckFikaAndFailures();
            CheckPrepareForScreen();
            CheckArenaSessionAdmission();
            CheckProfileAndRequestShape();
            CheckTwoClickGate();
            CheckRequestFailures();
            CheckRequestSuccess();
            CheckSourcePins();
            Console.WriteLine($"ARENA_SESSION_CLIENT_CHECKS_PASSED ({_checks} assertions)");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"ARENA_SESSION_CLIENT_CHECKS_FAILED after {_checks} assertions: {error}");
            return 1;
        }
        finally
        {
            try { if (_root != null && Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
        }
    }

    // ------------------------------------------------------------------ world

    private static void Setup()
    {
        string repo = FindRepositoryRoot();
        _repoPolicyText = File.ReadAllText(Path.Combine(repo, "assets", "arena-whitelist.json"));
        _root = Path.Combine(Path.GetTempPath(), "arena-session-client-checks-" + Guid.NewGuid().ToString("N"));
        _gameDir = Path.Combine(_root, "game");
        Paths.GameRootPath = _gameDir;
        Paths.ExecutablePath = Path.Combine(_gameDir, "EscapeFromTarkov.exe");
        Paths.PluginPath = Path.Combine(_gameDir, "BepInEx", "plugins");
        Paths.ConfigPath = Path.Combine(_gameDir, "BepInEx", "config");
        _policyPath = Path.Combine(Paths.PluginPath, ArenaSessionContract.PluginFolderName, ArenaSessionContract.DefaultPolicyFileName);
        _userPolicyPath = Path.Combine(Paths.ConfigPath, ArenaSessionContract.UserPolicyFileName);
        _helperPath = Path.Combine(Paths.PluginPath, ArenaSessionContract.PluginFolderName, ArenaSessionContract.HelperFileName);
        _requestPath = Path.Combine(_root, "session", ArenaSessionContract.RequestFileName);
        ArenaSessionSwitch.RequestPathProvider = () => _requestPath;
    }

    private static string FindRepositoryRoot()
    {
        // Verify.ps1 builds mutants outside the repository and says where the repository is.
        string configured = Environment.GetEnvironmentVariable("ARENA_REPO_ROOT");
        if (!string.IsNullOrEmpty(configured) && File.Exists(Path.Combine(configured, "ArenaMode.sln"))) return configured;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "ArenaMode.sln"))) return directory.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>A normal session with everything on the default whitelist, files in place, no ping yet.</summary>
    private static void ResetWorld()
    {
        Chainloader.PluginInfos.Clear();
        AddPlugin("com.SPT.core", "SPT Core", "4.1.6");
        AddPlugin("com.SPT.custom", "SPT Custom", "4.1.6");
        AddPlugin("com.SPT.debugging", "SPT Debugging", "4.1.6");
        AddPlugin("com.SPT.singleplayer", "SPT Singleplayer", "4.1.6");
        AddPlugin("com.bepis.bepinex.configurationmanager", "Configuration Manager", "18.3");
        AddPlugin(ArenaSessionContract.ArenaPluginGuid, "ArenaMode", "0.1.3",
            soft: new[] { "xyz.pit.fireteam", "com.danw.questingbots", "com.chazut.orbit" });
        AddPlugin("me.sol.sain", "SAIN", "4.5.1", hard: new[] { "xyz.drakia.bigbrain" }, soft: new[] { "xyz.drakia.waypoints" });
        AddPlugin("xyz.drakia.bigbrain", "BigBrain", "1.5.0");
        AddPlugin("xyz.drakia.waypoints", "Waypoints", "1.9.0");
        AddPlugin("com.acidphantasm.botplacementsystem", "ABPS", "2.1.0");
        AddPlugin("com.maschine.UiBridge", "UiBridge", "1.0.0");
        ArenaServerClient.Reset();
        ArenaSessionMode.Initialize(new[] { Paths.ExecutablePath, "-token=0123456789abcdef01234567" }, null);
        ArenaSessionSwitch.ResetRequestLatch();
        ArenaSessionSwitch.CommandLineProvider = () => new[] { Paths.ExecutablePath, "-force-gfx-jobs", "native", "-token=0123456789abcdef01234567" };
        SPT.Common.Http.RequestHandler.IsLocal = true;
        Application.QuitCalls = 0;
        ArenaMode.Plugin.ModLogger.Clear();
        WritePolicy(_repoPolicyText);
        if (File.Exists(_userPolicyPath)) File.Delete(_userPolicyPath);
        WriteFile(_helperPath, "stand-in helper");
        WriteFile(Path.Combine(_gameDir, "SPT_Runtime", "SPT.Server.exe"), "stand-in server");
        ArenaSessionSwitch.LaunchHelper = DefaultLauncher;
    }

    private static void AddPlugin(string guid, string name, string version, string[] hard = null, string[] soft = null)
    {
        var dependencies = new List<BepInDependency>();
        foreach (string dependency in hard ?? Array.Empty<string>())
            dependencies.Add(new BepInDependency { DependencyGUID = dependency, Flags = BepInDependency.DependencyFlags.HardDependency });
        foreach (string dependency in soft ?? Array.Empty<string>())
            dependencies.Add(new BepInDependency { DependencyGUID = dependency, Flags = BepInDependency.DependencyFlags.SoftDependency });
        Chainloader.PluginInfos[guid] = new PluginInfo
        {
            Metadata = new BepInPlugin { GUID = guid, Name = name, Version = Version.Parse(version) },
            Dependencies = dependencies,
            Location = Path.Combine(Paths.PluginPath, name + ".dll"),
        };
    }

    private static void Remove(string guid) => Chainloader.PluginInfos.Remove(guid);

    private static void WriteFile(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
    }

    private static void WritePolicy(string text) => WriteFile(_policyPath, text);
    private static void WriteUserPolicy(string text) => WriteFile(_userPolicyPath, text);

    private static ArenaServerModInfo Mod(string guid, string name, string version)
        => new ArenaServerModInfo(guid, name, version, Path.Combine(_gameDir, "SPT_Runtime", "user", "mods", name));

    private static List<ArenaServerModInfo> NormalServerMods() => new()
    {
        Mod(ArenaSessionContract.ArenaPluginGuid, "ArenaMode", "0.1.3"),
        Mod("me.sol.sain", "SAIN", "4.5.1"),
        Mod("com.acidphantasm.progressivebotsystem", "APBS", "2.4.1"),
        Mod("com.acidphantasm.botplacementsystem", "ABPS", "2.1.0"),
    };

    /// <summary>What a current server's ping says; a new generation, like a real refresh.</summary>
    private static void Ping(bool arenaInstance, List<ArenaServerModInfo> mods)
    {
        ArenaServerClient.IsServerModKnownAvailable = true;
        ArenaServerClient.SessionFieldsPresent = true;
        ArenaServerClient.IsArenaInstance = arenaInstance;
        ArenaServerClient.ServerMods = mods;
        ArenaServerClient.PingGeneration++;
    }

    private static void OldServerPing()
    {
        ArenaServerClient.IsServerModKnownAvailable = true;
        ArenaServerClient.SessionFieldsPresent = false;
        ArenaServerClient.IsArenaInstance = false;
        ArenaServerClient.ServerMods = null;
        ArenaServerClient.PingGeneration++;
    }

    private static string Reason() => ArenaWhitelistAdmission.GetRejectionReason();

    private static void Require(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal(string actual, string expected, string message)
    {
        _checks++;
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message}\n  expected: {expected ?? "<null>"}\n  actual:   {actual ?? "<null>"}");
    }

    private const string ReportActive = "{\"schema\":1,\"active\":true,\"error\":null,\"policyHash\":\"abc123\","
        + "\"allowed\":[{\"guid\":\"me.sol.sain\",\"name\":\"SAIN\",\"version\":\"4.5.1\",\"file\":\"x.dll\",\"reason\":\"whitelist\"}],"
        + "\"excluded\":[{\"guid\":\"com.example.bad\",\"name\":\"Bad\",\"version\":\"1\",\"file\":\"y.dll\",\"reason\":\"not-on-whitelist\"}],"
        + "\"warnings\":[\"SAIN: version 4.5.2 differs from reviewed 4.5.1\"],\"patchersNeutralised\":[\"other.dll\"]}";

    private static void ArenaSession(string reportJson)
        => ArenaSessionMode.Initialize(new[] { Paths.ExecutablePath, "-force-gfx-jobs", "native", "-arena-session" }, reportJson);

    // ------------------------------------------------------------------ session mode

    private static void CheckSessionMode()
    {
        ResetWorld();
        Require(!ArenaSessionMode.IsArenaSession && ArenaSessionMode.GateReport == null && !ArenaSessionMode.GateActive
            && ArenaSessionMode.Problem == null, "A process without the flag is no Arena session and has no gate state");

        ArenaSessionMode.Initialize(new[] { "game.exe", "-ARENA-SESSION" }, null);
        Require(ArenaSessionMode.IsArenaSession && !ArenaSessionMode.GateActive && ArenaSessionMode.GateReport == null
            && ArenaSessionMode.Problem.Contains("did not publish"),
            "The flag is case-insensitive; no published report means the gate did not run");

        ArenaSessionMode.Initialize(new[] { "game.exe", "-arena-session=1", "arena-session" }, ReportActive);
        Require(!ArenaSessionMode.IsArenaSession && !ArenaSessionMode.GateActive,
            "Only the exact flag counts; a report without the flag proves nothing");

        ArenaSessionMode.Initialize(new[] { "game.exe", "-arena-session" }, "{ not json");
        Require(ArenaSessionMode.IsArenaSession && !ArenaSessionMode.GateActive && ArenaSessionMode.Problem.Contains("unreadable"),
            "A malformed gate report is an inactive gate");

        ArenaSessionMode.Initialize(new[] { "game.exe", "-arena-session" }, "{\"schema\":2,\"active\":true}");
        Require(!ArenaSessionMode.GateActive && ArenaSessionMode.Problem.Contains("unreadable"),
            "An unknown report schema is an inactive gate");

        ArenaSessionMode.Initialize(new[] { "game.exe", "-arena-session" },
            "{\"schema\":1,\"active\":false,\"error\":\"policy file missing\",\"policyHash\":\"\"}");
        Require(!ArenaSessionMode.GateActive && ArenaSessionMode.GateReport != null
            && ArenaSessionMode.Problem == "the preloader gate is inactive: policy file missing",
            "An inactive report keeps the gate's own error");

        ArenaSessionMode.Initialize(new[] { "game.exe", "-arena-session" }, ReportActive);
        Require(ArenaSessionMode.GateActive && ArenaSessionMode.Problem == null
            && ArenaSessionMode.GateReport.Allowed.Count == 1 && ArenaSessionMode.GateReport.Excluded.Count == 1
            && ArenaSessionMode.GateReport.PolicyHash == "abc123",
            "An active report is parsed and exposed");
    }

    // ------------------------------------------------------------------ normal session

    private static void CheckNormalSessionAdmission()
    {
        ResetWorld();
        Require(Reason() == null && ArenaWhitelistAdmission.WarningLine == null,
            "A normal session whose plugins are all whitelisted may play Arena without a restart");
        Require(!ArenaWhitelistAdmission.CanOfferSessionStart("anything"), "No session is offered when nothing is excluded");

        AddPlugin("com.example.zombies", "Zombie Pack", "1.0.0");
        ArenaServerClient.PingGeneration++;
        Equal(Reason(),
            "Arena runs only with whitelisted mods. Not on the whitelist: Zombie Pack. Remove them and restart the game to play Arena.",
            "Without a session-capable server the refusal asks to remove the mods");
        Require(!ArenaWhitelistAdmission.CanOfferSessionStart(Reason()),
            "No session is offered before the server has answered");

        Ping(false, NormalServerMods());
        string offered = Reason();
        Equal(offered,
            "Arena runs only with whitelisted mods. Not on the whitelist: Zombie Pack. Start an Arena session to play Arena without them; the game restarts.",
            "The spec's refusal text, with the session sentence");
        Require(ArenaWhitelistAdmission.CanOfferSessionStart(offered), "The START ARENA SESSION action is offered");
        Require(!ArenaWhitelistAdmission.CanOfferSessionStart("some other reason") && !ArenaWhitelistAdmission.CanOfferSessionStart(null),
            "The action belongs to the whitelist refusal only");

        // The platform, Arena, mod and tool tiers are all allowed; a missing optional mod is no problem.
        ResetWorld();
        Remove("xyz.drakia.waypoints");
        Ping(false, NormalServerMods());
        Require(Reason() == null, "A whitelisted mod that is not installed is no refusal");

        // Soft dependencies of an allowed mod never pull an excluded mod in.
        ResetWorld();
        AddPlugin("com.chazut.orbit", "ORBIT", "1.0.0");
        Ping(false, NormalServerMods());
        Require(Reason() != null && Reason().Contains("ORBIT"), "A soft dependency target stays excluded");
    }

    private static void CheckSessionOffer()
    {
        // Server-only exclusion with a clean client side.
        ResetWorld();
        var mods = NormalServerMods();
        mods.Add(Mod("com.example.servermod", "Server Thing", "3.0"));
        Ping(false, mods);
        string reason = Reason();
        Require(reason != null && reason.Contains("Not on the whitelist: Server Thing.") && ArenaWhitelistAdmission.CanOfferSessionStart(reason),
            "A server mod that is not whitelisted refuses Arena and offers the session");
        Require(ArenaWhitelistAdmission.TryPrepareSessionStart(out string hash, out var allowed, out string error) && error == null,
            "The session start prepares from a refused normal session");
        var policy = LoadRepoPolicy();
        Equal(hash, policy.Hash, "The request carries the effective policy's hash");
        Equal(string.Join(",", allowed.Select(m => m.Guid)),
            "com.maschine.ArenaMode,me.sol.sain,com.acidphantasm.progressivebotsystem,com.acidphantasm.botplacementsystem",
            "Only the allowed server mods go into the session, in ping order");
        Require(allowed.All(m => m.File.StartsWith(Path.Combine(_gameDir, "SPT_Runtime", "user", "mods"))),
            "Each allowed server mod carries its folder from the ping");

        // Every reason that makes a session impossible removes the action but keeps the refusal.
        foreach (var (label, arrange) in new (string, Action)[]
        {
            ("older server", OldServerPing),
            ("helper missing", () => { File.Delete(_helperPath); ArenaServerClient.PingGeneration++; }),
            ("no local SPT server", () => { File.Delete(Path.Combine(_gameDir, "SPT_Runtime", "SPT.Server.exe")); ArenaServerClient.PingGeneration++; }),
            ("remote backend", () => { SPT.Common.Http.RequestHandler.IsLocal = false; ArenaServerClient.PingGeneration++; }),
            ("already an arena server", () => Ping(true, NormalServerMods())),
            ("server mod outside this installation", () =>
            {
                var elsewhere = NormalServerMods();
                elsewhere[1] = new ArenaServerModInfo("me.sol.sain", "SAIN", "4.5.1", Path.Combine(_root, "other-spt", "SPT_Runtime", "user", "mods", "SAIN"));
                Ping(false, elsewhere);
            }),
            ("server mod below a sub folder", () =>
            {
                var nested = NormalServerMods();
                nested[1] = new ArenaServerModInfo("me.sol.sain", "SAIN", "4.5.1", Path.Combine(_gameDir, "SPT_Runtime", "user", "mods", "SAIN", "inner"));
                Ping(false, nested);
            }),
            ("server mod without a folder", () =>
            {
                var nameless = NormalServerMods();
                nameless[1] = new ArenaServerModInfo("me.sol.sain", "SAIN", "4.5.1", string.Empty);
                Ping(false, nameless);
            }),
            ("no allowed server mod", () => Ping(false, new List<ArenaServerModInfo>())),
        })
        {
            ResetWorld();
            AddPlugin("com.example.zombies", "Zombie Pack", "1.0.0");
            Ping(false, NormalServerMods());
            arrange();
            string refusal = Reason();
            Require(refusal != null && refusal.EndsWith("Remove them and restart the game to play Arena.", StringComparison.Ordinal)
                && !ArenaWhitelistAdmission.CanOfferSessionStart(refusal),
                $"{label}: the whitelist still refuses, without the session action");
            Require(!ArenaWhitelistAdmission.TryPrepareSessionStart(out _, out _, out string why) && !string.IsNullOrEmpty(why),
                $"{label}: the session start is refused with a message");
        }

        // An older server still gets the client-side check, and a clean client passes.
        ResetWorld();
        OldServerPing();
        Require(Reason() == null, "An older server alone refuses nothing");
    }

    private static void CheckNamesAndNotes()
    {
        // Names: sorted, de-duplicated across both sides, capped.
        ResetWorld();
        AddPlugin("com.example.zed", "Zed", "1.0");
        AddPlugin("com.example.alpha", "alpha", "1.0");
        var mods = NormalServerMods();
        mods.Add(Mod("com.example.zed", "zed", "1"));
        Ping(false, mods);
        Require(Reason().Contains("Not on the whitelist: alpha, Zed."), "Names are sorted and a mod on both sides is listed once");

        // The same GUID under two different names (client "DynamicMaps", server "Dynamic Maps").
        ResetWorld();
        AddPlugin("com.example.maps", "DynamicMaps", "1.0");
        var mapMods = NormalServerMods();
        mapMods.Add(Mod("com.example.maps", "Dynamic Maps", "1"));
        Ping(false, mapMods);
        Require(Reason().Contains("Not on the whitelist: DynamicMaps.") && !Reason().Contains("Dynamic Maps"),
            "A mod with differently named client and server parts is listed once");

        ResetWorld();
        for (int i = 0; i < 15; i++) AddPlugin("com.example.m" + i.ToString("D2"), "Mod" + i.ToString("D2"), "1.0");
        Ping(false, NormalServerMods());
        string capped = Reason();
        Require(capped.Contains("Mod00, Mod01") && capped.Contains("Mod11, and 3 more.") && !capped.Contains("Mod12"),
            "Twelve names are listed, the rest is counted");

        // A mod without a name falls back to its GUID.
        ResetWorld();
        AddPlugin("com.example.nameless", "", "1.0");
        Ping(false, NormalServerMods());
        Require(Reason().Contains("com.example.nameless"), "An unnamed mod is listed by its GUID");

        // Version warnings: never refuse, shown as the second line, logged once at Info.
        ResetWorld();
        Chainloader.PluginInfos["me.sol.sain"].Metadata.Version = new Version(4, 5, 2);
        Ping(false, NormalServerMods());
        Require(Reason() == null, "A version difference never refuses");
        Equal(ArenaWhitelistAdmission.WarningLine, "Notes: SAIN: version 4.5.2 differs from reviewed 4.5.1",
            "The version difference is the second line");
        int infoLines = ArenaMode.Plugin.ModLogger.Info.Count(l => l.Contains("version 4.5.2 differs"));
        Require(infoLines == 1, "The warning is logged once at Info");
        Ping(false, NormalServerMods());
        _ = Reason();
        Require(ArenaMode.Plugin.ModLogger.Info.Count(l => l.Contains("version 4.5.2 differs")) == 1,
            "A new ping does not log the same warning again");

        // A refusal carries the notes as well.
        AddPlugin("com.example.zombies", "Zombie Pack", "1.0.0");
        Ping(false, NormalServerMods());
        Require(Reason() != null && ArenaWhitelistAdmission.WarningLine != null, "Notes accompany a refusal");

        // Hard dependencies are pulled in with a note; the server side warns on its own versions.
        ResetWorld();
        AddPlugin("com.example.lib", "Example Lib", "1.0", hard: null);
        Chainloader.PluginInfos["com.acidphantasm.botplacementsystem"].Dependencies = new[]
        {
            new BepInDependency { DependencyGUID = "com.example.lib", Flags = BepInDependency.DependencyFlags.HardDependency },
        };
        var serverMods = NormalServerMods();
        serverMods[2] = Mod("com.acidphantasm.progressivebotsystem", "APBS", "2.5.0");
        Ping(false, serverMods);
        Require(Reason() == null, "A hard dependency of an allowed mod is allowed");
        string notes = ArenaWhitelistAdmission.WarningLine;
        Require(notes.Contains("Example Lib: pulled in as hard dependency of ABPS") && notes.Contains("APBS: version 2.5.0 differs from reviewed 2.4.1"),
            "The pulled-in dependency and the server version difference are both noted");

        // More than four notes are counted.
        ResetWorld();
        foreach (var (guid, name, version) in new[] { ("me.sol.sain", "SAIN", "9.0"), ("xyz.drakia.bigbrain", "BigBrain", "9.0"),
                     ("xyz.drakia.waypoints", "Waypoints", "9.0"), ("com.acidphantasm.botplacementsystem", "ABPS", "9.0") })
            Chainloader.PluginInfos[guid].Metadata.Version = Version.Parse(version);
        var serverNotes = NormalServerMods();
        serverNotes[2] = Mod("com.acidphantasm.progressivebotsystem", "APBS", "9.0");
        Ping(false, serverNotes);
        Require(Reason() == null && ArenaWhitelistAdmission.WarningLine.EndsWith("; and 1 more", StringComparison.Ordinal),
            "Notes are capped at four lines and the rest counted");
    }

    private static void CheckPolicyFiles()
    {
        // Missing default file: fail open in a normal session, one warning, no session.
        ResetWorld();
        File.Delete(_policyPath);
        AddPlugin("com.example.zombies", "Zombie Pack", "1.0.0");
        Ping(false, NormalServerMods());
        Require(Reason() == null, "Without a policy file a normal session is not refused (the adapters still apply)");
        Require(ArenaMode.Plugin.ModLogger.Warnings.Count(w => w.Contains("whitelist unavailable")) == 1,
            "The missing policy is reported once");
        Require(!ArenaWhitelistAdmission.TryPrepareSessionStart(out _, out _, out _), "No session without a policy");

        // Oversized default file is treated as unreadable.
        ResetWorld();
        WritePolicy(new string(' ', 300 * 1024) + _repoPolicyText);
        AddPlugin("com.example.zombies", "Zombie Pack", "1.0.0");
        Ping(false, NormalServerMods());
        Require(Reason() == null && ArenaMode.Plugin.ModLogger.Warnings.Any(w => w.Contains("larger than 256 KB")),
            "A policy file above 256 KB is refused as a policy and reported");

        // User file: allow, deny, bad schema, bad entry.
        ResetWorld();
        AddPlugin("com.example.zombies", "Zombie Pack", "1.0.0");
        WriteUserPolicy("{\"schema\":1,\"allow\":[{\"guid\":\"com.example.zombies\",\"name\":\"Zombie Pack\",\"kind\":\"mod\"}]}");
        Ping(false, NormalServerMods());
        Require(Reason() == null, "A user allow entry admits the mod");

        ResetWorld();
        WriteUserPolicy("{\"schema\":1,\"deny\":[\"me.sol.sain\"]}");
        Ping(false, NormalServerMods());
        Require(Reason() != null && Reason().Contains("SAIN"), "A user deny entry excludes a default mod");

        ResetWorld();
        AddPlugin("com.example.zombies", "Zombie Pack", "1.0.0");
        WriteUserPolicy("{\"schema\":7,\"allow\":[{\"guid\":\"com.example.zombies\",\"name\":\"Zombie Pack\",\"kind\":\"mod\"}]}");
        Ping(false, NormalServerMods());
        Require(Reason() != null && Reason().Contains("Zombie Pack"), "A user file with an unknown schema is ignored");
        Require(ArenaMode.Plugin.ModLogger.Warnings.Any(w => w.Contains("user whitelist")), "The ignored user file is reported");

        // The answer is cached per ping generation: a file change needs a new ping.
        ResetWorld();
        AddPlugin("com.example.zombies", "Zombie Pack", "1.0.0");
        Ping(false, NormalServerMods());
        Require(Reason() != null, "Refused before the user file exists");
        WriteUserPolicy("{\"schema\":1,\"allow\":[{\"guid\":\"com.example.zombies\",\"name\":\"Zombie Pack\",\"kind\":\"mod\"}]}");
        Require(Reason() != null, "The policy files are not re-read within one ping generation");
        Ping(false, NormalServerMods());
        Require(Reason() == null, "A new ping re-reads them");
    }

    private static void CheckFikaAndFailures()
    {
        // Fika keeps its own refusal: no whitelist refusal, no session, no ping.
        ResetWorld();
        AddPlugin("com.fika.core", "Fika.Core", "1.0.0");
        AddPlugin("com.example.zombies", "Zombie Pack", "1.0.0");
        Ping(false, NormalServerMods());
        Require(Reason() == null && ArenaWhitelistAdmission.WarningLine == null, "With Fika the whitelist admits and leaves the refusal to the Fika rule");
        Require(!ArenaWhitelistAdmission.CanOfferSessionStart("x"), "No session is offered with Fika");
        Require(!ArenaWhitelistAdmission.TryPrepareSessionStart(out _, out _, out string fika) && fika.Contains("Fika"),
            "The session start refuses with Fika");

        // A failure inside the evaluation: fail open in a normal session, closed in an Arena session.
        ResetWorld();
        string pluginPath = Paths.PluginPath;
        Paths.PluginPath = null;
        ArenaServerClient.PingGeneration++;
        Require(Reason() == null, "An internal failure does not refuse a normal session");
        ArenaSession(ReportActive);
        ArenaServerClient.PingGeneration++;
        Require(Reason() != null && Reason().Contains("could not verify the session state"),
            "An internal failure refuses Arena in an Arena session");
        Paths.PluginPath = pluginPath;
    }

    private static void CheckPrepareForScreen()
    {
        ResetWorld();
        ArenaServerClient.OnRefresh = () => Ping(false, NormalServerMods());
        ArenaWhitelistAdmission.PrepareForScreen();
        Require(ArenaServerClient.RefreshCalls == 0, "A clean normal session needs no ping to open the screen");

        AddPlugin("com.example.zombies", "Zombie Pack", "1.0.0");
        ArenaServerClient.PingGeneration++;
        ArenaWhitelistAdmission.PrepareForScreen();
        Require(ArenaServerClient.RefreshCalls == 1, "Excluded client mods ask for the server's ping");
        Require(ArenaWhitelistAdmission.CanOfferSessionStart(Reason()), "After that ping the session is offered on the first display");
        ArenaWhitelistAdmission.PrepareForScreen();
        Require(ArenaServerClient.RefreshCalls == 1, "A known ping is not requested again");

        ResetWorld();
        ArenaServerClient.OnRefresh = () => Ping(true, NormalServerMods());
        ArenaSession(ReportActive);
        ArenaWhitelistAdmission.PrepareForScreen();
        Require(ArenaServerClient.RefreshCalls == 1 && Reason() == null, "An Arena session always asks for the ping");

        ResetWorld();
        AddPlugin("com.fika.core", "Fika.Core", "1.0.0");
        AddPlugin("com.example.zombies", "Zombie Pack", "1.0.0");
        ArenaServerClient.PingGeneration++;
        ArenaWhitelistAdmission.PrepareForScreen();
        Require(ArenaServerClient.RefreshCalls == 0, "Fika never triggers the ping");

        ResetWorld();
        File.Delete(_policyPath);
        AddPlugin("com.example.zombies", "Zombie Pack", "1.0.0");
        ArenaServerClient.PingGeneration++;
        ArenaWhitelistAdmission.PrepareForScreen();
        Require(ArenaServerClient.RefreshCalls == 0, "Without a policy there is nothing to ask the server for");
    }

    // ------------------------------------------------------------------ Arena session

    private static void CheckArenaSessionAdmission()
    {
        ResetWorld();
        ArenaSession(null);
        Require(Reason() != null && Reason().Contains("could not filter the installed mods") && Reason().Contains("did not publish a report"),
            "A session whose gate never ran refuses Arena");
        Require(ArenaWhitelistAdmission.WarningLine == null && !ArenaWhitelistAdmission.CanOfferSessionStart(Reason()),
            "An unfiltered session offers no session start");

        ResetWorld();
        ArenaSessionMode.Initialize(new[] { "game.exe", "-arena-session" }, "{\"schema\":1,\"active\":false,\"error\":\"boom\"}");
        Require(Reason().Contains("preloader gate is inactive: boom"), "The gate's own error is shown");

        ResetWorld();
        ArenaSession(ReportActive);
        Require(Reason() == null, "An active gate with an unknown ping admits (the click re-checks after the ping)");

        Ping(false, NormalServerMods());
        Require(Reason() != null && Reason().Contains("not connected to the Arena session server"),
            "A server that does not say arenaInstance:true refuses Arena in a session");

        OldServerPing();
        Require(Reason() != null && Reason().Contains("not connected to the Arena session server"),
            "An older server cannot be an Arena instance");

        Ping(true, NormalServerMods());
        Require(Reason() == null, "Active gate plus Arena instance admits");
        Equal(ArenaWhitelistAdmission.WarningLine, "Notes: SAIN: version 4.5.2 differs from reviewed 4.5.1",
            "The gate's warnings are the second line");

        var shifted = NormalServerMods();
        shifted[1] = Mod("me.sol.sain", "SAIN", "4.6.0");
        Ping(true, shifted);
        Require(ArenaWhitelistAdmission.WarningLine.Contains("version 4.5.2 differs") && ArenaWhitelistAdmission.WarningLine.Contains("version 4.6.0 differs"),
            "Server-side version differences are added to the gate's warnings");

        // An excluded plugin in the gate's report changes nothing here: it was never loaded.
        Require(Reason() == null, "Excluded plugins in the report do not refuse");

        ResetWorld();
        ArenaSession(ReportActive);
        AddPlugin("com.fika.core", "Fika.Core", "1.0.0");
        Ping(false, NormalServerMods());
        Require(Reason() == null, "Fika keeps its own refusal in a session too");

        ResetWorld();
        ArenaSession(ReportActive);
        Ping(true, NormalServerMods());
        Require(!ArenaWhitelistAdmission.TryPrepareSessionStart(out _, out _, out string error) && error.Contains("already"),
            "A session cannot start another session");
    }

    // ------------------------------------------------------------------ switch: pure parts

    private static void CheckProfileAndRequestShape()
    {
        const string id = "0123456789abcdef01234567";
        Equal(ArenaSessionSwitch.ResolveProfileId(new[] { "game.exe", "-token=" + id }, "fallback"), id, "The -token argument wins");
        Equal(ArenaSessionSwitch.ResolveProfileId(new[] { "game.exe", "-TOKEN=" + id }, null), id, "The prefix is case-insensitive");
        Equal(ArenaSessionSwitch.ResolveProfileId(new[] { "game.exe", "-token= " + id + " " }, null), id, "The token is trimmed");
        Equal(ArenaSessionSwitch.ResolveProfileId(new[] { "game.exe" }, "FEDCBA9876543210fedcba98"), "FEDCBA9876543210fedcba98", "Without a token the logged-in profile is used");
        Equal(ArenaSessionSwitch.ResolveProfileId(new[] { "game.exe", "-token=dev_profile_1" }, id), id, "A token the helper would refuse falls back to the logged-in profile");
        Equal(ArenaSessionSwitch.ResolveProfileId(new[] { "game.exe", "-token=dev_profile_1" }, "logged-in_1"), null, "No profile id the helper accepts gives null");
        Equal(ArenaSessionSwitch.ResolveProfileId(new[] { "game.exe", "-token=..\\evil" }, id), id, "A token that is not a plain id falls back");
        Equal(ArenaSessionSwitch.ResolveProfileId(new[] { "game.exe", "-token=a/b" }, "c:d"), null, "No plausible id at all gives null");
        Equal(ArenaSessionSwitch.ResolveProfileId(null, null), null, "Nothing at all gives null");
        Equal(ArenaSessionSwitch.ResolveProfileId(new[] { null, "-token=" }, ""), null, "An empty token and fallback give null");
        Require(ArenaSessionSwitch.IsSafeProfileId(id) && ArenaSessionSwitch.IsSafeProfileId(id.ToUpperInvariant())
            && !ArenaSessionSwitch.IsSafeProfileId(id.Substring(1)) && !ArenaSessionSwitch.IsSafeProfileId(id + "0")
            && !ArenaSessionSwitch.IsSafeProfileId(id.Substring(1) + "g") && !ArenaSessionSwitch.IsSafeProfileId("dev_profile_1")
            && !ArenaSessionSwitch.IsSafeProfileId(new string('a', 64)),
            "Ids are exactly the 24 hexadecimal characters the helper accepts");

        Equal(string.Join("|", ArenaSessionSwitch.SkipExecutable(new[] { "game.exe", "-a", null, "-b=1" })), "-a|-b=1",
            "argv[0] is dropped and nulls are skipped");
        Require(ArenaSessionSwitch.SkipExecutable(null).Count == 0 && ArenaSessionSwitch.SkipExecutable(new[] { "game.exe" }).Count == 0,
            "An empty command line gives no arguments");

        var request = new ArenaSessionRequest
        {
            Direction = "enter",
            GamePid = 1234,
            GameExe = "C:\\SPT41\\EscapeFromTarkov.exe",
            GameDir = "C:\\SPT41",
            GameArgs = new List<string> { "-force-gfx-jobs", "native", "-token=" + id, "-config={\"BackendUrl\":\"http://127.0.0.1:6969\"}" },
            ProfileId = id,
            SptRuntimeDir = "C:\\SPT41\\SPT_Runtime",
            ServerMods = new List<ArenaSessionUnit>
            {
                new ArenaSessionUnit("me.sol.sain", "SAIN", "4.5.1", "C:\\SPT41\\SPT_Runtime\\user\\mods\\Solarint-SAIN-ServerMod", null),
                new ArenaSessionUnit("no.folder", "No folder", "1", null, null),
                new ArenaSessionUnit("", "No guid", "1", "C:\\x", null),
            },
            PolicyHash = "3755e0db97983440",
        };
        string json = ArenaSessionSwitch.BuildRequestJson(request);
        Equal(json,
            "{\"schema\":1,\"direction\":\"enter\",\"gamePid\":1234,\"gameExe\":\"C:\\\\SPT41\\\\EscapeFromTarkov.exe\","
            + "\"gameDir\":\"C:\\\\SPT41\",\"gameArgs\":[\"-force-gfx-jobs\",\"native\",\"-token=" + id + "\","
            + "\"-config={\\\"BackendUrl\\\":\\\"http://127.0.0.1:6969\\\"}\"],\"profileId\":\"" + id + "\","
            + "\"sptRuntimeDir\":\"C:\\\\SPT41\\\\SPT_Runtime\",\"serverMods\":[{\"guid\":\"me.sol.sain\","
            + "\"folder\":\"C:\\\\SPT41\\\\SPT_Runtime\\\\user\\\\mods\\\\Solarint-SAIN-ServerMod\"}],\"policyHash\":\"3755e0db97983440\"}",
            "The request file has the documented keys in the documented order; incomplete mods are left out");
        Require(ArenaMiniJson.TryParse(json, out var parsed, out _) && ArenaMiniJson.GetInt(parsed, "gamePid", 0) == 1234
            && ArenaMiniJson.GetArray(parsed, "gameArgs").Count == 4, "The request file parses back");
    }

    private static void CheckTwoClickGate()
    {
        var gate = new ArenaTwoClickGate();
        Require(!gate.IsArmed(0f), "A fresh gate is not armed");
        Require(!gate.Click(10f) && gate.IsArmed(10f), "The first click only arms");
        Require(gate.IsArmed(15f) && !gate.IsArmed(15.01f), "The window is five seconds");
        Require(gate.Click(15f) && !gate.IsArmed(15f), "A second click inside the window confirms and disarms");
        Require(!gate.Click(15.1f), "The click after a confirmation starts over");

        gate.Reset();
        Require(!gate.IsArmed(15.2f), "Reset disarms");
        Require(!gate.Click(20f), "Arm");
        Require(!gate.Click(25.01f) && gate.IsArmed(25.5f), "A click after the window only re-arms; it never confirms");
        Require(gate.Click(26f), "And that new window confirms");
    }

    // ------------------------------------------------------------------ switch: requests

    private sealed class LaunchRecord
    {
        internal int Calls;
        internal string Helper, Direction, RequestPath;
        internal bool RequestExistedAtLaunch;
        internal int QuitCallsAtLaunch;
        internal bool Succeed = true;
        internal bool Throw;
    }

    private static readonly LaunchRecord Launch = new();

    private static bool DefaultLauncher(string helper, string direction, string requestPath, out int pid, out string error)
    {
        Launch.Calls++;
        Launch.Helper = helper;
        Launch.Direction = direction;
        Launch.RequestPath = requestPath;
        Launch.RequestExistedAtLaunch = File.Exists(requestPath);
        Launch.QuitCallsAtLaunch = Application.QuitCalls;
        if (Launch.Throw) throw new InvalidOperationException("launcher exploded");
        pid = Launch.Succeed ? 4321 : 0;
        error = Launch.Succeed ? null : "The Arena session helper could not be started.";
        return Launch.Succeed;
    }

    private static void ResetLaunch()
    {
        Launch.Calls = 0;
        Launch.Helper = Launch.Direction = Launch.RequestPath = null;
        Launch.Succeed = true;
        Launch.Throw = false;
        if (File.Exists(_requestPath)) File.Delete(_requestPath);
    }

    /// <summary>A normal session that may enter: one excluded mod, a current server.</summary>
    private static void ReadyToEnter()
    {
        ResetWorld();
        ResetLaunch();
        AddPlugin("com.example.zombies", "Zombie Pack", "1.0.0");
        Ping(false, NormalServerMods());
        _ = Reason();
    }

    private static void CheckRequestFailures()
    {
        ReadyToEnter();
        Launch.Succeed = false;
        Require(!ArenaSessionSwitch.Request("enter", null, out string error) && error.Contains("could not be started"),
            "A helper that did not start is reported");
        Require(Launch.Calls == 1 && Application.QuitCalls == 0 && !ArenaSessionSwitch.RestartRequested,
            "The game does not quit when the helper did not start");
        Require(ArenaMode.Plugin.ModLogger.Info.Count(l => l.Contains("Arena session:")) == 0,
            "No Info line for a switch that did not happen");
        Launch.Succeed = true;
        Launch.Throw = true;
        Require(!ArenaSessionSwitch.Request("enter", null, out error) && error.Contains("launcher exploded") && Application.QuitCalls == 0,
            "A launcher exception is caught and the game keeps running");

        ReadyToEnter();
        Require(!ArenaSessionSwitch.Request("sideways", null, out error) && Launch.Calls == 0, "An unknown direction is refused");
        Require(!ArenaSessionSwitch.Request("leave", null, out error) && error.Contains("not an Arena session") && Launch.Calls == 0,
            "Leaving needs an Arena session");

        ReadyToEnter();
        File.Delete(_helperPath);
        Require(!ArenaSessionSwitch.Request("enter", null, out error) && error.Contains("helper is missing") && Launch.Calls == 0,
            "A missing helper is reported before anything is written");
        Require(!File.Exists(_requestPath), "No request file without a helper");

        ReadyToEnter();
        ArenaSessionSwitch.RequestPathProvider = () => null;
        Require(!ArenaSessionSwitch.Request("enter", null, out error) && error.Contains("application data") && Launch.Calls == 0,
            "Without a runtime folder there is no switch");
        ArenaSessionSwitch.RequestPathProvider = () => _requestPath;

        ReadyToEnter();
        ArenaSessionSwitch.CommandLineProvider = () => new[] { "game.exe", "-force-gfx-jobs" };
        Require(!ArenaSessionSwitch.Request("enter", null, out error) && error.Contains("profile") && Launch.Calls == 0,
            "Without a token or a logged-in profile there is no switch");
        Require(!ArenaSessionSwitch.Request("enter", "fallback-profile_1", out error) && error.Contains("profile") && Launch.Calls == 0 && Application.QuitCalls == 0,
            "A logged-in profile id the helper would refuse is no fallback");
        Require(ArenaSessionSwitch.Request("enter", "fedcba9876543210fedcba98", out error) && Launch.Calls == 1,
            "The logged-in profile is the fallback");
        Require(ArenaSessionMode.IsArenaSession == false, "The request itself leaves the process state alone");

        // The helper refuses a request it cannot parse, after the game has quit: such a request must not leave.
        ReadyToEnter();
        string realExe = Paths.ExecutablePath;
        Paths.ExecutablePath = Path.Combine(_root, "elsewhere", "EscapeFromTarkov.exe");
        try
        {
            Require(!ArenaSessionSwitch.Request("enter", null, out error) && error.StartsWith("The Arena session cannot be started: ", StringComparison.Ordinal)
                && error.Contains("gameExe must lie inside gameDir") && Launch.Calls == 0 && Application.QuitCalls == 0 && !File.Exists(_requestPath)
                && !ArenaSessionSwitch.RestartRequested,
                "A request the helper's parser refuses neither writes the file, nor starts the helper, nor quits the game");
        }
        finally
        {
            Paths.ExecutablePath = realExe;
        }

        ReadyToEnter();
        ArenaServerClient.ServerMods = NormalServerMods().Select(u => new ArenaServerModInfo(u.Guid, u.Name, u.Version, string.Empty)).ToList();
        ArenaServerClient.PingGeneration++;
        Require(!ArenaSessionSwitch.Request("enter", null, out error) && Launch.Calls == 0 && Application.QuitCalls == 0,
            "Server mods without a folder give no session request");

        ReadyToEnter();
        OldServerPing();
        Require(!ArenaSessionSwitch.Request("enter", null, out error) && error.Contains("does not support") && Launch.Calls == 0,
            "An older server cannot enter a session");

        ReadyToEnter();
        File.Delete(_policyPath);
        ArenaServerClient.PingGeneration++;
        Require(!ArenaSessionSwitch.Request("enter", null, out error) && Launch.Calls == 0, "Without a policy there is no session");

        ReadyToEnter();
        ArenaSession(ReportActive);
        ArenaServerClient.PingGeneration++;
        Require(!ArenaSessionSwitch.Request("enter", null, out error) && error.Contains("already") && Launch.Calls == 0,
            "Entering needs a normal session");
    }

    private static void CheckRequestSuccess()
    {
        ReadyToEnter();
        Require(ArenaSessionSwitch.Request("enter", null, out string error) && error == null, "A prepared normal session enters");
        Require(Launch.Calls == 1 && Launch.Direction == "enter" && Launch.RequestPath == _requestPath && Launch.Helper == _helperPath
            && Launch.RequestExistedAtLaunch, "The helper starts with the request file already in place");
        Require(Launch.QuitCallsAtLaunch == 0 && Application.QuitCalls == 1,
            "The game quits only after the helper started, exactly once");
        var info = ArenaMode.Plugin.ModLogger.Info.Where(l => l.StartsWith("Arena session:")).ToList();
        Require(info.Count == 1 && info[0].Contains("enter") && info[0].Contains("4321"), "One Info line per request");
        Require(ArenaSessionSwitch.RestartRequested, "The restart is latched");

        string json = File.ReadAllText(_requestPath);
        Require(ArenaMiniJson.TryParse(json, out var parsed, out _), "The request file is valid JSON");
        Equal(ArenaMiniJson.GetString(parsed, "direction"), "enter", "Direction");
        Require(ArenaMiniJson.GetInt(parsed, "gamePid", 0) == Environment.ProcessId, "The game's own process id");
        Equal(ArenaMiniJson.GetString(parsed, "gameExe"), Paths.ExecutablePath, "Game executable from BepInEx");
        Equal(ArenaMiniJson.GetString(parsed, "gameDir"), Paths.GameRootPath, "Game directory from BepInEx");
        Equal(string.Join("|", ArenaMiniJson.GetArray(parsed, "gameArgs").Cast<string>()),
            "-force-gfx-jobs|native|-token=0123456789abcdef01234567", "The command line without argv[0]");
        Equal(ArenaMiniJson.GetString(parsed, "profileId"), "0123456789abcdef01234567", "Profile id from the token");
        Equal(ArenaMiniJson.GetString(parsed, "sptRuntimeDir"), Path.Combine(_gameDir, "SPT_Runtime"), "SPT_Runtime beside the game");
        var mods = ArenaMiniJson.GetArray(parsed, "serverMods");
        Require(mods.Count == 4 && ArenaMiniJson.GetString(mods[1], "guid") == "me.sol.sain"
            && ArenaMiniJson.GetString(mods[1], "folder").EndsWith("SAIN"), "The allowed server mods with their folders");
        Equal(ArenaMiniJson.GetString(parsed, "policyHash"), LoadRepoPolicy().Hash, "The policy hash");

        Require(!ArenaSessionSwitch.Request("enter", null, out error) && error.Contains("already restarting")
            && Launch.Calls == 1 && Application.QuitCalls == 1, "A second request while restarting does nothing");

        // Leaving: from an Arena session, no policy needed, the gate's hash goes along.
        ResetWorld();
        ResetLaunch();
        ArenaSession(ReportActive);
        Ping(true, NormalServerMods());
        ArenaSessionSwitch.CommandLineProvider = () => new[] { Paths.ExecutablePath, "-token=0123456789abcdef01234567", "-arena-session" };
        Require(ArenaSessionSwitch.Request("leave", null, out error) && Launch.Direction == "leave" && Application.QuitCalls == 1,
            "An Arena session returns to the normal session");
        Require(ArenaMiniJson.TryParse(File.ReadAllText(_requestPath), out parsed, out _)
            && ArenaMiniJson.GetString(parsed, "direction") == "leave"
            && ArenaMiniJson.GetArray(parsed, "serverMods").Count == 0
            && ArenaMiniJson.GetString(parsed, "policyHash") == "abc123"
            && ArenaMiniJson.GetArray(parsed, "gameArgs").Cast<string>().Contains("-arena-session"),
            "The leave request carries no server mods, the gate's policy hash and the unchanged command line");
        ArenaSessionSwitch.ResetRequestLatch();
    }

    private static ArenaWhitelistPolicy LoadRepoPolicy()
    {
        Require(ArenaWhitelistPolicy.TryLoad(_repoPolicyText, null, out var policy, out _), "The shipped policy loads");
        return policy;
    }
}
