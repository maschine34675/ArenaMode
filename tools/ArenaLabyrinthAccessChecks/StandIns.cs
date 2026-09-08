using System.Reflection;

namespace EFT
{
    public enum ESideType { Pmc, Savage }
    public enum EDateTime { CURR, PAST }
    public enum ELocationTransition { None, Common, Event }
    public sealed class RaidSettings
    {
        public ESideType Side;
        public bool IsPmc => Side == ESideType.Pmc;
        public JsonType.LocationSettings.Location SelectedLocation;
        public EDateTime SelectedDateTime;
        public ELocationTransition transitionType;
        public string KeyId;
        public bool OnlinePveRaid;
        // Native RaidSettings rule, used only to observe the effect of the
        // production projection; the actual DLL contract is checked separately.
        public bool NeedToGoOnlineInPve => SelectedLocation.ForceOnlineRaidInPVE || OnlinePveRaid;
    }
    public sealed class LocalRaidSettings
    {
        public ESideType playerSide;
        public string location;
        public ELocationTransition transitionType;
    }
    public sealed class MainMenuShowOperation
    {
        public RaidSettings raidSettings_0;
        public bool method_54() => throw new NotSupportedException();
    }
}

namespace JsonType
{
    public sealed class LocationSettings
    {
        public class Location
        {
            public string Id, Name;
            public bool Enabled, ForceOnlineRaidInPVE, Locked, DisabledForScav;
            public float IconX, IconY;
            public int RequiredPlayerLevelMin, RequiredPlayerLevelMax;
            public string[] AccessKeys;
            public object Scene, Exits;
        }
    }
}

namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); public void Enable() { } }
    public sealed class PatchPrefixAttribute : Attribute { }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name) => type.GetMethod(name);
    }
}
namespace Redeploy.Core.Compat { public static class FikaCompatCheck { public static bool IsFikaLoaded; } }
namespace ArenaMode
{
    public static class Plugin { public static TestLogger ModLogger = new(); }
    public sealed class TestLogger
    {
        public void LogInfo(object message) { }
        public void LogWarning(object message) { }
        public void LogError(object message) { }
    }
}
namespace ArenaMode.Config
{
    public class Entry<T> { public T Value; }
    public static class Settings
    {
        public static string SelectedGameModeId = "endless";
        public static Entry<string> TeamDeathmatchArea = new() { Value = ArenaMode.Shared.ArenaAreaCatalog.FullMapId };
        public static Entry<int> TeamDeathmatchSize = new() { Value = 5 };
        public static Entry<int> AreaTeamSize = new() { Value = 2 };
        public static bool TrySelectArea(string id) { TeamDeathmatchArea.Value = id; return true; }
        public static bool TrySelectAreaTeamSize(int size)
        {
            if (size < 2 || size > 5) return false;
            AreaTeamSize.Value = size;
            return true;
        }
        public static string Allowlist = ArenaMode.Shared.ArenaMapCatalog.DefaultClientAllowlist;
        public static bool IsArenaMap(string map) => ArenaMode.Shared.ArenaMapCatalog.AllowlistContains(Allowlist, map);
    }
}
namespace ArenaMode.Audio { public static class AnnouncerService { public static void EnsureLoaded() { } } }
namespace ArenaMode.Session
{
    public static class ArenaClientConfig
    {
        public static bool Enabled = true;
        public static bool IsMapEnabledOnServer(string map) => Enabled && ArenaMode.Shared.ArenaMapCatalog.IsSupportedMap(map);
    }
    public static class ArenaServerClient { public static bool IsServerModAvailable = true; }
}
