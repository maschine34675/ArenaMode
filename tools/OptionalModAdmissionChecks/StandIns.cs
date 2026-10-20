using System.Reflection;

namespace ArenaMode.Session
{
    internal static class ArenaServerClient
    {
        internal static string ServerAdmissionFailure { get; set; }
    }
}

namespace BepInEx.Bootstrap
{
    internal static class Chainloader
    {
        public static Dictionary<string, PluginInfo> PluginInfos { get; } = new(StringComparer.Ordinal);
    }
    internal sealed class PluginInfo
    {
        public object Instance { get; set; }
        public PluginMetadata Metadata { get; set; }
    }
    internal sealed class PluginMetadata
    {
        public Version Version { get; set; }
    }
}

namespace Comfort.Common
{
    public static class Singleton<T>
    {
        public static T Instance { get; set; }
    }
}

namespace HarmonyLib
{
    internal sealed class Patch
    {
        public MethodInfo PatchMethod { get; set; }
    }
    internal sealed class Patches
    {
        public List<Patch> Prefixes { get; } = new();
        public List<Patch> Postfixes { get; } = new();
        public List<Patch> Transpilers { get; } = new();
        public List<Patch> Finalizers { get; } = new();
    }
    internal static class Harmony
    {
        public static Dictionary<MethodBase, Patches> Registry { get; } = new();
        public static int EnumerationCount { get; private set; }
        public static bool ThrowOnEnumeration { get; set; }

        public static IEnumerable<MethodBase> GetAllPatchedMethods()
        {
            EnumerationCount++;
            if (ThrowOnEnumeration) throw new InvalidOperationException("Uninspectable Harmony registry");
            return Registry.Keys;
        }
        public static Patches GetPatchInfo(MethodBase method) => Registry[method];
        public static void Reset()
        {
            Registry.Clear();
            EnumerationCount = 0;
            ThrowOnEnumeration = false;
        }
    }
}

namespace QuestingBots
{
    internal sealed class QuestingBotsPlugin
    {
        // Disabling a MonoBehaviour does not remove its already-installed patches.
        public bool enabled { get; set; } = true;
    }
}

namespace QuestingBots.Utils
{
    internal sealed class ConfigUtil
    {
        // Private cached member deliberately matches the inspected foreign contract.
        private readonly object _currentConfig;
        public static int CurrentConfigReads { get; private set; }
        public ConfigUtil(object config) => _currentConfig = config;
        public object CurrentConfig
        {
            get
            {
                CurrentConfigReads++;
                throw new InvalidOperationException("The network-capable getter must never be invoked");
            }
        }
    }
}

namespace QuestingBots.Patches
{
    internal static class MenuShowPatch
    {
        public static void PatchPostfix() { }
    }
    internal static class BotsControllerInitPatch
    {
        public static void PatchPostfix() { }
    }
}

namespace ArenaMode.Compat
{
    internal static class ArenaMoreBotsCompatibility
    {
        public static string Reason { get; set; }
        public static int Calls { get; set; }
        public static string GetRejectionReason() { Calls++; return Reason; }
    }
    internal static class ArenaAiLimitCompat
    {
        public static string Reason { get; set; }
        public static int Calls { get; set; }
        public static string GetRejectionReason() { Calls++; return Reason; }
    }
    internal static class ArenaLootingBotsCompat
    {
        public static string Reason { get; set; }
        public static int Calls { get; set; }
        public static string GetRejectionReason() { Calls++; return Reason; }
    }
    internal static class ArenaRemoveTheDeadCompat
    {
        public static string Reason { get; set; }
        public static int Calls { get; set; }
        public static string GetRejectionReason() { Calls++; return Reason; }
    }
    internal static class ArenaSmoothieCompat
    {
        public static string Reason { get; set; }
        public static int Calls { get; set; }
        public static string GetRejectionReason() { Calls++; return Reason; }
    }
    internal static class ArenaContinuousHealingCompat
    {
        public static string Reason { get; set; }
        public static int Calls { get; set; }
        public static string GetRejectionReason() { Calls++; return Reason; }
    }
    internal static class ArenaLateToThePartyCompat
    {
        public static string Reason { get; set; }
        public static int Calls { get; set; }
        public static string GetRejectionReason() { Calls++; return Reason; }
    }
    internal static class ArenaClassicMovementCompat
    {
        public static string Reason { get; set; }
        public static int Calls { get; set; }
        public static string GetRejectionReason() { Calls++; return Reason; }
    }
    internal static class ArenaStatTrackCompat
    {
        public static string Reason { get; set; }
        public static int Calls { get; set; }
        public static string GetRejectionReason() { Calls++; return Reason; }
    }
    internal static class ArenaContinuousLoadAmmoCompat
    {
        public static string Reason { get; set; }
        public static int Calls { get; set; }
        public static string GetRejectionReason() { Calls++; return Reason; }
    }
    internal static class ArenaDeminvincibilityCompat
    {
        public static string Reason { get; set; }
        public static int Calls { get; set; }
        public static string GetRejectionReason() { Calls++; return Reason; }
    }
    internal static class ArenaSkillsExtendedCompat
    {
        public static string Reason { get; set; }
        public static int Calls { get; set; }
        public static string GetRejectionReason() { Calls++; return Reason; }
    }
    internal static class ArenaRecoilLegacyCompat
    {
        public static string Reason { get; set; }
        public static int Calls { get; set; }
        public static string GetRejectionReason() { Calls++; return Reason; }
    }
    internal static class ArenaPauseCompat
    {
        public static string Reason { get; set; }
        public static int Calls { get; set; }
        public static string GetRejectionReason() { Calls++; return Reason; }
    }
    internal static class ArenaEffectsCompat
    {
        public static string Reason { get; set; }
        public static int Calls { get; set; }
        public static string GetRejectionReason() { Calls++; return Reason; }
    }
}
