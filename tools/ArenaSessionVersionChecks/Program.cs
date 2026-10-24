using ArenaMode.Session;
using ArenaMode.Shared.Session;
using BepInEx;
using BepInEx.Bootstrap;
using System.Text.Json;
using ArenaMode.SessionHelper;

internal static class Program
{
    private static int _checks;
    private static string _root, _policy;
    private const string Guid = "tests.two-part-mod";

    private static void Need(bool condition, string name)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException("ASSERT: " + name);
    }

    private static int Count(string value, string part) => value == null ? 0 : value.Split(part).Length - 1;

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static void Reset(string serverReviewed = "0.10.3")
    {
        ArenaServerClient.Reset();
        Chainloader.PluginInfos.Clear();
        ArenaMode.Plugin.ModLogger.Clear();
        ArenaSessionMode.Initialize(new[] { Paths.ExecutablePath }, null);
        _policy = JsonSerializer.Serialize(new
        {
            schema = 1,
            mods = new[] {
                new { guid = Guid, name = "Canonical mod name", kind = "mod", clientVersion = "0.10.3", serverVersion = serverReviewed },
                new { guid = "tests.other-mod", name = "Other mod", kind = "mod", clientVersion = "0.10.3", serverVersion = "0.10.3" }
            }
        });
        Write(Path.Combine(Paths.PluginPath, ArenaSessionContract.PluginFolderName, ArenaSessionContract.DefaultPolicyFileName), _policy);
        Write(Path.Combine(Paths.PluginPath, ArenaSessionContract.PluginFolderName, ArenaSessionContract.HelperFileName), "fixture");
        Write(Path.Combine(Paths.GameRootPath, "SPT_Runtime", "SPT.Server.exe"), "fixture");
        SPT.Common.Http.RequestHandler.IsLocal = true;
    }

    private static void Plugin(string guid, string name, string version)
    {
        Chainloader.PluginInfos[guid] = new PluginInfo
        {
            Metadata = new BepInPlugin { GUID = guid, Name = name, Version = Version.Parse(version) },
            Location = Path.Combine(Paths.PluginPath, guid + ".dll")
        };
    }

    private static ArenaServerModInfo Mod(string guid, string name, string version)
        => new(guid, name, version, Path.Combine(Paths.GameRootPath, "SPT_Runtime", "user", "mods", guid));

    private static void Ping(string version = "1.0.0", string name = "Server Alias")
    {
        ArenaServerClient.IsServerModKnownAvailable = true;
        ArenaServerClient.SessionFieldsPresent = true;
        ArenaServerClient.ServerMods = new[] { Mod(ArenaSessionContract.ArenaPluginGuid, "ArenaMode", "0.2.0"), Mod(Guid, name, version) };
        ArenaServerClient.PingGeneration++;
    }

    private static void CheckNotes()
    {
        Reset();
        Plugin(Guid, "Client Alias", "1.0.0");
        Ping();
        Need(ArenaWhitelistAdmission.GetRejectionReason() == null, "version drift keeps direct play available");
        string notes = ArenaWhitelistAdmission.WarningLine;
        Need(Count(notes, "differs from reviewed") == 1 && notes.Contains("Client Alias"), "client/server alias dedup");
        Need(ArenaMode.Plugin.ModLogger.Info.Count(x => x.Contains("differs from reviewed")) == 1, "version alias logged once");
        Ping();
        _ = ArenaWhitelistAdmission.WarningLine;
        Need(ArenaMode.Plugin.ModLogger.Info.Count(x => x.Contains("differs from reviewed")) == 1, "refresh does not duplicate log");

        Reset();
        Plugin(Guid, "Client Alias", "1.0.0");
        Ping("1.0.0.0");
        Need(Count(ArenaWhitelistAdmission.WarningLine, "differs from reviewed") == 1, "version spelling dedup");

        Reset();
        Plugin(Guid, "Client Alias", "1.0.0");
        Ping("1.1.0");
        Need(Count(ArenaWhitelistAdmission.WarningLine, "differs from reviewed") == 2, "different installed versions retained");

        Reset("0.10.4");
        Plugin(Guid, "Client Alias", "1.0.0");
        Ping();
        Need(Count(ArenaWhitelistAdmission.WarningLine, "differs from reviewed") == 2, "different reviewed versions retained");

        Reset();
        Plugin(Guid, "Same Name", "1.0.0");
        Plugin("tests.other-mod", "Same Name", "1.0.0");
        Ping();
        Need(Count(ArenaWhitelistAdmission.WarningLine, "differs from reviewed") == 2, "distinct GUIDs retained");
        Reset();
        Plugin(Guid, "Same Name", "1.0.0");
        Plugin("tests.other-mod", "same name", "1.0.0");
        Ping();
        Need(Count(ArenaWhitelistAdmission.WarningLine, "differs from reviewed") == 2, "distinct GUIDs with case-only names retained");

        Reset();
        Plugin(Guid, "Client Alias", "1.0.0");
        Ping();
        Need(ArenaWhitelistPolicy.TryLoad(_policy, null, out var policy, out _), "fixture policy loads");
        var client = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client,
            new[] { new ArenaSessionUnit(Guid, "Client Alias", "1.0.0", "fixture.dll", null) });
        var report = ArenaGateReport.FromDecision(policy, client, Array.Empty<string>());
        ArenaSessionMode.Initialize(new[] { Paths.ExecutablePath, "-arena-session" }, report.ToJson());
        ArenaServerClient.IsArenaInstance = true;
        ArenaServerClient.PolicyHash = policy.Hash;
        ArenaServerClient.PingGeneration++;
        Need(ArenaWhitelistAdmission.GetRejectionReason() == null, "fixture Arena session admits");
        Need(Count(ArenaWhitelistAdmission.WarningLine, "differs from reviewed") == 1, "gate/server alias dedup");
    }

    private static ArenaSessionUnit Unit(string guid, string version, params string[] dependencies)
        => new(guid, guid, version, Path.Combine(Paths.GameRootPath, "SPT_Runtime", "user", "mods", guid), dependencies);

    private static ArenaWhitelistPolicy Policy(string user = null)
    {
        Need(ArenaWhitelistPolicy.TryLoad(_policy, user, out var policy, out _), "policy fixture loads");
        return policy;
    }

    private static void CheckPlan()
    {
        Reset();
        var policy = Policy();
        var client = new[] { Unit(Guid, "1.0.0"), Unit("tests.other-mod", "0.10.3", Guid),
            Unit(ArenaSessionContract.SptPlatformGuids[0], "9.0.0"), Unit(ArenaSessionContract.ArenaPluginGuid, "0.2.0") };
        var server = new[] { Unit(Guid, "0.10.3"), Unit("tests.other-mod", "0.10.3"), Unit(ArenaSessionContract.ArenaPluginGuid, "0.2.0") };
        var plan = ArenaSessionVersionPlan.Create(policy, client, server);
        Need(plan.ExcludedGuids.SequenceEqual(new[] { "tests.other-mod", Guid }), "client drift and cross-side dependant closure");
        Need(plan.TryApply(policy, out var effective, out _), "strict plan validates");
        Need(ArenaWhitelistDecision.Evaluate(effective, ArenaWhitelistSide.Client, client).Allowed.Count == 2, "platform and Arena are preserved");
        Need(ArenaWhitelistDecision.Evaluate(effective, ArenaWhitelistSide.Server, server).Allowed.Single().Guid == ArenaSessionContract.ArenaPluginGuid,
            "same GUID and dependant excluded on server");
        Need(policy.FindPlugin(Guid, ArenaWhitelistSide.Client) != null && !policy.IsDeniedForSession(Guid), "original policy unchanged");
        Need(plan.PolicyHash != policy.Hash, "strict hash differs from ordinary policy");
        var serverDrift = ArenaSessionVersionPlan.Create(policy, new[] { Unit(Guid, "0.10.3") }, new[] { Unit(Guid, "1.0.0") });
        Need(serverDrift.ExcludedGuids.Contains(Guid), "server-only drift excludes both sides");
        var unreviewed = ArenaSessionVersionPlan.Create(policy, new[] { Unit("unknown", "8.0.0") }, Array.Empty<ArenaSessionUnit>());
        Need(unreviewed.ExcludedGuids.Count == 0, "unknown mods retain normal whitelist semantics");
        var userPolicy = Policy("{\"schema\":1,\"unlisted\":\"allow\",\"allow\":[{\"guid\":\"tests.two-part-mod\",\"name\":\"Override\",\"clientVersion\":\"0.10.4\",\"side\":\"client\"}]}");
        var sidePlan = ArenaSessionVersionPlan.Create(userPolicy, new[] { Unit(Guid, "0.10.3") }, new[] { Unit(Guid, "0.10.3") });
        Need(sidePlan.ExcludedGuids.Contains(Guid), "client-only user review excludes server counterpart");
        Need(sidePlan.TryApply(userPolicy, out var sideEffective, out _) && !sideEffective.IsUnlistedAllowed(Guid), "strict omission wins over blacklist allow");
        var denied = Policy("{\"schema\":1,\"deny\":[\"tests.other-mod\"]}");
        var deniedPlan = ArenaSessionVersionPlan.Create(denied, client, server);
        Need(deniedPlan.TryApply(denied, out var deniedEffective, out _) && deniedEffective.IsDeniedByUser("tests.other-mod"), "user deny preserved");
        Need(!plan.TryApply(denied, out _, out _), "changed policy refuses strict plan");
        Need(!plan.ValidateAllowed(effective, ArenaWhitelistSide.Client, new[] { Unit("tests.other-mod", "0.10.3") }, out _), "omitted runtime unit refused");
        var noOther = ArenaSessionVersionPlan.Create(policy, new[] { Unit(Guid, "1.0.0"), Unit("tests.other-mod", "0.10.3") }, server.Take(1));
        Need(noOther.TryApply(policy, out var cleanEffective, out _), "isolated drift plan validates");
        Need(!noOther.ValidateAllowed(cleanEffective, ArenaWhitelistSide.Client, new[] { Unit("tests.other-mod", "1.0.0") }, out _), "new drift during restart refused");
        Need(!noOther.ValidateAllowed(cleanEffective, ArenaWhitelistSide.Client, new[] { Unit("tests.other-mod", "0.10.3", Guid) }, out _), "changed hard dependency on absent omitted mod refused");
        bool protectedRefused = false;
        try { ArenaSessionVersionPlan.Create(policy, new[] { Unit(Guid, "1.0.0"), Unit(ArenaSessionContract.ArenaPluginGuid, "0.2.0", Guid) }, server); }
        catch (InvalidOperationException) { protectedRefused = true; }
        Need(protectedRefused, "required Arena dependency cannot be silently removed");
        object json;
        Need(ArenaMiniJson.TryParse(ArenaMiniJson.Write(plan.ToObject()), out json, out _)
            && ArenaSessionVersionPlan.TryRead(json, out var parsed, out _) && parsed.TryApply(policy, out _, out _), "plan JSON round trip validates");
        var broken = ArenaMiniJson.AsObject(json);
        broken["excludedGuids"] = new List<object> { Guid };
        Need(ArenaSessionVersionPlan.TryRead(broken, out var changed, out _) && !changed.TryApply(policy, out _, out _), "changed omissions rejected");
        broken["excludedGuids"] = new List<object> { Guid, Guid };
        Need(!ArenaSessionVersionPlan.TryRead(broken, out _, out _), "duplicate omission rejected");
        ArenaMiniJson.TryParse(ArenaMiniJson.Write(plan.ToObject()), out var rawPlanFixture, out _);
        var changedBase = ArenaMiniJson.AsObject(rawPlanFixture);
        changedBase["basePolicyHash"] = "wrong-base";
        Need(ArenaSessionVersionPlan.TryRead(changedBase, out var wrongBase, out _) && !wrongBase.TryApply(policy, out _, out _), "tampered base policy identity refused");
        Need(!ArenaSessionVersionPlan.TryRead(null, out _, out _), "missing plan rejected");
        broken = plan.ToObject(); broken["clientUnits"] = "bad";
        Need(!ArenaSessionVersionPlan.TryRead(broken, out _, out _), "malformed inventory rejected");
    }

    private static void CheckOfferAndContract()
    {
        Reset();
        Plugin(Guid, "Client Alias", "1.0.0");
        ArenaServerClient.OnRefresh = () => Ping("0.10.3");
        ArenaWhitelistAdmission.PrepareForScreen();
        Need(ArenaServerClient.RefreshCalls == 1 && ArenaWhitelistAdmission.GetRejectionReason() == null, "version-only offer refreshes ping without refusal");
        Need(ArenaWhitelistAdmission.CanOfferSessionStart(null), "version-only offer available");
        Need(!ArenaWhitelistAdmission.CanOfferSessionStart("changed API contract") && ArenaWhitelistAdmission.CanOfferVersionSessionStart(),
            "other eligibility refusal keeps explicit optional version session available");
        Need(ArenaWhitelistAdmission.VersionSessionText.Contains("Client Alias") && ArenaWhitelistAdmission.VersionSessionText.Contains("restart without"), "offer explains omitted mods");
        Need(ArenaWhitelistAdmission.TryPrepareSessionStart(out var hash, out var mods, out var plan, out _), "strict switch can be prepared");
        Need(plan != null && hash == plan.PolicyHash && mods.Count == 1 && mods[0].Guid == ArenaSessionContract.ArenaPluginGuid,
            "client-only drift removes matching server version from request");
        var request = new ArenaSessionRequest {
            Direction = "enter", GamePid = 12345, GameExe = Paths.ExecutablePath, GameDir = Paths.GameRootPath,
            SptRuntimeDir = Path.Combine(Paths.GameRootPath, "SPT_Runtime"), ProfileId = "123456789012345678901234",
            PolicyHash = hash, ServerMods = mods, VersionPlan = plan,
            GameArgs = new List<string> { "-config={\"BackendUrl\":\"https://127.0.0.1:6969\"}" }
        };
        string requestJson = ArenaSessionSwitch.BuildRequestJson(request);
        Need(requestJson.Contains("\"schema\":2"), "strict request uses unsupported-by-old-helper schema");
        Need(HelperRequest.TryParse(requestJson, "enter", out var parsed, out _) && parsed.ValidateVersionPlan(out _), "helper accepts valid strict plan");
        Need(!HelperRequest.TryParse(requestJson.Replace("\"schema\":2", "\"schema\":1"), "enter", out _, out _), "strict plan cannot downgrade schema");
        ArenaMiniJson.TryParse(requestJson, out var raw, out _);
        var root = ArenaMiniJson.AsObject(raw); root.Remove("versionPlan");
        Need(!HelperRequest.TryParse(ArenaMiniJson.Write(root), "enter", out _, out _), "schema 2 missing plan refused");
        string policyFile = Path.Combine(Paths.PluginPath, ArenaSessionContract.PluginFolderName, ArenaSessionContract.DefaultPolicyFileName);
        string before = File.ReadAllText(policyFile);
        Write(policyFile, "{\"schema\":1,\"mods\":[]}");
        Need(!parsed.ValidateVersionPlan(out _), "helper refuses changed policy before tree writes");
        Write(policyFile, before);
        parsed.ServerMods.Add(new HelperServerMod { Guid = Guid, Folder = Unit(Guid, "1.0.0").File });
        Need(!parsed.ValidateVersionPlan(out _), "helper refuses reinserted omitted server mod");
        Need(File.ReadAllText(policyFile) == before, "session operations do not modify source policy");
        Need(HelperCommandLine.TryRewriteGameArguments(new[] { request.GameArgs[0], "-arena-session", ArenaSessionContract.VersionPlanFlag + hash }, false, 6969, out var args, out _)
            && args.All(x => !x.StartsWith("-arena-")), "return removes all session restriction flags");
        var policy = Policy();
        Need(plan.TryApply(policy, out var effective, out _), "gate fixture effective policy");
        var decision = ArenaWhitelistDecision.Evaluate(effective, ArenaWhitelistSide.Client, plan.ClientUnits);
        var report = ArenaGateReport.FromDecision(effective, decision, Array.Empty<string>());
        string[] strictArgs = { Paths.ExecutablePath, "-arena-session", ArenaSessionContract.VersionPlanFlag + hash };
        ArenaSessionMode.Initialize(strictArgs, report.ToJson());
        Need(!ArenaSessionMode.GateActive && ArenaSessionMode.Problem.Contains("did not verify"), "old gate cannot admit strict session");
        report.VersionPlan = plan;
        ArenaSessionMode.Initialize(strictArgs, report.ToJson());
        Need(ArenaSessionMode.GateActive, "matching strict gate admitted");
        Chainloader.PluginInfos.Clear();
        ArenaServerClient.IsArenaInstance = true;
        ArenaServerClient.ServerMods = new[] { Mod(ArenaSessionContract.ArenaPluginGuid, "Arena", "0.2.0") };
        ArenaServerClient.PolicyHash = hash;
        ArenaServerClient.PingGeneration++;
        Need(ArenaWhitelistAdmission.GetRejectionReason() == null, "matching strict server admitted");
        ArenaServerClient.PolicyHash = null; ArenaServerClient.PingGeneration++;
        Need(ArenaWhitelistAdmission.GetRejectionReason()?.Contains("did not verify") == true, "strict session requires server hash");
        ArenaServerClient.PolicyHash = hash;
        ArenaServerClient.ServerMods = new[] { Mod(ArenaSessionContract.ArenaPluginGuid, "Arena", "0.2.0"), Mod(Guid, "Server Alias", "0.10.3") };
        ArenaServerClient.PingGeneration++;
        Need(ArenaWhitelistAdmission.GetRejectionReason()?.Contains("current policy excludes") == true, "strict session refuses excluded counterpart loaded by server");
        ArenaSessionMode.Initialize(new[] { Paths.ExecutablePath, "-arena-session", ArenaSessionContract.VersionPlanFlag + "changed" }, report.ToJson());
        Need(!ArenaSessionMode.GateActive, "wrong process plan hash refused");
        var marker = HelperMarker.Create(DateTime.UtcNow, request.ProfileId, 6970, request.SptRuntimeDir, hash, "test");
        marker.VersionPlan = plan;
        string markerPath = Path.Combine(_root, "marker.json");
        Write(markerPath, marker.ToJson());
        Need(ArenaSessionVersionPlan.TryReadMarker(markerPath, hash, request.SptRuntimeDir, out _, out _), "gate reads same helper marker");
        Need(!ArenaSessionVersionPlan.TryReadMarker(markerPath, "changed", request.SptRuntimeDir, out _, out _), "changed marker hash refused");
        Need(!ArenaSessionVersionPlan.TryReadMarker(markerPath, hash, Path.Combine(_root, "other"), out _, out _), "foreign installation marker refused");
        Write(markerPath, "{}");
        Need(!ArenaSessionVersionPlan.TryReadMarker(markerPath, hash, request.SptRuntimeDir, out _, out _), "missing marker plan refused");
        Reset(); Ping("1.0.0");
        Need(ArenaWhitelistAdmission.GetRejectionReason() == null && ArenaWhitelistAdmission.CanOfferSessionStart(null), "server-only drift offers optional session");
        Reset(); Plugin(Guid, "Client Alias", "0.10.3"); Ping("0.10.3");
        Need(!ArenaWhitelistAdmission.CanOfferSessionStart(null), "reviewed versions do not offer version-only restart");
        Reset(); Plugin(Guid, "Client Alias", "1.0.0"); Plugin("com.fika.core", "Fika", "1.0.0"); Ping();
        Need(!ArenaWhitelistAdmission.CanOfferVersionSessionStart(), "Fika has no optional filtered session");
        Reset(); Plugin(Guid, "Client Alias", "1.0.0"); Ping();
        Plugin(ArenaSessionContract.ArenaPluginGuid, "Arena", "0.2.0");
        Chainloader.PluginInfos[ArenaSessionContract.ArenaPluginGuid].Dependencies = new[] {
            new BepInDependency { DependencyGUID = Guid, Flags = BepInDependency.DependencyFlags.HardDependency }
        };
        ArenaServerClient.PingGeneration++;
        Need(ArenaWhitelistAdmission.GetRejectionReason() == null && !ArenaWhitelistAdmission.CanOfferVersionSessionStart(),
            "mandatory dependency prevents only optional switch, direct play unchanged");
    }

    private static int Main()
    {
        try
        {
            _root = Path.Combine(Path.GetTempPath(), "arena-session-version-" + System.Guid.NewGuid().ToString("N"));
            Paths.GameRootPath = Path.Combine(_root, "game");
            Paths.ExecutablePath = Path.Combine(Paths.GameRootPath, "EscapeFromTarkov.exe");
            Paths.PluginPath = Path.Combine(Paths.GameRootPath, "BepInEx", "plugins");
            Paths.ConfigPath = Path.Combine(Paths.GameRootPath, "BepInEx", "config");
            Paths.PatcherPluginPath = Path.Combine(Paths.GameRootPath, "BepInEx", "patchers");
            CheckNotes();
            CheckPlan();
            CheckOfferAndContract();
            Console.WriteLine($"ARENA_SESSION_VERSION_BASELINE_PASSED ({_checks} assertions)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (_root != null && Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}
