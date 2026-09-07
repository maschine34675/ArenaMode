using ArenaMode.Shared;

namespace ArenaMode.Areas;

// Readiness/arming are external inputs to the production negotiation hook.
// This seam records that hook's calls; it does not exercise scene protection.
internal static class ArenaAreaNavigationPatches { internal static bool IsReady = true; }
internal static class ArenaAreaLoadPatch { internal static bool IsReady = true; }
internal static class ArenaAreaSpawnPointsPatch { internal static bool IsReady = true; }
internal static class ArenaAreaRuntime
{
    internal static ArenaAreaDefinition ArmedArea;
    internal static string Token;
    internal static int ArmCalls;
    internal static void Arm(ArenaAreaDefinition area, string token)
    { ArmedArea = area; Token = token; ArmCalls++; }
    internal static void Reset() { ArmedArea = null; Token = null; ArmCalls = 0; }
}
