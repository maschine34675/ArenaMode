using System.Globalization;
using System.Text.RegularExpressions;
using ArenaMode.Config;

namespace ArenaMode.Tests;

public sealed class ArenaConfigMenuLayoutTests
{
    private sealed record BindSite(string File, string Section, string Key, string Statement);

    private static IEnumerable<string> ClientSources()
    {
        var root = Path.Combine(RepoFiles.Root, "ArenaMode.Client");
        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative.StartsWith("bin/", StringComparison.Ordinal) || relative.StartsWith("obj/", StringComparison.Ordinal))
                continue;
            yield return path;
        }
    }

    /// <summary>
    /// The whole call starting at <paramref name="index"/>: up to the parenthesis that
    /// closes the first one, skipping string literals (descriptions contain ';' and
    /// parentheses).
    /// </summary>
    private static string StatementFrom(string text, int index)
    {
        int open = text.IndexOf('(', index);
        if (open < 0) return text.Substring(index);
        int depth = 0;
        bool inString = false;
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (inString)
            {
                if (c == '\\') { i++; continue; }
                if (c == '"') inString = false;
                continue;
            }
            if (c == '"') { inString = true; continue; }
            if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return text.Substring(index, i - index + 1);
        }
        return text.Substring(index);
    }

    /// <summary>
    /// Every ArenaMode setting the client binds, read from the source: literal
    /// config/file Bind calls, the per-mode helpers BindReplay/BindAnnouncer and the
    /// info-panel helper (which prefixes "HUD Panels - "). legacy.Bind reads the old
    /// RaidInfoPanels file and is not an ArenaMode entry.
    /// </summary>
    private static List<BindSite> BindSites()
    {
        var sites = new List<BindSite>();
        foreach (var path in ClientSources())
        {
            var text = File.ReadAllText(path);
            var file = Path.GetFileName(path);
            foreach (Match m in Regex.Matches(text, "\\b(?:config|file|Config|_config)\\.Bind\\(\\s*\"([^\"]+)\"\\s*,\\s*\"([^\"]+)\""))
                sites.Add(new BindSite(file, m.Groups[1].Value, m.Groups[2].Value, StatementFrom(text, m.Index)));
            foreach (Match m in Regex.Matches(text, "BindReplay\\(\\s*config\\s*,\\s*\"([^\"]+)\"\\s*\\)"))
                sites.Add(new BindSite(file, m.Groups[1].Value, "ReplaySeconds", "new AcceptableValueRange<float>(0f, 10f)"));
            foreach (Match m in Regex.Matches(text, "BindAnnouncer\\(\\s*config\\s*,\\s*\"([^\"]+)\""))
                sites.Add(new BindSite(file, m.Groups[1].Value, "Announcer", ""));
            foreach (Match m in Regex.Matches(text, "Bind\\(\\s*config\\s*,\\s*legacy\\s*,\\s*\"([^\"]+)\"\\s*,\\s*\"([^\"]+)\""))
                sites.Add(new BindSite(file, "HUD Panels - " + m.Groups[1].Value, m.Groups[2].Value, StatementFrom(text, m.Index)));
        }
        return sites;
    }

    /// <summary>
    /// Bind calls whose section or key is not a literal. Each one is covered by a
    /// helper pattern above or a "*" layout entry; a new one must be added here and
    /// to the layout on purpose.
    /// </summary>
    private static readonly HashSet<string> KnownDynamicBinds = new(StringComparer.Ordinal)
    {
        "Settings.cs:config.Bind(section, \"ReplaySeconds\"",
        "Settings.cs:config.Bind(section, \"Announcer\"",
        "ArenaParticipantSettings.cs:file.Bind(\"Map participants\", key",
        "InfoPanelSettings.cs:config.Bind(\"HUD Panels - \" + section, key",
        "InfoPanelSettings.cs:legacy.Bind(section, key",
    };

    [Fact]
    public void EveryNonLiteralBindIsAKnownHelper()
    {
        var dynamic = new List<string>();
        foreach (var path in ClientSources())
        {
            var text = File.ReadAllText(path);
            foreach (Match m in Regex.Matches(text, "\\b\\w+\\.Bind\\((?!\\s*\"[^\"]+\"\\s*,\\s*\")([^,]+,[^,]+)"))
            {
                var site = Path.GetFileName(path) + ":" + text.Substring(m.Index, m.Length).Trim();
                if (site.Contains(".Bind(config)", StringComparison.Ordinal) || site.Contains("Roster.Bind(", StringComparison.Ordinal))
                    continue;
                dynamic.Add(site);
            }
        }
        var unknown = dynamic.Where(d => !KnownDynamicBinds.Contains(d)).ToList();
        Assert.True(unknown.Count == 0, "unlisted dynamic Bind call(s): " + string.Join(" | ", unknown));
    }

    [Fact]
    public void EveryBoundKeyHasALayoutEntry()
    {
        var sites = BindSites();
        Assert.True(sites.Count > 180, $"source scan found only {sites.Count} binds; the patterns no longer match the code");
        var missing = sites.Where(s => !ArenaConfigMenuLayout.TryResolve(s.Section, s.Key, out _)).ToList();
        Assert.True(missing.Count == 0, "no layout entry: " + string.Join(", ", missing.Select(s => $"{s.File} {s.Section}/{s.Key}")));
        // The per-map participant keys are built at runtime (map_solo / map_team).
        Assert.True(ArenaConfigMenuLayout.TryResolve("Map participants", "factory4_day_solo", out var participants));
        Assert.True(participants.Hidden);
    }

    [Fact]
    public void EveryLayoutEntryIsActuallyBound()
    {
        var bound = BindSites().Select(s => (s.Section, s.Key)).ToHashSet();
        var stale = ArenaConfigMenuLayout.Sections
            .SelectMany(s => s.Items.Where(i => i.Key != "*").Select(i => (s.Name, i.Key)))
            .Where(k => !bound.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "layout entry without a bind: " + string.Join(", ", stale.Select(k => k.Name + "/" + k.Key)));
    }

    [Fact]
    public void PercentFlagsMatchTheBoundRanges()
    {
        var percentLooking = new HashSet<(string, string)>();
        foreach (var site in BindSites())
        {
            var m = Regex.Match(site.Statement, "AcceptableValueRange<(int|float)>\\(\\s*([-0-9.]+)f?\\s*,\\s*([-0-9.]+)f?\\s*\\)");
            if (!m.Success) continue;
            object min, max;
            if (m.Groups[1].Value == "int")
            {
                min = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                max = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            }
            else
            {
                min = float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                max = float.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            }
            if (ArenaConfigMenuLayout.LooksLikePercentRange(min, max))
                percentLooking.Add((site.Section, site.Key));
        }
        Assert.True(percentLooking.Count >= 8, $"range scan found only {percentLooking.Count} percent-looking ranges");

        var flagged = ArenaConfigMenuLayout.Sections
            .SelectMany(s => s.Items.Where(i => i.Percent).Select(i => (s.Name, i.Key))).ToHashSet();
        // A Percent flag only means something on a range the manager would guess as percent.
        Assert.True(flagged.IsSubsetOf(percentLooking),
            "percent flag on a non-percent range: " + string.Join(", ", flagged.Except(percentLooking)));
        // Every percent-looking range needs a decision: these two are absolute counts.
        var absolute = percentLooking.Except(flagged).OrderBy(k => k.Item1 + "/" + k.Item2).ToArray();
        Assert.Equal(new[] { ("Deathmatch", "Rounds"), ("Zombie", "WaveGrowth") }, absolute);
    }

    [Fact]
    public void SectionsAndKeysAreUniqueAndNamesAreReadable()
    {
        var sections = ArenaConfigMenuLayout.Sections.Select(s => s.Name).ToList();
        Assert.Equal(sections.Count, sections.Distinct(StringComparer.Ordinal).Count());
        foreach (var section in ArenaConfigMenuLayout.Sections)
        {
            Assert.NotEmpty(section.Items);
            var keys = section.Items.Select(i => i.Key).ToList();
            Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
            var shown = section.Items.Where(i => !i.Hidden).ToList();
            var names = shown.Select(i => i.DisplayName).ToList();
            Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
            foreach (var item in shown)
            {
                Assert.False(string.IsNullOrWhiteSpace(item.DisplayName), section.Name + "/" + item.Key);
                Assert.Equal(item.DisplayName.Trim(), item.DisplayName);
                // A human-readable name, not the raw CamelCase key (single words like "Killfeed" excepted).
                Assert.True(item.DisplayName != item.Key || !Regex.IsMatch(item.Key, "[a-z][A-Z]"),
                    $"{section.Name}/{item.Key} still shows its raw key");
            }
        }
    }

    [Fact]
    public void OrderFollowsTheTableTopFirst()
    {
        foreach (var section in ArenaConfigMenuLayout.Sections)
        {
            int previous = int.MaxValue;
            foreach (var item in section.Items.Where(i => i.Key != "*"))
            {
                Assert.True(ArenaConfigMenuLayout.TryResolve(section.Name, item.Key, out var resolved));
                Assert.True(resolved.Order < previous, $"{section.Name}/{item.Key} is not below its predecessor");
                Assert.True(resolved.Order > 0);
                previous = resolved.Order;
            }
        }
        Assert.True(ArenaConfigMenuLayout.TryResolve("General", "Maps", out var maps));
        Assert.True(ArenaConfigMenuLayout.TryResolve("General", "GiveUpHotkey", out var giveUp));
        Assert.True(maps.Order > giveUp.Order);
    }

    [Fact]
    public void ImportantSectionsComeFirstAndToolsLast()
    {
        int Rank(string s) => ArenaConfigMenuLayout.SectionRank(s);
        Assert.Equal(0, Rank("General"));
        Assert.True(Rank("Match") < Rank("Deathmatch"));
        Assert.True(Rank("Zombie") < Rank("Kits"));
        Assert.True(Rank("HUD") < Rank("HUD Panels - General"));
        Assert.True(Rank("Effects") < Rank("Performance"));
        Assert.True(Rank("Performance") < Rank("Diagnostics"));
        Assert.True(Rank("Diagnostics") < Rank("Development - Area outline"));
        Assert.True(Rank("Development - Area outline") < Rank("Development - Pickups"));
        // An unknown section goes after every listed one, never before General.
        Assert.Equal(ArenaConfigMenuLayout.Sections.Length, Rank("Something New"));
        Assert.Equal(ArenaConfigMenuLayout.Sections.Length, Rank(null!));
    }

    [Fact]
    public void DiagnosticsAndDevelopmentToolsAreAdvancedAndNothingElseIs()
    {
        var advanced = ArenaConfigMenuLayout.Sections.Where(s => s.Advanced).Select(s => s.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "Development - Area outline", "Development - Pickups", "Diagnostics" }, advanced);
        Assert.True(ArenaConfigMenuLayout.TryResolve("Diagnostics", "DeveloperMode", out var dev));
        Assert.True(dev.Advanced);
        Assert.True(ArenaConfigMenuLayout.TryResolve("Development - Pickups", "Export", out var export));
        Assert.True(export.Advanced);
        Assert.True(ArenaConfigMenuLayout.TryResolve("Zombie", "MaxAlive", out var maxAlive));
        Assert.False(maxAlive.Advanced);
    }

    [Fact]
    public void InternalMarkersAndMapScreenStateAreHidden()
    {
        foreach (var (section, key) in new[]
                 {
                     ("General", "OptionalMapsMigrated"), ("General", "OptionalMapsRevision"),
                     ("Deathmatch", "LegacyRulesMigrated"), ("Arena pickups", "M60DefaultMigrated"),
                     ("Match", "RoundKillTarget"), ("Match", "RoundTimeLimitSeconds"),
                     ("Map participants", "bigmap_team"),
                 })
        {
            Assert.True(ArenaConfigMenuLayout.TryResolve(section, key, out var resolved), section + "/" + key);
            Assert.True(resolved.Hidden, section + "/" + key);
        }
        Assert.True(ArenaConfigMenuLayout.TryResolve("Match", "GameMode", out var mode));
        Assert.False(mode.Hidden);
        var hidden = ArenaConfigMenuLayout.Sections.SelectMany(s => s.Items).Count(i => i.Hidden);
        Assert.Equal(7, hidden);
    }

    [Fact]
    public void UnknownEntriesAreNotResolved()
    {
        Assert.False(ArenaConfigMenuLayout.TryResolve("General", "NoSuchKey", out _));
        Assert.False(ArenaConfigMenuLayout.TryResolve("No Such Section", "Enabled", out _));
        Assert.False(ArenaConfigMenuLayout.TryResolve(null!, "Enabled", out _));
        Assert.False(ArenaConfigMenuLayout.TryResolve("General", null!, out _));
    }

    [Fact]
    public void PercentGuessMatchesTheConfigurationManagersOwnRule()
    {
        Assert.True(ArenaConfigMenuLayout.LooksLikePercentRange(0, 100));
        Assert.True(ArenaConfigMenuLayout.LooksLikePercentRange(1, 100));
        Assert.True(ArenaConfigMenuLayout.LooksLikePercentRange(0f, 1f));
        Assert.False(ArenaConfigMenuLayout.LooksLikePercentRange(2, 100));
        Assert.False(ArenaConfigMenuLayout.LooksLikePercentRange(0, 1000));
        Assert.False(ArenaConfigMenuLayout.LooksLikePercentRange(0.1f, 1f));
        Assert.False(ArenaConfigMenuLayout.LooksLikePercentRange(0f, 2f));
        Assert.False(ArenaConfigMenuLayout.LooksLikePercentRange(null, null));
    }

    [Fact]
    public void PluginAppliesTheLayoutLastAndTheSettingsFilesStayPlain()
    {
        var plugin = File.ReadAllText(Path.Combine(RepoFiles.Root, "ArenaMode.Client", "Plugin.cs"));
        int apply = plugin.IndexOf("ArenaConfigMenu.Apply(Config);", StringComparison.Ordinal);
        int fire = plugin.IndexOf("ArenaFireRuntime.Enable(Config);", StringComparison.Ordinal);
        Assert.True(apply > 0 && fire > 0 && apply > fire, "ArenaConfigMenu.Apply must run after the last bind (ArenaFireRuntime.Enable)");
        // Offline harnesses compile these files against stub ConfigFiles: no extension binds.
        foreach (var path in ClientSources())
            Assert.DoesNotContain("BindArena", File.ReadAllText(path));
    }
}
