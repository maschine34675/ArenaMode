using ArenaMode.Bots;
using ArenaMode.Combat;
using ArenaMode.Config;
using ArenaMode.Kits;
using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Modes;

/// <summary>
/// Counter-Strike, roughly: buy a loadout, fight one squad, and if you fall you
/// sit the round out.
///
/// This mode inverts the arena's central promise on purpose. Everywhere else
/// death costs nothing and you are back before the body lands; here it costs you
/// the round, which is what gives buying a decision and a corner a meaning. Two
/// consequences run through everything below — the player is held dead until the
/// next round (HoldRespawn), and the map has to stay empty otherwise, because a
/// round that ends when the squad is dead cannot end while bots keep arriving.
///
/// The emptiness is enforced twice, on purpose. The server silences the engine's
/// pumps on the per-raid clone when the client announces this mode; the client
/// silences ours with BurstSpawnService.ManualControl. Either alone would be a
/// single point of failure for a mode whose rounds would then never end.
/// </summary>
public sealed class TacticalMode : IArenaMode
{
    public enum Phase
    {
        /// <summary>Shopping. The squad has not spawned yet.</summary>
        Buy,

        /// <summary>The squad is out there.</summary>
        Live,

        /// <summary>Round decided; the standings are up and the next buy is counting in.</summary>
        Aftermath,
    }

    /// <summary>How long the aftermath lingers before the next buy phase.</summary>
    private const float AftermathSeconds = 6f;

    /// <summary>
    /// How long a squad request is given to produce its bots before the round
    /// starts counting them. Profile generation is a server round trip, so a
    /// squad that has not arrived yet must not read as "squad wiped".
    /// </summary>
    private const float SquadGraceSeconds = 12f;

    private ArenaMatch _match;

    private readonly List<BotOwner> _squad = new List<BotOwner>();

    private Phase _phase = Phase.Buy;
    private float _phaseEndsAt;
    private float _squadDeadlineAt;
    private int _squadRequested;
    private int _squadEpoch;

    private KitDefinition _bought;
    private bool _playerDown;
    private bool _roundStartPending;

    public string Id => "tactical";

    public string DisplayName => "Tactical";

    /// <summary>Rounds keep coming until the raid ends.</summary>
    public bool IsComplete => false;

    /// <summary>
    /// The round is the squad. An ambient top-up would keep feeding the map bots
    /// the round is not counting, and it would never end.
    /// </summary>
    public bool ControlsOwnSpawns => true;

    public int RoundNumber { get; private set; } = 1;
    public int RoundsWon { get; private set; }
    public int RoundsLost { get; private set; }

    /// <summary>Roubles. The handbook value of a kit is what it costs, so this is a currency the player already reads.</summary>
    public int Money { get; private set; }

    public Phase CurrentPhase => _phase;

    public bool InBuyPhase => _phase == Phase.Buy;

    public float PhaseRemaining => Mathf.Max(0f, _phaseEndsAt - Time.time);

    /// <summary>What the player will be handed when the round starts.</summary>
    public KitDefinition Bought => _bought;

    /// <summary>Squad members still standing. Counted live — bots die to each other too.</summary>
    public int SquadAlive
    {
        get
        {
            var alive = 0;
            foreach (var bot in _squad)
            {
                if (bot != null && bot.HealthController != null && bot.HealthController.IsAlive)
                    alive++;
            }

            return alive;
        }
    }

    public float DeathReplaySeconds => Settings.TacticalReplaySeconds.Value;

    /// <summary>
    /// Held from the moment the player goes down until the next round starts.
    /// The respawn routine waits on this before it picks a kit, which is what
    /// lets the buy menu decide what the player comes back holding.
    /// </summary>
    public bool HoldRespawn => _playerDown;

    public string HudStatusLine
    {
        get
        {
            var line = $"Round {RoundNumber}   {RoundsWon}-{RoundsLost}   {Money:N0} ₽";

            switch (_phase)
            {
                case Phase.Buy:
                    line += $"   BUY  {PhaseRemaining:0}s";
                    if (_bought != null)
                        line += $"   [{_bought.Name ?? _bought.Id}]";
                    break;

                case Phase.Live:
                    line += _playerDown ? "   DOWN" : $"   {SquadAlive} left";
                    break;

                case Phase.Aftermath:
                    line += $"   next round in {PhaseRemaining:0}s";
                    break;
            }

            return line;
        }
    }

    public void OnMatchStart(ArenaMatch match)
    {
        _match = match;

        Money = Settings.TacticalStartMoney.Value;
        RoundNumber = 1;
        RoundsWon = 0;
        RoundsLost = 0;
        _squad.Clear();
        _playerDown = false;
        _roundStartPending = false;
        _bought = Affordable();

        // Every respawn and every round-start swap asks this, so the player is
        // always handed what they bought rather than the config's selected kit.
        // GetBaseKit, not GetSelectedKit: the latter asks the provider, so this
        // lambda would have called itself for as long as _bought was null — an
        // unbounded recursion, and a StackOverflowException is not catchable.
        KitCatalog.KitProvider = () => _bought ?? KitCatalog.GetBaseKit();

        EnterBuy();
        Plugin.ModLogger.LogInfo($"Tactical: match started with {Money:N0} roubles.");
    }

    public void Tick(float deltaTime)
    {
        if (_match == null)
            return;

        switch (_phase)
        {
            case Phase.Buy:
                if (Time.time >= _phaseEndsAt)
                    StartRound();
                break;

            case Phase.Live:
                TickLive();
                break;

            case Phase.Aftermath:
                if (Time.time >= _phaseEndsAt)
                    EnterBuy();
                break;
        }
    }

    private void TickLive()
    {
        if (_playerDown)
            return;

        // The safety net, and it has to be checked or it is decoration: a squad
        // member stuck somewhere the player cannot reach would otherwise hold
        // the round — and therefore the match — open forever. Scored as a loss,
        // because the round was not won.
        if (Time.time >= _phaseEndsAt)
        {
            Plugin.ModLogger.LogWarning(
                $"Tactical: round {RoundNumber} ran out of time with {SquadAlive} enemies left.");
            EndRound(won: false);
            return;
        }

        // The squad is only allowed to read as "wiped" once it has had time to
        // arrive. Without this the round would end the instant it started, every
        // time, because profile generation has not come back yet.
        if (Time.time < _squadDeadlineAt && _squad.Count < _squadRequested)
            return;

        if (_squad.Count == 0)
        {
            // Nothing arrived at all within the grace period. Calling that a win
            // would hand out a free round, so it is a no-contest: back to the
            // buy phase, money untouched, round number unchanged.
            if (Time.time >= _squadDeadlineAt)
            {
                Plugin.ModLogger.LogWarning("Tactical: no squad arrived — replaying the round.");

                // Bumped so the in-flight request, if it ever lands, is retired
                // rather than joining a round that has already moved on.
                _squadEpoch++;
                EnterBuy();
            }

            return;
        }

        if (SquadAlive == 0)
            EndRound(won: true);
    }

    public void OnKill(ArenaKill kill)
    {
        if (_phase != Phase.Live || !kill.KilledByLocal || kill.LocalIsVictim)
            return;

        Money += Settings.TacticalKillReward.Value;
    }

    public void OnLocalDeath()
    {
        if (_phase != Phase.Live || _playerDown)
            return;

        // Set BEFORE the round is scored: HoldRespawn reads it, and the respawn
        // routine is already running by the time this returns.
        _playerDown = true;
        EndRound(won: false);
    }

    public void OnLocalRespawn()
    {
        // The hold is what ends a wait, not this — by the time the player is
        // standing again the next round has already started them off.
        _playerDown = false;
    }

    public void OnMatchEnd()
    {
        KitCatalog.KitProvider = null;
        _squad.Clear();

        Plugin.ModLogger.LogInfo(
            $"Tactical ended: {RoundsWon}-{RoundsLost} over {RoundNumber} round(s), {Money:N0} roubles left.");
    }

    /// <summary>Ends the buy phase now. The round still starts through Tick, so there is one path in.</summary>
    public void ReadyUp()
    {
        if (_phase == Phase.Buy)
            _phaseEndsAt = Time.time;
    }

    /// <summary>Buy a kit. Refuses what the player cannot afford, and charges nothing until the round starts.</summary>
    public bool TryBuy(KitDefinition kit)
    {
        if (kit == null || _phase != Phase.Buy)
            return false;

        if (PriceOf(kit) > Money)
            return false;

        _bought = kit;
        return true;
    }

    public int PriceOf(KitDefinition kit) => kit?.Price ?? 0;

    public bool CanAfford(KitDefinition kit) => PriceOf(kit) <= Money;

    private void EnterBuy()
    {
        _phase = Phase.Buy;
        _phaseEndsAt = Time.time + Mathf.Max(3f, Settings.TacticalBuySeconds.Value);
        _roundStartPending = true;

        // The last choice carries over when it is still affordable — re-picking
        // the same kit every round would be busywork, not a decision.
        if (_bought == null || !CanAfford(_bought))
            _bought = Affordable();

        ArenaToasts.Show($"Round {RoundNumber} — buy phase");
    }

    /// <summary>The dearest kit the player can afford, so being broke still arms them.</summary>
    private KitDefinition Affordable()
    {
        KitDefinition best = null;
        foreach (var kit in KitCatalog.Kits ?? new List<KitDefinition>())
        {
            if (PriceOf(kit) > Money)
                continue;

            if (best == null || PriceOf(kit) > PriceOf(best))
                best = kit;
        }

        // Nothing affordable at all: hand out the cheapest anyway rather than
        // sending the player in with nothing. Being broke is a setback, not a
        // reason to stand in a corridor unarmed.
        if (best == null)
        {
            foreach (var kit in KitCatalog.Kits ?? new List<KitDefinition>())
            {
                if (best == null || PriceOf(kit) < PriceOf(best))
                    best = kit;
            }
        }

        return best;
    }

    private void StartRound()
    {
        if (!_roundStartPending)
            return;

        _roundStartPending = false;

        // THE hold is released here, and this is the only place that can do it.
        // _playerDown was cleared in OnLocalRespawn, which is raised by the tail
        // of the very respawn routine the hold parks — a circular wait that left
        // the first death in a Tactical raid permanently dead behind a black
        // screen. Captured first, because the round-break swap below still needs
        // to know whether this player survived the last round or is being handed
        // back a body.
        var wasDown = _playerDown;
        _playerDown = false;

        // Charged now rather than at the click, so browsing the menu is free and
        // a round that never starts costs nothing.
        var price = PriceOf(_bought);
        if (price <= Money)
            Money -= price;

        _squad.Clear();
        _phase = Phase.Live;
        _phaseEndsAt = Time.time + Mathf.Max(30f, Settings.TacticalRoundSeconds.Value);
        _squadDeadlineAt = Time.time + SquadGraceSeconds;

        // The epoch is what makes an abandoned request harmless. The no-contest
        // path can start a new round while the last request is still in flight —
        // profile generation is a server round trip — and without this those bots
        // would arrive onto a map the new round believes is empty.
        var epoch = ++_squadEpoch;

        var size = Mathf.Max(1, Settings.TacticalSquadSize.Value);
        _squadRequested = BurstSpawnService.SpawnSquad(
            size,
            asPmc: Settings.TacticalSquadIsPmc.Value,
            difficulty: ArenaPressure.DifficultyFor(Settings.TacticalSquadIsPmc.Value),
            observer: bot =>
            {
                if (bot == null)
                    return;

                if (epoch != _squadEpoch)
                {
                    // A superseded request. Killing rather than adopting: adopting
                    // would silently inflate a round the player was told holds a
                    // specific number of enemies.
                    try
                    {
                        var stale = bot.GetPlayer?.ActiveHealthController;
                        if (stale != null && stale.IsAlive)
                            stale.Kill(EFT.EDamageType.Undefined);
                    }
                    catch (Exception ex)
                    {
                        Plugin.ModLogger.LogWarning($"Tactical: stale squad member survived: {ex.Message}");
                    }

                    return;
                }

                if (!_squad.Contains(bot))
                    _squad.Add(bot);
            });

        // A player who SURVIVED the last round is standing there in last round's
        // gear; the round-break swap puts them in what they just bought. One who
        // died is handled by the held respawn instead, which picks the same kit
        // through the same provider once HoldRespawn lets go.
        if (!wasDown
            && Comfort.Common.Singleton<EFT.AbstractGame>.Instance is LocalGame game
            && game.Status == GameStatus.Started)
        {
            Respawn.LiveKitRespawn.AtRoundBreak(
                game,
                Settings.SpawnProtectionSeconds.Value,
                () => _phase == Phase.Live,
                null);
        }

        Plugin.ModLogger.LogInfo(
            $"Tactical round {RoundNumber}: {_squadRequested} enemies, kit '{_bought?.Id}' for {price:N0}, "
            + $"{Money:N0} left.");

        ArenaToasts.Show($"Round {RoundNumber} — {_squadRequested} enemies");
    }

    /// <summary>
    /// Clears the map of whatever is left of the round's squad.
    ///
    /// A LOST round leaves its survivors standing, and the old code only forgot
    /// about them — so they became uncounted permanent residents hunting a
    /// player frozen in the buy menu, and the mode's "the map is empty
    /// otherwise" promise held for round one only.
    /// </summary>
    private void RetireSquad()
    {
        foreach (var bot in _squad)
        {
            try
            {
                // Through the PLAYER's ActiveHealthController: BotOwner.HealthController
                // is the IHealthController interface, which has no Kill. Same
                // route ArenaGiveUp uses on the human.
                var health = bot?.GetPlayer?.ActiveHealthController;
                if (health != null && health.IsAlive)
                    health.Kill(EFT.EDamageType.Undefined);
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning($"Tactical: could not retire a squad member: {ex.Message}");
            }
        }

        _squad.Clear();
    }

    private void EndRound(bool won)
    {
        if (_phase != Phase.Live)
            return;

        RetireSquad();

        _phase = Phase.Aftermath;
        _phaseEndsAt = Time.time + AftermathSeconds;

        if (won)
        {
            RoundsWon++;
            Money += Settings.TacticalWinBonus.Value;
            ArenaToasts.Show($"Round {RoundNumber} won  (+{Settings.TacticalWinBonus.Value:N0} ₽)");
        }
        else
        {
            RoundsLost++;
            Money += Settings.TacticalLossBonus.Value;
            ArenaToasts.Show($"Round {RoundNumber} lost  (+{Settings.TacticalLossBonus.Value:N0} ₽)");
        }

        Plugin.ModLogger.LogInfo(
            $"Tactical round {RoundNumber} {(won ? "won" : "lost")} — {RoundsWon}-{RoundsLost}, {Money:N0} roubles.");

        RoundNumber++;
    }
}
