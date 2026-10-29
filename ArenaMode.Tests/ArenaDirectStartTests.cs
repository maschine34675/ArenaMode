using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ArenaMode.SessionHelper;
using ArenaMode.Shared.Session;

namespace ArenaMode.Tests;

public sealed class ArenaDirectStartTests : IDisposable
{
    private const string Profile = "6ab7db1164f04b565cf78be7";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "arena-direct-start-" + Guid.NewGuid().ToString("N"));
    public ArenaDirectStartTests() { Directory.CreateDirectory(_root); }
    public void Dispose() { Directory.Delete(_root, true); }
    private string Put(string relative, string text)
    {
        string path = Path.Combine(_root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); return path;
    }
    private static string ProfileJson(string id = Profile, bool wipe = false, string side = "Usec", int level = 4) =>
        JsonSerializer.Serialize(new { info = new { id, wipe }, characters = new { pmc = new { Info = new { Nickname = "Test profile", Side = side, Level = level } } } });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultAndExplicitStartNeedNoOldRequest(bool explicitStart)
    {
        Assert.True(HelperCommand.TryParse(explicitStart ? ["start"] : [], out var command, out var error), error);
        Assert.Equal("start", command.Name); Assert.False(command.NeedsRequest); Assert.Null(command.RequestPath); Assert.Null(command.ProfileId);
    }

    [Fact]
    public void ProfileOptionIsExactAndDoesNotAllowRequestReuse()
    {
        Assert.True(HelperCommand.TryParse(["start", "--profile", Profile.ToUpperInvariant()], out var command, out _));
        Assert.Equal(Profile, command.ProfileId);
        foreach (var args in new[] { new[] { "start", "--profile" }, new[] { "start", "--profile", "../other" },
            new[] { "start", "--request", "old.json" }, new[] { "start", "--profile", Profile, "extra" } })
            Assert.False(HelperCommand.TryParse(args, out _, out _));
    }

    [Theory]
    [InlineData(true, "Usec", 4)]
    [InlineData(false, "Savage", 4)]
    [InlineData(false, "Usec", 0)]
    public void WipedAndUninitializedCharactersAreNotOffered(bool wipe, string side, int level)
    {
        Put(Profile + ".json", ProfileJson(wipe: wipe, side: side, level: level));
        Assert.Empty(HelperDirectStart.ReadProfiles(_root));
    }

    [Fact]
    public void ValidProfileSurvivesMalformedSiblingsAndBackupFiles()
    {
        Put(Profile + ".json", ProfileJson());
        Put("111111111111111111111111.json", "{bad json");
        Put("222222222222222222222222.json", ProfileJson()); // file/id mismatch
        Put("444444444444444444444444.json", ProfileJson("444444444444444444444444").Replace("\"wipe\":false", "\"wipe\":false,\"invalidOrUnloadableProfile\":true"));
        var invalid = Put("333333333333333333333333.json", ""); File.WriteAllBytes(invalid, [0xc0, 0xaf]);
        Put(Profile + ".sync-conflict-123.json", ProfileJson());
        var result = Assert.Single(HelperDirectStart.ReadProfiles(_root));
        Assert.Equal(Profile, result.Id); Assert.Equal("USEC", result.Side); Assert.Equal(4, result.Level);
        Assert.Equal(ProfileJson(), File.ReadAllText(Path.Combine(_root, Profile + ".json")));
    }

    [Theory]
    [InlineData("127.0.0.1", 6969, true)]
    [InlineData("::1", 6969, true)]
    [InlineData("0.0.0.0", 6969, false)]
    [InlineData("127.0.0.2", 6969, false)]
    [InlineData("::", 6969, false)]
    [InlineData("192.168.1.2", 6969, false)]
    [InlineData("127.0.0.1", 6979, false)]
    public void OnlyExplicitLocalUnproxiedServerIsAccepted(string ip, int backendPort, bool allowed)
    {
        Put("SPT_Data/configs/http.json", JsonSerializer.Serialize(new { ip, port = 6969, backendPort }));
        if (allowed) Assert.Equal(ip, HelperDirectStart.ReadEndpoint(_root).ServerHost);
        else Assert.Throws<HelperException>(() => HelperDirectStart.ReadEndpoint(_root));
    }

    [Fact]
    public void ProfilePickerDoesNotDenyTheNormalServersOpenWriter()
    {
        string path = Put(Profile + ".json", ProfileJson());
        using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        Assert.Single(HelperDirectStart.ReadProfiles(_root));
        Assert.Equal(ProfileJson(), HelperDirectStart.ReadText(path, 4096));
        writer.Flush();
    }

    [Fact]
    public void NullableNativeWipeFlagDoesNotHideInitializedCharacter()
    {
        Put(Profile + ".json", ProfileJson().Replace("\"wipe\":false", "\"wipe\":null"));
        Assert.Single(HelperDirectStart.ReadProfiles(_root));
    }

    private static Dictionary<string, object> Ping(params (string Id, string[] Dependencies)[] mods)
    {
        return new() { ["arenaInstance"] = false, ["serverMods"] = mods.Select(m => (object)new Dictionary<string, object> {
            ["guid"] = m.Id, ["name"] = m.Id, ["version"] = "99.0.0", ["folder"] = @"C:\SPT\SPT_Runtime\user\mods\" + m.Id,
            ["dependencies"] = m.Dependencies.Cast<object>().ToList() }).ToList() };
    }
    private static ArenaWhitelistPolicy Policy(string? user = null)
    {
        Assert.True(ArenaWhitelistPolicy.TryLoad("""{"schema":1,"mods":[{"guid":"reviewed","name":"Reviewed","kind":"mod","serverVersion":"1.0.0"}],"deny":["blocked"]}""",
            user!, out var policy, out _)); return policy;
    }

    [Fact]
    public void SharedModDecisionKeepsVersionDriftAndExcludesDeniedDependencies()
    {
        var policy = Policy("""{"schema":1,"unlisted":"allow","allow":[],"deny":[]}""");
        var ping = Ping((ArenaSessionContract.ArenaPluginGuid, []), ("reviewed", []), ("blocked", []), ("dependent", ["blocked"]), ("other", []));
        var allowed = HelperDirectStart.AllowedMods(ping, policy, @"C:\SPT\SPT_Runtime");
        Assert.Contains(allowed, m => m.Guid == "reviewed"); Assert.Contains(allowed, m => m.Guid == "other");
        Assert.DoesNotContain(allowed, m => m.Guid == "blocked" || m.Guid == "dependent");
    }

    [Theory]
    [InlineData("private")]
    [InlineData("duplicate")]
    [InlineData("fika")]
    [InlineData("outside")]
    [InlineData("missing-arena")]
    public void UntrustedServerDescriptionsAreRefused(string mutation)
    {
        var ping = Ping((ArenaSessionContract.ArenaPluginGuid, []));
        var mods = (List<object>)ping["serverMods"];
        if (mutation == "private") ping["arenaInstance"] = true;
        if (mutation == "duplicate") mods.Add(mods[0]);
        if (mutation == "fika") ((Dictionary<string, object>)mods[0])["guid"] = "com.fika.server";
        if (mutation == "outside") ((Dictionary<string, object>)mods[0])["folder"] = @"C:\other\ArenaMode";
        if (mutation == "missing-arena") ((Dictionary<string, object>)mods[0])["guid"] = "other";
        Assert.Throws<HelperException>(() => HelperDirectStart.AllowedMods(ping, Policy(), @"C:\SPT\SPT_Runtime"));
    }

    [Fact]
    public void FreshDirectRequestUsesNativeArgumentsAndCannotBecomeAnOnDiskSwitch()
    {
        var mods = new List<HelperServerMod> { new() { Guid = ArenaSessionContract.ArenaPluginGuid, Folder = @"C:\SPT\SPT_Runtime\user\mods\ArenaMode" } };
        string session = Guid.NewGuid().ToString("N");
        var request = HelperDirectStart.BuildRequest(@"C:\SPT", Profile, new Uri("https://127.0.0.1:6969"), session, "hash", mods);
        Assert.Equal(3, request.Schema); Assert.Equal(0, request.GamePid); Assert.Equal(session, request.OfflineSessionId);
        Assert.Null(request.RewardControlId); Assert.Null(request.VersionPlan);
        Assert.True(HelperCommandLine.TryRewriteGameArguments(request.GameArgs, true, 6970, out var arena, out var error), error);
        Assert.Contains("-arena-session", arena); Assert.Contains("-token=" + Profile, arena);
        Assert.Contains(arena, a => a.Contains("https://127.0.0.1:6970"));
        Assert.True(HelperCommandLine.TryRewriteGameArguments(arena, false, 6969, out var normal, out error), error);
        Assert.DoesNotContain("-arena-session", normal); Assert.Contains(normal, a => a.Contains("https://127.0.0.1:6969"));
        string serialized = JsonSerializer.Serialize(new { schema = 3, direction = "enter", gamePid = 0 });
        Assert.False(HelperRequest.TryParse(serialized, "enter", out _, out error)); Assert.Contains("gamePid", error);
        Assert.False(HelperRequest.TryParseDirectStart("""{"schema":1,"direction":"enter","gamePid":0}""", out _, out error));
        Assert.Contains("ownership", error);
    }

    [Fact]
    public void DirectHostOwnsMutexWithoutAnAcknowledgementFile()
    {
        var log = new HelperLog(Path.Combine(_root, "helper.log"));
        var runner = new HelperRunner(_root, log, "test");
        string mutexName = "ArenaDirectTest-" + Guid.NewGuid().ToString("N"); bool called = false;
        runner.DirectStart = id => { called = true; Assert.Equal(Profile, id);
            bool blocked = false;
            var thread = new Thread(() => { using var mutex = new Mutex(false, mutexName); bool got = mutex.WaitOne(50); if (got) mutex.ReleaseMutex(); blocked = !got; });
            thread.Start(); Assert.True(thread.Join(5000));
            Assert.True(blocked); return true; };
        Assert.True(HelperCommand.TryParse(["start", "--profile", Profile], out var command, out _));
        Assert.Equal(0, new HelperHost(runner, log, mutexName, _ => { }).Execute(command)); Assert.True(called);
        Assert.Empty(Directory.GetFiles(_root, "*.accepted"));
    }

    [SkippableFact]
    public void AlreadyStartingServerIsReusedAndForeignImageIsRefused()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        string name = "ArenaStartProbe" + Guid.NewGuid().ToString("N") + ".exe";
        string executable = Path.Combine(_root, name); File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), executable);
        Assert.Null(HelperDirectStart.FindNormalServer(executable));
        using var process = Process.Start(new ProcessStartInfo(executable, "-n 120 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            using var found = HelperDirectStart.FindNormalServer(executable); Assert.Equal(process.Id, found!.Id);
            string foreign = Put("other/" + name, "");
            Assert.Throws<HelperException>(() => HelperDirectStart.FindNormalServer(foreign));
            Assert.False(process.HasExited);
        }
        finally { process.Kill(); process.WaitForExit(5000); }
    }

    // Run needs a live normal server, a pinned certificate and the server's routes, so it has no unit seam. The refusal of a
    // session of another installation that is not closed must still come before the normal server is looked up and before
    // the /prepare call changes its state (C4); the order is pinned on the source.
    [Fact]
    public void ForeignSessionIsRefusedBeforeTheNormalServerIsTouched()
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root, "ArenaMode.Session", "HelperDirectStart.cs")).Replace("\r\n", "\n");
        int run = source.IndexOf("internal bool Run(string profileId)", StringComparison.Ordinal);
        int stop = source.IndexOf("_runner.StopArenaServer(Runtime)", run, StringComparison.Ordinal);
        int describe = source.IndexOf("_runner.DescribeStopRefusal(stopped)", run, StringComparison.Ordinal);
        int closed = source.IndexOf("_runner.RequireForeignSessionClosed(Runtime);", run, StringComparison.Ordinal);
        int normal = source.IndexOf("GetNormalServer(endpoint)", run, StringComparison.Ordinal);
        int prepare = source.IndexOf("/arenamode/session/prepare", run, StringComparison.Ordinal);
        Assert.True(run > 0 && stop > run && stop < describe && describe < closed, "the stop step, its refusal, then the closure check");
        Assert.True(closed < normal && normal < prepare, "the closure check precedes the normal server and the prepare call");
    }
}
