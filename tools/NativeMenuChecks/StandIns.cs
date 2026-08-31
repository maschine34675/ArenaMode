using System;
using System.Reflection;
using System.Threading.Tasks;

namespace EFT
{
    public enum ESideType { Pmc, Savage, Random }
    public enum EDateTime { CURR, PAST }
    [Flags] public enum ELocationTransition { None = 0, Common = 1, Event = 2 }
    public class RaidSettings
    {
        public ESideType Side;
        public bool IsPmc => Side == ESideType.Pmc;
        public JsonType.LocationSettings.Location SelectedLocation;
        public EDateTime SelectedDateTime;
        public ELocationTransition transitionType;
        public string KeyId;
    }
    public class LocalRaidSettings
    {
        public string location;
        public ESideType playerSide;
        public ELocationTransition transitionType;
    }
    public class EftClientBackendSession { public Task<JsonType.LocalSettings> LocalRaidStarted(LocalRaidSettings settings) => throw new NotSupportedException(); }
    public static class InGameStatus { public static bool InRaid; }
}
namespace JsonType
{
    public class LocalSettings { }
    public class LocationSettings { public class Location { public string Id; } }
}
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); public void Enable() { } }
    public class PatchPrefixAttribute : Attribute { }
}
namespace SPT.Common.Http
{
    public static class RequestHandler
    {
        public static Func<string, string, string> Post = (route, body) => "{\"disarmed\":true}";
        public static Func<string, string> Get = route => "{\"version\":\"test\"}";
        public static int PostCount, StartCount;
        public static string GetJson(string route) => Get(route);
        public static string PostJson(string route, string body)
        {
            PostCount++;
            if (route == ArenaMode.Shared.Protocol.RaidStartRoute) StartCount++;
            return Post(route, body);
        }
    }
}
namespace Redeploy.Core.Compat { public static class FikaCompatCheck { public static bool IsFikaLoaded; } }
namespace ArenaMode
{
    public static class Plugin { public static TestLogger ModLogger = new(); }
    public class TestLogger { public void LogInfo(object value) { } public void LogWarning(object value) { } public void LogError(object value) { } }
}
namespace ArenaMode.Config
{
    public class Entry<T> { public T Value; }
    public static class Settings
    {
        public static Entry<string> GameMode = new() { Value = "endless" };
        public static bool IsArenaMap(string map) => map == "factory4_day" || map == "labyrinth" || map == "laboratory";
    }
    public static class SettingsSync { public static void SyncNow() { } }
}
namespace ArenaMode.Shared { public class ClientSettingsSchema { } }
namespace ArenaMode.Kits { public static class KitCatalog { public static void Preload() { } } }
namespace ArenaMode.Session
{
    public static class ArenaClientConfig
    {
        public static bool Refresh() => true;
        public static bool IsMapEnabledOnServer(string map) => ArenaMode.Config.Settings.IsArenaMap(map);
    }
    public static class ArenaSession
    {
        public static bool ArmedForNextRaid;
        public static void MarkArmed(string map) => ArmedForNextRaid = true;
        public static void ClearArmed(string reason)
        {
            if (!ArmedForNextRaid) return;
            ArmedForNextRaid = false;
            ArenaServerClient.PostRaidCancel();
        }
    }
}
