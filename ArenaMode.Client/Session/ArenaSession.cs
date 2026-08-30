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
    public static bool ArmedForNextRaid { get; private set; }
    public static string ArmedMap { get; private set; }

    public static bool IsActive { get; private set; }

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

    public static void MarkArmed(string map)
    {
        ArmedForNextRaid = true;
        ArmedMap = map;
        Plugin.ModLogger.LogInfo($"Arena armed for next raid on {map}.");
    }

    /// <summary>Drops a stale armed flag and tells the server to discard its snapshot.</summary>
    public static void ClearArmed(string reason)
    {
        if (!ArmedForNextRaid)
            return;

        ArmedForNextRaid = false;
        Plugin.ModLogger.LogWarning($"Arena arming cleared ({reason}).");
        ArenaServerClient.PostRaidCancel();
    }

    private static void OnRaidStarted(GameWorld world)
    {
        if (!ArmedForNextRaid)
        {
            // A raid that was never armed is by definition not an arena raid.
            // Self-heal a stale latch (missed GameEnd after a crash) so it can
            // never arena-ize a normal raid.
            if (IsActive)
            {
                IsActive = false;
                Plugin.ModLogger.LogWarning("Stale arena latch cleared at raid start.");
            }

            LastRaidWasArena = false;
            return;
        }

        var location = world?.LocationId;
        ArmedForNextRaid = false;
        LastRaidWasArena = false;

        if (FikaCompatCheck.IsFikaLoaded)
        {
            ArenaServerClient.PostRaidCancel();
            return;
        }

        if (!string.Equals(location, ArmedMap, StringComparison.OrdinalIgnoreCase))
        {
            Plugin.ModLogger.LogWarning(
                $"Arena was armed for {ArmedMap} but raid started on {location} — arena stays inactive.");
            ArenaServerClient.PostRaidCancel();
            return;
        }

        IsActive = true;
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

        // Pre-warm the spawn system (respawns need it) and swap the real gear for
        // the selected kit via an in-place respawn.
        if (Comfort.Common.Singleton<AbstractGame>.Instance is LocalGame game)
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
        // FIRST, and outside the latch guard: the client reuses one profile for
        // the whole session, so boosted skills must come back even if the latch
        // was cleared abnormally. Self-noops when nothing was boosted.
        Combat.ArenaSkills.Restore();

        if (!IsActive && !ArmedForNextRaid)
            return;

        IsActive = false;
        ArmedForNextRaid = false;

        // Defensive: an aborted load between the countdown patch and its restore
        // must never leave the shortened value in the shared config.
        Movement.FastDeployPatches.Restore();

        // Best-effort confirmation; the authoritative snapshot restore already ran
        // server-side inside the post-raid skip patch.
        ArenaServerClient.PostRaidEnd();
        Plugin.ModLogger.LogInfo("Arena session ended.");
    }
}
