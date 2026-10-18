using System.Reflection;
using System.Text.Json;
using ArenaMode.Session;
using EFT;
using JsonType;
using Redeploy.Core.Compat;

internal static class Program
{
    private static int _checks;
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    private static readonly MethodInfo Keycard = typeof(MapAccessPatch)
        .GetNestedType("ArenaMapAccessKeycardBypassPatch", BindingFlags.NonPublic)!
        .GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void Assert(bool condition, string text)
    {
        if (!condition) throw new InvalidOperationException(text);
        _checks++;
    }

    private static LocationSettings.Location Labyrinth(bool upstream = false, bool emptyKeys = false) => new()
    {
        Id = "Labyrinth", Name = "A upstream name", Enabled = upstream,
        ForceOnlineRaidInPVE = !upstream, IconX = 245, IconY = upstream ? 350 : 325,
        AccessKeys = emptyKeys ? [] : ["679b9819a2f2dd4da9023512"],
        RequiredPlayerLevelMin = 0, RequiredPlayerLevelMax = 100, DisabledForScav = upstream,
        Scene = new object(), Exits = new[] { "labir_exit", "optional-upstream-exit" },
    };

    private static RaidSettings Select(LocationSettings.Location previous)
    {
        ArenaMenuSelection.EndFlow();
        var settings = new RaidSettings
        {
            Side = ESideType.Pmc, SelectedLocation = previous, SelectedDateTime = EDateTime.PAST,
            transitionType = ELocationTransition.Event, KeyId = "original-key",
        };
        ArenaMenuSelection.Begin(new object(), settings);
        ArenaMenuSelection.Select();
        settings.transitionType = ELocationTransition.Common;
        return settings;
    }

    private static bool WaiveKey(RaidSettings settings, out bool result)
    {
        object[] args = [new MainMenuShowOperation { raidSettings_0 = settings }, false];
        var runOriginal = (bool)Keycard.Invoke(null, args)!;
        result = (bool)args[1];
        return runOriginal;
    }

    private static void StandaloneProjectionAndRollback()
    {
        var original = Labyrinth();
        var baseline = JsonSerializer.Serialize(original, Json);
        var settings = Select(original);
        var projected = ArenaLabyrinthAccess.Project(settings, original);
        Assert(!ReferenceEquals(original, projected), "Standalone metadata must be copied before unlocking the Arena view");
        Assert(projected.Enabled && !projected.ForceOnlineRaidInPVE && projected.IconY == 350,
            "Standalone Arena projection must expose local-PVE Labyrinth at the native visible tile position");
        Assert(JsonSerializer.Serialize(original, Json) == baseline, "Projection must leave the shared original byte-identical");
        Assert(ReferenceEquals(projected.Scene, original.Scene) && ReferenceEquals(projected.Exits, original.Exits)
               && ReferenceEquals(projected.AccessKeys, original.AccessKeys),
            "Projection retains existing scene/exits/keys without creating or editing third-party data");
        Assert(projected.IconX == original.IconX && projected.Locked == original.Locked
               && projected.DisabledForScav == original.DisabledForScav
               && projected.RequiredPlayerLevelMax == original.RequiredPlayerLevelMax,
            "No Scav, level, lock or unrelated map fields may change");
        Assert(ReferenceEquals(projected, ArenaLabyrinthAccess.Project(settings, projected)),
            "An existing Arena projection must not be cloned twice");
        settings.SelectedLocation = projected;
        Assert(!settings.NeedToGoOnlineInPve, "Projected metadata must remove native force-online with the default local preference");
        settings.OnlinePveRaid = true;
        Assert(settings.NeedToGoOnlineInPve, "User online-PVE preference must remain untouched");
        Assert(!WaiveKey(settings, out var allowed) && allowed && settings.KeyId == string.Empty,
            "Existing exact Arena key waiver must cover standalone Labyrinth without consuming an item");

        // Another mod changes its original metadata while our copied menu is
        // alive. Ending Arena must restore the reference, never old field values.
        original.IconY = 410;
        original.ForceOnlineRaidInPVE = false;
        original.AccessKeys = ["upstream-new-key"];
        ArenaMenuSelection.EndFlow();
        Assert(ReferenceEquals(settings.SelectedLocation, original)
               && original.IconY == 410 && !original.ForceOnlineRaidInPVE
               && original.AccessKeys[0] == "upstream-new-key",
            "Ending Arena must not overwrite external changes made to original map metadata");
        Assert(settings.SelectedDateTime == EDateTime.PAST && settings.transitionType == ELocationTransition.Event
               && settings.KeyId == "original-key" && settings.OnlinePveRaid,
            "Actual menu rollback restores pre-Arena fields without writing user online preferences");
        Assert(ReferenceEquals(original, ArenaLabyrinthAccess.Project(settings, original)),
            "The same settings object becomes ordinary after ending Arena");
        Assert(WaiveKey(settings, out _) && settings.KeyId == "original-key",
            "Normal raids must regain the native keycard check after leaving Arena");
    }

    private static void CoInstalledProjection()
    {
        foreach (var emptyKeys in new[] { false, true })
        {
            var original = Labyrinth(upstream: true, emptyKeys);
            var before = JsonSerializer.Serialize(original, Json);
            var settings = Select(original);
            for (var i = 0; i < 6; i++)
                Assert(ReferenceEquals(original, ArenaLabyrinthAccess.Project(settings, original)),
                    "Co-installed EnableLabyrinth metadata needs no extra clone or repeated mutation");
            Assert(JsonSerializer.Serialize(original, Json) == before,
                "Co-installed keys, Scav setting and exits remain byte-identical");
            Assert(WaiveKey(settings, out var waived) == emptyKeys && (emptyKeys || waived),
                "Native card check runs when upstream removed keys, otherwise exact Arena waiver still applies");
            ArenaMenuSelection.EndFlow();
            Assert(JsonSerializer.Serialize(original, Json) == before,
                "Leaving co-installed Arena must preserve upstream normal-raid configuration");
            foreach (var side in new[] { ESideType.Pmc, ESideType.Savage })
            {
                settings.Side = side;
                Assert(ReferenceEquals(settings.SelectedLocation, original)
                       && ReferenceEquals(original, ArenaLabyrinthAccess.Project(settings, original))
                       && WaiveKey(settings, out _),
                    "Returning to ordinary PMC/Scav keeps co-installed map metadata and native keycard policy");
            }
        }
    }

    private static void ExcludedContexts()
    {
        var original = Labyrinth();
        var settings = Select(original);
        var cases = new (Action Setup, Action Restore, string Reason)[]
        {
            (() => settings.Side = ESideType.Savage, () => settings.Side = ESideType.Pmc, "Scav"),
            (() => ArenaClientConfig.Enabled = false, () => ArenaClientConfig.Enabled = true, "server-disabled map"),
            (() => ArenaServerClient.IsServerModAvailable = false, () => ArenaServerClient.IsServerModAvailable = true, "server absent"),
            (() => FikaCompatCheck.IsFikaLoaded = true, () => FikaCompatCheck.IsFikaLoaded = false, "Fika"),
            (() => ArenaMode.Config.Settings.Allowlist = "laboratory", () => ArenaMode.Config.Settings.Allowlist = ArenaMode.Shared.ArenaMapCatalog.DefaultClientAllowlist, "client-disabled map"),
            (() => ArenaMenuSelection.ConfigureEligibility(() => new(false, false, "blocked"), null),
                () => ArenaMenuSelection.ConfigureEligibility(null, null), "ineligible group"),
        };
        foreach (var row in cases)
        {
            row.Setup();
            Assert(ReferenceEquals(original, ArenaLabyrinthAccess.Project(settings, original)),
                "Projection must fail open for " + row.Reason);
            Assert(WaiveKey(settings, out _), "Native keycard check must remain for " + row.Reason);
            row.Restore();
        }
        Assert(ReferenceEquals(original, ArenaLabyrinthAccess.Project(new RaidSettings { Side = ESideType.Pmc }, original)),
            "Another settings object must not inherit an active Arena selection");
        var otherMap = Labyrinth();
        otherMap.Id = "laboratory";
        Assert(ReferenceEquals(otherMap, ArenaLabyrinthAccess.Project(settings, otherMap)), "Other Arena maps retain their own metadata");
        Assert(ArenaLabyrinthAccess.Project(settings, null) == null, "Null selection remains null");
        ArenaMenuSelection.EndFlow();
    }

    public static void Main()
    {
        StandaloneProjectionAndRollback();
        CoInstalledProjection();
        ExcludedContexts();
        Console.WriteLine($"ARENA_LABYRINTH_ACCESS_CHECKS_PASSED ({_checks} assertions)");
    }
}
