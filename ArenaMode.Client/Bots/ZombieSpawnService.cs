using ArenaMode.Combat;
using ArenaMode.Modes;
using Comfort.Common;
using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Seam between ZombieMode's pure wave policy and the real native spawn path.
///
/// STAGE C (this file): every member is wired for real.
///
/// TryRequest asks BurstSpawnService.SpawnInfected for one native Savage bot
/// of the requested infected role/difficulty. On arrival, OnZombieArrived
/// gives the bot's group the same awareness ArenaHostility.ReportPlayer gives
/// any other Arena bot, plus the exact BotsGroup.AddPointToSearch(..., 1000f,
/// ...) call the native pursuit system makes on a call
/// (InfectedLayersStrategy.Call, decompile-verified) — so a freshly spawned
/// zombie starts hunting immediately whether or not ZombieNativeEventBridge
/// could arm the native Halloween event this raid (see that class's own doc
/// comment for its fail-closed path). CountAliveInfected and NudgeIdleZombies
/// read BotsController.Bots.BotOwners directly, the same registry
/// BurstSpawnService's own TryCountAliveBots reads via a different handle
/// (spawner._bots.BotOwners — the same underlying BotsList either way).
///
/// Pending-request bookkeeping is a SEPARATE ledger from BurstSpawnService's
/// ambient RecentPmcInjections/RecentScavInjections (SpawnInfected does
/// internally share RecentScavInjections for ITS OWN correctness — harmless,
/// see its own doc comment). ZombieWavePolicy needs "requests accepted but
/// not yet confirmed alive or dead" as an input ZombieMode can read every
/// Tick, and BurstSpawnService's ledgers are private to that class. The
/// pattern — a list of request timestamps, aged out after a timeout — mirrors
/// BurstSpawnService.CountPending/InFlightSeconds.
///
/// TryMapRole/TryMapDifficulty are the one place the (string) ZombieRoleMix/
/// ZombieProfilePlan vocabulary turns into native WildSpawnType/BotDifficulty
/// values; ZombieMode's per-tick draws and ZombieNativeEventBridge's opening
/// profile list both call these same two methods, so the mapping table exists
/// exactly once.
/// </summary>
internal static class ZombieSpawnService
{
    /// <summary>
    /// How long a dispatched request may go unconfirmed before this ledger
    /// stops counting it as pending. Mirrors BurstSpawnService.InFlightSeconds:
    /// profile generation is a server round trip, and a request that silently
    /// fails after SpawnInfected returned true (no profiles came back, the
    /// zone emptied out, etc.) must not permanently inflate Pending and starve
    /// the wave of every further spawn.
    /// </summary>
    private const float PendingTimeoutSeconds = 20f;

    /// <summary>
    /// One ledger entry per dispatched-but-unconfirmed request, keyed by an
    /// id unique for this raid rather than tracked as a bare timestamp list.
    /// OnZombieArrived removes the entry whose id matches the arriving bot's
    /// own request, never just PendingRequests[0] — a request can resolve
    /// out of dispatch order (BurstSpawnService.SpawnPair is async per
    /// request), and popping an arbitrary FIFO slot would attribute one
    /// request's arrival to a different one's age.
    /// </summary>
    private readonly struct PendingEntry
    {
        internal readonly int RequestId;
        internal readonly float DispatchedAt;
        internal PendingEntry(int requestId, float dispatchedAt)
        {
            RequestId = requestId;
            DispatchedAt = dispatchedAt;
        }
    }

    private static readonly List<PendingEntry> PendingRequests = new List<PendingEntry>(8);
    private static int _nextRequestId;
    private static int _confirmedArrivals;

    /// <summary>True: Stage C's native spawn path (BurstSpawnService.SpawnInfected) is wired.</summary>
    internal static bool IsReady => true;

    /// <summary>Spawn requests accepted but not yet confirmed alive or dead.</summary>
    internal static int Pending
    {
        get
        {
            AgeOutPending();
            return PendingRequests.Count;
        }
    }

    /// <summary>
    /// Monotonic lifetime count of infected bots confirmed alive on arrival
    /// this raid (incremented in OnZombieArrived the moment a dispatched
    /// bot's own HealthController reports it alive — the exact same fact
    /// CountAliveInfected checks, deliberately independent of whether the
    /// player is alive right now, see OnZombieArrived's own comment). Never
    /// decremented; ZombieMode.Tick diffs it every evaluation and feeds the
    /// delta into ZombieWavePolicy.Evaluate's arrivedTotal parameter, so the
    /// stuck-wave watchdog can net a genuine infected death apart from alive
    /// merely rising because a request dispatched earlier resolved late (see
    /// that parameter's own doc comment). Reset alongside everything else in
    /// Reset().
    /// </summary>
    internal static int ConfirmedArrivals => _confirmedArrivals;

    /// <summary>Called by ZombieMode at both OnMatchStart and OnMatchEnd (idempotent).</summary>
    internal static void Reset()
    {
        PendingRequests.Clear();
        _confirmedArrivals = 0;
    }

    /// <summary>
    /// Asks BurstSpawnService for one native infected bot. False means nothing
    /// was dispatched (bad role/difficulty, no zone, or no active raid) —
    /// SpawnInfected already logged its own reason, so nothing is duplicated
    /// here.
    /// </summary>
    internal static bool TryRequest(WildSpawnType role, BotDifficulty difficulty)
    {
        int requestId = unchecked(++_nextRequestId);
        bool dispatched = BurstSpawnService.SpawnInfected(role, difficulty, bot => OnZombieArrived(requestId, bot));
        if (dispatched)
            PendingRequests.Add(new PendingEntry(requestId, Time.time));
        return dispatched;
    }

    /// <summary>
    /// Infected BotOwners whose health controller is alive — the same counting
    /// style as BurstSpawnService.TryCountAliveBots, excluding nothing else
    /// (decision 4: Zombie has no PitFireTeam-style exclusion to make).
    /// </summary>
    internal static int CountAliveInfected()
    {
        try
        {
            var owners = Singleton<IBotGame>.Instantiated
                ? Singleton<IBotGame>.Instance?.BotsController?.Bots?.BotOwners
                : null;
            if (owners == null)
                return 0;

            int alive = 0;
            foreach (var bot in owners)
            {
                if (bot == null || bot.HealthController == null || !bot.HealthController.IsAlive)
                    continue;

                var role = bot.Profile?.Info?.Settings?.Role;
                if (role.HasValue && role.Value.IsInfected())
                    alive++;
            }

            return alive;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena zombie alive count failed: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Repeats the native pursuit's own AddPointToSearch call for every
    /// infected bot that currently has no enemy, so a zombie that never got a
    /// sighting — the native bridge did not arm this raid, or it simply had no
    /// line of sight yet — is nudged toward the player every
    /// NudgeIntervalSeconds instead of patrolling forever (decision 4).
    /// </summary>
    internal static void NudgeIdleZombies(Vector3 playerPosition)
    {
        try
        {
            var owners = Singleton<IBotGame>.Instantiated
                ? Singleton<IBotGame>.Instance?.BotsController?.Bots?.BotOwners
                : null;
            if (owners == null)
                return;

            foreach (var bot in owners)
            {
                if (bot == null || bot.HealthController == null || !bot.HealthController.IsAlive)
                    continue;

                var role = bot.Profile?.Info?.Settings?.Role;
                if (!role.HasValue || !role.Value.IsInfected())
                    continue;

                if (bot.Memory != null && bot.Memory.HaveEnemy)
                    continue;

                var target = NearestLiveTeammate(bot.Position);
                if (Modes.ArenaMatchService.Current?.Mode is Modes.ZombieMode { IsTeamArena: true } && target == null)
                    continue;
                if (target != null) ArenaHostility.ReportPlayerTo(bot.BotsGroup, target);
                bot.BotsGroup?.AddPointToSearch(target != null ? target.Position : playerPosition, 1000f, bot);
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena zombie idle nudge failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Registers ZombieMode's own backup targets with the native backup loader
    /// (BotSpawner.AddToTargetBackup), in place of the native AddBackupProfiles
    /// call this mode's bridge skips (decision 3d; see
    /// ZombieNativeEventBridge).
    /// </summary>
    internal static void RegisterBackupTargets(IReadOnlyList<ZombieProfilePlan.Entry> targets)
    {
        if (targets == null || targets.Count == 0)
            return;

        try
        {
            var spawner = Singleton<IBotGame>.Instantiated
                ? Singleton<IBotGame>.Instance?.BotsController?.BotSpawner
                : null;
            if (spawner == null)
                return;

            foreach (var entry in targets)
            {
                if (TryMapRole(entry.Role, out var role) && TryMapDifficulty(entry.Difficulty, out var difficulty))
                    spawner.AddToTargetBackup(difficulty, role, entry.Count);
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena zombie backup target registration failed: {ex.Message}");
        }
    }

    /// <summary>
    /// On arrival: the same group-awareness steps ArenaHostility.ReportPlayer
    /// takes, plus the exact BotsGroup.AddPointToSearch(..., 1000f, ...) call
    /// the native pursuit system makes on a call (InfectedLayersStrategy.Call,
    /// decompile-verified) — so a freshly spawned zombie starts hunting
    /// immediately, with or without the native bridge armed (decision 4).
    /// </summary>
    private static void OnZombieArrived(int requestId, BotOwner bot)
    {
        try
        {
            for (int i = 0; i < PendingRequests.Count; i++)
            {
                if (PendingRequests[i].RequestId == requestId)
                {
                    PendingRequests.RemoveAt(i);
                    break;
                }
            }

            if (bot == null || bot.HealthController == null || !bot.HealthController.IsAlive)
                return;

            // Counted here, independent of the player-awareness steps below:
            // ZombieWavePolicy's stuck-wave watchdog (see its own doc
            // comment, "Why a raw alive-count drop is not enough by itself")
            // needs this to stay in lockstep with CountAliveInfected()'s own
            // "bot alive" definition, not with whether the player happens to
            // be alive right now — frequently false for a moment during
            // Zombie's instant-respawn window — which would otherwise
            // undercount an arrival and let a real infected death that lands
            // in the same tick hide behind it.
            _confirmedArrivals++;

            var player = NearestLiveTeammate(bot.Position)
                ?? (Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null);
            if (player == null || player.HealthController == null || !player.HealthController.IsAlive)
                return;

            ArenaHostility.ReportPlayerTo(bot.BotsGroup, player);
            bot.BotsGroup?.AddPointToSearch(player.Position, 1000f, bot);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena zombie arrival awareness failed: {ex.Message}");
        }

        // Separate try/catch: diagnostics must never cancel the awareness
        // steps above even if they themselves fail (ZombieMeleeDiagnostics
        // already fails closed internally too — belt and suspenders).
        try
        {
            ZombieMeleeDiagnostics.OnZombieArrived(bot);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena zombie melee diagnostics subscribe failed: {ex.Message}");
        }
    }

    private static Player NearestLiveTeammate(Vector3 from)
    {
        if (Modes.ArenaMatchService.Current?.Mode is not Modes.ZombieMode { IsTeamArena: true } mode)
            return null;
        Player nearest = null;
        float distance = float.MaxValue;
        foreach (var member in mode.LiveTeamMembers)
        {
            if (member == null || member.HealthController?.IsAlive != true) continue;
            float candidate = (member.Position - from).sqrMagnitude;
            if (candidate >= distance) continue;
            nearest = member;
            distance = candidate;
        }
        return nearest;
    }

    private static void AgeOutPending()
    {
        var cutoff = Time.time - PendingTimeoutSeconds;
        for (var i = PendingRequests.Count - 1; i >= 0; i--)
        {
            if (PendingRequests[i].DispatchedAt < cutoff)
                PendingRequests.RemoveAt(i);
        }
    }

    /// <summary>
    /// Administratively kills every currently alive infected bot. Called only
    /// when ZombieWavePolicy's stuck-wave watchdog fires
    /// (Result.ForceRetireStuckInfected) — i.e. the wave is SATURATED (either
    /// every infected in it has already been requested, or alive+pending
    /// already fills what it may ask for right now — see ZombieWavePolicy's
    /// own doc comment, "Saturated waves arm the watchdog too") and at least
    /// one infected has stayed alive, unreachable or otherwise stuck, past
    /// Settings.ZombieStuckWaveTimeoutSeconds (a bot wedged in geometry or
    /// stranded off a connected NavMesh island — AGENTS.md documents exactly
    /// this failure class). Without this, the policy's own force-clear would
    /// leave the stuck body existing and counting toward CountAliveInfected
    /// forever — ending the wave if it was the fully-spawned tail, or simply
    /// freezing it short of MaxAlive otherwise. Mirrors BurstSpawnService.
    /// RetireStaleSpawn's administrative-kill pattern exactly: gated through
    /// ArenaAdministrativeDeathGate so the retirement is never scored as a
    /// player kill or an assist.
    ///
    /// Logs ONE summary line for the whole retirement, not one per bot: the
    /// bigger-waves defaults (item 2 of the 2026-10-06 follow-up) raise the
    /// tail this can retire from at most WaveStartSize=8 (always &lt;
    /// MaxAlive) to up to MaxAlive=20 alive at once, so a per-bot line on the
    /// success path would burst up to 20 LogWarning calls in a single frame
    /// for an ordinary, expected watchdog firing. The per-bot LogWarning
    /// stays on the (rare, genuinely exceptional) failure-to-retire path.
    /// </summary>
    internal static void RetireAllAliveInfected()
    {
        try
        {
            var owners = Singleton<IBotGame>.Instantiated
                ? Singleton<IBotGame>.Instance?.BotsController?.Bots?.BotOwners
                : null;
            if (owners == null)
                return;

            var stuck = new List<BotOwner>();
            foreach (var bot in owners)
            {
                if (bot == null || bot.HealthController == null || !bot.HealthController.IsAlive)
                    continue;

                var role = bot.Profile?.Info?.Settings?.Role;
                if (role.HasValue && role.Value.IsInfected())
                    stuck.Add(bot);
            }

            int retired = 0;
            foreach (var bot in stuck)
            {
                try
                {
                    using (ArenaAdministrativeDeathGate.Enter(bot.ProfileId))
                        bot.GetPlayer?.ActiveHealthController?.Kill(EDamageType.Undefined);
                    retired++;
                }
                catch (Exception ex)
                {
                    Plugin.ModLogger.LogWarning(
                        $"Arena zombie wave watchdog: could not retire a stuck infected: {ex.Message}");
                }
            }

            if (retired > 0)
            {
                Plugin.ModLogger.LogWarning(
                    $"Arena zombie wave watchdog: administratively retired {retired} infected that stayed "
                    + "alive past the stuck-wave timeout.");
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena zombie wave watchdog retirement failed: {ex.Message}");
        }
    }

    /// <summary>The one place ZombieRoleMix's role strings turn into WildSpawnType.
    /// Shared by ZombieMode's per-tick draws, RegisterBackupTargets above, and
    /// ZombieNativeEventBridge's opening-profile list.</summary>
    internal static bool TryMapRole(string role, out WildSpawnType type)
    {
        switch (role)
        {
            case ZombieRoleMix.InfectedAssault: type = WildSpawnType.infectedAssault; return true;
            case ZombieRoleMix.InfectedPmc: type = WildSpawnType.infectedPmc; return true;
            case ZombieRoleMix.InfectedCivil: type = WildSpawnType.infectedCivil; return true;
            case ZombieRoleMix.InfectedLaborant: type = WildSpawnType.infectedLaborant; return true;
            default:
                type = default;
                Plugin.ModLogger.LogWarning($"Zombie: unknown role mix entry '{role}' — request skipped.");
                return false;
        }
    }

    /// <summary>The one place ZombieRoleMix's difficulty strings turn into BotDifficulty.
    /// Shared the same way as TryMapRole above.</summary>
    internal static bool TryMapDifficulty(string difficulty, out BotDifficulty value)
    {
        switch (difficulty)
        {
            case ZombieRoleMix.Normal: value = BotDifficulty.normal; return true;
            case ZombieRoleMix.Hard: value = BotDifficulty.hard; return true;
            default:
                value = default;
                Plugin.ModLogger.LogWarning($"Zombie: unknown difficulty entry '{difficulty}' — request skipped.");
                return false;
        }
    }
}
