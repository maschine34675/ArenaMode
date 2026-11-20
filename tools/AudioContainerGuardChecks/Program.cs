using System.Reflection;
using System.Text.RegularExpressions;
using ArenaMode;
using ArenaMode.Audio;
using ArenaMode.Session;
using Audio.SpatialSystem;
using Audio.SpatialSystem.AudioSourceContainers;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

// Compiles the real ArenaAudioContainerReturnGuardPatch and AudioContainerReturnPolicy against the stand-ins in
// Stubs.cs, patches the stand-in AudioSourceContainerData.ReturnContainer with real Harmony and returns containers the
// way the game's dead-player coroutine and method_26 do. Nothing here has run in the game; the native record is
// docs/reports/2026-11/AUDIO-OCCLUDER-EXCEPTION-20261120.md.
internal static class Program
{
    private static int checks;
    private static void Check(bool value, string label) { checks++; if (!value) throw new Exception("FAIL: " + label); }

    private static GameWorld world = null!;
    private static AbstractGame game = null!;
    private static AudioSourceContainerData data = null!;

    /// <summary>The exact active raid with a fresh container data: 8 slots, the lowest 4 drawn by owners that keep
    /// them (so returns have empty slots to go to, as in a raid), the other 4 unused.</summary>
    private static void Raid()
    {
        ArenaAudioContainerReturnGuardPatch.Reset();
        world = new GameWorld();
        game = new AbstractGame();
        data = new AudioSourceContainerData();
        for (int i = 0; i < 4; i++) data.GetContainer();
        Singleton<GameWorld>.Instance = world; Singleton<AbstractGame>.Instance = game;
        StubProbe.ThrowOnRead = false;
        ArenaSession.IsActive = true; ArenaSession.StartedGame = game; ArenaSession.StartedWorld = world;
        Log.Debugs.Clear(); Log.Warned.Clear(); Log.Warnings = 0;
    }

    /// <summary>Returns a container the way the game does. An exception that reaches the caller is an exception that
    /// reaches the game.</summary>
    private static void Return(AudioSourceContainer container)
    {
        try { data.ReturnContainer(container); }
        catch (Exception) { throw new Exception("FAIL: an exception inside the patch never reaches the game"); }
    }

    private static string SkipLine(AudioSourceContainer c)
        => $"Arena audio container guard: sound container {c.ID} is already in the pool and was not pooled a second time.";

    private static void Model()
    {
        // The stand-in reproduces the game's bug when the guard does not act: pooled twice, handed out twice.
        Raid();
        ArenaSession.IsActive = false;
        var c = data.GetContainer();
        Return(c); Return(c);
        Check(data.Pool.Count(c) == 2, "the stand-in pool takes one container twice, as the game's does");
        Check(ReferenceEquals(data.GetContainer(), data.GetContainer()), "the stand-in pool then hands one container to two owners");
    }

    private static void Twice()
    {
        Raid();
        var c = data.GetContainer();
        int clears = c.Clears;
        Return(c);
        Check(data.Pool.Count(c) == 1 && data.Calls == 1 && c.Clears == clears + 1, "a container returned once is pooled as in the game");
        Check(Log.Debugs.Count == 0 && Log.Warnings == 0, "a container returned once logs nothing");
        Return(c);
        Check(data.Pool.Count(c) == 1, "a container returned twice is pooled once");
        Check(data.Calls == 1, "the second return skips the game's method");
        Check(c.Clears == clears + 1, "the skipped return does not clear the container again");
        Check(Log.Debugs.Count == 1 && Log.Debugs[0] == SkipLine(c), "the skipped return is logged once at Debug with the container's id");
        Check(Log.Warnings == 0, "a skipped return is no warning");
        Return(c);
        Check(data.Pool.Count(c) == 1 && data.Calls == 1 && Log.Debugs.Count == 2, "every further return of a pooled container is skipped and logged");

        var first = data.GetContainer(); var second = data.GetContainer();
        Check(!ReferenceEquals(first, second), "two draws after the double return hand out two containers");
    }

    private static void DrawnBetween()
    {
        Raid();
        var c = data.GetContainer();
        Return(c);
        var owner = data.GetContainer();
        Check(ReferenceEquals(owner, c), "the next draw takes the returned container (lowest slot)");
        Return(c);
        Check(data.Calls == 2 && data.Pool.Count(c) == 1 && Log.Debugs.Count == 0,
            "a container drawn again before its second return is pooled as in the game");
    }

    private static void Identity()
    {
        Raid();
        var c = data.GetContainer();
        Return(c);
        var other = new AudioSourceContainer { ID = c.ID };
        Check(other.Equals(c) && !ReferenceEquals(other, c), "the stand-in containers compare equal and are distinct");
        Return(other);
        Check(data.Calls == 2 && data.Pool.Count(other) == 1 && data.Pool.Count(c) == 1 && Log.Debugs.Count == 0,
            "a distinct container that compares equal is pooled");
    }

    /// <summary>The second return of a pooled container runs as in the game: the original is called and the pool takes
    /// the container a second time.</summary>
    private static void Untouched(string label, Action change)
    {
        Raid();
        var c = data.GetContainer();
        Return(c);
        change();
        Return(c);
        Check(data.Calls == 2 && data.Pool.Count(c) == 2, label);
        Check(Log.Debugs.Count == 0 && Log.Warnings == 0, label + " (the log is the game's)");
    }

    private static void Outside()
    {
        Untouched("a session that is not active is left alone", () => ArenaSession.IsActive = false);
        Untouched("an active session with another game than the started one is left alone", () => ArenaSession.StartedGame = new AbstractGame());
        Untouched("an active session with another world than the started one is left alone", () => ArenaSession.StartedWorld = new GameWorld());
        Untouched("another world singleton than the started one is left alone", () => Singleton<GameWorld>.Instance = new GameWorld());
        Untouched("a game singleton that is another game is left alone", () => Singleton<AbstractGame>.Instance = new AbstractGame());
        Untouched("an absent game is left alone", () => Singleton<AbstractGame>.Instance = null);
        Untouched("an absent world is left alone", () => Singleton<GameWorld>.Instance = null);

        // The inactive session reads nothing of the game: the singletons would throw.
        Raid();
        var c = data.GetContainer();
        Return(c);
        ArenaSession.IsActive = false; StubProbe.ThrowOnRead = true;
        Return(c);
        StubProbe.ThrowOnRead = false;
        Check(data.Calls == 2 && data.Pool.Count(c) == 2 && Log.Warnings == 0,
            "a session that is not active is decided before anything of the game is read");

        // A null container is the game's business: its own method runs (and fails as the game's would).
        Raid();
        Exception thrown = null;
        try { data.ReturnContainer(null); } catch (Exception ex) { thrown = ex; }
        Check(data.Calls == 1 && thrown is NullReferenceException && Log.Warnings == 0, "a null container is left to the game");

        // A data without a pool: nothing to compare with, the game's method runs.
        Raid();
        c = new AudioSourceContainer { ID = 99 };
        data._audioContainersPool = null;
        Return(c);
        Check(data.Calls == 1 && Log.Warnings == 0 && Log.Debugs.Count == 0, "a container data without a pool is left to the game");
    }

    private static void Failures()
    {
        Raid();
        var c = data.GetContainer();
        Return(c);
        data.ThrowOnPool = true;
        Return(c);
        Check(data.Calls == 2 && data.Pool.Count(c) == 2, "an exception inside the prefix lets the game's method run");
        Check(Log.Warnings == 1 && Log.Warned[0].StartsWith("Arena audio container guard skipped; this sound container is returned as in the game: "),
            "an exception inside the prefix is reported");
        Return(c); Return(c);
        Check(Log.Warnings == 1, "an exception inside the prefix is reported once per raid");
        ArenaAudioContainerReturnGuardPatch.Reset();
        Return(c);
        Check(Log.Warnings == 2, "Reset arms the report again");
        Check(Log.Debugs.Count == 0, "a failure logs nothing at Debug");
        data.ThrowOnPool = false;
        Return(c);
        Check(data.Calls == 5 && Log.Debugs.Count == 1, "the guard works again once the pool can be read");
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
        target = (MethodInfo)typeof(ArenaAudioContainerReturnGuardPatch).GetMethod("GetTargetMethod", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(new ArenaAudioContainerReturnGuardPatch(), null);
        Check(target != null && target.Name == "ReturnContainer" && target.DeclaringType == typeof(AudioSourceContainerData)
            && target.GetParameters().Length == 1 && target.GetParameters()[0].ParameterType == typeof(AudioSourceContainer),
            "the patch targets AudioSourceContainerData.ReturnContainer(AudioSourceContainer)");
        prefix = typeof(ArenaAudioContainerReturnGuardPatch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!;
        Check(prefix.GetCustomAttribute<PatchPrefixAttribute>() != null, "Prefix is a Harmony prefix");
        Check(prefix.ReturnType == typeof(bool), "the prefix returns bool, so that it can skip the game's method");
        var parameters = prefix.GetParameters();
        Check(parameters.Length == 2 && parameters[0].Name == "__instance" && parameters[0].ParameterType == typeof(AudioSourceContainerData),
            "the prefix takes the instance as __instance");
        Check(parameters[1].Name == "__0" && parameters[1].ParameterType == typeof(AudioSourceContainer),
            "the prefix's container parameter is named __0 and is by value");

        Log.Warnings = 0; Log.Warned.Clear(); int enabled = ModulePatch.Enabled;
        ArenaAudioContainerReturnGuardPatch.EnablePatch();
        Check(ModulePatch.Enabled == enabled + 1 && Log.Warnings == 0, "EnablePatch enables the patch");
        ModulePatch.FailEnable = true;
        try { ArenaAudioContainerReturnGuardPatch.EnablePatch(); } catch (Exception) { throw new Exception("FAIL: EnablePatch never throws"); }
        finally { ModulePatch.FailEnable = false; }
        Check(Log.Warnings == 1 && Log.Warned[0].StartsWith("Arena audio container guard unavailable; a sound container returned twice is pooled twice as in the game: "),
            "a failing Enable warns once and the plugin loads on");

        var plugin = Text("PluginText");
        Check(plugin.Contains("Bots.ArenaBackupProfileDuplicatePatch.EnablePatch(); Audio.ArenaAudioContainerReturnGuardPatch.EnablePatch();"),
            "Plugin.Load enables the audio container guard directly after the backup profile patch");
        var driver = Text("DriverText");
        Check(driver.Contains("Bots.ArenaBackupProfileDuplicatePatch.Reset(); Audio.ArenaAudioContainerReturnGuardPatch.Reset();"),
            "the raid reset resets the audio container guard directly after the backup profile patch");
    }

    private static void Policy()
    {
        var a = new AudioSourceContainer { ID = 1 }; var b = new AudioSourceContainer { ID = 2 }; var last = new AudioSourceContainer { ID = 3 };
        var slots = new[] { a, null, b, null, last };
        Check(AudioContainerReturnPolicy.IsAlreadyPooled(slots, a) && AudioContainerReturnPolicy.IsAlreadyPooled(slots, b),
            "the policy finds a container in any slot");
        Check(AudioContainerReturnPolicy.IsAlreadyPooled(slots, last), "the policy finds a container in the last slot");
        Check(!AudioContainerReturnPolicy.IsAlreadyPooled(slots, new AudioSourceContainer { ID = 1 }), "a distinct container that compares equal is pooled");
        Check(!AudioContainerReturnPolicy.IsAlreadyPooled(new AudioSourceContainer[] { null, null }, null)
            && !AudioContainerReturnPolicy.IsAlreadyPooled<AudioSourceContainer>(null, a),
            "the policy never calls a null container or a null array pooled");
        Check(ReferenceEquals(slots[0], a) && slots[1] == null && ReferenceEquals(slots[4], last), "the policy does not change the array");
    }

    private static void Main()
    {
        // Wiring first: a prefix that cannot be bound would make Harmony throw before any assertion ran.
        Wiring();
        new Harmony("ArenaMode.AudioContainerGuardChecks").Patch(target, prefix: new HarmonyMethod(prefix));
        Policy();
        Model();
        Twice();
        DrawnBetween();
        Identity();
        Outside();
        Failures();
        Console.WriteLine($"ARENA_AUDIO_CONTAINER_GUARD_BASELINE_PASSED checks={checks}");
    }
}
