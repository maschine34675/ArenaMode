using ArenaMode.Compat;
using ArenaMode.Config;
using EFT;
using EFT.Communications;
using Redeploy.Core.Compat;
using Redeploy.Core.Engine;
using System;
using UnityEngine;

namespace ArenaMode.Session;

/// <summary>
/// Client-side arena session state. Armed-for-next-raid is set by the
/// LocalRaidStartedPatch after the server accepted the raid-start announcement;
/// IsActive is a per-raid latch flipped when the armed raid actually starts.
/// Every arena patch gates on IsActive as its first check, so normal raids are
/// bit-identical with the mod installed.
/// </summary>
public static class ArenaSession
{
    private static bool _rejectedServerRaidPendingEnd;

    public static bool ArmedForNextRaid { get; private set; }
    public static string ArmedMap { get; private set; }
    public static string ArmedMode { get; private set; }

    public static bool IsActive { get; private set; }
    public static string ActiveMode { get; private set; }

    /// <summary>
    /// How long a death replay may run right now, in seconds. Zero means none.
    ///
    /// This is API. KillcamReplay reflects on the type and property NAMES, so
    /// they cannot be renamed without changing that mod in the same commit — the
    /// same contract IsActive already carries. Answered live from the running
    /// mode rather than latched: modes are per-raid objects, and a stale answer
    /// here shows up as a replay over a live body.
    /// </summary>
    public static float ReplaySeconds
    {
        get
        {
            if (!IsActive)
                return 0f;

            var mode = Modes.ArenaMatchService.Current?.Mode;
            return mode?.DeathReplaySeconds ?? 0f;
        }
    }

    /// <summary>
    /// Survives past raid end (IsActive is cleared at GameEnd) — the session-result
    /// screens render AFTER that and need to know whether they show an arena raid.
    /// Cleared when the next non-arena raid starts.
    /// </summary>
    public static bool LastRaidWasArena { get; private set; }

    public static void Initialize()
    {
        RaidLifecycle.EnsurePatched();
        RaidLifecycle.OnRaidStarted += OnRaidStarted;
        RaidLifecycle.OnRaidEnded += OnRaidEnded;
    }

    public static void MarkArmed(string map, string mode)
    {
        ArmedForNextRaid = true;
        ArmedMap = map;
        ArmedMode = mode;
        Plugin.ModLogger.LogInfo($"Arena armed for next raid on {map} ({mode}).");
    }

    /// <summary>Drops a stale armed flag and tells the server to discard its snapshot.</summary>
    public static void ClearArmed(string reason)
    {
        if (!ArmedForNextRaid)
            return;

        ArmedForNextRaid = false;
        ArmedMode = null;
        Plugin.ModLogger.LogWarning($"Arena arming cleared ({reason}).");
        PitFireTeamCompat.Clear(reason);
        ArenaServerClient.PostRaidCancel(startFailed: true);
    }

    private static void OnRaidStarted(GameWorld world)
    {
        if (!ArmedForNextRaid)
        {
            // This new ordinary GameWorld is the first safe boundary after a
            // missed GameEnd. Clear every client-owned Arena latch before any
            // server reconciliation or ordinary combat can run. Each cleanup
            // self-noops in the normal path.
            Modes.ArenaMatchService.RecoverAtOrdinaryRaidStart();
            Respawn.ArenaRespawnDriver.ResetSession();
            Combat.ArenaSkills.Restore();
            Movement.FastDeployPatches.Restore();

            var staleArenaLatch = IsActive;
            IsActive = false;
            _rejectedServerRaidPendingEnd = false;
            ArmedMap = null;
            ArmedMode = null;
            ActiveMode = null;
            LastRaidWasArena = false;
            PitFireTeamCompat.Clear("ordinary raid started");

            if (staleArenaLatch)
                Plugin.ModLogger.LogWarning("Stale arena latch cleared at raid start.");

            if (!ArenaServerClient.ConfirmOrdinaryRaidStarted())
            {
                Plugin.ModLogger.LogError(
                    "The server did not confirm the exact ordinary raid generation; " +
                    "its exact native end remains responsible for closing this ordinary raid.");
            }
            return;
        }

        var location = world?.LocationId;
        var armedMode = ArmedMode;
        ArmedForNextRaid = false;
        ArmedMode = null;
        ActiveMode = null;
        LastRaidWasArena = false;

        if (FikaCompatCheck.IsFikaLoaded)
        {
            PitFireTeamCompat.Clear("Fika raid rejected");
            ArenaServerClient.PostRaidCancel();
            return;
        }

        if (!string.Equals(location, ArmedMap, StringComparison.OrdinalIgnoreCase))
        {
            Plugin.ModLogger.LogWarning(
                $"Arena was armed for {ArmedMap} but raid started on {location} — arena stays inactive.");
            PitFireTeamCompat.Clear("armed map did not match started raid");
            ArenaServerClient.PostRaidCancel();
            return;
        }

        // The server owns the snapshot and accepts only the exact arm token it
        // returned for this raid. Do not publish IsActive or start any Arena
        // gameplay until that identity has been acknowledged. OnGameStarted has
        // already fired, so a rejected confirmation is ended through the normal
        // native post-raid path; keeping the server InRaid lets it restore the
        // snapshot and retain PFT protection during that abort.
        if (!ArenaServerClient.ConfirmRaidStarted())
        {
            _rejectedServerRaidPendingEnd = true;
            LastRaidWasArena = true;
            Plugin.ModLogger.LogError(
                "Arena refused to activate because the server did not confirm the exact raid-start token.");
            Modes.ArenaMatchService.ScheduleFailedStartAbort(
                "the server rejected the exact Arena raid-start token");
            return;
        }

        IsActive = true;
        ActiveMode = armedMode;
        LastRaidWasArena = true;
        Respawn.ArenaRespawnDriver.ResetSession();
        Plugin.ModLogger.LogInfo($"Arena session active on {location}.");
        try
        {
            NotificationManager.DisplayMessageNotification(
                "Arena mode active — nothing gained, nothing lost.",
                ENotificationDurationType.Long,
                ENotificationIconType.Default,
                Color.cyan);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena toast failed: {ex.Message}");
        }

        // Bot groups created during the deploy countdown predate the IsActive latch —
        // their one-time hostility decision missed the IsPlayerEnemy patch. Force the
        // human into every existing group's enemy list once.
        ForceExistingGroupsHostile(world);

        // Before the kit swap: that swap rebuilds the player in place, so it
        // already picks up the boosted values with no extra work.
        Combat.ArenaSkills.Apply(world);

        // Keep PMC profiles cached so the first PMC top-up wave does not stall on
        // a server-side batch generation.
        Bots.BurstSpawnService.PrewarmPmcProfiles();

        // BEFORE the kit swap, because the swap asks for a kit: a mode that owns
        // the loadout (Gun Game picks the first rung of its ladder) has to exist
        // by then, or the player would start the raid on someone else's gun.
        Modes.ArenaMatchService.StartMatch(world);

        // PitFireTeam 0.10.0 holds the original PMC object in its leader wrapper.
        // Replacing that body would strand all four followers on a dead leader,
        // so the compatible one-round path keeps the exact PMC body and loadout
        // that entered the raid. Solo Arena retains the existing swap verbatim.
        if (PitFireTeamCompat.HasFrozenTeamRoster)
        {
            Plugin.ModLogger.LogInfo(
                "Arena 5v5: skipped the raid-start kit/body swap; PitFireTeam remains bound to the original PMC.");
        }
        else if (Comfort.Common.Singleton<AbstractGame>.Instance is LocalGame game)
        {
            Redeploy.Core.Spawning.SpawnSystemCache.Warm(game);
            Respawn.LiveKitRespawn.AtRaidStart(game);
        }
    }

    private static void ForceExistingGroupsHostile(GameWorld world)
    {
        try
        {
            var mainPlayer = world?.MainPlayer;
            var spawner = (Comfort.Common.Singleton<IBotGame>.Instance)?.BotsController?.BotSpawner;
            if (mainPlayer == null || spawner?._bots?.BotOwners == null)
                return;

            var forced = 0;
            foreach (var bot in spawner._bots.BotOwners)
            {
                var group = bot?.BotsGroup;
                if (group == null || group.IsEnemy(mainPlayer))
                    continue;

                // Teammates spawned before the latch are in this list too, and
                // this sweep is the one hostility path that runs BEFORE any of
                // them could have been made hostile — so it is the one that
                // would silently recruit them against you.
                if (Bots.ArenaHostility.CountsAsAlly(group, mainPlayer))
                    continue;

                if (group.AddEnemy(mainPlayer, EBotEnemyCause.initial))
                    forced++;
            }

            if (forced > 0)
                Plugin.ModLogger.LogInfo($"Arena: forced {forced} pre-start bot group(s) hostile.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena: pre-start hostility sweep failed: {ex.Message}");
        }
    }

    private static void OnRaidEnded()
    {
        // EndTeamRaid starts finalization before LocalGame.Stop, which
        // intentionally makes TeamArenaActive false. Preserve/refresh that same
        // bounded guard instead of mistaking it for a solo raid and clearing it.
        var teamRaid = PitFireTeamCompat.TeamArenaActive || PitFireTeamCompat.IsFinalizing;
        if (teamRaid)
            PitFireTeamCompat.BeginFinalizing();

        // FIRST, and outside the latch guard: the client reuses one profile for
        // the whole session, so boosted skills must come back even if the latch
        // was cleared abnormally. Self-noops when nothing was boosted.
        Combat.ArenaSkills.Restore();

        if (!IsActive && !ArmedForNextRaid && !_rejectedServerRaidPendingEnd)
            return;

        IsActive = false;
        ArmedForNextRaid = false;
        _rejectedServerRaidPendingEnd = false;
        ArmedMode = null;
        ActiveMode = null;

        // Defensive: an aborted load between the countdown patch and its restore
        // must never leave the shortened value in the shared config.
        Movement.FastDeployPatches.Restore();

        // Best-effort confirmation; the authoritative snapshot restore already ran
        // server-side inside the post-raid skip patch.
        ArenaServerClient.PostRaidEnd();
        Plugin.ModLogger.LogInfo("Arena session ended.");

        if (!teamRaid)
            PitFireTeamCompat.Clear("Arena raid ended without a PitFireTeam roster");
    }
}
