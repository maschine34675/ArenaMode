using ArenaMode.Config;
using ArenaMode.Modes;
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
            || ArenaServerClient.HasPendingEnd
            || ArenaServerClient.HasOrdinaryLifecycle
            || ArenaServerClient.NeedsInitialReconciliation;
        try
        {
            if (!ArenaServerClient.ReconcileInitialState())
                throw new InvalidOperationException("Arena could not confirm the server's previous raid state. Please retry when the server is reachable.");

            if (!ArenaServerClient.ReconcileOrdinaryLifecycleForNewRaid())
            {
                throw new InvalidOperationException(
                    "A previous ordinary raid still owns its exact start/end transaction.");
            }

            // A surviving armed flag means the previously announced raid never
            // reached OnGameStarted (aborted load) — this is a NEW negotiation.
            // Clearing here (before any gate) keeps a stale factory arming from
            // arena-izing an unrelated raid (wrong map, or a scav run).
            ArenaSession.ClearArmed("new raid negotiation started");
            // Defensive only: every path that clears the arming discards the
            // loading-screen preload with it, and a preload only ever exists
            // while armed. This is a second line for the day a path is added
            // that does not, and a no-op on every other.
            Kits.KitBundlePreloader.DiscardRaidStart("new raid negotiation started");

            if (ArenaServerClient.HasPendingEnd
                && !ArenaServerClient.PostRaidEnd(recoverWithoutNativeEnd: true))
            {
                // A raid that reached the game but whose exact confirmation was
                // rejected still has HasPendingArm. Its native end may also have
                // failed; the unconfirmed failed-start route is the only safe
                // fallback and uses the same exact token.
                if (!ArenaServerClient.HasPendingArm
                    || !ArenaServerClient.PostRaidCancel(startFailed: true))
                {
                    throw new InvalidOperationException(
                        "A previous Arena end is still being persisted. Please retry when the server confirms completion.");
                }
            }

            if (ArenaServerClient.HasPendingArm
                && !ArenaServerClient.PostRaidCancel(startFailed: true))
                throw new InvalidOperationException("A previous Arena start could not be cancelled on the server. Please retry when the server is reachable.");

            if (!arenaRequested)
            {
                // Plugin registers PitFireTeamCompat.Clear here without adding
                // an optional-mod type dependency to this safety-critical hook.
                ArenaMenuSelection.ClearCompatibilityState("ordinary raid negotiation");
                ArenaServerClient.BindOrdinaryNativeStartIdentity(settings);
                return true;
            }

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
            if (!ArenaClientConfig.Refresh())
                throw new InvalidOperationException("Arena could not refresh the server's current map configuration.");

            // This evaluates the live native/PitFireTeam identity sets once,
            // validates the final EFT request and freezes that exact decision.
            // It must precede PostRaidStart: no unauthenticated roster may ever
            // reach the server's profile-protection arm.
            if (!ArenaMenuSelection.TryValidateAndFreezeRequest(settings, out var selectionFailure))
                throw new InvalidOperationException(selectionFailure);

            // Freeze one mode for both sides of the load boundary. F12 remains
            // available while EFT loads, but changing it after this point must
            // not make the client create a different mode than the protected
            // location clone the server just armed.
            var requestedMode = Settings.SelectedGameModeId;
            var deathmatch = requestedMode == ArenaModeCatalog.DeathmatchId ? Settings.FreezeDeathmatchRules() : null;
            var hostility = deathmatch == null ? null : (deathmatch.FreeForAll
                ? Shared.DeathmatchSelection.FreeForAll : Shared.DeathmatchSelection.AllVsPlayer);
            var areaId = Shared.ArenaAreaCatalog.NormalizeId(Settings.TeamDeathmatchArea.Value);
            if (!Shared.ArenaAreaCatalog.TryResolve(areaId, settings.location, requestedMode,
                    ArenaMenuSelection.CompatibilityTeamArenaActive, out var area, out var areaFailure))
                throw new InvalidOperationException(areaFailure);
            if (area != null && (!Areas.ArenaAreaNavigationPatches.IsReady || !Areas.ArenaAreaLoadPatch.IsReady
                || !Areas.ArenaAreaSpawnPointsPatch.IsReady))
                throw new InvalidOperationException("Arena area protection is unavailable; restart after updating the client.");
            if (!Shared.ArenaAreaCatalog.TryResolveTeamSize(area, requestedMode,
                    ArenaMenuSelection.CompatibilityTeamArenaActive,
                    area?.TeamStarts != null && requestedMode == ArenaModeCatalog.TeamDeathmatchId
                    ? Settings.AreaTeamSize?.Value : null, out var areaTeamSize, out var sizeFailure))
                throw new InvalidOperationException(sizeFailure);
            if (area?.TeamStarts != null && !Areas.ArenaAreaInitialSpawnPatch.IsReady)
                throw new InvalidOperationException("Arena initial area placement is unavailable; restart after updating the client.");
            if (ArenaMenuSelection.CompatibilityTeamArenaActive
                && !Compat.PitFireTeamSelectionPolicy.SupportsMode(requestedMode))
            {
                throw new InvalidOperationException(
                    "PitFireTeam companions require Tactical or Zombies Arena at raid start.");
            }

            // The mode the player actually picked, not a literal. The server
            // only logs it, which is precisely why it has to be true: a status
            // line reading "mode=endless" over a Gun Game raid is a false clue
            // left behind for whoever reads the log next.
            // CompatibilityTeamArenaActive queries PitFireTeamCompat.TeamArenaActive;
            // at this point it reflects the roster frozen just above.
            int? participants = Shared.ArenaParticipantPolicy.IsSelectable(requestedMode, area != null,
                ArenaMenuSelection.CompatibilityTeamArenaActive)
                ? Config.ArenaParticipantSettings.Get(settings.location, requestedMode) : (int?)null;
            if (participants.HasValue && Shared.ArenaParticipantPolicy.IsSolo(requestedMode)
                && !Bots.ArenaBotSpawnPatches.SoloPopulationReady)
                throw new InvalidOperationException("Arena participant spawn protection is unavailable; restart after updating the client.");
            if (!ArenaServerClient.PostRaidStart(settings.location, requestedMode,
                    ArenaMenuSelection.CompatibilityTeamArenaActive, areaId, areaTeamSize == 0 ? (int?)null : areaTeamSize,
                    hostility, participants))
                throw new InvalidOperationException("The server did not arm Arena profile protection.");

            if (!ArenaServerClient.BindNativeStartIdentity(settings))
                throw new InvalidOperationException(
                    "Arena could not bind the native local-start request to its exact arm token.");

            ArenaSession.MarkArmed(settings.location, requestedMode, deathmatch,
                participants, ArenaServerClient.PendingParticipantBosses);
            Areas.ArenaAreaRuntime.Arm(area, ArenaServerClient.PendingArmToken, areaTeamSize);

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
            ArenaMenuSelection.ClearCompatibilityState("arena negotiation failed");
            // Also cancel after an ambiguous HTTP failure: the server may have
            // taken a snapshot even though its acknowledgement never arrived.
            if (ArenaServerClient.HasPendingArm)
                ArenaServerClient.PostRaidCancel(startFailed: true);
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

    [PatchPostfix]
    private static void Postfix(ref Task<LocalSettings> __result)
    {
        if (ArenaSession.ArmedForNextRaid || ArenaServerClient.HasPendingArm)
            __result = ObserveLocalStart(__result);
        else if (ArenaServerClient.HasOrdinaryLifecycle)
            __result = ObserveOrdinaryLocalStart(__result);
    }

    private static async Task<LocalSettings> ObserveOrdinaryLocalStart(
        Task<LocalSettings> original)
    {
        try
        {
            var result = await original;
            if (!ArenaServerClient.BindOrdinaryNativeEndIdentity(result))
            {
                throw new InvalidOperationException(
                    "The ordinary local-start response did not carry SPT's exact server identity.");
            }

            return result;
        }
        catch
        {
            ArenaServerClient.PostOrdinaryStartRecovery();
            throw;
        }
    }

    private static async Task<LocalSettings> ObserveLocalStart(Task<LocalSettings> original)
    {
        try
        {
            var result = await original;
            if (!ArenaServerClient.BindNativeEndIdentity(result))
                throw new InvalidOperationException(
                    "Arena could not bind the native local-end request to its exact arm token.");

            // The raid's kit bundles, while the loading screen still has the
            // loader to itself. This is the earliest point where the arena is
            // armed AND the map's GameWorld exists with its raid pools: the
            // player-prefab preload before LocalRaidStarted runs before arming,
            // and OnGameStarted is the first second of the raid. Never allowed
            // to fail the start — a preload that could not begin is a slower
            // first second, not a lost raid, and the catch below would cancel
            // the raid on the server.
            try
            {
                Kits.KitBundlePreloader.BeginRaidStart();
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning($"Arena: the loading-screen kit preload could not start: {ex.Message}");
            }

            return result;
        }
        catch
        {
            // The server prefix may already have consumed Armed → InRaid even
            // though StartLocalRaidAsync later faulted. The matching token and
            // unconfirmed flag let the server restore only this failed attempt;
            // a genuinely running raid cannot be cancelled by this path.
            ArenaSession.ClearArmed("local raid start failed");
            ArenaMenuSelection.ClearCompatibilityState("local raid start failed");
            if (ArenaServerClient.HasPendingArm)
                ArenaServerClient.PostRaidCancel(startFailed: true);
#pragma warning disable CS0618
            InGameStatus.InRaid = false;
#pragma warning restore CS0618
            throw;
        }
    }
}
