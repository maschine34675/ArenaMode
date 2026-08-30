using ArenaMode.Config;
using EFT;
using EFT.Bots;
using HarmonyLib;
using Redeploy.Core.Compat;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace ArenaMode.Session;

/// <summary>
/// Sends an arena raid straight from the map to the loading screen, past the
/// three clicks in between: Game Settings, Insurance and Confirmation.
///
/// None of them decides anything in an arena raid. Bot amount, difficulty and
/// raid length are all overwritten server-side on the per-raid location clone;
/// insurance is meaningless when the profile is snapshotted and restored around
/// the raid; and the confirmation screen confirms a loadout that gets swapped for
/// a kit in the first second anyway.
///
/// The skip is built out of BSG's own transitions rather than a jump to the end:
/// method_50 hands off to method_52 exactly as the scav path already does, and
/// method_52 hands off to method_27, which is what the Ready button calls. Every
/// pre-flight check therefore still runs — method_27 re-runs the key check and
/// the in-raid item restrictions itself, and the coop check method_52 does is a
/// no-op outside a coop raid.
/// </summary>
internal static class SkipPreRaidScreensPatch
{
    public static void Enable()
    {
        new SkipGameSettingsPatch().Enable();
        new SkipConfirmationPatch().Enable();
    }

    private static bool WouldBeArena(RaidSettings settings)
        => Settings.SkipPreRaidScreens.Value && ArenaClaim.WouldTakeOver(settings);

    private sealed class SkipGameSettingsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MainMenuShowOperation), nameof(MainMenuShowOperation.method_50));

        [PatchPrefix]
        private static bool Prefix(MainMenuShowOperation __instance)
        {
            try
            {
                if (!WouldBeArena(__instance.raidSettings_0))
                    return true;

                // The Game Settings screen is also the only place a player can
                // undo an AI amount of "none" — a setting SVM presets like to
                // leave behind. Skipping the screen without this would hand them
                // a silent, empty arena and no way back to the switch.
                RestoreBots(__instance.raidSettings_0);
                RestoreBots(__instance.raidSettings_1);

                // Carried over from the delegate we are bypassing, so that
                // skipping the screen changes nothing except the clicking. Live
                // now that laboratory is a default arena map — and load-bearing
                // there: the engine discards the whole boss-spawn array when
                // IsBosses is false, which would take the PMC entries with it.
                if (string.Equals(__instance.raidSettings_0?.SelectedLocation?.Id, "laboratory",
                        StringComparison.OrdinalIgnoreCase))
                {
                    SetBosses(__instance.raidSettings_0);
                    SetBosses(__instance.raidSettings_1);
                }

                __instance.method_52();
                return false;
            }
            catch (Exception ex)
            {
                // Fail open: a broken skip must still leave the player able to
                // start a raid by hand.
                Plugin.ModLogger.LogError($"Arena pre-raid skip failed, showing the screens: {ex}");
                return true;
            }
        }

        /// <summary>
        /// Undoes an AI amount of "none", which the skipped screen is the only
        /// place to undo.
        ///
        /// TWO fields, not one. WavesSettings.BotAmount is what the dropdown
        /// reads and writes, but BotControllerSettings.BotAmount is the one with
        /// teeth: IsEnabled is defined as `BotAmount != NoBots`, and LocalGame
        /// passes it to BotsController.Init as botEnable, which sets _canSpawn.
        /// With only the first restored, the server's waves, the PMC spawns and
        /// the engine's spawn pump all stay switched off and the arena runs on
        /// nothing but our own burst spawner trickling bots in.
        ///
        /// Both are STRUCTS held in fields, so these assignments write through to
        /// the real settings — the same writes via a property would have modified
        /// a copy and silently done nothing.
        /// </summary>
        private static void RestoreBots(RaidSettings settings)
        {
            if (settings == null
                || (settings.WavesSettings.BotAmount != EBotAmount.NoBots
                    && settings.BotSettings.BotAmount != EBotAmount.NoBots))
                return;

            settings.WavesSettings.BotAmount = EBotAmount.AsOnline;
            settings.BotSettings.BotAmount = EBotAmount.AsOnline;

            Plugin.ModLogger.LogWarning(
                "Arena: raid menu had AI amount set to none — restored to 'as online', "
                + "otherwise the arena would start with its bot spawning switched off.");
        }

        private static void SetBosses(RaidSettings settings)
        {
            if (settings != null)
                settings.WavesSettings.IsBosses = true;
        }
    }

    private sealed class SkipConfirmationPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MainMenuShowOperation), nameof(MainMenuShowOperation.method_52));

        [PatchPrefix]
        private static bool Prefix(MainMenuShowOperation __instance)
        {
            try
            {
                if (!WouldBeArena(__instance.raidSettings_0))
                    return true;

                // method_27 is what the confirmation screen's Ready button calls.
                // It guards itself against running twice, re-runs the key check
                // and the item-restriction check, and updates the matchmaker
                // settings — so calling it directly skips the screen, not the work.
                var start = __instance.method_27();
                start?.ContinueWith(
                    task => Plugin.ModLogger.LogError($"Arena quick start failed: {task.Exception}"),
                    TaskContinuationOptions.OnlyOnFaulted);

                return false;
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogError($"Arena quick start failed, showing the screens: {ex}");
                return true;
            }
        }
    }
}
