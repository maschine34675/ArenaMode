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
            // A surviving armed flag means the previously announced raid never
            // reached OnGameStarted (aborted load) — this is a NEW negotiation.
            // Clearing here (before any gate) keeps a stale factory arming from
            // arena-izing an unrelated raid (wrong map, or a scav run).
            ArenaSession.ClearArmed("new raid negotiation started");

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

            // The mode the player actually picked, not a literal. The server
            // only logs it, which is precisely why it has to be true: a status
            // line reading "mode=endless" over a Gun Game raid is a false clue
            // left behind for whoever reads the log next.
            if (ArenaServerClient.PostRaidStart(settings.location, Settings.GameMode.Value))
            {
                ArenaSession.MarkArmed(settings.location);

                // Anything the config page changed lands here, before the kit
                // catalogue and the client config are read — those reads must see
                // the settings this raid is actually going to run with.
                Config.SettingsSync.SyncNow();

                // Fetch catalog + config now (loading screen) instead of inside a
                // gameplay frame.
                Kits.KitCatalog.Preload();
                ArenaClientConfig.Preload();
            }
        }
        catch (System.Exception ex)
        {
            Plugin.ModLogger.LogError($"Arena raid-start hook failed: {ex}");
        }
    }
}
