// Production adapter, modeled external objects. No SAIN/Unity code is copied,
// and no Harmony detours or game behavior are executed by this harness.
using System.Reflection;

namespace EFT
{
    public class AbstractGame { }
    public class Player { public bool IsAI = true; public string ProfileId = "bot"; }
    public class BotOwner { public Player GetPlayer { get; set; } }
    public class GameWorld
    {
        public string LocationId = "factory4_day";
        public readonly Dictionary<string, Player> Players = new();
        public Player GetEverExistedPlayerByID(string id) => Players.GetValueOrDefault(id);
    }
}
namespace Comfort.Common
{
    public static class Singleton<T> { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive, ArmedForNextRaid;
        public static string ArmedMap;
        public static EFT.AbstractGame Game;
        public static EFT.GameWorld World;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(game, Game) && ReferenceEquals(world, World);
    }
}
namespace ArenaMode.Config
{
    public sealed class Toggle { public bool Value = true; }
    public static class Settings { public static Toggle ExcludePassiveSainPersonalities = new(); }
}
namespace ArenaMode
{
    public static class Plugin
    {
        public const string PluginGuid = "com.maschine.ArenaMode";
        public static readonly Log ModLogger = new();
    }
    public class Log
    {
        public readonly List<string> Messages = new();
        public void LogInfo(object value) => Messages.Add(value.ToString());
        public void LogWarning(object value) => Messages.Add(value.ToString());
    }
}
namespace HarmonyLib
{
    public static class Priority { public const int Last = 0; }
    public sealed class HarmonyMethod(MethodInfo method)
    {
        public MethodInfo method = method;
        public int priority;
    }
    public sealed class Harmony(string id)
    {
        public string Id { get; } = id;
        public static readonly Dictionary<MethodInfo, (HarmonyMethod Prefix, HarmonyMethod Postfix)> Hooks = new();
        public static bool FailSecondPatch;
        public void Patch(MethodInfo original, HarmonyMethod prefix = null, HarmonyMethod postfix = null)
        {
            if (FailSecondPatch && Hooks.Count == 1) throw new InvalidOperationException("simulated patch installation failure");
            Hooks[original] = (prefix, postfix);
        }
        public void UnpatchSelf() => Hooks.Clear();
    }
}
namespace SAIN.Preset.Shared.Models.Preset.Personalities
{
    public enum EPersonality { None, Wreckless, SnappingTurtle, GigaChad, Chad, Rat, Timmy, Coward, Normal, Custom1, Custom2, Custom3, Custom4, Custom5 }
}
namespace SAIN.Preset.Shared.Personalities.BasePersonality
{
    public class PersonalitySettingsClass { public string Label; public float MarkerValue = 123; }
}
namespace SAIN.Models.Preset.Personalities
{
    public class PersonalityDictionary : Dictionary<SAIN.Preset.Shared.Models.Preset.Personalities.EPersonality,
        SAIN.Preset.Shared.Personalities.BasePersonality.PersonalitySettingsClass> { }
}
namespace SAIN.Preset
{
    public class PersonalityManager
    {
        public SAIN.Models.Preset.Personalities.PersonalityDictionary PersonalityDictionary = new();
    }
    public class SAINPresetClass { public PersonalityManager PersonalityManager { get; set; } = new(); }
}
namespace SAIN
{
    public static class SAINPlugin { public static Preset.SAINPresetClass LoadedPreset { get; set; } = new(); }
}
namespace SAIN.SAINComponent.Classes.Info
{
    using SAIN.Preset.Shared.Models.Preset.Personalities;
    using SAIN.Preset.Shared.Personalities.BasePersonality;
    public class SAINBotInfoClass
    {
        public EFT.BotOwner BotOwner { get; set; }
        public EFT.Player Player { get; set; }
        public EPersonality Requested, Personality;
        public PersonalitySettingsClass InstalledSettings;
        public float ForgetEnemyTime = 777; // PFT-owned sentinel, never recomputed by adapter.
        public int NativeSelections, NativeSetters;
        public EPersonality GetPersonality(out PersonalitySettingsClass settings)
        {
            NativeSelections++;
            settings = SAINPlugin.LoadedPreset.PersonalityManager.PersonalityDictionary[Requested];
            return Requested;
        }
        public void SetPersonality(EPersonality personality)
        {
            NativeSetters++;
            if (SAINPlugin.LoadedPreset.PersonalityManager.PersonalityDictionary.TryGetValue(personality, out var settings))
            { Personality = personality; InstalledSettings = settings; }
        }
    }
}
