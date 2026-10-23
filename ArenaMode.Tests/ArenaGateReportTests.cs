using ArenaMode.Shared.Session;

namespace ArenaMode.Tests;

/// <summary>
/// The gate report's <c>patchersRanBeforeGate</c> member (docs/ARENA-SESSION-20261021.md, sections 4 and 5): the
/// patchers the gate neutralised although their initializer had already run because they sort before the gate.
/// The shared report code runs in the preloader (gate), in the client that reads the report and in the harness
/// tools/ArenaGateChecks, which checks the real thing against the real BepInEx core.
/// </summary>
public sealed class ArenaGateReportTests
{
    private const string Default = """{ "schema": 1, "mods": [ { "guid": "me.sol.sain", "name": "SAIN", "kind": "mod" } ] }""";

    private static ArenaGateReport FromEmptyDecision(IEnumerable<string>? neutralised, IEnumerable<string>? ranBefore,
        IEnumerable<string>? allowed = null, bool threeArguments = false)
    {
        Assert.True(ArenaWhitelistPolicy.TryLoad(Default, null!, out var policy, out var diagnostics), string.Join(" | ", diagnostics));
        var decision = ArenaWhitelistDecision.Evaluate(policy!, ArenaWhitelistSide.Client, Array.Empty<ArenaSessionUnit>());
        return threeArguments
            ? ArenaGateReport.FromDecision(policy!, decision, neutralised!)
            : ArenaGateReport.FromDecision(policy!, decision, neutralised!, ranBefore!, allowed!);
    }

    [Fact]
    public void ANewReportListsNoPatcherThatRanBeforeTheGate()
    {
        Assert.NotNull(new ArenaGateReport().PatchersRanBeforeGate);
        Assert.Empty(new ArenaGateReport().PatchersRanBeforeGate);
        Assert.Empty(ArenaGateReport.Inactive("boom", null!).PatchersRanBeforeGate);
    }

    [Fact]
    public void ThePatchersThatRanBeforeTheGateTravelThroughTheJsonAsTheLastMember()
    {
        var report = FromEmptyDecision(new[] { "Early.dll", "Late.dll" }, new[] { "Early.dll" });

        string json = report.ToJson();
        Assert.True(ArenaMiniJson.TryParse(json, out var root, out var error), error);
        var obj = Assert.IsType<Dictionary<string, object>>(root);
        Assert.Equal(
            new[] { "schema", "active", "error", "policyHash", "unlistedAllowed", "allowed", "excluded", "warnings", "patchersNeutralised", "patchersRanBeforeGate", "patchersAllowed" },
            obj.Keys.ToArray());
        var ranBefore = Assert.IsType<List<object>>(obj["patchersRanBeforeGate"]);
        Assert.Equal(new object[] { "Early.dll" }, ranBefore.ToArray());
        Assert.Equal(new object[] { "Early.dll", "Late.dll" }, Assert.IsType<List<object>>(obj["patchersNeutralised"]).ToArray());

        Assert.True(ArenaGateReport.TryParse(json, out var parsed, out error), error);
        Assert.Equal(new[] { "Early.dll" }, parsed!.PatchersRanBeforeGate.ToArray());
        Assert.Equal(new[] { "Early.dll", "Late.dll" }, parsed.PatchersNeutralised.ToArray());

        // And it is stable: writing the parsed report gives the same text.
        Assert.Equal(json, parsed.ToJson());
    }

    [Fact]
    public void EveryReportCarriesTheMemberAndWritesNothingButAnEmptyArrayWhenNothingRanEarly()
    {
        var reports = new[]
        {
            new ArenaGateReport(),
            ArenaGateReport.Inactive("boom", "feedfacecafebeef"),
            FromEmptyDecision(null, null),
            FromEmptyDecision(new[] { "Late.dll" }, Array.Empty<string>()),
            FromEmptyDecision(new[] { "Late.dll" }, null, threeArguments: true),
        };

        foreach (var report in reports)
        {
            Assert.Contains("\"patchersRanBeforeGate\":[]", report.ToJson());
            Assert.True(ArenaMiniJson.TryParse(report.ToJson(), out var root, out var error), error);
            var obj = Assert.IsType<Dictionary<string, object>>(root);
            Assert.Empty(Assert.IsType<List<object>>(obj["patchersRanBeforeGate"]));
        }

        // A list that was set to null is still written as the empty array.
        var cleared = new ArenaGateReport { PatchersRanBeforeGate = null! };
        Assert.Contains("\"patchersRanBeforeGate\":[]", cleared.ToJson());
    }

    [Fact]
    public void AReportOfAnOlderGateWithoutTheMemberReadsAsEmptyAndOnlyFileNamesAreRead()
    {
        Assert.True(ArenaGateReport.TryParse("""{"schema":1,"active":true,"patchersNeutralised":["a.dll"]}""", out var older, out var error), error);
        Assert.NotNull(older!.PatchersRanBeforeGate);
        Assert.Empty(older.PatchersRanBeforeGate);
        Assert.Equal(new[] { "a.dll" }, older.PatchersNeutralised.ToArray());

        Assert.True(ArenaGateReport.TryParse(
            """{"schema":1,"active":true,"patchersRanBeforeGate":["x.dll",3,null,{"a":1},"y.dll"]}""", out var tolerant, out error), error);
        Assert.Equal(new[] { "x.dll", "y.dll" }, tolerant!.PatchersRanBeforeGate.ToArray());

        // A member that is not an array reads as empty.
        Assert.True(ArenaGateReport.TryParse("""{"schema":1,"active":true,"patchersRanBeforeGate":"x.dll"}""", out var scalar, out error), error);
        Assert.Empty(scalar!.PatchersRanBeforeGate);
    }

    [Fact]
    public void FromADecisionCopiesTheListOfPatchersThatRanBeforeTheGate()
    {
        var neutralised = new List<string> { "Early.dll", "Late.dll" };
        var ranBefore = new List<string> { "Early.dll" };

        var report = FromEmptyDecision(neutralised, ranBefore);
        neutralised.Clear();
        ranBefore.Clear();

        Assert.Equal(new[] { "Early.dll" }, report.PatchersRanBeforeGate.ToArray());
        Assert.Equal(new[] { "Early.dll", "Late.dll" }, report.PatchersNeutralised.ToArray());

        // The argument is optional: a caller that does not know any leaves the list empty.
        Assert.Empty(FromEmptyDecision(new[] { "Early.dll" }, null, threeArguments: true).PatchersRanBeforeGate);
    }

    [Fact]
    public void ThePatchersTheGateLetRunTravelThroughTheJsonAndAnOlderReportReadsAsNone()
    {
        // Review 2026-10-23: the client judges the patchers the gate let run with the policy as it reads now, so the
        // report has to say which they were (patchersAllowed), in every report, copied like the other lists.
        Assert.Empty(new ArenaGateReport().PatchersAllowed);
        Assert.Empty(ArenaGateReport.Inactive("boom", null!).PatchersAllowed);

        var allowed = new List<string> { "spt-prepatch.dll", "WTT-ContentBackportPatcher.dll" };
        var report = FromEmptyDecision(new[] { "Late.dll" }, Array.Empty<string>(), allowed);
        allowed.Clear();
        Assert.Equal(new[] { "spt-prepatch.dll", "WTT-ContentBackportPatcher.dll" }, report.PatchersAllowed.ToArray());

        string json = report.ToJson();
        Assert.EndsWith("\"patchersAllowed\":[\"spt-prepatch.dll\",\"WTT-ContentBackportPatcher.dll\"]}", json);
        Assert.True(ArenaGateReport.TryParse(json, out var parsed, out var error), error);
        Assert.Equal(new[] { "spt-prepatch.dll", "WTT-ContentBackportPatcher.dll" }, parsed!.PatchersAllowed.ToArray());
        Assert.Equal(json, parsed.ToJson());

        // Every report carries the member, an empty array when the gate let nothing run; null is written as the empty array.
        Assert.Contains("\"patchersAllowed\":[]", FromEmptyDecision(new[] { "Late.dll" }, null, threeArguments: true).ToJson());
        Assert.Contains("\"patchersAllowed\":[]", ArenaGateReport.Inactive("boom", "feedfacecafebeef").ToJson());
        Assert.Contains("\"patchersAllowed\":[]", new ArenaGateReport { PatchersAllowed = null! }.ToJson());

        // A report of an older gate has no member: nothing to judge again. Only file names are read.
        Assert.True(ArenaGateReport.TryParse("""{"schema":1,"active":true,"patchersNeutralised":["a.dll"]}""", out var older, out error), error);
        Assert.Empty(older!.PatchersAllowed);
        Assert.True(ArenaGateReport.TryParse("""{"schema":1,"active":true,"patchersAllowed":["x.dll",3,null,"y.dll"]}""", out var tolerant, out error), error);
        Assert.Equal(new[] { "x.dll", "y.dll" }, tolerant!.PatchersAllowed.ToArray());
    }

    [Fact]
    public void TheSummaryLineCountsThePatchersThatRanBeforeTheGate()
    {
        const string plain = "Arena session: 0 plugins allowed, 0 excluded, 0 warnings";

        var none = FromEmptyDecision(new[] { "Late.dll" }, Array.Empty<string>());
        Assert.Equal(plain, none.SummaryLine());

        var one = FromEmptyDecision(new[] { "Early.dll" }, new[] { "Early.dll" });
        Assert.Equal(plain + " (1 patcher(s) ran before the gate)", one.SummaryLine());

        var two = FromEmptyDecision(new[] { "A.dll", "B.dll", "C.dll" }, new[] { "A.dll", "B.dll" });
        Assert.Equal(plain + " (2 patcher(s) ran before the gate)", two.SummaryLine());

        // After the policy-override suffix, never in front of it.
        two.UnlistedAllowed = true;
        Assert.Equal(plain + ArenaGateReport.PolicyOverrideSummarySuffix + " (2 patcher(s) ran before the gate)", two.SummaryLine());

        // A list that was set to null counts as empty.
        var cleared = new ArenaGateReport { PatchersRanBeforeGate = null! };
        Assert.Equal(plain, cleared.SummaryLine());
    }
}
