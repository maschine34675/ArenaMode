using ArenaMode.Config;
using ArenaMode.Session;
using ArenaMode.Shared;
using Comfort.Common;
using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>Owns negotiated Deathmatch/Gun Game population for an entire raid.</summary>
internal static class ArenaSoloPopulation
{
    private static ArenaSoloPopulationPolicy _population;
    private static LocalGame _game;
    private static GameWorld _world;
    private static BotSpawner _spawner;
    private static readonly List<BotOwner> Bodies = new List<BotOwner>(24);
    private static readonly List<BotOwner> Snapshot = new List<BotOwner>(24);
    private static BotOwner _boss;
    private static bool _nextBear;
    private static float _nextAttempt;
    private static float _nextDiagnostic;
    internal static int Generation { get; private set; }
    internal static bool IsSelected => ArenaSession.IsActive && ArenaSession.Participants.HasValue
        && ArenaParticipantPolicy.IsSolo(ArenaSession.ActiveMode);

    // Called only on actual raid start/end/failure. Burst.Reset also happens at
    // respawn and deliberately does not reset this independent lifetime.
    internal static void Reset()
    {
        Generation++;
        _population?.Stop(); _population = null;
        Bodies.Clear(); Snapshot.Clear();
        _boss = null; _game = null; _world = null; _spawner = null;
        _nextAttempt = _nextDiagnostic = 0f; _nextBear = false;
    }

    internal static bool IsLeaseCurrent(int generation)
        => generation == Generation && IsSelected && _population != null
            && Singleton<AbstractGame>.Instantiated && ReferenceEquals(Singleton<AbstractGame>.Instance, _game)
            && _game.Status == GameStatus.Started
            && Singleton<GameWorld>.Instantiated && ReferenceEquals(Singleton<GameWorld>.Instance, _world)
            && ArenaSession.IsExactActiveRaid(_game, _world)
            && Singleton<IBotGame>.Instantiated
            && ReferenceEquals(Singleton<IBotGame>.Instance?.BotsController?.BotSpawner, _spawner);

    internal static void Tick()
    {
        if (!IsSelected || Time.time < _nextAttempt) return;
        _nextAttempt = Time.time + 0.5f;
        try
        {
            if (_population == null)
            {
                if (!Singleton<AbstractGame>.Instantiated || Singleton<AbstractGame>.Instance is not LocalGame game
                    || game.Status != GameStatus.Started || !Singleton<GameWorld>.Instantiated
                    || !ArenaSession.IsExactActiveRaid(game, Singleton<GameWorld>.Instance)
                    || !Singleton<IBotGame>.Instantiated) return;
                var spawner = Singleton<IBotGame>.Instance?.BotsController?.BotSpawner;
                if (spawner == null) return;
                _game = game; _world = Singleton<GameWorld>.Instance; _spawner = spawner;
                _population = new ArenaSoloPopulationPolicy(ArenaSession.Participants.Value, ArenaSession.ParticipantBosses);
            }
            int generation = Generation;
            if (!IsLeaseCurrent(generation) || Modes.ArenaMatchService.Current?.Mode == null
                || Modes.ArenaMatchService.Current.Mode.ControlsOwnSpawns) return;
            if (!TrySnapshot(null, out int alive, out int bosses, out int pmcs)) return;
            var population = _population;
            population.Sweep(Time.realtimeSinceStartup);
            if (Time.realtimeSinceStartup >= _nextDiagnostic)
            {
                _nextDiagnostic = Time.realtimeSinceStartup + 30f;
                Plugin.ModLogger.LogInfo($"Arena solo population: total={population.Capacity + 1} (human slot reserved), "
                    + $"alive={alive}, bosses={bosses}, PMC={pmcs}, pending={population.Outstanding} "
                    + $"(native uncertain={population.NativeOutstanding}), boss slot={population.BossEnabled}.");
            }
            bool boss = population.BossEnabled && bosses == 0 && !population.BossPending;
            int normalCapacity = population.Capacity - (population.BossEnabled ? 1 : 0);
            bool pmc = !boss && pmcs + population.PendingPmc
                < Math.Min(normalCapacity, ArenaPressure.PmcFloor(Settings.PmcFloor.Value));
            if (!population.TryReserve(alive, bosses, boss, pmc, Time.realtimeSinceStartup, out var ticket)) return;
            var role = boss ? (population.NextBossIsKilla ? WildSpawnType.bossKillaAgro : WildSpawnType.bossTagillaAgro)
                : pmc ? (_nextBear ? WildSpawnType.pmcBEAR : WildSpawnType.pmcUSEC) : WildSpawnType.assault;
            if (pmc) _nextBear = !_nextBear;

            bool Current() => IsLeaseCurrent(generation) && ReferenceEquals(_population, population)
                && Modes.ArenaMatchService.Current?.Mode?.ControlsOwnSpawns == false
                && population.IsCurrent(ticket, Time.realtimeSinceStartup);
            bool Begin() => Current() && TrySnapshot(null, out var count, out var bossCount, out _)
                && population.TryBeginDispatch(ticket, count, bossCount, Time.realtimeSinceStartup);
            bool Arrive(BotOwner bot)
            {
                if (!Current() || !TrySnapshot(bot, out var other, out var otherBosses, out _)
                    || !population.CanAdmit(ticket, other, otherBosses, Time.realtimeSinceStartup)) return false;
                if (!Bodies.Exists(b => ReferenceEquals(b, bot))) Bodies.Add(bot);
                if (boss) _boss = bot;
                population.Settle(ticket);
                return true;
            }
            BurstSpawnService.SpawnManagedBot(role, ArenaPressure.DifficultyFor(pmc), Arrive, Current, Begin,
                handed => population.DispatchFinished(ticket, handed),
                (bot, retired, original) =>
                {
                    // Failed retirement, including an unexpected extra body,
                    // remains counted even when the native registry loses it.
                    // An old raid's callback must never contaminate this list.
                    if (generation == Generation && ReferenceEquals(_population, population)
                        && !ReferenceEquals(bot, null) && !Bodies.Exists(b => ReferenceEquals(b, bot))) Bodies.Add(bot);
                    population.Rejected(ticket, bot, retired, original);
                }, ticket.Id);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena solo population deferred: {ex.Message}");
            _nextAttempt = Time.time + 3f;
        }
    }

    private static bool IsBoss(BotOwner bot)
        => bot.Profile?.Info?.Settings?.Role == WildSpawnType.bossTagillaAgro
            || bot.Profile?.Info?.Settings?.Role == WildSpawnType.bossKillaAgro;

    // Merge the native registry with accepted identities; a missing registry
    // entry is not death. PreActive owners count identically to Active owners.
    private static bool TrySnapshot(BotOwner except, out int alive, out int bosses, out int pmcs)
    {
        alive = bosses = pmcs = 0;
        var owners = _spawner?._bots?.BotOwners;
        if (owners == null) return SnapshotUnavailable("native bot registry unavailable");
        Snapshot.Clear();
        foreach (var bot in owners)
        {
            if (ReferenceEquals(bot, null)) continue;
            if (!Snapshot.Exists(b => ReferenceEquals(b, bot))) Snapshot.Add(bot);
        }
        for (int i = Bodies.Count - 1; i >= 0; i--)
        {
            var bot = Bodies[i];
            if (ReferenceEquals(bot, null) || bot == null || bot.HealthController == null)
                return SnapshotUnavailable("accepted body disappeared before a proven death; capacity retained until raid teardown");
            if (!bot.HealthController.IsAlive)
            {
                Bodies.RemoveAt(i);
                _population.ObserveDeath(bot);
                if (ReferenceEquals(_boss, bot)) { _boss = null; _population.BossDied(); }
                continue;
            }
            if (!Snapshot.Exists(b => ReferenceEquals(b, bot))) Snapshot.Add(bot);
        }
        foreach (var bot in Snapshot)
        {
            if (ReferenceEquals(bot, except)) continue;
            if (bot == null || bot.HealthController == null) return SnapshotUnavailable("registered bot health unavailable");
            if (!bot.HealthController.IsAlive) continue;
            alive++;
            if (IsBoss(bot)) bosses++;
            var role = bot.Profile?.Info?.Settings?.Role;
            if (role == WildSpawnType.pmcUSEC || role == WildSpawnType.pmcBEAR) pmcs++;
        }
        return true;
    }

    private static bool SnapshotUnavailable(string reason)
    {
        if (Time.realtimeSinceStartup >= _nextDiagnostic)
        {
            _nextDiagnostic = Time.realtimeSinceStartup + 30f;
            Plugin.ModLogger.LogWarning("Arena solo population suspended: " + reason + ".");
        }
        return false;
    }
}
