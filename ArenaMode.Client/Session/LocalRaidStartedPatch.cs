using ArenaMode.Config;
using EFT;
using JsonType;
using Redeploy.Core.Compat;
using SPT.Reflection.Patching;
using System.Reflection;
using System;
using System.Threading.Tasks;

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
    private static bool Prefix(LocalRaidSettings settings, ref Task<LocalSettings> __result)
    {
        var arenaRequested = ArenaMenuSelection.IsSelected;
        var protectionRequired = arenaRequested || ArenaServerClient.HasPendingArm
            || ArenaServerClient.NeedsInitialReconciliation;
        try
        {
            if (!ArenaServerClient.ReconcileInitialState())
                throw new InvalidOperationException("Arena could not confirm the server's previous raid state. Please retry when the server is reachable.");

            // A surviving armed flag means the previously announced raid never
            // reached OnGameStarted (aborted load) — this is a NEW negotiation.
            // Clearing here (before any gate) keeps a stale factory arming from
            // arena-izing an unrelated raid (wrong map, or a scav run).
            ArenaSession.ClearArmed("new raid negotiation started");

            if (ArenaServerClient.HasPendingArm && !ArenaServerClient.PostRaidCancel())
                throw new InvalidOperationException("A previous Arena start could not be cancelled on the server. Please retry when the server is reachable.");

            if (!arenaRequested)
                return true;

            if (FikaCompatCheck.IsFikaLoaded)
                throw new InvalidOperationException("Arena is unavailable with Fika.");

            if (!ArenaServerClient.RefreshAvailability())
                throw new InvalidOperationException("Arena server mod is unavailable.");

            // BEFORE the announcement, not after. The server shapes the raid's
            // location clone from the mode it is told — Tactical needs a silent
            // map — so announcing first would have run the new mode on a clone
            // built for the old one.
            Config.SettingsSync.SyncNow();

            // Recheck after remote settings sync and refresh: a server-side map
            // can have been disabled while the map screen was open.
            if (!ArenaClientConfig.Refresh() || !ArenaMenuSelection.MatchesRequest(settings))
                throw new InvalidOperationException("Arena selection is no longer valid. Please select Arena and a supported map again.");

            // The mode the player actually picked, not a literal. The server
            // only logs it, which is precisely why it has to be true: a status
            // line reading "mode=endless" over a Gun Game raid is a false clue
            // left behind for whoever reads the log next.
            if (!ArenaServerClient.PostRaidStart(settings.location, Settings.GameMode.Value))
                throw new InvalidOperationException("The server did not arm Arena profile protection.");

            ArenaSession.MarkArmed(settings.location);

            // Fetch catalog now (loading screen) instead of inside a gameplay frame.
            Kits.KitCatalog.Preload();
            return true;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError($"Arena raid-start hook failed: {ex}");
            if (!protectionRequired)
                return true;

            ArenaSession.ClearArmed("arena negotiation failed");
            // Also cancel after an ambiguous HTTP failure: the server may have
            // taken a snapshot even though its acknowledgement never arrived.
            ArenaServerClient.PostRaidCancel();
            // GamePrepare already set this flag before asking LocalRaidStarted.
            // The error path reloads the profile but never reaches OnGameEnd,
            // the only vanilla reset of this legacy flag.
#pragma warning disable CS0618
            InGameStatus.InRaid = false;
#pragma warning restore CS0618
            __result = Task.FromException<LocalSettings>(new InvalidOperationException(
                "Arena start cancelled to protect your profile. " + ex.Message, ex));
            return false;
        }
    }
}
