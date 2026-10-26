namespace ArenaMode.Session
{
    internal static class ArenaServerClient
    {
        internal static string ServerAdmissionFailure { get; set; }
    }

    // The real whitelist admission is covered by ArenaSessionClientChecks; here only its position at
    // the head of the admission chain matters.
    internal static class ArenaWhitelistAdmission
    {
        public static string Reason { get; set; }
        public static int Calls { get; set; }
        public static string GetRejectionReason() { Calls++; return Reason; }
    }
}

namespace ArenaMode.Compat
{
    internal static class FastGrenadeThrowCompat
    {
        internal static string Reason;
        internal static int Calls;
        internal static string GetRejectionReason() { Calls++; return Reason; }
    }
    internal static class PipPreloadCompat
    {
        internal static string Reason;
        internal static int Calls;
        internal static string GetRejectionReason() { Calls++; return Reason; }
    }
    internal static class ArenaTarkovaColaCompat
    {
        public static string Reason { get; set; }
        public static int Calls { get; set; }
        public static string GetRejectionReason() { Calls++; return Reason; }
    }
}

// Production admission no longer reads the plugin list. The stand-in stays so that a code-level refusal of a
// named mod, if one were reintroduced, would compile here and then fail CheckNoCodeLevelModRefusals.
namespace BepInEx.Bootstrap
{
    internal static class Chainloader
    {
        public static Dictionary<string, PluginInfo> PluginInfos { get; } = new(StringComparer.Ordinal);
    }
    internal sealed class PluginInfo
    {
        public object Instance { get; set; }
    }
}

namespace Redeploy.Core.Compat
{
    internal static class BodyReplacementCompatibility
    {
        public static string Reason { get; set; }
        public static int Calls { get; set; }
        public static string GetRejectionReason() { Calls++; return Reason; }
    }
}
