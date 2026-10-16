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
/// Requests never become free capacity merely because time passed. Preparation
/// failures settle explicitly; ambiguous native activation ends survival after
/// a bounded wait rather than allowing a late body into another wave.
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
    /// Separate clocks: cold profile/pool work may take minutes, but native
    /// activation after submission should resolve promptly. Expiry ends the
    /// match; it never releases a slot or cancels uncertain native work.
    /// </summary>
    private const float PreparationTimeoutSeconds = 300f;
    private const float NativeArrivalTimeoutSeconds = 60f;

    /// <summary>
    /// One ledger entry per dispatched-but-unconfirmed request, keyed by an
    /// id unique for this raid rather than tracked as a bare timestamp list.
    /// OnZombieArrived removes the entry whose id matches the arriving bot's
    /// own request, never just PendingRequests[0] — a request can resolve
    /// out of dispatch order (BurstSpawnService.SpawnPair is async per
    /// request), and popping an arbitrary FIFO slot would attribute one
    /// request's arrival to a different one's age.
    /// </summary>
    private sealed class PendingEntry
    {
        internal readonly int RequestId;
        internal readonly int Generation;
        internal readonly float StartedAt;
        internal float NativeStartedAt = float.NaN;
        internal PendingEntry(int requestId, int generation, float startedAt)
        {
            RequestId = requestId;
            Generation = generation;
            StartedAt = startedAt;
        }
    }

    private static readonly List<PendingEntry> PendingRequests = new List<PendingEntry>(8);
    private static int _nextRequestId;
    private static int _confirmedArrivals;
    private static int _generation;
    private static int _failedDispatches;
    private static string _pendingFailure;

    /// <summary>Spawn requests accepted but not yet confirmed alive or dead.</summary>
    internal static int Pending => PendingRequests.Count;

    internal static int ConsumeFailedDispatches()
    {
        int count = _failedDispatches;
        _failedDispatches = 0;
        return count;
    }

    internal static bool TryGetPendingFailure(out string reason)
    {
        if (_pendingFailure == null)
            foreach (var entry in PendingRequests)
            {
                bool preparing = float.IsNaN(entry.NativeStartedAt);
                float elapsed = Time.time - (preparing ? entry.StartedAt : entry.NativeStartedAt);
                float timeout = preparing ? PreparationTimeoutSeconds : NativeArrivalTimeoutSeconds;
                if (elapsed < timeout) continue;
                _pendingFailure = $"infected request {entry.RequestId} {(preparing ? "preparation" : "native activation")} "
                    + $"did not settle within {timeout:0}s; {PendingRequests.Count} reservation(s) retained";
                break;
            }
        reason = _pendingFailure;
        return reason != null;
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
        unchecked { _generation++; }
        PendingRequests.Clear();
        _confirmedArrivals = 0;
        _failedDispatches = 0;
        _pendingFailure = null;
    }

    /// <summary>
    /// Asks BurstSpawnService for one native infected bot. False means nothing
    /// was dispatched (bad role/difficulty, no zone, or no active raid) —
    /// SpawnInfected already logged its own reason, so nothing is duplicated
    /// here.
    /// </summary>
    internal static bool TryRequest(WildSpawnType role, BotDifficulty difficulty)
    {
        if (_pendingFailure != null) return false;
        int requestId = unchecked(++_nextRequestId);
        var entry = new PendingEntry(requestId, _generation, Time.time);
        // Register before launching: warmed preparation can arrive synchronously.
        PendingRequests.Add(entry);
        bool dispatched = BurstSpawnService.SpawnInfected(role, difficulty,
            bot => OnZombieArrived(entry, bot),
            requestCurrent: () => IsPending(entry) && !TryGetPendingFailure(out _),
            beginNativeDispatch: () => BeginNativeDispatch(entry),
            dispatchFinished: handed => OnDispatchFinished(entry, handed),
            arrivalRejected: () => OnArrivalRejected(entry));
        // A synchronous preflight rejection has no asynchronous callbacks; the
        // caller rolls its optimistic wave slot back exactly once.
        if (!dispatched) PendingRequests.Remove(entry);
        return dispatched;
    }

    private static bool IsPending(PendingEntry entry)
        => entry.Generation == _generation && PendingRequests.Contains(entry);

    private static bool BeginNativeDispatch(PendingEntry entry)
    {
        if (!IsPending(entry) || !float.IsNaN(entry.NativeStartedAt) || TryGetPendingFailure(out _)) return false;
        entry.NativeStartedAt = Time.time;
        return true;
    }

    private static void OnDispatchFinished(PendingEntry entry, bool handedToNative)
    {
        if (!IsPending(entry) || handedToNative || _pendingFailure != null) return;
        PendingRequests.Remove(entry);
        _failedDispatches++;
    }

    private static void OnArrivalRejected(PendingEntry entry)
    {
        if (entry.Generation != _generation || _pendingFailure != null) return;
        // Rejection can mean an unexpected duplicate or unverified body. Do not
        // infer from it that a native slot is free, even after an earlier arrival.
        _pendingFailure = $"infected request {entry.RequestId} produced an unverified native arrival";
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
    private static void OnZombieArrived(PendingEntry entry, BotOwner bot)
    {
        try
        {
            if (!IsPending(entry) || _pendingFailure != null) return;
            if (bot == null || bot.HealthController == null)
            {
                OnArrivalRejected(entry);
                return;
            }
            PendingRequests.Remove(entry);
            if (!bot.HealthController.IsAlive)
            {
                _failedDispatches++;
                return;
            }

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
