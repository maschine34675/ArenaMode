using System.Text.RegularExpressions;
using ArenaMode.Config;

namespace ArenaMode.Tests;

public sealed class ArenaConfigMenuLayoutTests
{
    private static readonly string[] BindFiles =
    {
        "ArenaMode.Client/Config/Settings.cs",
        "ArenaMode.Client/Config/ArenaParticipantSettings.cs",
        "ArenaMode.Client/Pickups/ArenaPickupSettings.cs",
        "ArenaMode.Client/Hud/InfoPanels/InfoPanelSettings.cs",
        "ArenaMode.Client/Combat/Fire/ArenaFireSettings.cs",
    };

    private static string Source(string relative)
        => File.ReadAllText(Path.Combine(RepoFiles.Root, relative.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>Every (section, key) the five settings files bind, read from their source.</summary>
    private static HashSet<(string Section, string Key)> BoundKeys()
    {
        var keys = new HashSet<(string, string)>();
        foreach (var file in BindFiles)
        {
            var text = Source(file);
            foreach (Match m in Regex.Matches(text, "BindArena\\(\\s*\"([^\"]+)\"\\s*,\\s*\"([^\"]+)\""))
                keys.Add((m.Groups[1].Value, m.Groups[2].Value));
            foreach (Match m in Regex.Matches(text, "BindReplay\\(\\s*config\\s*,\\s*\"([^\"]+)\"\\s*\\)"))
                keys.Add((m.Groups[1].Value, "ReplaySeconds"));
            foreach (Match m in Regex.Matches(text, "BindAnnouncer\\(\\s*config\\s*,\\s*\"([^\"]+)\""))
                keys.Add((m.Groups[1].Value, "Announcer"));
            if (file.EndsWith("InfoPanelSettings.cs", StringComparison.Ordinal))
                foreach (Match m in Regex.Matches(text, "Bind\\(\\s*config\\s*,\\s*legacy\\s*,\\s*\"([^\"]+)\"\\s*,\\s*\"([^\"]+)\""))
                    keys.Add(("HUD Panels - " + m.Groups[1].Value, m.Groups[2].Value));
        }
        return keys;
    }

    [Fact]
    public void EverySettingsFileBindsOnlyThroughTheLayout()
    {
        foreach (var file in BindFiles)
        {
            var text = Source(file);
            Assert.DoesNotMatch(@"\b(config|file)\.Bind\(", text);
            Assert.DoesNotContain("new { Browsable", text);
        }
    }

    [Fact]
    public void EveryBoundKeyHasALayoutEntry()
    {
        var bound = BoundKeys();
        Assert.True(bound.Count > 180, $"source scan found only {bound.Count} keys; the patterns no longer match the code");
        var missing = bound.Where(k => !ArenaConfigMenuLayout.TryResolve(k.Section, k.Key, out _)).ToList();
        Assert.True(missing.Count == 0, "no layout entry: " + string.Join(", ", missing.Select(k => k.Section + "/" + k.Key)));
    }

    [Fact]
    public void EveryLayoutEntryIsActuallyBound()
    {
        var bound = BoundKeys();
        var stale = ArenaConfigMenuLayout.Sections
            .SelectMany(s => s.Items.Where(i => i.Key != "*").Select(i => (s.Name, i.Key)))
            .Where(k => !bound.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "layout entry without a bind: " + string.Join(", ", stale.Select(k => k.Name + "/" + k.Key)));
        Assert.Contains("file.BindArena(\"Map participants\"", Source(BindFiles[1]));
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
                // A human-readable name, not the raw CamelCase key.
                Assert.True(item.DisplayName.Contains(' ') || item.DisplayName != item.Key || item.Key.Length <= 9,
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
                     ("Map participants", "factory4_day:deathmatch"),
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
        // An absolute count on a 0..100 range must not be drawn as a percentage.
        Assert.True(ArenaConfigMenuLayout.TryResolve("Zombie", "WaveGrowth", out var growth));
        Assert.False(growth.Percent);
        Assert.True(ArenaConfigMenuLayout.TryResolve("Deathmatch", "Rounds", out var rounds));
        Assert.False(rounds.Percent);
        Assert.True(ArenaConfigMenuLayout.TryResolve("Zombie", "ShooterPercent", out var shooters));
        Assert.True(shooters.Percent);
    }
}
