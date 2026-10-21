using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using ArenaMode.SessionHelper;
using ArenaMode.Shared.Session;

namespace ArenaMode.Tests;

/// <summary>
/// The Arena session helper (docs/ARENA-SESSION-20261021.md, section 6): the pure functions of
/// <c>ArenaMode.Session/SessionLogic.cs</c> (argument rewrite, DOORSTOP_ cleaning, http.json and
/// sptLogger.json patching, path safety, request parsing) and the file system behaviour of
/// <c>ArenaMode.Session/SessionFileSystem.cs</c> against real temporary folders (junctions are
/// removed as links, nothing outside the sandbox root is touched, the server tree is built and
/// torn down without harming the folders it only links to). Both files are linked into this
/// project; the process and socket side of the helper is covered by
/// <see cref="ArenaSessionHelperProcessTests"/>.
/// </summary>
public sealed class ArenaSessionHelperTests
{
    // ------------------------------------------------------------------ fixtures

    private const string ProfileId = "6ab7db1164f04b565cf78be7";
    private const string TokenArgument = "-token=" + ProfileId;
    private const string LauncherConfig = "-config={'BackendUrl':'https://127.0.0.1:6969','Version':'live','MatchingVersion':'live'}";

    private const string SptHttpJson = """
        {
          "ip": "127.0.0.1",
          "port": 6969,
          "backendIp": "127.0.0.1",
          "backendPort": 6969,
          "logRequests": true,
          "logResponseBodies": false,
          "serverImagePathOverride": {},
          "webAuthenticationConfig": {
            "enabled": true,
            "enableDefaultUser": true,
            "allowDefaultUserFromAnyIp": true,
            "requireCredentialsOnLocalhost": false,
            "defaultUser": {
              "username": "spt",
              "password": "spt",
              "isAdministrator": true
            }
          }
        }
        """;

    private const string SptLoggerJson = """
        {
          "loggers": [
            {
              "type": "File",
              "logLevel": "Debug",
              "format": "[%date% %time%][%level%][%logger%] %message%",
              "filePath": "./user/logs/spt/",
              "filePattern": "spt-SPT-VM-%DATE%.log",
              "maxFileSizeMB": 10,
              "filters": [ { "type": "Exclude", "name": ".*RequestLogger", "matchingType": "Regex" } ]
            },
            {
              "type": "File",
              "logLevel": "Debug",
              "filePath": "./user/logs/requests/",
              "filePattern": "requests-SPT-VM-%DATE%.log"
            },
            {
              "type": "Console",
              "logLevel": "Debug",
              "format": "%message%"
            }
          ]
        }
        """;

    private static List<string> NormalArguments() => new() { "-force-gfx-jobs", "native", TokenArgument, LauncherConfig };

    private static Dictionary<string, object?> Mod(string guid, string folderName) => new()
    {
        ["guid"] = guid,
        ["folder"] = @"C:\SPT41\SPT_Runtime\user\mods\" + folderName,
    };

    private static Dictionary<string, object?> RequestFields(string direction = "enter") => new()
    {
        ["schema"] = 1,
        ["direction"] = direction,
        ["gamePid"] = 1234,
        ["gameExe"] = @"C:\SPT41\EscapeFromTarkov.exe",
        ["gameDir"] = @"C:\SPT41",
        ["gameArgs"] = NormalArguments(),
        ["profileId"] = ProfileId,
        ["sptRuntimeDir"] = @"C:\SPT41\SPT_Runtime",
        ["serverMods"] = new List<object?>
        {
            Mod("me.sol.sain", "Solarint-SAIN-ServerMod"),
            Mod("com.maschine.ArenaMode", "ArenaMode"),
        },
        ["policyHash"] = "3755e0db97983440",
    };

    private static string Json(Dictionary<string, object?> fields) => ArenaMiniJson.Write(fields);

    private static HelperRequest ParseRequest(Dictionary<string, object?> fields, string? direction = null)
    {
        Assert.True(HelperRequest.TryParse(Json(fields), direction!, out var request, out var error), error);
        return request;
    }

    private static void SkipUnlessWindows()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "the helper is a Windows program; these checks use Windows paths and NTFS junctions");
    }

    // ------------------------------------------------------------------ the helper's own command line

    [Theory]
    [InlineData("enter")]
    [InlineData("leave")]
    [InlineData("ENTER")]
    public void CommandTakesARequestFile(string name)
    {
        Assert.True(HelperCommand.TryParse(new[] { name, "--request", @"C:\x\request.json" }, out var command, out var error), error);
        Assert.Equal(name.ToLowerInvariant(), command.Name);
        Assert.Equal(@"C:\x\request.json", command.RequestPath);
        Assert.True(command.NeedsRequest);
    }

    [Fact]
    public void CommandAcceptsTheEqualsFormOfTheRequestOption()
    {
        Assert.True(HelperCommand.TryParse(new[] { "leave", @"--request=C:\x\r.json" }, out var command, out var error), error);
        Assert.Equal(@"C:\x\r.json", command.RequestPath);
    }

    [Theory]
    [InlineData("stop-server")]
    [InlineData("status")]
    public void CommandsWithoutOptionsParse(string name)
    {
        Assert.True(HelperCommand.TryParse(new[] { name }, out var command, out var error), error);
        Assert.Equal(name, command.Name);
        Assert.False(command.NeedsRequest);
        Assert.Null(command.RequestPath);
    }

    [Theory]
    [MemberData(nameof(BadCommandLines))]
    public void MalformedCommandLinesAreRefused(string[] args, string fragment)
    {
        Assert.False(HelperCommand.TryParse(args, out var command, out var error));
        Assert.Null(command);
        Assert.Contains(fragment, error);
    }

    public static IEnumerable<object[]> BadCommandLines()
    {
        yield return new object[] { Array.Empty<string>(), "no command" };
        yield return new object[] { new[] { "restart" }, "unknown command" };
        yield return new object[] { new[] { "enter" }, "--request" };
        yield return new object[] { new[] { "enter", "--request" }, "needs a file" };
        yield return new object[] { new[] { "enter", "--request", "--other" }, "needs a file" };
        yield return new object[] { new[] { "enter", "--request", "a.json", "--request", "b.json" }, "twice" };
        yield return new object[] { new[] { "enter", "--request=" }, "needs a file" };
        yield return new object[] { new[] { "status", "--request", "a.json" }, "unexpected argument" };
        yield return new object[] { new[] { "stop-server", "now" }, "unexpected argument" };
        yield return new object[] { new[] { "enter", "--request", "a.json", "extra" }, "unexpected argument" };
    }

    // ------------------------------------------------------------------ path safety

    [Theory]
    [InlineData(@"C:\a", true)]
    [InlineData("c:/a", true)]
    [InlineData(@"D:\", true)]
    [InlineData(@"C:\a b\c", true)]
    [InlineData("", false)]
    [InlineData("C:", false)]
    [InlineData(@"C:a", false)]
    [InlineData(@"\a", false)]
    [InlineData(@"a\b", false)]
    [InlineData(@"\\server\share\x", false)]
    [InlineData(@"\\?\C:\x", false)]
    [InlineData(@"C:\a\b:stream", false)]
    [InlineData(@"C:\a*", false)]
    [InlineData(@"C:\a?", false)]
    [InlineData("C:\\a\0b", false)]
    public void OnlyDriveAbsolutePathsAreAccepted(string path, bool expected)
    {
        Assert.Equal(expected, HelperPathSafety.IsDriveAbsolute(path));
    }

    [Theory]
    [InlineData(@"C:\a\..\b", true)]
    [InlineData(@"..\b", true)]
    [InlineData("C:/a/../b", true)]
    [InlineData(@"C:\a\b..c\d", false)]
    [InlineData(@"C:\a\.b", false)]
    public void ParentSegmentsAreDetected(string path, bool expected)
    {
        Assert.Equal(expected, HelperPathSafety.HasParentSegment(path));
    }

    [SkippableTheory]
    [InlineData(@"C:\a\.\b\..\c\", @"C:\a\c")]
    [InlineData("C:/a//b", @"C:\a\b")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"C:\a\b\", @"C:\a\b")]
    public void PathsAreNormalisedWithoutTrailingSeparator(string path, string expected)
    {
        SkipUnlessWindows();
        Assert.True(HelperPathSafety.TryNormalize(path, out var full));
        Assert.Equal(expected, full);
    }

    [SkippableTheory]
    [InlineData(@"C:\root\x", true)]
    [InlineData(@"C:\ROOT\X\Y", true)]
    [InlineData(@"C:\root\a\..\b", true)]
    [InlineData(@"C:\root", false)]
    [InlineData(@"C:\root\", false)]
    [InlineData(@"C:\root2\x", false)]
    [InlineData(@"C:\root\..\x", false)]
    [InlineData(@"C:\root\..\root2\x", false)]
    [InlineData(@"C:\", false)]
    [InlineData(@"D:\root\x", false)]
    [InlineData(@"relative\root\x", false)]
    public void StrictlyUnderMeansBelowTheRootAndNotTheRoot(string path, bool expected)
    {
        SkipUnlessWindows();
        Assert.Equal(expected, HelperPathSafety.IsStrictlyUnder(path, @"C:\root"));
    }

    [SkippableFact]
    public void SameOrUnderAcceptsTheRootItself()
    {
        SkipUnlessWindows();
        Assert.True(HelperPathSafety.IsSameOrUnder(@"C:\root\", @"C:\root"));
        Assert.True(HelperPathSafety.IsSameOrUnder(@"C:\root\a", @"C:\root"));
        Assert.False(HelperPathSafety.IsSameOrUnder(@"C:\rootx", @"C:\root"));
    }

    [SkippableTheory]
    [InlineData(@"C:\mods\x", true)]
    [InlineData(@"C:\mods\x\", true)]
    [InlineData(@"C:\MODS\x", true)]
    [InlineData(@"C:\mods\x\y", false)]
    [InlineData(@"C:\mods", false)]
    [InlineData(@"C:\other\x", false)]
    [InlineData(@"C:\mods\..\x", false)]
    public void DirectChildIsExactlyOneLevelBelow(string path, bool expected)
    {
        SkipUnlessWindows();
        Assert.Equal(expected, HelperPathSafety.IsDirectChild(path, @"C:\mods"));
    }

    [SkippableFact]
    public void ParentAndRelativeSegmentsFollowTheNormalisedPath()
    {
        SkipUnlessWindows();
        Assert.Equal(@"C:\a", HelperPathSafety.GetParent(@"C:\a\b"));
        Assert.Equal(@"C:\", HelperPathSafety.GetParent(@"C:\a"));
        Assert.Null(HelperPathSafety.GetParent(@"C:\"));
        Assert.Equal(new[] { "b", "c" }, HelperPathSafety.RelativeSegments(@"C:\a\b\c", @"C:\a"));
        Assert.Empty(HelperPathSafety.RelativeSegments(@"C:\a", @"C:\a")!);
        Assert.Null(HelperPathSafety.RelativeSegments(@"C:\x\b", @"C:\a"));
    }

    [Theory]
    [InlineData("Solarint-SAIN-ServerMod", true)]
    [InlineData("[SVM] Server Value Modifier", true)]
    [InlineData("a.b", true)]
    [InlineData("", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("a\\b", false)]
    [InlineData("a/b", false)]
    [InlineData("a:b", false)]
    [InlineData("a*b", false)]
    [InlineData("trailing.", false)]
    [InlineData("trailing ", false)]
    [InlineData(" leading", false)]
    [InlineData("CON", false)]
    [InlineData("nul.txt", false)]
    [InlineData("LPT1", false)]
    [InlineData("console", true)]
    public void SingleNamesMustBePlainFolderNames(string name, bool expected)
    {
        Assert.Equal(expected, HelperPathSafety.IsSafeSingleName(name));
    }

    // ------------------------------------------------------------------ request file

    [SkippableFact]
    public void ARequestAsTheClientWritesItParses()
    {
        SkipUnlessWindows();
        var request = ParseRequest(RequestFields(), "enter");
        Assert.True(request.IsEnter);
        Assert.False(request.IsLeave);
        Assert.Equal(1234, request.GamePid);
        Assert.Equal(@"C:\SPT41\EscapeFromTarkov.exe", request.GameExe);
        Assert.Equal(@"C:\SPT41", request.GameDir);
        Assert.Equal(@"C:\SPT41\SPT_Runtime", request.SptRuntimeDir);
        Assert.Equal(NormalArguments(), request.GameArgs);
        Assert.Equal(ProfileId, request.ProfileId);
        Assert.Equal("3755e0db97983440", request.PolicyHash);
        Assert.Equal(new[] { "me.sol.sain", "com.maschine.ArenaMode" }, request.ServerMods.Select(mod => mod.Guid));
        Assert.Equal(new[] { "Solarint-SAIN-ServerMod", "ArenaMode" }, request.ServerMods.Select(mod => mod.FolderName));
    }

    [SkippableFact]
    public void ThePathsOfARequestAreStoredNormalised()
    {
        SkipUnlessWindows();
        var fields = RequestFields();
        fields["gameDir"] = "C:/SPT41/";
        fields["sptRuntimeDir"] = @"C:\SPT41\SPT_Runtime\";
        fields["serverMods"] = new List<object?> { Mod("a", "ArenaMode") };
        var request = ParseRequest(fields);
        Assert.Equal(@"C:\SPT41", request.GameDir);
        Assert.Equal(@"C:\SPT41\SPT_Runtime", request.SptRuntimeDir);
    }

    [SkippableFact]
    public void ARequestRoundTripsThroughItsOwnWriter()
    {
        SkipUnlessWindows();
        var first = ParseRequest(RequestFields());
        Assert.True(HelperRequest.TryParse(first.ToJson(), "enter", out var second, out var error), error);
        Assert.Equal(first.GamePid, second.GamePid);
        Assert.Equal(first.GameExe, second.GameExe);
        Assert.Equal(first.GameArgs, second.GameArgs);
        Assert.Equal(first.ProfileId, second.ProfileId);
        Assert.Equal(first.ServerMods.Select(mod => mod.Folder), second.ServerMods.Select(mod => mod.Folder));
        Assert.Equal(first.PolicyHash, second.PolicyHash);
    }

    [SkippableFact]
    public void ALeaveRequestNeedsNeitherAProfileNorServerMods()
    {
        SkipUnlessWindows();
        var fields = RequestFields("leave");
        fields.Remove("profileId");
        fields.Remove("serverMods");
        var request = ParseRequest(fields, "leave");
        Assert.True(request.IsLeave);
        Assert.Null(request.ProfileId);
        Assert.Empty(request.ServerMods);
    }

    [SkippableFact]
    public void TheDirectionMustMatchTheCommand()
    {
        SkipUnlessWindows();
        Assert.False(HelperRequest.TryParse(Json(RequestFields("leave")), "enter", out _, out var error));
        Assert.Contains("'leave'", error);
        Assert.Contains("'enter'", error);
        Assert.True(HelperRequest.TryParse(Json(RequestFields("leave")), null!, out _, out _));
    }

    [SkippableTheory]
    [MemberData(nameof(BadRequests))]
    public void UnusableRequestsAreRefusedWithAReason(string label, Action<Dictionary<string, object?>> damage, string fragment)
    {
        SkipUnlessWindows();
        var fields = RequestFields();
        damage(fields);
        Assert.False(HelperRequest.TryParse(Json(fields), "enter", out var request, out var error), label);
        Assert.Null(request);
        Assert.Contains(fragment, error);
    }

    public static IEnumerable<object[]> BadRequests()
    {
        static object[] Case(string label, Action<Dictionary<string, object?>> damage, string fragment)
            => new object[] { label, damage, fragment };

        yield return Case("schema", f => f["schema"] = 2, "schema");
        yield return Case("direction", f => f["direction"] = "restart", "direction");
        yield return Case("pid zero", f => f["gamePid"] = 0, "gamePid");
        yield return Case("pid fraction", f => f["gamePid"] = 12.5, "gamePid");
        yield return Case("pid string", f => f["gamePid"] = "1234", "gamePid");
        yield return Case("exe missing", f => f.Remove("gameExe"), "gameExe");
        yield return Case("exe relative", f => f["gameExe"] = @"SPT41\EscapeFromTarkov.exe", "gameExe");
        yield return Case("exe unc", f => f["gameExe"] = @"\\server\share\EscapeFromTarkov.exe", "gameExe");
        yield return Case("exe parent segment", f => f["gameExe"] = @"C:\SPT41\..\Evil\EscapeFromTarkov.exe", "'..'");
        yield return Case("exe not an exe", f => f["gameExe"] = @"C:\SPT41\EscapeFromTarkov.dll", ".exe");
        yield return Case("exe outside dir", f => f["gameExe"] = @"C:\Other\EscapeFromTarkov.exe", "inside gameDir");
        yield return Case("dir missing", f => f.Remove("gameDir"), "gameDir");
        yield return Case("runtime missing", f => f.Remove("sptRuntimeDir"), "sptRuntimeDir");
        yield return Case("runtime relative", f => f["sptRuntimeDir"] = "SPT_Runtime", "sptRuntimeDir");
        yield return Case("args missing", f => f.Remove("gameArgs"), "gameArgs");
        yield return Case("args not strings", f => f["gameArgs"] = new List<object?> { "a", 5 }, "gameArgs");
        yield return Case("args null element", f => f["gameArgs"] = new List<object?> { "a", null }, "gameArgs");
        yield return Case("profile missing", f => f.Remove("profileId"), "profileId");
        yield return Case("profile too short", f => f["profileId"] = "6ab7db11", "profileId");
        yield return Case("profile traversal", f => f["profileId"] = @"..\..\..\Windows\win", "profileId");
        yield return Case("profile not hex", f => f["profileId"] = "zzb7db1164f04b565cf78be7", "profileId");
        yield return Case("mods missing", f => f.Remove("serverMods"), "serverMods");
        yield return Case("mods empty", f => f["serverMods"] = new List<object?>(), "empty");
        yield return Case("mod not object", f => f["serverMods"] = new List<object?> { "ArenaMode" }, "object");
        yield return Case("mod no guid", f => f["serverMods"] = new List<object?> { new Dictionary<string, object?> { ["folder"] = @"C:\SPT41\SPT_Runtime\user\mods\X" } }, "guid");
        yield return Case("mod relative folder", f => f["serverMods"] = new List<object?> { new Dictionary<string, object?> { ["guid"] = "a", ["folder"] = @"user\mods\X" } }, "folder");
        yield return Case("mod folder elsewhere", f => f["serverMods"] = new List<object?> { new Dictionary<string, object?> { ["guid"] = "a", ["folder"] = @"C:\Windows\System32" } }, "direct child");
        yield return Case("mod folder nested", f => f["serverMods"] = new List<object?> { new Dictionary<string, object?> { ["guid"] = "a", ["folder"] = @"C:\SPT41\SPT_Runtime\user\mods\X\Y" } }, "direct child");
        yield return Case("mod folder is mods root", f => f["serverMods"] = new List<object?> { new Dictionary<string, object?> { ["guid"] = "a", ["folder"] = @"C:\SPT41\SPT_Runtime\user\mods" } }, "direct child");
        yield return Case("mod folder traversal", f => f["serverMods"] = new List<object?> { new Dictionary<string, object?> { ["guid"] = "a", ["folder"] = @"C:\SPT41\SPT_Runtime\user\mods\X\..\..\..\Evil" } }, "'..'");
        yield return Case("mod folder name clash", f => f["serverMods"] = new List<object?> { Mod("a", "X"), Mod("b", "x") }, "folder name");
        yield return Case("mod folder reserved", f => f["serverMods"] = new List<object?> { Mod("a", "CON") }, "plain folder name");
    }

    [Fact]
    public void RequestsThatAreNotJsonObjectsAreRefused()
    {
        Assert.False(HelperRequest.TryParse("{ not json", "enter", out _, out var error));
        Assert.Contains("not valid JSON", error);
        Assert.False(HelperRequest.TryParse("[1,2]", "enter", out _, out error));
        Assert.Contains("object", error);
        Assert.False(HelperRequest.TryParse("null", "enter", out _, out error));
        Assert.False(HelperRequest.TryParse(null!, "enter", out _, out error));
    }

    [Theory]
    [InlineData("6ab7db1164f04b565cf78be7", true)]
    [InlineData("6AB7DB1164F04B565CF78BE7", true)]
    [InlineData("6ab7db1164f04b565cf78be", false)]
    [InlineData("6ab7db1164f04b565cf78be77", false)]
    [InlineData("6ab7db1164f04b565cf78beg", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ProfileIdsAre24HexCharacters(string? profileId, bool expected)
    {
        Assert.Equal(expected, HelperRequest.IsValidProfileId(profileId!));
    }

    // ------------------------------------------------------------------ game arguments

    private static List<string> Rewrite(IList<string> args, bool arenaSession, int port)
    {
        Assert.True(HelperCommandLine.TryRewriteGameArguments(args, arenaSession, port, out var result, out var error), error);
        return result;
    }

    [Fact]
    public void EnteringSetsTheArenaPortAndAppendsTheSessionFlag()
    {
        var result = Rewrite(NormalArguments(), true, 6970);
        Assert.Equal(new[]
        {
            "-force-gfx-jobs",
            "native",
            TokenArgument,
            "-config={'BackendUrl':'https://127.0.0.1:6970','Version':'live','MatchingVersion':'live'}",
            "-arena-session",
        }, result);
    }

    [Fact]
    public void LeavingRestoresTheNormalPortAndRemovesTheSessionFlag()
    {
        var session = Rewrite(NormalArguments(), true, 6970);
        var back = Rewrite(session, false, 6969);
        Assert.Equal(NormalArguments(), back);
    }

    [Theory]
    [InlineData("-arena-session")]
    [InlineData("-ARENA-SESSION")]
    [InlineData("-Arena-Session")]
    public void TheSessionFlagIsRemovedWhateverItsCase(string flag)
    {
        var args = NormalArguments();
        args.Insert(1, flag);
        var result = Rewrite(args, false, 6969);
        Assert.DoesNotContain(result, HelperCommandLine.IsSessionFlag);
        Assert.Equal(NormalArguments(), result);
    }

    [Fact]
    public void TheSessionFlagIsAppendedExactlyOnce()
    {
        var args = NormalArguments();
        args.Add("-arena-session");
        args.Insert(0, "-ARENA-SESSION");
        var result = Rewrite(args, true, 6971);
        Assert.Single(result, HelperCommandLine.IsSessionFlag);
        Assert.Equal("-arena-session", result[^1]);
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void EveryOtherArgumentKeepsItsValueAndPosition()
    {
        var args = new List<string> { "-batchmode", "-token=abc", "x y", LauncherConfig, "-last" };
        var result = Rewrite(args, true, 7000);
        Assert.Equal(new[] { "-batchmode", "-token=abc", "x y", "-config={'BackendUrl':'https://127.0.0.1:7000','Version':'live','MatchingVersion':'live'}", "-last", "-arena-session" }, result);
    }

    [Fact]
    public void ADoubleQuotedConfigIsRewrittenToo()
    {
        const string config = "-config={\"BackendUrl\":\"https://127.0.0.1:6969\",\"Version\":\"live\",\"MatchingVersion\":\"live\"}";
        var result = Rewrite(new[] { config }, true, 6970);
        Assert.Equal("-config={\"BackendUrl\":\"https://127.0.0.1:6970\",\"Version\":\"live\",\"MatchingVersion\":\"live\"}", result[0]);
    }

    [Fact]
    public void WhitespaceAndKeyOrderInTheConfigDoNotMatter()
    {
        const string config = "-config={ 'Version' : 'live' , 'BackendUrl' : 'https://192.168.1.5:6969/' }";
        var result = Rewrite(new[] { config }, true, 6975);
        Assert.Equal("-config={ 'Version' : 'live' , 'BackendUrl' : 'https://192.168.1.5:6975/' }", result[0]);
    }

    [Theory]
    [InlineData("https://127.0.0.1:6969", "https://127.0.0.1:6970")]
    [InlineData("https://127.0.0.1", "https://127.0.0.1:6970")]
    [InlineData("https://127.0.0.1:6969/", "https://127.0.0.1:6970/")]
    [InlineData("https://localhost:6969/api?x=1#top", "https://localhost:6970/api?x=1#top")]
    [InlineData("http://user:pw@example.org:80/a", "http://user:pw@example.org:6970/a")]
    [InlineData("https://[::1]:6969", "https://[::1]:6970")]
    [InlineData("https://[::1]", "https://[::1]:6970")]
    public void OnlyThePortOfTheBackendUrlChanges(string url, string expected)
    {
        Assert.True(HelperCommandLine.TryReplaceUrlPort(url, 6970, out var replaced));
        Assert.Equal(expected, replaced);
    }

    [Theory]
    [InlineData("")]
    [InlineData("127.0.0.1:6969")]
    [InlineData("://127.0.0.1:6969")]
    [InlineData("https://")]
    [InlineData("https://:6969")]
    [InlineData("https://[::1")]
    [InlineData("https://::1:6969")]
    public void UrlsWithoutAHostAreRefused(string url)
    {
        Assert.False(HelperCommandLine.TryReplaceUrlPort(url, 6970, out _));
    }

    [Theory]
    [MemberData(nameof(BadGameArguments))]
    public void ArgumentsThatCannotBeRerouteredAreRefused(string label, string[] args, int port, string fragment)
    {
        Assert.False(HelperCommandLine.TryRewriteGameArguments(args, true, port, out var result, out var error), label);
        Assert.Null(result);
        Assert.Contains(fragment, error);
    }

    public static IEnumerable<object[]> BadGameArguments()
    {
        yield return new object[] { "no config", new[] { "-token=abc" }, 6970, "no -config=" };
        yield return new object[] { "two configs", new[] { LauncherConfig, LauncherConfig }, 6970, "more than one" };
        yield return new object[] { "no BackendUrl", new[] { "-config={'Version':'live'}" }, 6970, "BackendUrl" };
        yield return new object[] { "BackendUrl without host", new[] { "-config={'BackendUrl':'6969'}" }, 6970, "not a URL" };
        yield return new object[] { "port zero", new[] { LauncherConfig }, 0, "invalid backend port" };
        yield return new object[] { "port too big", new[] { LauncherConfig }, 65536, "invalid backend port" };
        yield return new object[] { "null argument", new[] { LauncherConfig, null! }, 6970, "null" };
    }

    [Fact]
    public void NullArgumentListsAreRefused()
    {
        Assert.False(HelperCommandLine.TryRewriteGameArguments(null!, true, 6970, out _, out var error));
        Assert.Contains("no game arguments", error);
    }

    [Fact]
    public void TheBackendPortCanBeReadBack()
    {
        Assert.True(HelperCommandLine.TryReadBackendPort(LauncherConfig, out var port));
        Assert.Equal(6969, port);
        Assert.False(HelperCommandLine.TryReadBackendPort("-config={'Version':'live'}", out _));
        Assert.False(HelperCommandLine.TryReadBackendPort("-token=abc", out _));
        Assert.False(HelperCommandLine.TryReadBackendPort("-config={'BackendUrl':'https://host'}", out _));
    }

    [Fact]
    public void TheTokenIsHiddenInTheLog()
    {
        string shown = HelperCommandLine.RedactForLog(NormalArguments());
        Assert.DoesNotContain(ProfileId, shown);
        Assert.Contains("-token=<hidden>", shown);
        Assert.Contains("-force-gfx-jobs", shown);
    }

    // ------------------------------------------------------------------ command line quoting

    public static IEnumerable<object[]> AwkwardArguments()
    {
        string[] samples =
        {
            "plain",
            "with space",
            "",
            "quote\"inside",
            "trailing\\",
            "trailing space \\",
            "two\\\\",
            "\\\"",
            "a\\\\\"b",
            "tab\there",
            @"C:\Program Files\Some App\",
            @"C:\Program Files\Some App\file.exe",
            "unicode \u00FC \u00DF",
            "{\"a\":1,\"b\":\"x y\"}",
            LauncherConfig,
            "-config={'BackendUrl':'https://127.0.0.1:6970','Version':'live'}",
        };
        foreach (string sample in samples) yield return new object[] { sample };
    }

    [Theory]
    [MemberData(nameof(AwkwardArguments))]
    public void QuotedArgumentsSurviveTheReferenceSplitter(string argument)
    {
        string line = HelperCommandLine.BuildCommandLine(new[] { "first", argument, "last" });
        Assert.Equal(new[] { "first", argument, "last" }, ReferenceSplit(line));
    }

    [SkippableTheory]
    [MemberData(nameof(AwkwardArguments))]
    public void QuotedArgumentsSurviveWindowsOwnSplitter(string argument)
    {
        SkipUnlessWindows();
        string line = HelperCommandLine.BuildCommandLine(new[] { "first", argument, "last" });
        Assert.Equal(new[] { "first", argument, "last" }, WindowsSplit(line));
    }

    [Fact]
    public void PlainArgumentsAreLeftUnquoted()
    {
        Assert.Equal("-force-gfx-jobs native " + TokenArgument + " " + LauncherConfig,
            HelperCommandLine.BuildCommandLine(NormalArguments()));
    }

    [Fact]
    public void EmptyAndNullArgumentsAreQuoted()
    {
        Assert.Equal("\"\"", HelperCommandLine.QuoteArgument(""));
        Assert.Equal("\"\"", HelperCommandLine.QuoteArgument(null!));
        Assert.Equal(string.Empty, HelperCommandLine.BuildCommandLine(null!));
    }

    /// <summary>The documented rules of the C runtime (what every Windows program sees).</summary>
    private static List<string> ReferenceSplit(string commandLine)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        bool any = false;
        int i = 0;
        while (i < commandLine.Length)
        {
            char c = commandLine[i];
            if (c == '\\')
            {
                int count = 0;
                while (i < commandLine.Length && commandLine[i] == '\\')
                {
                    count++;
                    i++;
                }

                any = true;
                if (i < commandLine.Length && commandLine[i] == '"')
                {
                    current.Append('\\', count / 2);
                    if (count % 2 == 1)
                    {
                        current.Append('"');
                        i++;
                    }
                }
                else
                {
                    current.Append('\\', count);
                }

                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                any = true;
                i++;
                continue;
            }

            if (!inQuotes && (c == ' ' || c == '\t'))
            {
                if (any) result.Add(current.ToString());
                current.Clear();
                any = false;
                i++;
                continue;
            }

            current.Append(c);
            any = true;
            i++;
        }

        if (any) result.Add(current.ToString());
        return result;
    }

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static List<string> WindowsSplit(string commandLine)
    {
        IntPtr argv = CommandLineToArgvW("program.exe " + commandLine, out int count);
        Assert.NotEqual(IntPtr.Zero, argv);
        try
        {
            var result = new List<string>();
            for (int i = 1; i < count; i++) result.Add(Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!);
            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    // ------------------------------------------------------------------ environment

    [Theory]
    [InlineData("DOORSTOP_DISABLE", true)]
    [InlineData("DOORSTOP_INITIALIZED", true)]
    [InlineData("doorstop_invoke_dll_path", true)]
    [InlineData("Doorstop_Anything", true)]
    [InlineData("DOORSTOP", false)]
    [InlineData("DOORSTOPX", false)]
    [InlineData("MY_DOORSTOP_X", false)]
    [InlineData("PATH", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyDoorstopVariablesAreRecognised(string? name, bool expected)
    {
        Assert.Equal(expected, HelperEnvironment.IsDoorstopVariable(name!));
    }

    [Fact]
    public void CleaningRemovesDoorstopVariablesAndKeepsTheRest()
    {
        var source = new Dictionary<string, string>
        {
            ["PATH"] = @"C:\Windows",
            ["DOORSTOP_DISABLE"] = "1",
            ["Doorstop_Initialized"] = "1",
            ["TEMP"] = @"C:\Temp",
            ["DOORSTOPPER"] = "kept",
        };
        var cleaned = HelperEnvironment.Clean(source);
        Assert.Equal(new[] { "DOORSTOPPER", "PATH", "TEMP" }, cleaned.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Equal(@"C:\Windows", cleaned["path"]);
        Assert.Equal(5, source.Count);
        Assert.Empty(HelperEnvironment.Clean(null!));
    }

    [Fact]
    public void DoorstopNamesListsExactlyTheNamesToDrop()
    {
        var names = HelperEnvironment.DoorstopNames(new[] { "A", "DOORSTOP_X", "b", "doorstop_y" });
        Assert.Equal(new[] { "DOORSTOP_X", "doorstop_y" }, names);
        Assert.Empty(HelperEnvironment.DoorstopNames(null!));
    }

    [Fact]
    public void ProcessStartEnvironmentsLoseTheirDoorstopVariables()
    {
        var info = new ProcessStartInfo("anything.exe");
        info.EnvironmentVariables["DOORSTOP_DISABLE"] = "1";
        info.EnvironmentVariables["DOORSTOP_INITIALIZED"] = "1";
        info.EnvironmentVariables["ARENA_SESSION_TEST_KEPT"] = "yes";

        int removed = HelperEnvironment.RemoveDoorstopVariables(info.EnvironmentVariables);

        Assert.True(removed >= 2);
        Assert.False(info.EnvironmentVariables.ContainsKey("DOORSTOP_DISABLE"));
        Assert.False(info.EnvironmentVariables.ContainsKey("DOORSTOP_INITIALIZED"));
        Assert.Equal("yes", info.EnvironmentVariables["ARENA_SESSION_TEST_KEPT"]);
        Assert.Equal(0, HelperEnvironment.RemoveDoorstopVariables(null!));
    }

    [SkippableFact]
    public void ACleanedEnvironmentReachesARealChildProcess()
    {
        SkipUnlessWindows();
        var info = new ProcessStartInfo("cmd.exe", "/c set")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        info.EnvironmentVariables["DOORSTOP_INITIALIZED"] = "1";
        info.EnvironmentVariables["DOORSTOP_DISABLE"] = "1";
        info.EnvironmentVariables["ARENA_SESSION_TEST_KEPT"] = "yes";
        HelperEnvironment.RemoveDoorstopVariables(info.EnvironmentVariables);

        using var process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        Assert.DoesNotContain("DOORSTOP_", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ARENA_SESSION_TEST_KEPT=yes", output);
    }

    // ------------------------------------------------------------------ http.json

    private static string PatchHttp(string text, int port)
    {
        Assert.True(HelperJsonText.TryPatchHttpPorts(text, port, out var patched, out var error), error);
        return patched;
    }

    [Fact]
    public void TheShippedHttpJsonChangesOnlyTheTwoPorts()
    {
        string expected = SptHttpJson.Replace("\"port\": 6969", "\"port\": 6970").Replace("\"backendPort\": 6969", "\"backendPort\": 6970");
        Assert.NotEqual(SptHttpJson, expected);
        Assert.Equal(expected, PatchHttp(SptHttpJson, 6970));
    }

    [Fact]
    public void PatchingTwiceChangesNothingMore()
    {
        string once = PatchHttp(SptHttpJson, 6975);
        Assert.Equal(once, PatchHttp(once, 6975));
    }

    [Fact]
    public void EveryOtherMemberOfHttpJsonSurvivesParsed()
    {
        string patched = PatchHttp(SptHttpJson, 6970);
        Assert.True(ArenaMiniJson.TryParse(SptHttpJson, out var before, out _));
        Assert.True(ArenaMiniJson.TryParse(patched, out var after, out _));
        var left = ArenaMiniJson.AsObject(before)!;
        var right = ArenaMiniJson.AsObject(after)!;
        Assert.Equal(left.Keys, right.Keys);
        foreach (string key in left.Keys.Where(key => key != "port" && key != "backendPort"))
        {
            Assert.Equal(ArenaMiniJson.Write(left[key]), ArenaMiniJson.Write(right[key]));
        }

        Assert.Equal(6970, ArenaMiniJson.GetInt(right, "port", 0));
        Assert.Equal(6970, ArenaMiniJson.GetInt(right, "backendPort", 0));
    }

    [Fact]
    public void CommentsAndFormattingOfHttpJsonAreKept()
    {
        const string text = "// SPT http config\r\n{\r\n\t/* \"port\": 1 */\r\n\t\"ip\": \"127.0.0.1\", // \"port\": 2\r\n\t\"port\":6969,\r\n\t\"backendPort\"   :   6969\r\n}\r\n";
        const string expected = "// SPT http config\r\n{\r\n\t/* \"port\": 1 */\r\n\t\"ip\": \"127.0.0.1\", // \"port\": 2\r\n\t\"port\":6970,\r\n\t\"backendPort\"   :   6970\r\n}\r\n";
        Assert.Equal(expected, PatchHttp(text, 6970));
    }

    [Fact]
    public void ANestedPortIsNotTouched()
    {
        const string text = "{ \"port\": 1, \"backendPort\": 2, \"inner\": { \"port\": 3, \"backendPort\": 4 }, \"list\": [ { \"port\": 5 } ] }";
        Assert.Equal("{ \"port\": 7, \"backendPort\": 7, \"inner\": { \"port\": 3, \"backendPort\": 4 }, \"list\": [ { \"port\": 5 } ] }", PatchHttp(text, 7));
    }

    [Fact]
    public void APortInsideAStringIsNotTouched()
    {
        const string text = "{ \"note\": \"\\\"port\\\": 1 and // not a comment\", \"port\": 1, \"backendPort\": 1 }";
        Assert.Equal("{ \"note\": \"\\\"port\\\": 1 and // not a comment\", \"port\": 9, \"backendPort\": 9 }", PatchHttp(text, 9));
    }

    [Fact]
    public void APortGivenAsAStringBecomesANumber()
    {
        Assert.Equal("{ \"port\": 6970, \"backendPort\": 6970 }", PatchHttp("{ \"port\": \"6969\", \"backendPort\": \"6969\" }", 6970));
    }

    [Fact]
    public void RepeatedTopLevelPortsAreAllReplaced()
    {
        Assert.Equal("{ \"port\": 8, \"port\": 8, \"backendPort\": 8 }", PatchHttp("{ \"port\": 1, \"port\": 2, \"backendPort\": 3 }", 8));
    }

    [Fact]
    public void MissingPortsAreAddedInTheStyleOfTheFile()
    {
        Assert.Equal("{\n  \"ip\": \"127.0.0.1\",\n  \"port\": 6970,\n  \"backendPort\": 6970\n}",
            PatchHttp("{\n  \"ip\": \"127.0.0.1\"\n}", 6970));
        Assert.Equal("{\r\n    \"ip\": \"x\",\r\n    \"port\": 6970,\r\n    \"backendPort\": 6970\r\n}",
            PatchHttp("{\r\n    \"ip\": \"x\"\r\n}", 6970));
        Assert.Equal("{\"ip\":\"127.0.0.1\",\"port\":6970,\"backendPort\":6970}", PatchHttp("{\"ip\":\"127.0.0.1\"}", 6970));
        Assert.Equal("{\n  \"port\": 6970,\n  \"backendPort\": 6970\n}", PatchHttp("{}", 6970));
    }

    [Fact]
    public void OneMissingPortIsAddedAfterTheLastMember()
    {
        string patched = PatchHttp("{\n  \"port\": 6969,\n  \"ip\": \"x\"\n}", 6970);
        Assert.Equal("{\n  \"port\": 6970,\n  \"ip\": \"x\",\n  \"backendPort\": 6970\n}", patched);
    }

    [Fact]
    public void ABomIsKept()
    {
        string patched = PatchHttp("\uFEFF{ \"port\": 1, \"backendPort\": 1 }", 2);
        Assert.Equal("\uFEFF{ \"port\": 2, \"backendPort\": 2 }", patched);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[1]")]
    [InlineData("\"text\"")]
    [InlineData("{ \"port\": 1,}")]
    [InlineData("{ \"port\": 1 ")]
    [InlineData("{ \"port\": 1 } trailing")]
    [InlineData("{ \"port\" 1 }")]
    [InlineData("{ port: 1 }")]
    [InlineData("{ \"a\": \"unterminated }")]
    [InlineData("{ \"a\": 1 /* open comment }")]
    [InlineData("{ \"a\": @ }")]
    [InlineData("{ \"a\": \"bad \\q escape\" }")]
    public void MalformedHttpJsonIsRefused(string text)
    {
        Assert.False(HelperJsonText.TryPatchHttpPorts(text, 6970, out var patched, out var error));
        Assert.Null(patched);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void DeeplyNestedJsonIsRefusedNotOverflowed()
    {
        string text = "{ \"a\": " + new string('[', 200) + new string(']', 200) + " }";
        Assert.False(HelperJsonText.TryPatchHttpPorts(text, 6970, out _, out var error));
        Assert.Contains("nesting", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void InvalidPortsAreRefused(int port)
    {
        Assert.False(HelperJsonText.TryPatchHttpPorts(SptHttpJson, port, out _, out var error));
        Assert.Contains("invalid port", error);
    }

    [Fact]
    public void TheEndpointOfTheShippedHttpJsonIsRead()
    {
        Assert.True(HelperJsonText.TryReadEndpoint(SptHttpJson, out var endpoint, out var error), error);
        Assert.Equal("127.0.0.1", endpoint.ServerHost);
        Assert.Equal(6969, endpoint.ServerPort);
        Assert.Equal(6969, endpoint.BackendPort);
    }

    [Theory]
    [InlineData("{ \"ip\": \"0.0.0.0\", \"port\": 7001, \"backendPort\": 7002 }", "127.0.0.1", 7001, 7002)]
    [InlineData("{ \"ip\": \"localhost\", \"port\": \"7001\" }", "127.0.0.1", 7001, 7001)]
    [InlineData("{ \"ip\": \"192.168.1.5\", \"backendPort\": 7002 }", "192.168.1.5", 7002, 7002)]
    [InlineData("{ // comment\n \"port\": 7001 }", "127.0.0.1", 7001, 7001)]
    [InlineData("{ }", "127.0.0.1", 6969, 6969)]
    [InlineData("{ \"inner\": { \"port\": 1 }, \"port\": 7003 }", "127.0.0.1", 7003, 7003)]
    public void TheEndpointFollowsTheSameDefaultsAsTheServer(string text, string host, int serverPort, int backendPort)
    {
        Assert.True(HelperJsonText.TryReadEndpoint(text, out var endpoint, out var error), error);
        Assert.Equal(host, endpoint.ServerHost);
        Assert.Equal(serverPort, endpoint.ServerPort);
        Assert.Equal(backendPort, endpoint.BackendPort);
    }

    [Theory]
    [InlineData("{ \"port\": 0 }")]
    [InlineData("{ \"port\": 70000 }")]
    [InlineData("{ \"port\": \"abc\" }")]
    [InlineData("{ \"backendPort\": 1.5 }")]
    [InlineData("{ \"port\": null }")]
    [InlineData("not json")]
    public void UnusableEndpointsAreRefused(string text)
    {
        Assert.False(HelperJsonText.TryReadEndpoint(text, out var endpoint, out var error));
        Assert.Null(endpoint);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Theory]
    [InlineData("0.0.0.0", "127.0.0.1")]
    [InlineData("::", "127.0.0.1")]
    [InlineData("[::]", "127.0.0.1")]
    [InlineData("LocalHost", "127.0.0.1")]
    [InlineData("", "127.0.0.1")]
    [InlineData(" 10.0.0.2 ", "10.0.0.2")]
    public void WildcardHostsAreProbedThroughLoopback(string host, string expected)
    {
        Assert.Equal(expected, HelperJsonText.NormaliseHost(host));
    }

    // ------------------------------------------------------------------ sptLogger.json

    [Fact]
    public void EveryFilePatternGetsThePrefix()
    {
        Assert.True(HelperJsonText.TryPrefixLogFilePatterns(SptLoggerJson, "arena-", out var patched, out var changed, out var error), error);
        Assert.Equal(2, changed);
        Assert.Equal(SptLoggerJson
            .Replace("\"filePattern\": \"spt-SPT-VM-%DATE%.log\"", "\"filePattern\": \"arena-spt-SPT-VM-%DATE%.log\"")
            .Replace("\"filePattern\": \"requests-SPT-VM-%DATE%.log\"", "\"filePattern\": \"arena-requests-SPT-VM-%DATE%.log\""), patched);
    }

    [Fact]
    public void PrefixingTwiceDoesNotPrefixTwice()
    {
        Assert.True(HelperJsonText.TryPrefixLogFilePatterns(SptLoggerJson, "arena-", out var once, out _, out _));
        Assert.True(HelperJsonText.TryPrefixLogFilePatterns(once, "arena-", out var twice, out var changed, out _));
        Assert.Equal(0, changed);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void OnlyStringFilePatternsAnywhereInTheDocumentChange()
    {
        const string text = "{ \"filePattern\": \"top.log\", \"x\": { \"filePattern\": null, \"y\": [ { \"filePattern\": \"deep-%DATE%.log\" } ] }, \"note\": \"filePattern: \\\"nope\\\"\", \"z\": { \"filePattern\": 5 } }";
        Assert.True(HelperJsonText.TryPrefixLogFilePatterns(text, "arena-", out var patched, out var changed, out var error), error);
        Assert.Equal(2, changed);
        Assert.Equal("{ \"filePattern\": \"arena-top.log\", \"x\": { \"filePattern\": null, \"y\": [ { \"filePattern\": \"arena-deep-%DATE%.log\" } ] }, \"note\": \"filePattern: \\\"nope\\\"\", \"z\": { \"filePattern\": 5 } }", patched);
    }

    [Fact]
    public void ALoggerFileWithoutFilePatternsIsLeftAlone()
    {
        const string text = "{ \"loggers\": [ { \"type\": \"Console\" } ] }";
        Assert.True(HelperJsonText.TryPrefixLogFilePatterns(text, "arena-", out var patched, out var changed, out _));
        Assert.Equal(0, changed);
        Assert.Equal(text, patched);
    }

    [Fact]
    public void AnEmptyPrefixOrBrokenLoggerFileIsRefused()
    {
        Assert.False(HelperJsonText.TryPrefixLogFilePatterns(SptLoggerJson, "", out _, out _, out var error));
        Assert.Contains("prefix", error);
        Assert.False(HelperJsonText.TryPrefixLogFilePatterns("{ \"loggers\": [ ", "arena-", out _, out _, out error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    // ------------------------------------------------------------------ plans

    [Theory]
    [InlineData("database", true, HelperSptDataAction.Junction)]
    [InlineData("images", true, HelperSptDataAction.Junction)]
    [InlineData("Launcher", true, HelperSptDataAction.Junction)]
    [InlineData("wwwroot", true, HelperSptDataAction.Junction)]
    [InlineData("dotnet", true, HelperSptDataAction.Junction)]
    [InlineData("configs", true, HelperSptDataAction.CopyDirectory)]
    [InlineData("CONFIGS", true, HelperSptDataAction.CopyDirectory)]
    [InlineData("checks.dat", false, HelperSptDataAction.CopyFile)]
    [InlineData("configs", false, HelperSptDataAction.CopyFile)]
    public void SptDataEntriesAreLinkedOrCopied(string name, bool isDirectory, HelperSptDataAction expected)
    {
        Assert.Equal(expected, HelperTreePlan.Classify(name, isDirectory));
    }

    [Fact]
    public void TheFirstFreePortOfTheArenaRangeIsChosen()
    {
        var asked = new List<int>();
        Assert.Equal(6970, HelperPorts.PickArenaPort(port => { asked.Add(port); return true; }, 6969));
        Assert.Equal(new[] { 6970 }, asked);

        asked.Clear();
        Assert.Equal(6973, HelperPorts.PickArenaPort(port => { asked.Add(port); return port >= 6973; }, 6969));
        Assert.Equal(new[] { 6970, 6971, 6972, 6973 }, asked);
    }

    [Fact]
    public void TheNormalServersPortIsNeverTheArenaPort()
    {
        var asked = new List<int>();
        Assert.Equal(6971, HelperPorts.PickArenaPort(port => { asked.Add(port); return true; }, 6970));
        Assert.Equal(new[] { 6971 }, asked);
        Assert.Equal(6979, HelperPorts.PickArenaPort(port => port == 6979, 6975));
    }

    [Fact]
    public void NoFreePortGivesMinusOne()
    {
        Assert.Equal(-1, HelperPorts.PickArenaPort(_ => false, 6969));
        Assert.Equal(-1, HelperPorts.PickArenaPort(port => port == 6975, 6975));
    }

    [Theory]
    [InlineData("1234", 1234, true)]
    [InlineData(" 1234\r\n", 1234, true)]
    [InlineData("0", 0, false)]
    [InlineData("-5", 0, false)]
    [InlineData("12 34", 0, false)]
    [InlineData("abc", 0, false)]
    [InlineData("", 0, false)]
    [InlineData("12345678901", 0, false)]
    [InlineData("99999999999999999999", 0, false)]
    public void PidFilesHoldOnePositiveInteger(string text, int expected, bool ok)
    {
        Assert.Equal(ok, HelperPidFile.TryParsePid(text, out var pid));
        Assert.Equal(expected, ok ? pid : 0);
    }

    [Fact]
    public void ReusedAndStalePidsAreNotMistakenForTheRecordedServer()
    {
        var written = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(HelperPidFile.ProcessMatchesPidFile(written, written.AddSeconds(-1)));
        Assert.True(HelperPidFile.ProcessMatchesPidFile(written, written));
        Assert.True(HelperPidFile.ProcessMatchesPidFile(written, written.AddSeconds(2)));
        Assert.False(HelperPidFile.ProcessMatchesPidFile(written, written.AddMinutes(10)), "started long after the file");
        Assert.False(HelperPidFile.ProcessMatchesPidFile(written, written.AddHours(-3)), "started long before the file");
    }

    [Fact]
    public void TheMarkerHasTheDocumentedMembers()
    {
        var marker = HelperMarker.Create(new DateTime(2026, 10, 21, 12, 30, 45, DateTimeKind.Utc), ProfileId, 6970,
            @"C:\SPT41\SPT_Runtime", "3755e0db97983440", "0.1.3");
        Assert.True(ArenaMiniJson.TryParse(marker.ToJson(), out var parsed, out _));
        var obj = ArenaMiniJson.AsObject(parsed)!;
        Assert.Equal(new[] { "schema", "createdUtc", "profileId", "port", "sourceRuntime", "policyHash", "helperVersion" }, obj.Keys);
        Assert.Equal(1, ArenaMiniJson.GetInt(obj, "schema", 0));
        Assert.Equal("2026-10-21T12:30:45Z", ArenaMiniJson.GetString(obj, "createdUtc"));
        Assert.Equal(ProfileId, ArenaMiniJson.GetString(obj, "profileId"));
        Assert.Equal(6970, ArenaMiniJson.GetInt(obj, "port", 0));
        Assert.Equal(@"C:\SPT41\SPT_Runtime", ArenaMiniJson.GetString(obj, "sourceRuntime"));
        Assert.Equal("3755e0db97983440", ArenaMiniJson.GetString(obj, "policyHash"));
        Assert.Equal("0.1.3", ArenaMiniJson.GetString(obj, "helperVersion"));
    }

    [Fact]
    public void TheMarkerRoundTrips()
    {
        var marker = HelperMarker.Create(DateTime.UtcNow, ProfileId, 6971, @"C:\SPT41\SPT_Runtime", null!, "0.1.3");
        Assert.True(HelperMarker.TryParse(marker.ToJson(), out var back, out var error), error);
        Assert.Equal(6971, back.Port);
        Assert.Equal(@"C:\SPT41\SPT_Runtime", back.SourceRuntime);
        Assert.Null(back.PolicyHash);
        Assert.False(HelperMarker.TryParse("{\"schema\":2}", out _, out error));
        Assert.False(HelperMarker.TryParse("[1]", out _, out error));
        Assert.False(HelperMarker.TryParse("nope", out _, out error));
    }

    [Fact]
    public void LogLinesAreSingleLinesWithATimestampAndTheProcessId()
    {
        string line = HelperLogFormat.FormatLine(new DateTime(2026, 10, 21, 9, 8, 7, 123, DateTimeKind.Utc), 4242, "INFO", "first\r\nsecond\nthird");
        Assert.Equal("2026-10-21T09:08:07.123Z [4242] INFO first | second third", line);
        Assert.DoesNotContain('\n', line);
        Assert.True(HelperLogFormat.ShouldRotate(1024 * 1024));
        Assert.True(HelperLogFormat.ShouldRotate(5 * 1024 * 1024));
        Assert.False(HelperLogFormat.ShouldRotate(1024 * 1024 - 1));
    }

    // ------------------------------------------------------------------ file system: sandbox

    /// <summary>
    /// A fresh folder below the temp folder. Junction targets in these tests are always inside it,
    /// so even a wrong delete cannot reach anything outside.
    /// </summary>
    private sealed class TempArea : IDisposable
    {
        private readonly string _parent;

        public TempArea()
        {
            _parent = Path.Combine(Path.GetTempPath(), "arena-session-helper-tests");
            Base = Path.Combine(_parent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Base);
            Log = new List<string>();
        }

        public string Base { get; }

        public List<string> Log { get; }

        public string At(params string[] parts) => Path.Combine(new[] { Base }.Concat(parts).ToArray());

        public HelperSandbox Sandbox(string root, int attempts = 3, int delay = 10)
        {
            Directory.CreateDirectory(root);
            return new HelperSandbox(root, message => Log.Add(message)) { RetryAttempts = attempts, RetryDelayMilliseconds = delay };
        }

        public string Write(string path, string content = "content")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            try
            {
                new HelperSandbox(_parent, _ => { }).DeleteTree(Base);
            }
            catch (Exception)
            {
                // A leftover temp folder is not a test failure.
            }
        }
    }

    [SkippableFact]
    public void AJunctionIsCreatedWithoutAdministratorRights()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string target = area.Write(area.At("outside", "data", "file.txt"), "hello");
        var box = area.Sandbox(area.At("root"));

        box.CreateJunction(area.At("root", "link"), area.At("outside", "data"));

        Assert.True(HelperFileSystem.IsReparsePoint(area.At("root", "link")));
        Assert.Equal("hello", File.ReadAllText(area.At("root", "link", "file.txt")));
        Assert.Equal(1, box.JunctionsCreated);
        Assert.True(File.Exists(target));
    }

    [SkippableFact]
    public void DeletingAJunctionLeavesItsTargetAlone()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        area.Write(area.At("outside", "data", "a.txt"), "a");
        area.Write(area.At("outside", "data", "sub", "b.txt"), "b");
        var box = area.Sandbox(area.At("root"));
        box.CreateJunction(area.At("root", "link"), area.At("outside", "data"));

        box.DeleteTree(area.At("root", "link"));

        Assert.False(HelperFileSystem.PathExists(area.At("root", "link")));
        Assert.Equal("a", File.ReadAllText(area.At("outside", "data", "a.txt")));
        Assert.Equal("b", File.ReadAllText(area.At("outside", "data", "sub", "b.txt")));
    }

    [SkippableFact]
    public void DeletingATreeThatHoldsJunctionsRemovesThemAsLinks()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        area.Write(area.At("outside", "database", "items.json"), "items");
        area.Write(area.At("outside", "images", "deep", "x.png"), "png");
        area.Write(area.At("root", "tree", "plain", "own.txt"), "own");
        area.Write(area.At("root", "tree", "readonly.txt"), "ro");
        File.SetAttributes(area.At("root", "tree", "readonly.txt"), FileAttributes.ReadOnly);
        var box = area.Sandbox(area.At("root"));
        box.CreateJunction(area.At("root", "tree", "database"), area.At("outside", "database"));
        box.CreateJunction(area.At("root", "tree", "plain", "images"), area.At("outside", "images"));

        box.DeleteTree(area.At("root", "tree"));

        Assert.False(HelperFileSystem.PathExists(area.At("root", "tree")));
        Assert.Equal("items", File.ReadAllText(area.At("outside", "database", "items.json")));
        Assert.Equal("png", File.ReadAllText(area.At("outside", "images", "deep", "x.png")));
        Assert.Contains(area.Log, line => line.Contains("without entering it"));
    }

    [SkippableFact]
    public void ADanglingJunctionIsRemovedToo()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        area.Write(area.At("outside", "gone", "f.txt"));
        var box = area.Sandbox(area.At("root"));
        box.CreateJunction(area.At("root", "tree", "link"), area.At("outside", "gone"));
        Directory.Delete(area.At("outside", "gone"), true);

        Assert.True(HelperFileSystem.PathExists(area.At("root", "tree", "link")));
        box.DeleteTree(area.At("root", "tree"));

        Assert.False(HelperFileSystem.PathExists(area.At("root", "tree")));
    }

    [SkippableFact]
    public void ASymbolicLinkIsNeverFollowedEither()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        area.Write(area.At("outside", "data", "keep.txt"), "keep");
        area.Write(area.At("root", "tree", "x.txt"));
        try
        {
            Directory.CreateSymbolicLink(area.At("root", "tree", "symlink"), area.At("outside", "data"));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Skip.If(true, "creating a symbolic link needs a privilege this account does not have");
        }

        area.Sandbox(area.At("root")).DeleteTree(area.At("root", "tree"));

        Assert.False(HelperFileSystem.PathExists(area.At("root", "tree")));
        Assert.Equal("keep", File.ReadAllText(area.At("outside", "data", "keep.txt")));
    }

    [SkippableTheory]
    [InlineData("outside")]
    [InlineData("sibling")]
    [InlineData("root itself")]
    [InlineData("parent segment")]
    [InlineData("relative")]
    public void NothingOutsideTheRootIsEverDeleted(string kind)
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        area.Write(area.At("root", "inside.txt"));
        area.Write(area.At("outside", "keep.txt"), "keep");
        area.Write(area.At("root2", "keep.txt"), "keep");
        var box = area.Sandbox(area.At("root"));
        string victim = kind switch
        {
            "outside" => area.At("outside"),
            "sibling" => area.At("root2"),
            "root itself" => area.At("root"),
            "parent segment" => area.At("root", "..", "outside"),
            _ => "relative.txt",
        };

        var ex = Assert.Throws<HelperException>(() => box.DeleteTree(victim));

        Assert.Contains("outside", ex.Message);
        Assert.True(File.Exists(area.At("root", "inside.txt")));
        Assert.Equal("keep", File.ReadAllText(area.At("outside", "keep.txt")));
        Assert.Equal("keep", File.ReadAllText(area.At("root2", "keep.txt")));
    }

    [SkippableFact]
    public void TheSandboxAcceptsOnlyPathsStrictlyBelowItsRoot()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var box = area.Sandbox(area.At("root"));

        Assert.Equal(area.At("root", "b"), box.Require(area.At("root", "a", "..", "b")));
        Assert.Equal(area.At("root", "x", "y"), box.Require(area.At("root", "x", "y") + Path.DirectorySeparatorChar));
        Assert.Throws<HelperException>(() => box.Require(area.At("root")));
        Assert.Throws<HelperException>(() => box.Require(area.At("root2", "x")));
        Assert.Throws<HelperException>(() => box.Require(area.At("root", "..", "x")));
        Assert.Throws<HelperException>(() => box.Require(area.Base));
        Assert.Throws<HelperException>(() => box.Require(@"C:\"));
        Assert.Throws<HelperException>(() => box.Require("root\\x"));
        Assert.Throws<HelperException>(() => box.Require(null!));
        Assert.Throws<HelperException>(() => new HelperSandbox(@"C:\", _ => { }));
        Assert.Throws<HelperException>(() => new HelperSandbox("relative", _ => { }));
    }

    [SkippableFact]
    public void AJunctionOnTheWayDownFromTheRootStopsEveryOperation()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        area.Write(area.At("outside", "sub", "victim.txt"), "keep");
        var box = area.Sandbox(area.At("root"));
        box.CreateJunction(area.At("root", "via"), area.At("outside"));
        string through = area.At("root", "via", "sub");

        Assert.Throws<HelperException>(() => box.DeleteTree(through));
        Assert.Throws<HelperException>(() => box.WriteAllText(area.At("root", "via", "new.txt"), "x"));
        Assert.Throws<HelperException>(() => box.CreateDirectory(area.At("root", "via", "made")));
        Assert.Throws<HelperException>(() => box.CopyFile(area.Write(area.At("src.txt")), area.At("root", "via", "copy.txt")));
        Assert.Throws<HelperException>(() => box.CopyDirectory(area.At("outside"), area.At("root", "via", "copied")));

        Assert.Equal("keep", File.ReadAllText(area.At("outside", "sub", "victim.txt")));
        Assert.False(File.Exists(area.At("outside", "new.txt")));
        Assert.False(Directory.Exists(area.At("outside", "made")));
        Assert.False(File.Exists(area.At("outside", "copy.txt")));
        Assert.False(Directory.Exists(area.At("outside", "copied")));
    }

    [SkippableFact]
    public void ARootThatIsAJunctionIsRefused()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        area.Write(area.At("real", "keep.txt"), "keep");
        Directory.CreateDirectory(area.At("holder"));
        var maker = area.Sandbox(area.At("holder"));
        maker.CreateJunction(area.At("holder", "session"), area.At("real"));
        var box = new HelperSandbox(area.At("holder", "session"), _ => { });

        Assert.Throws<HelperException>(() => box.DeleteTree(area.At("holder", "session", "keep.txt")));
        Assert.Throws<HelperException>(() => box.WriteAllText(area.At("holder", "session", "new.txt"), "x"));

        Assert.Equal("keep", File.ReadAllText(area.At("real", "keep.txt")));
        Assert.False(File.Exists(area.At("real", "new.txt")));
    }

    [SkippableFact]
    public void JunctionsAreRefusedForBadTargets()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        area.Write(area.At("outside", "f.txt"));
        area.Write(area.At("root", "inside", "g.txt"));
        area.Write(area.At("root", "taken.txt"));
        var box = area.Sandbox(area.At("root"));

        Assert.Throws<HelperException>(() => box.CreateJunction(area.At("root", "a"), area.At("missing")));
        Assert.Throws<HelperException>(() => box.CreateJunction(area.At("root", "b"), area.At("outside", "f.txt")));
        Assert.Throws<HelperException>(() => box.CreateJunction(area.At("root", "c"), area.At("root", "inside")));
        Assert.Throws<HelperException>(() => box.CreateJunction(area.At("root", "taken.txt"), area.At("outside")));
        Assert.Throws<HelperException>(() => box.CreateJunction(area.At("elsewhere", "d"), area.At("outside")));
        Assert.Throws<HelperException>(() => box.CreateJunction(area.At("root", "e"), "relative"));
        Assert.Equal(0, box.JunctionsCreated);
        Assert.False(Directory.Exists(area.At("root", "a")));
    }

    [SkippableFact]
    public void CopyingASourceFolderSkipsJunctionsInsideIt()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        area.Write(area.At("src", "mod", "a.dll"), "dll");
        area.Write(area.At("src", "mod", "config", "c.json"), "cfg");
        area.Write(area.At("elsewhere", "secret.txt"), "secret");
        var maker = area.Sandbox(area.At("src"));
        maker.CreateJunction(area.At("src", "mod", "shared"), area.At("elsewhere"));
        var box = area.Sandbox(area.At("root"));

        box.CopyDirectory(area.At("src", "mod"), area.At("root", "user", "mods", "mod"));

        Assert.Equal("dll", File.ReadAllText(area.At("root", "user", "mods", "mod", "a.dll")));
        Assert.Equal("cfg", File.ReadAllText(area.At("root", "user", "mods", "mod", "config", "c.json")));
        Assert.False(HelperFileSystem.PathExists(area.At("root", "user", "mods", "mod", "shared")));
        Assert.Contains(area.Log, line => line.Contains("skipped the reparse point"));
        Assert.Equal(2, box.FilesCopied);
    }

    [SkippableFact]
    public void PathsLongerThanMaxPathAreCopiedAndDeleted()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string deep = area.At("src", "mod");
        for (int i = 0; i < 12; i++) deep = Path.Combine(deep, new string('d', 30) + i);
        area.Write(Path.Combine(deep, "file.txt"), "deep");
        Assert.True(deep.Length > 400, "the fixture must exceed MAX_PATH by far");
        var box = area.Sandbox(area.At("root"));

        box.CopyDirectory(area.At("src", "mod"), area.At("root", "user", "mods", "mod"));

        string copied = area.At("root", "user", "mods", "mod") + deep.Substring(area.At("src", "mod").Length);
        Assert.Equal("deep", File.ReadAllText(Path.Combine(copied, "file.txt")));
        box.CreateJunction(area.At("root", "user", "link"), area.At("src"));

        box.DeleteTree(area.At("root", "user"));

        Assert.False(Directory.Exists(area.At("root", "user")));
        Assert.Equal("deep", File.ReadAllText(Path.Combine(deep, "file.txt")));
    }

    [SkippableFact]
    public void CopiesKeepContentAndTimestamp()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string source = area.Write(area.At("src", "p.json"), "{\"a\":1}");
        var stamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(source, stamp);
        var box = area.Sandbox(area.At("root"));

        box.CopyFile(source, area.At("root", "deep", "er", "p.json"));

        Assert.Equal("{\"a\":1}", File.ReadAllText(area.At("root", "deep", "er", "p.json")));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(area.At("root", "deep", "er", "p.json")));
    }

    [SkippableFact]
    public void ACopyWaitsForALockedSourceToBeReleased()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string source = area.Write(area.At("src", "locked.txt"), "payload");
        var box = area.Sandbox(area.At("root"), attempts: 40, delay: 25);

        using var gate = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            using var stream = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            gate.Set();
            Thread.Sleep(300);
        });
        holder.Start();
        gate.Wait();

        box.CopyFile(source, area.At("root", "locked.txt"));
        holder.Join();

        Assert.Equal("payload", File.ReadAllText(area.At("root", "locked.txt")));
        Assert.Contains(area.Log, line => line.StartsWith("retry "));
    }

    [SkippableFact]
    public void ACopyOfASourceThatStaysLockedFailsInsteadOfHanging()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string source = area.Write(area.At("src", "locked.txt"), "payload");
        var box = area.Sandbox(area.At("root"), attempts: 3, delay: 5);

        using var stream = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.ThrowsAny<IOException>(() => box.CopyFile(source, area.At("root", "locked.txt")));
    }

    [SkippableFact]
    public void ADeleteWaitsForALockedFileToBeReleased()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string file = area.Write(area.At("root", "tree", "busy.txt"));
        var box = area.Sandbox(area.At("root"), attempts: 40, delay: 25);

        using var gate = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            gate.Set();
            Thread.Sleep(300);
        });
        holder.Start();
        gate.Wait();

        box.DeleteTree(area.At("root", "tree"));
        holder.Join();

        Assert.False(Directory.Exists(area.At("root", "tree")));
    }

    [SkippableFact]
    public void DeletingSomethingThatIsNotThereIsNotAnError()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var box = area.Sandbox(area.At("root"));

        box.DeleteTree(area.At("root", "nothing"));
        box.DeleteFile(area.At("root", "nothing", "at", "all.txt"));
    }

    [SkippableFact]
    public void ASharedReadWorksWhileAnotherHandleIsOpenForWriting()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string file = area.Write(area.At("a.json"), "{\"x\":1}");

        using var writer = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        Assert.Equal("{\"x\":1}", HelperFileSystem.ReadAllTextShared(file));
    }

    // ------------------------------------------------------------------ file system: the server tree

    /// <summary>A stand-in for the installed SPT runtime, with the folders the helper reads.</summary>
    private sealed class FakeRuntime
    {
        public FakeRuntime(TempArea area)
        {
            Dir = area.At("SPT_Runtime");
            area.Write(Path.Combine(Dir, "sptLogger.json"), SptLoggerJson);
            area.Write(Path.Combine(Dir, "SPT.Server.exe"), "MZ");
            area.Write(Path.Combine(Dir, "SPT_Data", "checks.dat"), "checks");
            area.Write(Path.Combine(Dir, "SPT_Data", "configs", "http.json"), SptHttpJson);
            area.Write(Path.Combine(Dir, "SPT_Data", "configs", "core.json"), "{\"core\":true}");
            area.Write(Path.Combine(Dir, "SPT_Data", "database", "items.json"), "items");
            area.Write(Path.Combine(Dir, "SPT_Data", "database", "locales", "en.json"), "en");
            area.Write(Path.Combine(Dir, "SPT_Data", "images", "a.png"), "png");
            area.Write(Path.Combine(Dir, "SPT_Data", "Launcher", "l.txt"), "launcher");
            area.Write(Path.Combine(Dir, "SPT_Data", "wwwroot", "index.html"), "web");
            Directory.CreateDirectory(Path.Combine(Dir, "SPT_Data", "dotnet"));
            area.Write(Path.Combine(Dir, "user", "mods", "ArenaMode", "maschine-ArenaMode.Server.dll"), "arena");
            area.Write(Path.Combine(Dir, "user", "mods", "ArenaMode", "config", "kits.json"), "kits");
            area.Write(Path.Combine(Dir, "user", "mods", "Solarint-SAIN-ServerMod", "sain.dll"), "sain");
            area.Write(Path.Combine(Dir, "user", "mods", "Unlisted-Mod", "other.dll"), "other");
            area.Write(Path.Combine(Dir, "user", "profiles", ProfileId + ".json"), "{\"profile\":1}");
            area.Write(Path.Combine(Dir, "user", "profiles", ProfileId + ".sync-conflict-20261012-144100-X.json"), "{\"conflict\":1}");
            area.Write(Path.Combine(Dir, "user", "profiles", "6ab7db1c64f04b565cf78be9.json"), "{\"other\":1}");
            area.Write(Path.Combine(Dir, "user", "profileData", ProfileId, "pft.json"), "{\"pft\":1}");
            area.Write(Path.Combine(Dir, "user", "profileData", "6ab7db1c64f04b565cf78be9", "pft.json"), "{\"pft\":2}");
            area.Write(Path.Combine(Dir, "user", "certs", "server.crt"), "crt");
            area.Write(Path.Combine(Dir, "user", "credentials", "credentials.json"), "{\"c\":1}");
            area.Write(Path.Combine(Dir, "user", "logs", "spt", "old.log"), "log");
        }

        public string Dir { get; }

        public HelperRequest Request(string direction = "enter")
        {
            var fields = RequestFields(direction);
            fields["sptRuntimeDir"] = Dir;
            fields["gameDir"] = Path.GetDirectoryName(Dir);
            fields["gameExe"] = Path.Combine(Path.GetDirectoryName(Dir)!, "EscapeFromTarkov.exe");
            fields["serverMods"] = new List<object?>
            {
                new Dictionary<string, object?> { ["guid"] = "com.maschine.ArenaMode", ["folder"] = Path.Combine(Dir, "user", "mods", "ArenaMode") },
                new Dictionary<string, object?> { ["guid"] = "me.sol.sain", ["folder"] = Path.Combine(Dir, "user", "mods", "Solarint-SAIN-ServerMod") },
            };
            return ParseRequest(fields, direction);
        }

        /// <summary>Every file below the runtime with its content, to prove nothing in it changed.</summary>
        public Dictionary<string, string> Snapshot()
        {
            return Directory.EnumerateFiles(Dir, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, path => File.ReadAllText(path), StringComparer.OrdinalIgnoreCase);
        }
    }

    [SkippableFact]
    public void TheServerTreeIsBuiltFromTheInstalledRuntime()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var runtime = new FakeRuntime(area);
        string root = area.At("session");
        var now = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

        var result = HelperServerTree.Build(runtime.Request(), 6970, root, "0.1.3", now, area.Log.Add);

        string tree = HelperServerTree.GetTreePath(root);
        Assert.Equal(tree, result.TreePath);
        Assert.Equal(6970, result.Port);

        // sptLogger.json is a patched copy.
        string logger = File.ReadAllText(Path.Combine(tree, "sptLogger.json"));
        Assert.Contains("\"filePattern\": \"arena-spt-SPT-VM-%DATE%.log\"", logger);
        Assert.Contains("\"filePattern\": \"arena-requests-SPT-VM-%DATE%.log\"", logger);
        Assert.Contains("\"filePattern\": \"spt-SPT-VM-%DATE%.log\"", SptLoggerJson);

        // SPT_Data: junctions for every folder except configs, copies for the rest.
        string data = Path.Combine(tree, "SPT_Data");
        Assert.Equal(new[] { "Launcher", "database", "dotnet", "images", "wwwroot" }.OrderBy(n => n, StringComparer.Ordinal),
            result.Junctions.OrderBy(n => n, StringComparer.Ordinal));
        foreach (string name in result.Junctions) Assert.True(HelperFileSystem.IsReparsePoint(Path.Combine(data, name)), name);
        Assert.Equal("items", File.ReadAllText(Path.Combine(data, "database", "items.json")));
        Assert.Equal("en", File.ReadAllText(Path.Combine(data, "database", "locales", "en.json")));
        Assert.False(HelperFileSystem.IsReparsePoint(Path.Combine(data, "configs")));
        Assert.False(HelperFileSystem.IsReparsePoint(Path.Combine(data, "checks.dat")));
        Assert.Equal("checks", File.ReadAllText(Path.Combine(data, "checks.dat")));
        Assert.Equal("{\"core\":true}", File.ReadAllText(Path.Combine(data, "configs", "core.json")));

        // http.json: the two ports, nothing else.
        Assert.Equal(SptHttpJson.Replace("\"port\": 6969", "\"port\": 6970").Replace("\"backendPort\": 6969", "\"backendPort\": 6970"),
            File.ReadAllText(Path.Combine(data, "configs", "http.json")));
        Assert.Equal(SptHttpJson, File.ReadAllText(Path.Combine(runtime.Dir, "SPT_Data", "configs", "http.json")));

        // user: exactly the listed mods, this one profile and its data, certificates and credentials.
        string user = Path.Combine(tree, "user");
        Assert.Equal(new[] { "ArenaMode", "Solarint-SAIN-ServerMod" }, result.Mods);
        Assert.Equal(new[] { "ArenaMode", "Solarint-SAIN-ServerMod" },
            Directory.GetDirectories(Path.Combine(user, "mods")).Select(path => Path.GetFileName(path)).OrderBy(n => n, StringComparer.Ordinal));
        Assert.False(HelperFileSystem.IsReparsePoint(Path.Combine(user, "mods", "ArenaMode")));
        Assert.Equal("kits", File.ReadAllText(Path.Combine(user, "mods", "ArenaMode", "config", "kits.json")));
        Assert.Equal(new[] { ProfileId + ".json" }, Directory.GetFiles(Path.Combine(user, "profiles")).Select(path => Path.GetFileName(path)));
        Assert.Equal("{\"profile\":1}", File.ReadAllText(Path.Combine(user, "profiles", ProfileId + ".json")));
        Assert.Equal(new[] { ProfileId }, Directory.GetDirectories(Path.Combine(user, "profileData")).Select(path => Path.GetFileName(path)));
        Assert.Equal("{\"pft\":1}", File.ReadAllText(Path.Combine(user, "profileData", ProfileId, "pft.json")));
        Assert.Equal("crt", File.ReadAllText(Path.Combine(user, "certs", "server.crt")));
        Assert.Equal("{\"c\":1}", File.ReadAllText(Path.Combine(user, "credentials", "credentials.json")));
        Assert.False(Directory.Exists(Path.Combine(user, "logs")));

        // The marker makes it an Arena instance.
        Assert.True(HelperMarker.TryParse(File.ReadAllText(HelperServerTree.GetMarkerPath(root)), out var marker, out var error), error);
        Assert.Equal(ProfileId, marker.ProfileId);
        Assert.Equal(6970, marker.Port);
        Assert.Equal(runtime.Dir, marker.SourceRuntime);
        Assert.Equal("3755e0db97983440", marker.PolicyHash);
        Assert.Equal("0.1.3", marker.HelperVersion);
        Assert.Equal("2026-10-21T12:00:00Z", marker.CreatedUtc);
    }

    [SkippableFact]
    public void BuildingTheTreeTwiceReplacesItAndTearingItDownHarmsNothingElse()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var runtime = new FakeRuntime(area);
        string root = area.At("session");
        var before = runtime.Snapshot();

        HelperServerTree.Build(runtime.Request(), 6970, root, "0.1.3", DateTime.UtcNow, area.Log.Add);
        string tree = HelperServerTree.GetTreePath(root);
        File.WriteAllText(Path.Combine(tree, "leftover.txt"), "from the first session");
        File.WriteAllText(Path.Combine(tree, "user", "profiles", ProfileId + ".json"), "{\"changed in the session\":1}");

        var second = HelperServerTree.Build(runtime.Request(), 6971, root, "0.1.3", DateTime.UtcNow, area.Log.Add);

        Assert.Equal(6971, second.Port);
        Assert.False(File.Exists(Path.Combine(tree, "leftover.txt")));
        Assert.Equal("{\"profile\":1}", File.ReadAllText(Path.Combine(tree, "user", "profiles", ProfileId + ".json")));
        Assert.Contains("\"port\": 6971", File.ReadAllText(Path.Combine(tree, "SPT_Data", "configs", "http.json")));

        // Writing through a copy never reaches the runtime; deleting the tree never reaches the linked folders.
        File.WriteAllText(Path.Combine(tree, "user", "mods", "ArenaMode", "config", "kits.json"), "edited in the arena tree");
        new HelperSandbox(root, _ => { }).DeleteTree(tree);

        Assert.False(Directory.Exists(tree));
        Assert.Equal(before, runtime.Snapshot());
    }

    [SkippableFact]
    public void ATreeThatCannotBeCompletedHasNoMarker()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var runtime = new FakeRuntime(area);
        File.Delete(Path.Combine(runtime.Dir, "user", "profiles", ProfileId + ".json"));
        string root = area.At("session");

        var ex = Assert.Throws<HelperException>(() => HelperServerTree.Build(runtime.Request(), 6970, root, "0.1.3", DateTime.UtcNow, area.Log.Add));

        Assert.Contains("no profile file", ex.Message);
        Assert.False(File.Exists(HelperServerTree.GetMarkerPath(root)));
    }

    [SkippableTheory]
    [InlineData("sptLogger.json", "sptLogger.json")]
    [InlineData("http.json", "http.json")]
    [InlineData("SPT_Data", "SPT_Data")]
    [InlineData("mod folder", "has no folder")]
    public void MissingInputsOfTheRuntimeAreNamed(string what, string fragment)
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var runtime = new FakeRuntime(area);
        switch (what)
        {
            case "sptLogger.json": File.Delete(Path.Combine(runtime.Dir, "sptLogger.json")); break;
            case "http.json": File.Delete(Path.Combine(runtime.Dir, "SPT_Data", "configs", "http.json")); break;
            case "SPT_Data": Directory.Delete(Path.Combine(runtime.Dir, "SPT_Data"), true); break;
            default: Directory.Delete(Path.Combine(runtime.Dir, "user", "mods", "Solarint-SAIN-ServerMod"), true); break;
        }

        var ex = Assert.Throws<HelperException>(() => HelperServerTree.Build(runtime.Request(), 6970, area.At("session"), "0.1.3", DateTime.UtcNow, area.Log.Add));

        Assert.Contains(fragment, ex.Message);
    }

    [SkippableFact]
    public void OnlyAnEnterRequestWithAGoodPortBuildsATree()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var runtime = new FakeRuntime(area);

        Assert.Throws<HelperException>(() => HelperServerTree.Build(runtime.Request("leave"), 6970, area.At("session"), "0.1.3", DateTime.UtcNow, area.Log.Add));
        Assert.Throws<HelperException>(() => HelperServerTree.Build(runtime.Request(), 0, area.At("session"), "0.1.3", DateTime.UtcNow, area.Log.Add));
        Assert.Throws<HelperException>(() => HelperServerTree.Build(runtime.Request(), 70000, area.At("session"), "0.1.3", DateTime.UtcNow, area.Log.Add));
        Assert.Throws<HelperException>(() => HelperServerTree.Build(runtime.Request(), 6970, "relative\\session", "0.1.3", DateTime.UtcNow, area.Log.Add));
        Assert.Throws<ArgumentNullException>(() => HelperServerTree.Build(null!, 6970, area.At("session"), "0.1.3", DateTime.UtcNow, area.Log.Add));
        Assert.False(Directory.Exists(area.At("session", "server")));
    }

    [SkippableFact]
    public void TheTreeNeverReachesOutsideTheRuntimeRootEvenWhenAFolderIsReplacedByAJunction()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var runtime = new FakeRuntime(area);
        string root = area.At("session");
        area.Write(area.At("outside", "keep.txt"), "keep");

        // Someone made <root>\server a junction to a folder with valuable content.
        Directory.CreateDirectory(root);
        new HelperSandbox(root, _ => { }).CreateJunction(HelperServerTree.GetTreePath(root), area.At("outside"));

        HelperServerTree.Build(runtime.Request(), 6970, root, "0.1.3", DateTime.UtcNow, area.Log.Add);

        Assert.Equal("keep", File.ReadAllText(area.At("outside", "keep.txt")));
        Assert.False(File.Exists(area.At("outside", "sptLogger.json")));
        Assert.False(HelperFileSystem.IsReparsePoint(HelperServerTree.GetTreePath(root)));
        Assert.True(File.Exists(HelperServerTree.GetMarkerPath(root)));
    }

    [Fact]
    public void TheFixedFileNamesMatchTheContract()
    {
        Assert.Equal(Path.Combine("R", "server"), HelperServerTree.GetTreePath("R"));
        Assert.Equal(Path.Combine("R", "server", "arena-session.json"), HelperServerTree.GetMarkerPath("R"));
        Assert.Equal(Path.Combine("R", "server.pid"), HelperServerTree.GetPidFilePath("R"));
        Assert.Equal("arena-", HelperLayout.LogFilePatternPrefix);
        Assert.Equal(120, HelperLayout.GameExitTimeoutSeconds);
        Assert.Equal(180, HelperLayout.ServerStartTimeoutSeconds);
        Assert.Equal(1024 * 1024, HelperLayout.LogRotateBytes);
    }

    // ------------------------------------------------------------------ the helper and the shared contract

    [Fact]
    public void TheSessionFlagTheHelperAppendsIsTheOneTheGateLooksFor()
    {
        var result = Rewrite(NormalArguments(), true, 6970);
        Assert.True(ArenaSessionContract.HasSessionFlag(new[] { "EscapeFromTarkov.exe" }.Concat(result)));
        Assert.False(ArenaSessionContract.HasSessionFlag(new[] { "EscapeFromTarkov.exe" }.Concat(Rewrite(result, false, 6969))));
    }

    [Fact]
    public void TheArenaPortRangeIsTheContractsRange()
    {
        var asked = new List<int>();
        HelperPorts.PickArenaPort(port => { asked.Add(port); return false; }, 6969);
        Assert.Equal(Enumerable.Range(ArenaSessionContract.ArenaPort, ArenaSessionContract.FallbackPortLast - ArenaSessionContract.ArenaPort + 1), asked);
    }
}
