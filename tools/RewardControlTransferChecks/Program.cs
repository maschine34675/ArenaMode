using ArenaMode.SessionHelper;
using ArenaMode.Shared.Session;

internal static class Program
{
    private static int assertions;
    private static readonly List<string> failures = new();
    private static int Main()
    {
        Check("exact-grant-marker-and-no-profile-writes", f =>
        {
            Require(f.Copy(), "valid grant copied");
            Require(ArenaMiniJson.TryParse(File.ReadAllText(f.Sidecar), out var raw, out _), "sidecar JSON");
            var sidecar = ArenaMiniJson.AsObject(raw);
            Require(sidecar.Count == 5 && ArenaMiniJson.GetInt(raw, "schema", -1) == 1, "bounded sidecar shape");
            Require(ArenaMiniJson.Write(ArenaMiniJson.GetObject(raw, "grant")) == ArenaMiniJson.Write(f.Grant), "exact export object");
            Require(ArenaMiniJson.GetString(raw, "privateRoot") == f.Tree
                && ArenaMiniJson.GetString(raw, "markerCreatedUtc") == f.Marker.CreatedUtc
                && ArenaMiniJson.GetInt(raw, "port", 0) == f.Marker.Port, "private marker binding");
            Require(File.ReadAllText(f.NormalProfile) == "normal-profile-sentinel"
                && File.ReadAllText(f.PrivateProfile) == "private-profile-sentinel", "profiles untouched");
            Require(f.Log.Count == 0, "no token-bearing success log");
        });
        Check("old-request-no-grant", f =>
        {
            Require(!f.Copy(null) && !File.Exists(f.Sidecar), "absent requested grant is read-only");
            Require(f.Log.Count == 0, "old request stays quiet");
        });
        Check("marker-required-before-copy", f => { File.Delete(f.MarkerPath); Refuse(f); });
        Check("marker-tamper", f => { File.WriteAllText(f.MarkerPath, f.Marker.ToJson().Replace("policy", "tampered")); Refuse(f); });
        Check("marker-other-session", f => { var old = f.Marker.CreatedUtc; f.Marker.CreatedUtc = f.Now.AddMinutes(-2).ToString("O"); Refuse(f); f.Marker.CreatedUtc = old; });
        Check("marker-other-profile", f => { f.Marker.ProfileId = new string('b', 24); f.SaveMarker(); Refuse(f); });
        Check("marker-other-control", f => { f.Marker.RewardControlId = new string('b', 32); f.SaveMarker(); Refuse(f); });
        Check("marker-other-runtime", f => { f.Marker.SourceRuntime = f.Tree; f.SaveMarker(); Refuse(f); });
        Check("marker-other-server", f => { f.Marker.ServerExe = Path.Combine(f.Normal, "SPT.Server.exe"); f.SaveMarker(); Refuse(f); });
        Check("normal-must-not-be-private", f => { File.WriteAllText(Path.Combine(f.Normal, ArenaSessionContract.MarkerFileName), "{}"); Refuse(f); });
        Check("grant-oversized", f => { File.WriteAllText(f.Export, new string('x', HelperRewardControlTransfer.MaximumBytes + 1)); Refuse(f); });
        Check("grant-malformed", f => { File.WriteAllText(f.Export, "{broken"); Refuse(f); });
        Check("grant-empty", f => { File.WriteAllText(f.Export, ""); Refuse(f); });
        Check("grant-missing", f => { File.Delete(f.Export); Refuse(f); });
        Check("grant-duplicate", f => { File.WriteAllText(f.Export, File.ReadAllText(f.Export).Replace("\"schema\":1", "\"schema\":1,\"schema\":1")); Refuse(f); });
        Check("stale-sidecar-not-reused", f => { File.WriteAllText(f.Sidecar, "stale bearer"); File.Delete(f.Export); Refuse(f); });
        Check("missing-control-never-reads-export", f => { File.Delete(f.Export); Require(!f.Copy(null) && f.Log.Count == 0, "old helper request optional"); });
        foreach (bool available in new[] { true, false })
            Check("tree-build-control-" + available, f =>
            {
                File.WriteAllText(Path.Combine(f.Normal, "SPT.Server.exe"), "synthetic executable, never run");
                File.WriteAllText(Path.Combine(f.Normal, "sptLogger.json"), "{\"loggers\":[]}");
                string config = Path.Combine(f.Normal, "SPT_Data", "configs"); Directory.CreateDirectory(config);
                File.WriteAllText(Path.Combine(config, "http.json"), "{\"ip\":\"127.0.0.1\",\"port\":6969,\"backendIp\":\"127.0.0.1\",\"backendPort\":6969}");
                if (!available) File.Delete(f.Export);
                string ownedRuntime = Path.Combine(f.Root, "built-session");
                var request = new HelperRequest { Schema = 1, Direction = "enter", ProfileId = Fixture.Profile,
                    SptRuntimeDir = f.Normal, RewardControlId = Fixture.Control, PolicyHash = "policy" };
                var result = HelperServerTree.Build(request, 6970, ownedRuntime, "synthetic", f.Now, f.Log.Add);
                string markerJson = File.ReadAllText(Path.Combine(result.TreePath, ArenaSessionContract.MarkerFileName));
                Require(HelperMarker.TryParse(markerJson, out var builtMarker, out _) && builtMarker.RewardControlId == Fixture.Control,
                    "completed marker retains requested identity even when optional control is unavailable");
                Require(File.Exists(Path.Combine(result.TreePath, HelperRewardControlTransfer.FileName)) == available,
                    "Build copies control only after marker and stays usable without export");
                Require(File.ReadAllText(f.NormalProfile) == "normal-profile-sentinel", "normal profile preserved through Build");
            });

        var changes = new Dictionary<string, (string Field, object Value)>
        {
            ["schema"] = ("schema", 2), ["extra"] = ("extra", true),
            ["control-id"] = ("controlId", new string('b', 32)),
            ["profile-id"] = ("profileId", new string('b', 24)),
            ["path-traversal"] = ("runtimeRoot", "C:\\not-the-normal-runtime"),
            ["token-size"] = ("token", Convert.ToBase64String(new byte[31])),
            ["token-base64"] = ("token", "not-base64"),
            ["token-spaces"] = ("token", Convert.ToBase64String(new byte[32]) + "\r\n"),
            ["pin-size"] = ("certificateSha256", new string('a', 63)),
            ["pin-case"] = ("certificateSha256", new string('A', 64)),
            ["http"] = ("originUrl", "http://127.0.0.1:6969"),
            ["remote"] = ("originUrl", "https://example.com:6969"),
            ["dns-localhost"] = ("originUrl", "https://localhost:6969"),
            ["userinfo"] = ("originUrl", "https://secret@127.0.0.1:6969"),
            ["path"] = ("originUrl", "https://127.0.0.1:6969/path"),
            ["query"] = ("originUrl", "https://127.0.0.1:6969?token=x"),
            ["fragment"] = ("originUrl", "https://127.0.0.1:6969#test"),
            ["private-port"] = ("originUrl", "https://127.0.0.1:6970"),
            ["non-iso-date"] = ("issuedUtc", "October 25, 2026"),
        };
        foreach (var change in changes)
            Check("reject-" + change.Key, f => { f.Grant[change.Value.Field] = change.Value.Value; f.SaveGrant(); Refuse(f); });
        foreach (var timing in new[] { "expired", "future", "stale", "overlong", "inverted" })
            Check("time-" + timing, f =>
            {
                f.Grant[timing == "future" || timing == "stale" ? "issuedUtc" : "expiresUtc"] = timing switch
                {
                    "expired" => f.Now.AddSeconds(-1).ToString("O"),
                    "future" => f.Now.AddMinutes(3).ToString("O"),
                    "stale" => f.Now.AddMinutes(-6).ToString("O"),
                    "overlong" => f.Now.AddHours(12).AddSeconds(1).ToString("O"),
                    _ => f.Now.AddMinutes(-1).ToString("O")
                };
                f.SaveGrant(); Refuse(f);
            });
        Check("small-clock-skew", f => { f.Grant["issuedUtc"] = f.Now.AddMinutes(1).ToString("O"); f.SaveGrant(); Require(f.Copy(), "small future skew accepted"); });
        Check("ipv6-loopback", f => { f.Grant["originUrl"] = "https://[::1]:6969"; f.SaveGrant(); Require(f.Copy(), "literal IPv6 loopback accepted"); });
        foreach (string location in new[] { "export", "authority-parent", "marker", "private-root", "destination", "source-root" })
            Check("reject-reparse-" + location, f =>
            {
                string outside = Path.Combine(f.Root, "outside"); Directory.CreateDirectory(outside);
                File.WriteAllText(Path.Combine(outside, "sentinel"), "untouched");
                string target = location switch { "export" => f.Export, "authority-parent" => Path.GetDirectoryName(f.Export),
                    "marker" => f.MarkerPath, "private-root" => f.Tree, "destination" => f.Sidecar, _ => f.Normal };
                if (location is "export" or "marker" or "destination")
                {
                    string file = Path.Combine(outside, "linked.json");
                    File.WriteAllText(file, location == "marker" ? f.Marker.ToJson() : ArenaMiniJson.Write(f.Grant));
                    if (File.Exists(target)) File.Delete(target);
                    File.CreateSymbolicLink(target, file);
                }
                else
                {
                    // Both link and target are synthetic, exclusively owned by this fixture.
                    Directory.Delete(target, true); Directory.CreateSymbolicLink(target, outside);
                }
                Require(!f.Copy(), "reparse refused");
                Require(File.ReadAllText(Path.Combine(outside, "sentinel")) == "untouched", "link target unchanged");
            });
        Check("optional-helper-request-contract", f =>
        {
            var request = new Dictionary<string, object> { ["schema"] = 1, ["direction"] = "enter", ["gamePid"] = 123,
                ["gameDir"] = f.Root, ["gameExe"] = Path.Combine(f.Root, "EscapeFromTarkov.exe"), ["gameArgs"] = new List<object>(),
                ["profileId"] = Fixture.Profile, ["sptRuntimeDir"] = f.Normal, ["policyHash"] = "policy",
                ["serverMods"] = new List<object> { new Dictionary<string, object> { ["guid"] = "com.maschine.ArenaMode",
                    ["folder"] = Path.Combine(f.Normal, "user", "mods", "maschine-ArenaMode") } } };
            Require(HelperRequest.TryParse(ArenaMiniJson.Write(request), "enter", out var old, out _) && old.RewardControlId == null, "legacy request accepted");
            request["rewardControlId"] = Fixture.Control;
            Require(HelperRequest.TryParse(ArenaMiniJson.Write(request), "enter", out var current, out _) && current.RewardControlId == Fixture.Control, "control request accepted");
            Require(HelperRequest.TryParse(current.ToJson(), "enter", out var roundtrip, out _) && roundtrip.RewardControlId == Fixture.Control, "request roundtrip preserves control");
            Require(HelperMarker.TryParse(f.Marker.ToJson(), out var marker, out _) && marker.RewardControlId == Fixture.Control, "marker roundtrip preserves control");
            Require(!HelperMarker.TryParse(f.Marker.ToJson().Replace(Fixture.Control, "invalid"), out _, out _), "invalid marker control rejected");
            foreach (object bad in new object[] { null, "", "../path", Fixture.Control.ToUpperInvariant(), Guid.NewGuid().ToString("D"), 12 })
            { request["rewardControlId"] = bad; Require(!HelperRequest.TryParse(ArenaMiniJson.Write(request), "enter", out _, out _), "malformed optional control rejected"); }
            request["rewardControlId"] = Fixture.Control; request["direction"] = "leave";
            Require(!HelperRequest.TryParse(ArenaMiniJson.Write(request), "leave", out _, out _), "leave request cannot carry control");
        });
        foreach (string failure in failures) Console.Error.WriteLine(failure);
        Console.WriteLine($"REWARD_CONTROL_TRANSFER_CHECKS {(failures.Count == 0 ? "PASS" : "FAIL")} assertions={assertions} failures={failures.Count}");
        return failures.Count == 0 ? 0 : 1;
    }

    private static void Refuse(Fixture f)
    {
        Require(!f.Copy(), "invalid grant refused"); Require(!File.Exists(f.Sidecar), "no private authority published");
        Require(f.Log.Count == 1 && f.Log[0] == "Arena reward control unavailable; private-session rewards remain read-only.", "concise fixed diagnostic");
        Require(File.ReadAllText(f.NormalProfile) == "normal-profile-sentinel", "normal profile unchanged");
    }
    private static void Check(string name, Action<Fixture> test)
    {
        try { using var fixture = new Fixture(); test(fixture); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failures.Add("FAIL " + name + ": " + error.Message); }
    }
    private static void Require(bool ok, string message) { assertions++; if (!ok) throw new Exception(message); }
    private sealed class Fixture : IDisposable
    {
        public const string Profile = "aaaaaaaaaaaaaaaaaaaaaaaa", Control = "0123456789abcdef0123456789abcdef";
        public readonly string Root = Path.Combine(Path.GetTempPath(), "ArenaRewardControl-" + Guid.NewGuid().ToString("N"));
        public readonly DateTime Now = DateTime.UtcNow;
        public string Normal => Path.Combine(Root, "normal"); public string Tree => Path.Combine(Root, "private");
        public string Export => Path.Combine(Normal, "user", "arena-rewards", Profile, "control.json");
        public string Sidecar => Path.Combine(Tree, HelperRewardControlTransfer.FileName);
        public string MarkerPath => Path.Combine(Tree, ArenaSessionContract.MarkerFileName);
        public string NormalProfile => Path.Combine(Normal, "user", "profiles", Profile + ".json");
        public string PrivateProfile => Path.Combine(Tree, "user", "profiles", Profile + ".json");
        public readonly HelperMarker Marker;
        public readonly Dictionary<string, object> Grant;
        public readonly List<string> Log = new();
        public Fixture()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Export)); Directory.CreateDirectory(Tree);
            Directory.CreateDirectory(Path.GetDirectoryName(NormalProfile)); Directory.CreateDirectory(Path.GetDirectoryName(PrivateProfile));
            File.WriteAllText(NormalProfile, "normal-profile-sentinel"); File.WriteAllText(PrivateProfile, "private-profile-sentinel");
            Marker = HelperMarker.Create(Now, Profile, 6970, Normal, "policy", "test", Path.Combine(Tree, "SPT.Server.exe"));
            Marker.RewardControlId = Control; SaveMarker();
            Grant = new() { ["schema"] = 1, ["controlId"] = Control, ["profileId"] = Profile, ["token"] = Convert.ToBase64String(new byte[32]),
                ["runtimeRoot"] = Normal, ["originUrl"] = "https://127.0.0.1:6969", ["certificateSha256"] = new string('a', 64),
                ["issuedUtc"] = Now.ToString("O"), ["expiresUtc"] = Now.AddHours(12).ToString("O") }; SaveGrant();
        }
        public void SaveGrant() => File.WriteAllText(Export, ArenaMiniJson.Write(Grant));
        public void SaveMarker() => File.WriteAllText(MarkerPath, Marker.ToJson());
        public bool Copy(string controlId = Control) => HelperRewardControlTransfer.TryCopy(Normal, Profile, controlId, Tree, Marker, Now, Log.Add);
        public void Dispose()
        {
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(Root).StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(Root).StartsWith("ArenaRewardControl-", StringComparison.Ordinal))
                throw new Exception("Fixture cleanup path is not owned");
            Directory.Delete(Root, true);
        }
    }
}
