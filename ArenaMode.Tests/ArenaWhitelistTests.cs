using System.ComponentModel;
using System.Diagnostics;
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

    /// <summary>
    /// The invariant the "deny wins over hard dependencies" rule exists for: no allowed unit has an installed
    /// hard dependency that is not allowed (a loader drops such a unit; the server's mod validator drops every mod).
    /// </summary>
    private static void AssertNoAllowedUnitNeedsAnExcludedOne(IEnumerable<ArenaSessionUnit> installed, ArenaWhitelistDecision decision)
    {
        var units = installed.ToList();
        var allowed = new HashSet<string>(decision.Allowed.Select(u => u.Guid), StringComparer.OrdinalIgnoreCase);
        foreach (var unit in units.Where(u => allowed.Contains(u.Guid)))
        {
            foreach (string dependency in unit.HardDependencies)
            {
                bool present = units.Any(u => string.Equals(u.Guid, dependency, StringComparison.OrdinalIgnoreCase));
                Assert.True(!present || allowed.Contains(dependency), unit.Guid + " is allowed but needs " + dependency + ", which is not");
            }
        }
    }

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

        // The user's decision of 2026-10-22: every mod installed on the development hosts, with the versions it
        // was installed in, plus the two body-replacement integrations approved after GPU tests on 2026-10-23.
        // Written out by hand, not derived from the file, so that a lost entry shows.
        var expected = new (string Id, string Name, ArenaWhitelistTier Tier, string? Client, string? Server)[]
        {
            ("me.sol.sain", "SAIN", ArenaWhitelistTier.Mod, "4.5.1", "4.5.1"),
            ("com.acidphantasm.progressivebotsystem", "APBS", ArenaWhitelistTier.Mod, null, "2.4.1"),
            ("com.acidphantasm.botplacementsystem", "ABPS", ArenaWhitelistTier.Mod, "2.1.0", "2.1.0"),
            ("xyz.drakia.bigbrain", "BigBrain", ArenaWhitelistTier.Mod, "1.5.0", null),
            ("xyz.drakia.waypoints", "Waypoints", ArenaWhitelistTier.Mod, "1.9.0", null),
            ("com.maschine.ArenaMapPort", "ArenaMapPort", ArenaWhitelistTier.Mod, "0.3.0", "0.3.0"),
            ("com.maschine.Killcam", "Killcam", ArenaWhitelistTier.Mod, "1.0.1", null),
            ("com.maschine.Redeploy", "Redeploy", ArenaWhitelistTier.Mod, "1.0.0", "1.0.0"),
            ("com.maschine.AutoCorpseSearch", "AutoCorpseSearch", ArenaWhitelistTier.Mod, "2.2.0", null),
            ("com.maschine.DefaultAutoFireMode", "DefaultAutoFireMode", ArenaWhitelistTier.Mod, "2.0.0", null),
            ("com.maschine.DualSideDoorBreach", "DualSideDoorBreach", ArenaWhitelistTier.Mod, "2.0.2", null),
            ("com.maschine.LinkedSearchInStash", "LinkedSearchInStash", ArenaWhitelistTier.Mod, "2.0.1", null),
            ("com.maschine.LowAmmoCue", "LowAmmoCue", ArenaWhitelistTier.Mod, "1.0.0", null),
            ("com.maschine.RaidMenuCleanupFix", "RaidMenuCleanupFix", ArenaWhitelistTier.Mod, "2.0.0", null),
            ("com.maschine.StashFilter", "StashFilter", ArenaWhitelistTier.Mod, "1.0.0", null),
            ("com.maschine.SurroundAudio", "SurroundAudio", ArenaWhitelistTier.Mod, "1.0.0", null),
            ("com.maschine.UnloadAllMagazines", "UnloadAllMagazines", ArenaWhitelistTier.Mod, "2.2.1", null),
            ("xyz.pit.fireteam", "PitFireTeam", ArenaWhitelistTier.Mod, "1.0.0", "1.0.0"),
            ("com.mpstark.dynamicmaps", "DynamicMaps", ArenaWhitelistTier.Mod, "1.2.1", "1.2.1"),
            ("com.tyfon.uifixes", "UIFixes", ArenaWhitelistTier.Mod, "6.0.3", "6.0.3"),
            ("com.wtt.commonlib", "WTT-CommonLib", ArenaWhitelistTier.Mod, "3.0.6", "3.0.6"),
            ("com.wtt.contentbackport", "WTT-ContentBackport", ArenaWhitelistTier.Mod, "2.0.5", "2.0.5"),
            ("WTT-ContentBackportPatcher.dll", "WTT-ContentBackport patcher", ArenaWhitelistTier.Mod, null, null),
            ("fika.ghostfenixx.svm", "SVM", ArenaWhitelistTier.Mod, null, "2.2.3"),
            ("com.agavalda.giveui", "give-ui", ArenaWhitelistTier.Mod, null, "5.0.0"),
            ("com.lacyway.ch", "ContinuousHealing", ArenaWhitelistTier.Mod, "1.6.3", null),
            ("com.ozen.continuousloadammo", "Continuous Load Ammo", ArenaWhitelistTier.Mod, "1.2.0", null),
            ("com.pein.camerarecoilmod", "Recoil Rework Legacy", ArenaWhitelistTier.Mod, "2.0.0", null),
            ("com.fontaine.fovfix", "Fontaine FOV Fix", ArenaWhitelistTier.Mod, "4.1.0", null),
            ("com.manimal.loadammoanim", "Manimal Ammo Loading Animations", ArenaWhitelistTier.Mod, "2.0.0", "2.0.0"),
            ("com.terkoiz.freecam", "Freecam", ArenaWhitelistTier.Mod, "1.4.8", null),
            ("com.tarkin.doordash", "DoorDash", ArenaWhitelistTier.Mod, "1.2.0", null),
            ("com.anvil.weboverlay", "Anvil-WebOverlay", ArenaWhitelistTier.Mod, "1.11.0", null),
            ("com.slpf.armorclassicon", "ArmorClassIcon", ArenaWhitelistTier.Mod, "1.0.0", null),
            ("katrin0522.betterammoloadinglist", "BetterAmmoLoadingList", ArenaWhitelistTier.Mod, "1.1.1", null),
            ("7Bpencil.BrighterInteriors", "BrighterInteriors", ArenaWhitelistTier.Mod, "1.1.1", null),
            ("com.acidphantasm.devbalaclavafix", "DevBalaclavaFix", ArenaWhitelistTier.Mod, "1.2.0", null),
            ("HealingAutoCancel", "HealingAutoCancel", ArenaWhitelistTier.Mod, "1.1.0", null),
            ("com.smajlec.lights", "SmajlecLights", ArenaWhitelistTier.Mod, "1.3.0", null),
            ("com.maschine.UiBridge", "UiBridge", ArenaWhitelistTier.Tool, null, null),
            ("com.maschine.ModProfiler", "ModProfiler", ArenaWhitelistTier.Tool, null, null),
            ("com.maschine.LogTools", "LogTools", ArenaWhitelistTier.Tool, null, null),
            ("com.maschine.ConsoleToMonitor", "ConsoleToMonitor", ArenaWhitelistTier.Tool, null, null),
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

    [Theory]
    [InlineData("1.0.0", false)]
    [InlineData("1.0.0.0", false)]
    [InlineData("1.0.0.1", true)]
    [InlineData("1.0.1", true)]
    public void ShippedPftReviewControlsBothNotesAndVersionFilteredSessions(string version, bool differs)
    {
        const string guid = "xyz.pit.fireteam";
        var policy = Load(File.ReadAllText(ShippedDefaultPath));
        var client = new[] { Unit(guid, "PitAlex-PitFireTeam", version) };
        var server = new[] { Unit(guid, "PitFireTeam", version) };
        foreach (var side in new[] { ArenaWhitelistSide.Client, ArenaWhitelistSide.Server })
        {
            var decision = ArenaWhitelistDecision.Evaluate(policy, side,
                side == ArenaWhitelistSide.Client ? client : server);
            Assert.Single(decision.Allowed);
            Assert.Empty(decision.Excluded);
            Assert.Equal(differs ? 1 : 0, decision.VersionDifferences.Count);
            Assert.Equal(differs ? 1 : 0, decision.Warnings.Count);
        }
        var plan = ArenaSessionVersionPlan.Create(policy, client, server);
        Assert.Equal(differs, plan.ExcludedGuids.Contains(guid));
    }

    /// <summary>The mods ArenaMode already refused by code before the whitelist existed (ORBIT, Questing Bots, ...).</summary>
    private static readonly (string Guid, string Name)[] ShippedDenies =
    {
        ("com.chazut.orbit", "ORBIT"),
        ("com.danw.questingbots", "Questing Bots"),
        ("com.hysocs.adaptivebotculling", "Adaptive Bot Culling"),
        ("com.samc137.isbspecialforces", "ISB Special Forces"),
        ("com.samc137.aishi", "Aishi"),
    };

    /// <summary>Installed GUIDs approved on 2026-10-22 plus the two clients approved after GPU testing on 2026-10-23.</summary>
    private static readonly string[] InstalledGuids =
    {
        "me.sol.sain", "com.acidphantasm.progressivebotsystem", "com.acidphantasm.botplacementsystem", "xyz.drakia.bigbrain",
        "xyz.drakia.waypoints", "com.maschine.ArenaMapPort", "com.maschine.Killcam", "com.maschine.Redeploy",
        "com.maschine.AutoCorpseSearch", "com.maschine.DefaultAutoFireMode", "com.maschine.DualSideDoorBreach",
        "com.maschine.LinkedSearchInStash", "com.maschine.LowAmmoCue", "com.maschine.RaidMenuCleanupFix",
        "com.maschine.StashFilter", "com.maschine.SurroundAudio", "com.maschine.UnloadAllMagazines", "xyz.pit.fireteam",
        "com.mpstark.dynamicmaps", "com.tyfon.uifixes", "com.wtt.commonlib", "com.wtt.contentbackport",
        "fika.ghostfenixx.svm", "com.agavalda.giveui", "com.lacyway.ch", "com.terkoiz.freecam", "com.tarkin.doordash",
        "com.ozen.continuousloadammo", "com.pein.camerarecoilmod",
        "com.anvil.weboverlay", "com.slpf.armorclassicon", "katrin0522.betterammoloadinglist", "7Bpencil.BrighterInteriors",
        "com.acidphantasm.devbalaclavafix", "HealingAutoCancel", "com.smajlec.lights",
        "com.maschine.UiBridge", "com.maschine.ModProfiler", "com.maschine.LogTools", "com.maschine.ConsoleToMonitor",
    };

    [Fact]
    public void ShippedDefaultFileDeniesTheFiveRefusedModsAndAllowsEveryInstalledOne()
    {
        string text = File.ReadAllText(ShippedDefaultPath);
        var policy = LoadWithDiagnostics(text, null, out var diagnostics);
        // No diagnostics also means: no GUID is both allowed and denied, and no platform unit is denied.
        Assert.Empty(diagnostics);

        // The file itself names the five, read as plain JSON: a check through the policy alone could not tell
        // a lost deny from a unit that was never installed.
        Assert.True(ArenaMiniJson.TryParse(text, out var root, out var parseError), parseError);
        var denyMember = ArenaMiniJson.GetArray(ArenaMiniJson.AsObject(root), "deny");
        Assert.NotNull(denyMember);
        Assert.Equal(
            ShippedDenies.Select(d => d.Guid).OrderBy(g => g, StringComparer.Ordinal).ToArray(),
            denyMember!.Cast<string>().OrderBy(g => g, StringComparer.Ordinal).ToArray());

        foreach (var (guid, name) in ShippedDenies)
        {
            Assert.True(policy.IsDeniedByDefault(guid), name);
            Assert.False(policy.IsAllowedPlugin(guid), name);
            Assert.False(policy.IsAllowedServerMod(guid), name);

            // Whitelist and blacklist alike, on both sides, with the reason of the default list.
            foreach (string? user in new[] { null, BlacklistOnly })
            {
                foreach (var side in new[] { ArenaWhitelistSide.Client, ArenaWhitelistSide.Server })
                {
                    var decision = ArenaWhitelistDecision.Evaluate(Load(text, user), side, new[] { Unit(guid, name, "1.0.0") });
                    Assert.Equal("denied", Assert.Single(decision.Excluded).Reason);
                    Assert.Empty(decision.Allowed);
                }
            }

            // The user decides last: an allow brings the mod back.
            Assert.True(Load(text, "{ \"schema\": 1, \"allow\": [ { \"guid\": \"" + guid + "\" } ] }").IsAllowedPlugin(guid), name);
        }

        foreach (string guid in InstalledGuids)
        {
            Assert.True(policy.IsAllowedPlugin(guid), guid);
            Assert.True(policy.IsAllowedServerMod(guid), guid);
            Assert.False(policy.IsDeniedByDefault(guid), guid);
            Assert.NotNull(policy.FindPlugin(guid, ArenaWhitelistSide.Client));
        }

        Assert.True(policy.IsAllowedPatcher("WTT-ContentBackportPatcher.dll"));
        Assert.True(policy.IsAllowedPatcher("maschine-LogTools.dll"));

        // The shipped list stays a whitelist: a mod nobody named is still out.
        Assert.False(policy.IsAllowedPlugin("com.example.foreign"));
        Assert.False(policy.IsAllowedPatcher("Foreign.Patcher.dll"));
        Assert.Equal(ArenaUnlistedMode.Deny, policy.UnlistedMode);
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

    // JSON null for allow, deny or unlisted is a missing member (spec section 3), in both modes and without a
    // diagnostic. Pinned for the blacklist in particular: there a deny list that cannot be read in full costs the
    // whole file, and a null deny must not be mistaken for one.
    [Theory]
    [InlineData("", false)]
    [InlineData(", \"unlisted\": \"deny\"", false)]
    [InlineData(", \"unlisted\": null", false)]
    [InlineData(", \"unlisted\": \"allow\"", true)]
    public void AJsonNullAllowOrDenyMemberIsTheSameAsAMissingOneInBothModes(string modeMember, bool blacklist)
    {
        var missing = LoadWithDiagnostics(SmallDefault, "{ \"schema\": 1" + modeMember + " }", out var missingDiagnostics);
        var withNulls = LoadWithDiagnostics(SmallDefault, "{ \"schema\": 1" + modeMember + ", \"allow\": null, \"deny\": null }", out var nullDiagnostics);

        Assert.Empty(missingDiagnostics);
        Assert.Empty(nullDiagnostics);
        Assert.True(withNulls.UserFileApplied);
        Assert.Equal(blacklist ? ArenaUnlistedMode.Allow : ArenaUnlistedMode.Deny, withNulls.UnlistedMode);
        Assert.Equal(missing.Hash, withNulls.Hash);
        Assert.Equal(blacklist, withNulls.IsAllowedPlugin("com.example.unknown"));
        Assert.True(withNulls.IsAllowedPlugin("me.sol.sain"));
        Assert.False(withNulls.IsDeniedByUser("me.sol.sain"));
        Assert.False(withNulls.IsDeniedByUser("com.example.unknown"));
        Assert.Equal(DescribeEntries(missing.Entries), DescribeEntries(withNulls.Entries));
    }

    [Fact]
    public void ABlacklistWithANullDenyKeepsTheFileWhileAnUnreadableDenyOfTheSameShapeDiscardsIt()
    {
        // The contrast that makes "null is absent" a rule and not an oversight: an empty string is not an array.
        var nullDeny = LoadWithDiagnostics(SmallDefault, """{ "schema": 1, "unlisted": "allow", "deny": null }""", out var nullDiagnostics);
        var stringDeny = LoadWithDiagnostics(SmallDefault, """{ "schema": 1, "unlisted": "allow", "deny": "" }""", out var stringDiagnostics);

        Assert.Empty(nullDiagnostics);
        Assert.True(nullDeny.UserFileApplied);
        Assert.True(nullDeny.AllowsUnlisted);
        Assert.Contains("deny", Assert.Single(stringDiagnostics));
        Assert.False(stringDeny.UserFileApplied);
        Assert.False(stringDeny.AllowsUnlisted);
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
    public void AUserDeniedHardDependencyIsNeverPulledInAndItsDependantsAreExcludedWithAWarning()
    {
        var policy = Load(SmallDefault, """
            { "schema": 1, "deny": [ "com.example.lib" ], "allow": [ { "guid": "com.example.second" } ] }
            """);
        var installed = new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.1", "com.example.lib"),
            Unit("com.example.lib", "Lib", "1.0.0"),
            Unit("com.example.second", "Second", "1.0.0", "com.example.lib"),
            Unit("com.maschine.UiBridge", "UiBridge", "0.9.4"),
        };

        foreach (var side in new[] { ArenaWhitelistSide.Client, ArenaWhitelistSide.Server })
        {
            var decision = ArenaWhitelistDecision.Evaluate(policy, side, installed);

            Assert.Equal(new[] { "com.maschine.UiBridge" }, decision.Allowed.Select(u => u.Guid).ToArray());
            Assert.Equal(new[] { "me.sol.sain", "com.example.lib", "com.example.second" }, decision.Excluded.Select(u => u.Guid).ToArray());
            Assert.Equal("requires-denied:com.example.lib", Find(decision.Excluded, "me.sol.sain").Reason);
            Assert.Equal("requires-denied:com.example.lib", Find(decision.Excluded, "com.example.second").Reason);
            Assert.Equal("denied-by-user", Find(decision.Excluded, "com.example.lib").Reason);
            Assert.Equal(
                new[]
                {
                    "SAIN: excluded because it requires Lib (denied by the user's policy file)",
                    "Second: excluded because it requires Lib (denied by the user's policy file)",
                },
                decision.Warnings.ToArray());
            AssertNoAllowedUnitNeedsAnExcludedOne(installed, decision);
        }
    }

    [Fact]
    public void ADeniedUnitExcludesEveryUnitThatNeedsItThroughOtherUnitsAndTheReasonNamesTheDeniedGuid()
    {
        // SAIN -> Middle -> Leaf, Leaf denied by the user: the denied unit is the only one named in the reasons.
        var policy = Load(SmallDefault, """
            { "schema": 1, "allow": [ { "guid": "com.example.middle" } ], "deny": [ "com.example.leaf" ] }
            """);
        var installed = new[]
        {
            Unit("com.example.leaf", "Leaf", "1.0.0"),
            Unit("com.example.middle", "Middle", "1.0.0", "com.example.leaf"),
            Unit("me.sol.sain", "SAIN", "4.5.1", "com.example.middle"),
            Unit("com.maschine.UiBridge", "UiBridge", "0.9.4"),
        };

        foreach (var side in new[] { ArenaWhitelistSide.Client, ArenaWhitelistSide.Server })
        {
            var decision = ArenaWhitelistDecision.Evaluate(policy, side, installed);

            Assert.Equal(new[] { "com.maschine.UiBridge" }, decision.Allowed.Select(u => u.Guid).ToArray());
            Assert.Equal("denied-by-user", Find(decision.Excluded, "com.example.leaf").Reason);
            Assert.Equal("requires-denied:com.example.leaf", Find(decision.Excluded, "com.example.middle").Reason);
            Assert.Equal("requires-denied:com.example.leaf", Find(decision.Excluded, "me.sol.sain").Reason);
            Assert.Equal(
                new[]
                {
                    "Middle: excluded because it requires Leaf (denied by the user's policy file)",
                    "SAIN: excluded because it requires Leaf (denied by the user's policy file)",
                },
                decision.Warnings.ToArray());
            AssertNoAllowedUnitNeedsAnExcludedOne(installed, decision);
        }
    }

    [Theory]
    [InlineData("leaf1", "leaf2", "Leaf A")]
    [InlineData("leaf2", "leaf1", "Leaf B")]
    public void AUnitThatNeedsSeveralDeniedUnitsNamesTheFirstOneInItsOwnDependencyOrder(string first, string second, string firstName)
    {
        // Two different denied GUIDs: the one the unit lists first is named in the reason and in the warning, whatever
        // the installed order is.
        var policy = Load(SmallDefault, """{ "schema": 1, "deny": [ "com.example.leaf1", "com.example.leaf2" ] }""");
        var installed = new[]
        {
            Unit("com.example.leaf1", "Leaf A", "1.0.0"),
            Unit("com.example.leaf2", "Leaf B", "1.0.0"),
            Unit("me.sol.sain", "SAIN", "4.5.1", "com.example." + first, "com.example." + second),
        };

        foreach (var side in new[] { ArenaWhitelistSide.Client, ArenaWhitelistSide.Server })
        {
            var decision = ArenaWhitelistDecision.Evaluate(policy, side, installed);

            Assert.Equal("requires-denied:com.example." + first, Find(decision.Excluded, "me.sol.sain").Reason);
            Assert.Equal(new[] { "SAIN: excluded because it requires " + firstName + " (denied by the user's policy file)" }, decision.Warnings.ToArray());
        }
    }

    [Theory]
    [InlineData(false, "Leaf new")]
    [InlineData(true, "Leaf old")]
    public void AGuidInstalledTwiceNamesTheFirstInstalledCopyThatIsDenied(bool oldFirst, string expectedName)
    {
        // Two installed copies of one denied GUID (an old and a new copy of a plugin): the first one in the installed
        // order is the one named in the warning; the reason carries the GUID, which is the same for both.
        var policy = Load(SmallDefault, """{ "schema": 1, "deny": [ "com.example.leaf" ] }""");
        var copies = new[]
        {
            Unit("com.example.leaf", "Leaf new", "2.0.0"),
            Unit("com.example.leaf", "Leaf old", "1.0.0"),
        };
        var installed = (oldFirst ? copies.Reverse() : copies).Append(Unit("me.sol.sain", "SAIN", "4.5.1", "com.example.leaf")).ToArray();

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, installed);

        Assert.Equal("requires-denied:com.example.leaf", Find(decision.Excluded, "me.sol.sain").Reason);
        Assert.Equal(new[] { "SAIN: excluded because it requires " + expectedName + " (denied by the user's policy file)" }, decision.Warnings.ToArray());
    }

    [Fact]
    public void AnUnlistedLibraryBetweenAnAllowedUnitAndADeniedOneIsExcludedWithItAndAnUnrelatedOneIsNot()
    {
        // Whitelist: SAIN -> Between (unlisted) -> Leaf (denied). Between would have been pulled in, so it is
        // reported as requires-denied like SAIN; Stray (unlisted, also needs Leaf) is needed by nobody allowed
        // and stays a plain not-on-whitelist exclusion without a warning.
        var policy = Load(SmallDefault, """{ "schema": 1, "deny": [ "com.example.leaf" ] }""");
        var installed = new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.1", "com.example.between"),
            Unit("com.example.between", "Between", "1.0.0", "com.example.leaf"),
            Unit("com.example.stray", "Stray", "1.0.0", "com.example.leaf"),
            Unit("com.example.leaf", "Leaf", "1.0.0"),
        };

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, installed);

        Assert.Empty(decision.Allowed);
        Assert.Equal("requires-denied:com.example.leaf", Find(decision.Excluded, "me.sol.sain").Reason);
        Assert.Equal("requires-denied:com.example.leaf", Find(decision.Excluded, "com.example.between").Reason);
        Assert.Equal("not-on-whitelist", Find(decision.Excluded, "com.example.stray").Reason);
        Assert.Equal("denied-by-user", Find(decision.Excluded, "com.example.leaf").Reason);
        Assert.Equal(
            new[]
            {
                "SAIN: excluded because it requires Leaf (denied by the user's policy file)",
                "Between: excluded because it requires Leaf (denied by the user's policy file)",
            },
            decision.Warnings.ToArray());
    }

    [Fact]
    public void ACutOffUnitDoesNotPullItsOtherLibrariesIn()
    {
        // SAIN needs an unlisted library and a denied one. Excluding SAIN means the library has no allowed
        // dependant left: it must stay out, and nothing may say it was pulled in.
        var policy = Load(SmallDefault, """{ "schema": 1, "deny": [ "com.example.leaf" ] }""");
        var installed = new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.1", "com.example.lib", "com.example.leaf"),
            Unit("com.example.lib", "Lib", "1.0.0"),
            Unit("com.example.leaf", "Leaf", "1.0.0"),
            Unit("com.maschine.UiBridge", "UiBridge", "0.9.4"),
        };

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, installed);

        Assert.Equal(new[] { "com.maschine.UiBridge" }, decision.Allowed.Select(u => u.Guid).ToArray());
        Assert.Equal("not-on-whitelist", Find(decision.Excluded, "com.example.lib").Reason);
        Assert.Equal("SAIN: excluded because it requires Leaf (denied by the user's policy file)", Assert.Single(decision.Warnings));
        AssertNoAllowedUnitNeedsAnExcludedOne(installed, decision);
    }

    [Fact]
    public void AUnitOnlyADeniedUnitNeedsKeepsItsPlainReasonEvenWhenItNeedsADeniedUnitItself()
    {
        // SAIN -> Denied -> Library -> OtherDenied. SAIN is cut off by Denied, and a denied unit pulls nothing
        // in, so nothing allowed would ever have pulled Library in: it is an ordinary unlisted exclusion and
        // says nothing about a denied unit.
        var policy = Load(SmallDefault, """{ "schema": 1, "deny": [ "com.example.denied", "com.example.other" ] }""");
        var installed = new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.1", "com.example.denied"),
            Unit("com.example.denied", "Denied", "1.0.0", "com.example.library"),
            Unit("com.example.library", "Library", "1.0.0", "com.example.other"),
            Unit("com.example.other", "Other Denied", "1.0.0"),
            Unit("com.maschine.UiBridge", "UiBridge", "0.9.4"),
        };

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, installed);

        Assert.Equal(new[] { "com.maschine.UiBridge" }, decision.Allowed.Select(u => u.Guid).ToArray());
        Assert.Equal("requires-denied:com.example.denied", Find(decision.Excluded, "me.sol.sain").Reason);
        Assert.Equal("denied-by-user", Find(decision.Excluded, "com.example.denied").Reason);
        Assert.Equal("not-on-whitelist", Find(decision.Excluded, "com.example.library").Reason);
        Assert.Equal("denied-by-user", Find(decision.Excluded, "com.example.other").Reason);
        Assert.Equal("SAIN: excluded because it requires Denied (denied by the user's policy file)", Assert.Single(decision.Warnings));
    }

    [Fact]
    public void ADependencyCycleThatReachesADeniedUnitExcludesTheWholeCycle()
    {
        var policy = Load(SmallDefault, """{ "schema": 1, "deny": [ "com.example.leaf" ] }""");
        var installed = new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.1", "com.example.a"),
            Unit("com.example.a", "A", "1.0.0", "com.example.b"),
            Unit("com.example.b", "B", "1.0.0", "com.example.a", "com.example.leaf", "me.sol.sain"),
            Unit("com.example.leaf", "Leaf", "1.0.0"),
        };

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, installed);

        Assert.Empty(decision.Allowed);
        foreach (string guid in new[] { "me.sol.sain", "com.example.a", "com.example.b" })
        {
            Assert.Equal("requires-denied:com.example.leaf", Find(decision.Excluded, guid).Reason);
        }

        Assert.Equal(3, decision.Warnings.Count);
    }

    [Fact]
    public void ADeniedDependencyIsMatchedIgnoringCaseAndTheReasonUsesTheInstalledGuid()
    {
        var policy = Load(SmallDefault, """{ "schema": 1, "deny": [ "com.example.lib" ] }""");
        var installed = new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.2", "COM.EXAMPLE.LIB"),
            Unit("Com.Example.Lib", "Lib", "1.0.0"),
        };

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, installed);

        Assert.Empty(decision.Allowed);
        Assert.Equal("requires-denied:Com.Example.Lib", Find(decision.Excluded, "me.sol.sain").Reason);
        // The version differs from the reviewed one, but an excluded unit gets the exclusion warning only.
        Assert.Equal("SAIN: excluded because it requires Lib (denied by the user's policy file)", Assert.Single(decision.Warnings));
    }

    [Fact]
    public void ALiftedDefaultDenyIsNoDenyForTheDependencyRuleButAUserDenyOnTopOfTheLiftIs()
    {
        var installed = new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.1", "com.example.bad"),
            Unit("com.example.bad", "Bad", "1.0.0"),
        };

        // The user's allow entry brings the default-denied unit back: it is an ordinary allowed unit.
        var lifted = ArenaWhitelistDecision.Evaluate(
            Load(DenyDefault, """{ "schema": 1, "allow": [ { "guid": "com.example.bad" } ] }"""), ArenaWhitelistSide.Client, installed);
        Assert.Empty(lifted.Excluded);
        Assert.Equal(new[] { "whitelist", "whitelist" }, lifted.Allowed.Select(u => u.Reason).ToArray());
        Assert.Empty(lifted.Warnings);

        // The user file applies its allow before its deny, so the deny wins and the dependant goes with it.
        var denied = ArenaWhitelistDecision.Evaluate(
            Load(DenyDefault, """{ "schema": 1, "allow": [ { "guid": "com.example.bad" } ], "deny": [ "com.example.bad" ] }"""),
            ArenaWhitelistSide.Client, installed);
        Assert.Empty(denied.Allowed);
        Assert.Equal("denied-by-user", Find(denied.Excluded, "com.example.bad").Reason);
        Assert.Equal("requires-denied:com.example.bad", Find(denied.Excluded, "me.sol.sain").Reason);
        Assert.Equal("SAIN: excluded because it requires Bad (denied by the user's policy file)", Assert.Single(denied.Warnings));
    }

    [Fact]
    public void PlatformAndArenaUnitsStayAllowedEvenWhenTheyNameADeniedDependency()
    {
        // No list can remove them (spec section 3, step 1), so the dependency rule does not either. Their denied
        // dependency stays out; a mod that needs only the Arena unit is not cut off.
        var policy = Load(DenyDefault, """{ "schema": 1, "deny": [ "com.example.userbad" ] }""");
        var installed = new[]
        {
            Unit("com.maschine.ArenaMode", "ArenaMode", "0.1.3", "com.example.bad", "com.example.userbad", "com.example.between"),
            Unit("com.SPT.core", "SPT.Core", "4.1.6", "com.example.bad"),
            Unit("com.fika.core", "Fika", "2.0.0", "com.example.bad"),
            Unit("me.sol.sain", "SAIN", "4.5.1", "com.maschine.ArenaMode"),
            Unit("com.example.bad", "Bad", "1.0.0"),
            Unit("com.example.userbad", "User Bad", "1.0.0"),
            Unit("com.example.between", "Between", "1.0.0", "com.example.bad"),   // unlisted, cut off, and the Arena unit names it
        };

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, installed);

        Assert.Equal(new[] { "arena", "platform", "platform", "whitelist" }, decision.Allowed.Select(u => u.Reason).ToArray());
        Assert.Equal("denied", Find(decision.Excluded, "com.example.bad").Reason);
        Assert.Equal("denied-by-user", Find(decision.Excluded, "com.example.userbad").Reason);
        // Between is cut off, and propagation stops at the Arena unit: it neither takes ArenaMode down with it
        // nor is pulled in by it.
        Assert.Equal("requires-denied:com.example.bad", Find(decision.Excluded, "com.example.between").Reason);
        Assert.Equal(3, decision.Excluded.Count);
        Assert.Equal("Between: excluded because it requires Bad (denied by the default list)", Assert.Single(decision.Warnings));

        var server = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Server, installed);
        Assert.Contains(server.Allowed, u => u.Guid == "com.maschine.ArenaMode" && u.Reason == "arena");
        Assert.Contains(server.Allowed, u => u.Guid == "me.sol.sain" && u.Reason == "whitelist");
    }

    [Fact]
    public void TheRequiresDeniedReasonHasAStablePrefixAndAHelper()
    {
        Assert.Equal("requires-denied:", ArenaWhitelistDecision.ReasonRequiresDeniedPrefix);

        Assert.True(ArenaWhitelistDecision.IsRequiresDeniedReason("requires-denied:com.example.lib"));
        foreach (string? other in new[] { null, "", "denied", "denied-by-user", "dependency-of:requires-denied:x", "not-on-whitelist", "Requires-Denied:x" })
        {
            Assert.False(ArenaWhitelistDecision.IsRequiresDeniedReason(other!), other ?? "(null)");
        }
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
        // No warning of its own for an unlisted unit; the one aggregated policy-override warning names them.
        Assert.Equal("policy override: 3 unlisted mod(s) allowed by your policy file: Looting Bots, No Guid, Unversioned",
            Assert.Single(decision.Warnings));
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
        Assert.Equal("policy override: 2 unlisted mod(s) allowed by your policy file: One, Two", Assert.Single(decision.Warnings));
        Assert.All(decision.Allowed, u => Assert.Equal("unlisted-allowed", u.Reason));
    }

    // ------------------------------------------------------------------ decision: policy-override warning

    private const string PolicyOverrideText = "policy override: ";

    private static bool IsOverride(string warning) => warning.StartsWith(PolicyOverrideText, StringComparison.Ordinal);

    [Fact]
    public void ThePolicyOverrideWarningConstantsMatchTheDocumentedWording()
    {
        Assert.Equal(PolicyOverrideText, ArenaWhitelistDecision.PolicyOverridePrefix);
        Assert.Equal(6, ArenaWhitelistDecision.PolicyOverrideNamesListed);
        Assert.True(ArenaWhitelistDecision.IsPolicyOverrideWarning("policy override: 1 unlisted mod(s) allowed by your policy file: X"));
        Assert.False(ArenaWhitelistDecision.IsPolicyOverrideWarning("SAIN: version 4.5.2 differs from reviewed 4.5.1"));
        Assert.False(ArenaWhitelistDecision.IsPolicyOverrideWarning("Policy override: 1 unlisted mod(s)"));
        Assert.False(ArenaWhitelistDecision.IsPolicyOverrideWarning(string.Empty));
        Assert.False(ArenaWhitelistDecision.IsPolicyOverrideWarning(null!));
    }

    [Fact]
    public void BlacklistModeAddsExactlyOnePolicyOverrideWarningPerSideAndPutsItLast()
    {
        var policy = Load(SmallDefault, BlacklistUser);

        // Client side: one version warning, then the override naming the unlisted units, sorted ignoring case.
        var client = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, new[]
        {
            Unit("com.example.zed", "zed", "1.0.0"),
            Unit("me.sol.sain", "SAIN", "4.5.2"),
            Unit("com.example.alpha", "Alpha", "1.0.0"),
            Unit("com.example.bad", "Bad Mod", "1.0.0"),           // denied: not counted
            Unit("com.example.beta", "beta", "1.0.0"),
        });
        Assert.Equal(
            new[]
            {
                "SAIN: version 4.5.2 differs from reviewed 4.5.1",
                "policy override: 3 unlisted mod(s) allowed by your policy file: Alpha, beta, zed",
            },
            client.Warnings.ToArray());

        // Server side: its own decision, its own single warning, counted from its own units.
        var server = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Server, new[]
        {
            Unit("com.maschine.ArenaMode", "ArenaMode", "0.1.3"),
            Unit("com.example.servermod", "Server Mod", "1.0.0"),
        });
        Assert.Equal("policy override: 1 unlisted mod(s) allowed by your policy file: Server Mod", Assert.Single(server.Warnings));

        // The warning is about the allowed units only: nothing about it changes who is allowed or excluded.
        Assert.Equal(new[] { "com.example.bad" }, client.Excluded.Select(u => u.Guid).ToArray());
        Assert.Equal(4, client.Allowed.Count);
    }

    [Fact]
    public void ThePolicyOverrideWarningListsAtMostSixNamesAndCountsTheRest()
    {
        var policy = Load(SmallDefault, BlacklistOnly);

        string WarningFor(int total)
        {
            // Reverse input order: the names are sorted, not listed as installed.
            var installed = Enumerable.Range(1, total).Reverse()
                .Select(i => Unit("com.example.m" + i.ToString("D2"), "Mod" + i.ToString("D2"), "1.0.0")).ToArray();
            return Assert.Single(ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, installed).Warnings);
        }

        Assert.Equal("policy override: 1 unlisted mod(s) allowed by your policy file: Mod01", WarningFor(1));
        Assert.Equal("policy override: 5 unlisted mod(s) allowed by your policy file: Mod01, Mod02, Mod03, Mod04, Mod05", WarningFor(5));
        Assert.Equal("policy override: 6 unlisted mod(s) allowed by your policy file: Mod01, Mod02, Mod03, Mod04, Mod05, Mod06", WarningFor(6));
        Assert.Equal("policy override: 7 unlisted mod(s) allowed by your policy file: Mod01, Mod02, Mod03, Mod04, Mod05, Mod06, and 1 more", WarningFor(7));
        Assert.Equal("policy override: 30 unlisted mod(s) allowed by your policy file: Mod01, Mod02, Mod03, Mod04, Mod05, Mod06, and 24 more", WarningFor(30));
    }

    [Fact]
    public void ThePolicyOverrideNamesAreSortedIgnoringCaseAndFallBackToTheGuid()
    {
        var policy = Load(SmallDefault, BlacklistOnly);

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, new[]
        {
            Unit("com.example.banana", "banana", "1.0.0"),
            Unit("com.example.apple", "Apple", "1.0.0"),
            Unit("com.example.cherry", "cherry", "1.0.0"),
            Unit("com.example.nameless", "", "1.0.0"),              // no name: the GUID
            Unit("com.example.blank", "   ", "1.0.0"),              // blank name: "(unnamed)"
        });

        Assert.Equal(
            "policy override: 5 unlisted mod(s) allowed by your policy file: (unnamed), Apple, banana, cherry, com.example.nameless",
            Assert.Single(decision.Warnings));

        // Names that differ only in case come out in one fixed order, whatever the input order was.
        foreach (var installed in new[]
        {
            new[] { Unit("com.example.a", "alpha", "1.0.0"), Unit("com.example.b", "Alpha", "1.0.0") },
            new[] { Unit("com.example.b", "Alpha", "1.0.0"), Unit("com.example.a", "alpha", "1.0.0") },
        })
        {
            Assert.Equal("policy override: 2 unlisted mod(s) allowed by your policy file: Alpha, alpha",
                Assert.Single(ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, installed).Warnings));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{ "schema": 1 }""")]
    [InlineData("""{ "schema": 1, "unlisted": "deny" }""")]
    [InlineData("""{ "schema": 1, "unlisted": "deny", "allow": [ { "guid": "com.example.other" } ], "deny": [ "com.example.bad" ] }""")]
    public void AWhitelistNeverAddsAPolicyOverrideWarning(string? userJson)
    {
        var policy = Load(SmallDefault, userJson);

        foreach (var side in new[] { ArenaWhitelistSide.Client, ArenaWhitelistSide.Server })
        {
            var decision = ArenaWhitelistDecision.Evaluate(policy, side, new[]
            {
                Unit("me.sol.sain", "SAIN", "4.5.1"),
                Unit("com.example.other", "Other", "1.0.0"),
                Unit("com.example.bad", "Bad", "1.0.0"),
                Unit("com.example.unknown", "Unknown", "1.0.0"),
            });

            Assert.DoesNotContain(decision.Warnings, IsOverride);
            Assert.DoesNotContain(decision.Allowed, u => u.Reason == "unlisted-allowed");
        }
    }

    [Fact]
    public void ABlacklistWithoutAnUnlistedAllowedUnitAddsNoPolicyOverrideWarning()
    {
        // The mode alone is not the warning: it is about units that actually got in because nothing names them.
        var policy = Load(SmallDefault, """{ "schema": 1, "unlisted": "allow", "deny": [ "com.example.bad", "com.example.lib" ] }""");

        Assert.Empty(ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, Array.Empty<ArenaSessionUnit>()).Warnings);
        Assert.Empty(ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, null!).Warnings);

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.1"),                                 // listed
            Unit("com.example.foreign", "Foreign", "1.0.0", "com.example.lib"),   // unlisted, but needs a denied unit: excluded, not counted
            Unit("com.example.lib", "Lib", "1.0.0"),                              // denied
            Unit("com.example.bad", "Bad", "1.0.0"),                              // denied
            Unit("com.maschine.UiBridge", "UiBridge", "0.9.4"),                   // tool
        });

        Assert.Equal(new[] { "me.sol.sain", "com.maschine.UiBridge" }, decision.Allowed.Select(u => u.Guid).ToArray());
        Assert.Equal("requires-denied:com.example.lib", Find(decision.Excluded, "com.example.foreign").Reason);
        Assert.Equal("Foreign: excluded because it requires Lib (denied by the user's policy file)", Assert.Single(decision.Warnings));
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
                // The unlisted units get no version warning; they are only named here, after the per-unit warnings.
                "policy override: 3 unlisted mod(s) allowed by your policy file: Garbage, No Version, Unlisted",
            },
            decision.Warnings.ToArray());
        Assert.Equal(new[] { "whitelist", "whitelist", "unlisted-allowed", "unlisted-allowed", "unlisted-allowed" },
            decision.Allowed.Select(u => u.Reason).ToArray());
    }

    [Fact]
    public void BlacklistModeNeverPullsADeniedHardDependencyInAndExcludesWhatNeedsIt()
    {
        var policy = Load(SmallDefault, """
            { "schema": 1, "unlisted": "allow", "deny": [ "com.example.lib", "com.example.leaf", "com.example.sainlib", "com.example.other" ] }
            """);
        var installed = new[]
        {
            Unit("com.example.foreign", "Foreign", "1.0.0", "com.example.lib"),   // unlisted, but needs a denied unit
            Unit("com.example.lib", "Lib", "1.0.0", "com.example.leaf"),          // denied, needs another denied unit
            Unit("com.example.leaf", "Leaf", "1.0.0"),                            // denied
            Unit("me.sol.sain", "SAIN", "4.5.1", "com.example.sainlib"),          // listed, but needs a denied unit
            Unit("com.example.sainlib", "Sain Lib", "1.0.0"),                     // denied
            Unit("com.example.other", "Other", "1.0.0"),                          // denied, needed by nobody
            Unit("com.example.free", "Free", "1.0.0"),                            // unlisted, needs nothing
        };

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, installed);

        Assert.Equal("com.example.free", Assert.Single(decision.Allowed).Guid);
        Assert.Equal("unlisted-allowed", decision.Allowed[0].Reason);
        Assert.Equal(
            new[] { "com.example.foreign", "com.example.lib", "com.example.leaf", "me.sol.sain", "com.example.sainlib", "com.example.other" },
            decision.Excluded.Select(u => u.Guid).ToArray());
        Assert.Equal(
            new[]
            {
                "requires-denied:com.example.lib", "denied-by-user", "denied-by-user",
                "requires-denied:com.example.sainlib", "denied-by-user", "denied-by-user",
            },
            decision.Excluded.Select(u => u.Reason).ToArray());
        Assert.Equal(
            new[]
            {
                "Foreign: excluded because it requires Lib (denied by the user's policy file)",
                "SAIN: excluded because it requires Sain Lib (denied by the user's policy file)",
                // Only Free was allowed as unlisted; the two excluded units are not counted.
                "policy override: 1 unlisted mod(s) allowed by your policy file: Free",
            },
            decision.Warnings.ToArray());
        AssertNoAllowedUnitNeedsAnExcludedOne(installed, decision);
    }

    [Fact]
    public void ServerModsThatNeedADeniedLibraryAreLeftOutOfTheTreeTogetherWithIt()
    {
        // The server's mod validator drops every mod when one ModDependencies entry is missing, so a mod
        // whose library is denied must not stay in the session tree: it goes out with the library, and so does
        // the mod that needs that mod.
        var policy = Load(SmallDefault, """
            { "schema": 1, "unlisted": "allow", "deny": [ "com.example.lib", "com.example.unneeded" ] }
            """);
        var installed = new[]
        {
            Unit("com.example.content", "Content Pack", "1.0.0", "com.example.lib"),   // unlisted, but needs a denied unit
            Unit("com.example.lib", "Lib", "3.0.6"),                                   // denied
            Unit("com.example.addon", "Addon", "1.0.0", "com.example.content"),        // unlisted, needs the Content Pack
            Unit("com.example.unneeded", "Unneeded", "1.0.0"),                         // denied, needed by nobody
            Unit("com.example.free", "Free", "1.0.0"),                                 // unlisted, needs nothing
        };

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Server, installed);

        Assert.Equal("com.example.free", Assert.Single(decision.Allowed).Guid);
        Assert.Equal("requires-denied:com.example.lib", Find(decision.Excluded, "com.example.content").Reason);
        Assert.Equal("requires-denied:com.example.lib", Find(decision.Excluded, "com.example.addon").Reason);
        Assert.Equal("denied-by-user", Find(decision.Excluded, "com.example.lib").Reason);
        Assert.Equal("denied-by-user", Find(decision.Excluded, "com.example.unneeded").Reason);
        Assert.Equal(
            new[]
            {
                "Content Pack: excluded because it requires Lib (denied by the user's policy file)",
                "Addon: excluded because it requires Lib (denied by the user's policy file)",
                "policy override: 1 unlisted mod(s) allowed by your policy file: Free",
            },
            decision.Warnings.ToArray());
        AssertNoAllowedUnitNeedsAnExcludedOne(installed, decision);
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
        Assert.Equal(
            new[]
            {
                "APBS: version 2.4.0 differs from reviewed 2.4.1",
                // SPT.Core is a client platform entry only: on the server side it is just another unlisted mod.
                "policy override: 2 unlisted mod(s) allowed by your policy file: Server Thing, SPT.Core",
            },
            decision.Warnings.ToArray());
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

    // ------------------------------------------------------------------ default deny list

    // The shipped default file may carry a "deny" list of its own (2026-10-22): mods ArenaMode refuses by design.
    // Per unit the order is platform/Arena, user deny, user allow, DEFAULT deny, default entries, unlisted.

    /// <summary>
    /// A default file with a deny list: a plugin/server GUID and a patcher file name, next to SAIN and the
    /// LogTools patcher as ordinary entries.
    /// </summary>
    private const string DenyDefault = """
        {
          "schema": 1,
          "mods": [
            { "guid": "me.sol.sain", "name": "SAIN", "kind": "mod", "clientVersion": "4.5.1", "serverVersion": "4.5.1" },
            { "patcher": "maschine-LogTools.dll", "name": "LogTools patcher", "kind": "tool" }
          ],
          "deny": [ "com.example.bad", "Bad.Patcher.dll" ]
        }
        """;

    private const string BlacklistOnly = """{ "schema": 1, "unlisted": "allow" }""";

    [Fact]
    public void ADefaultDenyExcludesTheUnitInWhitelistModeWithItsOwnReasonOnBothSides()
    {
        var policy = LoadWithDiagnostics(DenyDefault, null, out var diagnostics);
        Assert.Empty(diagnostics);
        var installed = new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.1"),
            Unit("COM.EXAMPLE.BAD", "Bad", "1.0.0"),
            Unit("com.example.other", "Other", "1.0.0"),
        };

        foreach (var side in new[] { ArenaWhitelistSide.Client, ArenaWhitelistSide.Server })
        {
            var decision = ArenaWhitelistDecision.Evaluate(policy, side, installed);

            Assert.Equal(new[] { "me.sol.sain" }, decision.Allowed.Select(u => u.Guid).ToArray());
            Assert.Equal("denied", Find(decision.Excluded, "com.example.bad").Reason);
            Assert.Equal("not-on-whitelist", Find(decision.Excluded, "com.example.other").Reason);
            Assert.Equal("Bad, Other", decision.DescribeExcluded());
        }

        Assert.Equal("denied", ArenaWhitelistDecision.ReasonDenied);
        Assert.Equal("denied-by-user", ArenaWhitelistDecision.ReasonDeniedByUser);
        Assert.False(policy.IsAllowedPlugin("com.example.bad"));
        Assert.False(policy.IsAllowedServerMod("com.example.bad"));
        Assert.True(policy.IsDeniedByDefault("com.example.bad"));
        Assert.True(policy.IsDeniedByDefault("  COM.example.BAD "));
        Assert.False(policy.IsDeniedByUser("com.example.bad"));
        Assert.False(policy.IsDeniedByDefault("com.example.other"));
        Assert.False(policy.IsDeniedByDefault("me.sol.sain"));
        Assert.False(policy.IsDeniedByDefault(null!));
        Assert.False(policy.IsDeniedByDefault("  "));
        Assert.Equal("denied", policy.ExclusionReason("com.example.bad"));
        Assert.Equal("not-on-whitelist", policy.ExclusionReason("com.example.other"));
        Assert.Equal("not-on-whitelist", policy.ExclusionReason(""));
    }

    [Fact]
    public void ADefaultDenyHoldsInBlacklistModeAndEverythingElseStaysUnlistedAllowed()
    {
        var policy = Load(DenyDefault, BlacklistOnly);
        var installed = new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.1"),
            Unit("com.example.bad", "Bad", "1.0.0"),
            Unit("com.example.other", "Other", "1.0.0"),
        };

        foreach (var side in new[] { ArenaWhitelistSide.Client, ArenaWhitelistSide.Server })
        {
            var decision = ArenaWhitelistDecision.Evaluate(policy, side, installed);

            Assert.Equal("denied", Assert.Single(decision.Excluded).Reason);
            Assert.Equal("com.example.bad", decision.Excluded[0].Guid);
            Assert.Equal(new[] { "whitelist", "unlisted-allowed" }, decision.Allowed.Select(u => u.Reason).ToArray());
            Assert.Equal("policy override: 1 unlisted mod(s) allowed by your policy file: Other", Assert.Single(decision.Warnings));
        }

        Assert.False(policy.IsAllowedPlugin("com.example.bad"));
        Assert.False(policy.IsAllowedServerMod("com.example.bad"));
        Assert.False(policy.IsUnlistedAllowed("com.example.bad"));
        Assert.True(policy.IsUnlistedAllowed("com.example.other"));
    }

    [Fact]
    public void ADefaultDeniedPatcherIsOffInBothModesAndAPathFormIsReducedToItsFileName()
    {
        const string withPath = """
            { "schema": 1, "mods": [ { "patcher": "maschine-LogTools.dll", "kind": "tool" } ], "deny": [ "BepInEx\\patchers\\Bad.Patcher.dll" ] }
            """;

        foreach (string? user in new[] { null, BlacklistOnly })
        {
            var policy = Load(withPath, user);

            Assert.False(policy.IsAllowedPatcher("bad.patcher.dll"), "denied by the default list");
            Assert.False(policy.IsAllowedPatcher(@"C:\game\BepInEx\patchers\BAD.PATCHER.DLL"));
            Assert.True(policy.IsDeniedByDefault("Bad.Patcher.dll"));
            Assert.Equal("denied", policy.ExclusionReason("Bad.Patcher.dll"));
            Assert.True(policy.IsAllowedPatcher("maschine-LogTools.dll"));
            Assert.Equal(user != null, policy.IsAllowedPatcher("Some.Other.dll"));
        }
    }

    [Fact]
    public void AUserAllowLiftsADefaultDenyOnBothSidesAndKeepsItsReviewedVersion()
    {
        const string user = """
            {
              "schema": 1,
              "allow": [
                { "guid": "com.example.bad", "name": "Bad", "kind": "mod", "clientVersion": "2.0.0", "serverVersion": "3.0.0" },
                { "patcher": "bad.patcher.dll", "kind": "tool" }
              ]
            }
            """;

        foreach (string? userJson in new[] { user, user.Replace("\"schema\": 1,", "\"schema\": 1, \"unlisted\": \"allow\",") })
        {
            var policy = LoadWithDiagnostics(DenyDefault, userJson, out var diagnostics);
            Assert.Empty(diagnostics);

            Assert.True(policy.IsAllowedPlugin("com.example.bad"));
            Assert.True(policy.IsAllowedServerMod("com.example.bad"));
            Assert.True(policy.IsAllowedPatcher("Bad.Patcher.dll"));
            Assert.Equal("2.0.0", policy.ReviewedVersion("com.example.bad", ArenaWhitelistSide.Client));
            Assert.Equal("3.0.0", policy.ReviewedVersion("com.example.bad", ArenaWhitelistSide.Server));

            var client = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, new[] { Unit("com.example.bad", "Bad", "2.0.1") });
            Assert.Equal("whitelist", Assert.Single(client.Allowed).Reason);
            Assert.Empty(client.Excluded);
            Assert.Equal("Bad: version 2.0.1 differs from reviewed 2.0.0", Assert.Single(client.Warnings));

            var server = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Server, new[] { Unit("com.example.bad", "Bad", "3.0.0") });
            Assert.Equal("whitelist", Assert.Single(server.Allowed).Reason);
            Assert.Empty(server.Warnings);
        }

        // Without the user's allow the same unit is out: the lift comes from the user file and from nothing else.
        Assert.False(Load(DenyDefault).IsAllowedPlugin("com.example.bad"));
        Assert.NotEqual(Load(DenyDefault).Hash, Load(DenyDefault, user).Hash);
    }

    [Fact]
    public void AUserDenyBeatsADefaultAllowAndAUserAllowOfADefaultDeniedUnitAlike()
    {
        var policy = Load(DenyDefault, """
            {
              "schema": 1,
              "allow": [ { "guid": "com.example.bad" }, { "patcher": "Bad.Patcher.dll" } ],
              "deny": [ "me.sol.sain", "com.example.bad", "Bad.Patcher.dll", "maschine-LogTools.dll" ]
            }
            """);

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.1"),
            Unit("com.example.bad", "Bad", "1.0.0"),
        });

        Assert.Empty(decision.Allowed);
        // The user's deny is the higher precedence, so it names the reason even for a default-denied unit.
        Assert.All(decision.Excluded, u => Assert.Equal("denied-by-user", u.Reason));
        Assert.False(policy.IsAllowedPatcher("Bad.Patcher.dll"));
        Assert.False(policy.IsAllowedPatcher("maschine-LogTools.dll"));
        Assert.Equal("denied-by-user", policy.ExclusionReason("Bad.Patcher.dll"));
    }

    [Fact]
    public void AUserDenyOfADefaultAllowedUnitInBlacklistModeStillWinsAndADefaultDenyIsNotLostByTheUsersDenyList()
    {
        var policy = Load(DenyDefault, """
            { "schema": 1, "unlisted": "allow", "deny": [ "me.sol.sain", "com.example.worse" ] }
            """);

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.1"),
            Unit("com.example.bad", "Bad", "1.0.0"),
            Unit("com.example.worse", "Worse", "1.0.0"),
            Unit("com.example.fine", "Fine", "1.0.0"),
        });

        Assert.Equal("Fine", Assert.Single(decision.Allowed).DisplayName);
        Assert.Equal("denied-by-user", Find(decision.Excluded, "me.sol.sain").Reason);
        Assert.Equal("denied", Find(decision.Excluded, "com.example.bad").Reason);
        Assert.Equal("denied-by-user", Find(decision.Excluded, "com.example.worse").Reason);
    }

    [Fact]
    public void AGuidThatTheDefaultFileAllowsAndDeniesIsDeniedAndNamedInADiagnostic()
    {
        // "deny" comes first in the text on purpose: the order of the members must not matter.
        const string contradiction = """
            {
              "schema": 1,
              "deny": [ "COM.EXAMPLE.BOTH", "Both.Patcher.dll" ],
              "mods": [
                { "guid": "com.example.both", "name": "Both", "kind": "mod", "clientVersion": "1.0.0", "serverVersion": "1.0.0" },
                { "patcher": "Both.Patcher.dll", "name": "Both patcher", "kind": "tool" },
                { "guid": "me.sol.sain", "name": "SAIN", "kind": "mod" }
              ]
            }
            """;

        var policy = LoadWithDiagnostics(contradiction, null, out var diagnostics);

        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.StartsWith("default whitelist:", d));
        Assert.Single(diagnostics, d => d.Contains("COM.EXAMPLE.BOTH") && d.Contains("both allowed and denied"));
        Assert.Single(diagnostics, d => d.Contains("Both.Patcher.dll") && d.Contains("both allowed and denied"));

        Assert.False(policy.IsAllowedPlugin("com.example.both"));
        Assert.False(policy.IsAllowedServerMod("com.example.both"));
        Assert.False(policy.IsAllowedPatcher("both.patcher.dll"));
        Assert.Null(policy.ReviewedVersion("com.example.both", ArenaWhitelistSide.Client));
        Assert.DoesNotContain(policy.Entries, e => e.Id.StartsWith("com.example.both", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(policy.Entries, e => e.Patcher != null && e.Patcher.StartsWith("Both", StringComparison.OrdinalIgnoreCase));
        Assert.True(policy.IsAllowedPlugin("me.sol.sain"), "the rest of the file is intact");

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, new[] { Unit("com.example.both", "Both", "1.0.0") });
        Assert.Equal("denied", Assert.Single(decision.Excluded).Reason);

        // The deny wins also in blacklist mode, and a user allow still brings the unit back.
        Assert.False(Load(contradiction, BlacklistOnly).IsAllowedPlugin("com.example.both"));
        Assert.True(Load(contradiction, """{ "schema": 1, "allow": [ { "guid": "com.example.both" } ] }""").IsAllowedPlugin("com.example.both"));
    }

    [Theory]
    [InlineData("""{ "schema": 1, "mods": [], "deny": "com.example.x" }""", "\"deny\" must be an array")]
    [InlineData("""{ "schema": 1, "mods": [], "deny": { "guid": "com.example.x" } }""", "\"deny\" must be an array")]
    [InlineData("""{ "schema": 1, "mods": [], "deny": 5 }""", "\"deny\" must be an array")]
    [InlineData("""{ "schema": 1, "mods": [], "deny": null }""", "\"deny\" must be an array")]
    [InlineData("""{ "schema": 1, "mods": [], "deny": [ 5 ] }""", "deny[0]")]
    [InlineData("""{ "schema": 1, "mods": [], "deny": [ "" ] }""", "deny[0]")]
    [InlineData("""{ "schema": 1, "mods": [], "deny": [ "   " ] }""", "deny[0]")]
    [InlineData("""{ "schema": 1, "mods": [], "deny": [ null ] }""", "deny[0]")]
    [InlineData("""{ "schema": 1, "mods": [], "deny": [ "ok.guid", true ] }""", "deny[1]")]
    [InlineData("""{ "schema": 1, "mods": [], "deny": [ "ok.guid", [ "nested" ] ] }""", "deny[1]")]
    [InlineData("""{ "schema": 1, "mods": [], "deny": [ "ok.guid", { "guid": "x" } ] }""", "deny[1]")]
    public void AMalformedDefaultDenyListInvalidatesTheDefaultFile(string defaultJson, string fragment)
    {
        // Like any other broken default entry: the Arena session is unavailable, and a valid user file
        // cannot rescue it. A lost deny would let through a mod the build refuses.
        const string validUser = """{ "schema": 1, "allow": [ { "guid": "com.example.x" } ] }""";

        foreach (string? user in new[] { null, validUser, BlacklistOnly })
        {
            bool loaded = ArenaWhitelistPolicy.TryLoad(defaultJson, user!, out var policy, out var diagnostics);

            Assert.False(loaded);
            Assert.Null(policy);
            var diagnostic = Assert.Single(diagnostics);
            Assert.StartsWith("default whitelist:", diagnostic);
            Assert.Contains(fragment, diagnostic);
        }
    }

    [Fact]
    public void ADefaultDenyListThatIsEmptyOrAbsentIsTheSamePolicyAndAnEmptyOneIsNotAnError()
    {
        var baseline = Load(SmallDefault);
        var emptyList = LoadWithDiagnostics(SmallDefault.Replace("\"schema\": 1,", "\"schema\": 1, \"deny\": [],"), null, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(baseline.Hash, emptyList.Hash);
        Assert.False(emptyList.IsDeniedByDefault("me.sol.sain"));
        Assert.True(emptyList.IsAllowedPlugin("me.sol.sain"));
    }

    [Fact]
    public void ADefaultDenyCannotRemovePlatformOrArenaUnits()
    {
        string[] protectedIds =
        [
            "com.SPT.core", "com.bepis.bepinex.configurationmanager", "com.maschine.ArenaMode", "com.fika.core",
            "FixPluginTypesSerialization.dll", "spt-prepatch.dll", "maschine-ArenaMode.Gate.dll",
        ];
        string deny = string.Join(",", protectedIds.Select(id => "\"" + id.ToUpperInvariant() + "\""));

        var baseline = Load(EmptyDefault);
        var policy = LoadWithDiagnostics("{ \"schema\": 1, \"mods\": [], \"deny\": [" + deny + "] }", null, out var diagnostics);

        Assert.Equal(protectedIds.Length, diagnostics.Count);
        Assert.All(diagnostics, d =>
        {
            Assert.StartsWith("default whitelist:", d);
            Assert.Contains("cannot be denied", d);
        });
        foreach (string id in protectedIds)
        {
            Assert.False(policy.IsDeniedByDefault(id), id);
            if (id.EndsWith(".dll", StringComparison.Ordinal)) Assert.True(policy.IsAllowedPatcher(id), id);
            else Assert.True(policy.IsAllowedPlugin(id), id);
        }

        Assert.True(policy.IsAllowedServerMod("com.maschine.ArenaMode"));
        Assert.Equal(baseline.Hash, policy.Hash);
    }

    // ------------------------------------------------------------------ policy: the default file cannot touch code-defined units

    private const string SainEntry =
        """{ "guid": "me.sol.sain", "name": "SAIN", "kind": "mod", "clientVersion": "4.5.1", "serverVersion": "4.5.1" }""";

    /// <summary>
    /// Every unit that code allows (platform, Arena, Fika) and whether a file entry for it is written as a patcher.
    /// Written out by hand so that a lost unit shows; <see cref="ContractConstantsMatchTheSpec"/> pins the contract
    /// lists, and <see cref="CodeDefinedUnitIds"/> adds them to the script checks below.
    /// </summary>
    private static readonly (string Id, bool IsPatcher)[] CodeDefinedUnits =
    {
        ("com.SPT.core", false), ("com.SPT.custom", false), ("com.SPT.debugging", false), ("com.SPT.singleplayer", false),
        ("com.bepis.bepinex.configurationmanager", false), ("com.maschine.ArenaMode", false), ("com.fika.core", false),
        ("FixPluginTypesSerialization.dll", true), ("spt-prepatch.dll", true), ("maschine-ArenaMode.Gate.dll", true),
    };

    /// <summary>A default-file entry that would replace the code entry of a unit: another tier, both sides, a version nobody reviewed.</summary>
    private static string ReplacingEntry(string id, bool isPatcher) => isPatcher
        ? "{ \"patcher\": \"" + id + "\", \"name\": \"Replaced\", \"kind\": \"tool\" }"
        : "{ \"guid\": \"" + id + "\", \"name\": \"Replaced\", \"kind\": \"tool\", \"clientVersion\": \"9.9.9\", \"serverVersion\": \"9.9.9\" }";

    private static string DescribeEntries(IEnumerable<ArenaWhitelistEntry> entries)
        => string.Join("\n", entries.Select(e => string.Join("|", e.Id, e.Tier, e.Name, e.ClientVersion, e.ServerVersion, e.AppliesToClient, e.AppliesToServer)));

    [Fact]
    public void ADefaultEntryThatNamesAPlatformOrArenaUnitIsSkippedWithADiagnosticAndTheCodeEntryStays()
    {
        // The shipped file is trusted data, but a mistake in it must not replace what code allows: AddEntry
        // would swap in a Mod/Tool entry with the file's sides, name and reviewed versions.
        var baseline = Load("{ \"schema\": 1, \"mods\": [ " + SainEntry + " ] }");

        foreach (var (id, isPatcher) in CodeDefinedUnits)
        {
            string defaultJson = "{ \"schema\": 1, \"mods\": [ " + ReplacingEntry(id, isPatcher) + ", " + SainEntry + " ] }";
            var policy = LoadWithDiagnostics(defaultJson, null, out var diagnostics);

            // The file stays valid; the entry costs one diagnostic that names it, and nothing else.
            var diagnostic = Assert.Single(diagnostics);
            Assert.StartsWith("default whitelist: mods[0]: '" + id + "' is ignored", diagnostic);
            Assert.Contains("platform and Arena units are fixed in code", diagnostic);
            Assert.True(policy.IsAllowedPlugin("me.sol.sain"), "the rest of the file is intact");

            // Not one entry changed (tier, name, sides, reviewed versions), and so the hash did not either.
            Assert.Equal(DescribeEntries(baseline.Entries), DescribeEntries(policy.Entries));
            Assert.Equal(baseline.Hash, policy.Hash);

            if (isPatcher)
            {
                Assert.Equal(ArenaWhitelistTier.Platform, policy.FindPatcher(id)!.Tier);
                continue;
            }

            bool isArena = id == "com.maschine.ArenaMode";
            var entry = policy.FindPlugin(id, ArenaWhitelistSide.Client)!;
            Assert.Equal(isArena ? ArenaWhitelistTier.Arena : ArenaWhitelistTier.Platform, entry.Tier);
            Assert.NotEqual("Replaced", entry.Name);
            Assert.NotEqual("9.9.9", policy.ReviewedVersion(id, ArenaWhitelistSide.Client));
            Assert.Null(policy.ReviewedVersion(id, ArenaWhitelistSide.Server));
            Assert.Equal(isArena, policy.IsAllowedServerMod(id)); // platform units are client plugins; only Arena is a server mod too

            // The decision reports the code tier and has no version to warn about.
            var client = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, new[] { Unit(id, "Unit", "4.1.6") });
            Assert.Equal(isArena ? "arena" : "platform", Assert.Single(client.Allowed).Reason);
            Assert.Empty(client.Warnings);
            if (isArena)
            {
                var server = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Server, new[] { Unit(id, "Unit", "0.1.3") });
                Assert.Equal("arena", Assert.Single(server.Allowed).Reason);
                Assert.Empty(server.Warnings);
            }
        }
    }

    [Fact]
    public void ADefaultFileSkipsEveryWrittenFormOfACodeDefinedUnitAndKeepsTheOtherEntries()
    {
        // Case, padding, a patcher given as a path, another Fika GUID: the same matching as the user file's allow list.
        var baseline = Load(SmallDefault);
        var policy = LoadWithDiagnostics("""
            {
              "schema": 1,
              "mods": [
                { "guid": "me.sol.sain", "name": "SAIN", "kind": "mod", "clientVersion": "4.5.1", "serverVersion": "4.5.1" },
                { "guid": "COM.SPT.CORE", "kind": "tool", "clientVersion": "9.9.9" },
                { "guid": "  com.maschine.arenamode  ", "serverVersion": "9.9.9" },
                { "guid": "com.acidphantasm.progressivebotsystem", "name": "APBS", "kind": "mod", "serverVersion": "2.4.1" },
                { "patcher": "BepInEx\\patchers\\SPT-PREPATCH.DLL", "kind": "tool" },
                { "guid": "COM.FIKA.Headless", "clientVersion": "9.9.9" },
                { "guid": "com.maschine.UiBridge", "name": "UiBridge", "kind": "tool" },
                { "patcher": "maschine-LogTools.dll", "name": "LogTools patcher", "kind": "tool" }
              ]
            }
            """, null, out var diagnostics);

        Assert.Equal(4, diagnostics.Count);
        Assert.Single(diagnostics, d => d.StartsWith("default whitelist: mods[1]: 'COM.SPT.CORE' is ignored"));
        Assert.Single(diagnostics, d => d.StartsWith("default whitelist: mods[2]: 'com.maschine.arenamode' is ignored"));
        Assert.Single(diagnostics, d => d.StartsWith("default whitelist: mods[4]: 'SPT-PREPATCH.DLL' is ignored"));
        Assert.Single(diagnostics, d => d.StartsWith("default whitelist: mods[5]: 'COM.FIKA.Headless' is ignored"));

        // Same units and the same hash as the default file without the four code-defined entries.
        Assert.Equal(DescribeEntries(baseline.Entries), DescribeEntries(policy.Entries));
        Assert.Equal(baseline.Hash, policy.Hash);
        Assert.Equal("4.1.6", policy.ReviewedVersion("com.SPT.core", ArenaWhitelistSide.Client));
        Assert.Equal(ArenaWhitelistTier.Platform, policy.FindPatcher("spt-prepatch.dll")!.Tier);
        Assert.Equal(ArenaWhitelistTier.Platform, policy.FindPlugin("com.fika.headless", ArenaWhitelistSide.Client)!.Tier);
        Assert.False(policy.IsAllowedServerMod("com.SPT.core"));
    }

    [Fact]
    public void ADefaultEntryAndADefaultDenyForTheSameCodeDefinedUnitNeverRemoveIt()
    {
        // The chain the external review found: the entry replaced the code entry (Platform -> Mod tier), and the
        // deny of the same name, which only protects code-tier entries, then removed it: neither SPT core nor
        // ArenaMode was allowed any more. "deny" comes first in the text on purpose: the order must not matter.
        var baseline = Load(EmptyDefault);

        foreach (var (id, isPatcher) in CodeDefinedUnits)
        {
            string defaultJson = "{ \"schema\": 1, \"deny\": [ \"" + id + "\" ], \"mods\": [ " + ReplacingEntry(id, isPatcher) + " ] }";

            foreach (string? user in new[] { null, BlacklistOnly })
            {
                var policy = LoadWithDiagnostics(defaultJson, user, out var diagnostics);

                Assert.Equal(2, diagnostics.Count);
                Assert.All(diagnostics, d => Assert.StartsWith("default whitelist:", d));
                Assert.Single(diagnostics, d => d.Contains("mods[0]") && d.Contains("is ignored") && d.Contains("cannot be listed"));
                Assert.Single(diagnostics, d => d.Contains("deny of '" + id + "' is ignored") && d.Contains("cannot be denied"));
                Assert.DoesNotContain(diagnostics, d => d.Contains("both allowed and denied"));

                Assert.False(policy.IsDeniedByDefault(id), id);
                if (isPatcher)
                {
                    Assert.True(policy.IsAllowedPatcher(id), id);
                    Assert.Equal(ArenaWhitelistTier.Platform, policy.FindPatcher(id)!.Tier);
                }
                else
                {
                    Assert.True(policy.IsAllowedPlugin(id), id);
                    Assert.NotEqual(ArenaWhitelistTier.Mod, policy.FindPlugin(id, ArenaWhitelistSide.Client)!.Tier);
                    Assert.NotEqual(ArenaWhitelistTier.Tool, policy.FindPlugin(id, ArenaWhitelistSide.Client)!.Tier);
                }

                if (user == null) Assert.Equal(baseline.Hash, policy.Hash);
            }

            if (!isPatcher)
            {
                // The decision on a real installation: still allowed, with the code tier's reason.
                var policy = Load(defaultJson);
                var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, new[] { Unit(id, "Unit", "4.1.6") });
                Assert.Empty(decision.Excluded);
                Assert.Contains(Assert.Single(decision.Allowed).Reason, new[] { "platform", "arena" });
            }
        }
    }

    [Fact]
    public void ADefaultDenyOfAPatcherGivenAsAPathCannotRemoveAPlatformPatcherEither()
    {
        var baseline = Load(EmptyDefault);
        var policy = LoadWithDiagnostics("""
            { "schema": 1, "mods": [], "deny": [ "BepInEx/patchers/maschine-ArenaMode/maschine-ArenaMode.Gate.dll", "C:\\x\\Spt-Prepatch.dll" ] }
            """, null, out var diagnostics);

        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Contains("cannot be denied", d));
        Assert.True(policy.IsAllowedPatcher("maschine-ArenaMode.Gate.dll"));
        Assert.True(policy.IsAllowedPatcher("spt-prepatch.dll"));
        Assert.False(policy.IsDeniedByDefault("maschine-ArenaMode.Gate.dll"));
        Assert.Equal(baseline.Hash, policy.Hash);
    }

    [Fact]
    public void ADefaultFileThatListsACodeDefinedUnitStillFailsOnARealDefect()
    {
        // The skip is not a way to let a broken file through: the entries around it are still validated.
        Assert.False(ArenaWhitelistPolicy.TryLoad(
            """{ "schema": 1, "mods": [ { "guid": "com.SPT.core" }, { "guid": "ok" }, { "name": "bad" } ] }""",
            null!, out var policy, out var diagnostics));
        Assert.Null(policy);
        Assert.Contains("mods[2]", diagnostics[^1]);

        Assert.False(ArenaWhitelistPolicy.TryLoad(
            """{ "schema": 1, "mods": [ { "guid": "com.maschine.ArenaMode", "kind": "plugin" } ] }""",
            null!, out _, out diagnostics));
        Assert.Contains("mods[0]", Assert.Single(diagnostics));
    }

    [Fact]
    public void ADefaultDeniedHardDependencyIsNeverPulledInAndTheWarningNamesTheDefaultList()
    {
        var installed = new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.1", "com.example.bad"),
            Unit("com.example.bad", "Bad", "1.0.0", "com.example.badlib"),
            Unit("com.example.badlib", "Bad Lib", "1.0.0"),
            Unit("com.example.other", "Other", "1.0.0"),
        };

        foreach (string? user in new[] { null, BlacklistOnly })
        {
            foreach (var side in new[] { ArenaWhitelistSide.Client, ArenaWhitelistSide.Server })
            {
                var decision = ArenaWhitelistDecision.Evaluate(Load(DenyDefault, user), side, installed);

                Assert.DoesNotContain(decision.Allowed, u => u.Guid is "me.sol.sain" or "com.example.bad");
                Assert.Equal("requires-denied:com.example.bad", Find(decision.Excluded, "me.sol.sain").Reason);
                Assert.Equal("denied", Find(decision.Excluded, "com.example.bad").Reason);
                Assert.Equal("SAIN: excluded because it requires Bad (denied by the default list)", decision.Warnings[0]);

                // The library below the denied unit is not denied itself and nobody allowed needs it: it is
                // an ordinary unlisted unit (excluded under a whitelist, allowed in blacklist mode).
                if (user == null)
                {
                    Assert.Empty(decision.Allowed);
                    Assert.Equal("not-on-whitelist", Find(decision.Excluded, "com.example.badlib").Reason);
                    Assert.Equal("not-on-whitelist", Find(decision.Excluded, "com.example.other").Reason);
                    Assert.Single(decision.Warnings);
                }
                else
                {
                    Assert.Equal(new[] { "unlisted-allowed", "unlisted-allowed" }, decision.Allowed.Select(u => u.Reason).ToArray());
                    Assert.Equal(
                        new[]
                        {
                            "SAIN: excluded because it requires Bad (denied by the default list)",
                            "policy override: 2 unlisted mod(s) allowed by your policy file: Bad Lib, Other",
                        },
                        decision.Warnings.ToArray());
                }
            }
        }

        // When the user denies the same unit too, the warning and the reason name the higher precedence: the user's.
        var both = ArenaWhitelistDecision.Evaluate(
            Load(DenyDefault, """{ "schema": 1, "deny": [ "com.example.bad" ] }"""), ArenaWhitelistSide.Client, installed);
        Assert.Equal("denied-by-user", Find(both.Excluded, "com.example.bad").Reason);
        Assert.Equal("requires-denied:com.example.bad", Find(both.Excluded, "me.sol.sain").Reason);
        Assert.Equal("SAIN: excluded because it requires Bad (denied by the user's policy file)", Assert.Single(both.Warnings));
        Assert.DoesNotContain(both.Warnings, w => w.Contains("default list"));
    }

    [Fact]
    public void ADefaultDeniedUnitDoesNotPullItsOwnDependenciesIn()
    {
        var policy = Load(DenyDefault);

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, new[]
        {
            Unit("com.example.bad", "Bad", "1.0.0", "com.example.lib"),
            Unit("com.example.lib", "Lib", "1.0.0"),
        });

        Assert.Empty(decision.Allowed);
        Assert.Equal("denied", Find(decision.Excluded, "com.example.bad").Reason);
        Assert.Equal("not-on-whitelist", Find(decision.Excluded, "com.example.lib").Reason);
        Assert.Empty(decision.Warnings);
    }

    [Fact]
    public void ADefaultDenyKeepsAServerModOutOfTheArenaServerTreeUnlessTheUserAllowsIt()
    {
        const string defaultFile = """
            {
              "schema": 1,
              "mods": [
                { "guid": "com.example.servermod", "name": "Server Mod", "kind": "mod", "serverVersion": "1.0.0" },
                { "guid": "me.sol.sain", "name": "SAIN", "kind": "mod", "serverVersion": "4.5.1" }
              ],
              "deny": [ "com.example.servermod" ]
            }
            """;
        var mods = new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.1"),
            Unit("com.example.servermod", "Server Mod", "1.0.0"),
        };

        var denied = ArenaWhitelistDecision.Evaluate(Load(defaultFile), ArenaWhitelistSide.Server, mods);
        Assert.Equal(new[] { "me.sol.sain" }, denied.Allowed.Select(u => u.Guid).ToArray());
        Assert.Equal("denied", Assert.Single(denied.Excluded).Reason);

        var lifted = ArenaWhitelistDecision.Evaluate(
            Load(defaultFile, """{ "schema": 1, "allow": [ { "guid": "com.example.servermod", "serverVersion": "1.0.1" } ] }"""),
            ArenaWhitelistSide.Server, mods);
        Assert.Empty(lifted.Excluded);
        Assert.Equal("Server Mod: version 1.0.0 differs from reviewed 1.0.1", Assert.Single(lifted.Warnings));
    }

    [Fact]
    public void AReportCarriesTheDeniedReasonThroughJson()
    {
        var policy = Load(DenyDefault);
        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, new[]
        {
            Unit("com.example.bad", "Bad", "1.0.0"),
            Unit("com.example.other", "Other", "1.0.0"),
        });

        var report = ArenaGateReport.FromDecision(policy, decision, null!);
        Assert.True(ArenaGateReport.TryParse(report.ToJson(), out var parsed, out var error), error);

        Assert.Equal(new[] { "denied", "not-on-whitelist" }, parsed!.Excluded.Select(u => u.Reason).ToArray());
        Assert.Equal(policy.Hash, parsed.PolicyHash);
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
        MustDiffer("a default deny", Load(WithDefaultDeny(SmallDefault, "com.example.x")));
        MustDiffer("another default deny", Load(WithDefaultDeny(SmallDefault, "com.example.y")));
        MustDiffer("a default denied patcher", Load(WithDefaultDeny(SmallDefault, "x.dll")));
        MustDiffer("a second default deny", Load(WithDefaultDeny(SmallDefault, "com.example.x", "com.example.y")));
        MustDiffer("a user allow that lifts a default deny",
            Load(WithDefaultDeny(SmallDefault, "com.example.x"), """{ "schema": 1, "allow": [ { "guid": "com.example.x" } ] }"""));
    }

    /// <summary>The same default file with a deny list of the given names added.</summary>
    private static string WithDefaultDeny(string defaultJson, params string[] names)
        => defaultJson.Replace("\"schema\": 1,", "\"schema\": 1, \"deny\": [ " + string.Join(", ", names.Select(n => "\"" + n + "\"")) + " ],");

    [Fact]
    public void PolicyHashIgnoresOrderCaseDuplicatesAndThePathFormOfDefaultDeniedNames()
    {
        string reference = Load(WithDefaultDeny(EmptyDefault, "com.example.a", "com.example.b", "x.dll")).Hash;

        Assert.Equal(reference, Load(WithDefaultDeny(EmptyDefault, "BepInEx/patchers/X.DLL", "COM.EXAMPLE.B", "com.example.a", "com.example.a")).Hash);
        Assert.NotEqual(reference, Load(WithDefaultDeny(EmptyDefault, "com.example.a", "com.example.b")).Hash);
        Assert.NotEqual(reference, Load(WithDefaultDeny(EmptyDefault, "com.example.a", "com.example.b", "x.dll", "y.dll")).Hash);
        Assert.NotEqual(Load(EmptyDefault).Hash, reference);
    }

    [Fact]
    public void ADefaultDenyAndAUserDenyOfTheSameNamesAreDifferentPolicies()
    {
        // A user deny is the whole filter in blacklist mode and, in whitelist mode, a policy member that cuts off
        // the units that need the denied one; a default deny is part of the build. The canonical text keeps them
        // apart, so a hash never stands for both.
        string defaultDeny = Load(WithDefaultDeny(EmptyDefault, "com.example.a")).Hash;
        string userDeny = Load(EmptyDefault, """{ "schema": 1, "unlisted": "allow", "deny": [ "com.example.a" ] }""").Hash;
        string both = Load(WithDefaultDeny(EmptyDefault, "com.example.a"),
            """{ "schema": 1, "unlisted": "allow", "deny": [ "com.example.a" ] }""").Hash;

        Assert.Equal(3, new HashSet<string> { defaultDeny, userDeny, both }.Count);
        // In whitelist mode a user deny is a policy member too (it can cut off a unit that needs the denied one), and
        // it is not the default deny of the same name.
        string whitelistUserDeny = Load(WithDefaultDeny(EmptyDefault, "com.example.a"), """{ "schema": 1, "deny": [ "com.example.b" ] }""").Hash;
        Assert.NotEqual(defaultDeny, whitelistUserDeny);
        Assert.NotEqual(Load(EmptyDefault, """{ "schema": 1, "deny": [ "com.example.a" ] }""").Hash, defaultDeny);
    }

    [Fact]
    public void PolicyHashOfAWhitelistIsNotChangedByTheUnlistedFeature()
    {
        string baseline = Load(SmallDefault).Hash;

        // The mode written out, or an empty deny list, is the same policy: the same hash. (A deny that names
        // something is not: it changes decisions, see ADenyOfAnUnlistedLibraryIsPartOfAWhitelistPolicy...)
        Assert.Equal(baseline, Load(SmallDefault, """{ "schema": 1, "unlisted": "deny" }""").Hash);
        Assert.Equal(baseline, Load(SmallDefault, """{ "schema": 1, "deny": [] }""").Hash);
        Assert.Equal(baseline, Load(SmallDefault, """{ "schema": 1, "unlisted": "deny", "deny": [] }""").Hash);
    }

    [Fact]
    public void ADenyOfAnUnlistedLibraryIsPartOfAWhitelistPolicyBecauseItCutsOffTheModsThatNeedIt()
    {
        // The default list names SAIN, which needs a library that no list names. Without a deny the library is pulled in
        // with SAIN; with a deny of the library neither is allowed (deny wins over hard dependencies).
        var installed = new[]
        {
            Unit("me.sol.sain", "SAIN", "4.5.1", "com.example.lib"),
            Unit("com.example.lib", "Lib", "1.0.0"),
        };
        var open = Load(SmallDefault);
        var denied = Load(SmallDefault, """{ "schema": 1, "deny": [ "com.example.lib" ] }""");

        Assert.Equal(new[] { "me.sol.sain", "com.example.lib" },
            ArenaWhitelistDecision.Evaluate(open, ArenaWhitelistSide.Client, installed).Allowed.Select(u => u.Guid).ToArray());
        var cutOff = ArenaWhitelistDecision.Evaluate(denied, ArenaWhitelistSide.Client, installed);
        Assert.Empty(cutOff.Allowed);
        Assert.Equal("requires-denied:com.example.lib", Find(cutOff.Excluded, "me.sol.sain").Reason);

        // So the deny is part of the policy: policies that decide differently never share a hash. Otherwise a user file
        // that changes between the click and the restart (Syncthing mirrors it) would leave a server tree built for the
        // other policy undetected by the comparison of the hashes.
        Assert.NotEqual(open.Hash, denied.Hash);
    }

    [Fact]
    public void PolicyHashIgnoresOrderCaseDuplicatesAndThePathFormOfAUserDenyInAWhitelist()
    {
        string reference = Load(EmptyDefault, """{ "schema": 1, "deny": [ "com.example.a", "com.example.b", "x.dll" ] }""").Hash;

        Assert.Equal(reference, Load(EmptyDefault,
            """{ "schema": 1, "unlisted": "deny", "deny": [ "BepInEx/patchers/X.DLL", "COM.EXAMPLE.B", "com.example.a", "com.example.a" ] }""").Hash);
        Assert.NotEqual(reference, Load(EmptyDefault, """{ "schema": 1, "deny": [ "com.example.a", "com.example.b" ] }""").Hash);
        Assert.NotEqual(reference, Load(EmptyDefault, """{ "schema": 1, "deny": [ "com.example.a", "com.example.b", "x.dll", "y.dll" ] }""").Hash);
        // A deny of a platform or Arena unit is skipped, so it is no policy member.
        Assert.Equal(Load(EmptyDefault).Hash, Load(EmptyDefault, """{ "schema": 1, "deny": [ "com.SPT.core", "com.maschine.ArenaMode" ] }""").Hash);
    }

    [Fact]
    public void PolicyHashHasAPinnedValueForAUserDenyInAWhitelist()
    {
        // The user's denied names are canonical-text lines in a whitelist as well. Both values were cross-checked with an
        // independent FNV-1a implementation of the documented text (the same one that reproduces the pins above). Same
        // rule: change them only on purpose, together with the spec.
        Assert.Equal("493905767f8ef4eb", Load(EmptyDefault, """{ "schema": 1, "deny": [ "com.example.bad", "Bad.dll" ] }""").Hash);
        Assert.Equal("6ee0d57aa28ee15f", Load(WithDefaultDeny(EmptyDefault, "com.example.bad", "Bad.dll"),
            """{ "schema": 1, "deny": [ "com.example.other" ] }""").Hash);
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
    public void PolicyHashHasAPinnedValueForTheDefaultDenyCanonicalText()
    {
        // The default-denied lines are part of the canonical text. The pins above did not change when the default
        // deny list was introduced: a default file without one hashes exactly as before. These two values were
        // cross-checked with an independent FNV-1a implementation of the documented text. Same rule: change them
        // only on purpose, together with the spec.
        Assert.Equal("35a98336da50139b", Load(WithDefaultDeny(EmptyDefault, "com.example.bad", "Bad.dll")).Hash);
        Assert.Equal("691ca16b33aeb510", Load(WithDefaultDeny(EmptyDefault, "com.example.bad", "Bad.dll"),
            """{ "schema": 1, "unlisted": "allow", "deny": [ "com.example.other" ] }""").Hash);
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
            new[] { "schema", "active", "error", "policyHash", "unlistedAllowed", "allowed", "excluded", "warnings", "patchersNeutralised", "patchersRanBeforeGate", "patchersAllowed" },
            obj.Keys.ToArray());
        Assert.Equal(1d, obj["schema"]);
        Assert.Equal(true, obj["active"]);
        Assert.Null(obj["error"]);
        Assert.Equal("0123456789abcdef", obj["policyHash"]);
        Assert.Equal(false, obj["unlistedAllowed"]);

        var allowed = Assert.IsType<List<object>>(obj["allowed"]);
        var first = Assert.IsType<Dictionary<string, object>>(allowed[0]);
        Assert.Equal(new[] { "guid", "name", "version", "file", "reason" }, first.Keys.ToArray());
        Assert.Equal("whitelist", first["reason"]);
        Assert.IsType<List<object>>(obj["warnings"]);
        Assert.IsType<List<object>>(obj["patchersNeutralised"]);
        Assert.IsType<List<object>>(obj["patchersRanBeforeGate"]);
        Assert.IsType<List<object>>(obj["patchersAllowed"]);
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
        Assert.Equal("policy override: 1 unlisted mod(s) allowed by your policy file: Other", Assert.Single(parsed.Warnings));
        Assert.True(parsed.UnlistedAllowed);
        Assert.Equal("Arena session: 1 plugins allowed, 1 excluded, 1 warnings (policy override: unlisted mods allowed)",
            parsed.SummaryLine());
    }

    [Fact]
    public void AReportRecordsWhetherThePolicyAllowsUnlistedUnitsWhateverWasInstalled()
    {
        // The flag is the policy's mode: a blacklist with nothing unlisted installed still sets it.
        var noUnits = Array.Empty<ArenaSessionUnit>();
        var blacklistPolicy = Load(SmallDefault, BlacklistUser);
        var blacklist = ArenaGateReport.FromDecision(blacklistPolicy,
            ArenaWhitelistDecision.Evaluate(blacklistPolicy, ArenaWhitelistSide.Client, noUnits), null!);
        Assert.True(blacklist.UnlistedAllowed);
        Assert.EndsWith(ArenaGateReport.PolicyOverrideSummarySuffix, blacklist.SummaryLine());

        foreach (string? user in new[] { null, """{ "schema": 1, "unlisted": "deny" }""", """{ "schema": 1, "unlisted": "deny", "deny": [ "com.example.bad" ] }""" })
        {
            var whitelist = Load(SmallDefault, user);
            var report = ArenaGateReport.FromDecision(whitelist,
                ArenaWhitelistDecision.Evaluate(whitelist, ArenaWhitelistSide.Client, noUnits), null!);
            Assert.False(report.UnlistedAllowed);
            Assert.DoesNotContain(ArenaGateReport.PolicyOverrideSummarySuffix, report.SummaryLine());
        }
    }

    [Fact]
    public void TheSummaryLineGetsThePolicyOverrideSuffixExactlyWhenThePolicyAllowsUnlistedUnits()
    {
        Assert.Equal(" (policy override: unlisted mods allowed)", ArenaGateReport.PolicyOverrideSummarySuffix);

        var report = SampleReport();
        Assert.Equal("Arena session: 2 plugins allowed, 2 excluded, 2 warnings", report.SummaryLine());

        report.UnlistedAllowed = true;
        Assert.Equal("Arena session: 2 plugins allowed, 2 excluded, 2 warnings (policy override: unlisted mods allowed)", report.SummaryLine());
    }

    [Fact]
    public void TheUnlistedAllowedFlagTravelsThroughTheJsonAndAnOlderReportReadsAsFalse()
    {
        var report = SampleReport();
        report.UnlistedAllowed = true;
        string json = report.ToJson();
        Assert.Contains("\"unlistedAllowed\":true", json);
        Assert.True(ArenaGateReport.TryParse(json, out var parsed, out var error), error);
        Assert.True(parsed!.UnlistedAllowed);
        Assert.Equal(json, parsed.ToJson());

        report.UnlistedAllowed = false;
        Assert.Contains("\"unlistedAllowed\":false", report.ToJson());
        Assert.True(ArenaGateReport.TryParse(report.ToJson(), out parsed, out error), error);
        Assert.False(parsed!.UnlistedAllowed);

        // A report from before the member existed, and one with a member of the wrong type: both false.
        foreach (string text in new[]
        {
            """{"schema":1,"active":true}""",
            """{"schema":1,"active":true,"unlistedAllowed":"true"}""",
            """{"schema":1,"active":true,"unlistedAllowed":1}""",
            """{"schema":1,"active":true,"unlistedAllowed":null}""",
        })
        {
            Assert.True(ArenaGateReport.TryParse(text, out parsed, out error), error);
            Assert.False(parsed!.UnlistedAllowed, text);
        }

        // An inactive report says nothing about the mode.
        Assert.False(ArenaGateReport.Inactive("boom", "abc").UnlistedAllowed);
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
        Assert.Equal("Global\\maschine-ArenaMode.Session.", ArenaSessionContract.HelperMutexNamePrefix);
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

    // ------------------------------------------------------------------ release scripts: the default file may not name code-defined units

    // tools/Write-ArenaAssets.ps1 stops a build, a deployment or a package, and tools/Verify-TestRelease.py rejects a
    // package, when the shipped default file lists or denies a platform, Arena or Fika unit. The scripts are run here
    // on variations of the shipped file: the PowerShell functions are lifted out of the real script with its syntax
    // tree (the script itself needs a whole source tree), the Python function is imported from the real file.

    /// <summary>The units the scripts have to refuse: the hand-written list plus everything the contract names.</summary>
    private static IEnumerable<string> CodeDefinedUnitIds()
        => CodeDefinedUnits.Select(unit => unit.Id)
            .Concat(ArenaSessionContract.SptPlatformGuids)
            .Append(ArenaSessionContract.ConfigurationManagerGuid)
            .Append(ArenaSessionContract.ArenaPluginGuid)
            .Concat(ArenaSessionContract.PlatformPatcherFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>The fragment stand-in of the case that drops ORBIT from the deny list (each script words that refusal its own way).</summary>
    private const string LostRefusedMod = "<lost refused mod>";

    /// <summary>One variation of the shipped default file. A null fragment means the script has to accept it.</summary>
    private sealed record DefaultFileCase(string Name, string? AddGuid, string? AddPatcher, string? AddDeny, string? DropDeny, string? Fragment);

    private static List<DefaultFileCase> DefaultFileCases()
    {
        var cases = new List<DefaultFileCase> { new("the shipped file", null, null, null, null, null) };
        foreach (string id in CodeDefinedUnitIds())
        {
            bool patcher = id.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            string fragment = "platform or Arena unit '" + id + "'";
            cases.Add(new("mods lists " + id, patcher ? null : id, patcher ? id : null, null, null, fragment));
            cases.Add(new("deny names " + id, null, null, id, null, fragment));
        }

        // The same units written differently.
        cases.Add(new("mods lists an upper-case GUID", "COM.SPT.CORE", null, null, null, "platform or Arena unit 'COM.SPT.CORE'"));
        cases.Add(new("mods lists a padded GUID", "  com.maschine.ArenaMode ", null, null, null, "platform or Arena unit 'com.maschine.ArenaMode'"));
        cases.Add(new("mods lists another Fika GUID", "com.fika.headless", null, null, null, "platform or Arena unit 'com.fika.headless'"));
        cases.Add(new("mods lists a patcher as a path", null, @"BepInEx\patchers\spt-prepatch.dll", null, null, @"platform or Arena unit 'BepInEx\patchers\spt-prepatch.dll'"));
        cases.Add(new("deny names an upper-case patcher", null, null, "SPT-PREPATCH.DLL", null, "platform or Arena unit 'SPT-PREPATCH.DLL'"));
        cases.Add(new("deny names another Fika GUID", null, null, "COM.FIKA.Headless", null, "platform or Arena unit 'COM.FIKA.Headless'"));

        // What the scripts checked before, so that a runner that refuses everything cannot pass the cases above.
        cases.Add(new("a lost refused mod", null, null, null, "com.chazut.orbit", LostRefusedMod));
        cases.Add(new("a mod that is both allowed and denied", "com.chazut.orbit", null, null, null, "both allows and denies"));
        return cases;
    }

    /// <summary>The message each script gives when the shipped list lost a refused mod (the two scripts word it differently).</summary>
    private static string LostRefusedModFragment(bool powerShell)
        => powerShell ? "no longer denies 'com.chazut.orbit'" : "lost its deny";

    private static string WriteDefaultFileCase(string directory, int index, DefaultFileCase item)
    {
        Assert.True(ArenaMiniJson.TryParse(File.ReadAllText(ShippedDefaultPath), out var root, out var error), error);
        var document = ArenaMiniJson.AsObject(root);
        var mods = ArenaMiniJson.GetArray(document, "mods");
        var deny = ArenaMiniJson.GetArray(document, "deny");
        if (item.AddGuid != null) mods.Add(new Dictionary<string, object> { ["guid"] = item.AddGuid, ["name"] = "Added", ["kind"] = "mod" });
        if (item.AddPatcher != null) mods.Add(new Dictionary<string, object> { ["patcher"] = item.AddPatcher, ["name"] = "Added", ["kind"] = "mod" });
        if (item.AddDeny != null) deny.Add(item.AddDeny);
        if (item.DropDeny != null) Assert.Equal(1, deny.RemoveAll(entry => entry is string text && text == item.DropDeny));

        string path = Path.Combine(directory, index.ToString(CultureInfo.InvariantCulture) + ".json");
        File.WriteAllText(path, ArenaMiniJson.Write(document));
        return path;
    }

    private const string PythonRunner = """
        import importlib.util, json, sys

        spec = importlib.util.spec_from_file_location('verify_test_release', sys.argv[1])
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        with open(sys.argv[2], encoding='utf-8') as handle:
            cases = json.load(handle)
        for case in cases:
            with open(case['path'], encoding='utf-8') as policy_file:
                policy = json.load(policy_file)
            try:
                module.check_default_whitelist(policy)
                print(case['name'] + '\tOK')
            except AssertionError as error:
                print(case['name'] + '\tFAIL\t' + str(error).replace('\n', ' '))
        """;

    private const string PowerShellRunner = """
        param([string]$ScriptPath, [string]$Manifest)
        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'
        $errors = $null
        $tree = [System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref]$null, [ref]$errors)
        if ($errors.Count -gt 0) { throw "cannot parse $ScriptPath" }
        $functions = @($tree.FindAll({ $args[0] -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false))
        foreach ($name in @('Test-ArenaProtectedUnit', 'Assert-ArenaDefaultWhitelist')) {
            $definition = @($functions | Where-Object { $_.Name -eq $name })
            if ($definition.Count -ne 1) { throw "$ScriptPath needs exactly one function $name" }
            . ([scriptblock]::Create($definition[0].Extent.Text))
        }
        foreach ($case in @(Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json)) {
            try {
                Assert-ArenaDefaultWhitelist -Path $case.path
                "$($case.name)`tOK"
            } catch {
                "$($case.name)`tFAIL`t$($_.Exception.Message)"
            }
        }
        """;

    /// <summary>Runs a program to completion; false when it cannot be started (not installed). Output is stdout only.</summary>
    private static bool TryRunProcess(string exe, IEnumerable<string> arguments, out string output, out string errors, out int exitCode)
    {
        output = errors = string.Empty;
        exitCode = -1;
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(info)!;
        }
        catch (Win32Exception)
        {
            return false;
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(120_000))
            {
                process.Kill(true);
                throw new TimeoutException(exe + " did not finish within two minutes");
            }

            output = stdout.GetAwaiter().GetResult();
            errors = stderr.GetAwaiter().GetResult();
            exitCode = process.ExitCode;
            return true;
        }
    }

    private static void CheckScriptAgainstDefaultFileCases(bool powerShell)
    {
        string exe = powerShell ? "pwsh.exe" : "python.exe";
        Skip.IfNot(
            TryRunProcess(exe, powerShell ? new[] { "-NoLogo", "-NoProfile", "-Command", "exit 0" } : new[] { "-I", "-c", "import json" },
                out _, out _, out int probe) && probe == 0,
            exe + " is needed to run the release script (PowerShell 7 and Python 3 are part of the build and package tooling)");

        var cases = DefaultFileCases();
        var directory = Directory.CreateTempSubdirectory("arena-default-file-cases-");
        try
        {
            var manifest = new List<object>();
            for (int i = 0; i < cases.Count; i++)
                manifest.Add(new Dictionary<string, object> { ["name"] = cases[i].Name, ["path"] = WriteDefaultFileCase(directory.FullName, i, cases[i]) });
            string manifestPath = Path.Combine(directory.FullName, "manifest.json");
            File.WriteAllText(manifestPath, ArenaMiniJson.Write(manifest));
            string runnerPath = Path.Combine(directory.FullName, powerShell ? "runner.ps1" : "runner.py");
            File.WriteAllText(runnerPath, powerShell ? PowerShellRunner : PythonRunner);
            string script = Path.Combine(RepoFiles.Root, "tools", powerShell ? "Write-ArenaAssets.ps1" : "Verify-TestRelease.py");
            Assert.True(File.Exists(script), "missing " + script);

            var arguments = powerShell
                ? new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", runnerPath, script, manifestPath }
                : new[] { "-I", runnerPath, script, manifestPath };
            Assert.True(TryRunProcess(exe, arguments, out string output, out string errors, out int exitCode), exe + " could not be started");
            Assert.True(exitCode == 0, exe + " runner failed (" + exitCode.ToString(CultureInfo.InvariantCulture) + "):\n" + output + "\n" + errors);

            var results = new Dictionary<string, (string Status, string Message)>();
            foreach (string line in output.Split('\n'))
            {
                string[] parts = line.TrimEnd('\r').Split('\t', 3);
                if (parts.Length >= 2 && (parts[1] == "OK" || parts[1] == "FAIL")) results[parts[0]] = (parts[1], parts.Length == 3 ? parts[2] : string.Empty);
            }

            var problems = new List<string>();
            foreach (var item in cases)
            {
                string? fragment = item.Fragment == LostRefusedMod ? LostRefusedModFragment(powerShell) : item.Fragment;
                if (!results.TryGetValue(item.Name, out var result)) problems.Add(item.Name + ": no result");
                else if (fragment == null && result.Status != "OK") problems.Add(item.Name + ": refused: " + result.Message);
                else if (fragment != null && result.Status != "FAIL") problems.Add(item.Name + ": accepted, expected a refusal naming '" + fragment + "'");
                else if (fragment != null && !result.Message.Contains(fragment, StringComparison.Ordinal))
                    problems.Add(item.Name + ": refused for another reason (expected '" + fragment + "'): " + result.Message);
            }

            Assert.True(problems.Count == 0, exe + " release script:\n" + string.Join("\n", problems));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [SkippableFact]
    public void TheShippedFileCheckOfVerifyTestReleaseRefusesACodeDefinedUnitInModsAndInDeny()
        => CheckScriptAgainstDefaultFileCases(powerShell: false);

    [SkippableFact]
    public void TheShippedFileCheckOfWriteArenaAssetsRefusesACodeDefinedUnitInModsAndInDeny()
        => CheckScriptAgainstDefaultFileCases(powerShell: true);

    // ------------------------------------------------------------------ the pieces together

    [Fact]
    public void ShippedPolicyKeepsTheInstalledModsAndExcludesTheRefusedOnesAndAForeignMod()
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
            Unit("com.maschine.Killcam", "Killcam", "1.0.1"),
            Unit("com.tyfon.uifixes", "UIFixes", "6.0.3"),
            Unit("com.chazut.orbit", "ORBIT", "1.0.0"),
            Unit("com.danw.questingbots", "Questing Bots", "1.0.0"),
            Unit("com.example.foreign", "Foreign Mod", "1.0.0"),
        };

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, installed);

        Assert.Equal(new[] { "ORBIT", "Questing Bots", "Foreign Mod" }, decision.Excluded.Select(u => u.DisplayName).ToArray());
        Assert.Equal(new[] { "denied", "denied", "not-on-whitelist" }, decision.Excluded.Select(u => u.Reason).ToArray());
        Assert.Equal(13, decision.Allowed.Count);
        Assert.Empty(decision.Warnings);
        Assert.Equal("whitelist", Find(decision.Allowed, "xyz.drakia.bigbrain").Reason);
        Assert.Equal("whitelist", Find(decision.Allowed, "com.maschine.Killcam").Reason);
        Assert.Equal("whitelist", Find(decision.Allowed, "com.tyfon.uifixes").Reason);
    }

    [Fact]
    public void ShippedPolicyAsABlacklistStillRefusesTheDefaultDeniedModsAndAddsWhatTheUserDenied()
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
            Unit("com.maschine.Killcam", "Killcam", "1.0.1"),
            Unit("com.tyfon.uifixes", "UIFixes", "6.0.3"),
            Unit("com.chazut.orbit", "ORBIT", "1.0.0"),
            Unit("com.example.foreign", "Foreign Mod", "1.0.0"),
        };

        var decision = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client, installed);

        Assert.Equal("UIFixes, ORBIT", decision.DescribeExcluded());
        Assert.Equal("denied-by-user", Find(decision.Excluded, "com.tyfon.uifixes").Reason);
        Assert.Equal("denied", Find(decision.Excluded, "com.chazut.orbit").Reason);
        Assert.Equal("unlisted-allowed", Find(decision.Allowed, "com.example.foreign").Reason);
        Assert.Equal("whitelist", Find(decision.Allowed, "com.maschine.Killcam").Reason);
        Assert.Equal("whitelist", Find(decision.Allowed, "me.sol.sain").Reason);
        Assert.Equal(
            new[]
            {
                "SAIN: version 4.5.2 differs from reviewed 4.5.1",
                "policy override: 1 unlisted mod(s) allowed by your policy file: Foreign Mod",
            },
            decision.Warnings.ToArray());
    }
}
