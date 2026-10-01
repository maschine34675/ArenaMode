using ArenaMode.Bots;
using ArenaMode.Combat;
using ArenaMode.Config;
using ArenaMode.Effects;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using UnityEngine;
using UnityEngine.EventSystems;

internal static partial class Program
{
    private static int _checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        _checks++;
    }

    private sealed class Raid
    {
        internal readonly GameWorld World = new();
        internal readonly LocalGame Game = new();
        internal readonly TeamDeathmatchMode Mode = new();
        internal readonly ArenaMatch Match;
        internal readonly bool Usec;
        internal Player Local => World.MainPlayer;
        internal int CapturedKills;
        internal readonly List<string> Callouts = new();
        internal Raid(bool usec = true, bool bounded = false, bool startLive = true,
            int killLimit = 0, int timeLimit = 0)
        {
            ArenaMode.Areas.ArenaAreaRuntime.Reset();
            ArenaMode.Areas.ArenaAreaRuntime.CurrentArea = bounded ? ArenaMode.Shared.ArenaAreaCatalog.FactorySouth : null;
            Usec = usec;
            Time.time = Time.realtimeSinceStartup = Time.unscaledTime = 0;
            TdmMatchExit.Reset();
            LiveKitRespawn.IsInitialSwapPending = false;
            ArenaGiveUp.Reset();
            ArenaAdministrativeDeathGate.Reset();
            ArenaUncreditedDeathPatch.Calls = 0;
            ArenaUncreditedDeathPatch.AllAfterDeathAndLease = true;
            ArenaTracerTeams.Reset();
            TeamDeathmatchRelations.Reset();
            ArenaRespawnDriver.IsRespawnInProgress = false;
            ArenaMode.Compat.PitFireTeamCompat.HasFrozenTeamRoster = false;
            Singleton<GameWorld>.Instance = World;
            Singleton<AbstractGame>.Instance = Game;
            Singleton<IBotGame>.Instance = Game;
            ArenaSession.IsActive = true;
            ArenaSession.ActiveMode = "teamdeathmatch";
            ArenaSession.OwnedGame = Game;
            ArenaSession.OwnedWorld = World;
            Settings.TeamDeathmatchSize.Value = 2;
            Settings.TeamDeathmatchKillLimit.Value = killLimit;
            Settings.TeamDeathmatchTimeLimitSeconds.Value = timeLimit;
            Input.Down = Input.Held = true;
            GUIUtility.keyboardControl = 0;
            EventSystem.current = null;
            Settings.GiveUpHotkey.Value.Modifiers = [];
            BurstSpawnService.Requests.Clear();
            BurstSpawnService.OnRequest = null;
            Model.Body("local", usec ? EPlayerSide.Usec : EPlayerSide.Bear);
            Match = new ArenaMatch(Mode, "local", "You", "PMC");
            ArenaMatchService.Current = Match;
            Match.OnKill += _ => CapturedKills++;
            Match.OnCallout += text => Callouts.Add(text);
            Mode.OnMatchStart(Match);
            WireDeath(Local);
            if (startLive) StartPlaying();
        }
        internal void Step(float at)
        {
            Time.time = Time.realtimeSinceStartup = Time.unscaledTime = at;
            Match.Tick(1f);
        }
        internal void StartPlaying()
        {
            BurstSpawnService.OnRequest = request =>
            {
                var body = Model.Body("bot-" + ++Model.NextId,
                    request.Role == WildSpawnType.pmcUSEC ? EPlayerSide.Usec : EPlayerSide.Bear, true);
                body.AIData.BotOwner.BotState = EBotState.Active;
                Check(request.Submit(), "live give-up setup submits an actual reservation");
                request.Observer(body.AIData.BotOwner);
                request.DispatchFinished(true);
            };
            for (int i = 0; !Mode.InitialRosterReady && !Mode.IsComplete
                && i < 2 * Mode.TeamSize + TeamDeathmatchMatchPolicy.CountdownSeconds + 3; i++) Step(Time.time + 1);
            BurstSpawnService.OnRequest = null;
            Check(Mode.InitialRosterReady, "give-up setup completed real roster and countdown");
        }
        internal void WireDeath(Player body)
        {
            body.HealthController.OnKilled = () =>
            {
                Check(ArenaAdministrativeDeathGate.IsActive(body.ProfileId), "native give-up remains administrative");
                // This models the native driver notification only. Actual match
                // death/awards and scoreboard code are linked, never substituted.
                Match.ReportLocalDeath();
                ArenaRespawnDriver.IsRespawnInProgress = true;
            };
        }
        internal void Respawn()
        {
            Model.Body("local", Usec ? EPlayerSide.Usec : EPlayerSide.Bear);
            Match.ReportLocalRespawn(Local);
            WireDeath(Local);
            ArenaRespawnDriver.IsRespawnInProgress = false;
            Time.time = Time.realtimeSinceStartup = Time.unscaledTime = Time.time + 2;
        }
        internal BotOwner AddBot(bool usec)
        {
            var existing = Game.BotsController.BotSpawner._bots.BotOwners.FirstOrDefault(bot =>
                bot.HealthController.IsAlive && bot.BotState == EBotState.Active
                && (bot.Profile.Info.Side == EPlayerSide.Usec) == usec);
            if (existing != null) return existing;
            // Drive actual mode reservation/adoption rather than inject its list.
            BotOwner adopted = null;
            BurstSpawnService.OnRequest = request =>
            {
                if ((request.Role == WildSpawnType.pmcUSEC) != usec) return;
                var body = Model.Body("bot-" + ++Model.NextId,
                    usec ? EPlayerSide.Usec : EPlayerSide.Bear, true);
                adopted = body.AIData.BotOwner;
                adopted.BotState = EBotState.Active;
                Check(request.Submit(), "native give-up fixture submits before physical arrival");
                request.Observer(adopted);
                request.DispatchFinished(true);
            };
            Time.time += 2;
            Mode.Tick(1);
            BurstSpawnService.OnRequest = null;
            Check(adopted != null, "actual mode admitted requested bot");
            return adopted;
        }
    }

    private static void SuccessfulAndPersistent()
    {
        foreach (bool usec in new[] { true, false })
        {
            var raid = new Raid(usec);
            for (int life = 1; life <= 3; life++)
            {
                var original = raid.Local;
                int version = raid.Match.Scoreboard.Version;
                ArenaGiveUp.Request();
                var enemies = raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Opponent);
                Check(!original.HealthController.IsAlive, "give-up killed current life");
                Check(ArenaUncreditedDeathPatch.Calls == life && ArenaUncreditedDeathPatch.AllAfterDeathAndLease,
                    "one death notice per life after native death and administrative lease");
                Check(enemies.Kills == life && enemies.Score == life * ScoreRules.PointsFor(ArenaRole.Pmc),
                    "give-up grants exactly one opposing PMC kill and base points");
                Check(raid.Match.Deaths == life && raid.Match.Kills == 0 && raid.Match.Score == 0,
                    "ordinary local death accounting remains single, no local kill or points");
                Check(raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Friendly).Deaths == life,
                    "team death count remains normal");
                Check(raid.Match.Scoreboard.ParticipantCount == 4
                    && raid.Match.Scoreboard.Leader(ArenaScoreboardSide.Opponent)?.Kills == 0,
                    "no fabricated opponent or personal killer");
                Check(raid.CapturedKills == 0 && raid.Callouts.Count == 0 && enemies.Headshots == 0,
                    "no synthetic kill event, headshot, spree or revenge callout");
                Check(raid.Match.Scoreboard.Version > version, "team panel invalidated");
                ArenaGiveUp.Request();
                raid.Mode.CompleteLocalGiveUp(original, true);
                ArenaGiveUp.Reset();
                ArenaRespawnDriver.IsRespawnInProgress = false;
                ArenaGiveUp.Request();
                Check(original.HealthController.KillCalls == 1 && raid.Match.Deaths == life
                    && raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills == life,
                    "hotkey repeats/direct duplicate completion/reset cannot credit old life twice");
                Check(!ArenaAdministrativeDeathGate.IsActive(original.ProfileId), "administrative scope released");
                raid.Respawn();
            }

            // Compaction archives old bot runs; team-only credits have no body
            // entry to be dropped and must remain across roster replacement.
            for (int i = 0; i < 100; i++)
                Check(raid.Match.Scoreboard.TrySynchronizeRoster(1,
                    new ArenaScoreboardActor("local", "You", "PMC", true), [],
                    [new ArenaScoreboardActor("archived-" + i, "Bot", "PMC", false)], out _), "roster cycle accepted");
            Check(raid.Match.Scoreboard.ArchivedParticipantCount > 0, "production history actually compacted");
            Check(raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills == 3
                && raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Score == 600,
                "forfeit team totals survived real compaction");
            var enemy = raid.AddBot(!usec);
            raid.Match.ReportKill(new ArenaKill
            {
                KillerProfileId = "local", VictimProfileId = enemy.ProfileId, VictimRole = ArenaRole.Pmc,
                KilledByLocal = true, AwardEligible = true, KillerBodyId = 1, VictimBodyId = 2, Time = Time.time
            });
            Check(raid.Callouts.SequenceEqual(new[] { "FIRST BLOOD" }), "give-up does not consume First Blood");
            Check(raid.Match.Kills == 1 && raid.Match.Streak == 1, "next real kill begins a fresh personal spree");
        }
    }

    private static void FailuresAndLifecycle()
    {
        foreach (bool throws in new[] { false, true })
        {
            var raid = new Raid();
            raid.Local.HealthController.SuppressKill = !throws;
            raid.Local.HealthController.ThrowOnKill = throws;
            ArenaGiveUp.Request();
            Check(raid.Local.HealthController.IsAlive && raid.Match.Deaths == 0
                && raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills == 0,
                "suppressed/throwing native kill cannot score");
            Check(ArenaUncreditedDeathPatch.Calls == 0, "failed native kill cannot display a death notice");
            ArenaGiveUp.Request();
            Check(raid.Local.HealthController.KillCalls == 1, "failed native kill still respects original cooldown");
            raid.Local.HealthController.SuppressKill = raid.Local.HealthController.ThrowOnKill = false;
            Time.time += 2;
            ArenaGiveUp.Request();
            Check(raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills == 1,
                "failed request lease released for later success");
        }
        {
            var raid = new Raid();
            raid.Local.HealthController.OnKilled += () => throw new InvalidOperationException("subscriber failed after native death");
            ArenaGiveUp.Request();
            Check(raid.Match.Deaths == 1 && raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills == 1,
                "already confirmed death counts despite later subscriber exception");
        }
        {
            var raid = new Raid();
            raid.Local.HealthController.OnKilled = () =>
            {
                ArenaGiveUp.Reset();
                raid.Local.HealthController.IsAlive = true;
                ArenaGiveUp.Request();
                raid.Local.HealthController.IsAlive = false;
                raid.Match.ReportLocalDeath();
            };
            ArenaGiveUp.Request();
            Check(raid.Local.HealthController.KillCalls == 1 && raid.Match.Deaths == 1
                && raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills == 1,
                "pending exact-life lease rejects synchronous reentrant give-up");
        }
        Action<Raid>[] invalidations =
        [
            raid => ArenaSession.IsActive = false,
            raid => ArenaSession.ActiveMode = "endless",
            raid => ArenaSession.OwnedWorld = new GameWorld(),
            raid => Singleton<GameWorld>.Instance = new GameWorld(),
            raid => Singleton<AbstractGame>.Instance = new LocalGame(),
            raid => ArenaMatchService.Current = new ArenaMatch(new TeamDeathmatchMode()),
            raid => raid.Mode.OnMatchEnd(),
            raid => Model.Body("local", EPlayerSide.Usec),
            raid => raid.Local.Profile.Info.Side = EPlayerSide.Bear,
            raid => raid.Local.ProfileId = "different-profile",
            raid => raid.Local.HealthController = new Health { Player = raid.Local, IsAlive = false }
        ];
        foreach (var invalidate in invalidations)
        {
            var raid = new Raid();
            var body = raid.Local;
            body.HealthController.OnKilled += () => invalidate(raid);
            ArenaGiveUp.Request();
            Check(raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills == 0,
                "stale match/world/mode/body/profile/health cannot receive give-up point");
            Check(!ArenaAdministrativeDeathGate.IsActive("local")
                && !ArenaAdministrativeDeathGate.IsActive(body.ProfileId), "invalidated request scope closed");
        }
        {
            var raid = new Raid();
            raid.Local.HealthController.IsAlive = false; // unrelated environmental death
            raid.Mode.CompleteLocalGiveUp(raid.Local, true);
            Check(raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills == 0,
                "unreserved environmental/administrative death never scores");
        }
    }

    private static void RequestAndTypingGates()
    {
        Action<Raid>[] reject =
        [
            raid => ArenaSession.IsActive = false,
            raid => ArenaRespawnDriver.IsRespawnInProgress = true,
            raid => Singleton<GameWorld>.Instance = null,
            raid => raid.Local.HealthController.IsAlive = false,
            raid => raid.Local.HealthController = null
        ];
        foreach (var invalidate in reject)
        {
            var raid = new Raid();
            var health = raid.Local.HealthController;
            invalidate(raid);
            ArenaGiveUp.Request();
            Check(health.KillCalls == 0 && raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills == 0,
                "ineligible give-up rejected before native kill");
        }
        Action<Raid>[] unavailableScore =
        [
            raid => raid.Mode.OnMatchEnd(),
            raid => ArenaSession.ActiveMode = "tactical",
            raid => raid.Local.Profile.Info.Side = EPlayerSide.Bear,
            raid => { Model.Body("local", EPlayerSide.Usec); raid.WireDeath(raid.Local); },
            raid => { raid.Local.Profile.Info.Side = EPlayerSide.Savage; raid.Step(Time.time + 1); }
        ];
        for (int index = 0; index < unavailableScore.Length; index++)
        {
            var raid = new Raid(); unavailableScore[index](raid); var health = raid.Local.HealthController;
            ArenaGiveUp.Request();
            // A still-current live match keeps its native death bookkeeping;
            // ended, foreign-mode and failed lifecycles reject further scores.
            int expectedDeaths = index == 2 || index == 3 ? 1 : 0;
            Check(health.KillCalls == 1 && !health.IsAlive && raid.Match.Deaths == expectedDeaths
                && raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills == 0,
                $"stopped/failed/unbound team scoring never blocks the existing give-up escape action (case={index}, calls={health.KillCalls}, alive={health.IsAlive}, deaths={raid.Match.Deaths})");
        }
        {
            var raid = new Raid();
            ArenaGiveUp.Request();
            raid.Local.HealthController.IsAlive = true;
            raid.Match.ReportLocalRespawn(raid.Local);
            ArenaRespawnDriver.IsRespawnInProgress = false;
            Time.time += 2;
            ArenaGiveUp.Request();
            Check(raid.Match.Deaths == 2 && raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills == 2,
                "explicit completed same-body respawn starts a new eligible life");
        }
        Action<Raid>[] typing =
        [
            raid => GUIUtility.keyboardControl = 5,
            raid => EventSystem.current = new EventSystem { currentSelectedGameObject = new object() },
            raid => Input.Down = false,
            raid => { Settings.GiveUpHotkey.Value.Modifiers = [KeyCode.LeftShift]; Input.Held = false; }
        ];
        foreach (var gate in typing)
        {
            var raid = new Raid(); gate(raid); ArenaGiveUp.Tick();
            Check(raid.Local.HealthController.KillCalls == 0, "typing/hotkey guard retained");
        }
        {
            var raid = new Raid(); ArenaGiveUp.Tick();
            Check(raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills == 1, "hotkey reaches same scoring path");
        }
        foreach (bool intermission in new[] { false, true })
        {
            var raid = new Raid();
            var ordinary = new ArenaMatch(new RoundsMode { InIntermission = intermission });
            ArenaMatchService.Current = ordinary;
            raid.Local.HealthController.OnKilled = ordinary.ReportLocalDeath;
            ArenaGiveUp.Request();
            Check(ordinary.Deaths == (intermission ? 0 : 1)
                && ordinary.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills == 0,
                "other mode/intermission death behavior unchanged, no team forfeit credit");
        }
    }

    private static void ExactVisibleBodies()
    {
        foreach (bool usec in new[] { true, false })
        {
            var raid = new Raid(usec, startLive: false);
            Check(raid.Mode.TryClassifyCurrentBody(raid.Local, out bool localFriendly) && localFriendly, "current local body friendly");
            var friend = raid.AddBot(usec);
            // The prior tick already reserved an enemy request; deliver that
            // reservation explicitly to keep actual ownership admission intact.
            var request = BurstSpawnService.Requests.Last(r => (r.Role == WildSpawnType.pmcUSEC) != usec);
            var foeBody = Model.Body("foe", usec ? EPlayerSide.Bear : EPlayerSide.Usec, true);
            var foe = foeBody.AIData.BotOwner;
            foe.BotState = EBotState.Active;
            Check(request.Submit(), "opponent fixture submits before physical arrival");
            request.Observer(foe); request.DispatchFinished(true);
            Check(raid.Mode.TryClassifyCurrentBody(friend.GetPlayer, out bool friendly) && friendly, "admitted friendly body classified");
            Check(raid.Mode.TryClassifyCurrentBody(foe.GetPlayer, out friendly) && !friendly, "admitted opposing body classified");
            string originalId = friend.GetPlayer.ProfileId;
            friend.GetPlayer.ProfileId = null;
            Check(!raid.Mode.TryClassifyCurrentBody(friend.GetPlayer, out _), "unreadable profile ID fails closed without throwing");
            friend.GetPlayer.ProfileId = originalId;
            foreach (var state in new[] { EBotState.PreActive, EBotState.ActiveFail, EBotState.Disposed, EBotState.NonActive })
            { friend.BotState = state; Check(!raid.Mode.TryClassifyCurrentBody(friend.GetPlayer, out _), "inactive wrapper not eligible"); }
            friend.BotState = EBotState.Active;
            var stranger = Model.Body("unowned", usec ? EPlayerSide.Usec : EPlayerSide.Bear, true);
            stranger.AIData.BotOwner.BotState = EBotState.Active;
            Check(!raid.Mode.TryClassifyCurrentBody(stranger, out _), "same faction unknown owner not auto-admitted");
            var old = friend.GetPlayer;
            var alias = Model.Body(old.ProfileId, old.Profile.Info.Side, true);
            alias.AIData.BotOwner.BotState = EBotState.Active;
            Check(!raid.Mode.TryClassifyCurrentBody(old, out _) && !raid.Mode.TryClassifyCurrentBody(alias, out _),
                "historical profile/new body alias cannot borrow visual affiliation");
            foe.GetPlayer.Profile.Info.Settings.Role = WildSpawnType.assault;
            Check(!raid.Mode.TryClassifyCurrentBody(foe.GetPlayer, out _), "role drift rejected");
            raid.Local.HealthController.IsAlive = false;
            Check(!raid.Mode.TryClassifyCurrentBody(raid.Local, out _), "dead local body not eligible");
            raid.Respawn();
            Check(raid.Mode.TryClassifyCurrentBody(raid.Local, out friendly) && friendly, "fully rebound replacement local body eligible");
            raid.Mode.OnMatchEnd();
            Check(!raid.Mode.TryClassifyCurrentBody(raid.Local, out _), "ended match releases visual affiliation");
            ArenaMatchService.Current = new ArenaMatch(new TeamDeathmatchMode());
            Check(!raid.Mode.TryClassifyCurrentBody(raid.Local, out _), "old match no longer owns visuals");
        }
    }

    private static void Main()
    {
        TeamDeathmatchRelations.Enable();
        SuccessfulAndPersistent();
        FailuresAndLifecycle();
        RequestAndTypingGates();
        ExactVisibleBodies();
        BoundedGiveUpContract();
        MatchStagingAccounting();
        CountdownDeathAndHandoverCallbacks();
        FinalLocalDeathOrdering();
        GiveUpCanFinishMatch();
        DeadlineBeforeCombatAndForfeit();
        Console.WriteLine($"PASS: {_checks} production-linked Give up, scoreboard, awards and exact-body checks.");
        Console.WriteLine("ARENA_TEAM_GIVE_UP_CHECKS_PASSED");
    }
}
