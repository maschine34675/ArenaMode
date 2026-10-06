using EFT;
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace ArenaMode.Bots;

/// <summary>
/// Cheap, rate-limited visibility into whether a zombie's native melee
/// swing actually runs, for the next GPU raid to confirm ZombieMeleePatch's
/// fix against the 2026-10-06 finding (zero player damage over 25 minutes —
/// see ZOMBIE-MODE-REVIEW.md, "Native melee is suppressed by SAIN's global
/// prefix"). Subscribes to a freshly-arrived zombie's own
/// <c>BotMeleeWeaponData.OnTryHit</c>/<c>OnEnemyHitted</c> events (both
/// public, decompile-verified: ../_decompiled/BotMeleeWeaponData.cs:104-106)
/// from <c>ZombieSpawnService.OnZombieArrived</c>, logs the first few swings
/// and landed hits per raid at Info, counts the rest silently, and reports
/// the raid's totals once from <c>ZombieMode.OnMatchEnd</c> (which has no
/// richer existing summary to fold into — see that method's own comment).
///
/// Unsubscribes on death/unspawn like every other per-bot event hook in this
/// codebase (the same <c>Player.OnIPlayerDeadOrUnspawn</c> SAIN's own
/// SAINEnableClass uses for its per-bot bookkeeping) — a zombie that leaves
/// one of these delegates attached past its own death would otherwise keep
/// a <c>BotMeleeWeaponData</c> (and this class, transitively) alive for the
/// rest of the raid for nothing.
/// </summary>
internal static class ZombieMeleeDiagnostics
{
    private const int LoggedSwingsPerRaid = 3;
    private const int LoggedHitsPerRaid = 3;

    // Keyed by the bot, not the melee data, only because BotOwner is what
    // OnZombieArrived already has in hand; the value is unused (a
    // ConditionalWeakTable needs one) and the table exists purely so a
    // bot that somehow arrives through this hook twice is not subscribed
    // twice (should not happen — ZombieSpawnService.OnZombieArrived fires
    // once per dispatched request — but idempotence here is free).
    private static readonly ConditionalWeakTable<BotOwner, object> Subscribed = new();

    private static int _swingsSeen;
    private static int _hitsSeen;

    /// <summary>Called from ZombieSpawnService.OnZombieArrived for every bot that just spawned.</summary>
    internal static void OnZombieArrived(BotOwner bot)
    {
        try
        {
            var melee = bot?.WeaponManager?.Melee;
            var player = bot?.GetPlayer;
            if (melee == null || player == null)
                return;

            if (Subscribed.TryGetValue(bot, out _))
                return;
            Subscribed.Add(bot, s_marker);

            void OnSwing(BotOwner swinger) => RecordSwing(swinger);
            void OnHit(BotOwner hitter, Player target) => RecordHit(hitter);
            void Cleanup(IPlayer _)
            {
                try { melee.OnTryHit -= OnSwing; }
                catch (Exception ex) { Warn(ex); }
                try { melee.OnEnemyHitted -= OnHit; }
                catch (Exception ex) { Warn(ex); }
                player.OnIPlayerDeadOrUnspawn -= Cleanup;
            }

            melee.OnTryHit += OnSwing;
            melee.OnEnemyHitted += OnHit;
            player.OnIPlayerDeadOrUnspawn += Cleanup;
        }
        catch (Exception ex)
        {
            Warn(ex);
        }
    }

    private static readonly object s_marker = new();

    private static void RecordSwing(BotOwner bot)
    {
        int n = Interlocked.Increment(ref _swingsSeen);
        if (n > LoggedSwingsPerRaid)
            return;

        try
        {
            var role = bot?.Profile?.Info?.Settings?.Role;
            float distance = bot?.Memory?.GoalEnemy?.Distance ?? -1f;
            Plugin.ModLogger.LogInfo($"Arena zombie melee: first native swing by {role} at {distance:0.0} m.");
        }
        catch (Exception ex)
        {
            Warn(ex);
        }
    }

    private static void RecordHit(BotOwner bot)
    {
        int n = Interlocked.Increment(ref _hitsSeen);
        if (n > LoggedHitsPerRaid)
            return;

        try
        {
            var role = bot?.Profile?.Info?.Settings?.Role;
            Plugin.ModLogger.LogInfo($"Arena zombie melee: first native landed hit by {role}.");
        }
        catch (Exception ex)
        {
            Warn(ex);
        }
    }

    /// <summary>
    /// Called exactly once from ZombieMode.OnMatchEnd: returns this raid's
    /// totals and clears them, so a second Zombie raid in the same process
    /// starts counting from zero rather than compounding onto the first.
    /// </summary>
    internal static (int Swings, int Hits) ConsumeTotals()
    {
        int swings = Interlocked.Exchange(ref _swingsSeen, 0);
        int hits = Interlocked.Exchange(ref _hitsSeen, 0);
        return (swings, hits);
    }

    private static void Warn(Exception ex)
    {
        // Logged every time, not just once: each occurrence is a different
        // bot's subscribe/cleanup, and silently swallowing all but the first
        // would hide a hook that stopped working for every zombie after it.
        // Still bounded in practice — a GPU raid spawns at most a few dozen
        // zombies total, nowhere near log-spam territory.
        Plugin.ModLogger.LogWarning($"Arena zombie melee diagnostics: {ex.Message}");
    }
}
