using System.Reflection;
namespace ArenaMode.Movement {
    internal static class ArenaAirControl { internal static bool Ready = true; }
    internal static class ArenaMovementPatches { internal static float Scale = 2; internal static float MotionScale(EFT.MovementContext c) => Scale; }
    public class ArenaSlideController { internal bool IsSliding; }
}
namespace HarmonyLib {
    public class HarmonyMethod(MethodInfo method) { public MethodInfo Method = method; }
    public class Harmony(string id) {
        public static bool FailPatch;
        public void Patch(MethodInfo original, HarmonyMethod prefix=null, HarmonyMethod postfix=null, HarmonyMethod finalizer=null) {
            if (FailPatch || original==null) throw new Exception("patch failure");
        }
        public void UnpatchSelf() { }
    }
}
