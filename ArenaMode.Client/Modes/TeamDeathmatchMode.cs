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
public sealed partial class TeamDeathmatchMode : IArenaMode
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
    private bool _nativeStopObserved;
    private bool _announcedReady;
    private bool _reportedStall;
    private bool _reportedUnreadableRoster;
    private readonly float[] _retryAt = new float[2];
    private string _failure;
    private Player _pendingGiveUpLife;
    private Player _creditedGiveUpLife;
    private int _spawnGateEpoch;
    // Live Arena's team table has fixed seats (2026-11-08, from the fork survey): a
    // replacement bot takes the seat, the name and the running stats of the body it
    // replaces, so the table keeps the same names all match and a vacant seat stays
    // visible as OUT until the replacement arrives. Seats are match-local presentation:
    // the bot profile is never written (TeamSeatNames carries the display name) and kill
    // classification still runs on profile ids through _usecById.
    private sealed class Seat { public string Name, Label, LastProfileId; }
    private readonly Dictionary<string, Seat> _seats = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _seatByProfile = new(StringComparer.Ordinal);

    public string Id => IsDomination ? Shared.ArenaPopulationPolicy.Domination : ArenaModeCatalog.TeamDeathmatchId;
    public string DisplayName => IsDomination ? "Domination" : "Team Deathmatch";
    public bool IsComplete => _failure != null || HasResult;
    internal bool AssistScoringOpen => !_ended && !IsComplete && IsCurrentRaid() && MatchPhase == TdmMatchPhase.Live;
    public float DeathReplaySeconds => Session.ArenaReplaySelection.Seconds;
    public bool HoldRespawn => Areas.ArenaAreaRuntime.IsPending || HasResult;
    public bool ControlsOwnSpawns => true;
    public int TeamSize => _population?.TeamSize ?? 5;
    public string FriendlyFaction => _population?.LocalUsec == true ? "USEC" : "BEAR";
    public string OpponentFaction => _population?.LocalUsec == true ? "BEAR" : "USEC";
    internal bool InitialRosterReady => _announcedReady && IsCurrentMatch();

    public string HudStatusLine
    {
        get
        {
            if (_failure != null) return DisplayName + " stopped — " + _failure;
            if (HasResult) return ResultHeading + " — <color=" + ArenaTeamPalette.FriendlyHex + ">"
                + FriendlyFaction + " " + FinalFriendlyScore + "</color> : <color=" + ArenaTeamPalette.OpponentHex + ">"
                + FinalOpponentScore + " " + OpponentFaction + "</color>";
            if (Areas.ArenaAreaRuntime.IsPending) return "PREPARING ARENA AREA — checking navigation";
            if (_match == null) return DisplayName;
            int friendly = FriendlyTeamScore;
            int opponent = OpponentTeamScore;
            string waiting = _reportedStall ? " | spawn delayed" : "";
            return $"{TeamSize}v{TeamSize}  <color={ArenaTeamPalette.FriendlyHex}>{FriendlyFaction} {friendly}</color> : "
                   + $"<color={ArenaTeamPalette.OpponentHex}>{opponent} {OpponentFaction}</color> | {RulesStatus}{waiting}";
        }
    }

    public void OnMatchStart(ArenaMatch match)
    {
        _match = match;
        _world = Singleton<GameWorld>.Instance;
        _game = Singleton<AbstractGame>.Instance as LocalGame;
        _spawnGateEpoch = ArenaSession.ArmEpoch;
        _local = _world?.MainPlayer;
        _localId = _local?.ProfileId;
        _spawnShieldSeconds = 0f;
        ResetSeats();
        if (!IsDomination)
            _rules = new TeamDeathmatchMatchPolicy(Settings.TeamDeathmatchKillLimit.Value,
                Settings.TeamDeathmatchTimeLimitSeconds.Value);
        EPlayerSide? faction = TeamDeathmatchRelations.Faction(_local);
        if (!IsCurrentRaid() || string.IsNullOrEmpty(_localId)
            || !faction.HasValue || Compat.PitFireTeamCompat.HasFrozenTeamRoster)
        {
            Fail("local PMC identity or independent roster unavailable");
            return;
        }
        // Only the catalog's fixed team-box starts need spawn-camping protection.
        // Use the captured active raid, not menu/armed state or box readiness:
        // loading may still be assembling it, but box maps never fall back to
        // general starts. Freeze this alongside the rules for every replacement.
        if (Shared.ArenaTeamBoxCatalog.Applies(_world.LocationId, ArenaSession.ActiveMode))
            _spawnShieldSeconds = Settings.SpawnProtectionSeconds?.Value ?? 4f;
        _population = new TeamDeathmatchPopulation(
            Areas.ArenaAreaRuntime.CurrentTeamSize ?? (ArenaSession.Participants.HasValue
                ? ArenaSession.Participants.Value / 2 : Settings.TeamDeathmatchSize.Value),
            faction == EPlayerSide.Usec);
        TeamDeathmatchRelations.Reset();
        TeamDeathmatchRelations.Register(_local);
        _usecById.Add(_localId, _population.LocalUsec);
        ArenaTracerTeams.BeginFactionMatch(_localId);
        var spawner = Singleton<IBotGame>.Instance?.BotsController?.BotSpawner;
        spawner?.SetMaxBots(2 * TeamSize - 1);
        Plugin.ModLogger.LogDebug($"Team Deathmatch: {TeamSize}v{TeamSize}, local {FriendlyFaction}, "
            + $"{TeamSize - 1} friendly / {TeamSize} opposing bot slots; no PFT followers.");
        InitializeDomination();
        ClaimMonitors();
    }

    public void Tick(float deltaTime)
    {
        TickSpawnShields();
        if (ObserveNativeStop()) return;
        CheckMatchLimits();
        PushMonitors();
        if (HasResult) { TickResult(); return; }
        if (IsCurrentMatch() && MatchPhase == TdmMatchPhase.Countdown
            && (Areas.ArenaAreaRuntime.IsPending || Respawn.ArenaRespawnDriver.IsRespawnInProgress
                || Respawn.LiveKitRespawn.IsInitialSwapPending || _local?.HealthController?.IsAlive != true
                || !ReferenceEquals(_world.MainPlayer, _local))) TickMatchStart(false);
        // Check before the simulation-clock tick throttle. Pausing or a slow
        // frame must not extend the preparation's real-time watchdog.
        if (ObserveNativeStop() || CheckPreparationWatchdog()) return;
        if (ObserveNativeStop() || _ended || IsComplete || _population == null
            || !IsCurrentRaid() || Areas.ArenaAreaRuntime.IsPending || Time.time < _nextTick) return;
        _nextTick = Time.time + 1f;
        try
        {
            Player current = _world.MainPlayer;
            if (!Respawn.ArenaRespawnDriver.IsRespawnInProgress
                && current != null && !ReferenceEquals(current, _local)) OnLocalRespawn(current);
            // A replacement may temporarily own MainPlayer while the respawn
            // handover is still pending. Check the frozen native identity here;
            // physical-current-body admission belongs to OnLocalRespawn below.
            if (_local != null && (!ArenaTeamSideSelection.NativeUsec.HasValue
                || _local.Profile?.Info?.Side != (ArenaTeamSideSelection.NativeUsec.Value ? EPlayerSide.Usec : EPlayerSide.Bear)
                || ArenaSession.TeamDeathmatchLocalSide(_game, _world)
                    != (_population.LocalUsec ? EPlayerSide.Usec : EPlayerSide.Bear)))
                Fail("local PMC faction changed");
            if (IsComplete) return;
            if (!ValidateActivations()) return;
            _bots.RemoveAll(bot => !Alive(bot));
            var owners = Singleton<IBotGame>.Instance?.BotsController?.BotSpawner?._bots?.BotOwners;
            if (owners == null)
            {
                TickMatchStart(false);
                // Unreadable occupancy must never become zero. Report the
                // transition, not every tick while native storage is missing.
                if (!_reportedUnreadableRoster)
                    Plugin.ModLogger.LogWarning("Team Deathmatch: native roster unavailable; refill paused; " + DescribeState());
                _reportedUnreadableRoster = true;
                return;
            }
            if (_reportedUnreadableRoster)
                Plugin.ModLogger.LogInfo("Team Deathmatch: native roster readable again; bot refills resumed.");
            _reportedUnreadableRoster = false;
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
            if (ObserveNativeStop() || !IsCurrentRaid()) return;
            bool stalled = _population.IsStalled(Time.realtimeSinceStartup);
            if (stalled && !_reportedStall)
                Plugin.ModLogger.LogWarning("Team Deathmatch: spawn delayed; preparation remains single-flight "
                    + "with a terminal 300s watchdog; expired native arrivals are rejected and retain bounded debt; "
                    + DescribeState());
            else if (!stalled && _reportedStall)
                Plugin.ModLogger.LogInfo("Team Deathmatch: delayed spawn requests settled; bot refills resumed.");
            _reportedStall = stalled;
            bool ready = CountBots(true) == _population.BotTarget(true)
                && CountBots(false) == _population.BotTarget(false)
                && _bots.All(bot => bot.BotState == EBotState.Active)
                && _local?.HealthController?.IsAlive == true
                && ReferenceEquals(_world.MainPlayer, _local)
                && !Respawn.ArenaRespawnDriver.IsRespawnInProgress
                && !Respawn.LiveKitRespawn.IsInitialSwapPending;
            TickMatchStart(ready);
            if (!_announcedReady && MatchPhase == TdmMatchPhase.Live)
            {
                _announcedReady = true;
                Areas.ArenaAreaRuntime.FinishTeamAssembly();
                _match.ReportAnnouncerCue(ArenaAnnouncerCue.RoundStart);
                Plugin.ModLogger.LogDebug($"Team Deathmatch ready: {TeamSize}v{TeamSize}.");
            }
        }
        catch (Exception ex)
        {
            Fail("roster could not be validated: " + ex.GetBaseException().Message);
        }
    }

    private void Refill(bool usec)
    {
        if (ObserveNativeStop() || IsComplete || !IsCurrentRaid() || Time.time < _retryAt[usec ? 0 : 1]) return;
        if (!_population.TryReserve(usec, CountBots(usec), Time.realtimeSinceStartup, out var ticket)) return;
        var role = usec ? WildSpawnType.pmcUSEC : WildSpawnType.pmcBEAR;
        Plugin.ModLogger.LogDebug($"Team Deathmatch preparing {(usec ? "USEC" : "BEAR")} request {ticket.Id}.");
        int planned = BurstSpawnService.SpawnFactionBot(role, ArenaPressure.DifficultyFor(true),
            bot => Adopt(ticket, bot),
            () => IsRequestCurrent(ticket),
            () => BeginNativeDispatch(ticket),
            dispatched =>
            {
                // Completion can race the watchdog tick. Never turn an overdue
                // unresolved preparation into an ordinary retry by removing it.
                if (CheckPreparationWatchdog(ticket)) return;
                _population.DispatchFinished(ticket, dispatched);
                if (!dispatched) DelayRetry(ticket);
            },
            () => { _population.Settle(ticket); DelayRetry(ticket); }, ticket.Id);
        if (planned == 0) { _population.Settle(ticket); DelayRetry(ticket); }
    }

    private bool IsRequestCurrent(TeamDeathmatchPopulation.Ticket ticket)
        => IsCurrentMatch() && !CheckPreparationWatchdog(ticket)
           && _population.IsCurrent(ticket, Time.realtimeSinceStartup);

    private bool BeginNativeDispatch(TeamDeathmatchPopulation.Ticket ticket)
    {
        if (!IsRequestCurrent(ticket)
            || !_population.TryBeginDispatch(ticket, CountBots(ticket.Usec), Time.realtimeSinceStartup)) return false;
        Plugin.ModLogger.LogDebug($"Team Deathmatch submitting {(ticket.Usec ? "USEC" : "BEAR")} request {ticket.Id}: "
            + FormattableString.Invariant($"prepared={ticket.SubmittedAt - ticket.CreatedAt:F1}s; native arrival budget=45s."));
        return true;
    }

    private bool CheckPreparationWatchdog(TeamDeathmatchPopulation.Ticket ticket = null)
    {
        if (!IsCurrentMatch()) return false;
        ticket ??= _population.ExpiredPreparation(Time.realtimeSinceStartup);
        if (!_population.PreparationExpired(ticket, Time.realtimeSinceStartup)) return false;
        Fail($"{(ticket.Usec ? "USEC" : "BEAR")} request {ticket.Id} profile/pool preparation exceeded the 300s watchdog; "
            + "unresolved native loading is not cancelled or retried");
        return true;
    }

    private void DelayRetry(TeamDeathmatchPopulation.Ticket ticket)
        => _retryAt[ticket.Usec ? 0 : 1] = Time.time + 5f;

    private bool ValidateActivations()
    {
        foreach (BotOwner bot in _bots)
        {
            if (ObserveNativeStop() || !IsCurrentRaid()) return false;
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
                if (_activationDeadline.Remove(bot))
                {
                    int previousFailures = _activationFailures[index];
                    _activationFailures[index] = 0;
                    if (previousFailures > 0)
                        Plugin.ModLogger.LogDebug($"Team Deathmatch: {(usec ? "USEC" : "BEAR")} activation recovered "
                            + $"after {previousFailures} consecutive failures; " + DescribeState());
                }
                continue;
            }
            if (bot.BotState == EBotState.PreActive
                && _activationDeadline.TryGetValue(bot, out float deadline) && Time.time < deadline) continue;
            // EFT can leave a healthy body in ActiveFail or wait forever for
            // its weapon manager. Arrival acknowledged the request, not combat
            // readiness: retire the exact failed body before freeing its slot.
            string wait = _activationDeadline.TryGetValue(bot, out float expires)
                ? FormattableString.Invariant($"{Math.Max(0f, Time.time - expires + TeamDeathmatchPopulation.RequestTimeout):F0}s")
                : "n/a (previously active)";
            if (!Retire(bot, $"failed or timed-out activation ({bot.BotState}; faction={(usec ? "USEC" : "BEAR")}, "
                + $"wait={wait}, failure={_activationFailures[index] + 1}/3)")) return false;
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
            if (ObserveNativeStop()) return;
            var player = bot?.GetPlayer;
            EPlayerSide expected = ticket.Usec ? EPlayerSide.Usec : EPlayerSide.Bear;
            string rejection = ArrivalRejection(ticket, bot, player, expected);
            if (rejection != null)
            {
                // Retire warns only for a living body; a dead or missing arrival is still named here.
                if (!Alive(bot))
                    Plugin.ModLogger.LogDebug("Team Deathmatch rejected a dead " + (ticket.Usec ? "USEC" : "BEAR")
                        + " arrival: " + rejection);
                Retire(bot, "arrival rejected: " + rejection);
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
            TrackSpawnShield(bot);
            _activationDeadline.Add(bot, Time.time + TeamDeathmatchPopulation.RequestTimeout);
            _population.Settle(ticket);
            // Load-bearing for the seats: this sync assigns the body's seat before any kill can
            // reach the scoreboard, so Resolve never invents a body row that a later roster
            // plan would reject as a changed identity.
            if (!TrySynchronizeScoreboardRoster(out var problem))
                Fail("scoreboard affiliation drift: " + problem);
            _nextTick = 0f;
            Plugin.ModLogger.LogDebug($"Team Deathmatch admitted {(ticket.Usec ? "USEC" : "BEAR")} "
                + $"request {ticket.Id}: {CountBots(ticket.Usec)}/{_population.BotTarget(ticket.Usec)} bots; "
                + FormattableString.Invariant($"state={bot.BotState}, prepared={ticket.SubmittedAt - ticket.CreatedAt:F1}s, native arrival={Math.Max(0f, Time.realtimeSinceStartup - ticket.SubmittedAt):F1}s."));
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

    /// <summary>
    /// The first admission gate a native arrival fails, in the order the gates are checked,
    /// or null when the body may be adopted. Registration with the combat relations stays
    /// the last gate and runs only when every other gate passed, as before. The gates are
    /// named (2026-11-08, adopted from the fork survey) because the former generic text left
    /// every such retirement in the log unexplained.
    /// </summary>
    private string ArrivalRejection(TeamDeathmatchPopulation.Ticket ticket, BotOwner bot, Player player, EPlayerSide expected)
    {
        if (!IsRequestCurrent(ticket)) return "stale request";
        if (!_population.CanAdmit(ticket, Time.realtimeSinceStartup)) return "request not admitting (preparing or expired)";
        if (!Alive(bot)) return "dead on arrival";
        EPlayerSide? faction = TeamDeathmatchRelations.Faction(player);
        if (faction != expected) return "faction " + (faction.HasValue ? faction.Value.ToString() : "unknown") + ", expected " + expected;
        if (string.IsNullOrWhiteSpace(bot.ProfileId)) return "empty profile id";
        if (_usecById.ContainsKey(bot.ProfileId)) return "duplicate profile";
        int occupied = CountBots(ticket.Usec), target = _population.BotTarget(ticket.Usec);
        if (occupied >= target) return "over capacity (" + occupied + "/" + target + ")";
        if (!ValidGroup(bot, expected)) return "group side or membership inconsistent";
        if (!TeamDeathmatchRelations.Register(player)) return "combat relations refused registration";
        return null;
    }

    private void ResetSeats()
    {
        _seats.Clear();
        _seatByProfile.Clear();
        TeamSeatNames.Reset();
    }

    /// <summary>
    /// The lowest seat of the faction not held by a living bot; a seat is named by its first
    /// occupant and every later body in it is shown under that name.
    /// </summary>
    private string AssignSeat(BotOwner bot, bool usec)
    {
        if (_seatByProfile.TryGetValue(bot.ProfileId, out string seat)) return seat;
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (BotOwner other in _bots)
            if (Alive(other) && _seatByProfile.TryGetValue(other.ProfileId, out string held)) taken.Add(held);
        string prefix = usec ? "usec-" : "bear-";
        int index = 0;
        while (taken.Contains(prefix + index)) index++;
        seat = prefix + index;
        _seatByProfile[bot.ProfileId] = seat;
        ArenaScoreboardActor snapshot = ArenaMatchService.SnapshotActor(bot.GetPlayer);
        if (_seats.TryGetValue(seat, out Seat record))
        {
            record.LastProfileId = bot.ProfileId;
            TeamSeatNames.Assign(bot.ProfileId, record.Name);
            Plugin.ModLogger.LogDebug("Team Deathmatch seat " + seat + " reoccupied; the replacement wears the seat's name.");
        }
        else
            _seats[seat] = new Seat { Name = snapshot?.DisplayName, Label = snapshot?.RoleLabel, LastProfileId = bot.ProfileId };
        return seat;
    }

    private int CountBots(bool usec)
        => _bots.Count(bot => Alive(bot) && _usecById.TryGetValue(bot.ProfileId, out bool side) && side == usec);

    private static bool Alive(BotOwner bot)
        => !ReferenceEquals(bot, null) && bot != null && bot.GetPlayer != null
           && bot.HealthController?.IsAlive == true;

    private bool IsCurrentRaid()
        => !_ended && !_nativeStopObserved && _game != null && _game.Status == GameStatus.Started && _world != null
           && Singleton<AbstractGame>.Instantiated && ReferenceEquals(Singleton<AbstractGame>.Instance, _game)
           && Singleton<GameWorld>.Instantiated && ReferenceEquals(Singleton<GameWorld>.Instance, _world)
           && ArenaSession.IsExactActiveRaid(_game, _world)
           && string.Equals(ArenaSession.ActiveMode, Id, StringComparison.OrdinalIgnoreCase);

    private bool ObserveNativeStop()
    {
        if (_nativeStopObserved) return true;
        // BaseLocalGame.Stop commits Stopping BEFORE disposing the bot owners,
        // then defers GameEnd until the black screen. Healthy NonActive/Disposed
        // wrappers in that gap are native teardown, not failed activations.
        // Use our captured game, never a replacement raid's status. Life-only
        // Give up is intercepted by Redeploy and leaves that game Started.
        if (_population == null || _game == null
            || (_game.Status != GameStatus.Stopping && _game.Status != GameStatus.Stopped)) return false;
        _nativeStopObserved = true;
        ReleaseDomination();
        ReleaseMonitors();
        ClearSpawnShields();
        ArenaTeamBoxRuntime.ReleaseGates(_game, _world, _spawnGateEpoch, "ArenaMode Team DM native shutdown");
        string state = DescribeState(); // capture pending work before Stop clears it
        _population.Stop();
        _pendingGiveUpLife = null;
        Plugin.ModLogger.LogDebug("Team Deathmatch: native raid shutdown; pending spawn work cancelled; " + state);
        return true;
    }

    private string DescribeState()
    {
        // Diagnostics must never turn a disposed Unity property or incomplete
        // startup into another mode failure. Only inspect the bounded live roster.
        try
        {
            string state = $"status={_game?.Status.ToString() ?? "missing"}, "
                + $"respawn={Respawn.ArenaRespawnDriver.IsRespawnInProgress}";
            if (_population == null) return state + "; roster=unavailable";
            return state + "; USEC " + DescribeFaction(true) + "; BEAR " + DescribeFaction(false);
        }
        catch (Exception ex) { return "roster snapshot unavailable (" + ex.GetType().Name + ")"; }
    }

    private string DescribeFaction(bool usec)
    {
        int occupied = 0, active = 0, preActive = 0;
        foreach (BotOwner bot in _bots)
        {
            if (!Alive(bot) || !_usecById.TryGetValue(bot.ProfileId, out bool side) || side != usec) continue;
            occupied++;
            if (bot.BotState == EBotState.Active) active++;
            else if (bot.BotState == EBotState.PreActive) preActive++;
        }
        return $"occupied={occupied}/{_population.BotTarget(usec)}, active={active}, preActive={preActive}, "
            + $"other={occupied - active - preActive}, " + _population.DescribePending(usec, Time.realtimeSinceStartup);
    }

    internal bool TrySynchronizeScoreboardRoster(out string problem)
    {
        problem = null;
        if (_population == null || _match == null || _local == null || !IsCurrentRaid() || HasResult) return true;
        var friendly = new List<ArenaScoreboardActor>();
        var opposing = new List<ArenaScoreboardActor>();
        var occupied = new Dictionary<string, BotOwner>(StringComparer.Ordinal);
        foreach (BotOwner bot in _bots)
        {
            if (!Alive(bot) || !_usecById.TryGetValue(bot.ProfileId, out bool usec)) continue;
            occupied[AssignSeat(bot, usec)] = bot;
        }
        // Every seat ever taken stays on the table: the living body under the seat's name,
        // or the seat's last body as OUT until its replacement arrives.
        foreach (var pair in _seats)
        {
            bool usec = pair.Key.StartsWith("usec-", StringComparison.Ordinal);
            Seat seat = pair.Value;
            ArenaScoreboardActor actor = occupied.TryGetValue(pair.Key, out BotOwner body)
                ? new ArenaScoreboardActor(body.ProfileId, seat.Name, seat.Label, true, pair.Key)
                : new ArenaScoreboardActor(seat.LastProfileId, seat.Name, seat.Label, false, pair.Key);
            (usec == _population.LocalUsec ? friendly : opposing).Add(actor);
        }
        return _match.Scoreboard.TrySynchronizeRoster(1, ArenaMatchService.SnapshotActor(_local),
            friendly, opposing, out problem);
    }

    internal void ClassifyKill(ArenaKill kill)
    {
        kill.KillerIsFriendly = Friendly(kill.KillerProfileId);
        kill.VictimIsFriendly = Friendly(kill.VictimProfileId);
        kill.ScoreEligible &= !_ended && !IsComplete && IsCurrentRaid() && MatchPhase == TdmMatchPhase.Live
            && kill.KillerIsFriendly.HasValue && kill.VictimIsFriendly.HasValue
            && kill.KillerIsFriendly != kill.VictimIsFriendly;
        kill.AwardEligible &= kill.ScoreEligible;
    }

    private bool? Friendly(string id)
        => _population != null && id != null && _usecById.TryGetValue(id, out bool usec)
            ? usec == _population.LocalUsec : null;

    public void OnKill(ArenaKill kill) => CheckMatchLimits();
    public void OnLocalDeath()
    {
        if (IsCurrentRaid() && MatchPhase == TdmMatchPhase.Countdown) TickMatchStart(false);
        _nextTick = 0f;
    }

    /// <summary>Reserve this exact living body for the synchronous Give up call.</summary>
    internal bool TryBeginLocalGiveUp(Player player)
    {
        if (!TryBeginScoredEvent() || _pendingGiveUpLife != null || ReferenceEquals(_creditedGiveUpLife, player)
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
        ArenaRewardCapture.LocalForfeit(player);
        _match.Scoreboard.ReportOpponentForfeitKill();
        CheckMatchLimits();
    }

    private bool IsCurrentLocalGiveUpLife(Player player)
        => IsCurrentMatch()
           && player != null && ReferenceEquals(player, _local)
           && ReferenceEquals(player, _world.MainPlayer)
           && !player.IsAI
           && player.ProfileId == _localId
           && ArenaSession.TeamDeathmatchBodySide(player, _game, _world)
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
        if (MatchPhase == TdmMatchPhase.Countdown) TickMatchStart(false);
        try { if (player != null) Areas.ArenaAreaRuntime.RequirePosition(player.Position); }
        catch (Exception ex)
        { Areas.ArenaAreaRuntime.Abort("replacement body left the verified area: " + ex.Message); return; }
        if (player == null || player.ProfileId != _localId
            || ArenaSession.TeamDeathmatchBodySide(player, _game, _world) != (_population.LocalUsec ? EPlayerSide.Usec : EPlayerSide.Bear)
            || TeamDeathmatchRelations.Faction(player) != (_population.LocalUsec ? EPlayerSide.Usec : EPlayerSide.Bear))
        { Fail("local PMC faction changed"); return; }
        _local = player;
        Areas.ArenaAreaRuntime.ReleasePlayerReservation(player);
        _pendingGiveUpLife = null;
        _creditedGiveUpLife = null;
        _localDeathCounted = false;
        TeamDeathmatchRelations.Register(player);
        _nextTick = 0f;
    }

    private void Fail(string reason)
    {
        if (_failure != null || ObserveNativeStop()) return;
        _failure = reason;
        ReleaseDomination();
        ReleaseMonitors();
        ArenaTeamBoxRuntime.ReleaseGates(_game, _world, _spawnGateEpoch, "ArenaMode Team DM failure");
        string state = DescribeState();
        _population?.Stop();
        Plugin.ModLogger.LogError("Team Deathmatch stopped: " + reason + "; " + state);
        if (!HasResult) _match?.ReportAnnouncerCue(ArenaAnnouncerCue.NoContest);
        ArenaToasts.Show("TEAM DM stopped: " + reason);
        if (IsDomination) ArenaMatchService.ScheduleFailedStartAbort("Domination stopped: " + reason);
    }

    private bool Retire(BotOwner bot, string reason)
    {
        try
        {
            if (ObserveNativeStop()) return false;
            if (!Alive(bot)) return true;
            using (ArenaAdministrativeDeathGate.Enter(bot.ProfileId))
                bot.GetPlayer.ActiveHealthController.Kill(EDamageType.Undefined);
            // Native death callbacks can synchronously begin leaving the raid.
            if (ObserveNativeStop()) return false;
            if (Alive(bot)) throw new InvalidOperationException("body stayed alive after administrative retirement");
            Plugin.ModLogger.LogWarning("Team Deathmatch retired " + reason + "; " + DescribeState());
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
        ReleaseDomination();
        ReleaseMonitors();
        ClearSpawnShields();
        ArenaTeamBoxRuntime.ReleaseGates(_game, _world, _spawnGateEpoch, "ArenaMode Team DM end");
        _ended = true;
        _pendingGiveUpLife = null;
        _creditedGiveUpLife = null;
        _population?.Stop();
        _bots.Clear();
        _activationDeadline.Clear();
        _usecById.Clear();
        ResetSeats();
        TeamDeathmatchRelations.Reset();
    }
}
