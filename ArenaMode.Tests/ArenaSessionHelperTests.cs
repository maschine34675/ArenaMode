using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
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
/// torn down without harming the folders it only links to, and only the prepatch folders of the listed
/// mods are copied), plus the request's acknowledgement file (names, content, the atomic temporary-file-then-move
/// write, removal of stale files). Both files are linked into this project; the process and socket side of the
/// helper, and the order of one run, are covered by <see cref="ArenaSessionHelperProcessTests"/>.
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

    // ------------------------------------------------------------------ sptLogger.json: where the arena server logs

    private static string JsonString(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string LoggerWithPath(string pathJson) =>
        "{ \"loggers\": [ { \"type\": \"File\", \"filePath\": " + pathJson + ", \"filePattern\": \"spt-%DATE%.log\" } ] }";

    private static string Prefixed(string loggerJson) => loggerJson.Replace("\"spt-%DATE%", "\"arena-spt-%DATE%");

    [Theory]
    [InlineData("./user/logs/spt/")]
    [InlineData("user/logs/requests")]
    [InlineData(@".\user\logs\spt\")]
    [InlineData("./USER/Logs/spt/")]
    [InlineData("./user/../user/logs/spt")]
    [InlineData("./user/./logs//spt/")]
    [InlineData("user")]
    public void ALoggerPathBelowUserIsKept(string path)
    {
        Assert.True(HelperJsonText.IsLogPathUnderUser(path));
        string text = LoggerWithPath(JsonString(path));

        Assert.True(HelperJsonText.TryPrefixLogFilePatterns(text, "arena-", out var patched, out var changed, out List<string> moved, out var error), error);

        Assert.Empty(moved);
        Assert.Equal(1, changed);
        Assert.Equal(Prefixed(text), patched);
    }

    [Theory]
    [InlineData(@"D:\SPT41\SPT_Runtime\user\logs\spt")] // absolute
    [InlineData("C:/logs/requests/")]
    [InlineData(@"\\server\share\logs")]
    [InlineData(@"\logs")]
    [InlineData("/var/log/spt")]
    [InlineData("/user/logs/spt")] // starts at the root of the current drive, whatever its first segment is
    [InlineData(@"\user\logs")]
    [InlineData("C:logs")] // relative to the current folder of a drive
    [InlineData("../logs/spt/")] // climbs out of the tree
    [InlineData("../user/logs/spt")] // out of the tree and into a folder that is called user
    [InlineData("./../user/logs")]
    [InlineData("./user/../../user/logs")]
    [InlineData("./user/../../logs/spt")]
    [InlineData("./user/..")] // the tree itself
    [InlineData("./SPT_Data/database/")] // inside the tree, but the SPT_Data folders are junctions into the installation
    [InlineData("./user/../SPT_Data/database")]
    [InlineData("./logs/")] // inside the tree, not below user
    [InlineData("logs/user/spt")]
    [InlineData(".")]
    [InlineData("user./logs")] // Windows rewrites the segment
    [InlineData("user/logs:stream")]
    [InlineData("user/lo*gs")]
    [InlineData("user/con/x")] // a device name
    [InlineData(" ")]
    public void ALoggerPathThatIsNotRelativeBelowUserIsNot(string path)
    {
        Assert.False(HelperJsonText.IsLogPathUnderUser(path));
    }

    [Theory]
    [InlineData(@"D:\SPT41\SPT_Runtime\user\logs\spt", "spt")]
    [InlineData("C:/logs/requests/", "requests")]
    [InlineData(@"\\server\share\logs\", "logs")]
    [InlineData("/var/log/spt", "spt")]
    [InlineData("../logs/spt/", "spt")]
    [InlineData("./user/../../logs/spt", "spt")]
    [InlineData("./SPT_Data/database/", "database")]
    [InlineData("./logs/", "logs")]
    [InlineData(@"D:\", "arena")] // no folder name at all
    [InlineData(".", "arena")]
    [InlineData("C:logs", "arena")] // not a single folder name for Windows
    [InlineData(@"D:\logs\con", "arena")] // a device name
    public void ALoggerPathOutsideUserMovesBelowUserLogsUnderItsLastSegment(string path, string folder)
    {
        string text = LoggerWithPath(JsonString(path));
        string expected = "./user/logs/" + folder + "/";

        Assert.True(HelperJsonText.TryPrefixLogFilePatterns(text, "arena-", out var patched, out var changed, out List<string> moved, out var error), error);

        Assert.Equal(Prefixed(LoggerWithPath(JsonString(expected))), patched);
        Assert.Equal(new[] { "'" + path + "' -> '" + expected + "'" }, moved);
        Assert.Equal(1, changed); // only the filePattern is counted as an edited pattern
        Assert.True(HelperJsonText.IsLogPathUnderUser(expected));

        // What was moved stays where it is: a second pass changes nothing.
        Assert.True(HelperJsonText.TryPrefixLogFilePatterns(patched, "arena-", out var again, out var changedAgain, out List<string> movedAgain, out _));
        Assert.Equal(patched, again);
        Assert.Equal(0, changedAgain);
        Assert.Empty(movedAgain);
    }

    [Fact]
    public void OnlyTheLoggerPathsOutsideUserChangeAndTheRestOfTheFileStaysAsItWas()
    {
        string text = SptLoggerJson.Replace("\"./user/logs/requests/\"", JsonString(@"D:\SPT41\SPT_Runtime\user\logs\requests"));

        Assert.True(HelperJsonText.TryPrefixLogFilePatterns(text, "arena-", out var patched, out var changed, out List<string> moved, out var error), error);

        Assert.Equal(2, changed);
        Assert.Equal(new[] { @"'D:\SPT41\SPT_Runtime\user\logs\requests' -> './user/logs/requests/'" }, moved);
        Assert.Equal(SptLoggerJson
            .Replace("\"filePattern\": \"spt-SPT-VM-%DATE%.log\"", "\"filePattern\": \"arena-spt-SPT-VM-%DATE%.log\"")
            .Replace("\"filePattern\": \"requests-SPT-VM-%DATE%.log\"", "\"filePattern\": \"arena-requests-SPT-VM-%DATE%.log\""), patched);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("5")]
    [InlineData("\"\"")] // SPT itself refuses an empty path
    [InlineData("[ \"C:\\\\x\" ]")]
    public void AFilePathThatIsNotAStringValueIsLeftAlone(string valueJson)
    {
        string text = LoggerWithPath(valueJson);

        Assert.True(HelperJsonText.TryPrefixLogFilePatterns(text, "arena-", out var patched, out _, out List<string> moved, out var error), error);

        Assert.Empty(moved);
        Assert.Equal(Prefixed(text), patched);
    }

    [Fact]
    public void AFilePathAtAnyDepthIsJudgedLikeAFilePatternAtAnyDepth()
    {
        const string text = "{ \"a\": { \"b\": [ { \"filePath\": \"C:/deep\", \"filePattern\": \"x.log\" } ] }, \"filePath\": \"user/ok\" }";

        Assert.True(HelperJsonText.TryPrefixLogFilePatterns(text, "arena-", out var patched, out var changed, out List<string> moved, out var error), error);

        Assert.Equal("{ \"a\": { \"b\": [ { \"filePath\": \"./user/logs/deep/\", \"filePattern\": \"arena-x.log\" } ] }, \"filePath\": \"user/ok\" }", patched);
        Assert.Equal(1, changed);
        Assert.Single(moved);
    }

    [SkippableFact]
    public void TheTreesLoggerConfigurationNeverLogsOutsideUser()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var runtime = new FakeRuntime(area);
        string absolute = area.At("real runtime", "user", "logs", "spt");
        string logger = "{ \"loggers\": [ { \"type\": \"File\", \"filePath\": " + JsonString(absolute) + ", \"filePattern\": \"spt-%DATE%.log\" }, "
            + "{ \"type\": \"File\", \"filePath\": \"./user/logs/requests/\", \"filePattern\": \"requests-%DATE%.log\" } ] }";
        File.WriteAllText(Path.Combine(runtime.Dir, "sptLogger.json"), logger);
        string root = area.At("session");

        var result = HelperServerTree.Build(runtime.Request(), 6970, root, "0.1.3", DateTime.UtcNow, area.Log.Add);

        string copy = File.ReadAllText(Path.Combine(HelperServerTree.GetTreePath(root), "sptLogger.json"));
        Assert.Equal(logger
            .Replace(JsonString(absolute), "\"./user/logs/spt/\"")
            .Replace("\"spt-%DATE%", "\"arena-spt-%DATE%")
            .Replace("\"requests-%DATE%", "\"arena-requests-%DATE%"), copy);
        Assert.Equal(new[] { "'" + absolute + "' -> './user/logs/spt/'" }, result.LogPathRewrites);
        Assert.Equal(logger, File.ReadAllText(Path.Combine(runtime.Dir, "sptLogger.json"))); // the installation's file is not touched
    }

    [SkippableFact]
    public void ALoggerConfigurationThatNeedsNoMoveLeavesNothingToReport()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var runtime = new FakeRuntime(area);

        var result = HelperServerTree.Build(runtime.Request(), 6970, area.At("session"), "0.1.3", DateTime.UtcNow, area.Log.Add);

        Assert.Empty(result.LogPathRewrites);
    }

    // ------------------------------------------------------------------ backup.json (review 2026-10-23)

    private const string SptBackupJson = """
        {
          "enabled": true,
          "maxBackups": 15,
          "backupCooldown": 30,
          "directory": "./user/profiles/backups",
          "backupInterval": {
            "enabled": false,
            "intervalMinutes": 120
          }
        }
        """;

    [Theory]
    [InlineData("D:/SPT-Backups", "D:/SPT-Backups")]
    [InlineData("D:\\\\SPT-Backups", "D:\\SPT-Backups")]
    [InlineData("../../normal-backups", "../../normal-backups")]
    [InlineData("./user/../../backups", "./user/../../backups")]
    [InlineData("./SPT_Data/backups", "./SPT_Data/backups")]
    [InlineData("./logs/backups", "./logs/backups")]
    [InlineData("/backups", "/backups")]
    [InlineData("", "")]
    public void TheBackupDirectoryOfTheTreeIsMovedBelowUserWhenItWouldLeaveIt(string jsonValue, string decoded)
    {
        // SPT's backup service writes to <directory>/<date> against the working directory, deletes the oldest folders there
        // and restores from the newest: a directory outside user would mix the disposable profile's copies into the normal
        // server's pool. The private default replaces it; everything else in the file stays as it was.
        string text = SptBackupJson.Replace("\"./user/profiles/backups\"", "\"" + jsonValue + "\"");
        Assert.True(HelperJsonText.TryConfineBackupDirectory(text, out var patched, out var rewrite, out var error), error);
        Assert.Equal(SptBackupJson, patched);
        Assert.Equal("'" + decoded + "' -> './user/profiles/backups'", rewrite);
    }

    [Theory]
    [InlineData("./user/profiles/backups")]
    [InlineData("user\\\\profiles\\\\backups")]
    [InlineData("./user/../user/backups/")]
    [InlineData("USER/x")]
    public void ABackupDirectoryBelowUserIsKeptByteForByte(string jsonValue)
    {
        string text = SptBackupJson.Replace("\"./user/profiles/backups\"", "\"" + jsonValue + "\"");
        Assert.True(HelperJsonText.TryConfineBackupDirectory(text, out var patched, out var rewrite, out var error), error);
        Assert.Equal(text, patched);
        Assert.Null(rewrite);
    }

    [Fact]
    public void ABackupConfigurationWithoutADirectoryGetsThePrivateOneAddedAndOnlyTheTopLevelMemberIsJudged()
    {
        // SPT's code default for the member is the empty string, which the backup service resolves to the root of the
        // working directory's drive (the private folder is only the value the shipped file carries): the member is added,
        // in the file's style, at the end of the object, and a second pass then finds a path below user.
        string without = SptBackupJson.Replace("\"directory\": \"./user/profiles/backups\",", "");
        Assert.DoesNotContain("directory", without);
        Assert.True(HelperJsonText.TryConfineBackupDirectory(without, out var patched, out var rewrite, out var error), error);
        Assert.EndsWith("  },\n  \"directory\": \"./user/profiles/backups\"\n}", patched!.Replace("\r\n", "\n"));
        Assert.Equal(without.Replace("\r\n", "\n").TrimEnd('}', '\n'), patched.Replace("\r\n", "\n").Substring(0, without.Replace("\r\n", "\n").TrimEnd('}', '\n').Length));
        Assert.Equal("(no directory member) -> './user/profiles/backups'", rewrite);
        Assert.True(ArenaMiniJson.TryParse(patched, out var parsed, out error), error);
        Assert.Equal("./user/profiles/backups", ArenaMiniJson.GetString(ArenaMiniJson.AsObject(parsed)!, "directory"));
        Assert.True(HelperJsonText.TryConfineBackupDirectory(patched, out var again, out var rewriteAgain, out error), error);
        Assert.Equal(patched, again);
        Assert.Null(rewriteAgain);

        // The file's own style: compact stays compact, an empty object gets the member on a line of its own.
        Assert.True(HelperJsonText.TryConfineBackupDirectory("{\"enabled\":true}", out patched, out rewrite, out error), error);
        Assert.Equal("{\"enabled\":true,\"directory\":\"./user/profiles/backups\"}", patched);
        Assert.True(HelperJsonText.TryConfineBackupDirectory("{}", out patched, out rewrite, out error), error);
        Assert.Equal("{\n  \"directory\": \"./user/profiles/backups\"\n}", patched);
        Assert.Equal("(no directory member) -> './user/profiles/backups'", rewrite);

        // A member of that name deeper in the document is somebody else's.
        string nested = SptBackupJson.Replace("\"intervalMinutes\": 120", "\"intervalMinutes\": 120, \"directory\": \"D:/elsewhere\"");
        Assert.True(HelperJsonText.TryConfineBackupDirectory(nested, out patched, out rewrite, out error), error);
        Assert.Equal(nested, patched);
        Assert.Null(rewrite);

        // A directory that is not a string cannot be judged: replaced (fail closed), named by its text.
        string number = SptBackupJson.Replace("\"./user/profiles/backups\"", "5");
        Assert.True(HelperJsonText.TryConfineBackupDirectory(number, out patched, out rewrite, out error), error);
        Assert.Equal(SptBackupJson, patched);
        Assert.Equal("'5' -> './user/profiles/backups'", rewrite);

        Assert.False(HelperJsonText.TryConfineBackupDirectory("{ \"directory\": ", out _, out _, out error));
        Assert.NotNull(error);
    }

    [SkippableFact]
    public void TheTreesBackupConfigurationNeverBacksUpOutsideUser()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var runtime = new FakeRuntime(area);
        string external = area.At("normal-backups");
        string config = SptBackupJson.Replace("\"./user/profiles/backups\"", JsonString(external));
        File.WriteAllText(Path.Combine(runtime.Dir, "SPT_Data", "configs", "backup.json"), config);
        string root = area.At("session");

        var result = HelperServerTree.Build(runtime.Request(), 6970, root, "0.1.3", DateTime.UtcNow, area.Log.Add);

        Assert.Equal(SptBackupJson, File.ReadAllText(Path.Combine(HelperServerTree.GetTreePath(root), "SPT_Data", "configs", "backup.json")));
        Assert.Equal(new[] { "backup.json: '" + external + "' -> './user/profiles/backups'" }, result.BackupDirectoryRewrites);
        Assert.Equal(config, File.ReadAllText(Path.Combine(runtime.Dir, "SPT_Data", "configs", "backup.json"))); // the installation's file is not touched
    }

    [SkippableFact]
    public void ABackupConfigurationBelowUserIsCopiedUnchangedAndARuntimeWithoutOneNeedsNothing()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var runtime = new FakeRuntime(area);
        string root = area.At("session");

        var without = HelperServerTree.Build(runtime.Request(), 6970, root, "0.1.3", DateTime.UtcNow, area.Log.Add);
        Assert.Empty(without.BackupDirectoryRewrites);
        Assert.False(File.Exists(Path.Combine(HelperServerTree.GetTreePath(root), "SPT_Data", "configs", "backup.json")));

        File.WriteAllText(Path.Combine(runtime.Dir, "SPT_Data", "configs", "backup.json"), SptBackupJson);
        var kept = HelperServerTree.Build(runtime.Request(), 6970, root, "0.1.3", DateTime.UtcNow, area.Log.Add);
        Assert.Empty(kept.BackupDirectoryRewrites);
        Assert.Equal(SptBackupJson, File.ReadAllText(Path.Combine(HelperServerTree.GetTreePath(root), "SPT_Data", "configs", "backup.json")));
    }

    [SkippableFact]
    public void EveryFileTheLoaderReadsAsTheBackupConfigIsConfinedAndAMalformedOneFailsTheBuild()
    {
        SkipUnlessWindows();
        // SPT's loader takes .json and .jsonc files whose name is a substring of spt-backup, the last one read wins.
        Assert.True(HelperLayout.IsBackupConfigFile("backup.json"));
        Assert.True(HelperLayout.IsBackupConfigFile("backup.jsonc"));
        Assert.True(HelperLayout.IsBackupConfigFile("spt-backup.json"));
        Assert.False(HelperLayout.IsBackupConfigFile("http.json"));
        Assert.False(HelperLayout.IsBackupConfigFile("core.json"));
        Assert.False(HelperLayout.IsBackupConfigFile("Backup.json")); // the loader compares exactly
        Assert.False(HelperLayout.IsBackupConfigFile("backup.JSON"));
        Assert.False(HelperLayout.IsBackupConfigFile("backup.json.bak"));

        using var area = new TempArea();
        var runtime = new FakeRuntime(area);
        string external = area.At("normal-backups");
        string config = SptBackupJson.Replace("\"./user/profiles/backups\"", JsonString(external));
        File.WriteAllText(Path.Combine(runtime.Dir, "SPT_Data", "configs", "backup.jsonc"), "// comment\n" + config);
        File.WriteAllText(Path.Combine(runtime.Dir, "SPT_Data", "configs", "spt-backup.json"), "{ \"enabled\": true }");
        string root = area.At("session");

        var result = HelperServerTree.Build(runtime.Request(), 6970, root, "0.1.3", DateTime.UtcNow, area.Log.Add);

        Assert.Equal(new[]
        {
            "backup.jsonc: '" + external + "' -> './user/profiles/backups'",
            "spt-backup.json: (no directory member) -> './user/profiles/backups'",
        }, result.BackupDirectoryRewrites);
        string configs = Path.Combine(HelperServerTree.GetTreePath(root), "SPT_Data", "configs");
        Assert.Equal("// comment\n" + SptBackupJson, File.ReadAllText(Path.Combine(configs, "backup.jsonc")));
        Assert.Equal("{ \"enabled\": true,\"directory\":\"./user/profiles/backups\" }", File.ReadAllText(Path.Combine(configs, "spt-backup.json")));
        Assert.Equal("// comment\n" + config, File.ReadAllText(Path.Combine(runtime.Dir, "SPT_Data", "configs", "backup.jsonc"))); // the installation's files are not touched

        File.WriteAllText(Path.Combine(runtime.Dir, "SPT_Data", "configs", "backup.json"), "{ \"directory\": ");
        var ex = Assert.Throws<HelperException>(() => HelperServerTree.Build(runtime.Request(), 6970, root, "0.1.3", DateTime.UtcNow, area.Log.Add));
        Assert.StartsWith("backup.json: ", ex.Message);
    }

    // ------------------------------------------------------------------ core.json: SPT's profile clean-up (2026-10-25)

    /// <summary>The fixes block as SPT 4.1 ships it: every profile clean-up off.</summary>
    private const string SptCoreJson = """
        {
          "serverName": "SPT",
          "fixes": {
            "fixShotgunDispersion": true,
            "shotgunIdsToFix": [
              "576165642459773c7a400233"
            ],
            "removeModItemsFromProfile": false,
            "removeInvalidTradersFromProfile": false,
            "fixProfileBreakingInventoryItemIssues": false
          },
          "survey": { "locale": { "removeModItemsFromProfile": false } }
        }
        """;

    /// <summary><see cref="SptCoreJson"/> with LF line ends, whatever the checkout's line ends are.</summary>
    private static string SptCore => SptCoreJson.Replace("\r\n", "\n");

    [Fact]
    public void TheCoreConfigOfTheTreeSwitchesOnSptsProfileCleanUpAndChangesNothingElse()
    {
        // The session leaves out the mods the policy excludes; with SPT's shipped values an item of such a mod in the profile
        // makes the server mark the whole profile invalid and refuse the game start. The copy switches SPT's clean-up on.
        Assert.True(HelperJsonText.TryEnableProfileCleanup(SptCore, out var patched, out var changes, out var error), error);
        Assert.Equal(SptCore
            .Replace("\"removeModItemsFromProfile\": false,\n    \"removeInvalidTradersFromProfile\": false",
                "\"removeModItemsFromProfile\": true,\n    \"removeInvalidTradersFromProfile\": true"), patched);
        Assert.Equal(new[] { "fixes.removeModItemsFromProfile: false -> true", "fixes.removeInvalidTradersFromProfile: false -> true" }, changes);

        // A member of that name outside fixes (here deeper in another object) and the third fix are left alone.
        Assert.Contains("\"locale\": { \"removeModItemsFromProfile\": false }", patched);
        Assert.Contains("\"fixProfileBreakingInventoryItemIssues\": false", patched);

        // A file that already has both on is not touched, byte for byte; a second pass over the result changes nothing.
        Assert.True(HelperJsonText.TryEnableProfileCleanup(patched, out var again, out var changesAgain, out error), error);
        Assert.Equal(patched, again);
        Assert.Empty(changesAgain);
    }

    [Theory]
    [InlineData("\"false\"", "\"false\"")]
    [InlineData("\"true\"", "\"true\"")]
    [InlineData("0", "0")]
    [InlineData("null", "null")]
    public void AFlagThatIsNotTheLiteralTrueIsReplaced(string value, string shown)
    {
        // Fail closed: SPT's reader would not read a string, a number or null as true.
        string text = "{ \"fixes\": { \"removeModItemsFromProfile\": " + value + ", \"removeInvalidTradersFromProfile\": true } }";
        Assert.True(HelperJsonText.TryEnableProfileCleanup(text, out var patched, out var changes, out var error), error);
        Assert.Equal("{ \"fixes\": { \"removeModItemsFromProfile\": true, \"removeInvalidTradersFromProfile\": true } }", patched);
        Assert.Equal(new[] { "fixes.removeModItemsFromProfile: " + shown + " -> true" }, changes);
    }

    [Fact]
    public void AMissingFlagOrAMissingFixesObjectIsAddedInTheFilesStyle()
    {
        // One flag missing: appended at the end of fixes, indented like its members.
        string oneMissing = "{\n  \"fixes\": {\n    \"removeModItemsFromProfile\": false\n  }\n}";
        Assert.True(HelperJsonText.TryEnableProfileCleanup(oneMissing, out var patched, out var changes, out var error), error);
        Assert.Equal("{\n  \"fixes\": {\n    \"removeModItemsFromProfile\": true,\n    \"removeInvalidTradersFromProfile\": true\n  }\n}", patched);
        Assert.Equal(new[] { "fixes.removeModItemsFromProfile: false -> true", "fixes.removeInvalidTradersFromProfile: (missing) -> true" }, changes);

        // An empty fixes object, a compact file and a file without fixes at all.
        Assert.True(HelperJsonText.TryEnableProfileCleanup("{\"fixes\":{}}", out patched, out changes, out error), error);
        AssertBothFlagsOn(patched);
        Assert.Equal(2, changes.Count);
        Assert.True(HelperJsonText.TryEnableProfileCleanup("{\"serverName\":\"SPT\"}", out patched, out changes, out error), error);
        Assert.Equal("{\"serverName\":\"SPT\",\"fixes\":{\"removeModItemsFromProfile\":true,\"removeInvalidTradersFromProfile\":true}}", patched);
        Assert.Equal(new[] { "fixes: (missing) -> {\"removeModItemsFromProfile\":true,\"removeInvalidTradersFromProfile\":true}" }, changes);
        Assert.True(HelperJsonText.TryEnableProfileCleanup("{}", out patched, out changes, out error), error);
        AssertBothFlagsOn(patched);

        // A fixes that is not an object is replaced by one (fail closed); a fixes nested deeper is somebody else's.
        Assert.True(HelperJsonText.TryEnableProfileCleanup("{ \"fixes\": null }", out patched, out changes, out error), error);
        Assert.Equal("{ \"fixes\": {\"removeModItemsFromProfile\":true,\"removeInvalidTradersFromProfile\":true} }", patched);
        Assert.Equal(new[] { "fixes: null -> {\"removeModItemsFromProfile\":true,\"removeInvalidTradersFromProfile\":true}" }, changes);
        string nested = "{ \"other\": { \"fixes\": { \"removeModItemsFromProfile\": false } }, \"fixes\": { \"removeModItemsFromProfile\": true, \"removeInvalidTradersFromProfile\": true } }";
        Assert.True(HelperJsonText.TryEnableProfileCleanup(nested, out patched, out changes, out error), error);
        Assert.Equal(nested, patched);
        Assert.Empty(changes);

        // An object inside an array inside fixes holds no direct member of fixes.
        string inArray = "{ \"fixes\": { \"list\": [ { \"removeModItemsFromProfile\": true } ], \"removeInvalidTradersFromProfile\": true } }";
        Assert.True(HelperJsonText.TryEnableProfileCleanup(inArray, out patched, out changes, out error), error);
        Assert.Equal(new[] { "fixes.removeModItemsFromProfile: (missing) -> true" }, changes);
        AssertBothFlagsOn(patched);

        Assert.False(HelperJsonText.TryEnableProfileCleanup("{ \"fixes\": ", out _, out _, out error));
        Assert.NotNull(error);
    }

    private static void AssertBothFlagsOn(string? text)
    {
        Assert.True(ArenaMiniJson.TryParse(text, out var root, out var error), error);
        var fixes = ArenaMiniJson.AsObject(ArenaMiniJson.AsObject(root)!["fixes"])!;
        foreach (string flag in HelperLayout.ProfileCleanupFlags) Assert.Equal(true, fixes[flag]);
    }

    [SkippableFact]
    public void TheTreesCoreConfigCleansTheProfileCopyAndTheRuntimesFileIsNotTouched()
    {
        SkipUnlessWindows();
        Assert.True(HelperLayout.IsCoreConfigFile("core.json"));
        Assert.True(HelperLayout.IsCoreConfigFile("core.jsonc"));
        Assert.True(HelperLayout.IsCoreConfigFile("spt-core.json"));
        Assert.False(HelperLayout.IsCoreConfigFile("Core.json")); // the loader compares exactly
        Assert.False(HelperLayout.IsCoreConfigFile("backup.json"));
        Assert.False(HelperLayout.IsCoreConfigFile("http.json"));
        Assert.False(HelperLayout.IsCoreConfigFile("core.json.bak"));
        Assert.False(HelperLayout.IsConfigFileOf("spt-core", ".json"));

        using var area = new TempArea();
        var runtime = new FakeRuntime(area);
        string runtimeCore = Path.Combine(runtime.Dir, "SPT_Data", "configs", "core.json");
        File.WriteAllText(runtimeCore, SptCore);
        string root = area.At("session");

        var result = HelperServerTree.Build(runtime.Request(), 6970, root, "0.1.3", DateTime.UtcNow, area.Log.Add);

        string copy = File.ReadAllText(Path.Combine(HelperServerTree.GetTreePath(root), "SPT_Data", "configs", "core.json"));
        Assert.True(HelperJsonText.TryEnableProfileCleanup(SptCore, out var expected, out _, out var error), error);
        Assert.Equal(expected, copy);
        Assert.Equal(new[] { "core.json: fixes.removeModItemsFromProfile: false -> true, fixes.removeInvalidTradersFromProfile: false -> true" },
            result.ProfileCleanupChanges);
        Assert.Equal(SptCore, File.ReadAllText(runtimeCore)); // the installation's file is not touched

        // A malformed core config fails the build with its name, before the server would.
        File.WriteAllText(runtimeCore, "{ \"fixes\": ");
        var ex = Assert.Throws<HelperException>(() => HelperServerTree.Build(runtime.Request(), 6970, root, "0.1.3", DateTime.UtcNow, area.Log.Add));
        Assert.StartsWith("core.json: ", ex.Message);
    }

    // ------------------------------------------------------------------ the helper mutex (review 2026-10-23)

    [SkippableFact]
    public void TheHelperMutexIsGlobalNamedForTheUserAndKeepsASecondHelperOut()
    {
        SkipUnlessWindows();
        // A Local\ object is one per logon session; the runtime root is one per user, so the mutex has to be too.
        using var identity = WindowsIdentity.GetCurrent();
        string sid = identity.User!.Value;
        Assert.Equal("Global\\maschine-ArenaMode.Session." + sid, HelperMutex.Name());
        Assert.Equal("Global\\maschine-ArenaMode.Session.S-1-5-21-1-2-3-1001", HelperMutex.NameFor("S-1-5-21-1-2-3-1001"));

        string name = HelperMutex.NameFor("S-1-5-21-test-" + Guid.NewGuid().ToString("N"));
        using var first = HelperMutex.Open(name);
        Assert.True(first.WaitOne(0));
        bool secondOwned = true;
        var other = new Thread(() =>
        {
            using var second = HelperMutex.Open(name);
            secondOwned = second.WaitOne(0);
            if (secondOwned) second.ReleaseMutex();
        });
        other.Start();
        Assert.True(other.Join(10000));
        Assert.False(secondOwned, "the same name opened from another thread is the same mutex");
        first.ReleaseMutex();
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
            @"C:\SPT41\SPT_Runtime", "3755e0db97983440", "0.1.3", @"C:\Users\x\AppData\Local\maschine-ArenaMode\session\server\SPT.Server.exe");
        Assert.True(ArenaMiniJson.TryParse(marker.ToJson(), out var parsed, out _));
        var obj = ArenaMiniJson.AsObject(parsed)!;
        Assert.Equal(new[] { "schema", "createdUtc", "profileId", "port", "sourceRuntime", "serverExe", "policyHash", "helperVersion" }, obj.Keys);
        Assert.Equal(1, ArenaMiniJson.GetInt(obj, "schema", 0));
        Assert.Equal("2026-10-21T12:30:45Z", ArenaMiniJson.GetString(obj, "createdUtc"));
        Assert.Equal(ProfileId, ArenaMiniJson.GetString(obj, "profileId"));
        Assert.Equal(6970, ArenaMiniJson.GetInt(obj, "port", 0));
        Assert.Equal(@"C:\SPT41\SPT_Runtime", ArenaMiniJson.GetString(obj, "sourceRuntime"));
        Assert.Equal(@"C:\Users\x\AppData\Local\maschine-ArenaMode\session\server\SPT.Server.exe", ArenaMiniJson.GetString(obj, "serverExe"));
        Assert.Equal("3755e0db97983440", ArenaMiniJson.GetString(obj, "policyHash"));
        Assert.Equal("0.1.3", ArenaMiniJson.GetString(obj, "helperVersion"));
    }

    [Fact]
    public void TheMarkerOfAnOlderHelperHasNoServerExe()
    {
        // Written by a helper that started the server from the installation's own runtime: no serverExe member.
        const string older = """
            { "schema": 1, "createdUtc": "2026-10-21T12:30:45Z", "profileId": "6ab7db1164f04b565cf78be7", "port": 6970,
              "sourceRuntime": "C:\\SPT41\\SPT_Runtime", "policyHash": "h", "helperVersion": "0.1.3" }
            """;
        Assert.True(HelperMarker.TryParse(older, out var marker, out var error), error);
        Assert.False(marker.RunsFromTree);
        Assert.Null(marker.ServerExe);

        // A marker created without it is written without it (and reads back the same).
        var created = HelperMarker.Create(DateTime.UtcNow, ProfileId, 6970, @"C:\SPT41\SPT_Runtime", "h", "0.1.3");
        Assert.DoesNotContain("serverExe", created.ToJson());
        Assert.True(HelperMarker.TryParse(created.ToJson(), out var back, out error), error);
        Assert.False(back.RunsFromTree);
    }

    [Fact]
    public void TheMarkerRoundTrips()
    {
        var marker = HelperMarker.Create(DateTime.UtcNow, ProfileId, 6971, @"C:\SPT41\SPT_Runtime", null!, "0.1.3", @"C:\t\server\SPT.Server.exe");
        Assert.True(HelperMarker.TryParse(marker.ToJson(), out var back, out var error), error);
        Assert.Equal(6971, back.Port);
        Assert.Equal(@"C:\SPT41\SPT_Runtime", back.SourceRuntime);
        Assert.Equal(@"C:\t\server\SPT.Server.exe", back.ServerExe);
        Assert.True(back.RunsFromTree);
        Assert.Null(back.PolicyHash);
        Assert.False(HelperMarker.TryParse("{\"schema\":2}", out _, out error));
        Assert.False(HelperMarker.TryParse("[1]", out _, out error));
        Assert.False(HelperMarker.TryParse("nope", out _, out error));
    }

    // ------------------------------------------------------------------ the server binaries and the server's identity

    [Theory]
    [InlineData("SPT.Server.exe", true)]
    [InlineData("SPT.Server.dll", true)]
    [InlineData("SPT.Server.deps.json", true)]
    [InlineData("SPT.Server.runtimeconfig.json", true)]
    [InlineData("SPTarkov.Server.Core.dll", true)]
    [InlineData("SPTarkov.Server.Core.pdb", true)]
    [InlineData("SPTarkov.Server.Core.xml", true)]
    [InlineData("0Harmony.dll", true)]
    [InlineData("argon2_sharp_core.dll", true)]
    [InlineData("sptLogger.json", false)]
    [InlineData("SPTLOGGER.JSON", false)]
    [InlineData("SPT.Launcher.exe", false)]
    [InlineData("SPT.Launcher.Linux", false)]
    [InlineData("spt.launcher.pdb", false)]
    [InlineData("SPT.Server.Linux", false)]
    [InlineData("SPT.Server.Linux.dll", false)]
    [InlineData("SPT.Server.Linux.deps.json", false)]
    [InlineData("SPT.Server.Linux.runtimeconfig.json", false)]
    [InlineData("Some.linux.Helper.dll", false)]
    [InlineData("libargon2_sharp_core.so", false)]
    [InlineData("LIBARGON2.SO", false)]
    [InlineData("arena-session.json", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("sub\\file.dll", false)]
    [InlineData("..", false)]
    [InlineData("name.dll ", false)]
    public void OnlyTheFilesTheWindowsServerLoadsAreServerBinaries(string? fileName, bool expected)
    {
        Assert.Equal(expected, HelperServerBinaries.IsServerBinary(fileName!));
    }

    [Fact]
    public void TheBinariesOfAListingAreSelectedAndSorted()
    {
        var listing = new[] { "SPT.Server.exe", "sptLogger.json", "SPT.Launcher.exe", "0Harmony.dll", "SPT.Server.Linux", "x.so", "SPT.Server.dll" };

        Assert.Equal(new[] { "0Harmony.dll", "SPT.Server.dll", "SPT.Server.exe" }, HelperServerBinaries.Select(listing));
        Assert.Empty(HelperServerBinaries.Select(null!));
        Assert.Empty(HelperServerBinaries.Select(Array.Empty<string>()));
    }

    [Theory]
    [InlineData("SPT.Server.dll", true, true)]
    [InlineData("spt.server.DLL", true, true)]
    [InlineData("SPT.Server.dll", false, false)]
    [InlineData("stale.dll", true, false)]
    [InlineData("arena-session.json", true, false)]
    [InlineData("user", false, false)]
    public void OnlyAPlainFileThatIsStillABinarySurvivesTheCleaningOfATree(string name, bool isPlainFile, bool expected)
    {
        var binaries = new[] { "0Harmony.dll", "SPT.Server.dll", "SPT.Server.exe" };
        Assert.Equal(expected, HelperServerBinaries.KeepsTreeEntry(name, isPlainFile, binaries));
        Assert.False(HelperServerBinaries.KeepsTreeEntry(name, isPlainFile, null!));
        Assert.False(HelperServerBinaries.KeepsTreeEntry(null!, isPlainFile, binaries));
    }

    [Fact]
    public void ACopyIsUnchangedOnlyWhenLengthAndWriteTimeBothMatch()
    {
        var time = new DateTime(2026, 9, 18, 17, 38, 0, DateTimeKind.Utc);

        Assert.True(HelperServerBinaries.IsUnchanged(229376, time, 229376, time));
        Assert.True(HelperServerBinaries.IsUnchanged(229376, time, 229376, time.ToLocalTime()), "the same instant in another kind");
        Assert.False(HelperServerBinaries.IsUnchanged(229376, time, 229377, time), "length");
        Assert.False(HelperServerBinaries.IsUnchanged(229376, time, 229376, time.AddTicks(1)), "one tick newer");
        Assert.False(HelperServerBinaries.IsUnchanged(229376, time, 229376, time.AddTicks(-1)), "one tick older");
        Assert.False(HelperServerBinaries.IsUnchanged(229376, time, 229376, time.AddDays(-1)), "an older copy");
    }

    [Fact]
    public void AMarkerOfThisHelperNamesTheTreeCopyAndNeverTheInstallationsExe()
    {
        var marker = HelperMarker.Create(DateTime.UtcNow, ProfileId, 6970, @"C:\SPT41\SPT_Runtime", "h", "0.1.4", @"C:\x\server\SPT.Server.exe");

        Assert.Equal(@"C:\L\session\server\SPT.Server.exe", HelperServerIdentity.ExpectedImage(marker, @"C:\L\session\server\SPT.Server.exe"));
        Assert.Null(HelperServerIdentity.ExpectedImage(marker, null!));
        Assert.Null(HelperServerIdentity.ExpectedImage(marker, string.Empty));
    }

    [Fact]
    public void AMarkerOfAnOlderHelperNamesTheInstallationsExe()
    {
        var older = HelperMarker.Create(DateTime.UtcNow, ProfileId, 6970, @"C:\SPT41\SPT_Runtime", "h", "0.1.3");

        Assert.Equal(Path.Combine(@"C:\SPT41\SPT_Runtime", "SPT.Server.exe"), HelperServerIdentity.ExpectedImage(older, @"C:\L\session\server\SPT.Server.exe"));
        older.SourceRuntime = null!;
        Assert.Null(HelperServerIdentity.ExpectedImage(older, @"C:\L\session\server\SPT.Server.exe"));
        Assert.Null(HelperServerIdentity.ExpectedImage(null!, @"C:\L\session\server\SPT.Server.exe"));
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
    public void ModCopyLinksBundleOnlyFoldersAndCopiesEverythingElse()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        area.Write(area.At("src", "mod", "a.dll"), "dll");
        area.Write(area.At("src", "mod", "bundles", "x.bundle"), "bx");
        area.Write(area.At("src", "mod", "bundles", "deep", "y.BUNDLE"), "by");
        area.Write(area.At("src", "mod", "mixed", "z.bundle"), "bz");
        area.Write(area.At("src", "mod", "mixed", "config.json"), "cfg");
        area.Write(area.At("src", "mod", "mixed", "inner", "w.bundle"), "bw");
        Directory.CreateDirectory(area.At("src", "mod", "empty"));
        var box = area.Sandbox(area.At("root"));

        box.CopyModDirectory(area.At("src", "mod"), area.At("root", "user", "mods", "mod"));

        string copy = area.At("root", "user", "mods", "mod");
        Assert.True(HelperFileSystem.IsReparsePoint(Path.Combine(copy, "bundles")), "a bundle-only folder is linked");
        Assert.Equal("by", File.ReadAllText(Path.Combine(copy, "bundles", "deep", "y.BUNDLE")));
        Assert.False(HelperFileSystem.IsReparsePoint(Path.Combine(copy, "mixed")), "a folder with another file is copied");
        Assert.True(HelperFileSystem.IsReparsePoint(Path.Combine(copy, "mixed", "inner")), "a bundle-only folder below a copied one is linked");
        Assert.Equal("cfg", File.ReadAllText(Path.Combine(copy, "mixed", "config.json")));
        Assert.False(HelperFileSystem.IsReparsePoint(Path.Combine(copy, "empty")), "an empty folder is copied, not linked");
        Assert.True(Directory.Exists(Path.Combine(copy, "empty")));
        Assert.Equal(2, box.BundleFoldersLinked);
        Assert.Equal(3, box.FilesCopied);

        box.DeleteTree(area.At("root", "user"));

        Assert.Equal("bx", File.ReadAllText(area.At("src", "mod", "bundles", "x.bundle")));
        Assert.Equal("bw", File.ReadAllText(area.At("src", "mod", "mixed", "inner", "w.bundle")));
    }

    [SkippableFact]
    public void BundleOnlyFolderRequiresBundlesOnlyAndAtLeastOne()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        area.Write(area.At("only", "a.bundle"), "a");
        area.Write(area.At("only", "sub", "b.bundle"), "b");
        area.Write(area.At("manifest", "a.bundle"), "a");
        area.Write(area.At("manifest", "a.bundle.manifest"), "m");
        Directory.CreateDirectory(area.At("emptysub", "nothing"));
        area.Write(area.At("emptysub", "a.bundle"), "a");
        Directory.CreateDirectory(area.At("empty"));
        area.Write(area.At("elsewhere", "c.bundle"), "c");
        area.Write(area.At("linked", "a.bundle"), "a");
        area.Sandbox(area.At("linked")).CreateJunction(area.At("linked", "link"), area.At("elsewhere"));

        Assert.True(HelperSandbox.IsBundleOnlyFolder(area.At("only"), 0));
        Assert.False(HelperSandbox.IsBundleOnlyFolder(area.At("manifest"), 0));
        Assert.False(HelperSandbox.IsBundleOnlyFolder(area.At("emptysub"), 0));
        Assert.False(HelperSandbox.IsBundleOnlyFolder(area.At("empty"), 0));
        Assert.False(HelperSandbox.IsBundleOnlyFolder(area.At("linked"), 0));
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
    public void AnUnchangedFileIsNotCopiedAgain()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string source = area.Write(area.At("src", "a.dll"), "12345");
        File.SetLastWriteTimeUtc(source, new DateTime(2026, 9, 18, 17, 38, 0, DateTimeKind.Utc));
        var box = area.Sandbox(area.At("root"));
        string copy = area.At("root", "tree", "a.dll");

        Assert.True(box.CopyFileIfChanged(source, copy));
        Assert.Equal("12345", File.ReadAllText(copy));
        Assert.Equal(File.GetLastWriteTimeUtc(source), File.GetLastWriteTimeUtc(copy));
        Assert.Equal(1, box.FilesCopied);

        // Edited in place with the same length and time: only a copy that does not write would leave it as it is.
        DateTime stamp = File.GetLastWriteTimeUtc(copy);
        File.WriteAllText(copy, "abcde");
        File.SetLastWriteTimeUtc(copy, stamp);

        Assert.False(box.CopyFileIfChanged(source, copy));
        Assert.Equal("abcde", File.ReadAllText(copy));
        Assert.Equal(1, box.FilesCopied);
        Assert.Equal(1, box.FilesUnchanged);
    }

    [SkippableTheory]
    [InlineData("length")]
    [InlineData("older")]
    [InlineData("newer")]
    [InlineData("missing")]
    public void AFileThatDiffersFromItsCopyIsCopiedAgain(string difference)
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string source = area.Write(area.At("src", "a.dll"), "12345");
        var stamp = new DateTime(2026, 9, 18, 17, 38, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(source, stamp);
        var box = area.Sandbox(area.At("root"));
        string copy = area.At("root", "tree", "a.dll");
        Assert.True(box.CopyFileIfChanged(source, copy));

        switch (difference)
        {
            case "length":
                File.WriteAllText(copy, "123456");
                File.SetLastWriteTimeUtc(copy, stamp);
                break;
            case "older":
                File.SetLastWriteTimeUtc(copy, stamp.AddSeconds(-1));
                break;
            case "newer":
                File.SetLastWriteTimeUtc(copy, stamp.AddSeconds(1));
                break;
            default:
                File.Delete(copy);
                break;
        }

        Assert.True(box.CopyFileIfChanged(source, copy));
        Assert.Equal("12345", File.ReadAllText(copy));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(copy));
        Assert.Equal(2, box.FilesCopied);
        Assert.Equal(0, box.FilesUnchanged);
    }

    [SkippableFact]
    public void AnIncrementalCopyStaysInsideTheRootAndWaitsForALockedCopy()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string source = area.Write(area.At("src", "a.dll"), "payload");
        var box = area.Sandbox(area.At("root"), attempts: 40, delay: 25);

        var ex = Assert.Throws<HelperException>(() => box.CopyFileIfChanged(source, area.At("outside", "a.dll")));
        Assert.Contains("outside", ex.Message);
        Assert.False(File.Exists(area.At("outside", "a.dll")));

        // A copy that exists but is held open by someone (a scanner) is retried like any other write.
        string copy = area.At("root", "a.dll");
        area.Write(copy, "older and longer content");
        using var gate = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            using var stream = new FileStream(copy, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            gate.Set();
            Thread.Sleep(300);
        });
        holder.Start();
        gate.Wait();

        Assert.True(box.CopyFileIfChanged(source, copy));
        holder.Join();

        Assert.Equal("payload", File.ReadAllText(copy));
        Assert.Contains(area.Log, line => line.StartsWith("retry "));
    }

    [SkippableFact]
    public void ACopyOntoALinkReplacesTheLinkAndLeavesItsTargetAlone()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string source = area.Write(area.At("src", "a.dll"), "payload");
        string target = area.Write(area.At("outside", "target.dll"), "the target stays");
        Directory.CreateDirectory(area.At("root"));
        try
        {
            File.CreateSymbolicLink(area.At("root", "a.dll"), target);
            File.CreateSymbolicLink(area.At("root", "b.dll"), target);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Skip.If(true, "creating a symbolic link needs a privilege this account does not have");
        }

        var box = area.Sandbox(area.At("root"));
        Assert.True(box.CopyFileIfChanged(source, area.At("root", "a.dll")));
        box.CopyFile(source, area.At("root", "b.dll"));

        Assert.Equal("the target stays", File.ReadAllText(target));
        foreach (string name in new[] { "a.dll", "b.dll" })
        {
            Assert.False(HelperFileSystem.IsReparsePoint(area.At("root", name)), name);
            Assert.Equal("payload", File.ReadAllText(area.At("root", name)));
        }
    }

    private static void CreateFileLinkOrSkip(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Skip.If(true, "creating a symbolic link needs a privilege this account does not have");
        }
    }

    [SkippableFact]
    public void AWriteReplacesTheWholeContentAsUtf8WithoutAMark()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var box = area.Sandbox(area.At("root"));
        string path = area.At("root", "deep", "server.pid");

        box.WriteAllText(path, "a long first content, \u00e9 and more");
        box.WriteAllText(path, "4242");
        Assert.Equal(new byte[] { (byte)'4', (byte)'2', (byte)'4', (byte)'2' }, File.ReadAllBytes(path));

        box.WriteAllText(path, "\u00e9");
        Assert.Equal(new byte[] { 0xC3, 0xA9 }, File.ReadAllBytes(path));

        box.WriteAllText(path, "");
        Assert.Empty(File.ReadAllBytes(path));
    }

    [SkippableFact]
    public void AWriteOntoALinkReplacesTheLinkAndNeverTouchesItsTarget()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string target = area.Write(area.At("outside", "target.txt"), "the target stays");
        Directory.CreateDirectory(area.At("root"));
        CreateFileLinkOrSkip(area.At("root", "server.pid"), target);
        CreateFileLinkOrSkip(area.At("root", "dangling.txt"), area.At("outside", "missing.txt")); // nothing at the end of this one
        var box = area.Sandbox(area.At("root"));

        box.WriteAllText(area.At("root", "server.pid"), "4242");
        box.WriteAllText(area.At("root", "dangling.txt"), "written");

        Assert.Equal("the target stays", File.ReadAllText(target));
        Assert.False(File.Exists(area.At("outside", "missing.txt")), "a dangling link must not create its target");
        Assert.False(HelperFileSystem.IsReparsePoint(area.At("root", "server.pid")));
        Assert.False(HelperFileSystem.IsReparsePoint(area.At("root", "dangling.txt")));
        Assert.Equal("4242", File.ReadAllText(area.At("root", "server.pid")));
        Assert.Equal("written", File.ReadAllText(area.At("root", "dangling.txt")));
        Assert.Equal(2, area.Log.Count(line => line.Contains("is a link: removing the link itself")));
    }

    [SkippableFact]
    public void AWriteOntoAJunctionReplacesTheJunctionAndNeverTouchesItsTarget()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        area.Write(area.At("outside", "data", "keep.txt"), "keep");
        var box = area.Sandbox(area.At("root"));
        box.CreateJunction(area.At("root", "server.pid"), area.At("outside", "data"));

        box.WriteAllText(area.At("root", "server.pid"), "4242");

        Assert.False(HelperFileSystem.IsReparsePoint(area.At("root", "server.pid")));
        Assert.Equal("4242", File.ReadAllText(area.At("root", "server.pid")));
        Assert.Equal("keep", File.ReadAllText(area.At("outside", "data", "keep.txt")));
    }

    [SkippableFact]
    public void AFileIsNotOpenedForWritingThroughALinkEvenWhenNobodyRemovedItFirst()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string target = area.Write(area.At("outside", "target.txt"), "the target stays");
        area.Write(area.At("outside", "data", "keep.txt"), "keep");
        Directory.CreateDirectory(area.At("root"));
        CreateFileLinkOrSkip(area.At("root", "link.txt"), target);
        area.Sandbox(area.At("root")).CreateJunction(area.At("root", "junction"), area.At("outside", "data"));

        // The caller's own check (RemoveLinkAt) is not the only line of defence: the open itself refuses the link, so
        // one that appears between the check and the open is not followed either.
        var ex = Assert.Throws<HelperException>(() => HelperNative.OpenFileForWriting(area.At("root", "link.txt"), true, FileShare.None));
        Assert.Contains("symbolic link or junction", ex.Message);

        // A junction and a folder never reach the attribute check: without backup semantics CreateFile cannot open a folder
        // for writing, so both fail with access denied (UnauthorizedAccessException, not a HelperException). Nothing is written.
        Assert.Throws<UnauthorizedAccessException>(() => HelperNative.OpenFileForWriting(area.At("root", "junction"), true, FileShare.None));
        Assert.Throws<UnauthorizedAccessException>(() => HelperNative.OpenFileForWriting(area.At("outside", "data"), true, FileShare.None)); // a plain folder

        Assert.Equal("the target stays", File.ReadAllText(target));
        Assert.Equal("keep", File.ReadAllText(area.At("outside", "data", "keep.txt")));
        Assert.Single(Directory.GetFileSystemEntries(area.At("outside", "data")));
        Assert.True(HelperFileSystem.IsReparsePoint(area.At("root", "link.txt")), "the refused open leaves the link as it is");
    }

    [SkippableFact]
    public void ALinkThatAppearsAfterTheCheckIsRefusedByAWriteAndByACopy()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string target = area.Write(area.At("outside", "target.txt"), "the target stays");
        string source = area.Write(area.At("src", "a.dll"), "payload");
        Directory.CreateDirectory(area.At("root"));
        CreateFileLinkOrSkip(area.At("probe.lnk"), target); // proves that links can be made here, so the hook below may use them directly
        File.Delete(area.At("probe.lnk"));
        var box = area.Sandbox(area.At("root"));

        // The helper looked for a link at the destination and found none; the hook plants one in the instant before the open.
        box.BeforeWriteOpen = destination => File.CreateSymbolicLink(destination, target);

        var written = Assert.Throws<HelperException>(() => box.WriteAllText(area.At("root", "server.pid"), "4242"));
        var copied = Assert.Throws<HelperException>(() => box.CopyFile(source, area.At("root", "a.dll")));

        Assert.Contains("symbolic link or junction", written.Message);
        Assert.Contains("symbolic link or junction", copied.Message);
        Assert.Equal("the target stays", File.ReadAllText(target));
    }

    [SkippableFact]
    public void OpeningAPlainFileForWritingTruncatesOrAppendsAsAsked()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string path = area.At("root", "a.log");
        Directory.CreateDirectory(area.At("root"));

        using (FileStream created = HelperNative.OpenFileForWriting(path, false, FileShare.ReadWrite | FileShare.Delete)) created.Write(new byte[] { (byte)'a' }, 0, 1);
        using (FileStream appended = HelperNative.OpenFileForWriting(path, false, FileShare.ReadWrite | FileShare.Delete)) appended.Write(new byte[] { (byte)'b' }, 0, 1);
        Assert.Equal("ab", File.ReadAllText(path));

        using (FileStream truncated = HelperNative.OpenFileForWriting(path, true, FileShare.Read)) truncated.Write(new byte[] { (byte)'c' }, 0, 1);
        Assert.Equal("c", File.ReadAllText(path));
    }

    [SkippableFact]
    public void AWriteWaitsForALockedFileToBeReleased()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string file = area.Write(area.At("root", "server.pid"), "old");
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

        box.WriteAllText(file, "4242");
        holder.Join();

        Assert.Equal("4242", File.ReadAllText(file));
        Assert.Contains(area.Log, line => line.StartsWith("retry "));
    }

    [SkippableFact]
    public void AWriteToAFileThatStaysLockedOrReadOnlyFailsInsteadOfHanging()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string locked = area.Write(area.At("root", "locked.pid"), "old");
        string readOnly = area.Write(area.At("root", "readonly.pid"), "old");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        var box = area.Sandbox(area.At("root"), attempts: 3, delay: 5);

        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => box.WriteAllText(locked, "4242"));
        }

        Assert.Throws<UnauthorizedAccessException>(() => box.WriteAllText(readOnly, "4242"));
        File.SetAttributes(readOnly, FileAttributes.Normal);
        Assert.Equal("old", File.ReadAllText(locked));
        Assert.Equal("old", File.ReadAllText(readOnly));
    }

    [SkippableFact]
    public void ALinkUnderTheAcknowledgementsTemporaryNameIsNotWrittenThrough()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string request = area.Write(area.At("session", "request.json"), "{}");
        string target = area.Write(area.At("outside", "target.txt"), "the target stays");
        CreateFileLinkOrSkip(request + ".accepted.4242.tmp", target);

        var ex = Assert.Throws<HelperException>(() => HelperAcknowledgementFile.Write(request, 4242, AcceptedAt, null!));

        Assert.Contains("symbolic link or junction", ex.Message);
        Assert.Equal("the target stays", File.ReadAllText(target));
        Assert.False(File.Exists(request + ".accepted"), "no acknowledgement came out of a refused write");
    }

    // ------------------------------------------------------------------ helper.log

    [SkippableFact]
    public void TheLogAppendsOneLineAfterTheOtherWithoutAMark()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string path = area.At("session", "helper.log"); // the folder does not exist yet

        var log = new HelperLog(path);
        log.Info("first");
        log.Warn("second");
        log.Error("third");
        new HelperLog(path).Info("fourth"); // another run appends

        byte[] bytes = File.ReadAllBytes(path);
        Assert.Equal((byte)'2', bytes[0]); // the year: no byte order mark
        string[] lines = File.ReadAllLines(path);
        Assert.Equal(4, lines.Length);
        Assert.EndsWith(" INFO first", lines[0]);
        Assert.EndsWith(" WARN second", lines[1]);
        Assert.EndsWith(" ERROR third", lines[2]);
        Assert.EndsWith(" INFO fourth", lines[3]);
        Assert.Equal(path, log.FilePath);
    }

    [SkippableFact]
    public void ALogThatIsALinkIsReplacedByAPlainFileAndNeverWrittenThrough()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string target = area.Write(area.At("outside", "target.txt"), "the target stays");
        string path = area.At("session", "helper.log");
        Directory.CreateDirectory(area.At("session"));
        CreateFileLinkOrSkip(path, target);

        var log = new HelperLog(path);
        log.Info("hello");
        log.Info("again");

        Assert.Equal("the target stays", File.ReadAllText(target));
        Assert.False(HelperFileSystem.IsReparsePoint(path));
        string[] lines = File.ReadAllLines(path);
        Assert.Equal(3, lines.Length);
        Assert.Contains(" WARN helper.log was a symbolic link or junction; the link itself was removed", lines[0]);
        Assert.EndsWith(" INFO hello", lines[1]);
        Assert.EndsWith(" INFO again", lines[2]);
    }

    [SkippableFact]
    public void ALogThatIsAJunctionIsReplacedByAPlainFileAndItsTargetIsNeverWritten()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        area.Write(area.At("outside", "data", "keep.txt"), "keep");
        string path = area.At("session", "helper.log");
        area.Sandbox(area.At("session")).CreateJunction(path, area.At("outside", "data"));

        new HelperLog(path).Error("hello");

        Assert.Equal(new[] { "keep.txt" }, Directory.GetFiles(area.At("outside", "data")).Select(file => Path.GetFileName(file)));
        Assert.Equal("keep", File.ReadAllText(area.At("outside", "data", "keep.txt")));
        Assert.False(HelperFileSystem.IsReparsePoint(path));
        string[] lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Contains("helper.log was a symbolic link or junction", lines[0]);
        Assert.EndsWith(" ERROR hello", lines[1]);
    }

    [SkippableFact]
    public void ALinkThatAppearsAfterTheCheckIsNotAppendedThrough()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string target = area.Write(area.At("outside", "target.txt"), "the target stays");
        string path = area.At("session", "helper.log");
        Directory.CreateDirectory(area.At("session"));
        CreateFileLinkOrSkip(area.At("probe.lnk"), target);
        File.Delete(area.At("probe.lnk"));
        bool planted = false;
        var log = new HelperLog(path)
        {
            // The first write finds no link and the hook plants one in the instant before the open; later writes are left alone.
            BeforeOpen = fullPath =>
            {
                if (planted) return;
                planted = true;
                File.CreateSymbolicLink(fullPath, target);
            },
        };

        log.Info("refused");
        Assert.Equal("the target stays", File.ReadAllText(target));
        Assert.True(HelperFileSystem.IsReparsePoint(path));

        log.Info("written"); // this one finds the link, removes it and goes on in a plain file
        Assert.Equal("the target stays", File.ReadAllText(target));
        Assert.False(HelperFileSystem.IsReparsePoint(path));
        string[] lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Contains("helper.log was a symbolic link or junction", lines[0]);
        Assert.EndsWith(" INFO written", lines[1]);
    }

    [SkippableFact]
    public void ALogLinkInAFolderThatIsAJunctionIsRefusedNotRedirected()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string target = area.Write(area.At("outside", "target.txt"), "the target stays");
        Directory.CreateDirectory(area.At("session"));
        // The log's own name is a link to a file, and the folder that holds it is a link as well: the sandbox that would
        // remove the link refuses to work inside a link, so nothing is written and nothing throws.
        area.Sandbox(area.At("holder")).CreateJunction(area.At("holder", "linked"), area.At("session"));
        CreateFileLinkOrSkip(area.At("session", "helper.log"), target);

        new HelperLog(area.At("holder", "linked", "helper.log")).Info("hello");

        Assert.Equal("the target stays", File.ReadAllText(target));
        Assert.True(HelperFileSystem.IsReparsePoint(area.At("session", "helper.log")));
    }

    [SkippableFact]
    public void APlainLogInAFolderThatIsAJunctionIsWrittenThrough()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        Directory.CreateDirectory(area.At("session"));
        area.Sandbox(area.At("holder")).CreateJunction(area.At("holder", "linked"), area.At("session"));

        // Only a link under the log's own name is refused or replaced; the folder that holds a plain log may be a link.
        new HelperLog(area.At("holder", "linked", "helper.log")).Info("hello");

        string[] lines = File.ReadAllLines(area.At("session", "helper.log"));
        Assert.Single(lines);
        Assert.EndsWith(" INFO hello", lines[0]);
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
        /// <summary>The top-level files the arena server needs beside its exe (a framework-dependent server: flat).</summary>
        public static readonly string[] ServerBinaries =
        {
            "0Harmony.dll", "SPT.Server.deps.json", "SPT.Server.dll", "SPT.Server.exe", "SPT.Server.runtimeconfig.json", "SPTarkov.Server.Core.dll",
        };

        /// <summary>Top-level files the tree must not get: the launcher, everything Linux, shared libraries (and sptLogger.json, which is generated).</summary>
        public static readonly string[] NotServerBinaries =
        {
            "SPT.Launcher.exe", "SPT.Launcher.Linux", "SPT.Server.Linux", "SPT.Server.Linux.dll", "SPT.Server.Linux.deps.json", "libargon2_sharp_core.so",
        };

        public FakeRuntime(TempArea area)
        {
            Dir = area.At("SPT_Runtime");
            area.Write(Path.Combine(Dir, "sptLogger.json"), SptLoggerJson);
            area.Write(Path.Combine(Dir, "SPT.Server.exe"), "MZ");
            area.Write(Path.Combine(Dir, "SPT.Server.dll"), "server-dll");
            area.Write(Path.Combine(Dir, "SPT.Server.deps.json"), "{\"deps\":1}");
            area.Write(Path.Combine(Dir, "SPT.Server.runtimeconfig.json"), "{\"runtimeOptions\":{}}");
            area.Write(Path.Combine(Dir, "SPTarkov.Server.Core.dll"), "core-dll");
            area.Write(Path.Combine(Dir, "0Harmony.dll"), "harmony");
            foreach (string name in NotServerBinaries) area.Write(Path.Combine(Dir, name), "not for the arena tree: " + name);
            area.Write(Path.Combine(Dir, "SPT_Data", "checks.dat"), "checks");
            area.Write(Path.Combine(Dir, "SPT_Data", "configs", "http.json"), SptHttpJson);
            area.Write(Path.Combine(Dir, "SPT_Data", "configs", "core.json"), "{\"core\":true,\"fixes\":{\"removeModItemsFromProfile\":true,\"removeInvalidTradersFromProfile\":true}}");
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

        /// <summary>An enter request that lists exactly these server mods (GUID, folder below user\mods).</summary>
        public HelperRequest RequestFor(params (string Guid, string FolderName)[] mods)
        {
            var fields = RequestFields("enter");
            fields["sptRuntimeDir"] = Dir;
            fields["gameDir"] = Path.GetDirectoryName(Dir);
            fields["gameExe"] = Path.Combine(Path.GetDirectoryName(Dir)!, "EscapeFromTarkov.exe");
            fields["serverMods"] = mods.Select(mod => (object?)new Dictionary<string, object?>
            {
                ["guid"] = mod.Guid,
                ["folder"] = Path.Combine(Dir, "user", "mods", mod.FolderName),
            }).ToList();
            return ParseRequest(fields, "enter");
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

        // The server binaries sit beside the tree's own exe, so AppContext.BaseDirectory is the tree.
        Assert.Equal(Path.Combine(tree, "SPT.Server.exe"), result.ServerExe);
        foreach (string name in FakeRuntime.ServerBinaries)
        {
            Assert.Equal(File.ReadAllText(Path.Combine(runtime.Dir, name)), File.ReadAllText(Path.Combine(tree, name)));
            Assert.Equal(File.GetLastWriteTimeUtc(Path.Combine(runtime.Dir, name)), File.GetLastWriteTimeUtc(Path.Combine(tree, name)));
        }

        foreach (string name in FakeRuntime.NotServerBinaries) Assert.False(File.Exists(Path.Combine(tree, name)), name);
        Assert.Equal(FakeRuntime.ServerBinaries.Length, result.ServerFilesCopied);
        Assert.Equal(0, result.ServerFilesUnchanged);
        Assert.Equal(FakeRuntime.ServerBinaries.Concat(new[] { "arena-session.json", "sptLogger.json" }).OrderBy(n => n, StringComparer.OrdinalIgnoreCase),
            Directory.GetFiles(tree).Select(path => Path.GetFileName(path)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(result.UnsharedFolders);

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
        Assert.Equal("{\"core\":true,\"fixes\":{\"removeModItemsFromProfile\":true,\"removeInvalidTradersFromProfile\":true}}", File.ReadAllText(Path.Combine(data, "configs", "core.json"))); // both flags on: copied unchanged

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
        Assert.Equal(Path.Combine(tree, "SPT.Server.exe"), marker.ServerExe);
        Assert.True(marker.RunsFromTree);
        Assert.Equal("3755e0db97983440", marker.PolicyHash);
        Assert.Equal("0.1.3", marker.HelperVersion);
        Assert.Equal("2026-10-21T12:00:00Z", marker.CreatedUtc);
    }

    [SkippableFact]
    public void TheTreeLinksModBundlesAndCarriesTheBundleHashCacheOver()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var runtime = new FakeRuntime(area);
        area.Write(Path.Combine(runtime.Dir, "user", "mods", "Solarint-SAIN-ServerMod", "bundles", "a.bundle"), "bundle-a");
        area.Write(Path.Combine(runtime.Dir, "user", "cache", "bundleHashCache.json"), "{\"user/mods/x/bundles/a.bundle\":1}");
        area.Write(Path.Combine(runtime.Dir, "user", "cache", "other.json"), "{}");
        var before = runtime.Snapshot();
        string root = area.At("session");

        HelperServerTree.Build(runtime.Request(), 6970, root, "0.1.3", DateTime.UtcNow, area.Log.Add);

        string user = Path.Combine(HelperServerTree.GetTreePath(root), "user");
        string bundles = Path.Combine(user, "mods", "Solarint-SAIN-ServerMod", "bundles");
        Assert.True(HelperFileSystem.IsReparsePoint(bundles), "the bundle folder is a link, not a copy");
        Assert.Equal("bundle-a", File.ReadAllText(Path.Combine(bundles, "a.bundle")));
        Assert.False(HelperFileSystem.IsReparsePoint(Path.Combine(user, "mods", "Solarint-SAIN-ServerMod")));
        Assert.Equal("{\"user/mods/x/bundles/a.bundle\":1}", File.ReadAllText(Path.Combine(user, "cache", "bundleHashCache.json")));
        Assert.False(File.Exists(Path.Combine(user, "cache", "other.json")), "only the bundle hash cache is carried over");

        // A second build removes the link as a link: the installation's bundle stays.
        HelperServerTree.Build(runtime.Request(), 6970, root, "0.1.3", DateTime.UtcNow, area.Log.Add);
        Assert.Equal(before, runtime.Snapshot());
    }

    [SkippableFact]
    public void ASecondBuildCopiesOnlyWhatChangedAndDropsWhatTheRuntimeNoLongerHas()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var runtime = new FakeRuntime(area);
        string root = area.At("session");
        string tree = HelperServerTree.GetTreePath(root);
        HelperServerTree.Build(runtime.Request(), 6970, root, "0.1.3", DateTime.UtcNow, area.Log.Add);

        // The runtime moves on: one binary gets another size, one only another time (same size), one is new
        // and one is gone. A third is left alone in the runtime but edited in the tree with the same length
        // and time: an incremental copy must not notice it, which proves that it did not write the file.
        string longer = Path.Combine(runtime.Dir, "SPT.Server.dll");
        File.WriteAllText(longer, "server-dll, a newer and longer build");
        string touched = Path.Combine(runtime.Dir, "0Harmony.dll");
        File.SetLastWriteTimeUtc(touched, File.GetLastWriteTimeUtc(touched).AddMinutes(-7));
        File.WriteAllText(Path.Combine(runtime.Dir, "Brand.New.dll"), "new");
        File.Delete(Path.Combine(runtime.Dir, "SPTarkov.Server.Core.dll"));
        string untouchedInRuntime = Path.Combine(runtime.Dir, "SPT.Server.deps.json");
        string untouchedInTree = Path.Combine(tree, "SPT.Server.deps.json");
        DateTime stamp = File.GetLastWriteTimeUtc(untouchedInTree);
        File.WriteAllText(untouchedInTree, "{\"deps\":2}");
        File.SetLastWriteTimeUtc(untouchedInTree, stamp);
        Assert.Equal(new FileInfo(untouchedInRuntime).Length, new FileInfo(untouchedInTree).Length);
        File.WriteAllText(Path.Combine(tree, "leftover.txt"), "from the first session");

        var second = HelperServerTree.Build(runtime.Request(), 6971, root, "0.1.3", DateTime.UtcNow, area.Log.Add);

        Assert.Equal(3, second.ServerFilesCopied); // SPT.Server.dll, 0Harmony.dll, Brand.New.dll
        Assert.Equal(3, second.ServerFilesUnchanged); // the exe, deps.json and runtimeconfig.json
        Assert.Equal("server-dll, a newer and longer build", File.ReadAllText(Path.Combine(tree, "SPT.Server.dll")));
        Assert.Equal(File.GetLastWriteTimeUtc(touched), File.GetLastWriteTimeUtc(Path.Combine(tree, "0Harmony.dll")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(tree, "Brand.New.dll")));
        Assert.False(File.Exists(Path.Combine(tree, "SPTarkov.Server.Core.dll")), "a binary the runtime no longer has");
        Assert.False(File.Exists(Path.Combine(tree, "leftover.txt")));
        Assert.Equal("{\"deps\":2}", File.ReadAllText(untouchedInTree));
        Assert.Equal(6971, HelperMarker.TryParse(File.ReadAllText(HelperServerTree.GetMarkerPath(root)), out var marker, out _) ? marker.Port : 0);
        Assert.Contains("\"port\": 6971", File.ReadAllText(Path.Combine(tree, "SPT_Data", "configs", "http.json")));
    }

    [SkippableFact]
    public void ABuildClearsEverythingThatIsNotAServerBinaryEvenWhereABinaryBelongs()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var runtime = new FakeRuntime(area);
        string root = area.At("session");
        string tree = HelperServerTree.GetTreePath(root);
        area.Write(area.At("outside", "keep.txt"), "keep");

        // A folder where a binary goes, a stray folder, a junction to something valuable, and a marker.
        area.Write(Path.Combine(tree, "SPT.Server.dll", "inside.txt"));
        area.Write(Path.Combine(tree, "stray", "deep", "x.txt"));
        area.Write(Path.Combine(tree, "arena-session.json"), "{}");
        area.Write(Path.Combine(tree, "user", "profiles", "old.json"));
        new HelperSandbox(root, _ => { }).CreateJunction(Path.Combine(tree, "linked"), area.At("outside"));

        var result = HelperServerTree.Build(runtime.Request(), 6970, root, "0.1.3", DateTime.UtcNow, area.Log.Add);

        Assert.True(File.Exists(Path.Combine(tree, "SPT.Server.dll")), "a plain file now");
        Assert.False(Directory.Exists(Path.Combine(tree, "stray")));
        Assert.False(HelperFileSystem.PathExists(Path.Combine(tree, "linked")));
        Assert.False(File.Exists(Path.Combine(tree, "user", "profiles", "old.json")));
        Assert.Equal("keep", File.ReadAllText(area.At("outside", "keep.txt")));
        Assert.Equal(FakeRuntime.ServerBinaries.Length, result.ServerFilesCopied);
        Assert.True(HelperMarker.TryParse(File.ReadAllText(HelperServerTree.GetMarkerPath(root)), out var marker, out var error), error);
        Assert.Equal(6970, marker.Port);
    }

    [SkippableFact]
    public void ALinkInPlaceOfABinaryIsRemovedWithoutWritingItsTarget()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var runtime = new FakeRuntime(area);
        string root = area.At("session");
        string tree = HelperServerTree.GetTreePath(root);
        area.Write(area.At("outside", "target.dll"), "the target stays");
        Directory.CreateDirectory(tree);
        try
        {
            File.CreateSymbolicLink(Path.Combine(tree, "SPT.Server.dll"), area.At("outside", "target.dll"));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Skip.If(true, "creating a symbolic link needs a privilege this account does not have");
        }

        HelperServerTree.Build(runtime.Request(), 6970, root, "0.1.3", DateTime.UtcNow, area.Log.Add);

        Assert.Equal("the target stays", File.ReadAllText(area.At("outside", "target.dll")));
        Assert.Equal("server-dll", File.ReadAllText(Path.Combine(tree, "SPT.Server.dll")));
        Assert.False(HelperFileSystem.IsReparsePoint(Path.Combine(tree, "SPT.Server.dll")));
    }

    [SkippableFact]
    public void ATreeThatCannotBeCleanedHasLostItsMarkerBeforeAnythingElse()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var runtime = new FakeRuntime(area);
        string root = area.At("session");
        string tree = HelperServerTree.GetTreePath(root);
        HelperServerTree.Build(runtime.Request(), 6970, root, "0.1.3", DateTime.UtcNow, area.Log.Add);
        Assert.True(File.Exists(HelperServerTree.GetMarkerPath(root)));

        // Something keeps a file of the old tree open: the cleaning fails, but a half-cleaned tree must not look finished.
        // The folder sorts before the marker's name, so a cleaning that only deleted in listing order would stop first.
        string held = area.Write(Path.Combine(tree, "+held", "open.txt"));
        using (new FileStream(held, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Exception? failure = Record.Exception(() => HelperServerTree.Build(runtime.Request(), 6970, root, "0.1.3", DateTime.UtcNow, area.Log.Add));
            Assert.True(failure is IOException || failure is UnauthorizedAccessException, "the locked file stops the build: " + failure);
        }

        Assert.False(File.Exists(HelperServerTree.GetMarkerPath(root)));
    }

    [SkippableFact]
    public void ARuntimeWithoutItsServerExeCannotBuildATree()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var runtime = new FakeRuntime(area);
        File.Delete(Path.Combine(runtime.Dir, "SPT.Server.exe"));
        string root = area.At("session");

        var ex = Assert.Throws<HelperException>(() => HelperServerTree.Build(runtime.Request(), 6970, root, "0.1.3", DateTime.UtcNow, area.Log.Add));

        Assert.Contains("SPT.Server.exe", ex.Message);
        Assert.False(File.Exists(HelperServerTree.GetMarkerPath(root)));
    }

    [SkippableFact]
    public void RuntimeFoldersTheTreeDoesNotContainAreReported()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var runtime = new FakeRuntime(area);
        Directory.CreateDirectory(Path.Combine(runtime.Dir, "runtimes"));
        Directory.CreateDirectory(Path.Combine(runtime.Dir, "Plugins"));

        var result = HelperServerTree.Build(runtime.Request(), 6970, area.At("session"), "0.1.3", DateTime.UtcNow, area.Log.Add);

        Assert.Equal(new[] { "Plugins", "runtimes" }, result.UnsharedFolders.OrderBy(n => n, StringComparer.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(result.TreePath, "runtimes")));
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

    // ------------------------------------------------------------------ the server tree: prepatch definitions of the listed mods

    private static readonly string[] ArenaAndSainGuids = { "com.maschine.ArenaMode", "me.sol.sain" };

    [SkippableFact]
    public void ThePrepatchFoldersOfTheListedModsAreCopiedAndNoOthers()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var runtime = new FakeRuntime(area);
        area.Write(Path.Combine(runtime.Dir, "user", "patchers", "me.sol.sain", "EnumExtensions.json"), "{\"sain\":1}");
        area.Write(Path.Combine(runtime.Dir, "user", "patchers", "com.maschine.ArenaMode", "enums.json"), "{\"arena\":1}");

        // A mod the policy left out: SPT's prepatch pass applies EVERY folder it finds in user\patchers, so this
        // one must not come along even though it sits next to the others.
        area.Write(Path.Combine(runtime.Dir, "user", "patchers", "com.foreign.unlisted", "EnumExtensions.json"), "{\"foreign\":1}");
        var before = runtime.Snapshot();
        string root = area.At("session");

        var result = HelperServerTree.Build(runtime.Request(), 6970, root, "0.1.3", DateTime.UtcNow, area.Log.Add);

        string patchers = Path.Combine(HelperServerTree.GetTreePath(root), "user", "patchers");
        Assert.Equal(ArenaAndSainGuids, result.Patchers);
        Assert.Equal(ArenaAndSainGuids.OrderBy(n => n, StringComparer.Ordinal),
            Directory.GetDirectories(patchers).Select(path => Path.GetFileName(path)).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal("{\"sain\":1}", File.ReadAllText(Path.Combine(patchers, "me.sol.sain", "EnumExtensions.json")));
        Assert.Equal("{\"arena\":1}", File.ReadAllText(Path.Combine(patchers, "com.maschine.ArenaMode", "enums.json")));
        Assert.False(HelperFileSystem.IsReparsePoint(Path.Combine(patchers, "me.sol.sain")), "a real copy, not a link into the installation");
        Assert.False(Directory.Exists(Path.Combine(patchers, "com.foreign.unlisted")));
        Assert.Equal(before, runtime.Snapshot());
    }

    [SkippableFact]
    public void WithoutPrepatchFoldersTheTreeHasNoPatchersFolderAtAll()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var runtime = new FakeRuntime(area);
        area.Write(Path.Combine(runtime.Dir, "user", "patchers", "com.foreign.unlisted", "EnumExtensions.json"), "{\"foreign\":1}");

        var result = HelperServerTree.Build(runtime.Request(), 6970, area.At("session"), "0.1.3", DateTime.UtcNow, area.Log.Add);

        Assert.Empty(result.Patchers);
        Assert.False(Directory.Exists(Path.Combine(HelperServerTree.GetTreePath(area.At("session")), "user", "patchers")));
    }

    [SkippableFact]
    public void AGuidThatIsNotAPlainFolderNameNeverLooksAnythingUp()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var runtime = new FakeRuntime(area);
        area.Write(Path.Combine(runtime.Dir, "user", "patchers", "com.maschine.ArenaMode", "enums.json"), "{\"arena\":1}");

        // <runtime>\user\patchers\..\profiles is the profiles folder: a lookup by the raw GUID would copy it.
        var request = runtime.RequestFor(("com.maschine.ArenaMode", "ArenaMode"), (@"..\profiles", "Solarint-SAIN-ServerMod"));
        var result = HelperServerTree.Build(request, 6970, area.At("session"), "0.1.3", DateTime.UtcNow, area.Log.Add);

        Assert.Equal(new[] { "com.maschine.ArenaMode" }, result.Patchers);
        Assert.Contains(area.Log, line => line.Contains("is not a plain folder name", StringComparison.Ordinal) && line.Contains(@"..\profiles", StringComparison.Ordinal));
    }

    [SkippableFact]
    public void TwoModsWithOneGuidShareOnePrepatchFolder()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var runtime = new FakeRuntime(area);
        area.Write(Path.Combine(runtime.Dir, "user", "patchers", "me.sol.sain", "EnumExtensions.json"), "{\"sain\":1}");

        var request = runtime.RequestFor(("me.sol.sain", "Solarint-SAIN-ServerMod"), ("ME.SOL.SAIN", "ArenaMode"));
        var result = HelperServerTree.Build(request, 6970, area.At("session"), "0.1.3", DateTime.UtcNow, area.Log.Add);

        Assert.Equal(new[] { "me.sol.sain" }, result.Patchers);
    }

    [SkippableFact]
    public void ASecondBuildDropsPrepatchFoldersTheRuntimeNoLongerHas()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        var runtime = new FakeRuntime(area);
        string source = Path.Combine(runtime.Dir, "user", "patchers", "me.sol.sain");
        area.Write(Path.Combine(source, "EnumExtensions.json"), "{\"sain\":1}");
        string root = area.At("session");
        string patchers = Path.Combine(HelperServerTree.GetTreePath(root), "user", "patchers");

        HelperServerTree.Build(runtime.Request(), 6970, root, "0.1.3", DateTime.UtcNow, area.Log.Add);
        Assert.True(File.Exists(Path.Combine(patchers, "me.sol.sain", "EnumExtensions.json")));

        Directory.Delete(source, true);
        var second = HelperServerTree.Build(runtime.Request(), 6970, root, "0.1.3", DateTime.UtcNow, area.Log.Add);

        Assert.Empty(second.Patchers);
        Assert.False(Directory.Exists(patchers));
    }

    [Theory]
    [InlineData("me.sol.sain", "me.sol.sain")]
    [InlineData("com.maschine.ArenaMode", "com.maschine.ArenaMode")]
    [InlineData(@"..\profiles", null)]
    [InlineData("a/b", null)]
    [InlineData(@"C:\x", null)]
    [InlineData("..", null)]
    [InlineData("con", null)]
    [InlineData(" lead", null)]
    [InlineData("trail.", null)]
    [InlineData(null, null)]
    public void OnlyAPlainGuidNamesAPrepatchFolder(string? guid, string? expected)
    {
        Assert.Equal(expected, new HelperServerMod { Guid = guid!, Folder = @"C:\SPT\user\mods\x" }.PatcherFolderName);
    }

    // ------------------------------------------------------------------ acknowledgement of a request

    private static readonly DateTime AcceptedAt = new(2026, 10, 22, 12, 34, 56, 789, DateTimeKind.Utc);
    private const string AcceptedJson = "{\"schema\":1,\"helperPid\":4242,\"acceptedUtc\":\"2026-10-22T12:34:56.789Z\"}";

    private static string[] FileNames(string folder) =>
        Directory.GetFileSystemEntries(folder).Select(path => Path.GetFileName(path)).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    [Fact]
    public void TheAcknowledgementNamesAreDerivedFromTheRequestFile()
    {
        Assert.Equal(@"C:\s\request.json.accepted", HelperAcknowledgement.GetPath(@"C:\s\request.json"));
        Assert.Equal(@"C:\s\request.json.accepted.4242.tmp", HelperAcknowledgement.GetTempPath(@"C:\s\request.json", 4242));
        Assert.Equal(".accepted", HelperAcknowledgement.FileSuffix);
        Assert.Equal(AcceptedJson, HelperAcknowledgement.BuildContent(4242, AcceptedAt));
    }

    [Fact]
    public void TheAcknowledgementTimeIsAlwaysWrittenInUtc()
    {
        string local = HelperAcknowledgement.BuildContent(7, new DateTime(2026, 10, 22, 14, 34, 56, 789, DateTimeKind.Utc).ToLocalTime());

        Assert.Equal("{\"schema\":1,\"helperPid\":7,\"acceptedUtc\":\"2026-10-22T14:34:56.789Z\"}", local);
    }

    [Fact]
    public void TheAcknowledgementReadsBackWhatWasWritten()
    {
        Assert.True(HelperAcknowledgement.TryParse(AcceptedJson, out int pid, out DateTime accepted, out string? error), error);

        Assert.Equal(4242, pid);
        Assert.Equal(AcceptedAt, accepted);
        Assert.Equal(DateTimeKind.Utc, accepted.Kind);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("[]", "JSON object")]
    [InlineData("{\"schema\":2,\"helperPid\":1,\"acceptedUtc\":\"2026-10-22T12:34:56.789Z\"}", "schema")]
    [InlineData("{\"helperPid\":1,\"acceptedUtc\":\"2026-10-22T12:34:56.789Z\"}", "schema")]
    [InlineData("{\"schema\":1,\"helperPid\":0,\"acceptedUtc\":\"2026-10-22T12:34:56.789Z\"}", "helperPid")]
    [InlineData("{\"schema\":1,\"acceptedUtc\":\"2026-10-22T12:34:56.789Z\"}", "helperPid")]
    [InlineData("{\"schema\":1,\"helperPid\":1,\"acceptedUtc\":\"yesterday\"}", "acceptedUtc")]
    [InlineData("{\"schema\":1,\"helperPid\":1}", "acceptedUtc")]
    public void ContentThatIsNotAnAcknowledgementIsRefused(string json, string fragment)
    {
        Assert.False(HelperAcknowledgement.TryParse(json, out int pid, out _, out string? error));

        Assert.Equal(0, pid);
        Assert.Contains(fragment, error);
    }

    [Theory]
    [InlineData("request.json", "request.json.accepted.4242.tmp", true)]
    [InlineData("request.json", "REQUEST.JSON.ACCEPTED.1.TMP", true)]
    [InlineData("request.json", "request.json.accepted.tmp", false)]
    [InlineData("request.json", "request.json.accepted.4242x.tmp", false)]
    [InlineData("request.json", "request.json.accepted.4242.tmp.bak", false)]
    [InlineData("request.json", "request.json.accepted", false)]
    [InlineData("request.json", "request.json.tmp", false)]
    [InlineData("request.json", "other.json.accepted.4242.tmp", false)]
    [InlineData("request.json", "", false)]
    [InlineData("", "request.json.accepted.4242.tmp", false)]
    public void OnlyTheTemporaryFilesOfThisRequestAreRecognised(string request, string candidate, bool expected)
    {
        Assert.Equal(expected, HelperAcknowledgement.IsTempName(request, candidate));
    }

    [SkippableFact]
    public void TheAcknowledgementSitsBesideTheRequestFileWithTheDocumentedContent()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string request = area.Write(area.At("session", "request.json"), "{}");

        string path = HelperAcknowledgementFile.Write(request, 4242, AcceptedAt, null!);

        Assert.Equal(request + ".accepted", path);
        Assert.Equal(AcceptedJson, File.ReadAllText(path));
        Assert.Equal((byte)'{', File.ReadAllBytes(path)[0]);
        Assert.Equal(new[] { "request.json", "request.json.accepted" }, FileNames(area.At("session")));
    }

    [SkippableFact]
    public void TheAcknowledgementIsMovedIntoPlaceFromACompleteTemporaryFile()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string request = area.Write(area.At("session", "request.json"), "{}");
        string final = request + ".accepted";
        string? seen = null;
        string? content = null;
        bool finalThereEarly = true;

        HelperAcknowledgementFile.Write(request, 4242, AcceptedAt, temporary =>
        {
            seen = temporary;
            finalThereEarly = File.Exists(final);
            content = File.ReadAllText(temporary);
        });

        // Before the move: a finished temporary file, and no final name for the client to find yet.
        Assert.NotNull(seen);
        Assert.Equal(request + ".accepted.4242.tmp", seen);
        Assert.False(finalThereEarly);
        Assert.Equal(AcceptedJson, content);

        // After it: the same bytes under the final name, and the temporary name gone (moved, not copied).
        Assert.False(File.Exists(seen!));
        Assert.Equal(AcceptedJson, File.ReadAllText(final));
        Assert.Equal(new[] { "request.json", "request.json.accepted" }, FileNames(area.At("session")));
    }

    [SkippableFact]
    public void TheClientsOwnTemporaryRequestFileIsNeverTheAcknowledgementsTemporaryFile()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string request = area.Write(area.At("session", "request.json"), "{}");
        string? seen = null;

        HelperAcknowledgementFile.Write(request, 4242, AcceptedAt, temporary => seen = temporary);

        Assert.NotEqual(request + ".tmp", seen);
    }

    [SkippableFact]
    public void AnAcknowledgementReplacesAStaleOneInOneStep()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string request = area.Write(area.At("session", "request.json"), "{}");
        area.Write(request + ".accepted", "stale from an earlier request");

        HelperAcknowledgementFile.Write(request, 4242, AcceptedAt, null!);

        Assert.Equal(AcceptedJson, File.ReadAllText(request + ".accepted"));
        Assert.Equal(new[] { "request.json", "request.json.accepted" }, FileNames(area.At("session")));
    }

    [SkippableFact]
    public void AnAcknowledgementThatCannotBePublishedLeavesNoTemporaryFile()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string request = area.Write(area.At("session", "request.json"), "{}");
        Directory.CreateDirectory(request + ".accepted"); // a folder where the file belongs

        var ex = Assert.Throws<HelperException>(() => HelperAcknowledgementFile.Write(request, 4242, AcceptedAt, null!));

        Assert.Contains("acknowledgement", ex.Message);
        Assert.Equal(new[] { "request.json", "request.json.accepted" }, FileNames(area.At("session")));
        Assert.True(Directory.Exists(request + ".accepted"));
    }

    [SkippableFact]
    public void ARequestPathThatIsNotAbsoluteGetsNoAcknowledgement()
    {
        SkipUnlessWindows();

        Assert.Throws<HelperException>(() => HelperAcknowledgementFile.Write(@"session\request.json", 4242, AcceptedAt, null!));
        Assert.Throws<HelperException>(() => HelperAcknowledgementFile.Write(@"\\server\share\request.json", 4242, AcceptedAt, null!));
        Assert.False(HelperAcknowledgementFile.DiscardStale(@"session\request.json", out string? problem));
        Assert.Contains("absolute", problem);
    }

    [SkippableFact]
    public void AStaleAcknowledgementIsRemoved()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string request = area.Write(area.At("session", "request.json"), "{}");
        area.Write(request + ".accepted", AcceptedJson);

        Assert.True(HelperAcknowledgementFile.DiscardStale(request, out string? problem), problem);

        Assert.False(File.Exists(request + ".accepted"));
        Assert.Equal("{}", File.ReadAllText(request));
    }

    [SkippableFact]
    public void AReadOnlyStaleAcknowledgementIsRemovedToo()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string request = area.Write(area.At("session", "request.json"), "{}");
        area.Write(request + ".accepted", AcceptedJson);
        File.SetAttributes(request + ".accepted", FileAttributes.ReadOnly);

        Assert.True(HelperAcknowledgementFile.DiscardStale(request, out string? problem), problem);

        Assert.False(File.Exists(request + ".accepted"));
    }

    [SkippableFact]
    public void DiscardingNothingIsSuccessEvenWithoutARequestFolder()
    {
        SkipUnlessWindows();
        using var area = new TempArea();

        Assert.True(HelperAcknowledgementFile.DiscardStale(area.At("session", "request.json"), out string? problem), problem);
        Assert.True(HelperAcknowledgementFile.DiscardStale(area.Write(area.At("session2", "request.json"), "{}"), out problem), problem);
    }

    [SkippableFact]
    public void OnlyTheTemporaryFilesOfADeadHelperAreRemovedWithTheStaleAcknowledgement()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string request = area.Write(area.At("session", "request.json"), "{}");
        area.Write(request + ".accepted.111.tmp", "left by a helper that died");
        area.Write(request + ".accepted.222.tmp", "left by another one");
        area.Write(request + ".tmp", "the client's own temporary request file");
        area.Write(request + ".accepted.abc.tmp", "not a helper's temporary file");
        area.Write(area.At("session", "other.json.accepted.333.tmp"), "belongs to another request");

        Assert.True(HelperAcknowledgementFile.DiscardStale(request, out string? problem), problem);

        Assert.Equal(new[] { "other.json.accepted.333.tmp", "request.json", "request.json.accepted.abc.tmp", "request.json.tmp" }, FileNames(area.At("session")));
    }

    [SkippableFact]
    public void AFolderInThePlaceOfTheStaleAcknowledgementIsReportedNotThrown()
    {
        SkipUnlessWindows();
        using var area = new TempArea();
        string request = area.Write(area.At("session", "request.json"), "{}");
        Directory.CreateDirectory(request + ".accepted");

        Assert.False(HelperAcknowledgementFile.DiscardStale(request, out string? problem));

        Assert.Contains("is a folder", problem);
        Assert.True(Directory.Exists(request + ".accepted"));
    }

    [Fact]
    public void TheAcknowledgementIsPartOfTheDocumentedHandshake()
    {
        // The client waits for "<request file>.accepted" (spec section 6); this pins the name and the schema it reads.
        Assert.Equal("request.json.accepted", ArenaSessionContract.RequestFileName + HelperAcknowledgement.FileSuffix);
        Assert.Equal(1, ArenaSessionContract.SchemaVersion);
        Assert.StartsWith("{\"schema\":1,", HelperAcknowledgement.BuildContent(1, AcceptedAt), StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramHandsEverythingToTheHostThatKeepsTheOrder()
    {
        string program = File.ReadAllText(Path.Combine(RepoFiles.Root, "ArenaMode.Session", "Program.cs"));
        string host = File.ReadAllText(Path.Combine(RepoFiles.Root, "ArenaMode.Session", "HelperHost.cs"));

        // Program must not bypass the host (and its order of stale acknowledgement, mutex, request, acknowledgement).
        Assert.Contains("new HelperHost(runner, _log, mutexName, HelperConsole.WriteLine)", program, StringComparison.Ordinal);
        Assert.Contains("mutexName = HelperMutex.Name();", program, StringComparison.Ordinal);
        Assert.Contains(".Execute(command)", program, StringComparison.Ordinal);
        Assert.DoesNotContain("new Mutex", program, StringComparison.Ordinal);
        Assert.DoesNotContain("HelperMutex.Open", program, StringComparison.Ordinal);

        // The host opens the mutex through HelperMutex (the Global, per-user name with its access list), nowhere else.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(host, @"HelperMutex\.Open\(_mutexName\)").Count);
        Assert.DoesNotContain("new Mutex(false, _mutexName", host, StringComparison.Ordinal);
        Assert.DoesNotContain(".Enter(", program, StringComparison.Ordinal);

        int stale = host.IndexOf("HelperAcknowledgementFile.DiscardStale", StringComparison.Ordinal);
        int mutex = host.IndexOf("HelperMutex.Open(_mutexName)", StringComparison.Ordinal);
        int load = host.IndexOf("LoadRequest(command, requestPath);", StringComparison.Ordinal);
        int acknowledge = host.IndexOf("Acknowledge(requestPath)", StringComparison.Ordinal);
        int run = host.IndexOf("_runner.Enter(request)", StringComparison.Ordinal);
        Assert.True(stale > 0 && stale < mutex, "the stale acknowledgement goes first");
        Assert.True(mutex < load && load < acknowledge && acknowledge < run, "mutex, then request, then acknowledgement, then the command");
    }

    [Fact]
    public void TheEnterStartsTheWatchdogAsASecondProcessAndTheHostRunsItWithoutTheMutex()
    {
        string runner = File.ReadAllText(Path.Combine(RepoFiles.Root, "ArenaMode.Session", "HelperRunner.cs"));
        string host = File.ReadAllText(Path.Combine(RepoFiles.Root, "ArenaMode.Session", "HelperHost.cs"));
        string program = File.ReadAllText(Path.Combine(RepoFiles.Root, "ArenaMode.Session", "Program.cs"));

        // The enter keeps the game's handle only to describe it in watch.json, then starts the watch as a process of its own
        // (section 6, step 7) and ends; the normal game of a leave is let go.
        int started = runner.IndexOf("StartGame(request, arguments, true);", StringComparison.Ordinal);
        int watchdog = runner.IndexOf("StartWatchdog(request);", StringComparison.Ordinal);
        int finished = runner.IndexOf("_log.Info(\"enter finished\");", StringComparison.Ordinal);
        Assert.True(started > 0 && started < watchdog && watchdog < finished, "the enter starts the game, then the watchdog, then finishes");
        Assert.Contains("HelperCommand.Watch, \"--file\", path", runner, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(runner, @"StartGame\(request, arguments, true\);"));

        // The host runs the watch before it takes the mutex (a leave must be able to take it while the game runs); the
        // watch takes the mutex only after the game exited, and Program leaves the dispatch to the host.
        int watchCommand = host.IndexOf("command.Name == HelperCommand.Watch", StringComparison.Ordinal);
        int mutex = host.IndexOf("HelperMutex.Open(_mutexName)", StringComparison.Ordinal);
        Assert.True(watchCommand > 0 && watchCommand < mutex, "the watch command runs before the host's mutex");
        int wait = host.IndexOf("_runner.WaitForArenaGameExit()", StringComparison.Ordinal);
        int stopMutex = host.IndexOf("HelperMutex.Open(_mutexName)", wait, StringComparison.Ordinal);
        int stop = host.IndexOf("_runner.StopAfterGameExit()", StringComparison.Ordinal);
        Assert.True(wait > 0 && wait < stopMutex && stopMutex < stop, "the watch waits, then takes the mutex, then stops");
        Assert.DoesNotContain("Watch", program, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFixedFileNamesMatchTheContract()
    {
        Assert.Equal(Path.Combine("R", "server"), HelperServerTree.GetTreePath("R"));
        Assert.Equal(Path.Combine("R", "server", "arena-session.json"), HelperServerTree.GetMarkerPath("R"));
        Assert.Equal(Path.Combine("R", "server.pid"), HelperServerTree.GetPidFilePath("R"));
        Assert.Equal("arena-", HelperLayout.LogFilePatternPrefix);
        Assert.Equal("watch.json", HelperLayout.WatchFileName);
        Assert.Equal("backup.json", HelperLayout.BackupJsonFileName);
        Assert.Equal("./user/profiles/backups", HelperLayout.PrivateBackupDirectory);
        Assert.Equal("spt-backup", HelperLayout.BackupConfigKind);
        Assert.Equal("spt-core", HelperLayout.CoreConfigKind);
        Assert.Equal("fixes", HelperLayout.FixesMemberName);
        Assert.Equal(new[] { "removeModItemsFromProfile", "removeInvalidTradersFromProfile" }, HelperLayout.ProfileCleanupFlags);
        Assert.Equal("patchers", HelperLayout.PatchersFolder);
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
