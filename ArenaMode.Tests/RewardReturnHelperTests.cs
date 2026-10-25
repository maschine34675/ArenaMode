using System.Text;
using System.Text.Json.Nodes;
using System.Diagnostics;
using ArenaMode.SessionHelper;
using ArenaMode.Shared.Session;

namespace ArenaMode.Tests;

public sealed class RewardReturnHelperTests
{
    [Fact]
    public void RestartWaitsForOldExitAndFreePortBeforeStarting()
    {
        var events = new List<string>(); int exits = 0, ports = 0;
        HelperRewardReturn.CompleteRestart(
            () => { events.Add("exit"); return ++exits > 1; },
            () => { events.Add("port"); return ++ports > 1; },
            () => { events.Add("start"); return Process.GetCurrentProcess(); },
            _ => events.Add("ready"), () => { events.Add("retry"); return true; }, _ => { });
        Assert.Equal(["exit", "retry", "exit", "port", "retry", "exit", "port", "start", "ready"], events);
    }

    [Fact]
    public void ReadinessRetryDoesNotStartASecondLiveServer()
    {
        int starts = 0, probes = 0, waits = 0;
        HelperRewardReturn.CompleteRestart(() => true, () => { probes++; return true; },
            () => { starts++; return Process.GetCurrentProcess(); },
            _ => { if (++waits == 1) throw new TimeoutException(); }, () => true, _ => { });
        Assert.Equal(1, starts); Assert.Equal(1, probes); Assert.Equal(2, waits);
    }

    [Fact]
    public void FailedStartCanRetryButCancelDoesNotForceAnExit()
    {
        int starts = 0, waits = 0;
        HelperRewardReturn.CompleteRestart(() => true, () => true,
            () => { if (++starts == 1) throw new IOException(); return Process.GetCurrentProcess(); },
            _ => waits++, () => true, _ => { });
        Assert.Equal(2, starts); Assert.Equal(1, waits);
        bool started = false;
        Assert.Throws<HelperException>(() => HelperRewardReturn.CompleteRestart(() => false, () => true,
            () => { started = true; return Process.GetCurrentProcess(); }, _ => { }, () => false, _ => { }));
        Assert.False(started);
    }

    private const string Id = "0123456789abcdef01234567";
    private const string Control = "0123456789abcdef0123456789abcdef";
    private static readonly DateTime Now = new(2026, 10, 25, 10, 0, 0, DateTimeKind.Utc);

    private static JsonObject Grant(string root = "C:\\SPT41\\SPT_Runtime") => new()
    {
        ["schema"] = 1, ["controlId"] = Control, ["profileId"] = Id,
        ["token"] = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()),
        ["runtimeRoot"] = root, ["originUrl"] = "https://127.0.0.1:6969",
        ["certificateSha256"] = new string('a', 64), ["issuedUtc"] = Now.ToString("o"),
        ["expiresUtc"] = Now.AddHours(12).ToString("o"), ["serverPid"] = 123,
        ["serverStartedUtc"] = Now.AddMinutes(-30).ToString("o"),
        ["serverArguments"] = new JsonArray("--test-value", "two words", "quoted\"value", "trailing\\")
    };

    private static HelperRewardReturn.Grant Parse(JsonObject value, string root = "C:\\SPT41\\SPT_Runtime") =>
        HelperRewardReturn.Parse(Encoding.UTF8.GetBytes(value.ToJsonString()), root, Id, Control, Now);

    [Fact]
    public void ValidGrantRetainsOriginalArgumentsAndProcessIdentity()
    {
        var grant = Parse(Grant());
        Assert.Equal(123, grant.ServerPid);
        Assert.Equal(Now.AddMinutes(-30), grant.ServerStartedUtc);
        Assert.Equal(["--test-value", "two words", "quoted\"value", "trailing\\"], grant.ServerArguments);
        Assert.Equal(6969, grant.Origin.Port);
    }

    [Theory]
    [InlineData("schema", "2")]
    [InlineData("controlId", "\"ffffffffffffffffffffffffffffffff\"")]
    [InlineData("profileId", "\"ffffffffffffffffffffffff\"")]
    [InlineData("runtimeRoot", "\"C:\\\\Other\\\\SPT_Runtime\"")]
    [InlineData("token", "\"AQ==\"")]
    [InlineData("certificateSha256", "\"not-a-pin\"")]
    [InlineData("originUrl", "\"https://example.org:6969\"")]
    [InlineData("originUrl", "\"http://127.0.0.1:6969\"")]
    [InlineData("originUrl", "\"https://127.0.0.1:6969/extra\"")]
    [InlineData("originUrl", "\"https://user@127.0.0.1:6969\"")]
    [InlineData("originUrl", "\"https://127.0.0.1:6969?x=1\"")]
    [InlineData("serverPid", "0")]
    [InlineData("serverPid", "-1")]
    [InlineData("serverArguments", "null")]
    [InlineData("serverArguments", "[1]")]
    [InlineData("serverArguments", "[\"bad\\u0000arg\"]")]
    [InlineData("expiresUtc", "\"2026-10-25T09:59:59Z\"")]
    [InlineData("expiresUtc", "\"2026-10-26T23:00:00Z\"")]
    [InlineData("issuedUtc", "\"2026-10-25T10:05:00Z\"")]
    [InlineData("serverStartedUtc", "\"2026-10-25T10:01:00Z\"")]
    [InlineData("serverStartedUtc", "\"2026-10-25T10:00:00+02:00\"")]
    public void InvalidAuthorityIsRefused(string field, string json)
    {
        var grant = Grant(); grant[field] = JsonNode.Parse(json);
        Assert.ThrowsAny<Exception>(() => Parse(grant));
    }

    [Fact]
    public void UnknownOrMissingFieldsAndOversizedArgumentsAreRefused()
    {
        var grant = Grant(); grant["extra"] = true;
        Assert.ThrowsAny<Exception>(() => Parse(grant));
        grant = Grant(); grant.Remove("serverPid");
        Assert.ThrowsAny<Exception>(() => Parse(grant));
        grant = Grant(); grant["serverArguments"] = new JsonArray(new string('x', 4097));
        Assert.ThrowsAny<Exception>(() => Parse(grant));
        grant["serverArguments"] = new JsonArray(Enumerable.Range(0, 33).Select(_ => (JsonNode?)JsonValue.Create("")).ToArray());
        Assert.ThrowsAny<Exception>(() => Parse(grant));
    }

    [Fact]
    public void DuplicateJsonFieldsAreRefused()
    {
        string json = Grant().ToJsonString().Replace("\"schema\":1", "\"schema\":1,\"schema\":1");
        Assert.ThrowsAny<Exception>(() => HelperRewardReturn.Parse(Encoding.UTF8.GetBytes(json), "C:\\SPT41\\SPT_Runtime", Id, Control, Now));
    }

    [Fact]
    public void CopyIsBoundToTheCurrentPrivateMarkerAndDoesNotCopyProfiles()
    {
        string parent = Path.Combine(Path.GetTempPath(), "ArenaMode.RewardReturnHelper.Tests");
        string root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "normal"), tree = Path.Combine(root, "private");
            string authority = Path.Combine(source, "user", "arena-rewards", Id);
            Directory.CreateDirectory(authority); Directory.CreateDirectory(tree);
            File.WriteAllText(Path.Combine(authority, "return.json"), Grant(source).ToJsonString());
            var marker = new HelperMarker { Schema = ArenaSessionContract.SchemaVersion, SourceRuntime = source,
                ProfileId = Id, RewardControlId = Control, ServerExe = Path.Combine(tree, HelperLayout.ServerExeName),
                Port = 6970, CreatedUtc = Now.ToString("o") };
            File.WriteAllText(Path.Combine(tree, ArenaSessionContract.MarkerFileName), marker.ToJson());
            Assert.True(HelperRewardReturn.TryCopy(source, Id, Control, tree, marker, Now, null));
            Assert.Equal(File.ReadAllBytes(Path.Combine(authority, "return.json")), File.ReadAllBytes(Path.Combine(tree, HelperRewardReturn.FileName)));
            Assert.False(Directory.Exists(Path.Combine(tree, "user", "profiles")));
            marker.RewardControlId = new string('f', 32);
            Assert.False(HelperRewardReturn.TryCopy(source, Id, Control, tree, marker, Now, null));
            Assert.False(File.Exists(Path.Combine(tree, HelperRewardReturn.FileName))); // no stale capability after failed refresh
        }
        finally
        {
            Assert.Equal(Path.GetFullPath(parent), Path.GetDirectoryName(Path.GetFullPath(root)));
            Assert.True(Guid.TryParseExact(Path.GetFileName(root), "N", out _));
            Directory.Delete(root, recursive: true);
        }
    }
}
