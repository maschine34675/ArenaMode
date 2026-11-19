using System.Text.Json.Nodes;
using System.Reflection;
using ArenaMode.Server;
using ArenaMode.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Json;

namespace ArenaMode.Tests;

/// <summary>
/// The pool generator's one promise that is not about weapons: a broken
/// database must not take the server down. The exclusion index is the first
/// thing at load that forces the English locale, and the first version of it
/// ran outside the fail-open blocks, so a locale that threw would have stopped
/// the server where the old code logged and fell back to the shipped pool.
/// </summary>
[Collection(ArenaContextSerialCollection.Name)]
public sealed class LadderGeneratorTests
{
    [Fact]
    public async Task AFaultyLocaleLeavesTheShippedPoolAndTheServerUp()
    {
        QuietLogger.Clear();
        var ladderBefore = ArenaContext.LadderJson;
        var configBefore = ArenaContext.Config;
        try
        {
            ArenaContext.LadderJson = "[{\"id\":\"shipped\"}]";
            ArenaContext.Config = new ArenaModeConfig();

            // One template, so the index has a name to ask for, and a locale
            // whose lazy load throws the moment it is asked.
            var tpl = new MongoId("5e85aac65505fa48730d8af2");
            var templates = new TemplateTable
            {
                Items = new Dictionary<MongoId, TemplateItem>
                {
                    [tpl] = new TemplateItem { Id = tpl, Name = "patron_23x75_cheremukha_7m", Parent = new MongoId("5485a8684bdc2da71d8b4567") },
                },
                Character = null!, CustomisationStorage = null!, Prestige = null!, Quests = null!,
                RepeatableQuests = null!, Handbook = null!, Customization = null!, Dialogue = null!,
                Profiles = null!, Prices = null!, DefaultEquipmentPresets = null!, Achievements = null!,
                CustomAchievements = null!, LocationServices = null!,
            };
            var locales = new LocaleTable
            {
                Global = new Dictionary<string, LazyLoad<GlobalLocaleDictionary>>
                {
                    ["en"] = new LazyLoad<GlobalLocaleDictionary>(() => throw new InvalidOperationException("corrupt locale")),
                },
                Menu = new Dictionary<string, Dictionary<string, object>>(),
                Languages = new Dictionary<string, string>(),
            };
            var globals = (GlobalTable)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GlobalTable));
            var logger = QuietLogger.Create<LadderGenerator>();

            var generator = new LadderGenerator(templates, globals, locales, logger);

            // Must not throw: SPT awaits every IOnLoad without a catch of its
            // own, and an exception here is a server that does not start.
            await generator.OnLoadAsync();

            Assert.Equal("[{\"id\":\"shipped\"}]", ArenaContext.LadderJson);
            Assert.Contains(QuietLogger.Lines, line => line.Contains("Exclusion index failed", StringComparison.Ordinal));
        }
        finally
        {
            ArenaContext.LadderJson = ladderBefore;
            ArenaContext.Config = configBefore;
            ArenaContext.WeaponVariants = null;
        }
    }

    // ------------------------------------------------------------------ the preset guard
    //
    // These run over the installed vanilla database (SPT_DATABASE) and skip without it; the harness
    // tools/WeaponPoolGuardChecks runs them through Assert-ArenaTestRun and fails on a skip. The fixture is not
    // the live pool (no handbook, no English locale, see WeaponPoolFixture): it compares builds of the generator
    // over identical input and says nothing about the count a player's server serves.

    [SkippableFact]
    public async Task GuardDoesNotChangeTheVanillaPool()
    {
        Skip.If(RepoFiles.ItemDatabase == null, "real item database required");

        var items = WeaponPoolFixture.LoadTemplates();
        var presets = WeaponPoolFixture.LoadPresets();

        var guarded = await WeaponPoolFixture.GenerateAsync(items, presets, guard: true);
        var unguarded = await WeaponPoolFixture.GenerateAsync(items, presets, guard: false);

        // A pool that really was generated, not the shipped fallback after a failure.
        Assert.True(guarded.Pool.Count >= 100, $"only {guarded.Pool.Count} entries");
        Assert.Equal(0, guarded.Count("Error", "generation failed"));
        Assert.Equal(0, guarded.Count("Warning", "left out"));
        Assert.Equal(WeaponPoolFixture.Normalise(unguarded.Json), WeaponPoolFixture.Normalise(guarded.Json));
        Assert.Equal(Enumerable.Range(0, guarded.Pool.Count), guarded.Pool.Select(e => e!["order"]!.GetValue<int>()));
    }

    /// <summary>
    /// A weapon with several vanilla presets, none of them named "default", plus one injected preset of it that
    /// IS named "default" (so Plainer picks it) and carries a part whose parent is in no node. That is the
    /// failure the guard exists for: before it, the build of this one preset threw inside the generator's single
    /// try and the server fell back to the shipped 24-entry ladder.
    /// </summary>
    private static (Dictionary<MongoId, Preset> Presets, MongoId Weapon) WithBrokenDefaultPreset(
        Dictionary<MongoId, TemplateItem> items, Dictionary<MongoId, Preset> vanilla, HashSet<string> pooledWeapons)
    {
        var weapon = vanilla.Values
            .Where(p => WeaponPoolFixture.RootOf(p) is { } root && WeaponPoolFixture.IsWeapon(items, root))
            .GroupBy(p => WeaponPoolFixture.RootOf(p)!.Value)
            .Where(g => g.Count() >= 2
                        && pooledWeapons.Contains(g.Key.ToString())
                        && g.All(p => p.Name?.Contains("default", StringComparison.OrdinalIgnoreCase) != true))
            .OrderBy(g => g.Key.ToString(), StringComparer.Ordinal)
            .First();

        var source = weapon.OrderBy(p => p.Id.ToString(), StringComparer.Ordinal).First();
        var broken = new List<Item>(source.Items)
        {
            new() { Id = new MongoId("d00000000000000000000001"), Template = source.Items[0].Template, ParentId = "cccccccccccccccccccccccc", SlotId = "mod_nowhere" },
        };

        var injected = new Preset
        {
            Id = new MongoId("d00000000000000000000002"),
            Name = "Injected default",
            Parent = source.Parent,
            Items = broken,
            Encyclopedia = weapon.Key,
        };

        return (new Dictionary<MongoId, Preset>(vanilla) { [injected.Id] = injected }, weapon.Key);
    }

    private static string WeaponOf(JsonNode? entry)
        => entry!["items"]!.AsArray().First(n => n!["slotId"]?.GetValue<string>() == "FirstPrimaryWeapon")!["_tpl"]!.GetValue<string>();

    private static HashSet<string> PooledWeapons(WeaponPoolFixture.Run run) => run.Pool.Select(WeaponOf).ToHashSet();

    [SkippableFact]
    public async Task OneBrokenPresetCostsOnlyItself()
    {
        Skip.If(RepoFiles.ItemDatabase == null, "real item database required");

        var items = WeaponPoolFixture.LoadTemplates();
        var vanilla = WeaponPoolFixture.LoadPresets();
        var baseline = await WeaponPoolFixture.GenerateAsync(items, vanilla);
        var (presets, weapon) = WithBrokenDefaultPreset(items, vanilla, PooledWeapons(baseline));

        // Precondition of the dense-numbering assertions below: a skipped candidate leaves a hole only when a
        // later rung exists, so the injected weapon must not be the last one in score order.
        var position = baseline.Pool.Select(WeaponOf).ToList().IndexOf(weapon.ToString());
        Assert.InRange(position, 0, baseline.Pool.Count - 2);

        // Guard on: the broken preset is left out BEFORE Plainer, so the weapon keeps its vanilla build and the
        // pool is exactly the baseline. One Warning names the preset and the reason; nothing fell back.
        var guarded = await WeaponPoolFixture.GenerateAsync(items, presets);
        Assert.Equal(0, guarded.Count("Error", "generation failed"));
        Assert.Equal(1, guarded.Count("Warning", "left out"));
        Assert.Equal(1, guarded.Count("Warning", "'Injected default'"));
        Assert.Equal(1, guarded.Count("Warning", "orphan:"));
        Assert.Equal(WeaponPoolFixture.Normalise(baseline.Json), WeaponPoolFixture.Normalise(guarded.Json));

        // Guard off, isolation on: the build of the injected preset throws and costs exactly that candidate
        // (here the weapon, whose only candidate it was); numbering stays dense and nothing else is lost.
        var isolated = await WeaponPoolFixture.GenerateAsync(items, presets, guard: false);
        Assert.Equal(0, isolated.Count("Error", "generation failed"));
        Assert.Equal(1, isolated.Count("Warning", "build-threw:KeyNotFoundException"));
        Assert.Equal(baseline.Pool.Count - 1, isolated.Pool.Count);
        Assert.DoesNotContain(weapon.ToString(), PooledWeapons(isolated));
        Assert.Equal(Enumerable.Range(0, isolated.Pool.Count), isolated.Pool.Select(e => e!["order"]!.GetValue<int>()));
        Assert.Equal(Enumerable.Range(1, isolated.Pool.Count).Select(n => $"rung{n:000}"), isolated.Pool.Select(e => e!["id"]!.GetValue<string>()));
    }

    [SkippableFact]
    public async Task AFailingReportNeverDiscardsAGoodPool()
    {
        Skip.If(RepoFiles.ItemDatabase == null, "real item database required");

        var items = WeaponPoolFixture.LoadTemplates();
        var vanilla = WeaponPoolFixture.LoadPresets();
        var baseline = await WeaponPoolFixture.GenerateAsync(items, vanilla);
        var (presets, _) = WithBrokenDefaultPreset(items, vanilla, PooledWeapons(baseline));

        // A logger whose Warning throws: the report fails, the pool is still served.
        var ladderBefore = ArenaContext.LadderJson;
        var configBefore = ArenaContext.Config;
        var variantsBefore = ArenaContext.WeaponVariants;
        try
        {
            ArenaContext.LadderJson = "[{\"id\":\"shipped\"}]";
            ArenaContext.Config = new ArenaModeConfig();
            QuietLogger.Clear();
            QuietLogger.ThrowOnWarning = true;
            var generator = new LadderGenerator(WeaponPoolFixture.Templates(items), WeaponPoolFixture.Globals(presets),
                WeaponPoolFixture.EmptyLocales(), QuietLogger.Create<LadderGenerator>());
            await generator.OnLoadAsync();

            Assert.Equal(baseline.Pool.Count, JsonNode.Parse(ArenaContext.LadderJson)!.AsArray().Count);
            Assert.DoesNotContain(QuietLogger.Entries, e => e.Level == "Error");
            Assert.Contains(QuietLogger.Entries, e => e.Level == "Debug" && e.Text.Contains("left-out report failed", StringComparison.Ordinal));
        }
        finally
        {
            QuietLogger.Clear();
            ArenaContext.LadderJson = ladderBefore;
            ArenaContext.Config = configBefore;
            ArenaContext.WeaponVariants = variantsBefore;
        }
    }

    [SkippableFact]
    public async Task NullElementsAreContainedAndOnlyAWeaponIsReported()
    {
        Skip.If(RepoFiles.ItemDatabase == null, "real item database required");

        var items = WeaponPoolFixture.LoadTemplates();
        var vanilla = WeaponPoolFixture.LoadPresets();
        var baseline = await WeaponPoolFixture.GenerateAsync(items, vanilla);

        var weapon = WeaponOf(baseline.Pool[0]);
        var presets = new Dictionary<MongoId, Preset>(vanilla)
        {
            // Not a weapon, no Encyclopedia, a null element: the root lookup itself throws. Nobody's business.
            [new MongoId("d00000000000000000000003")] = new Preset { Id = new MongoId("d00000000000000000000003"), Name = "Armour with a hole", Parent = new MongoId("d00000000000000000000009"), Items = [null!] },
            // A weapon (known by its Encyclopedia) with a null element: reported, and only itself is lost.
            // A null preset: the root lookup throws before any weapon is known, so the net contains it silently.
            [new MongoId("d00000000000000000000002")] = null!,
            [new MongoId("d00000000000000000000004")] = new Preset { Id = new MongoId("d00000000000000000000004"), Name = "Weapon with a hole", Parent = new MongoId("d00000000000000000000009"), Items = [null!], Encyclopedia = new MongoId(weapon) },
            // No Encyclopedia, a null element BEFORE the root: the weapon is still found through the root node, so the
            // hole is reported (no-parts) instead of the preset vanishing silently.
            [new MongoId("d00000000000000000000005")] = new Preset { Id = new MongoId("d00000000000000000000005"), Name = "Hole before root", Parent = new MongoId("d00000000000000000000006"), Items = [null!, new Item { Id = new MongoId("d00000000000000000000006"), Template = new MongoId(weapon) }] },
            // No Encyclopedia and no parentless node: the declared root part (Preset.Parent) names the weapon, so the
            // preset is reported as no-root instead of being lost without a trace.
            [new MongoId("d00000000000000000000007")] = new Preset { Id = new MongoId("d00000000000000000000007"), Name = "No root part", Parent = new MongoId("d00000000000000000000008"), Items = [new Item { Id = new MongoId("d00000000000000000000008"), Template = new MongoId(weapon), ParentId = "d00000000000000000000008", SlotId = "mod_barrel" }] },
        };

        var run = await WeaponPoolFixture.GenerateAsync(items, presets);

        Assert.Equal(0, run.Count("Error", "generation failed"));
        Assert.Equal(1, run.Count("Warning", "left out"));
        Assert.Equal(1, run.Count("Warning", "'Weapon with a hole'"));
        // The reasons are read from the per-entry Debug lines: the Warning shows only the first three entries.
        void Reported(string name, string reason)
            => Assert.Equal(1, run.Log.Count(l => l.Level == "Debug" && l.Text.Contains($"preset '{name}'", StringComparison.Ordinal)
                                                  && l.Text.EndsWith("left out: " + reason, StringComparison.Ordinal)));
        Reported("Weapon with a hole", "no-parts");
        Reported("Hole before root", "no-parts");
        Reported("No root part", "no-root");
        Assert.Equal(0, run.Count("Warning", "Armour with a hole"));
        Assert.Equal(WeaponPoolFixture.Normalise(baseline.Json), WeaponPoolFixture.Normalise(run.Json));
    }

    /// <summary>A logger that keeps what it was told and otherwise does nothing.</summary>
    public class QuietLogger : DispatchProxy
    {
        public static readonly List<string> Lines = new();

        /// <summary>Every message with the logger method that carried it ("Warning", "Debug", "Error", ...).</summary>
        public static readonly List<(string Level, string Text)> Entries = new();

        /// <summary>A logger whose Warning throws, for the test that reporting never discards a pool.</summary>
        public static bool ThrowOnWarning;

        /// <summary>Tests clear both lists at their start; the collection is serial, so nothing else writes meanwhile.</summary>
        public static void Clear()
        {
            Lines.Clear();
            Entries.Clear();
            ThrowOnWarning = false;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            // Only the left-out report: other Warnings of the load (the variant index) are not under test.
            if (targetMethod?.Name == "Warning" && ThrowOnWarning && args?[0] is string warning && warning.Contains("Weapon pool: ", StringComparison.Ordinal))
                throw new InvalidOperationException("the logger refused the warning");

            if (args is { Length: > 0 } && args[0] is string text)
            {
                Lines.Add(text);
                Entries.Add((targetMethod?.Name ?? "?", text));
            }

            return targetMethod?.ReturnType == typeof(void)
                ? null
                : targetMethod?.ReturnType.IsValueType == true
                    ? Activator.CreateInstance(targetMethod.ReturnType)
                    : null;
        }

        public static ISptLogger<T> Create<T>() => DispatchProxy.Create<ISptLogger<T>, QuietLogger>();
    }
}
