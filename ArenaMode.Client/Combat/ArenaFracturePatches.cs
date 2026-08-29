using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ArenaMode.Combat;

/// <summary>
/// No broken bones in the arena.
///
/// Maxing the Health skill is not enough on its own: its fracture buff only
/// widens the random range the roll is made against (1.0 to 1.6 at elite), so
/// roughly five eighths of the chance survives. Fractures also come from the
/// heavy-but-survivable limb hits that are an arena's bread and butter — a
/// blacked limb never fractures, because the part is destroyed before the roll.
///
/// Blocking the one method that adds the effect covers the bullet path, the
/// fall path and the stimulator debuff at once, without touching a probability
/// curve. Bots keep their fractures; only the local player is exempt.
/// </summary>
internal static class ArenaFracturePatches
{
    public static void Enable() => new NoFracturePatch().Enable();

    private sealed class NoFracturePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.DoFracture));

        [PatchPrefix]
        private static bool Prefix(ActiveHealthController __instance)
        {
            if (!ArenaSession.IsActive || !Settings.NoFractures.Value)
                return true;

            var mainPlayer = Singleton<GameWorld>.Instantiated
                ? Singleton<GameWorld>.Instance.MainPlayer
                : null;

            if (mainPlayer == null || !ReferenceEquals(mainPlayer.ActiveHealthController, __instance))
                return true;

            return false;
        }
    }
}
