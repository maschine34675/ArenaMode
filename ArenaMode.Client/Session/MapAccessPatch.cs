using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace ArenaMode.Session;

/// <summary>
/// Opens a keycard-locked map for arena raids, and for those only.
///
/// The Lab is the reason this exists. Its location entry is Enabled and not
/// Locked; what stops you selecting it is AccessKeys — a single keycard the menu
/// insists you are carrying before it will let the raid start. An arena kit is
/// conjured at spawn and the profile is snapshotted around the raid, so the
/// player never has one and never could.
///
/// The check lives in the MENU (method_54, reading raidSettings_0.SelectedLocation
/// .AccessKeys), which is why the server cannot help here: the per-raid location
/// clone the rest of the mod mutates does not exist yet, and clearing AccessKeys
/// in the shared database would unlock the Lab for ordinary raids too. A client
/// prefix gated on "this raid would be an arena raid" is the narrow instrument —
/// same predicate as the pre-raid screen skip, so the two cannot drift apart.
/// </summary>
internal static class MapAccessPatch
{
    public static void Enable() => new KeycardBypassPatch().Enable();

    private sealed class KeycardBypassPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MainMenuShowOperation), nameof(MainMenuShowOperation.method_54));

        [PatchPrefix]
        private static bool Prefix(MainMenuShowOperation __instance, ref bool __result)
        {
            try
            {
                var settings = __instance.raidSettings_0;
                if (!ArenaClaim.WouldTakeOver(settings))
                    return true;

                var keys = settings.SelectedLocation?.AccessKeys;
                if (keys == null || keys.Length == 0)
                    return true;

                // The SERVER has to agree that this map is an arena map. The
                // client list alone is not enough: if the two drifted apart, the
                // waiver would unlock the map and the raid would then run as an
                // ordinary one — real kit, real profile, on a map the player has
                // no key for and no business being on.
                var map = settings.SelectedLocation?.Id;
                if (!ArenaClientConfig.IsMapEnabledOnServer(map))
                {
                    Plugin.ModLogger.LogWarning(
                        $"Arena: '{map}' is keycard-locked and the server does not have it enabled — "
                        + "leaving the lock in place.");
                    return true;
                }

                // Cleared the way the original clears it: KeyId names the key that
                // was found and consumed, and leaving a stale one behind would have
                // the raid claim a keycard the player is not carrying.
                settings.KeyId = string.Empty;

                Plugin.ModLogger.LogInfo(
                    $"Arena: '{settings.SelectedLocation?.Id}' needs a keycard the arena never issues — waiving it for this raid.");

                __result = true;
                return false;
            }
            catch (Exception ex)
            {
                // Fail open to the vanilla check: a broken bypass must leave the
                // player able to enter the maps they legitimately have keys for.
                Plugin.ModLogger.LogError($"Arena keycard waiver failed: {ex}");
                return true;
            }
        }
    }
}
