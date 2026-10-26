using ArenaMode.Bots;
using ArenaMode.Combat;
using ArenaMode.Config;
using ArenaMode.Effects;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using UnityEngine;

internal static partial class Program
{
    private static int _checks;
    private static void Check(bool result, string description)
    {
        if (!result) throw new InvalidOperationException(description);
        _checks++;
    }

    private sealed class Raid
    {
        internal readonly LocalGame Game = new();
        internal readonly GameWorld World = new();
        internal readonly TeamDeathmatchMode Mode;
        internal readonly ArenaMatch Match;
        internal Player Local => World.MainPlayer;
        internal List<BotOwner> Bots => Game.BotsController.BotSpawner._bots.BotOwners;
        internal Raid(bool usec = true, int size = 5, bool active = true,
            string mode = "teamdeathmatch", bool pft = false, EPlayerSide? localSide = null,
            bool bounded = false, bool areaPending = false, int? areaSize = null,
            int killLimit = 0, int timeLimit = 0, int? participants = null, float protection = 0f,
            string location = "factory4_day", EPlayerSide? selectedTeam = null, bool domination = false)
        {
            Mode = new TeamDeathmatchMode(domination);
            if (domination) mode = "domination";
            World.LocationId = location;
            ArenaMode.Areas.ArenaAreaRuntime.Reset();
            ArenaTeamBoxRuntime.Reset();
            ArenaMode.Areas.ArenaAreaRuntime.CurrentArea = bounded ? ArenaMode.Shared.ArenaAreaCatalog.FactorySouth : null;
            if (areaSize.HasValue)
            {
                ArenaMode.Areas.ArenaAreaRuntime.CurrentArea = ArenaMode.Shared.ArenaAreaCatalog.CustomsFactoryCenterTeams;
                ArenaMode.Areas.ArenaAreaRuntime.FrozenTeamSize = areaSize;
            }
            ArenaMode.Areas.ArenaAreaRuntime.IsPending = bounded && areaPending;
            Time.time = Time.realtimeSinceStartup = Time.unscaledTime = 0;
            TdmMatchExit.Reset();
            ArenaMode.Respawn.LiveKitRespawn.IsInitialSwapPending = false;
            Model.Events.Clear();
            BurstSpawnService.Requests.Clear();
            BurstSpawnService.OnRequest = null;
            ArenaMode.Plugin.ModLogger.Lines.Clear();
            Singleton<AbstractGame>.Instance = Game;
            Singleton<IBotGame>.Instance = Game;
            Singleton<GameWorld>.Instance = World;
            ArenaSession.IsActive = active;
            ArenaSession.ActiveMode = mode;
            ArenaSession.SelectedTeam = selectedTeam;
            ArenaSession.NativeTeam = localSide ?? (usec ? EPlayerSide.Usec : EPlayerSide.Bear);
            ArenaSession.Participants = participants;
            ArenaSession.OwnedGame = Game;
            ArenaSession.OwnedWorld = World;
            ArenaMode.Compat.PitFireTeamCompat.HasFrozenTeamRoster = pft;
            ArenaMode.Respawn.ArenaRespawnDriver.IsRespawnInProgress = false;
            Settings.TeamDeathmatchSize.Value = size;
            Settings.TeamDeathmatchKillLimit.Value = killLimit;
            Settings.TeamDeathmatchTimeLimitSeconds.Value = timeLimit;
            Settings.SpawnProtectionSeconds.Value = protection;
            Settings.NoCorpseLooting.Value = true;
            TeamDeathmatchRelations.Reset();
            ArenaTracerTeams.Reset();
            ArenaAdministrativeDeathGate.Reset();
            Model.Body("local", localSide ?? (usec ? EPlayerSide.Usec : EPlayerSide.Bear));
            Match = new ArenaMatch { Scoreboard = new ArenaScoreboard("local", "You", "PMC") };
            Match.Mode = Mode;
            ArenaMatchService.Current = Match;
            Mode.OnMatchStart(Match);
        }
        internal void Step(float at) { Time.time = Time.realtimeSinceStartup = Time.unscaledTime = at; Mode.Tick(1f); }
        internal int Alive(EPlayerSide side) => Bots.Count(bot => bot.HealthController.IsAlive && bot.Profile.Info.Side == side);
        internal void Fill()
        {
            BurstSpawnService.OnRequest = request =>
            {
                Deliver(request, EBotState.Active);
                // Real EFT may invoke observer before dispatch returns.
                request.DispatchFinished(true);
            };
            for (int tick = 0; tick < Mode.TeamSize + 2; tick++) Step(Time.time + 1);
            // Do not bypass the phase: keep ticking actual readiness and the
            // production countdown until the public ready contract releases.
            for (int tick = 0; !Mode.InitialRosterReady && !Mode.IsComplete
                && tick <= TeamDeathmatchMatchPolicy.CountdownSeconds + 2; tick++) Step(Time.time + 1);
        }
    }

    private static BotOwner Deliver(SpawnRequest request, EBotState state = EBotState.PreActive,
        string id = null, EPlayerSide? actualSide = null, Action<BotOwner> change = null, bool submit = true)
    {
        if (submit && !request.Submitted) request.Submit();
        EPlayerSide side = actualSide ?? (request.Role == WildSpawnType.pmcUSEC ? EPlayerSide.Usec : EPlayerSide.Bear);
        var player = Model.Body(id ?? "bot-" + ++Model.NextId, side, true, request.World, request.Game);
        var owner = player.AIData.BotOwner;
        owner.BotState = state;
        change?.Invoke(owner);
        request.Delivered = true;
        request.Observer(owner);
        return owner;
    }

    private static SpawnRequest Request(EPlayerSide side)
        => BurstSpawnService.Requests.Last(request => !request.Delivered
            && (request.Role == WildSpawnType.pmcUSEC) == (side == EPlayerSide.Usec));

    private static void InitializationAndReady()
    {
        foreach (int total in new[] { 4, 8, 16, 24 })
        {
            var negotiated = new Raid(size: 5, participants: total);
            Settings.TeamDeathmatchSize.Value = 12;
            negotiated.Fill();
            Check(negotiated.Mode.TeamSize == total / 2 && negotiated.Bots.Count == total - 1,
                "frozen full-map total overrides legacy team setting, includes human, stays equal after F12 edit");
        }
        var boundedNegotiated = new Raid(areaSize: 3, participants: 24);
        Check(boundedNegotiated.Mode.TeamSize == 3, "bounded-area reservation wins over unrelated full-map value");
        foreach (bool usec in new[] { true, false })
        foreach (int size in new[] { 2, 5, 12 })
        {
            var raid = new Raid(usec, size);
            Check(!raid.Mode.IsComplete && raid.Mode.ControlsOwnSpawns && !raid.Mode.HoldRespawn
                && raid.Mode.DeathReplaySeconds == 0, "TDM starts as independent continuous instant-respawn mode");
            Check(!raid.Mode.InitialRosterReady, "optional warmup remains blocked before the complete initial roster");
            Check(raid.Game.BotsController.BotSpawner.MaxBots == 2 * size - 1,
                "native cap reserves the human slot");
            raid.Fill();
            Check(raid.Alive(usec ? EPlayerSide.Usec : EPlayerSide.Bear) == size - 1,
                "human capacity: local faction has exactly N minus one bots");
            Check(raid.Alive(usec ? EPlayerSide.Bear : EPlayerSide.Usec) == size,
                "opposing faction has exactly N bots");
            Check(raid.Match.Cues.Count == 1 && raid.Match.Cues[0] == ArenaAnnouncerCue.RoundStart,
                "synchronous arrivals and later ticks announce ready exactly once");
            Check(raid.Mode.InitialRosterReady, "optional warmup is released only after the initial active roster");
            Check(raid.Match.Scoreboard.Count(ArenaScoreboardSide.Friendly, true) == size
                && raid.Match.Scoreboard.Count(ArenaScoreboardSide.Opponent, true) == size,
                "real mode populates real scoreboard with both complete factions");
            int before = BurstSpawnService.Requests.Count;
            raid.Step(Time.time + 1);
            Check(BurstSpawnService.Requests.Count == before, "full factions never request extra bots");
        }
        foreach (var setup in new (bool Active, string Mode, bool Pft, EPlayerSide Side)[]
        {
            (false, "teamdeathmatch", false, EPlayerSide.Usec),
            (true, "endless", false, EPlayerSide.Usec),
            (true, "teamdeathmatch", true, EPlayerSide.Usec),
            (true, "teamdeathmatch", false, EPlayerSide.Savage)
        })
        {
            var raid = new Raid(active: setup.Active, mode: setup.Mode, pft: setup.Pft, localSide: setup.Side);
            raid.Step(1);
            Check(raid.Mode.IsComplete && BurstSpawnService.Requests.Count == 0,
                "normal, foreign mode, selected PFT and non-PMC starts cannot spawn TDM actors");
        }
    }

    private static void LoadingAndPreActiveOccupancy()
    {
        var raid = new Raid(size: 2);
        raid.Step(0);
        Check(BurstSpawnService.Requests.Count == 2, "first tick starts one request per faction");
        var usecRequest = Request(EPlayerSide.Usec);
        var bearRequest = Request(EPlayerSide.Bear);
        raid.Step(50);
        Check(BurstSpawnService.Requests.Count == 2, "expired unresolved profile loads remain single-flight");
        usecRequest.DispatchFinished(false);
        bearRequest.DispatchFinished(false);
        raid.Step(51);
        Check(BurstSpawnService.Requests.Count == 2, "confirmed failures wait for retry cooldown");
        raid.Step(55);
        var usec = Deliver(Request(EPlayerSide.Usec));
        var firstBear = Deliver(Request(EPlayerSide.Bear));
        Check(usec.HealthController.IsAlive && firstBear.HealthController.IsAlive,
            "PreActive physical arrivals are admitted before activation");
        int before = BurstSpawnService.Requests.Count;
        raid.Step(56);
        Check(BurstSpawnService.Requests.Count == before + 1
            && BurstSpawnService.Requests[^1].Role == WildSpawnType.pmcBEAR,
            "PreActive immediately occupies the only friendly bot slot");
        var secondBear = Deliver(Request(EPlayerSide.Bear));
        raid.Step(57);
        Check(raid.Match.Cues.Count == 0, "physical fullness alone cannot announce combat readiness");
        usec.BotState = firstBear.BotState = secondBear.BotState = EBotState.Active;
        raid.Step(58);
        Check(raid.Match.Cues.Count == 0 && !raid.Mode.InitialRosterReady,
            "full active roster starts the real countdown rather than instant combat");
        raid.Step(58 + TeamDeathmatchMatchPolicy.CountdownSeconds);
        Check(raid.Match.Cues.Count == 1, "ready waits until every admitted body reaches Active");
    }

    private static void PreparationLifecycle()
    {
        var cold = new Raid(size: 2);
        cold.Step(0);
        var request = Request(EPlayerSide.Usec);
        cold.Step(154);
        Check(request.Current() && BurstSpawnService.Requests.Count == 2 && !cold.Mode.IsComplete,
            "154-second cold preparation remains owned without a replacement load");
        Check(request.Submit(), "completed cold preparation may submit once after fresh validation");
        Check(!request.Submit(), "runtime submission cannot reset its native clock");
        Time.realtimeSinceStartup = 198.99f;
        Check(request.Current(), "cold preparation does not consume the separate native arrival budget");
        Time.realtimeSinceStartup = 199f;
        Check(!request.Current(), "native request expires exactly 45 seconds after slow preparation");
        var late = Deliver(request);
        Check(!late.HealthController.IsAlive, "slow-prepared but late native body is still retired");

        var premature = new Raid(size: 2);
        premature.Step(0);
        var unsolicited = Deliver(Request(EPlayerSide.Usec), submit: false);
        Check(!unsolicited.HealthController.IsAlive && premature.Match.Cues.Count == 0,
            "an observer cannot admit a body while its ticket is still preparing");

        foreach (int completionKind in new[] { 0, 1, 2 })
        {
            var hung = new Raid();
            hung.Step(0);
            var unresolved = Request(EPlayerSide.Usec);
            // The simulation clock does not move. Exercise the tick, predicate
            // and terminal-completion races independently at the hard boundary.
            Time.realtimeSinceStartup = 300f;
            if (completionKind == 0) hung.Mode.Tick(0f);
            else if (completionKind == 1) unresolved.Current();
            else unresolved.DispatchFinished(false);
            Check(hung.Mode.IsComplete && !unresolved.Current() && !unresolved.Submit(),
                "preparation watchdog terminally closes ownership even between simulation ticks");
            int before = BurstSpawnService.Requests.Count;
            unresolved.DispatchFinished(false);
            hung.Step(999f);
            Check(BurstSpawnService.Requests.Count == before,
                "an unresolved overdue preparation never frees capacity for a retry");
            Check(ArenaMode.Plugin.ModLogger.Lines.Count(line => line.Contains("Team Deathmatch stopped:")) == 1,
                "preparation watchdog failure is reported once");
        }

        var ending = new Raid(size: 2);
        ending.Fill();
        Check(ending.Mode.InitialRosterReady, "current match publishes initial readiness after full activation");
        var replacement = new Raid();
        Check(!ending.Mode.InitialRosterReady, "old match readiness cannot release another raid's optional warmup");
        replacement.Fill();
        replacement.Mode.OnMatchEnd();
        Check(!replacement.Mode.InitialRosterReady, "ended match readiness cannot release optional warmup");

        var stopped = new Raid(size: 2);
        stopped.Fill();
        stopped.Game.Status = GameStatus.Stopping;
        Check(!stopped.Mode.InitialRosterReady, "native stop invalidates initial readiness before the next tick");
        var staleWorld = new Raid(size: 2);
        staleWorld.Fill();
        Singleton<GameWorld>.Instance = new GameWorld();
        Check(!staleWorld.Mode.InitialRosterReady, "foreign world cannot inherit initial roster readiness");
        var staleMatch = new Raid();
        staleMatch.Step(0);
        var orphan = Request(EPlayerSide.Usec);
        ArenaMatchService.Current = new ArenaMatch { Mode = new TeamDeathmatchMode() };
        Check(!orphan.Current() && !orphan.Submit(),
            "replaced current match cannot submit its old preparation into the same raid");
        Time.realtimeSinceStartup = 300f;
        orphan.DispatchFinished(false);
        Check(!staleMatch.Mode.IsComplete,
            "superseded match completion cannot publish a watchdog failure into its replacement");
    }

    private static void HumanDeathRespawnAndAwareness()
    {
        foreach (bool usec in new[] { true, false })
        {
            var raid = new Raid(usec, 3);
            raid.Fill();
            var oldLocal = raid.Local;
            oldLocal.HealthController.IsAlive = false;
            raid.World.Players.Remove(oldLocal.ProfileId);
            raid.Mode.OnLocalDeath();
            int before = BurstSpawnService.Requests.Count;
            Model.Events.Clear();
            raid.Step(Time.time + 1);
            Check(BurstSpawnService.Requests.Count == before, "human death never frees an AI team slot");
            Check(Model.Events.Count(item => item.StartsWith("awareness:")) == raid.Bots.Count,
                "both factions receive opposing bot knowledge while human is dead");
            var replacement = Model.Body("local", oldLocal.Profile.Info.Side);
            raid.Mode.OnLocalRespawn(replacement);
            raid.Step(Time.time + 1);
            Check(!raid.Mode.IsComplete && raid.Match.Scoreboard.Local.IsActive,
                "same-profile replacement rejoins active scoreboard and mode");
            foreach (var friend in raid.Bots.Where(bot => bot.Profile.Info.Side == replacement.Profile.Info.Side))
                Check(friend.BotsGroup.Allies.Contains(replacement) && !friend.BotsGroup.Allies.Contains(oldLocal),
                    "friendly groups replace the exact old local body");
            foreach (var enemy in raid.Bots.Where(bot => bot.Profile.Info.Side != replacement.Profile.Info.Side))
                Check(!enemy.BotsGroup.Enemies.ContainsKey(oldLocal), "old local enemy keys are removed before new awareness");
            Check(!TeamDeathmatchRelations.ShouldBlockFriendlyDamage(replacement, oldLocal),
                "old self-thrown projectile keeps self-damage semantics across local respawn");
        }
        var changed = new Raid();
        changed.Local.Profile.Info.Side = EPlayerSide.Bear;
        changed.Step(1);
        Check(changed.Mode.IsComplete && BurstSpawnService.Requests.Count == 0,
            "same-body local faction mutation stops before spawning");

        var rebuilding = new Raid();
        rebuilding.Fill();
        rebuilding.Local.HealthController.IsAlive = false;
        ArenaMode.Respawn.ArenaRespawnDriver.IsRespawnInProgress = true;
        var partial = Model.Body("local", EPlayerSide.Usec);
        Health finalHealth = partial.HealthController;
        partial.HealthController = null;
        rebuilding.Step(Time.time + 1);
        Check(!rebuilding.Mode.IsComplete,
            "intermediate local body published during respawn must not be adopted before readiness");
        partial.HealthController = finalHealth;
        ArenaMode.Respawn.ArenaRespawnDriver.IsRespawnInProgress = false;
        rebuilding.Mode.OnLocalRespawn(partial);
        rebuilding.Step(Time.time + 1);
        Check(!rebuilding.Mode.IsComplete && rebuilding.Match.Scoreboard.Local.IsActive,
            "completed local respawn rejoins after the intermediate phase");
    }

    private static void RejectionsAndRetirement()
    {
        foreach (string failure in new[] { "faction", "role", "foreign-group", "mixed-group" })
        {
            var raid = new Raid();
            raid.Step(0);
            var request = Request(EPlayerSide.Usec);
            var bot = Deliver(request, change: owner =>
            {
                if (failure == "faction") owner.GetPlayer.Profile.Info.Side = EPlayerSide.Bear;
                if (failure == "role") owner.GetPlayer.Profile.Info.Settings.Role = WildSpawnType.assault;
                if (failure == "foreign-group") owner.BotsGroup.BotGame = new LocalGame();
                if (failure == "mixed-group")
                    owner.BotsGroup.Members.Add(Model.Body("mixed", EPlayerSide.Bear, true).AIData.BotOwner);
            });
            Check(!bot.HealthController.IsAlive && bot.HealthController.KillCalls == 1,
                "invalid " + failure + " arrival is retired before adoption");
            Check(raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Friendly).Kills == 0
                && !ArenaAdministrativeDeathGate.IsActive(bot.ProfileId),
                "administrative retirement earns no combat points and releases its scope");
        }
        foreach (bool throwOnKill in new[] { false, true })
        {
            var raid = new Raid();
            var unowned = Model.Body("unowned", EPlayerSide.Usec, true).AIData.BotOwner;
            unowned.GetPlayer.HealthController.ThrowOnKill = throwOnKill;
            unowned.GetPlayer.HealthController.SuppressKill = !throwOnKill;
            raid.Step(0);
            Check(raid.Mode.IsComplete && BurstSpawnService.Requests.Count == 0,
                "retirement failure stops refill even when native Kill silently does nothing");
            Check(!ArenaAdministrativeDeathGate.IsActive(unowned.ProfileId), "failed retirement releases administrative scope");
        }
        var successful = new Raid();
        var outsider = Model.Body("outsider", EPlayerSide.Bear, true).AIData.BotOwner;
        successful.Step(0);
        Check(!outsider.HealthController.IsAlive && !successful.Mode.IsComplete,
            "unexpected living native owners are removed without stopping a healthy match");
        Check(Model.Events.FindIndex(value => value == "kill:outsider")
            < Model.Events.FindIndex(value => value.StartsWith("request:")),
            "unexpected bodies retire before any refill request");
        var unreadable = new Raid();
        unreadable.Game.BotsController.BotSpawner._bots.BotOwners = null;
        unreadable.Step(0);
        Check(BurstSpawnService.Requests.Count == 0, "unreadable native population never means empty");

        var preflight = new Raid();
        BurstSpawnService.OnRequest = request =>
        {
            request.RejectedBeforePlan = true;
            request.DispatchFinished(false);
        };
        preflight.Step(0);
        preflight.Step(4);
        Check(BurstSpawnService.Requests.Count == 2, "synchronous preflight refusal cannot spin on every frame");
        preflight.Step(5);
        Check(BurstSpawnService.Requests.Count == 4, "confirmed preflight refusal becomes retryable after bounded delay");
    }

    private static void ActivationLifetime()
    {
        foreach (var state in new[] { EBotState.PreActive, EBotState.ActiveFail, EBotState.Disposed, EBotState.NonActive })
        {
            var raid = new Raid(size: 2);
            raid.Step(0);
            var bot = Deliver(Request(EPlayerSide.Usec), state);
            raid.Step(state == EBotState.PreActive ? 44 : 1);
            Check(bot.HealthController.IsAlive == (state == EBotState.PreActive),
                "only bounded PreActive is allowed to retain a healthy unready body");
            if (state == EBotState.PreActive) raid.Step(45);
            Check(!bot.HealthController.IsAlive && !raid.Mode.IsComplete,
                "failed or timed-out activation retires its exact body");
            int before = BurstSpawnService.Requests.Count;
            raid.Step(Time.time + 4);
            Check(BurstSpawnService.Requests.Count == before, "activation retry respects cooldown");
            raid.Step(Time.time + 1);
            Check(BurstSpawnService.Requests.Count == before + 1
                && BurstSpawnService.Requests[^1].Role == WildSpawnType.pmcUSEC,
                "confirmed retired activation frees only its faction slot");
        }
        var bounded = new Raid(size: 2);
        bounded.Step(0);
        for (int failure = 0; failure < 3; failure++)
        {
            Deliver(Request(EPlayerSide.Usec), EBotState.ActiveFail);
            bounded.Step(Time.time + 1);
            if (failure != 2) bounded.Step(Time.time + 5);
        }
        int stopped = BurstSpawnService.Requests.Count;
        bounded.Step(Time.time + 100);
        Check(bounded.Mode.IsComplete && BurstSpawnService.Requests.Count == stopped,
            "three consecutive activation failures stop further native growth");
    }

    private static void ExactRaidAndLateCallbacks()
    {
        var oldRaid = new Raid();
        oldRaid.Step(0);
        var delayed = Request(EPlayerSide.Usec);
        var nextRaid = new Raid();
        int before = BurstSpawnService.Requests.Count;
        oldRaid.Step(10);
        Check(BurstSpawnService.Requests.Count == before, "old mode cannot tick into new exact game/world");
        var oldBody = Deliver(delayed);
        Check(!oldBody.HealthController.IsAlive && nextRaid.World.Players.Count == 1,
            "late old-world observer cannot adopt a bot into the next raid");
        var raid = new Raid();
        raid.Step(0);
        var request = Request(EPlayerSide.Usec);
        request.DispatchFinished(true);
        raid.Step(45);
        var late = Deliver(request);
        Check(!late.HealthController.IsAlive, "expired dispatched observer is retired despite a still-active raid");
        var pending = Request(EPlayerSide.Bear);
        raid.Mode.OnMatchEnd();
        int count = BurstSpawnService.Requests.Count;
        Deliver(pending);
        raid.Step(100);
        Check(BurstSpawnService.Requests.Count == count, "match end invalidates detached observers and refill");
    }

    private static void NativeShutdownWindow()
    {
        foreach (var status in new[] { GameStatus.Stopping, GameStatus.Stopped })
        foreach (var state in new[] { EBotState.NonActive, EBotState.Disposed })
        {
            var raid = new Raid();
            raid.Fill();
            foreach (var bot in raid.Bots.Where(bot => bot.Profile.Info.Side == EPlayerSide.Bear))
                bot.BotState = state;
            int before = BurstSpawnService.Requests.Count;
            raid.Game.Status = status;
            raid.Step(Time.time + 1);
            raid.Step(Time.time + 100);
            Check(!raid.Mode.IsComplete && raid.Bots.All(bot => bot.HealthController.KillCalls == 0),
                "native teardown wrappers must not count as activation failures or be killed by TDM");
            Check(BurstSpawnService.Requests.Count == before
                && !Model.Events.Any(item => item.StartsWith("toast:")),
                "native shutdown must not refill or show an error toast");
            Check(ArenaMode.Plugin.ModLogger.Lines.Count(line => line.Contains("native raid shutdown")) == 1
                && !ArenaMode.Plugin.ModLogger.Lines.Any(line => line.Contains("Team Deathmatch retired")
                    || line.Contains("Team Deathmatch stopped:")),
                "native shutdown is recorded once without false retirement or failure messages");
            var kill = Kill("local", raid.Bots.First().ProfileId, true);
            raid.Mode.ClassifyKill(kill);
            Check(!kill.ScoreEligible && !kill.AwardEligible, "shutdown rejects delayed combat scoring");
        }

        // No Tick between Stop and observer: a dispatched callback can be first
        // to see the committed stop while ArenaSession still owns the old raid.
        foreach (var status in new[] { GameStatus.Stopping, GameStatus.Stopped })
        {
            var raid = new Raid();
            raid.Step(0);
            var request = Request(EPlayerSide.Usec);
            int rosterBefore = raid.Match.Scoreboard.Count(ArenaScoreboardSide.Friendly, true);
            raid.Game.Status = status;
            Check(!request.Current(), "native stop immediately invalidates detached request predicates");
            var late = Deliver(request, EBotState.NonActive);
            request.DispatchFinished(true);
            raid.Step(1);
            Check(late.HealthController.KillCalls == 0 && !raid.Mode.IsComplete,
                "late arrival after native shutdown is left to native disposal without a mode failure");
            Check(raid.Match.Scoreboard.Count(ArenaScoreboardSide.Friendly, true) == rosterBefore
                && BurstSpawnService.Requests.Count == 2,
                "late shutdown callback cannot admit a bot or reopen settled work");
        }

        foreach (bool throwAfterStop in new[] { false, true })
        {
            var raid = new Raid();
            raid.Step(0);
            var bot = Deliver(Request(EPlayerSide.Usec), EBotState.ActiveFail);
            bot.HealthController.OnKilled = () =>
            {
                raid.Game.Status = GameStatus.Stopping;
                if (throwAfterStop) throw new InvalidOperationException("native stop during death callback");
            };
            raid.Step(1);
            Check(!raid.Mode.IsComplete && !Model.Events.Any(item => item.StartsWith("toast:"))
                && !ArenaMode.Plugin.ModLogger.Lines.Any(line => line.Contains("Team Deathmatch retired")
                    || line.Contains("Team Deathmatch stopped:")),
                "reentrant stop during retirement cannot become a false mode failure or warning");
            Check(BurstSpawnService.Requests.Count == 2 && !ArenaAdministrativeDeathGate.IsActive(bot.ProfileId),
                "reentrant exit prevents refill and releases the administrative scope");
        }

        var dispatchExit = new Raid();
        BurstSpawnService.OnRequest = request =>
        {
            dispatchExit.Game.Status = GameStatus.Stopping;
            request.DispatchFinished(false);
        };
        dispatchExit.Step(0);
        Check(BurstSpawnService.Requests.Count == 1 && !dispatchExit.Mode.IsComplete,
            "native stop in first faction dispatch prevents second faction refill");

        var readyExit = new Raid(size: 2);
        BurstSpawnService.OnRequest = request =>
        {
            Deliver(request, EBotState.Active);
            if (readyExit.Alive(EPlayerSide.Bear) == 2) readyExit.Game.Status = GameStatus.Stopping;
            request.DispatchFinished(true);
        };
        readyExit.Step(0);
        readyExit.Step(1);
        Check(readyExit.Match.Cues.Count == 0 && !readyExit.Mode.HudStatusLine.Contains("START IN"),
            "stop in last arrival prevents a round-start announcement during native shutdown");

        var suspended = new Raid();
        suspended.Game.Status = GameStatus.Starting;
        suspended.Step(0);
        Check(BurstSpawnService.Requests.Count == 0 && !suspended.Mode.IsComplete,
            "TDM work requires Started even before a terminal stop has been observed");

        var genuineFailure = new Raid();
        genuineFailure.Fill();
        foreach (var bot in genuineFailure.Bots.Where(bot => bot.Profile.Info.Side == EPlayerSide.Bear))
            bot.BotState = EBotState.NonActive;
        genuineFailure.Step(Time.time + 1);
        Check(genuineFailure.Mode.IsComplete && genuineFailure.Bots.Sum(bot => bot.HealthController.KillCalls) == 3
            && Model.Events.Count(item => item.StartsWith("toast:TEAM DM stopped:")) == 1,
            "three genuine NonActive failures while Started still retire bodies and report the stopped match");
    }

    private static void RuntimeDiagnostics()
    {
        var missing = new Raid();
        var storage = missing.Bots;
        missing.Game.BotsController.BotSpawner._bots.BotOwners = null;
        missing.Step(0);
        missing.Step(1);
        missing.Step(50);
        Check(ArenaMode.Plugin.ModLogger.Lines.Count(line => line.Contains("native roster unavailable")) == 1
            && BurstSpawnService.Requests.Count == 0,
            "unreadable roster is reported once while refills remain paused");
        missing.Game.BotsController.BotSpawner._bots.BotOwners = storage;
        missing.Step(51);
        missing.Step(52);
        Check(ArenaMode.Plugin.ModLogger.Lines.Count(line => line.Contains("native roster readable again")) == 1,
            "readable roster recovery is reported once");

        var stalled = new Raid();
        stalled.Step(0);
        var loading = Request(EPlayerSide.Usec);
        var dispatched = Request(EPlayerSide.Bear);
        dispatched.DispatchFinished(true);
        stalled.Step(45);
        var timeout = ArenaMode.Plugin.ModLogger.Lines.Single(line => line.Contains("spawn delayed;"));
        Check(timeout.Contains("status=Started, respawn=False")
            && timeout.Contains("USEC occupied=0/4") && timeout.Contains("BEAR occupied=0/5")
            && timeout.Contains("#1:preparing:45s:slow") && timeout.Contains("#2:native:45s:expired:prepared=0.0s")
            && timeout.Contains("#3:preparing:0s"),
            "timeout diagnostics distinguish faction capacity, profile loading and uncertain native work");
        stalled.Step(46);
        Check(ArenaMode.Plugin.ModLogger.Lines.Count(line => line.Contains("spawn delayed;")) == 1,
            "unchanged spawn stall does not log every tick");
        Check(BurstSpawnService.Requests.Count == 3 && Request(EPlayerSide.Bear).Current(),
            "diagnostic inspection neither opens a second profile load nor settles valid tickets");
        loading.DispatchFinished(false);
        dispatched.Rejected();
        Request(EPlayerSide.Bear).DispatchFinished(false);
        stalled.Step(47);
        stalled.Step(48);
        Check(ArenaMode.Plugin.ModLogger.Lines.Count(line => line.Contains("delayed spawn requests settled")) == 1
            && stalled.Match.Cues.Count == 0,
            "settled timeout debt is reported once without claiming combat readiness");

        var activation = new Raid(size: 2);
        activation.Step(0);
        Deliver(Request(EPlayerSide.Usec));
        activation.Step(45);
        string failure = ArenaMode.Plugin.ModLogger.Lines.Single(line => line.Contains("Team Deathmatch retired"));
        Check(failure.Contains("PreActive; faction=USEC, wait=45s, failure=1/3")
            && failure.Contains("status=Started") && failure.Contains("USEC occupied=0/1"),
            "activation failure describes the pre-retirement state, wait, faction and failure budget");
        activation.Step(50);
        var recovered = Deliver(Request(EPlayerSide.Usec), EBotState.Active);
        activation.Step(51);
        activation.Step(52);
        Check(ArenaMode.Plugin.ModLogger.Lines.Count(line => line.Contains("USEC activation recovered after 1")) == 1,
            "successful activation reports recovery once and resets the consecutive-failure budget");
        Check(ArenaMode.Plugin.ModLogger.Lines.Any(line => line.Contains("state=Active, prepared=0.0s, native arrival=0.0s")),
            "existing admission line includes request duration and actual arrival state");
        recovered.HealthController.IsAlive = false;
        activation.Step(53);
        Deliver(Request(EPlayerSide.Usec), EBotState.ActiveFail);
        activation.Step(54);
        Check(ArenaMode.Plugin.ModLogger.Lines.Last(line => line.Contains("Team Deathmatch retired")).Contains("failure=1/3"),
            "diagnostics preserve failure reset after a successfully activated replacement");
        Check(!ArenaMode.Plugin.ModLogger.Lines.Any(line => line.Contains(recovered.ProfileId)),
            "TDM diagnostics do not disclose profile identities");

        var exit = new Raid();
        exit.Step(0);
        exit.Game.Status = GameStatus.Stopping;
        exit.Step(1);
        var summary = ArenaMode.Plugin.ModLogger.Lines.Single(line => line.Contains("native raid shutdown"));
        Check(summary.Contains("status=Stopping") && summary.Contains("#1:preparing:1s")
            && summary.Contains("#2:preparing:1s"),
            "shutdown diagnostics capture pending work before cancellation clears it");

        var fault = new Raid();
        fault.Step(0);
        fault.Local.Profile.Info.Side = EPlayerSide.Bear;
        fault.Step(1);
        Check(ArenaMode.Plugin.ModLogger.Lines.Single(line => line.Contains("Team Deathmatch stopped:"))
            .Contains("#1:preparing:1s"), "genuine failure diagnostics capture pending work before stopping");
        Check(Model.Events.Single(item => item.StartsWith("toast:")).Equals("toast:TEAM DM stopped: local PMC faction changed"),
            "technical diagnostics stay in the log rather than expanding player error toasts");

        var damagedSnapshot = new Raid();
        damagedSnapshot.Fill();
        damagedSnapshot.Bots.First().GetPlayer.ProfileId = null; // unreadable identity during disposal
        damagedSnapshot.Game.Status = GameStatus.Stopping;
        bool exitThrew = false;
        try { damagedSnapshot.Step(Time.time + 1); }
        catch { exitThrew = true; }
        damagedSnapshot.Step(Time.time + 2);
        Check(!exitThrew && !damagedSnapshot.Mode.IsComplete
            && damagedSnapshot.Bots.All(bot => bot.HealthController.KillCalls == 0)
            && !Model.Events.Any(item => item.StartsWith("toast:")),
            "a broken diagnostic snapshot cannot interrupt native shutdown or fail the mode");
        Check(ArenaMode.Plugin.ModLogger.Lines.Count(line => line.Contains("native raid shutdown")
            && line.Contains("roster snapshot unavailable (ArgumentNullException)")) == 1,
            "unreadable shutdown snapshot emits one fallback diagnostic without repeated logging");
    }

    private static void NativeFriendshipAndDamagePolicy()
    {
        var raid = new Raid(size: 3);
        raid.Fill();
        var friend = raid.Bots.First(bot => bot.Profile.Info.Side == EPlayerSide.Usec);
        var enemy = raid.Bots.First(bot => bot.Profile.Info.Side == EPlayerSide.Bear);
        foreach (var cause in Enum.GetValues<EBotEnemyCause>())
        {
            Check(!friend.BotsGroup.AddEnemy(raid.Local, cause),
                "native friendly admission rejects initial, revenge and direct causes");
            Check(friend.BotsGroup.AddEnemy(enemy, cause), "native opposing faction admission stays available");
        }
        var fake = new BotGroupEnemyInfo { Player = raid.Local, _botGroup = friend.BotsGroup };
        friend.Memory.AddEnemy(raid.Local, fake, false);
        Check(!friend.EnemiesController.EnemyInfos.ContainsKey(raid.Local), "direct memory bypass cannot add a teammate");
        var friendlyInfo = new EnemyInfo { Person = raid.Local, GroupInfo = fake, Owner = friend, GroupOwner = friend.BotsGroup };
        friend.Memory.GoalEnemy = friendlyInfo;
        Check(friend.Memory.GoalEnemy == null, "goal setter rejects stale friendly enemy info");
        friend.BotsGroup.Enemies[raid.Local] = fake; // seed preexisting bad relation, bypassing normal admission
        friend.EnemiesController.EnemyInfos[raid.Local] = friendlyInfo;
        raid.Step(Time.time + 1);
        Check(!friend.BotsGroup.Enemies.ContainsKey(raid.Local)
            && !friend.EnemiesController.EnemyInfos.ContainsKey(raid.Local)
            && friend.BotsGroup.Allies.Contains(raid.Local), "reconciliation removes group and memory remnants before adding friendship");
        Check(TeamDeathmatchRelations.ShouldBlockFriendlyDamage(raid.Local, friend)
            && TeamDeathmatchRelations.ShouldBlockFriendlyDamage(friend.GetPlayer, raid.Local),
            "attributed friendly-fire policy works in both directions and with BotOwner wrapper");
        Check(!TeamDeathmatchRelations.ShouldBlockFriendlyDamage(raid.Local, enemy)
            && !TeamDeathmatchRelations.ShouldBlockFriendlyDamage(raid.Local, null),
            "opponent and unattributed damage fail open");
        friend.HealthController.IsAlive = false;
        raid.World.Players.Remove(friend.ProfileId);
        Check(TeamDeathmatchRelations.ShouldBlockFriendlyDamage(raid.Local, friend.GetPlayer),
            "dead admitted teammate retains projectile affiliation");
        Check(TeamDeathmatchRelations.AreOpponents(enemy, friend), "dead aggressor can still earn valid opposing-faction kill");
        var foreign = new Player { ProfileId = friend.ProfileId, IsAI = true };
        Check(TeamDeathmatchRelations.Faction(foreign) == null,
            "same profile id on an unknown physical body does not inherit faction");
        ArenaSession.IsActive = false;
        Check(!TeamDeathmatchRelations.ShouldBlockFriendlyDamage(raid.Local, friend),
            "ordinary raid gate overrides known old affiliation");
        Check(friend.BotsGroup.AddEnemy(raid.Local, EBotEnemyCause.byKill), "ordinary raid retains native hostility mutations");
    }

    private static ArenaKill Kill(string killer, string victim, bool local = false)
        => new() { KillerProfileId = killer, VictimProfileId = victim, VictimRole = ArenaRole.Pmc,
            KillerName = killer, VictimName = victim, KilledByLocal = local, AwardEligible = true,
            KillerBodyId = 1, VictimBodyId = 2, SingleHitHeadshot = true, Headshot = true };

    private static void ScoreAwardsTracersAndLoot()
    {
        var raid = new Raid(size: 3);
        raid.Fill();
        var friend = raid.Bots.First(bot => bot.Profile.Info.Side == EPlayerSide.Usec);
        var enemy = raid.Bots.First(bot => bot.Profile.Info.Side == EPlayerSide.Bear);
        var awards = new ArenaCombatAwards();
        var teamkill = Kill("local", friend.ProfileId, true);
        raid.Mode.ClassifyKill(teamkill);
        raid.Match.Scoreboard.ReportKill(teamkill);
        Check(!teamkill.ScoreEligible && !teamkill.AwardEligible
            && awards.ReportKill(teamkill, "KILLING SPREE") == null,
            "mode suppresses teamkill points and combat awards before downstream consumers");
        Check(raid.Match.Scoreboard.Local.Kills == 0 && raid.Match.Scoreboard.Local.Score == 0,
            "same-team kill cannot add points, headshot or streak bonuses");
        var opposing = Kill("local", enemy.ProfileId, true);
        raid.Mode.ClassifyKill(opposing);
        raid.Match.Scoreboard.ReportKill(opposing);
        Check(opposing.ScoreEligible && opposing.AwardEligible && awards.ReportKill(opposing, null) == "FIRST BLOOD",
            "suppressed friendly fire does not consume First Blood");
        Check(raid.Match.Scoreboard.Local.Kills == 1 && raid.Match.Scoreboard.Local.Score == 250,
            "opposing PMC headshot preserves production score rules");
        friend.HealthController.IsAlive = false;
        raid.World.Players.Remove(friend.ProfileId);
        raid.Step(Time.time + 1);
        var late = Kill(friend.ProfileId, enemy.ProfileId);
        raid.Mode.ClassifyKill(late);
        Check(late.ScoreEligible && late.KillerIsFriendly == true,
            "mode scalar affiliation survives removal of a dead aggressor's physical body");
        Check(ArenaTracerTeams.Classify(friend.ProfileId, true) == ArenaTracerSide.Friendly
            && ArenaTracerTeams.Classify(enemy.ProfileId, true) == ArenaTracerSide.Opponent
            && ArenaTracerTeams.Classify("foreign", true) == ArenaTracerSide.Unknown,
            "production tracer roster retains known dead shooter colors and leaves unknown shots native");
        var unknown = Kill("foreign", enemy.ProfileId);
        raid.Mode.ClassifyKill(unknown);
        Check(!unknown.ScoreEligible && !unknown.AwardEligible, "unknown shooters cannot gain TDM awards or score");
        var loot = new BotDeadBodyWork { _owner = friend };
        friend.BotsGroup.AddAlly(raid.Local);
        loot.UpdateCheck();
        Check(loot.NativeStarts == 0, "TDM friendly bots do not inherit commandable follower looting exception");
        loot.ShallUse = true;
        loot.UpdateCheck();
        Check(loot.NativeStarts == 1, "a body already under work is finished or left the vanilla way");
        loot.ShallUse = false;
        ArenaSession.ActiveMode = "tactical";
        loot.UpdateCheck();
        Check(loot.NativeStarts == 2, "ordinary allied Tactical follower looting exception remains");
        var hostile = new BotDeadBodyWork { _owner = enemy };
        hostile.UpdateCheck();
        Check(hostile.NativeStarts == 0, "an ordinary hostile bot never chooses a corpse in an arena raid");
        ArenaSession.ActiveMode = "teamdeathmatch";
        Settings.NoCorpseLooting.Value = false;
        loot.UpdateCheck();
        Check(loot.NativeStarts == 3, "disabled no-looting preference remains respected");
        raid.Mode.OnMatchEnd();
        var ended = Kill("local", enemy.ProfileId, true);
        raid.Mode.ClassifyKill(ended);
        Check(!ended.ScoreEligible && !ended.AwardEligible, "ended match does not score delayed callbacks");
    }

    private static void Main()
    {
        TeamDeathmatchRelations.Enable();
        new ArenaNoCorpseLootingPatch().Enable();
        InitializationAndReady();
        SelectedTeamRuntime();
        LoadingAndPreActiveOccupancy();
        HumanDeathRespawnAndAwareness();
        RejectionsAndRetirement();
        ActivationLifetime();
        ExactRaidAndLateCallbacks();
        NativeShutdownWindow();
        PreparationLifecycle();
        RuntimeDiagnostics();
        NativeFriendshipAndDamagePolicy();
        ScoreAwardsTracersAndLoot();
        BoundedAreaModeContract();
        MatchReadinessAndCountdown();
        MatchSpawnGateRelease();
        MatchDifficultyClock();
        MatchReadinessLossBetweenRosterTicks();
        MatchTerminalPopulationAndPresentation();
        MatchTimeLimitAndRuleSnapshot();
        BotSpawnShields();
        DominationLifecycle();
        Console.WriteLine($"PASS: {_checks} production TDM mode/relations runtime-model checks.");
        Console.WriteLine("ARENA_TDM_RUNTIME_CHECKS_PASSED");
    }
}
