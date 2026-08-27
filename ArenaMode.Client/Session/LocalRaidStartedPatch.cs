using ArenaMode.Config;
using EFT;
using Redeploy.Core.Compat;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ArenaMode.Session;

/// <summary>
/// Announces the arena raid to the server BEFORE the client's /client/match/local/start
/// request goes out — the server must snapshot the profile and arm the per-raid flag
/// ahead of StartLocalRaidAsync. The HTTP call is synchronous, guaranteeing order.
/// </summary>
internal sealed class LocalRaidStartedPatch : ModulePatch
{
    public static void EnablePatch() => new LocalRaidStartedPatch().Enable();

    protected override MethodBase GetTargetMethod()
    {
        return typeof(EftClientBackendSession).GetMethod(nameof(EftClientBackendSession.LocalRaidStarted));
    }

    [PatchPrefix]
    private static void Prefix(LocalRaidSettings settings)
    {
        try
        {
            if (!Settings.Enabled.Value)
                return;

            if (settings?.location == null || !Settings.IsArenaMap(settings.location))
                return;

            // Arena is a PMC-side arcade — a scav raid must never arm the server
            // (its post-raid path bypasses the PMC skip patch).
            if (settings.playerSide == ESideType.Savage)
            {
                Plugin.ModLogger.LogInfo("Arena skipped: scav raid.");
                return;
            }

            if (FikaCompatCheck.IsFikaLoaded)
            {
                Plugin.ModLogger.LogWarning("Arena skipped: Fika detected.");
                return;
            }

            if (!ArenaServerClient.IsServerModAvailable)
                return;

            if (ArenaServerClient.PostRaidStart(settings.location, "endless"))
            {
                ArenaSession.MarkArmed(settings.location);
                // Fetch the kit catalog now (loading screen) instead of inside the
                // raid-start gameplay frame.
                Kits.KitCatalog.Preload();
            }
        }
        catch (System.Exception ex)
        {
            Plugin.ModLogger.LogError($"Arena raid-start hook failed: {ex}");
        }
    }
}
