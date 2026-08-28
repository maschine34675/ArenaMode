using ArenaMode.Config;
using ArenaMode.Session;
using EFT;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using EFT.UI.SessionEnd;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Linq;
using System.Reflection;

namespace ArenaMode.Loot;

internal static class ArenaLootPatches
{
    public static void Enable()
    {
        new InstantSearchPatch().Enable();
        new KillListNamesPatch().Enable();
    }

    /// <summary>
    /// The 4.1 search operation carries a single public Instant flag (normally the
    /// elite-Attention lucky-search roll) that zeroes BOTH the 2s access delay and
    /// the per-item reveal delays. Forcing it in the base ctor covers the SP
    /// subclass; the network operation does not derive from this type, so Fika
    /// paths are untouched by construction.
    /// </summary>
    private sealed class InstantSearchPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => typeof(ActiveSearchContentOperation).GetConstructors().First();

        [PatchPostfix]
        private static void Postfix(ActiveSearchContentOperation __instance)
        {
            if (!ArenaSession.IsActive || !Settings.InstantSearch.Value)
                return;

            __instance.Instant = true;
        }
    }

    /// <summary>
    /// The end-of-raid kill list masks PMC victims as "???" unless their dogtag was
    /// collected — pointless busywork in an arena. Forcing knownName flips name,
    /// level and the kill-detail line together. Gated on the LastRaidWasArena latch:
    /// this screen shows after IsActive has already been cleared.
    /// </summary>
    private sealed class KillListNamesPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(KillListVictim), nameof(KillListVictim.Show));

        [PatchPrefix]
        private static void Prefix(ref bool knownName)
        {
            if (!ArenaSession.LastRaidWasArena || !Settings.ShowVictimNames.Value)
                return;

            knownName = true;
        }
    }
}
