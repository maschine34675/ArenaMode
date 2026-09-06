using System.Reflection;
using ArenaMode.Bots;
using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SAIN;
using SAIN.Preset;
using SAIN.Preset.Shared.Models.Preset.Personalities;
using SAIN.Preset.Shared.Personalities.BasePersonality;
using SAIN.SAINComponent.Classes.Info;

internal static class Program
{
    private static int _checks;
    private static readonly MethodInfo Select = typeof(SAINBotInfoClass).GetMethod("GetPersonality");
    private static readonly MethodInfo Set = typeof(SAINBotInfoClass).GetMethod("SetPersonality");

    private static int Main()
    {
        try
        {
            CheckBinding();
            CheckAssignments();
            CheckScope();
            CheckReloadAndMalformedPreset();
            CheckLoggingAndCleanup();
            Console.WriteLine($"ARENA_SAIN_PERSONALITY_CHECKS_PASSED ({_checks} assertions)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ARENA_SAIN_PERSONALITY_CHECKS_FAILED: {ex}");
            return 1;
        }
    }

    private static void Assert(bool value, string name)
    { if (!value) throw new InvalidOperationException(name); _checks++; }

    private static SAINBotInfoClass Setup(bool armed = false)
    {
        Settings.ExcludePassiveSainPersonalities = new();
        ArenaSession.IsActive = !armed;
        ArenaSession.ArmedForNextRaid = armed;
        ArenaSession.ArmedMap = "factory4_day";
        ArenaSession.Game = Singleton<AbstractGame>.Instance = new();
        ArenaSession.World = Singleton<GameWorld>.Instance = new();
        var player = new Player();
        ArenaSession.World.Players[player.ProfileId] = player;
        SAINPlugin.LoadedPreset = Preset("initial");
        return new() { Player = player, BotOwner = new() { GetPlayer = player } };
    }

    private static SAINPresetClass Preset(string label)
    {
        var preset = new SAINPresetClass();
        foreach (var value in Enum.GetValues<EPersonality>())
            preset.PersonalityManager.PersonalityDictionary.Add(value, new() { Label = label + ":" + value });
        return preset;
    }

    private static void CheckBinding()
    {
        ArenaSainPersonalities.Shutdown();
        Assert(!ArenaSainPersonalities.TryBind(null), "absent/invalid assembly fails open");
        Harmony.FailSecondPatch = true;
        Assert(!ArenaSainPersonalities.TryBind(typeof(Program).Assembly) && Harmony.Hooks.Count == 0,
            "partial patch failure removes both adapter hooks");
        Harmony.FailSecondPatch = false;
        Assert(ArenaSainPersonalities.TryBind(typeof(Program).Assembly) && Harmony.Hooks.Count == 2,
            "optional adapter installs exactly selection and setter hooks");
        var selection = Harmony.Hooks[Select].Postfix;
        Assert(!selection.method.ContainsGenericParameters && selection.priority == Priority.Last
            && selection.method.GetParameters()[1].ParameterType == typeof(PersonalitySettingsClass).MakeByRefType()
            && selection.method.GetParameters()[2].ParameterType == typeof(EPersonality).MakeByRefType(),
            "closed selection hook exactly matches enum and settings ref types");
        Assert(Harmony.Hooks[Set].Prefix.method.GetParameters()[1].ParameterType == typeof(EPersonality).MakeByRefType(),
            "closed setter hook uses actual enum reference");
    }

    // Native result is chosen first. Invoke the actual registered production
    // postfix with ref arguments, then model the caller installing that pair.
    private static void Assign(SAINBotInfoClass info, EPersonality chosen)
    {
        info.Requested = chosen;
        var result = info.GetPersonality(out var settings);
        if (Harmony.Hooks.TryGetValue(Select, out var hooks))
        {
            object[] args = { info, settings, result };
            hooks.Postfix.method.Invoke(null, args);
            settings = (PersonalitySettingsClass)args[1];
            result = (EPersonality)args[2];
        }
        info.Personality = result;
        info.InstalledSettings = settings;
    }

    private static void Explicit(SAINBotInfoClass info, EPersonality chosen)
    {
        if (Harmony.Hooks.TryGetValue(Set, out var hooks))
        {
            object[] args = { info, chosen };
            hooks.Prefix.method.Invoke(null, args);
            chosen = (EPersonality)args[1];
        }
        info.SetPersonality(chosen);
    }

    private static void CheckAssignments()
    {
        foreach (bool armed in new[] { true, false })
        foreach (var personality in Enum.GetValues<EPersonality>())
        {
            var info = Setup(armed);
            var dictionary = SAINPlugin.LoadedPreset.PersonalityManager.PersonalityDictionary;
            var snapshot = dictionary.ToArray();
            bool excluded = personality is EPersonality.Rat or EPersonality.Timmy or EPersonality.Coward;
            var expected = excluded ? EPersonality.Normal : personality;
            Assign(info, personality);
            Assert(info.Personality == expected, "selection excludes only passive personalities");
            Assert(ReferenceEquals(info.InstalledSettings, dictionary[expected]), "selection enum and settings stay paired");
            Explicit(info, personality);
            Assert(info.Personality == expected && ReferenceEquals(info.InstalledSettings, dictionary[expected]),
                "explicit setter cannot reintroduce passive personalities");
            Assert(info.NativeSelections == 1 && info.NativeSetters == 1 && info.ForgetEnemyTime == 777,
                "native assignments run once without touching PFT forget time");
            Assert(snapshot.Length == dictionary.Count && snapshot.All(pair => ReferenceEquals(pair.Value, dictionary[pair.Key])
                && pair.Value.MarkerValue == 123 && pair.Value.Label == "initial:" + pair.Key),
                "shared preset dictionary and every settings object remain unchanged");
        }
    }

    private static void CheckScope()
    {
        foreach (string reason in new[] { "ordinary", "disabled", "missing-setting", "foreign-world", "foreign-game",
            "wrong-map", "missing-world", "human", "mismatched-owner", "old-body", "unregistered", "null-info" })
        {
            var info = Setup();
            switch (reason)
            {
                case "ordinary": ArenaSession.IsActive = false; break;
                case "disabled": Settings.ExcludePassiveSainPersonalities.Value = false; break;
                case "missing-setting": Settings.ExcludePassiveSainPersonalities = null; break;
                case "foreign-world": Singleton<GameWorld>.Instance = new(); break;
                case "foreign-game": Singleton<AbstractGame>.Instance = new(); break;
                case "wrong-map": ArenaSession.World.LocationId = "laboratory"; break;
                case "missing-world": Singleton<GameWorld>.Instance = null; break;
                case "human": info.Player.IsAI = false; break;
                case "mismatched-owner": info.BotOwner.GetPlayer = new(); break;
                case "old-body": ArenaSession.World.Players["bot"] = new(); break;
                case "unregistered": ArenaSession.World.Players.Clear(); break;
                case "null-info": info.BotOwner = null; break;
            }
            Assign(info, EPersonality.Rat);
            Assert(info.Personality == EPersonality.Rat, "selection stays native outside exact Arena scope: " + reason);
            Explicit(info, EPersonality.Coward);
            Assert(info.Personality == EPersonality.Coward, "setter stays native outside exact Arena scope: " + reason);
        }
        var loading = Setup(true);
        ArenaSession.IsActive = true; // Stale previous session, freshly armed next raid wins.
        ArenaSession.Game = new();
        Assign(loading, EPersonality.Timmy);
        Assert(loading.Personality == EPersonality.Normal, "new armed raid works before active identity binding");
    }

    private static void CheckReloadAndMalformedPreset()
    {
        var info = Setup();
        var old = SAINPlugin.LoadedPreset;
        Assign(info, EPersonality.Rat);
        SAINPlugin.LoadedPreset = Preset("reloaded");
        Assign(info, EPersonality.Timmy);
        Assert(info.InstalledSettings.Label == "reloaded:Normal"
            && !ReferenceEquals(info.InstalledSettings, old.PersonalityManager.PersonalityDictionary[EPersonality.Normal]),
            "preset reload uses the current Normal settings without stale cached settings");
        SAINPlugin.LoadedPreset.PersonalityManager.PersonalityDictionary.Remove(EPersonality.Normal);
        Assign(info, EPersonality.Rat);
        Assert(info.Personality == EPersonality.Rat && info.InstalledSettings.Label == "reloaded:Rat",
            "missing Normal fails open without inconsistent result/settings");
        Explicit(info, EPersonality.Coward);
        Assert(info.Personality == EPersonality.Coward, "missing Normal preserves native explicit setter");
    }

    private static void CheckLoggingAndCleanup()
    {
        var info = Setup();
        ArenaMode.Plugin.ModLogger.Messages.Clear();
        for (int i = 0; i < 100; i++) Assign(info, EPersonality.Rat);
        Assert(ArenaMode.Plugin.ModLogger.Messages.Count(message => message.Contains(" -> Normal (")) == 3,
            "assignment diagnostics stay bounded over a long raid");
        ArenaSainPersonalities.Shutdown();
        Assert(Harmony.Hooks.Count == 0, "shutdown removes optional hooks");
        Assign(info, EPersonality.Rat);
        Assert(info.Personality == EPersonality.Rat, "native selection restored after adapter shutdown");
    }
}
