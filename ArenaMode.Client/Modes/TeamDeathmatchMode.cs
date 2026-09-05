using ArenaMode.Bots;
using ArenaMode.Combat;
using ArenaMode.Config;
using ArenaMode.Effects;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArenaMode.Modes;

/// <summary>
/// Continuous faction deathmatch. The human owns one permanent faction slot;
/// bots own disposable bodies, never PFT follower accounts or persisted kits.
/// </summary>
public sealed class TeamDeathmatchMode : IArenaMode
{
    private ArenaMatch _match;
    private GameWorld _world;
    private LocalGame _game;
    private Player _local;
    private string _localId;
    private TeamDeathmatchPopulation _population;
    private readonly List<BotOwner> _bots = new();
    private readonly Dictionary<BotOwner, float> _activationDeadline = new();
    private readonly int[] _activationFailures = new int[2];
    // Only scalar identity history survives corpse cleanup. It also classifies
    // projectiles whose owner died before their hit/tracer was observed.
    private readonly Dictionary<string, bool> _usecById = new(StringComparer.Ordinal);
    private float _nextTick;
    private bool _ended;
    private bool _announcedReady;
    private bool _reportedStall;
    private readonly float[] _retryAt = new float[2];
    private string _failure;
    private Player _pendingGiveUpLife;
    private Player _creditedGiveUpLife;

    public string Id => ArenaModeCatalog.TeamDeathmatchId;
    public string DisplayName => "Team Deathmatch";
    public bool IsComplete => _failure != null;
    public float DeathReplaySeconds => 0f;
    public bool HoldRespawn => false;
    public bool ControlsOwnSpawns => true;
    public int TeamSize => _population?.TeamSize ?? 5;
    public string FriendlyFaction => _population?.LocalUsec == true ? "USEC" : "BEAR";
    public string OpponentFaction => _population?.LocalUsec == true ? "BEAR" : "USEC";

    public string HudStatusLine
    {
        get
        {
            if (_failure != null) return "TEAM DM stopped — " + _failure;
            if (_match == null) return "TEAM DM";
            int friendly = _match.Scoreboard.Totals(ArenaScoreboardSide.Friendly).Kills;
            int opponent = _match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills;
            string waiting = _reportedStall ? " | spawn delayed" : "";
            return $"{TeamSize}v{TeamSize}  <color=#7FDBFF>{FriendlyFaction} {friendly}</color> : "
                   + $"<color=#FF6B6B>{opponent} {OpponentFaction}</color>{waiting}";
        }
    }

    public void OnMatchStart(ArenaMatch match)
    {
        _match = match;
        _world = Singleton<GameWorld>.Instance;
        _game = Singleton<AbstractGame>.Instance as LocalGame;
        _local = _world?.MainPlayer;
        _localId = _local?.ProfileId;
        EPlayerSide? faction = TeamDeathmatchRelations.Faction(_local);
        if (!IsCurrentRaid() || string.IsNullOrEmpty(_localId)
            || !faction.HasValue || Compat.PitFireTeamCompat.HasFrozenTeamRoster)
        {
            Fail("local PMC identity or independent roster unavailable");
            return;
        }
        _population = new TeamDeathmatchPopulation(Settings.TeamDeathmatchSize.Value, faction == EPlayerSide.Usec);
        TeamDeathmatchRelations.Reset();
        TeamDeathmatchRelations.Register(_local);
        _usecById.Add(_localId, _population.LocalUsec);
        ArenaTracerTeams.BeginFactionMatch(_localId);
        var spawner = Singleton<IBotGame>.Instance?.BotsController?.BotSpawner;
        spawner?.SetMaxBots(2 * TeamSize - 1);
        Plugin.ModLogger.LogInfo($"Team Deathmatch: {TeamSize}v{TeamSize}, local {FriendlyFaction}, "
            + $"{TeamSize - 1} friendly / {TeamSize} opposing bot slots; no PFT followers.");
    }

    public void Tick(float deltaTime)
    {
        if (_ended || IsComplete || _population == null || !IsCurrentRaid() || Time.time < _nextTick) return;
        _nextTick = Time.time + 1f;
        try
        {
            Player current = _world.MainPlayer;
            if (!Respawn.ArenaRespawnDriver.IsRespawnInProgress
                && current != null && !ReferenceEquals(current, _local)) OnLocalRespawn(current);
            if (_local != null && _local.Profile?.Info?.Side != (_population.LocalUsec ? EPlayerSide.Usec : EPlayerSide.Bear))
                Fail("local PMC faction changed");
            if (IsComplete) return;
            if (!ValidateActivations()) return;
            _bots.RemoveAll(bot => !Alive(bot));
            var owners = Singleton<IBotGame>.Instance?.BotsController?.BotSpawner?._bots?.BotOwners;
            if (owners == null) return; // unreadable occupancy must never become zero
            foreach (BotOwner bot in owners.ToArray())
            {
                if (Alive(bot) && !_bots.Any(known => ReferenceEquals(known, bot)))
                    if (!Retire(bot, "unowned body")) return;
            }

            var actors = _bots.Where(Alive).Select(bot => bot.GetPlayer).ToList();
            if (_local != null && _local.HealthController?.IsAlive == true) actors.Add(_local);
            TeamDeathmatchRelations.Reconcile(actors);
            if (!TrySynchronizeScoreboardRoster(out var problem))
            { Fail("scoreboard affiliation drift: " + problem); return; }
            Refill(true);
            Refill(false);
            bool stalled = _population.IsStalled(Time.time);
            if (stalled && !_reportedStall)
                Plugin.ModLogger.LogWarning("Team Deathmatch: spawn request timed out; late arrivals are rejected, "
                    + "profile loads remain single-flight and uncertain native activations have a bounded retry budget.");
            _reportedStall = stalled;
            if (!_announcedReady && CountBots(true) == _population.BotTarget(true)
                && CountBots(false) == _population.BotTarget(false)
                && _bots.All(bot => bot.BotState == EBotState.Active))
            {
                _announcedReady = true;
                _match.ReportAnnouncerCue(ArenaAnnouncerCue.RoundStart);
                Plugin.ModLogger.LogInfo($"Team Deathmatch ready: {TeamSize}v{TeamSize}.");
            }
        }
        catch (Exception ex)
        {
            Fail("roster could not be validated: " + ex.GetBaseException().Message);
        }
    }

    private void Refill(bool usec)
    {
        if (IsComplete || _ended || Time.time < _retryAt[usec ? 0 : 1]) return;
        if (!_population.TryReserve(usec, CountBots(usec), Time.time, out var ticket)) return;
        var role = usec ? WildSpawnType.pmcUSEC : WildSpawnType.pmcBEAR;
        int planned = BurstSpawnService.SpawnFactionBot(role, ArenaPressure.DifficultyFor(true),
            bot => Adopt(ticket, bot),
            () => IsCurrentRaid() && !_ended && !IsComplete && _population.IsCurrent(ticket, Time.time),
            dispatched =>
            {
                _population.DispatchFinished(ticket, dispatched);
                if (!dispatched) DelayRetry(ticket);
            },
            () => { _population.Settle(ticket); DelayRetry(ticket); });
        if (planned == 0) { _population.Settle(ticket); DelayRetry(ticket); }
    }

    private void DelayRetry(TeamDeathmatchPopulation.Ticket ticket)
        => _retryAt[ticket.Usec ? 0 : 1] = Time.time + 5f;

    private bool ValidateActivations()
    {
        foreach (BotOwner bot in _bots)
        {
            if (!Alive(bot)) { _activationDeadline.Remove(bot); continue; }
            bool usec = _usecById[bot.ProfileId];
            int index = usec ? 0 : 1;
            EPlayerSide side = usec ? EPlayerSide.Usec : EPlayerSide.Bear;
            WildSpawnType role = usec ? WildSpawnType.pmcUSEC : WildSpawnType.pmcBEAR;
            if (bot.Profile?.Info?.Side != side || bot.Profile?.Info?.Settings?.Role != role
                || !TeamDeathmatchRelations.Register(bot.GetPlayer) || !ValidGroup(bot, side))
            { Fail("admitted bot faction/group changed"); return false; }
            if (bot.BotState == EBotState.Active)
            {
                if (_activationDeadline.Remove(bot)) _activationFailures[index] = 0;
                continue;
            }
            if (bot.BotState == EBotState.PreActive
                && _activationDeadline.TryGetValue(bot, out float deadline) && Time.time < deadline) continue;
            // EFT can leave a healthy body in ActiveFail or wait forever for
            // its weapon manager. Arrival acknowledged the request, not combat
            // readiness: retire the exact failed body before freeing its slot.
            if (!Retire(bot, "failed or timed-out activation (" + bot.BotState + ")")) return false;
            _activationDeadline.Remove(bot);
            _retryAt[index] = Time.time + 5f;
            if (++_activationFailures[index] >= 3)
            { Fail("three consecutive activation failures for " + (usec ? "USEC" : "BEAR")); return false; }
        }
        return true;
    }

    private void Adopt(TeamDeathmatchPopulation.Ticket ticket, BotOwner bot)
    {
        try
        {
            var player = bot?.GetPlayer;
            EPlayerSide expected = ticket.Usec ? EPlayerSide.Usec : EPlayerSide.Bear;
            if (!IsCurrentRaid() || !_population.IsCurrent(ticket, Time.time) || !Alive(bot)
                || TeamDeathmatchRelations.Faction(player) != expected
                || string.IsNullOrWhiteSpace(bot.ProfileId) || _usecById.ContainsKey(bot.ProfileId)
                || CountBots(ticket.Usec) >= _population.BotTarget(ticket.Usec)
                || !ValidGroup(bot, expected) || !TeamDeathmatchRelations.Register(player))
            {
                Retire(bot, "expired, duplicate, over-capacity or inconsistent faction arrival");
                DelayRetry(ticket);
                return;
            }
            _usecById.Add(bot.ProfileId, ticket.Usec);
            if (!ArenaTracerTeams.RegisterFactionActor(bot.ProfileId, ticket.Usec == _population.LocalUsec))
            {
                Retire(bot, "tracer affiliation drift");
                Fail("tracer affiliation drift");
                return;
            }
            _bots.Add(bot); // PreActive already occupies its slot
            _activationDeadline.Add(bot, Time.time + TeamDeathmatchPopulation.RequestTimeout);
            _population.Settle(ticket);
            if (!TrySynchronizeScoreboardRoster(out var problem))
                Fail("scoreboard affiliation drift: " + problem);
            _nextTick = 0f;
            Plugin.ModLogger.LogInfo($"Team Deathmatch admitted {(ticket.Usec ? "USEC" : "BEAR")} "
                + $"request {ticket.Id}: {CountBots(ticket.Usec)}/{_population.BotTarget(ticket.Usec)} bots.");
        }
        finally { _population.Settle(ticket); }
    }

    private static bool ValidGroup(BotOwner bot, EPlayerSide expected)
    {
        BotsGroup group = bot?.BotsGroup;
        if (group == null || group.Side != expected
            || !TeamDeathmatchRelations.TryGroupRelationship(group, bot.GetPlayer, out bool enemies)
            || enemies) return false;
        for (int i = 0; i < group.MembersCount; i++)
        {
            BotOwner member = group.Member(i);
            if (Alive(member) && TeamDeathmatchRelations.Faction(member.GetPlayer) != expected) return false;
        }
        return true;
    }

    private int CountBots(bool usec)
        => _bots.Count(bot => Alive(bot) && _usecById.TryGetValue(bot.ProfileId, out bool side) && side == usec);

    private static bool Alive(BotOwner bot)
        => !ReferenceEquals(bot, null) && bot != null && bot.GetPlayer != null
           && bot.HealthController?.IsAlive == true;

    private bool IsCurrentRaid()
        => !_ended && _game != null && _world != null
           && Singleton<AbstractGame>.Instantiated && ReferenceEquals(Singleton<AbstractGame>.Instance, _game)
           && Singleton<GameWorld>.Instantiated && ReferenceEquals(Singleton<GameWorld>.Instance, _world)
           && ArenaSession.IsExactActiveRaid(_game, _world)
           && string.Equals(ArenaSession.ActiveMode, Id, StringComparison.OrdinalIgnoreCase);

    internal bool TrySynchronizeScoreboardRoster(out string problem)
    {
        problem = null;
        if (_population == null || _match == null || _local == null || !IsCurrentRaid()) return true;
        var friendly = new List<ArenaScoreboardActor>();
        var opposing = new List<ArenaScoreboardActor>();
        foreach (BotOwner bot in _bots)
        {
            if (bot == null || bot.GetPlayer == null || !_usecById.TryGetValue(bot.ProfileId, out bool usec)) continue;
            (usec == _population.LocalUsec ? friendly : opposing).Add(ArenaMatchService.SnapshotActor(bot.GetPlayer));
        }
        return _match.Scoreboard.TrySynchronizeRoster(1, ArenaMatchService.SnapshotActor(_local),
            friendly, opposing, out problem);
    }

    internal void ClassifyKill(ArenaKill kill)
    {
        kill.KillerIsFriendly = Friendly(kill.KillerProfileId);
        kill.VictimIsFriendly = Friendly(kill.VictimProfileId);
        kill.ScoreEligible &= !_ended && !IsComplete && IsCurrentRaid()
            && kill.KillerIsFriendly.HasValue && kill.VictimIsFriendly.HasValue
            && kill.KillerIsFriendly != kill.VictimIsFriendly;
        kill.AwardEligible &= kill.ScoreEligible;
    }

    private bool? Friendly(string id)
        => _population != null && id != null && _usecById.TryGetValue(id, out bool usec)
            ? usec == _population.LocalUsec : null;

    public void OnKill(ArenaKill kill) { }
    public void OnLocalDeath() => _nextTick = 0f;

    /// <summary>Reserve this exact living body for the synchronous Give up call.</summary>
    internal bool TryBeginLocalGiveUp(Player player)
    {
        if (_pendingGiveUpLife != null || ReferenceEquals(_creditedGiveUpLife, player)
            || !IsCurrentLocalGiveUpLife(player) || player.ActiveHealthController?.IsAlive != true)
            return false;
        _pendingGiveUpLife = player;
        return true;
    }

    /// <summary>
    /// Team-only bookkeeping, after native death has been observed. The normal
    /// local-death event owns deaths/streak reset; this must never synthesize an
    /// ArenaKill or give a random opponent personal credit or announcer awards.
    /// </summary>
    internal void CompleteLocalGiveUp(Player player, bool deathConfirmed)
    {
        if (!ReferenceEquals(_pendingGiveUpLife, player) || player == null) return;
        _pendingGiveUpLife = null;
        if (!deathConfirmed || !IsCurrentLocalGiveUpLife(player)
            || player.ActiveHealthController?.IsAlive != false
            || ReferenceEquals(_creditedGiveUpLife, player)) return;
        _creditedGiveUpLife = player;
        _match.Scoreboard.ReportOpponentForfeitKill();
    }

    private bool IsCurrentLocalGiveUpLife(Player player)
        => IsCurrentMatch()
           && player != null && ReferenceEquals(player, _local)
           && ReferenceEquals(player, _world.MainPlayer)
           && !player.IsAI
           && player.ProfileId == _localId
           && player.Profile?.Info?.Side
                == (_population.LocalUsec ? EPlayerSide.Usec : EPlayerSide.Bear);

    private bool IsCurrentMatch()
        => _population != null && !IsComplete && IsCurrentRaid()
           && ReferenceEquals(ArenaMatchService.Current, _match)
           && ReferenceEquals(_match?.Mode, this);

    /// <summary>
    /// Read-only affiliation for visible effects. Historical profile IDs and
    /// auto-admission in combat relations are deliberately insufficient: pooled
    /// visuals may outlive a bot or be rebound before its replacement is ready.
    /// </summary>
    internal bool TryClassifyCurrentBody(Player player, out bool friendly)
    {
        friendly = false;
        if (!IsCurrentMatch() || player == null || string.IsNullOrEmpty(player.ProfileId)
            || player.HealthController?.IsAlive != true)
            return false;
        if (IsCurrentLocalGiveUpLife(player))
        {
            friendly = true;
            return true;
        }
        var owner = player.AIData?.BotOwner;
        if (!player.IsAI || owner == null || owner.BotState != EBotState.Active
            || !ReferenceEquals(owner.GetPlayer, player)
            || owner.ProfileId != player.ProfileId
            || !_bots.Any(bot => ReferenceEquals(bot, owner))
            || !ReferenceEquals(_world.GetAlivePlayerByProfileID(player.ProfileId), player)
            || !_usecById.TryGetValue(player.ProfileId, out bool usec)
            || player.Profile?.Info?.Side != (usec ? EPlayerSide.Usec : EPlayerSide.Bear)
            || player.Profile?.Info?.Settings?.Role != (usec ? WildSpawnType.pmcUSEC : WildSpawnType.pmcBEAR))
            return false;
        friendly = usec == _population.LocalUsec;
        return true;
    }

    public void OnLocalRespawn(Player player)
    {
        if (_population == null || !IsCurrentRaid()) return;
        if (player == null || player.ProfileId != _localId
            || TeamDeathmatchRelations.Faction(player) != (_population.LocalUsec ? EPlayerSide.Usec : EPlayerSide.Bear))
        { Fail("local PMC faction changed"); return; }
        _local = player;
        _pendingGiveUpLife = null;
        _creditedGiveUpLife = null;
        TeamDeathmatchRelations.Register(player);
        _nextTick = 0f;
    }

    private void Fail(string reason)
    {
        if (_failure != null) return;
        _failure = reason;
        _population?.Stop();
        Plugin.ModLogger.LogError("Team Deathmatch stopped: " + reason);
        ArenaToasts.Show("TEAM DM stopped: " + reason);
    }

    private bool Retire(BotOwner bot, string reason)
    {
        try
        {
            if (!Alive(bot)) return true;
            using (ArenaAdministrativeDeathGate.Enter(bot.ProfileId))
                bot.GetPlayer.ActiveHealthController.Kill(EDamageType.Undefined);
            if (Alive(bot)) throw new InvalidOperationException("body stayed alive after administrative retirement");
            Plugin.ModLogger.LogWarning("Team Deathmatch retired " + reason + ".");
            return true;
        }
        catch (Exception ex)
        {
            // A suppressed Kill is not proof that this unowned slot is free.
            // Freeze refills immediately, including a synchronous spawn callback.
            Fail("could not retire " + reason + ": " + ex.GetBaseException().Message);
            return false;
        }
    }

    public void OnMatchEnd()
    {
        _ended = true;
        _pendingGiveUpLife = null;
        _creditedGiveUpLife = null;
        _population?.Stop();
        _bots.Clear();
        _activationDeadline.Clear();
        _usecById.Clear();
        TeamDeathmatchRelations.Reset();
    }
}
