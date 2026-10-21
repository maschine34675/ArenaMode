using System.Globalization;
using ArenaMode.Shared.Session;

namespace ArenaMode.Tests;

/// <summary>
/// The shared logic of the Arena session feature (docs/ARENA-SESSION-20261021.md, section 4):
/// the mini JSON reader and writer, the effective whitelist policy, the pure allow/exclude
/// decision, the gate report and the contract constants. This code runs inside the BepInEx
/// preloader, in a net472 helper, in the client and in the server, so it is tested here once as
/// plain data in, plain data out.
/// </summary>
public sealed class ArenaWhitelistTests
{
    // ------------------------------------------------------------------ fixtures

    /// <summary>A small default file in the shape of assets/arena-whitelist.json.</summary>
    private const string SmallDefault = """
        {
          "schema": 1,
          "mods": [
            { "guid": "me.sol.sain", "name": "SAIN", "kind": "mod", "clientVersion": "4.5.1", "serverVersion": "4.5.1" },
            { "guid": "com.acidphantasm.progressivebotsystem", "name": "APBS", "kind": "mod", "serverVersion": "2.4.1" },
            { "guid": "com.maschine.UiBridge", "name": "UiBridge", "kind": "tool" },
            { "patcher": "maschine-LogTools.dll", "name": "LogTools patcher", "kind": "tool" }
          ]
        }
        """;

    private const string EmptyDefault = """{ "schema": 1, "mods": [] }""";

    private static string ShippedDefaultPath => Path.Combine(RepoFiles.Root, "assets", "arena-whitelist.json");

    private static ArenaWhitelistPolicy Load(string defaultJson, string? userJson = null)
    {
        Assert.True(ArenaWhitelistPolicy.TryLoad(defaultJson, userJson!, out var policy, out var diagnostics),
            "the policy should load: " + string.Join(" | ", diagnostics));
        Assert.NotNull(policy);
        return policy!;
    }

    private static ArenaWhitelistPolicy LoadWithDiagnostics(string defaultJson, string? userJson, out List<string> diagnostics)
    {
        Assert.True(ArenaWhitelistPolicy.TryLoad(defaultJson, userJson!, out var policy, out diagnostics),
            "the policy should load: " + string.Join(" | ", diagnostics));
        return policy!;
    }

    private static ArenaSessionUnit Unit(string guid, string name, string version, params string[] hardDependencies)
        => new(guid, name, version, guid + ".dll", hardDependencies);

    private static ArenaSessionUnit Find(IEnumerable<ArenaSessionUnit> units, string guid)
        => units.Single(unit => string.Equals(unit.Guid, guid, StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------ JSON: reading

    [Fact]
    public void JsonParsesObjectsArraysAndScalarsIntoPlainTypes()
    {
        const string text = """
            { "a": { "b": [1, 2.5, -3e2, true, false, null, "x"] }, "empty": {}, "list": [], "n": null }
            """;

        Assert.True(ArenaMiniJson.TryParse(text, out var root, out var error), error);
        var obj = Assert.IsType<Dictionary<string, object>>(root);
        var inner = Assert.IsType<Dictionary<string, object>>(obj["a"]);
        var list = Assert.IsType<List<object>>(inner["b"]);

        Assert.Equal(7, list.Count);
        Assert.Equal(1d, Assert.IsType<double>(list[0]));
        Assert.Equal(2.5d, Assert.IsType<double>(list[1]));
        Assert.Equal(-300d, Assert.IsType<double>(list[2]));
        Assert.True(Assert.IsType<bool>(list[3]));
        Assert.False(Assert.IsType<bool>(list[4]));
        Assert.Null(list[5]);
        Assert.Equal("x", Assert.IsType<string>(list[6]));
        Assert.Empty(Assert.IsType<Dictionary<string, object>>(obj["empty"]));
        Assert.Empty(Assert.IsType<List<object>>(obj["list"]));
        Assert.True(obj.ContainsKey("n"));
        Assert.Null(obj["n"]);
    }

    [Fact]
    public void JsonDecodesEveryEscapeIncludingUnicodeAndSurrogatePairs()
    {
        const string text = """
            ["\"\\\/\b\f\n\r\t\u0041\u00E9\uD83D\uDE00\u00e9"]
            """;

        Assert.True(ArenaMiniJson.TryParse(text, out var root, out var error), error);
        string decoded = Assert.IsType<string>(Assert.IsType<List<object>>(root)[0]);

        Assert.Equal("\"\\/\b\f\n\r\tA\u00E9\U0001F600\u00E9", decoded);
    }

    [Fact]
    public void JsonKeepsNonAsciiTextAndNormalPathBackslashEscapes()
    {
        Assert.True(ArenaMiniJson.TryParse("""{"gameDir":"C:\\SPT41\\BepInEx","name":"Gr\u00fc\u00dfe \u00e4"}""",
            out var root, out var error), error);

        Assert.Equal(@"C:\SPT41\BepInEx", ArenaMiniJson.GetString(root, "gameDir"));
        Assert.Equal("Gr\u00fc\u00dfe \u00e4", ArenaMiniJson.GetString(root, "name"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t ")]
    [InlineData("{")]
    [InlineData("}")]
    [InlineData("[")]
    [InlineData("[1,]")]
    [InlineData("[,1]")]
    [InlineData("{\"a\":1,}")]
    [InlineData("{,}")]
    [InlineData("{'a':1}")]
    [InlineData("{a:1}")]
    [InlineData("{\"a\" 1}")]
    [InlineData("{\"a\":}")]
    [InlineData("{\"a\":1 \"b\":2}")]
    [InlineData("[1 2]")]
    [InlineData("\"abc")]
    [InlineData("\"a\\x\"")]
    [InlineData("\"a\\u12\"")]
    [InlineData("\"a\\u12G4\"")]
    [InlineData("\"a\\")]
    [InlineData("\"tab\there\"")]
    [InlineData("\"line\nbreak\"")]
    [InlineData("01")]
    [InlineData("1.")]
    [InlineData("-")]
    [InlineData("+1")]
    [InlineData(".5")]
    [InlineData("1e")]
    [InlineData("1e+")]
    [InlineData("0x10")]
    [InlineData("[1e999]")]
    [InlineData("tru")]
    [InlineData("nul")]
    [InlineData("truex")]
    [InlineData("True")]
    [InlineData("NaN")]
    [InlineData("undefined")]
    [InlineData("// comment\n{}")]
    [InlineData("{} // comment")]
    [InlineData("{\"a\":1,\"a\":2}")]
    public void JsonRejectsMalformedInputWithoutThrowing(string text)
    {
        object? root = new object();
        string? error = null;

        bool parsed = false;
        var thrown = Record.Exception(() => parsed = ArenaMiniJson.TryParse(text, out root, out error));

        Assert.Null(thrown);
        Assert.False(parsed, "should be rejected: " + text);
        Assert.Null(root);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Theory]
    [InlineData("{} x")]
    [InlineData("{}{}")]
    [InlineData("[] []")]
    [InlineData("1 2")]
    [InlineData("\"a\" \"b\"")]
    [InlineData("true false")]
    [InlineData("{\"a\":1}}")]
    [InlineData("[1]]")]
    [InlineData("{} ,")]
    public void JsonRejectsTrailingGarbage(string text)
    {
        Assert.False(ArenaMiniJson.TryParse(text, out var root, out var error));
        Assert.Null(root);
        Assert.Contains("after the JSON value", error);
    }

    [Fact]
    public void JsonRejectsNullInputAndReportsPositions()
    {
        Assert.False(ArenaMiniJson.TryParse(null!, out var root, out var error));
        Assert.Null(root);
        Assert.False(string.IsNullOrEmpty(error));

        Assert.False(ArenaMiniJson.TryParse("{\n  \"a\": 1,\n  \"b\": ?\n}", out _, out error));
        Assert.Contains("line 3", error);
    }

    [Fact]
    public void JsonIsBoundedInNestingDepthInsteadOfOverflowingTheStack()
    {
        string Nested(int depth) => new string('[', depth) + new string(']', depth);

        Assert.True(ArenaMiniJson.TryParse(Nested(ArenaMiniJson.MaxDepth + 1), out _, out var error), error);
        Assert.False(ArenaMiniJson.TryParse(Nested(ArenaMiniJson.MaxDepth + 2), out var root, out error));
        Assert.Null(root);
        Assert.Contains("deeper", error);

        // Far past any sane limit: still a clean failure, not a crashed process.
        Assert.False(ArenaMiniJson.TryParse(new string('[', 200_000), out _, out _));
        Assert.False(ArenaMiniJson.TryParse(new string('{', 1) + string.Concat(Enumerable.Repeat("\"a\":{", 200_000)), out _, out _));
    }

    [Fact]
    public void JsonAcceptsAByteOrderMarkAndSurroundingWhitespaceAndABareNull()
    {
        Assert.True(ArenaMiniJson.TryParse("\uFEFF \r\n{\"a\":1}\n ", out var root, out var error), error);
        Assert.Equal(1, ArenaMiniJson.GetInt(root, "a", -1));

        Assert.True(ArenaMiniJson.TryParse("null", out root, out error), error);
        Assert.Null(root);
        Assert.Null(error);
    }

    [Fact]
    public void JsonAccessorsReturnTheFallbackForMissingOrMistypedMembers()
    {
        Assert.True(ArenaMiniJson.TryParse(
            """{"s":"x","n":42,"f":12.5,"big":1e10,"neg":-7,"b":true,"nul":null,"arr":[1],"obj":{"k":1}}""",
            out var root, out var error), error);

        Assert.Equal("x", ArenaMiniJson.GetString(root, "s"));
        Assert.Null(ArenaMiniJson.GetString(root, "n"));          // a number is not converted to text
        Assert.Null(ArenaMiniJson.GetString(root, "nul"));
        Assert.Null(ArenaMiniJson.GetString(root, "missing"));
        Assert.Null(ArenaMiniJson.GetString("not an object", "s"));
        Assert.Null(ArenaMiniJson.GetString(null!, "s"));

        Assert.Equal(42, ArenaMiniJson.GetInt(root, "n", -1));
        Assert.Equal(-7, ArenaMiniJson.GetInt(root, "neg", -1));
        Assert.Equal(-1, ArenaMiniJson.GetInt(root, "f", -1));     // 12.5 is not a process id
        Assert.Equal(-1, ArenaMiniJson.GetInt(root, "big", -1));   // outside the int range
        Assert.Equal(-1, ArenaMiniJson.GetInt(root, "s", -1));
        Assert.Equal(12.5, ArenaMiniJson.GetNumber(root, "f", 0));
        Assert.Equal(3, ArenaMiniJson.GetNumber(root, "s", 3));

        Assert.True(ArenaMiniJson.GetBool(root, "b", false));
        Assert.True(ArenaMiniJson.GetBool(root, "s", true));       // not a bool: fallback
        Assert.False(ArenaMiniJson.GetBool(root, "missing", false));

        Assert.Single(ArenaMiniJson.GetArray(root, "arr")!);
        Assert.Null(ArenaMiniJson.GetArray(root, "obj"));
        Assert.Single(ArenaMiniJson.GetObject(root, "obj")!);
        Assert.Null(ArenaMiniJson.GetObject(root, "arr"));
    }

    // ------------------------------------------------------------------ JSON: writing

    [Fact]
    public void JsonWriterProducesCompactDocumentedShapes()
    {
        var tree = new Dictionary<string, object?>
        {
            ["a"] = 1,
            ["b"] = new List<object?> { true, null, "x" },
            ["c"] = new Dictionary<string, object?>(),
            ["d"] = new List<string>(),
            ["e"] = new[] { "p", "q" },
            ["f"] = 0.5,
            ["g"] = 6970L,
        };

        Assert.Equal("""{"a":1,"b":[true,null,"x"],"c":{},"d":[],"e":["p","q"],"f":0.5,"g":6970}""",
            ArenaMiniJson.Write(tree));
    }

    [Fact]
    public void JsonWriterRoundTripsEscapesControlCharactersAndLoneSurrogates()
    {
        string tricky = "quote\" backslash\\ slash/ tab\t nl\n cr\r bs\b ff\f ctl\u0001\u001f "
                        + "unicode \u00e9\u00fc emoji \U0001F600 lone-high \ud800 lone-low \udc00 path C:\\SPT41";
        var tree = new Dictionary<string, object?>
        {
            ["text"] = tricky,
            ["key with \"quotes\""] = new List<object?> { tricky, 1.5, -2, false },
        };

        string json = ArenaMiniJson.Write(tree);

        // No raw control character may survive into the text, in either layout.
        Assert.DoesNotContain(json, c => c < ' ');
        Assert.Contains("\\ud800", json);
        Assert.Contains("\\u0001", json);

        Assert.True(ArenaMiniJson.TryParse(json, out var root, out var error), error);
        Assert.Equal(tricky, ArenaMiniJson.GetString(root, "text"));
        var list = ArenaMiniJson.GetArray(root, "key with \"quotes\"")!;
        Assert.Equal(tricky, list[0]);
        Assert.Equal(1.5, list[1]);
        Assert.Equal(-2d, list[2]);
        Assert.Equal(false, list[3]);

        string indented = ArenaMiniJson.Write(tree, indented: true);
        Assert.Contains("\n  ", indented);
        Assert.True(ArenaMiniJson.TryParse(indented, out var again, out error), error);
        Assert.Equal(tricky, ArenaMiniJson.GetString(again, "text"));
    }

    [Fact]
    public void JsonWriterKeepsWholeNumbersExactAndNeverEmitsNonFiniteNumbers()
    {
        Assert.Equal("[1234,-5,0,9007199254740991]", ArenaMiniJson.Write(new List<object> { 1234d, -5, 0L, 9007199254740991d }));
        Assert.Equal("[null,null,null]", ArenaMiniJson.Write(new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity }));

        string big = ArenaMiniJson.Write(1e20);
        Assert.True(ArenaMiniJson.TryParse(big, out var root, out var error), error);
        Assert.Equal(1e20, root);

        // Culture must not leak into the text (a comma would corrupt the document).
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("[0.25,4000]", ArenaMiniJson.Write(new List<object> { 0.25, 4000 }));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ------------------------------------------------------------------ policy: platform, arena, shipped file

    [Fact]
    public void ShippedDefaultFileHoldsExactlyTheDefaultListOfTheSpec()
    {
        Assert.True(File.Exists(ShippedDefaultPath), "missing " + ShippedDefaultPath);
        var policy = LoadWithDiagnostics(File.ReadAllText(ShippedDefaultPath), null, out var diagnostics);
        Assert.Empty(diagnostics);

        var fromFile = policy.Entries.Where(e => e.Tier is ArenaWhitelistTier.Mod or ArenaWhitelistTier.Tool).ToList();

        var expected = new (string Id, string Name, ArenaWhitelistTier Tier, string? Client, string? Server)[]
        {
            ("me.sol.sain", "SAIN", ArenaWhitelistTier.Mod, "4.5.1", "4.5.1"),
            ("com.acidphantasm.progressivebotsystem", "APBS", ArenaWhitelistTier.Mod, null, "2.4.1"),
            ("com.acidphantasm.botplacementsystem", "ABPS", ArenaWhitelistTier.Mod, "2.1.0", "2.1.0"),
            ("xyz.drakia.bigbrain", "BigBrain", ArenaWhitelistTier.Mod, "1.5.0", null),
            ("xyz.drakia.waypoints", "Waypoints", ArenaWhitelistTier.Mod, "1.9.0", null),
            ("com.maschine.UiBridge", "UiBridge", ArenaWhitelistTier.Tool, null, null),
            ("com.maschine.ModProfiler", "ModProfiler", ArenaWhitelistTier.Tool, null, null),
            ("com.maschine.LogTools", "LogTools", ArenaWhitelistTier.Tool, null, null),
            ("maschine-LogTools.dll", "LogTools patcher", ArenaWhitelistTier.Tool, null, null),
        };

        Assert.Equal(expected.Length, fromFile.Count);
        foreach (var want in expected)
        {
            var entry = fromFile.Single(e => string.Equals(e.Id, want.Id, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(want.Name, entry.Name);
            Assert.Equal(want.Tier, entry.Tier);
            if (entry.IsPatcher)
            {
                Assert.True(policy.IsAllowedPatcher(want.Id));
                continue;
            }

            Assert.Equal(want.Client, policy.ReviewedVersion(want.Id, ArenaWhitelistSide.Client));
            Assert.Equal(want.Server, policy.ReviewedVersion(want.Id, ArenaWhitelistSide.Server));
            Assert.True(policy.IsAllowedPlugin(want.Id));
            Assert.True(policy.IsAllowedServerMod(want.Id));
        }
    }

    [Fact]
    public void PlatformAndArenaUnitsAreAllowedFromCodeAloneWithTheirSides()
    {
        var policy = Load(EmptyDefault);

        foreach (string guid in new[] { "com.SPT.core", "com.SPT.custom", "com.SPT.debugging", "com.SPT.singleplayer" })
        {
            Assert.True(policy.IsAllowedPlugin(guid), guid);
            Assert.False(policy.IsAllowedServerMod(guid), guid + " is a client plugin, not a server mod");
            Assert.Equal("4.1.6", policy.ReviewedVersion(guid, ArenaWhitelistSide.Client));
        }

        Assert.True(policy.IsAllowedPlugin("com.bepis.bepinex.configurationmanager"));
        Assert.Null(policy.ReviewedVersion("com.bepis.bepinex.configurationmanager", ArenaWhitelistSide.Client));

        Assert.True(policy.IsAllowedPlugin("com.maschine.ArenaMode"));
        Assert.True(policy.IsAllowedServerMod("com.maschine.ArenaMode"));
        Assert.Null(policy.ReviewedVersion("com.maschine.ArenaMode", ArenaWhitelistSide.Client));

        foreach (string patcher in new[] { "FixPluginTypesSerialization.dll", "spt-prepatch.dll", "maschine-ArenaMode.Gate.dll" })
            Assert.True(policy.IsAllowedPatcher(patcher), patcher);

        Assert.False(policy.IsAllowedPlugin("me.sol.sain"));      // nothing from a default file here
        Assert.False(policy.IsAllowedPlugin("com.example.other"));
        Assert.False(policy.IsAllowedPatcher("maschine-LogTools.dll"));
        Assert.False(policy.IsAllowedPatcher("WTT-ContentBackportPatcher.dll"));
    }

    [Fact]
    public void FikaIsNeverFilteredOnTheClientAndOnlyThere()
    {
        var policy = Load(EmptyDefault);

        foreach (string guid in new[] { "com.fika.core", "com.FIKA.Headless", "com.fika.", "com.fika.anything.at.all" })
        {
            Assert.True(policy.IsAllowedPlugin(guid), guid);
            Assert.True(ArenaWhitelistPolicy.IsFikaGuid(guid), guid);
            var entry = policy.FindPlugin(guid, ArenaWhitelistSide.Client);
            Assert.NotNull(entry);
            Assert.Equal(ArenaWhitelistTier.Platform, entry!.Tier);
        }

        Assert.False(policy.IsAllowedPlugin("com.fikaish.mod"));
        Assert.False(policy.IsAllowedPlugin("fika.core"));
        Assert.False(policy.IsAllowedPlugin("com.fika"));          // the prefix includes the trailing dot
        Assert.False(policy.IsAllowedServerMod("com.fika.core"));  // the spec lists Fika as client only
        Assert.False(ArenaWhitelistPolicy.IsFikaGuid(null!));
    }

    // ------------------------------------------------------------------ policy: user file

    [Fact]
    public void UserAllowAddsUnitsAndUserDenyRemovesDefaultOnes()
    {
        var policy = LoadWithDiagnostics(SmallDefault, """
            {
              "schema": 1,
              "allow": [
                { "guid": "com.example.extra", "name": "Extra", "kind": "mod", "clientVersion": "1.2.3" },
                { "guid": "com.example.tool", "kind": "tool" },
                { "patcher": "Extra.Patcher.dll", "name": "Extra patcher" }
              ],
              "deny": [ "com.maschine.UiBridge", "maschine-LogTools.dll" ]
            }
            """, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.True(policy.UserFileApplied);

        Assert.True(policy.IsAllowedPlugin("com.example.extra"));
        Assert.Equal("1.2.3", policy.ReviewedVersion("com.example.extra", ArenaWhitelistSide.Client));
        Assert.Equal(ArenaWhitelistTier.Tool, policy.FindPlugin("com.example.tool", ArenaWhitelistSide.Client)!.Tier);
        Assert.Equal("com.example.tool", policy.FindPlugin("com.example.tool", ArenaWhitelistSide.Client)!.Name);
        Assert.True(policy.IsAllowedPatcher("Extra.Patcher.dll"));

        Assert.False(policy.IsAllowedPlugin("com.maschine.UiBridge"), "denied default tool");
        Assert.False(policy.IsAllowedPatcher("maschine-LogTools.dll"), "denied default patcher");
        Assert.True(policy.IsAllowedPlugin("me.sol.sain"), "untouched default entry");
        Assert.True(policy.IsDeniedByUser("com.maschine.UiBridge"));
        Assert.True(policy.IsDeniedByUser("maschine-LogTools.dll"));
        Assert.False(policy.IsDeniedByUser("me.sol.sain"));
        Assert.False(policy.IsDeniedByUser("com.example.unknown"));
    }

    [Fact]
    public void UserDenyWinsOverUserAllowForTheSameUnit()
    {
        var policy = Load(EmptyDefault, """
            { "schema": 1, "allow": [ { "guid": "com.example.both" } ], "deny": [ "com.example.both" ] }
            """);

        Assert.False(policy.IsAllowedPlugin("com.example.both"));
        Assert.True(policy.IsDeniedByUser("com.example.both"));
    }

    [Fact]
    public void UserEntryReplacesTheDefaultEntryOfTheSameUnit()
    {
        var policy = Load(SmallDefault, """
            { "schema": 1, "allow": [ { "guid": "ME.SOL.SAIN", "name": "SAIN mine", "clientVersion": "5.0.0" } ] }
            """);

        Assert.Equal("5.0.0", policy.ReviewedVersion("me.sol.sain", ArenaWhitelistSide.Client));
        Assert.Null(policy.ReviewedVersion("me.sol.sain", ArenaWhitelistSide.Server));
        Assert.Equal(1, policy.Entries.Count(e => string.Equals(e.Guid, "me.sol.sain", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void DenyCannotRemovePlatformOrArenaUnits()
    {
        string[] protectedIds =
        [
            "com.SPT.core", "com.SPT.custom", "com.SPT.debugging", "com.SPT.singleplayer",
            "com.bepis.bepinex.configurationmanager", "com.maschine.ArenaMode", "com.fika.core",
            "FixPluginTypesSerialization.dll", "spt-prepatch.dll", "maschine-ArenaMode.Gate.dll",
        ];
        string deny = string.Join(",", protectedIds.Select(id => "\"" + id.ToUpperInvariant() + "\""));

        var baseline = Load(SmallDefault);
        var policy = LoadWithDiagnostics(SmallDefault, "{ \"schema\": 1, \"deny\": [" + deny + "] }", out var diagnostics);

        foreach (string id in protectedIds)
        {
            Assert.False(policy.IsDeniedByUser(id), id);
            if (id.EndsWith(".dll", StringComparison.Ordinal)) Assert.True(policy.IsAllowedPatcher(id), id);
            else Assert.True(policy.IsAllowedPlugin(id), id);
        }

        Assert.True(policy.IsAllowedServerMod("com.maschine.ArenaMode"));
        Assert.Equal(protectedIds.Length, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Contains("cannot be denied", d));
        Assert.Equal(baseline.Hash, policy.Hash);
    }

    [Fact]
    public void UserAllowCannotChangeTheMetadataOfPlatformOrArenaUnits()
    {
        var baseline = Load(EmptyDefault);
        var policy = Load(EmptyDefault, """
            {
              "schema": 1,
              "allow": [
                { "guid": "com.SPT.core", "clientVersion": "9.9.9" },
                { "guid": "com.maschine.ArenaMode", "serverVersion": "9.9.9" },
                { "patcher": "spt-prepatch.dll", "kind": "tool" },
                { "guid": "com.fika.core", "clientVersion": "9.9.9" }
              ]
            }
            """);

        Assert.Equal("4.1.6", policy.ReviewedVersion("com.SPT.core", ArenaWhitelistSide.Client));
        Assert.Null(policy.ReviewedVersion("com.maschine.ArenaMode", ArenaWhitelistSide.Server));
        Assert.Equal(ArenaWhitelistTier.Platform, policy.FindPatcher("spt-prepatch.dll")!.Tier);
        Assert.Null(policy.ReviewedVersion("com.fika.core", ArenaWhitelistSide.Client));
        Assert.Equal(baseline.Hash, policy.Hash);
    }

    [Theory]
    [InlineData("""{ "schema": 2, "allow": [ { "guid": "com.example.x" } ], "deny": [ "me.sol.sain" ] }""", "schema")]
    [InlineData("""{ "allow": [ { "guid": "com.example.x" } ] }""", "schema")]
    [InlineData("""{ "schema": "1", "allow": [ { "guid": "com.example.x" } ] }""", "schema")]
    [InlineData("""{ "schema": 1.5, "allow": [ { "guid": "com.example.x" } ] }""", "schema")]
    [InlineData("""{ "schema": 1, "allow": [ { "guid": "com.example.x" } """, "cannot parse")]
    [InlineData("not json at all", "cannot parse")]
    [InlineData("[]", "JSON object")]
    [InlineData("   ", "empty")]
    [InlineData("", "empty")]
    public void UserFileWithUnknownSchemaOrParseErrorIsIgnoredWithADiagnostic(string userJson, string expectedFragment)
    {
        var baseline = Load(SmallDefault);
        var policy = LoadWithDiagnostics(SmallDefault, userJson, out var diagnostics);

        var message = Assert.Single(diagnostics);
        Assert.StartsWith("user whitelist:", message);
        Assert.Contains(expectedFragment, message);
        Assert.Contains("ignored", message);

        Assert.False(policy.UserFileApplied);
        Assert.False(policy.IsAllowedPlugin("com.example.x"));
        Assert.True(policy.IsAllowedPlugin("me.sol.sain"), "the deny of the ignored file must not apply either");
        Assert.Equal(baseline.Hash, policy.Hash);
    }

    [Fact]
    public void NoUserFileMeansNoDiagnosticsAndAnAbsentFileIsNotAnError()
    {
        var policy = LoadWithDiagnostics(SmallDefault, null, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.False(policy.UserFileApplied);

        var emptyObject = LoadWithDiagnostics(SmallDefault, """{ "schema": 1 }""", out diagnostics);
        Assert.Empty(diagnostics);
        Assert.True(emptyObject.UserFileApplied);
        Assert.Equal(policy.Hash, emptyObject.Hash);
    }

    [Fact]
    public void InvalidUserEntriesAreSkippedIndividuallyAndTheRestStillApplies()
    {
        var policy = LoadWithDiagnostics(SmallDefault, """
            {
              "schema": 1,
              "allow": [
                { "guid": "com.example.good1" },
                { "name": "no id at all" },
                { "guid": "a", "patcher": "b.dll" },
                { "guid": "com.example.badkind", "kind": "plugin" },
                { "guid": "com.example.badversion", "clientVersion": "latest" },
                { "guid": 12 },
                "just a string",
                { "guid": "com.example.good2", "kind": "TOOL" }
              ],
              "deny": [ "", 7, "com.acidphantasm.progressivebotsystem" ]
            }
            """, out var diagnostics);

        Assert.True(policy.IsAllowedPlugin("com.example.good1"));
        Assert.Equal(ArenaWhitelistTier.Tool, policy.FindPlugin("com.example.good2", ArenaWhitelistSide.Client)!.Tier);
        Assert.False(policy.IsAllowedPlugin("com.example.badkind"));
        Assert.False(policy.IsAllowedPlugin("com.example.badversion"));
        Assert.False(policy.IsAllowedPlugin("a"));
        Assert.False(policy.IsAllowedPatcher("b.dll"));
        Assert.False(policy.IsAllowedPlugin("com.acidphantasm.progressivebotsystem"), "the valid deny still applies");

        Assert.Equal(8, diagnostics.Count); // six bad allow entries + two bad deny entries
        Assert.Contains(diagnostics, d => d.Contains("allow[1]"));
        Assert.Contains(diagnostics, d => d.Contains("allow[6]"));
        Assert.Contains(diagnostics, d => d.Contains("deny[0]"));
        Assert.Contains(diagnostics, d => d.Contains("deny[1]"));
        Assert.All(diagnostics, d => Assert.StartsWith("user whitelist:", d));
    }

    [Fact]
    public void MistypedUserAllowAndDenyContainersAreIgnoredWithADiagnostic()
    {
        var policy = LoadWithDiagnostics(SmallDefault, """
            { "schema": 1, "allow": { "guid": "com.example.x" }, "deny": "me.sol.sain" }
            """, out var diagnostics);

        Assert.Equal(2, diagnostics.Count);
        Assert.False(policy.IsAllowedPlugin("com.example.x"));
        Assert.True(policy.IsAllowedPlugin("me.sol.sain"));
    }

    // ------------------------------------------------------------------ policy: unlisted mode (blacklist)

    [Theory]
    [InlineData(null)]
    [InlineData("""{ "schema": 1 }""")]
    [InlineData("""{ "schema": 1, "unlisted": null }""")]
    [InlineData("""{ "schema": 1, "unlisted": "deny" }""")]
    [InlineData("""{ "schema": 1, "unlisted": " DENY " }""")]
    public void WithoutAnUnlistedAllowThePolicyIsAWhitelist(string? userJson)
    {
        var baseline = Load(SmallDefault);
        var policy = LoadWithDiagnostics(SmallDefault, userJson, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(ArenaUnlistedMode.Deny, policy.UnlistedMode);
        Assert.False(policy.AllowsUnlisted);
        Assert.False(policy.IsUnlistedAllowed("com.example.unknown"));
        Assert.False(policy.IsAllowedPlugin("com.example.unknown"));
        Assert.False(policy.IsAllowedServerMod("com.example.unknown"));
        Assert.False(policy.IsAllowedPatcher("Unknown.Patcher.dll"));
        Assert.Equal(baseline.Hash, policy.Hash);
    }

    [Theory]
    [InlineData("allow")]
    [InlineData("ALLOW")]
    [InlineData("  Allow ")]
    public void UnlistedAllowMakesThePolicyABlacklist(string value)
    {
        var policy = LoadWithDiagnostics(SmallDefault, "{ \"schema\": 1, \"unlisted\": \"" + value + "\", "
            + "\"deny\": [ \"com.example.bad\", \"BepInEx/patchers/Bad.Patcher.dll\", \"me.sol.sain\" ] }", out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.True(policy.UserFileApplied);
        Assert.Equal(ArenaUnlistedMode.Allow, policy.UnlistedMode);
        Assert.True(policy.AllowsUnlisted);

        // Everything nobody named is allowed, for plugins, server mods and preloader patchers alike.
        Assert.True(policy.IsAllowedPlugin("com.example.anything"));
        Assert.True(policy.IsAllowedPlugin("COM.EXAMPLE.ANYTHING"));
        Assert.True(policy.IsAllowedServerMod("com.example.anything"));
        Assert.True(policy.IsAllowedPatcher("Anything.Patcher.dll"));
        Assert.True(policy.IsAllowedPatcher(@"D:\SPT41\BepInEx\patchers\Some\Anything.Patcher.dll"));
        Assert.True(policy.IsUnlistedAllowed("com.example.anything"));

        // The deny list is the blacklist: by GUID, case-insensitively, and for patchers by file name.
        Assert.False(policy.IsAllowedPlugin("com.example.bad"));
        Assert.False(policy.IsAllowedPlugin("COM.EXAMPLE.BAD"));
        Assert.False(policy.IsAllowedServerMod("com.example.bad"));
        Assert.False(policy.IsAllowedPatcher("Bad.Patcher.dll"));
        Assert.False(policy.IsAllowedPatcher("bad.patcher.DLL"));
        Assert.False(policy.IsAllowedPatcher(@"X:\elsewhere\Bad.Patcher.dll"));
        Assert.False(policy.IsUnlistedAllowed("com.example.bad"));
        Assert.False(policy.IsAllowedPlugin("me.sol.sain"), "a deny also removes a unit the default list names");

        // Named units stay named: nothing about them changes with the mode.
        Assert.True(policy.IsAllowedPlugin("com.maschine.UiBridge"));
        Assert.True(policy.IsAllowedPatcher("maschine-LogTools.dll"));
        Assert.Equal(ArenaWhitelistTier.Platform, policy.FindPlugin("com.SPT.core", ArenaWhitelistSide.Client)!.Tier);

        // A name that is blank cannot be asked about.
        Assert.False(policy.IsAllowedPlugin(""));
        Assert.False(policy.IsAllowedPlugin("   "));
        Assert.False(policy.IsAllowedPlugin(null!));
        Assert.False(policy.IsAllowedServerMod(null!));
        Assert.False(policy.IsAllowedPatcher(""));
        Assert.False(policy.IsAllowedPatcher(null!));
        Assert.False(policy.IsAllowedPatcher(@"D:\SPT41\BepInEx\patchers\Anything.Patcher.dll\"));
    }

    [Fact]
    public void UnlistedAllowGivesAnUnlistedUnitNoReviewedVersion()
    {
        var policy = Load(SmallDefault, """{ "schema": 1, "unlisted": "allow" }""");

        Assert.Null(policy.FindPlugin("com.example.anything", ArenaWhitelistSide.Client));
        Assert.Null(policy.FindPatcher("Anything.Patcher.dll"));
        Assert.Null(policy.ReviewedVersion("com.example.anything", ArenaWhitelistSide.Client));
        Assert.Null(policy.ReviewedVersion("com.example.anything", ArenaWhitelistSide.Server));
        Assert.DoesNotContain(policy.Entries, e => e.Id == "com.example.anything");
    }

    [Theory]
    [InlineData("\"maybe\"")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("\"allowed\"")]
    [InlineData("\"alow\"")]
    [InlineData("\"yes\"")]
    [InlineData("\"true\"")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("[ \"allow\" ]")]
    [InlineData("{ \"mode\": \"allow\" }")]
    public void AnUnlistedValueThatIsNotDenyOrAllowInvalidatesTheWholeUserFile(string unlistedJson)
    {
        var baseline = Load(SmallDefault);
        var policy = LoadWithDiagnostics(SmallDefault,
            "{ \"schema\": 1, \"unlisted\": " + unlistedJson + ", \"allow\": [ { \"guid\": \"com.example.x\" } ], "
            + "\"deny\": [ \"me.sol.sain\", \"com.maschine.UiBridge\" ] }", out var diagnostics);

        var message = Assert.Single(diagnostics);
        Assert.StartsWith("user whitelist:", message);
        Assert.Contains("unlisted", message);
        Assert.Contains("\"deny\" or \"allow\"", message);
        Assert.EndsWith("the file is ignored", message);

        // Ignored like an unknown schema: neither the mode nor a single entry of the file applies.
        Assert.False(policy.UserFileApplied);
        Assert.Equal(ArenaUnlistedMode.Deny, policy.UnlistedMode);
        Assert.False(policy.IsAllowedPlugin("com.example.x"));
        Assert.True(policy.IsAllowedPlugin("me.sol.sain"));
        Assert.True(policy.IsAllowedPlugin("com.maschine.UiBridge"));
        Assert.False(policy.IsAllowedPlugin("com.example.unknown"));
        Assert.Equal(baseline.Hash, policy.Hash);
    }

    [Fact]
    public void ALongBogusUnlistedValueIsShortenedInTheDiagnostic()
    {
        var policy = LoadWithDiagnostics(SmallDefault,
            "{ \"schema\": 1, \"unlisted\": \"" + new string('x', 5000) + "\" }", out var diagnostics);

        Assert.False(policy.UserFileApplied);
        Assert.InRange(Assert.Single(diagnostics).Length, 1, 300);
    }

    [Fact]
    public void AFileWithAnUnknownSchemaDoesNotSwitchTheMode()
    {
        var policy = LoadWithDiagnostics(SmallDefault, """{ "schema": 2, "unlisted": "allow" }""", out var diagnostics);

        Assert.Contains("schema", Assert.Single(diagnostics));
        Assert.False(policy.UserFileApplied);
        Assert.Equal(ArenaUnlistedMode.Deny, policy.UnlistedMode);
        Assert.False(policy.IsAllowedPlugin("com.example.unknown"));
    }

    [Fact]
    public void TheModeSurvivesAnInvalidAllowEntryInTheSameFile()
    {
        // A lost allow entry costs nothing under a blacklist (the mod is unlisted-allowed anyway), so it is skipped alone.
        var policy = LoadWithDiagnostics(SmallDefault, """
            { "schema": 1, "unlisted": "allow", "allow": [ { "name": "no id" }, 7 ], "deny": [ "com.example.bad" ] }
            """, out var diagnostics);

        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Contains("allow[", d));
        Assert.True(policy.UserFileApplied);
        Assert.Equal(ArenaUnlistedMode.Allow, policy.UnlistedMode);
        Assert.True(policy.IsAllowedPlugin("com.example.other"));
        Assert.False(policy.IsAllowedPlugin("com.example.bad"));
    }

    [Fact]
    public void ABlacklistMayLeaveTheAllowMemberMistypedWithoutLosingTheFile()
    {
        var policy = LoadWithDiagnostics(SmallDefault, """
            { "schema": 1, "unlisted": "allow", "allow": "com.example.x", "deny": [ "com.example.bad" ] }
            """, out var diagnostics);

        Assert.Contains("allow", Assert.Single(diagnostics));
        Assert.True(policy.UserFileApplied);
        Assert.True(policy.IsAllowedPlugin("com.example.other"));
        Assert.False(policy.IsAllowedPlugin("com.example.bad"));
    }

    // In blacklist mode the deny list is the whole filter. A deny entry that is silently lost is a mod the
    // user wanted out that loads, so a deny list that cannot be read in full costs the whole file (like an
    // unknown "unlisted" value) and the shipped whitelist stays in force.
    [Theory]
    [InlineData("\"com.example.looting\"")]                       // a string instead of an array
    [InlineData("{ \"guid\": \"com.example.looting\" }")]          // an object instead of an array
    [InlineData("7")]
    [InlineData("true")]
    [InlineData("[ 123, \"com.example.looting\" ]")]              // a number
    [InlineData("[ \"\", \"com.example.looting\" ]")]             // an empty string
    [InlineData("[ \"   \", \"com.example.looting\" ]")]          // a blank string
    [InlineData("[ \"com.example.looting\", null ]")]            // a JSON null entry
    [InlineData("[ \"com.example.looting\", true ]")]
    [InlineData("[ \"com.example.looting\", [ \"x\" ] ]")]        // a nested array
    [InlineData("[ { \"guid\": \"com.example.looting\" } ]")]     // the allow entry syntax by mistake
    public void ABlacklistWithADenyListThatCannotBeReadInFullIsIgnoredAsAWhole(string denyValue)
    {
        var baseline = Load(SmallDefault);
        string userJson = "{ \"schema\": 1, \"unlisted\": \"allow\", "
            + "\"allow\": [ { \"guid\": \"com.example.extra\", \"name\": \"Extra\" } ], \"deny\": " + denyValue + " }";

        var policy = LoadWithDiagnostics(SmallDefault, userJson, out var diagnostics);

        var diagnostic = Assert.Single(diagnostics);
        Assert.StartsWith("user whitelist:", diagnostic);
        Assert.Contains("deny", diagnostic);
        Assert.EndsWith("the file is ignored", diagnostic);
        Assert.False(policy.UserFileApplied);
        Assert.Equal(ArenaUnlistedMode.Deny, policy.UnlistedMode);
        Assert.False(policy.AllowsUnlisted);
        Assert.False(policy.IsAllowedPlugin("com.example.looting"), "the mod the user meant to exclude does not load");
        Assert.False(policy.IsAllowedPlugin("com.example.unknown"), "the policy fell back to the shipped whitelist");
        Assert.False(policy.IsAllowedPlugin("com.example.extra"), "nothing of the rejected file applies, its allow list included");
        Assert.True(policy.IsAllowedPlugin("me.sol.sain"));
        Assert.Equal(baseline.Hash, policy.Hash);
    }

    [Theory]
    [InlineData("")]                                                // no deny member at all
    [InlineData(", \"deny\": null")]
    [InlineData(", \"deny\": []")]
    [InlineData(", \"deny\": [ \"com.example.bad\", \"x.dll\", \" Padded.Guid \" ]")]
    public void ABlacklistWithAReadableDenyListIsApplied(string denyMember)
    {
        var policy = LoadWithDiagnostics(SmallDefault, "{ \"schema\": 1, \"unlisted\": \"allow\"" + denyMember + " }", out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.True(policy.UserFileApplied);
        Assert.Equal(ArenaUnlistedMode.Allow, policy.UnlistedMode);
        Assert.True(policy.IsAllowedPlugin("com.example.unknown"));
    }

    [Theory]
    [InlineData("""{ "schema": 1, "deny": "me.sol.sain" }""")]
    [InlineData("""{ "schema": 1, "unlisted": "deny", "deny": [ 7, "", "me.sol.sain" ] }""")]
    public void AWhitelistKeepsSkippingUnreadableDenyEntriesIndividually(string userJson)
    {
        // Under a whitelist nothing unlisted loads, so a lost deny entry only keeps a listed mod; the rest still applies.
        var policy = LoadWithDiagnostics(SmallDefault, userJson, out var diagnostics);

        Assert.NotEmpty(diagnostics);
        Assert.True(policy.UserFileApplied);
        Assert.Equal(ArenaUnlistedMode.Deny, policy.UnlistedMode);
        Assert.False(policy.IsAllowedPlugin("com.example.unknown"));
    }

    [Fact]
    public void TheDefaultFileDoesNotChooseTheMode()
    {
        // Unknown members of the shipped file are tolerated; this one must not become a way to ship a blacklist.
        var baseline = Load(SmallDefault);
        var policy = LoadWithDiagnostics(SmallDefault.Replace("\"schema\": 1,", "\"schema\": 1, \"unlisted\": \"allow\","), null, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(ArenaUnlistedMode.Deny, policy.UnlistedMode);
        Assert.False(policy.IsAllowedPlugin("com.example.unknown"));
        Assert.Equal(baseline.Hash, policy.Hash);
    }

    [Fact]
    public void DenyStillCannotRemovePlatformOrArenaUnitsInBlacklistMode()
    {
        string[] protectedIds =
        [
            "com.SPT.core", "com.bepis.bepinex.configurationmanager", "com.maschine.ArenaMode", "com.fika.core",
            "FixPluginTypesSerialization.dll", "spt-prepatch.dll", "maschine-ArenaMode.Gate.dll",
        ];
        string deny = string.Join(",", protectedIds.Select(id => "\"" + id + "\""));

        var baseline = Load(SmallDefault, """{ "schema": 1, "unlisted": "allow" }""");
        var policy = LoadWithDiagnostics(SmallDefault, "{ \"schema\": 1, \"unlisted\": \"allow\", \"deny\": [" + deny + "] }", out var diagnostics);

        Assert.Equal(protectedIds.Length, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Contains("cannot be denied", d));
        foreach (string id in protectedIds)
        {
            Assert.False(policy.IsDeniedByUser(id), id);
            if (id.EndsWith(".dll", StringComparison.Ordinal)) Assert.NotNull(policy.FindPatcher(id));
            else Assert.NotNull(policy.FindPlugin(id, ArenaWhitelistSide.Client));
        }

        Assert.Equal(baseline.Hash, policy.Hash);
    }

    // ------------------------------------------------------------------ policy: broken default

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("""{ "mods": [] }""")]
    [InlineData("""{ "schema": 2, "mods": [] }""")]
    [InlineData("""{ "schema": "1", "mods": [] }""")]
    [InlineData("""{ "schema": 1 }""")]
    [InlineData("""{ "schema": 1, "mods": {} }""")]
    [InlineData("""{ "schema": 1, "mods": [ "me.sol.sain" ] }""")]
    [InlineData("""{ "schema": 1, "mods": [ { "name": "no id" } ] }""")]
    [InlineData("""{ "schema": 1, "mods": [ { "guid": "a", "patcher": "b.dll" } ] }""")]
    [InlineData("""{ "schema": 1, "mods": [ { "guid": "" } ] }""")]
    [InlineData("""{ "schema": 1, "mods": [ { "guid": 5 } ] }""")]
    [InlineData("""{ "schema": 1, "mods": [ { "guid": "a", "kind": "plugin" } ] }""")]
    [InlineData("""{ "schema": 1, "mods": [ { "guid": "a", "clientVersion": "latest" } ] }""")]
    [InlineData("""{ "schema": 1, "mods": [ { "guid": "a", "serverVersion": 4 } ] }""")]
    [InlineData("""{ "schema": 1, "mods": [ { "guid": "a" }, { "kind": "mod" } ] }""")]
    [InlineData("""{ "schema": 1, "mods": [ { "patcher": "x/" } ] }""")]
    public void ABrokenDefaultFileFailsTheLoadWithAnExplanation(string? defaultJson)
    {
        // A valid user file must not rescue a broken default: the session would run on a policy
        // nobody shipped.
        const string validUser = """{ "schema": 1, "allow": [ { "guid": "com.example.x" } ] }""";

        foreach (string? user in new[] { null, validUser })
        {
            bool loaded = ArenaWhitelistPolicy.TryLoad(defaultJson!, user!, out var policy, out var diagnostics);

            Assert.False(loaded);
            Assert.Null(policy);
            Assert.NotEmpty(diagnostics);
            Assert.All(diagnostics, d => Assert.StartsWith("default whitelist:", d));
        }
    }

    [Fact]
    public void ADefaultEntryNamesTheBadIndexInItsDiagnostic()
    {
        Assert.False(ArenaWhitelistPolicy.TryLoad(
            """{ "schema": 1, "mods": [ { "guid": "ok" }, { "guid": "ok2" }, { "name": "bad" } ] }""",
            null!, out _, out var diagnostics));

        Assert.Contains("mods[2]", Assert.Single(diagnostics));
    }

    // ------------------------------------------------------------------ policy: matching

    [Fact]
    public void GuidMatchingIsOrdinalAndCaseInsensitiveOnBothSides()
    {
        var policy = Load(SmallDefault, """{ "schema": 1, "deny": [ "COM.MASCHINE.UIBRIDGE" ] }""");

        Assert.True(policy.IsAllowedPlugin("me.sol.sain"));
        Assert.True(policy.IsAllowedPlugin("ME.SOL.SAIN"));
        Assert.True(policy.IsAllowedPlugin("Me.Sol.Sain"));
        Assert.True(policy.IsAllowedPlugin("  me.sol.sain\t"));
        Assert.True(policy.IsAllowedServerMod("ME.SOL.SAIN"));
        Assert.True(policy.IsAllowedPlugin("COM.SPT.CORE"));
        Assert.True(policy.IsAllowedPlugin("COM.MASCHINE.ARENAMODE"));
        Assert.Equal("4.5.1", policy.ReviewedVersion("ME.SOL.SAIN", ArenaWhitelistSide.Client));
        Assert.Equal("2.4.1", policy.ReviewedVersion("COM.ACIDPHANTASM.PROGRESSIVEBOTSYSTEM", ArenaWhitelistSide.Server));

        Assert.False(policy.IsAllowedPlugin("com.maschine.uibridge"), "denied in another case");
        Assert.True(policy.IsDeniedByUser("com.maschine.UiBridge"));

        // Ordinal: no culture folding, no partial or prefix matches.
        Assert.False(policy.IsAllowedPlugin("me.sol.sain2"));
        Assert.False(policy.IsAllowedPlugin("me.sol"));
        Assert.False(policy.IsAllowedPlugin("me.sol.sa\u0131n")); // dotless i is not an I
        Assert.False(policy.IsAllowedPlugin(""));
        Assert.False(policy.IsAllowedPlugin("   "));
        Assert.False(policy.IsAllowedPlugin(null!));
        Assert.False(policy.IsAllowedServerMod(null!));
        Assert.Null(policy.ReviewedVersion(null!, ArenaWhitelistSide.Client));
        Assert.Null(policy.ReviewedVersion("com.example.unknown", ArenaWhitelistSide.Client));
    }

    [Fact]
    public void PatchersAreMatchedByFileNameNotByPathOrSubstring()
    {
        var policy = Load(SmallDefault);

        Assert.True(policy.IsAllowedPatcher("maschine-LogTools.dll"));
        Assert.True(policy.IsAllowedPatcher("MASCHINE-LOGTOOLS.DLL"));
        Assert.True(policy.IsAllowedPatcher(@"D:\SPT41\BepInEx\patchers\maschine-LogTools.dll"));
        Assert.True(policy.IsAllowedPatcher("BepInEx/patchers/maschine-ArenaMode/maschine-ArenaMode.Gate.dll"));
        Assert.True(policy.IsAllowedPatcher("  spt-prepatch.dll "));

        Assert.False(policy.IsAllowedPatcher("LogTools.dll"));
        Assert.False(policy.IsAllowedPatcher("maschine-LogTools"));
        Assert.False(policy.IsAllowedPatcher("maschine-LogTools.dll.bak"));
        Assert.False(policy.IsAllowedPatcher("xmaschine-LogTools.dll"));
        Assert.False(policy.IsAllowedPatcher(@"D:\SPT41\BepInEx\patchers\maschine-LogTools.dll\"));
        Assert.False(policy.IsAllowedPatcher(""));
        Assert.False(policy.IsAllowedPatcher(null!));

        // A GUID is not a patcher and a patcher is not a GUID.
        Assert.False(policy.IsAllowedPlugin("maschine-LogTools.dll"));
        Assert.False(policy.IsAllowedPatcher("me.sol.sain"));
    }

    [Fact]
    public void APatcherEntryWrittenWithADirectoryIsReducedToItsFileName()
    {
        var policy = Load("""
            { "schema": 1, "mods": [ { "patcher": "BepInEx\\patchers\\Some\\Thing.Patcher.dll", "kind": "tool" } ] }
            """);

        Assert.True(policy.IsAllowedPatcher("Thing.Patcher.dll"));
        Assert.True(policy.IsAllowedPatcher(@"C:\elsewhere\thing.patcher.DLL"));
        Assert.Equal("Thing.Patcher.dll", policy.FindPatcher("Thing.Patcher.dll")!.Patcher);
    }

    [Fact]
    public void UserDenyOfAPatcherGivenAsAPathStillRemovesIt()
    {
        var policy = Load(SmallDefault, """{ "schema": 1, "deny": [ "BepInEx/patchers/MASCHINE-LOGTOOLS.DLL" ] }""");

        Assert.False(policy.IsAllowedPatcher("maschine-LogTools.dll"));
        Assert.True(policy.IsDeniedByUser("maschine-LogTools.dll"));
    }

    // ------------------------------------------------------------------ versions

    [Theory]
    [InlineData("4.5.1", "4.5.1", false)]
    [InlineData("v4.5.1", "4.5.1", false)]
    [InlineData("V4.5.1", "4.5.1", false)]
    [InlineData("4.5.1", "v4.5.1", false)]
    [InlineData(" 4.5.1 ", "4.5.1", false)]
    [InlineData("4.5.1-beta.2", "4.5.1", false)]
    [InlineData("4.5.1+d52d9c99836b", "4.5.1", false)]
    [InlineData("v4.5.1-rc1+build.7", "4.5.1", false)]
    [InlineData("4.5.1.0", "4.5.1", false)]
    [InlineData("4.5.1", "4.5.1.0", false)]
    [InlineData("4.5", "4.5.0", false)]
    [InlineData("4.5", "4.5.0.0", false)]
    [InlineData("5", "5.0", false)]
    [InlineData("4.5.2", "4.5.1", true)]
    [InlineData("4.5.0", "4.5.1", true)]
    [InlineData("4.6.1", "4.5.1", true)]
    [InlineData("5.5.1", "4.5.1", true)]
    [InlineData("4.5.1.1", "4.5.1", true)]
    [InlineData("4.5.1.1", "4.5.1.0", true)]
    [InlineData("4.10.0", "4.9.0", true)]
    [InlineData("garbage", "4.5.1", true)]
    [InlineData("4.5.x", "4.5.1", true)]
    [InlineData("1.2.3.4.5", "1.2.3.4", true)]
    [InlineData("", "4.5.1", true)]
    [InlineData("   ", "4.5.1", true)]
    [InlineData(null, "4.5.1", true)]
    [InlineData("-1", "4.5.1", true)]
    [InlineData("v", "4.5.1", true)]
    [InlineData("nightly", "nightly", false)]
    [InlineData("4.5.1", null, false)]
    [InlineData("anything", "", false)]
    [InlineData(null, null, false)]
    public void VersionsCompareAsSystemVersionAfterDroppingLeadingVAndSuffixes(string? installed, string? reviewed, bool differs)
    {
        Assert.Equal(differs, ArenaWhitelistPolicy.VersionDiffers(installed!, reviewed!));
    }

    [Fact]
    public void TryParseVersionNormalisesToFourComponents()
    {
        Assert.True(ArenaWhitelistPolicy.TryParseVersion("v4.5.1-beta+x", out var version));
        Assert.Equal(new Version(4, 5, 1, 0), version);

        Assert.True(ArenaWhitelistPolicy.TryParseVersion("7", out version));
        Assert.Equal(new Version(7, 0, 0, 0), version);

        foreach (string bad in new[] { "", "v", "x.y", "1.2.3.4.5", "-1.0", "1..2" })
        {
            Assert.False(ArenaWhitelistPolicy.TryParseVersion(bad, out var none), bad);
            Assert.Null(none);
        }
    }

    // ------------------------------------------------------------------ decision

    [Fact]
    public void DecisionAllowsPlatformArenaWhitelistAndToolUnitsWithTheirReasons()
    {
        var policy = Load(SmallDefault);
        var installed = new[]
        {
            Unit("com.SPT.core", "SPT.Core", "4.1.6"),
            Unit("com.fika.core", "Fika", "2.0.0"),
            Unit("com.maschine.ArenaMode", "ArenaMode", "0.1.3"),
            Unit("me.sol.sain", "SAIN", "4.5.1"),
            Unit("com.maschine.UiBridge", "UiBridge", "0.9.4"),
        };

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, installed);

        Assert.Empty(decision.Excluded);
        Assert.Empty(decision.Warnings);
        Assert.False(decision.HasExclusions);
        Assert.Equal(new[] { "platform", "platform", "arena", "whitelist", "tool" }, decision.Allowed.Select(u => u.Reason).ToArray());
        Assert.Equal(installed.Select(u => u.Guid), decision.Allowed.Select(u => u.Guid));
    }

    [Fact]
    public void DecisionExcludesUnknownUnitsAndNamesAUserDenyDifferently()
    {
        var policy = Load(SmallDefault, """{ "schema": 1, "deny": [ "com.maschine.UiBridge", "com.example.neverheardof" ] }""");
        var installed = new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.1"),
            Unit("com.example.looting", "Looting Bots", "1.8.0"),
            Unit("com.maschine.UiBridge", "UiBridge", "0.9.4"),
            Unit("com.example.neverheardof", "Whatever", "1.0.0"),
            Unit("", "No Guid", "1.0.0"),
        };

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, installed);

        Assert.Equal(new[] { "me.sol.sain" }, decision.Allowed.Select(u => u.Guid).ToArray());
        Assert.Equal("not-on-whitelist", Find(decision.Excluded, "com.example.looting").Reason);
        Assert.Equal("denied-by-user", Find(decision.Excluded, "com.maschine.UiBridge").Reason);
        Assert.Equal("denied-by-user", Find(decision.Excluded, "com.example.neverheardof").Reason);
        Assert.Equal("not-on-whitelist", decision.Excluded.Single(u => u.Guid == "").Reason);
        Assert.True(decision.HasExclusions);
    }

    [Fact]
    public void ADifferentReviewedVersionWarnsAndNeverExcludes()
    {
        var policy = Load(SmallDefault);

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.2"),
            Unit("com.SPT.core", "SPT.Core", "4.1.7"),
            Unit("com.maschine.UiBridge", "UiBridge", "9.9.9"),                       // reviewed: any
            Unit("com.acidphantasm.progressivebotsystem", "APBS", "0.0.1"),           // client: no reviewed version
        });

        Assert.Empty(decision.Excluded);
        Assert.Equal(4, decision.Allowed.Count);
        Assert.Equal(new[]
        {
            "SAIN: version 4.5.2 differs from reviewed 4.5.1",
            "SPT.Core: version 4.1.7 differs from reviewed 4.1.6",
        }, decision.Warnings.ToArray());
    }

    [Theory]
    [InlineData("4.5.1", false)]
    [InlineData("v4.5.1", false)]
    [InlineData("4.5.1.0", false)]
    [InlineData("4.5.1-beta", false)]
    [InlineData("4.5.0", true)]
    [InlineData("not-a-version", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    public void VersionWarningDependsOnTheInstalledVersionOnly(string? installed, bool warns)
    {
        var policy = Load(SmallDefault);

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client,
            new[] { Unit("me.sol.sain", "SAIN", installed!) });

        Assert.Single(decision.Allowed);
        Assert.Empty(decision.Excluded);
        Assert.Equal(warns ? 1 : 0, decision.Warnings.Count);
        if (warns) Assert.StartsWith("SAIN: version ", decision.Warnings[0]);
    }

    [Fact]
    public void AnUnknownInstalledVersionIsNamedAsSuchInTheWarning()
    {
        var policy = Load(SmallDefault);

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client,
            new[] { Unit("me.sol.sain", "SAIN", "") });

        Assert.Equal("SAIN: version (unknown) differs from reviewed 4.5.1", Assert.Single(decision.Warnings));
    }

    [Fact]
    public void AHardDependencyOfAnAllowedUnitIsAllowedWithAWarning()
    {
        var policy = Load(SmallDefault);
        var installed = new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.1", "com.example.lib"),
            Unit("com.example.lib", "Example Lib", "3.0.0"),
            Unit("com.example.unrelated", "Unrelated", "1.0.0"),
        };

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, installed);

        Assert.Equal("dependency-of:me.sol.sain", Find(decision.Allowed, "com.example.lib").Reason);
        Assert.Equal("not-on-whitelist", Find(decision.Excluded, "com.example.unrelated").Reason);
        Assert.Equal("Example Lib: pulled in as hard dependency of SAIN", Assert.Single(decision.Warnings));
    }

    [Fact]
    public void DependencyClosureIsTransitiveAndNamesTheDirectDependant()
    {
        var policy = Load(SmallDefault);
        var installed = new[]
        {
            Unit("com.example.leaf", "Leaf", "1.0.0"),
            Unit("com.example.middle", "Middle", "1.0.0", "com.example.leaf"),
            Unit("me.sol.sain", "SAIN", "4.5.1", "com.example.middle"),
        };

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, installed);

        Assert.Empty(decision.Excluded);
        Assert.Equal("dependency-of:me.sol.sain", Find(decision.Allowed, "com.example.middle").Reason);
        Assert.Equal("dependency-of:com.example.middle", Find(decision.Allowed, "com.example.leaf").Reason);
        Assert.Equal(
            new[]
            {
                "Leaf: pulled in as hard dependency of Middle",
                "Middle: pulled in as hard dependency of SAIN",
            },
            decision.Warnings.ToArray());
    }

    [Fact]
    public void DependencyClosureDoesNotFollowExcludedUnitsAndIgnoresMissingOnes()
    {
        var policy = Load(SmallDefault);
        var installed = new[]
        {
            Unit("com.example.excluded", "Excluded", "1.0.0", "com.example.hidden"),
            Unit("com.example.hidden", "Hidden", "1.0.0"),
            Unit("me.sol.sain", "SAIN", "4.5.1", "com.example.not.installed", ""),
        };

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, installed);

        Assert.Equal(new[] { "me.sol.sain" }, decision.Allowed.Select(u => u.Guid).ToArray());
        Assert.Equal(2, decision.Excluded.Count);
        Assert.Empty(decision.Warnings);
    }

    [Fact]
    public void DependencyCyclesTerminate()
    {
        var policy = Load(SmallDefault);
        var installed = new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.1", "com.example.a"),
            Unit("com.example.a", "A", "1.0.0", "com.example.b"),
            Unit("com.example.b", "B", "1.0.0", "com.example.a", "me.sol.sain"),
        };

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, installed);

        Assert.Empty(decision.Excluded);
        Assert.Equal("dependency-of:me.sol.sain", Find(decision.Allowed, "com.example.a").Reason);
        Assert.Equal("dependency-of:com.example.a", Find(decision.Allowed, "com.example.b").Reason);
        Assert.Equal("whitelist", Find(decision.Allowed, "me.sol.sain").Reason);
    }

    [Fact]
    public void DependencyGuidsMatchCaseInsensitively()
    {
        var policy = Load(SmallDefault);

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.1", "COM.EXAMPLE.LIB"),
            Unit("com.example.lib", "Lib", "1.0.0"),
        });

        Assert.Empty(decision.Excluded);
        Assert.Equal("dependency-of:me.sol.sain", Find(decision.Allowed, "com.example.lib").Reason);
    }

    [Fact]
    public void AUserDenyDoesNotStopAHardDependencyButSaysSoInTheWarning()
    {
        var policy = Load(SmallDefault, """{ "schema": 1, "deny": [ "com.example.lib" ] }""");

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.1", "com.example.lib"),
            Unit("com.example.lib", "Lib", "1.0.0"),
        });

        Assert.Empty(decision.Excluded);
        Assert.Equal("dependency-of:me.sol.sain", Find(decision.Allowed, "com.example.lib").Reason);
        var warning = Assert.Single(decision.Warnings);
        Assert.StartsWith("Lib: pulled in as hard dependency of SAIN", warning);
        Assert.Contains("deny", warning);
    }

    [Fact]
    public void ADependencyOnAUserDeniedUnitDoesNotRescueAnUnrelatedDeniedUnit()
    {
        var policy = Load(SmallDefault, """{ "schema": 1, "deny": [ "com.example.other" ] }""");

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.1"),
            Unit("com.example.other", "Other", "1.0.0"),
        });

        Assert.Equal("denied-by-user", Assert.Single(decision.Excluded).Reason);
    }

    // ------------------------------------------------------------------ decision: unlisted mode (blacklist)

    private const string BlacklistUser = """{ "schema": 1, "unlisted": "allow", "deny": [ "com.example.bad" ] }""";

    [Fact]
    public void BlacklistModeAllowsEveryUnitTheUserDidNotDenyAndNamesOnlyTheDeniedOnes()
    {
        var policy = Load(SmallDefault, BlacklistUser);
        var installed = new[]
        {
            Unit("com.SPT.core", "SPT.Core", "4.1.6"),
            Unit("com.maschine.ArenaMode", "ArenaMode", "0.1.3"),
            Unit("me.sol.sain", "SAIN", "4.5.1"),
            Unit("com.maschine.UiBridge", "UiBridge", "0.9.4"),
            Unit("com.example.looting", "Looting Bots", "1.8.0"),
            Unit("com.example.bad", "Bad Mod", "2.0.0"),
            Unit("com.fika.core", "Fika", "2.0.0"),
            Unit("com.example.unversioned", "Unversioned", ""),
            Unit("", "No Guid", "1.0.0"),
        };

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, installed);

        Assert.Equal(
            new[] { "com.SPT.core", "com.maschine.ArenaMode", "me.sol.sain", "com.maschine.UiBridge", "com.example.looting", "com.fika.core", "com.example.unversioned", "" },
            decision.Allowed.Select(u => u.Guid).ToArray());
        Assert.Equal(
            new[] { "platform", "arena", "whitelist", "tool", "unlisted-allowed", "platform", "unlisted-allowed", "unlisted-allowed" },
            decision.Allowed.Select(u => u.Reason).ToArray());
        var excluded = Assert.Single(decision.Excluded);
        Assert.Equal("com.example.bad", excluded.Guid);
        Assert.Equal("denied-by-user", excluded.Reason);
        Assert.Equal("Bad Mod", decision.DescribeExcluded());
        Assert.True(decision.HasExclusions);
        Assert.Empty(decision.Warnings);
        Assert.Equal("unlisted-allowed", ArenaWhitelistDecision.ReasonUnlistedAllowed);
    }

    [Fact]
    public void BlacklistModeWithNothingDeniedExcludesNothing()
    {
        var policy = Load(SmallDefault, """{ "schema": 1, "unlisted": "allow" }""");

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, new[]
        {
            Unit("com.example.one", "One", "1.0.0"),
            Unit("com.example.two", "Two", "2.0.0", "com.example.one"),
        });

        Assert.Empty(decision.Excluded);
        Assert.False(decision.HasExclusions);
        Assert.Empty(decision.Warnings);
        Assert.All(decision.Allowed, u => Assert.Equal("unlisted-allowed", u.Reason));
    }

    [Fact]
    public void WhitelistModeNeverProducesTheUnlistedAllowedReason()
    {
        var installed = new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.1", "com.example.lib"),
            Unit("com.example.lib", "Lib", "1.0.0"),
            Unit("com.example.looting", "Looting Bots", "1.8.0"),
            Unit("com.example.bad", "Bad Mod", "2.0.0"),
            Unit("", "No Guid", "1.0.0"),
        };

        var users = new (string? Json, string BadReason)[]
        {
            (null, "not-on-whitelist"),
            ("""{ "schema": 1 }""", "not-on-whitelist"),
            ("""{ "schema": 1, "unlisted": "deny", "deny": [ "com.example.bad" ] }""", "denied-by-user"),
        };

        foreach (var (user, badReason) in users)
        {
            foreach (var side in new[] { ArenaWhitelistSide.Client, ArenaWhitelistSide.Server })
            {
                var decision = ArenaWhitelistDecision.Evaluate(Load(SmallDefault, user), side, installed);

                Assert.DoesNotContain(decision.Allowed, u => u.Reason == "unlisted-allowed");
                Assert.Equal(new[] { "me.sol.sain", "com.example.lib" }, decision.Allowed.Select(u => u.Guid).ToArray());
                Assert.Equal(new[] { "com.example.looting", "com.example.bad", "" }, decision.Excluded.Select(u => u.Guid).ToArray());
                Assert.Equal(new[] { "not-on-whitelist", badReason, "not-on-whitelist" }, decision.Excluded.Select(u => u.Reason).ToArray());
            }
        }
    }

    [Fact]
    public void ADenyExcludesAUnitTheDefaultListNamesInBlacklistModeToo()
    {
        var policy = Load(SmallDefault, """{ "schema": 1, "unlisted": "allow", "deny": [ "me.sol.sain", "COM.MASCHINE.UIBRIDGE" ] }""");

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.1"),
            Unit("com.maschine.UiBridge", "UiBridge", "0.9.4"),
            Unit("com.acidphantasm.progressivebotsystem", "APBS", "2.4.1"),
        });

        Assert.Equal(new[] { "me.sol.sain", "com.maschine.UiBridge" }, decision.Excluded.Select(u => u.Guid).ToArray());
        Assert.All(decision.Excluded, u => Assert.Equal("denied-by-user", u.Reason));
        Assert.Equal("whitelist", Assert.Single(decision.Allowed).Reason);
    }

    [Fact]
    public void BlacklistModeKeepsVersionWarningsForNamedUnitsOnly()
    {
        var policy = Load(SmallDefault, """
            { "schema": 1, "unlisted": "allow", "allow": [ { "guid": "com.example.reviewed", "clientVersion": "1.0.0" } ] }
            """);

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.2"),
            Unit("com.example.reviewed", "Reviewed", "2.0.0"),
            Unit("com.example.unlisted", "Unlisted", "9.9.9"),
            Unit("com.example.garbage", "Garbage", "not-a-version"),
            Unit("com.example.noversion", "No Version", ""),
        });

        Assert.Empty(decision.Excluded);
        Assert.Equal(
            new[]
            {
                "SAIN: version 4.5.2 differs from reviewed 4.5.1",
                "Reviewed: version 2.0.0 differs from reviewed 1.0.0",
            },
            decision.Warnings.ToArray());
        Assert.Equal(new[] { "whitelist", "whitelist", "unlisted-allowed", "unlisted-allowed", "unlisted-allowed" },
            decision.Allowed.Select(u => u.Reason).ToArray());
    }

    [Fact]
    public void BlacklistModeStillPullsADeniedHardDependencyInAndSaysSo()
    {
        var policy = Load(SmallDefault, """
            { "schema": 1, "unlisted": "allow", "deny": [ "com.example.lib", "com.example.leaf", "com.example.sainlib", "com.example.other" ] }
            """);

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, new[]
        {
            Unit("com.example.foreign", "Foreign", "1.0.0", "com.example.lib"),   // unlisted, allowed
            Unit("com.example.lib", "Lib", "1.0.0", "com.example.leaf"),          // denied, needed by Foreign
            Unit("com.example.leaf", "Leaf", "1.0.0"),                            // denied, needed by Lib
            Unit("me.sol.sain", "SAIN", "4.5.1", "com.example.sainlib"),          // listed, allowed
            Unit("com.example.sainlib", "Sain Lib", "1.0.0"),                     // denied, needed by SAIN
            Unit("com.example.other", "Other", "1.0.0"),                          // denied, needed by nobody
        });

        Assert.Equal("unlisted-allowed", Find(decision.Allowed, "com.example.foreign").Reason);
        Assert.Equal("dependency-of:com.example.foreign", Find(decision.Allowed, "com.example.lib").Reason);
        Assert.Equal("dependency-of:com.example.lib", Find(decision.Allowed, "com.example.leaf").Reason);
        Assert.Equal("dependency-of:me.sol.sain", Find(decision.Allowed, "com.example.sainlib").Reason);
        Assert.Equal("denied-by-user", Assert.Single(decision.Excluded).Reason);
        Assert.Equal("com.example.other", decision.Excluded[0].Guid);
        Assert.Equal(
            new[]
            {
                "Lib: pulled in as hard dependency of Foreign (overrides the user's deny)",
                "Leaf: pulled in as hard dependency of Lib (overrides the user's deny)",
                "Sain Lib: pulled in as hard dependency of SAIN (overrides the user's deny)",
            },
            decision.Warnings.ToArray());
    }

    [Fact]
    public void ServerModsKeepADeniedLibraryTheirModDependenciesNeed()
    {
        // The server's mod validator drops every mod when one ModDependencies entry is missing, so a denied
        // library that an allowed server mod lists must stay in the session tree, like on the client.
        var policy = Load(SmallDefault, """
            { "schema": 1, "unlisted": "allow", "deny": [ "com.example.lib", "com.example.unneeded" ] }
            """);

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Server, new[]
        {
            Unit("com.example.content", "Content Pack", "1.0.0", "com.example.lib"),   // unlisted, allowed
            Unit("com.example.lib", "Lib", "3.0.6"),                                   // denied, needed
            Unit("com.example.unneeded", "Unneeded", "1.0.0"),                         // denied, needed by nobody
        });

        Assert.Equal("unlisted-allowed", Find(decision.Allowed, "com.example.content").Reason);
        Assert.Equal("dependency-of:com.example.content", Find(decision.Allowed, "com.example.lib").Reason);
        Assert.Equal("com.example.unneeded", Assert.Single(decision.Excluded).Guid);
        Assert.Equal("Lib: pulled in as hard dependency of Content Pack (overrides the user's deny)", Assert.Single(decision.Warnings));
    }

    [Fact]
    public void ServerModsKeepAnUnlistedLibraryTheirModDependenciesNeedUnderAWhitelist()
    {
        var policy = Load(SmallDefault, """{ "schema": 1, "allow": [ { "guid": "com.example.content" } ] }""");

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Server, new[]
        {
            Unit("com.example.content", "Content Pack", "1.0.0", "com.example.lib"),
            Unit("com.example.lib", "Lib", "3.0.6"),
        });

        Assert.Empty(decision.Excluded);
        Assert.Equal("dependency-of:com.example.content", Find(decision.Allowed, "com.example.lib").Reason);
    }

    [Fact]
    public void ADeniedUnitDoesNotPullItsOwnDeniedDependenciesIn()
    {
        var policy = Load(SmallDefault, """
            { "schema": 1, "unlisted": "allow", "deny": [ "com.example.top", "com.example.below" ] }
            """);

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, new[]
        {
            Unit("com.example.top", "Top", "1.0.0", "com.example.below"),
            Unit("com.example.below", "Below", "1.0.0"),
        });

        Assert.Empty(decision.Allowed);
        Assert.Equal(new[] { "com.example.top", "com.example.below" }, decision.Excluded.Select(u => u.Guid).ToArray());
        Assert.All(decision.Excluded, u => Assert.Equal("denied-by-user", u.Reason));
        Assert.Empty(decision.Warnings);
    }

    [Fact]
    public void BlacklistModeDecidesServerModsTheSameWayIncludingUnitsOnlyTheClientNames()
    {
        var policy = Load(SmallDefault, BlacklistUser);

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Server, new[]
        {
            Unit("com.maschine.ArenaMode", "ArenaMode", "0.1.3"),
            Unit("me.sol.sain", "SAIN", "4.5.1"),
            Unit("com.acidphantasm.progressivebotsystem", "APBS", "2.4.0"),
            Unit("com.SPT.core", "SPT.Core", "4.1.6"),               // platform entry, client only: unlisted here
            Unit("com.example.serverthing", "Server Thing", "1.0.0"),
            Unit("com.example.bad", "Bad Server Mod", "1.0.0"),
        });

        Assert.Equal(
            new[] { "arena", "whitelist", "whitelist", "unlisted-allowed", "unlisted-allowed" },
            decision.Allowed.Select(u => u.Reason).ToArray());
        Assert.Equal("com.example.bad", Assert.Single(decision.Excluded).Guid);
        Assert.Equal("denied-by-user", decision.Excluded[0].Reason);
        Assert.Equal("APBS: version 2.4.0 differs from reviewed 2.4.1", Assert.Single(decision.Warnings));
    }

    [Fact]
    public void ADenyHoldsOnTheSideItWasNamedForAndOnTheOtherOneAlike()
    {
        // One GUID names a mod on both sides, so the deny list cannot treat the sides differently.
        var policy = Load(SmallDefault, BlacklistUser);
        var bad = new[] { Unit("com.example.bad", "Bad", "1.0.0") };

        Assert.Equal("denied-by-user", Assert.Single(ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, bad).Excluded).Reason);
        Assert.Equal("denied-by-user", Assert.Single(ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Server, bad).Excluded).Reason);
    }

    [Fact]
    public void ServerSideDecisionUsesServerReviewedVersionsAndAllowsNoClientPlatformUnits()
    {
        var policy = Load(SmallDefault);
        var installed = new[]
        {
            Unit("com.maschine.ArenaMode", "ArenaMode", "0.1.3"),
            Unit("me.sol.sain", "SAIN", "4.5.1"),
            Unit("com.acidphantasm.progressivebotsystem", "APBS", "2.4.0"),
            Unit("com.SPT.core", "SPT.Core", "4.1.6"),
            Unit("com.fika.core", "Fika", "2.0.0"),
            Unit("com.example.serverthing", "Server Thing", "1.0.0"),
        };

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Server, installed);

        Assert.Equal(new[] { "arena", "whitelist", "whitelist" }, decision.Allowed.Select(u => u.Reason).ToArray());
        Assert.Equal(
            new[] { "com.SPT.core", "com.fika.core", "com.example.serverthing" },
            decision.Excluded.Select(u => u.Guid).ToArray());
        Assert.All(decision.Excluded, u => Assert.Equal("not-on-whitelist", u.Reason));
        Assert.Equal("APBS: version 2.4.0 differs from reviewed 2.4.1", Assert.Single(decision.Warnings));
        Assert.Equal("SPT.Core, Fika, Server Thing", decision.DescribeExcluded());
    }

    [Fact]
    public void SameUnitsDecideDifferentlyPerSideWhereVersionsAreOnlyReviewedForOne()
    {
        var policy = Load(SmallDefault);
        var apbs = Unit("com.acidphantasm.progressivebotsystem", "APBS", "9.9.9");

        var client = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, new[] { apbs });
        var server = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Server, new[] { apbs });

        Assert.Empty(client.Warnings);
        Assert.Single(server.Warnings);
        Assert.Single(client.Allowed);
        Assert.Single(server.Allowed);
    }

    [Fact]
    public void DecisionKeepsInputOrderDoesNotTouchItsInputAndHandlesDuplicatesAndNulls()
    {
        var policy = Load(SmallDefault);
        var sain = Unit("me.sol.sain", "SAIN", "4.5.1", "com.example.lib");
        var sainTwin = Unit("ME.SOL.SAIN", "SAIN copy", "4.5.1");
        var installed = new List<ArenaSessionUnit>
        {
            Unit("z.unknown", "Z", "1"),
            sain,
            null!,
            Unit("com.example.lib", "Lib", "1"),
            sainTwin,
            Unit("a.unknown", "A", "1"),
        };

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, installed);

        Assert.Equal(new[] { "me.sol.sain", "com.example.lib", "ME.SOL.SAIN" }, decision.Allowed.Select(u => u.Guid).ToArray());
        Assert.Equal(new[] { "z.unknown", "a.unknown" }, decision.Excluded.Select(u => u.Guid).ToArray());

        // The inputs are untouched: no reason written into them, no list mutated.
        Assert.All(installed.Where(u => u != null), u => Assert.Null(u.Reason));
        Assert.Equal(new[] { "com.example.lib" }, sain.HardDependencies.ToArray());
        Assert.NotSame(sain, decision.Allowed[0]);
    }

    [Fact]
    public void DecisionHandlesEmptyInputAndRejectsANullPolicy()
    {
        var policy = Load(SmallDefault);

        var empty = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, null!);
        Assert.Empty(empty.Allowed);
        Assert.Empty(empty.Excluded);
        Assert.Empty(empty.Warnings);

        Assert.Throws<ArgumentNullException>(() =>
            ArenaWhitelistDecision.Evaluate(null!, ArenaWhitelistSide.Client, Array.Empty<ArenaSessionUnit>()));
    }

    // ------------------------------------------------------------------ hash

    [Fact]
    public void PolicyHashIsStableAndIgnoresEntryOrderAndDisplayNames()
    {
        var a = Load(SmallDefault);
        var again = Load(SmallDefault);
        var reorderedAndRenamed = Load("""
            {
              "mods": [
                { "patcher": "maschine-LogTools.dll", "kind": "tool", "name": "other name" },
                { "kind": "tool", "guid": "com.maschine.UiBridge" },
                { "serverVersion": "2.4.1", "kind": "mod", "guid": "com.acidphantasm.progressivebotsystem", "name": "Renamed" },
                { "serverVersion": "4.5.1", "clientVersion": "4.5.1", "guid": "me.sol.sain", "kind": "mod" }
              ],
              "schema": 1,
              "ignored": "unknown members are tolerated"
            }
            """);

        Assert.Matches("^[0-9a-f]{16}$", a.Hash);
        Assert.Equal(a.Hash, again.Hash);
        Assert.Equal(a.Hash, reorderedAndRenamed.Hash);
    }

    [Fact]
    public void PolicyHashIsOrderIndependentForUserEntriesToo()
    {
        var one = Load(EmptyDefault, """
            { "schema": 1, "allow": [ { "guid": "com.example.a" }, { "guid": "com.example.b", "clientVersion": "1.0" } ] }
            """);
        var two = Load(EmptyDefault, """
            { "schema": 1, "allow": [ { "guid": "com.example.b", "clientVersion": "1.0" }, { "guid": "COM.EXAMPLE.A" } ] }
            """);

        Assert.Equal(one.Hash, two.Hash);
    }

    [Fact]
    public void PolicyHashChangesWithEveryPartOfTheEffectivePolicy()
    {
        string baseline = Load(SmallDefault).Hash;
        var seen = new HashSet<string> { baseline };

        void MustDiffer(string label, ArenaWhitelistPolicy changed)
        {
            Assert.True(seen.Add(changed.Hash), label + " must change the hash");
        }

        MustDiffer("an additional allowed unit", Load(SmallDefault, """{ "schema": 1, "allow": [ { "guid": "com.example.x" } ] }"""));
        MustDiffer("an additional allowed patcher", Load(SmallDefault, """{ "schema": 1, "allow": [ { "patcher": "x.dll" } ] }"""));
        MustDiffer("a denied default unit", Load(SmallDefault, """{ "schema": 1, "deny": [ "me.sol.sain" ] }"""));
        MustDiffer("a denied default patcher", Load(SmallDefault, """{ "schema": 1, "deny": [ "maschine-LogTools.dll" ] }"""));
        MustDiffer("another client version", Load(SmallDefault.Replace("\"clientVersion\": \"4.5.1\"", "\"clientVersion\": \"4.5.2\"")));
        MustDiffer("another server version", Load(SmallDefault.Replace("\"serverVersion\": \"2.4.1\"", "\"serverVersion\": \"2.4.2\"")));
        MustDiffer("another kind", Load(SmallDefault.Replace("\"name\": \"UiBridge\", \"kind\": \"tool\"", "\"name\": \"UiBridge\", \"kind\": \"mod\"")));
        MustDiffer("an empty default", Load(EmptyDefault));
        MustDiffer("a removed server version", Load(SmallDefault.Replace(", \"serverVersion\": \"2.4.1\"", "")));
        MustDiffer("blacklist mode", Load(SmallDefault, """{ "schema": 1, "unlisted": "allow" }"""));
        MustDiffer("a denied unit in blacklist mode", Load(SmallDefault, """{ "schema": 1, "unlisted": "allow", "deny": [ "com.example.x" ] }"""));
        MustDiffer("another denied unit in blacklist mode", Load(SmallDefault, """{ "schema": 1, "unlisted": "allow", "deny": [ "com.example.y" ] }"""));
        MustDiffer("a denied patcher in blacklist mode", Load(SmallDefault, """{ "schema": 1, "unlisted": "allow", "deny": [ "x.dll" ] }"""));
    }

    [Fact]
    public void PolicyHashOfAWhitelistIsNotChangedByTheUnlistedFeature()
    {
        string baseline = Load(SmallDefault).Hash;

        // The mode written out, or a deny nothing lists, is the same policy: the same hash.
        Assert.Equal(baseline, Load(SmallDefault, """{ "schema": 1, "unlisted": "deny" }""").Hash);
        Assert.Equal(baseline, Load(SmallDefault, """{ "schema": 1, "deny": [ "com.example.unlisted" ] }""").Hash);
        Assert.Equal(baseline, Load(SmallDefault, """{ "schema": 1, "unlisted": "deny", "deny": [ "com.example.unlisted", "unlisted.dll" ] }""").Hash);
    }

    [Fact]
    public void PolicyHashOfABlacklistIgnoresOrderCaseAndThePathFormOfDeniedNames()
    {
        string reference = Load(SmallDefault, """
            { "schema": 1, "unlisted": "allow", "deny": [ "com.example.a", "com.example.b", "x.dll" ] }
            """).Hash;

        Assert.Equal(reference, Load(SmallDefault, """
            { "schema": 1, "deny": [ "BepInEx/patchers/X.DLL", "COM.EXAMPLE.B", "com.example.a", "com.example.a" ], "unlisted": "ALLOW" }
            """).Hash);
        Assert.NotEqual(reference, Load(SmallDefault, """
            { "schema": 1, "unlisted": "allow", "deny": [ "com.example.a", "com.example.b" ] }
            """).Hash);
        Assert.NotEqual(reference, Load(SmallDefault, """
            { "schema": 1, "unlisted": "allow", "deny": [ "com.example.a", "com.example.b", "x.dll", "y.dll" ] }
            """).Hash);
    }

    [Fact]
    public void PolicyHashHasAPinnedValueForTheCodeDefinedPolicy()
    {
        // Platform and Arena units are part of the hash. Change them, or the canonical text, and
        // the client, the helper and the arena server stop agreeing across builds: update this
        // value only on purpose, together with the spec.
        Assert.Equal("3755e0db97983440", Load(EmptyDefault).Hash);
    }

    [Fact]
    public void PolicyHashHasAPinnedValueForTheBlacklistCanonicalText()
    {
        // The unlisted line and the denied lines are part of the canonical text. Same rule as the
        // pinned whitelist hash above: change this value only on purpose, together with the spec.
        Assert.Equal("d909483c706fa307", Load(EmptyDefault, """{ "schema": 1, "unlisted": "allow" }""").Hash);
        Assert.Equal("660edbd88b08c0e4", Load(EmptyDefault, """{ "schema": 1, "unlisted": "allow", "deny": [ "com.example.bad", "Bad.dll" ] }""").Hash);
    }

    [Fact]
    public void EntriesAreListedByTierThenKindThenIdAndCannotBeChangedByTheCaller()
    {
        var policy = Load(SmallDefault, """{ "schema": 1, "allow": [ { "guid": "aaa.first" }, { "patcher": "zzz.dll" } ] }""");

        var order = policy.Entries.Select(e => (e.Tier, e.IsPatcher, e.Id)).ToList();
        var sorted = order.OrderBy(x => (int)x.Tier).ThenBy(x => x.IsPatcher)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.Equal(sorted, order);
        Assert.Equal(ArenaWhitelistTier.Platform, policy.Entries[0].Tier);
        Assert.Equal("com.maschine.ArenaMode", policy.Entries.Single(e => e.Tier == ArenaWhitelistTier.Arena).Id);
        Assert.Throws<NotSupportedException>(() => ((IList<ArenaWhitelistEntry>)policy.Entries).Clear());
    }

    // ------------------------------------------------------------------ gate report

    private static ArenaGateReport SampleReport()
    {
        var report = new ArenaGateReport
        {
            Active = true,
            Error = null!,
            PolicyHash = "0123456789abcdef",
        };
        report.Allowed.Add(new ArenaSessionUnit("me.sol.sain", "SAIN", "4.5.1", @"D:\SPT41\BepInEx\plugins\SAIN\SAIN.dll", null!)
            { Reason = "whitelist" });
        report.Allowed.Add(new ArenaSessionUnit("xyz.drakia.bigbrain", "Big\"Brain\u00e9", "1.5.0", "DrakiaXYZ-BigBrain.dll", null!)
            { Reason = "dependency-of:me.sol.sain" });
        report.Excluded.Add(new ArenaSessionUnit("com.example.looting", "Looting Bots", "1.8.0", "LootingBots.dll", null!)
            { Reason = "not-on-whitelist" });
        report.Excluded.Add(new ArenaSessionUnit("com.example.nullish", null!, null!, null!, null!) { Reason = "denied-by-user" });
        report.Warnings.Add("SAIN: version 4.5.2 differs from reviewed 4.5.1");
        report.Warnings.Add("tricky \"warning\" with \\ and \n newline");
        report.PatchersNeutralised.Add("WTT-ContentBackportPatcher.dll");
        return report;
    }

    [Fact]
    public void GateReportRoundTripsThroughJson()
    {
        var original = SampleReport();

        string json = original.ToJson();
        Assert.True(ArenaGateReport.TryParse(json, out var parsed, out var error), error);

        Assert.NotNull(parsed);
        Assert.Equal(1, parsed!.Schema);
        Assert.True(parsed.Active);
        Assert.Null(parsed.Error);
        Assert.Equal("0123456789abcdef", parsed.PolicyHash);
        Assert.Equal(original.Warnings, parsed.Warnings);
        Assert.Equal(original.PatchersNeutralised, parsed.PatchersNeutralised);

        void AssertUnits(List<ArenaSessionUnit> want, List<ArenaSessionUnit> got)
        {
            Assert.Equal(want.Count, got.Count);
            for (int i = 0; i < want.Count; i++)
            {
                Assert.Equal(want[i].Guid, got[i].Guid);
                Assert.Equal(want[i].Name, got[i].Name);
                Assert.Equal(want[i].Version, got[i].Version);
                Assert.Equal(want[i].File, got[i].File);
                Assert.Equal(want[i].Reason, got[i].Reason);
            }
        }

        AssertUnits(original.Allowed, parsed.Allowed);
        AssertUnits(original.Excluded, parsed.Excluded);

        // And it is stable: writing the parsed report gives the same text.
        Assert.Equal(json, parsed.ToJson());
    }

    [Fact]
    public void GateReportHasTheDocumentedJsonShape()
    {
        var report = SampleReport();
        Assert.True(ArenaMiniJson.TryParse(report.ToJson(), out var root, out var error), error);
        var obj = Assert.IsType<Dictionary<string, object>>(root);

        Assert.Equal(
            new[] { "schema", "active", "error", "policyHash", "allowed", "excluded", "warnings", "patchersNeutralised" },
            obj.Keys.ToArray());
        Assert.Equal(1d, obj["schema"]);
        Assert.Equal(true, obj["active"]);
        Assert.Null(obj["error"]);
        Assert.Equal("0123456789abcdef", obj["policyHash"]);

        var allowed = Assert.IsType<List<object>>(obj["allowed"]);
        var first = Assert.IsType<Dictionary<string, object>>(allowed[0]);
        Assert.Equal(new[] { "guid", "name", "version", "file", "reason" }, first.Keys.ToArray());
        Assert.Equal("whitelist", first["reason"]);
        Assert.IsType<List<object>>(obj["warnings"]);
        Assert.IsType<List<object>>(obj["patchersNeutralised"]);
    }

    [Fact]
    public void AnInactiveReportCarriesTheErrorAndNothingElse()
    {
        var report = ArenaGateReport.Inactive("policy file missing", "feedfacecafebeef");

        Assert.True(ArenaGateReport.TryParse(report.ToJson(), out var parsed, out var error), error);

        Assert.False(parsed!.Active);
        Assert.Equal("policy file missing", parsed.Error);
        Assert.Equal("feedfacecafebeef", parsed.PolicyHash);
        Assert.Empty(parsed.Allowed);
        Assert.Empty(parsed.Excluded);
        Assert.Empty(parsed.Warnings);
        Assert.Empty(parsed.PatchersNeutralised);

        var noHash = ArenaGateReport.Inactive("boom", null!);
        Assert.True(ArenaGateReport.TryParse(noHash.ToJson(), out parsed, out error), error);
        Assert.Null(parsed!.PolicyHash);
    }

    [Fact]
    public void GateReportParsingRejectsGarbageWrongSchemaAndWrongShapeButToleratesUnknownMembers()
    {
        foreach (string bad in new[] { "", "{", "[]", "null", "\"x\"", """{"active":true}""", """{"schema":2,"active":true}""", """{"schema":"1"}""" })
        {
            bool parsed = ArenaGateReport.TryParse(bad, out var report, out var error);
            Assert.False(parsed, bad);
            Assert.Null(report);
            Assert.False(string.IsNullOrEmpty(error), bad);
        }

        Assert.True(ArenaGateReport.TryParse(
            """{"schema":1,"active":true,"future":[1,2],"allowed":[{"guid":"a","extra":1},"junk",{"guid":"b"}],"warnings":["w",3,null]}""",
            out var tolerant, out _));
        Assert.True(tolerant!.Active);
        Assert.Equal(new[] { "a", "b" }, tolerant.Allowed.Select(u => u.Guid).ToArray());
        Assert.Equal(new[] { "w" }, tolerant.Warnings.ToArray());
        Assert.Empty(tolerant.Excluded);
    }

    [Fact]
    public void GateReportFromADecisionCopiesItAndCountsLikeTheLogLine()
    {
        var policy = Load(SmallDefault);
        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.2"),
            Unit("com.maschine.ArenaMode", "ArenaMode", "0.1.3"),
            Unit("com.example.other", "Other", "1.0.0"),
        });

        var report = ArenaGateReport.FromDecision(policy, decision, new[] { "Some.Patcher.dll" });

        Assert.True(report.Active);
        Assert.Null(report.Error);
        Assert.Equal(policy.Hash, report.PolicyHash);
        Assert.Equal(new[] { "me.sol.sain", "com.maschine.ArenaMode" }, report.Allowed.Select(u => u.Guid).ToArray());
        Assert.Equal(new[] { "com.example.other" }, report.Excluded.Select(u => u.Guid).ToArray());
        Assert.Single(report.Warnings);
        Assert.Equal(new[] { "Some.Patcher.dll" }, report.PatchersNeutralised.ToArray());
        Assert.Equal("Arena session: 2 plugins allowed, 1 excluded, 1 warnings", report.SummaryLine());

        decision.Allowed.Clear();
        Assert.Equal(2, report.Allowed.Count);

        Assert.Throws<ArgumentNullException>(() => ArenaGateReport.FromDecision(null!, decision, null!));
        Assert.Throws<ArgumentNullException>(() => ArenaGateReport.FromDecision(policy, null!, null!));
    }

    [Fact]
    public void AReportOfABlacklistPolicyCarriesTheUnlistedAllowedAndDeniedReasonsThroughJson()
    {
        var policy = Load(SmallDefault, BlacklistUser);
        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, new[]
        {
            Unit("com.example.other", "Other", "1.0.0"),
            Unit("com.example.bad", "Bad", "1.0.0"),
        });

        var report = ArenaGateReport.FromDecision(policy, decision, null!);
        Assert.True(ArenaGateReport.TryParse(report.ToJson(), out var parsed, out var error), error);

        Assert.Equal(policy.Hash, parsed!.PolicyHash);
        Assert.Equal("unlisted-allowed", Assert.Single(parsed.Allowed).Reason);
        Assert.Equal("denied-by-user", Assert.Single(parsed.Excluded).Reason);
        Assert.Empty(parsed.Warnings);
        Assert.Equal("Arena session: 1 plugins allowed, 1 excluded, 0 warnings", parsed.SummaryLine());
    }

    // ------------------------------------------------------------------ contract

    [Fact]
    public void ContractConstantsMatchTheSpec()
    {
        Assert.Equal("-arena-session", ArenaSessionContract.CommandLineFlag);
        Assert.Equal("com.maschine.ArenaMode.Gate.Report", ArenaSessionContract.GateReportDataKey);
        Assert.Equal(1, ArenaSessionContract.SchemaVersion);
        Assert.Equal(6970, ArenaSessionContract.ArenaPort);
        Assert.Equal(6971, ArenaSessionContract.FallbackPortFirst);
        Assert.Equal(6979, ArenaSessionContract.FallbackPortLast);
        Assert.Equal("arena-session.json", ArenaSessionContract.MarkerFileName);
        Assert.Equal("request.json", ArenaSessionContract.RequestFileName);
        Assert.Equal("helper.log", ArenaSessionContract.HelperLogFileName);
        Assert.Equal("server.pid", ArenaSessionContract.ServerPidFileName);
        Assert.Equal("maschine-ArenaMode.Session.exe", ArenaSessionContract.HelperFileName);
        Assert.Equal("maschine-ArenaMode.Gate.dll", ArenaSessionContract.GateFileName);
        Assert.Equal("arena-whitelist.json", ArenaSessionContract.DefaultPolicyFileName);
        Assert.Equal("com.maschine.ArenaMode.whitelist.json", ArenaSessionContract.UserPolicyFileName);
        Assert.Equal("Local\\maschine-ArenaMode.Session", ArenaSessionContract.HelperMutexName);
        Assert.Equal("enter", ArenaSessionContract.DirectionEnter);
        Assert.Equal("leave", ArenaSessionContract.DirectionLeave);
        Assert.Equal("com.maschine.ArenaMode", ArenaSessionContract.ArenaPluginGuid);
        Assert.Equal(
            new[] { "FixPluginTypesSerialization.dll", "spt-prepatch.dll", "maschine-ArenaMode.Gate.dll" },
            ArenaSessionContract.PlatformPatcherFiles.ToArray());
        Assert.Equal(
            new[] { "com.SPT.core", "com.SPT.custom", "com.SPT.debugging", "com.SPT.singleplayer" },
            ArenaSessionContract.SptPlatformGuids.ToArray());
    }

    [Fact]
    public void TheSessionFlagIsRecognisedCaseInsensitivelyAndOnlyAsAWholeArgument()
    {
        Assert.True(ArenaSessionContract.HasSessionFlag(new[] { @"C:\SPT41\EscapeFromTarkov.exe", "-force-gfx-jobs", "native", "-arena-session" }));
        Assert.True(ArenaSessionContract.HasSessionFlag(new[] { "game.exe", "-Arena-Session" }));
        Assert.True(ArenaSessionContract.HasSessionFlag(new[] { "game.exe", "-ARENA-SESSION" }));

        Assert.False(ArenaSessionContract.HasSessionFlag(new[] { "game.exe", "-token=abc", "-config={\"BackendUrl\":\"https://127.0.0.1:6969\"}" }));
        Assert.False(ArenaSessionContract.HasSessionFlag(new[] { "game.exe", "-arena-session2" }));
        Assert.False(ArenaSessionContract.HasSessionFlag(new[] { "game.exe", "--arena-session" }));
        Assert.False(ArenaSessionContract.HasSessionFlag(new[] { "game.exe", "arena-session" }));
        Assert.False(ArenaSessionContract.HasSessionFlag(new[] { "game.exe", "-arena" }));
        Assert.False(ArenaSessionContract.HasSessionFlag(new[] { "-arena-session=1" }));
        Assert.False(ArenaSessionContract.HasSessionFlag(Array.Empty<string>()));
        Assert.False(ArenaSessionContract.HasSessionFlag(null!));
        Assert.False(ArenaSessionContract.HasSessionFlag(new string[] { null! }));
    }

    [Fact]
    public void RuntimePathsLiveBelowLocalAppDataOrAreNull()
    {
        string? local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local))
        {
            Assert.Null(ArenaSessionContract.GetRuntimeRoot());
            Assert.Null(ArenaSessionContract.GetServerTreePath());
            Assert.Null(ArenaSessionContract.GetRequestFilePath());
            Assert.Null(ArenaSessionContract.GetHelperLogPath());
            return;
        }

        string root = Path.Combine(local, "maschine-ArenaMode", "session");
        Assert.Equal(root, ArenaSessionContract.GetRuntimeRoot());
        Assert.Equal(Path.Combine(root, "server"), ArenaSessionContract.GetServerTreePath());
        Assert.Equal(Path.Combine(root, "request.json"), ArenaSessionContract.GetRequestFilePath());
        Assert.Equal(Path.Combine(root, "helper.log"), ArenaSessionContract.GetHelperLogPath());
        Assert.True(Path.IsPathRooted(ArenaSessionContract.GetRuntimeRoot()!));
    }

    // ------------------------------------------------------------------ the pieces together

    [Fact]
    public void ShippedPolicyKeepsSainAndFriendsAndExcludesATypicalForeignMod()
    {
        var policy = Load(File.ReadAllText(ShippedDefaultPath));
        var installed = new[]
        {
            Unit("com.SPT.core", "SPT.Core", "4.1.6"),
            Unit("com.SPT.custom", "SPT.Custom", "4.1.6"),
            Unit("com.SPT.debugging", "SPT.Debugging", "4.1.6"),
            Unit("com.SPT.singleplayer", "SPT.Singleplayer", "4.1.6"),
            Unit("com.bepis.bepinex.configurationmanager", "Configuration Manager", "18.4.1"),
            Unit("com.maschine.ArenaMode", "ArenaMode", "0.1.3"),
            Unit("me.sol.sain", "SAIN", "4.5.1", "xyz.drakia.bigbrain", "xyz.drakia.waypoints"),
            Unit("xyz.drakia.bigbrain", "BigBrain", "1.5.0"),
            Unit("xyz.drakia.waypoints", "Waypoints", "1.9.0"),
            Unit("com.acidphantasm.botplacementsystem", "ABPS", "2.1.0"),
            Unit("com.maschine.UiBridge", "UiBridge", "0.9.4"),
            Unit("com.maschine.Killcam", "Killcam", "1.0.0"),
            Unit("com.tyfon.uifixes", "UIFixes", "2.4.0"),
        };

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, installed);

        Assert.Equal(new[] { "Killcam", "UIFixes" }, decision.Excluded.Select(u => u.DisplayName).ToArray());
        Assert.Equal(11, decision.Allowed.Count);
        Assert.Empty(decision.Warnings);
        Assert.Equal("whitelist", Find(decision.Allowed, "xyz.drakia.bigbrain").Reason);
    }

    [Fact]
    public void ShippedPolicyAsABlacklistKeepsTheReviewedModsAndOnlyExcludesWhatTheUserDenied()
    {
        var policy = Load(File.ReadAllText(ShippedDefaultPath), """
            { "schema": 1, "unlisted": "allow", "deny": [ "com.tyfon.uifixes" ] }
            """);
        var installed = new[]
        {
            Unit("com.SPT.core", "SPT.Core", "4.1.6"),
            Unit("com.maschine.ArenaMode", "ArenaMode", "0.1.3"),
            Unit("me.sol.sain", "SAIN", "4.5.2", "xyz.drakia.bigbrain"),
            Unit("xyz.drakia.bigbrain", "BigBrain", "1.5.0"),
            Unit("com.maschine.Killcam", "Killcam", "1.0.0"),
            Unit("com.tyfon.uifixes", "UIFixes", "2.4.0"),
        };

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, installed);

        Assert.Equal("UIFixes", decision.DescribeExcluded());
        Assert.Equal("denied-by-user", decision.Excluded[0].Reason);
        Assert.Equal("unlisted-allowed", Find(decision.Allowed, "com.maschine.Killcam").Reason);
        Assert.Equal("whitelist", Find(decision.Allowed, "me.sol.sain").Reason);
        Assert.Equal("SAIN: version 4.5.2 differs from reviewed 4.5.1", Assert.Single(decision.Warnings));
    }
}
