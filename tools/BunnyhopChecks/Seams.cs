using System.Reflection;
namespace EFT {
    public class PlayerOwner { public Player Player; public void CreateInputTranslators() { } }
}
namespace ArenaMode.Movement {
    internal static class ArenaAirControl { internal static bool Ready = true; }
    internal static class ArenaMovementPatches { internal static float Scale = 2; internal static float MotionScale(EFT.MovementContext c) => Scale; }
    public class ArenaSlideController { internal bool IsSliding; }
    internal static class ArenaSlideTransitions {
        internal static void PrepareJump(EFT.Player player) { }
        internal static bool HasActiveSlide(EFT.Player player) => false;
        internal static bool NeedsBufferedJump(EFT.Player player) => false;
        internal static float BufferedJumpWindow(EFT.Player player) => 0f;
        internal static long OwnerEpoch => 0;
    }
    internal static class ArenaJumpReliability {
        internal static int Epoch => 0;
        internal static void PrepareJump(EFT.Player player) { }
        internal static void ObserveGround(EFT.MovementContext context) { }
        internal static void JumpEntered(EFT.JumpPlayerState jump) { }
        internal static bool TryConsumeCoyote(EFT.Player player) => false;
        internal static bool IsCoyoteCurrent(EFT.Player player, int epoch) => false;
    }
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
