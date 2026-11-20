using System.Reflection;
using System.Text.RegularExpressions;
using ArenaMode;
using ArenaMode.Bots;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

// Compiles the real ArenaBackupProfileDuplicatePatch and BackupProfileDuplicatePolicy against the stand-ins in
// Stubs.cs, patches the stand-in BotProfileClient.LoadedByBackup with real Harmony and calls it the way the game's
// backup loader does. Nothing here has run in the game; the native run of 2026-11-20 is in
// docs/reports/2026-11/GPU-ZOMBIE-PROFILE-POOL-20261120.md.
internal static class Program
{
    private static int checks;
    private static void Check(bool value, string label) { checks++; if (!value) throw new Exception("FAIL: " + label); }

    private static GameWorld world = null!;
    private static LocalGame game = null!;
    private static BotCreatorClient creator = null!;
    private static BotProfileClient client = null!;
    private static int ids;

    private static Profile P() => new() { Id = "p" + (++ids) };
    private static List<Profile> L(params Profile[] profiles) => new(profiles);

    /// <summary>The exact active raid: a world, a game with its creator and the game's own profile client.</summary>
    private static void Raid()
    {
        ArenaBackupProfileDuplicatePatch.Reset();
        world = new GameWorld();
        game = new LocalGame();
        client = new BotProfileClient();
        creator = new BotCreatorClient { _game = game, _creatorProfile = client };
        game.BotsController.BotSpawner._botCreator = creator;
        Singleton<GameWorld>.Instance = world; Singleton<AbstractGame>.Instance = game;
        StubProbe.ThrowOnRead = false;
        ArenaSession.IsActive = true; ArenaSession.StartedGame = game; ArenaSession.StartedWorld = world;
        Log.Debugs.Clear(); Log.Warned.Clear(); Log.Warnings = 0;
    }

    /// <summary>Calls the patched stand-in the way the backup loader calls the game's callback. An exception that
    /// reaches the caller is an exception that reaches the game.</summary>
    private static void Reload(List<Profile> list)
    {
        try { client.LoadedByBackup(list); }
        catch (Exception) { throw new Exception("FAIL: an exception inside the patch never reaches the game"); }
    }

    private static bool Same(List<Profile> a, List<Profile> b) => a.Count == b.Count && a.Zip(b).All(pair => ReferenceEquals(pair.First, pair.Second));

    /// <summary>The reload is passed on as it is: the very list object, the pool as the original left it, no log.</summary>
    private static void Untouched(string label, Action change = null, bool nullList = false, bool emptyList = false, bool emptyPool = false)
    {
        Raid();
        var a = P(); var b = P();
        if (!emptyPool) client.Pool = L(a, b);
        change?.Invoke();
        var list = nullList ? null : emptyList ? new List<Profile>() : L(a);
        int poolBefore = client.Pool.Count;
        Reload(list);
        Check(ReferenceEquals(client.Received, list) && client.Calls == 1, label);
        Check(client.Pool.Count == poolBefore + (list?.Count ?? 0) && Log.Debugs.Count == 0 && Log.Warnings == 0, label + " (the call and the log are the game's)");
    }

    private static void Duplicates()
    {
        Raid();
        var a = P(); var b = P(); var c = P();
        client.Pool = L(a, b, c);
        var list = L(a, b);
        Reload(list);
        Check(client.Received != null && client.Received.Count == 0 && !ReferenceEquals(client.Received, list),
            "a reload the pool already holds leaves the callee with an empty list");
        Check(client.PoolAtEntry.Count == 3 && Same(client.PoolAtEntry, L(a, b, c)),
            "the prefix does not change the pool");
        Check(Same(client.Pool, L(a, b, c)), "the pool holds every profile once after the reload");
        Check(list.Count == 2 && ReferenceEquals(list[0], a) && ReferenceEquals(list[1], b),
            "the loader's own list is not changed");
        Check(Log.Debugs.Count == 1 && Log.Debugs[0] == "Arena profile backup: 2 of 2 reloaded bot profile(s) were already in the pool and were not added a second time.",
            "the duplicates are logged once at Debug with their count");
        Check(Log.Warnings == 0, "removing duplicates is no warning");

        // A reload nothing of which is pooled.
        Raid();
        var x = P(); var y = P();
        client.Pool = L(a);
        list = L(x, y);
        Reload(list);
        Check(ReferenceEquals(client.Received, list), "a reload the pool does not hold is passed on unchanged");
        Check(Same(client.Pool, L(a, x, y)) && Log.Debugs.Count == 0, "a reload the pool does not hold is pooled and logs nothing");

        // Mixed.
        Raid();
        a = P(); b = P(); c = P(); x = P(); y = P();
        client.Pool = L(a, b, c);
        list = L(a, x, b, y);
        Reload(list);
        Check(client.Received != null && Same(client.Received, L(x, y)), "a mixed reload passes on exactly the profiles not yet pooled, in order");
        Check(Same(client.Pool, L(a, b, c, x, y)), "a mixed reload leaves every profile in the pool once");
        Check(Log.Debugs.Count == 1 && Log.Debugs[0].StartsWith("Arena profile backup: 2 of 4 reloaded"), "a mixed reload logs its duplicates and its size");

        // A reference twice in one reload is one pool entry.
        Raid();
        a = P(); x = P();
        client.Pool = L(a);
        Reload(L(x, x));
        Check(client.Received != null && Same(client.Received, L(x)), "a profile twice in one reload is passed on once");
    }

    private static void Identity()
    {
        Raid();
        var held = new Profile { Id = "same" };
        var first = new Profile { Id = "same" }; var second = new Profile { Id = "same" };
        Check(held.Equals(first) && first.Equals(second) && !ReferenceEquals(held, first), "the stand-in profiles compare equal and are distinct");
        client.Pool = L(held);
        var list = L(first, second);
        Reload(list);
        Check(ReferenceEquals(client.Received, list) && Same(client.Received, L(first, second)) && Log.Debugs.Count == 0,
            "two distinct profiles that compare equal are both kept");
        Check(Same(client.Pool, L(held, first, second)), "the equal profiles are pooled");
    }

    private static void Outside()
    {
        Untouched("a session that is not active is left alone", () => ArenaSession.IsActive = false);
        Untouched("an active session with another game than the started one is left alone", () => ArenaSession.StartedGame = new LocalGame());
        Untouched("an active session with another world than the started one is left alone", () => ArenaSession.StartedWorld = new GameWorld());
        Untouched("another world singleton than the started one is left alone", () => Singleton<GameWorld>.Instance = new GameWorld());
        Untouched("a game singleton that is another game is left alone", () => Singleton<AbstractGame>.Instance = new LocalGame());
        Untouched("a game that is not a LocalGame is left alone", () => Singleton<AbstractGame>.Instance = new AbstractGame());
        Untouched("an absent game is left alone", () => Singleton<AbstractGame>.Instance = null);
        Untouched("an absent world is left alone", () => Singleton<GameWorld>.Instance = null);
        Untouched("a creator of another game is left alone", () => creator._game = new LocalGame());
        Untouched("another profile client than the game's own is left alone", () => creator._creatorProfile = new BotProfileClient());
        Untouched("a game without a creator is left alone", () => game.BotsController.BotSpawner._botCreator = null);
        Untouched("a game without a bots controller is left alone", () => game.BotsController = null);
        Untouched("an empty pool is left alone", emptyPool: true);
        Untouched("a null list is left alone", nullList: true);
        Untouched("an empty list is left alone", emptyList: true);

        // The inactive session reads nothing of the game: the singletons would throw.
        Raid();
        ArenaSession.IsActive = false; StubProbe.ThrowOnRead = true;
        var a = P(); client.Pool = L(a); var list = L(a);
        Reload(list);
        StubProbe.ThrowOnRead = false;
        Check(ReferenceEquals(client.Received, list) && Log.Warnings == 0,
            "a session that is not active is decided before anything of the game is read");
    }

    private static void Failures()
    {
        Raid();
        var a = P(); client.Pool = L(a);
        var list = L(a);
        client.ThrowOnPool = true;
        Reload(list);
        Check(ReferenceEquals(client.Received, list) && client.Calls == 1 && list.Count == 1,
            "an exception inside the prefix leaves the argument untouched");
        Check(Log.Warnings == 1 && Log.Warned[0].StartsWith("Arena backup profile correction skipped; this reload is pooled as in the game: "),
            "an exception inside the prefix is reported");
        Reload(list); Reload(list);
        Check(Log.Warnings == 1, "an exception inside the prefix is reported once per raid");
        ArenaBackupProfileDuplicatePatch.Reset();
        Reload(list);
        Check(Log.Warnings == 2, "Reset arms the report again");
        Check(Log.Debugs.Count == 0, "a failure logs nothing at Debug");
        // The pool is readable again: the next reload is corrected as before.
        client.ThrowOnPool = false;
        Reload(L(a));
        Check(client.Received.Count == 0, "the patch works again once the pool can be read");
    }

    private static string Text(string key)
    {
        var path = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == key).Value!;
        return Regex.Replace(File.ReadAllText(path), @"\s+", " ");
    }

    private static MethodInfo target = null!;
    private static MethodInfo prefix = null!;

    private static void Wiring()
    {
        target = (MethodInfo)typeof(ArenaBackupProfileDuplicatePatch).GetMethod("GetTargetMethod", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(new ArenaBackupProfileDuplicatePatch(), null);
        Check(target != null && target.Name == "LoadedByBackup" && target.DeclaringType == typeof(BotProfileClient)
            && target.GetParameters().Length == 1 && target.GetParameters()[0].ParameterType == typeof(List<Profile>),
            "the patch targets BotProfileClient.LoadedByBackup(List<Profile>)");
        prefix = typeof(ArenaBackupProfileDuplicatePatch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!;
        Check(prefix.GetCustomAttribute<PatchPrefixAttribute>() != null, "Prefix is a Harmony prefix");
        var parameters = prefix.GetParameters();
        Check(parameters.Length == 2 && parameters[0].Name == "__instance" && parameters[0].ParameterType == typeof(BotProfileClient),
            "the prefix takes the instance as __instance");
        Check(parameters[1].Name == "__0" && parameters[1].ParameterType == typeof(List<Profile>).MakeByRefType() && parameters[1].ParameterType.IsByRef,
            "the prefix's list parameter is named __0 and is by-ref");

        Log.Warnings = 0; int enabled = ModulePatch.Enabled;
        ArenaBackupProfileDuplicatePatch.EnablePatch();
        Check(ModulePatch.Enabled == enabled + 1 && Log.Warnings == 0, "EnablePatch enables the patch");
        ModulePatch.FailEnable = true;
        try { ArenaBackupProfileDuplicatePatch.EnablePatch(); } catch (Exception) { throw new Exception("FAIL: EnablePatch never throws"); }
        finally { ModulePatch.FailEnable = false; }
        Check(Log.Warnings == 1 && Log.Warned[0].StartsWith("Arena backup profile correction unavailable; reloaded bot profiles are pooled twice as in the game: "),
            "a failing Enable warns once and the plugin loads on");

        var plugin = Text("PluginText");
        Check(plugin.Contains("Bots.ArenaTacticalFreshGearPatch.EnablePatch(); Bots.ArenaBackupProfileDuplicatePatch.EnablePatch();"),
            "Plugin.Load enables the backup profile patch directly after the Tactical fresh gear patch");
        var driver = Text("DriverText");
        Check(driver.Contains("Bots.ArenaTacticalOpening.Reset(); Bots.ArenaBackupProfileDuplicatePatch.Reset();"),
            "the raid reset resets the backup profile patch directly after the Tactical opening");
    }

    private static void Main()
    {
        // Wiring first: a prefix that cannot be bound would make Harmony throw before any assertion ran.
        Wiring();
        new Harmony("ArenaMode.BackupProfileDuplicateChecks").Patch(target, prefix: new HarmonyMethod(prefix));
        Duplicates();
        Identity();
        Outside();
        Failures();
        Console.WriteLine($"ARENA_BACKUP_PROFILE_DUPLICATE_BASELINE_PASSED checks={checks}");
    }
}
